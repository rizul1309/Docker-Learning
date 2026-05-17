using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<ResultOrchestratorWorker>();
builder.Services.AddHostedService<AuditLogWorker>();

var host = builder.Build();
host.Run();

// ============================================================
// RESULT ORCHESTRATOR WORKER
// ============================================================
// Consumes "job.completed" events from RabbitMQ.
//
// KEY DESIGN PATTERNS (mirrors your real system's revamp):
//
// 1. STRATEGY PATTERN — Export handlers are independent, pluggable strategies.
//    Each handler decides if it applies (AppliesTo) and handles independently.
//    Real system: IResultExportHandler → EmailExportHandler, IPortExportHandler
//
// 2. SINGLE-EXECUTION PROCESSING — Processes one event at a time, ACKs it,
//    then immediately picks up the next. No batch-fetch, no fixed sleep.
//    Real system: ProcessPendingResultAsync() returns bool (found work?)
//
// 3. EMAIL GROUPING — Doesn't send email immediately when one execution
//    completes. Waits for ALL executions in the same group (InvocationId +
//    GroupId) to reach terminal state. Sends one consolidated email.
//    Real system: EmailExportHandler.HandleAsync checks HasPendingExecutionsInGroupAsync
//
// 4. IDEMPOTENCY — Each handler checks if it already processed this execution.
//    Safe to retry. Safe to run twice. No duplicate emails, no duplicate iPort uploads.
//    Real system: checks EmailsSent dict and IPortPublishedAt timestamp
//
// 5. DYNAMIC DELAY — No fixed sleep between processing. Only sleeps when idle.
//    Real system: ResultOrchestratorWorker skips Task.Delay when processed=true
public class ResultOrchestratorWorker : BackgroundService
{
    private readonly ILogger<ResultOrchestratorWorker> _logger;

    // Simulated "group state" from the current event
    private bool _currentGroupComplete;
    private string _currentGroupProgress = "";

    public ResultOrchestratorWorker(ILogger<ResultOrchestratorWorker> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = await WaitForRabbitMQ(stoppingToken);

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync("result-orchestrator", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);

        _logger.LogInformation("[ResultOrchestrator] Listening for job.completed events...");
        _logger.LogInformation("[ResultOrchestrator] Using Strategy Pattern: EmailExportHandler + IPortExportHandler");

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            var evt = JsonSerializer.Deserialize<JsonElement>(body);

            var executionId = evt.GetProperty("executionId").GetString() ?? "unknown";
            var status = evt.GetProperty("status").GetString() ?? "unknown";
            var triggerId = evt.GetProperty("triggerId").GetString() ?? "unknown";

            // Extract group completion state from the event (set by the API)
            _currentGroupComplete = evt.TryGetProperty("groupComplete", out var gc) && gc.GetBoolean();
            _currentGroupProgress = evt.TryGetProperty("groupProgress", out var gp) ? gp.GetString() ?? "?" : "?";

            _logger.LogInformation(
                "[ResultOrchestrator] Processing execution {ExecutionId} (trigger {TriggerId}, status={Status}, group={GroupProgress}, complete={GroupComplete})",
                executionId, triggerId, status, _currentGroupProgress, _currentGroupComplete);

            // ---- STRATEGY PATTERN: Dispatch to applicable handlers ----
            var allSucceeded = true;

            // Handler 1: Email Export (with grouping logic)
            allSucceeded &= await HandleEmailExport(executionId, triggerId, status, stoppingToken);

            // Handler 2: iPort Export
            allSucceeded &= await HandleIPortExport(executionId, status, stoppingToken);

            // ---- Mark fully processed only if ALL handlers succeeded ----
            if (allSucceeded)
            {
                _logger.LogInformation("[ResultOrchestrator] ✓ Execution {ExecutionId} fully processed (all handlers succeeded)", executionId);
            }
            else
            {
                _logger.LogWarning("[ResultOrchestrator] ⚠ Execution {ExecutionId} had handler failures — would be retried in production", executionId);
            }

            // ACK the message — in production with failures, you might NACK for retry
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        };

        await channel.BasicConsumeAsync("result-orchestrator", autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(1000, stoppingToken);
    }

    /// <summary>
    /// Simulates EmailExportHandler with GROUP WAITING logic.
    /// The API includes "groupComplete: true/false" in the event payload.
    /// If groupComplete is false → DEFER (don't send email yet).
    /// If groupComplete is true → SEND consolidated email for the whole group.
    /// </summary>
    private async Task<bool> HandleEmailExport(string executionId, string triggerId, string status, CancellationToken ct)
    {
        _logger.LogInformation("[EmailExportHandler] Checking if email applies to execution {ExecutionId}", executionId);

        // Read group completion state from the event (set by the API)
        if (!_currentGroupComplete)
        {
            _logger.LogInformation(
                "[EmailExportHandler] ⏳ DEFERRING email for execution {ExecutionId} — group NOT complete ({GroupProgress}). Waiting for all executions to finish.",
                executionId, _currentGroupProgress);
            return true; // Deferred, not failed — email will be sent when last execution completes
        }

        // Group is complete — send consolidated email
        _logger.LogInformation("[EmailExportHandler] ✓ Group COMPLETE ({GroupProgress}). Building consolidated email for all executions in group...", _currentGroupProgress);
        await Task.Delay(300, ct); // Simulate email building

        if (status == "Success")
        {
            _logger.LogInformation("[EmailExportHandler] → Sending consolidated email to rizul1309@gmail.com (covers ALL executions in group)");
            await Task.Delay(500, ct); // Simulate SMTP send
            _logger.LogInformation("[EmailExportHandler] → ✓ Email sent successfully (idempotent: tracked in ExportStatus.EmailsSent)");
        }
        else
        {
            _logger.LogInformation("[EmailExportHandler] → Sending failure notification email");
            await Task.Delay(200, ct);
            _logger.LogInformation("[EmailExportHandler] → ✓ Failure alert sent");
        }

        return true;
    }

    /// <summary>
    /// Simulates IPortExportHandler.
    /// In the real system:
    ///   1. Check if iPort is enabled: if (!execution.Attributes.IportEnabled) return true;
    ///   2. Idempotency check: if (execution.ExportStatus.IPortPublishedAt.HasValue) return true;
    ///   3. Decrypt credentials, fetch CSV from S3, POST to iPort API
    ///   4. On success: update ExportStatus.IPortPublishedAt = DateTime.UtcNow
    /// </summary>
    private async Task<bool> HandleIPortExport(string executionId, string status, CancellationToken ct)
    {
        // Only publish successful results to iPort
        if (status != "Success")
        {
            _logger.LogInformation("[IPortExportHandler] Skipping iPort — execution {ExecutionId} was not successful", executionId);
            return true;
        }

        _logger.LogInformation("[IPortExportHandler] Publishing CSV to iPort for execution {ExecutionId}", executionId);
        await Task.Delay(400, ct); // Simulate iPort API call
        _logger.LogInformation("[IPortExportHandler] → ✓ Published to iPort (idempotent: tracked in ExportStatus.IPortPublishedAt)");
        return true;
    }

    private async Task<ConnectionFactory> WaitForRabbitMQ(CancellationToken ct)
    {
        var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq";
        var factory = new ConnectionFactory { HostName = host, UserName = "guest", Password = "guest" };

        for (int i = 0; i < 30; i++)
        {
            try
            {
                await using var conn = await factory.CreateConnectionAsync(ct);
                _logger.LogInformation("[ResultOrchestrator] RabbitMQ connected");
                return factory;
            }
            catch
            {
                _logger.LogWarning("[ResultOrchestrator] Waiting for RabbitMQ... ({Attempt}/30)", i + 1);
                await Task.Delay(2000, ct);
            }
        }
        throw new Exception("Could not connect to RabbitMQ");
    }
}

// ============================================================
// AUDIT LOG WORKER
// ============================================================
// Consumes ALL events (binding: #) and logs them.
// Demonstrates the topic exchange wildcard pattern.
public class AuditLogWorker : BackgroundService
{
    private readonly ILogger<AuditLogWorker> _logger;

    public AuditLogWorker(ILogger<AuditLogWorker> logger) => _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq";
        var factory = new ConnectionFactory { HostName = host, UserName = "guest", Password = "guest" };

        // Wait for RabbitMQ
        for (int i = 0; i < 30; i++)
        {
            try { await using var c = await factory.CreateConnectionAsync(stoppingToken); break; }
            catch { await Task.Delay(2000, stoppingToken); }
        }

        await using var connection = await factory.CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync("audit-log", durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await channel.BasicQosAsync(0, 5, false, stoppingToken);

        _logger.LogInformation("[AuditLog] Listening for ALL events (binding: #)...");

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var body = Encoding.UTF8.GetString(ea.Body.ToArray());
            _logger.LogInformation("[AuditLog] Event: routingKey={RoutingKey}, body={Body}",
                ea.RoutingKey, body);
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        };

        await channel.BasicConsumeAsync("audit-log", autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(1000, stoppingToken);
    }
}
