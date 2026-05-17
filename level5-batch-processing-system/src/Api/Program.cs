using Api.Data;
using Api.Entities;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ---- JSON serialization: enums as strings, camelCase for consistency ----
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
};

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ---- POSTGRESQL (Entity Framework Core) ----
var connectionString = builder.Configuration.GetConnectionString("BatchDb")
    ?? "Host=db;Database=batchprocessing;Username=batchuser;Password=batchpass";
builder.Services.AddDbContext<BatchDbContext>(options =>
    options.UseNpgsql(connectionString));

// ---- REDIS ----
var redisConn = builder.Configuration.GetValue<string>("Redis:ConnectionString") ?? "redis:6379";
var redis = ConnectionMultiplexer.Connect(redisConn);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// ---- RABBITMQ ----
var rabbitHost = builder.Configuration.GetValue<string>("RabbitMQ:Host") ?? "rabbitmq";
var rabbitFactory = new ConnectionFactory { HostName = rabbitHost, UserName = "guest", Password = "guest" };
builder.Services.AddSingleton(rabbitFactory);

// ---- CORS (for the React UI) ----
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ---- HEALTH CHECKS ----
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql")
    .AddRedis(redisConn, name: "redis");

var app = builder.Build();
app.UseCors();

// ---- AUTO-MIGRATE DATABASE ON STARTUP ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();
    db.Database.EnsureCreated();
}

// ---- DECLARE RABBITMQ TOPOLOGY ON STARTUP ----
await DeclareRabbitMqTopology(rabbitFactory);

// ============================================================
// HEALTH
// ============================================================
app.MapHealthChecks("/health");

app.MapGet("/", () => new
{
    service = "Batch Processing Service (Level 5)",
    framework = ".NET 9",
    features = new[] { "PostgreSQL", "Redis Cache", "RabbitMQ Events", "Background Workers" }
});

// ============================================================
// REPORTS — Read-only reference data
// ============================================================
app.MapGet("/api/v1/reports", async (BatchDbContext db) =>
{
    var reports = await db.Reports.ToListAsync();
    return Results.Ok(reports);
});

// ============================================================
// DATA TRIGGER DEFINITIONS — CRUD with Redis caching
// ============================================================

// GET all triggers (with Redis cache)
app.MapGet("/api/v1/triggers", async (BatchDbContext db, IConnectionMultiplexer mux) =>
{
    var cache = mux.GetDatabase();
    const string cacheKey = "triggers:all";

    // 1. Check Redis cache first
    var cached = await cache.StringGetAsync(cacheKey);
    if (!cached.IsNullOrEmpty)
    {
        return Results.Json(
            JsonSerializer.Deserialize<object>(cached!),
            statusCode: 200);
    }

    // 2. Cache miss — query PostgreSQL
    var triggers = await db.DataTriggerDefinitions
        .Include(t => t.Report)
        .OrderBy(t => t.Priority)
        .Select(t => new
        {
            t.DataTriggerId,
            t.TriggerName,
            t.PanelId,
            t.IsActive,
            t.OutputFormat,
            t.Frequency,
            t.EmailEnabled,
            t.EmailTo,
            t.Priority,
            t.DateCreated,
            t.DateLastModified,
            ReportName = t.Report.DisplayName,
            ReportId = t.Report.ReportId
        })
        .ToListAsync();

    // 3. Store in Redis for 2 minutes
    var json = JsonSerializer.Serialize(triggers, jsonOptions);
    await cache.StringSetAsync(cacheKey, json, TimeSpan.FromMinutes(2));

    return Results.Ok(triggers);
});

// GET single trigger
app.MapGet("/api/v1/triggers/{id}", async (Guid id, BatchDbContext db, IConnectionMultiplexer mux) =>
{
    var cache = mux.GetDatabase();
    var cacheKey = $"trigger:{id}";

    var cached = await cache.StringGetAsync(cacheKey);
    if (!cached.IsNullOrEmpty)
    {
        return Results.Json(JsonSerializer.Deserialize<object>(cached!), statusCode: 200);
    }

    var trigger = await db.DataTriggerDefinitions
        .Include(t => t.Report)
        .Where(t => t.DataTriggerId == id)
        .Select(t => new
        {
            t.DataTriggerId,
            t.TriggerName,
            t.PanelId,
            t.IsActive,
            t.OutputFormat,
            t.Frequency,
            t.EmailEnabled,
            t.EmailTo,
            t.Priority,
            t.DateCreated,
            t.DateLastModified,
            ReportName = t.Report.DisplayName
        })
        .FirstOrDefaultAsync();

    if (trigger == null) return Results.NotFound();

    await cache.StringSetAsync(cacheKey, JsonSerializer.Serialize(trigger, jsonOptions), TimeSpan.FromMinutes(5));
    return Results.Ok(trigger);
});

// CREATE trigger
app.MapPost("/api/v1/triggers", async (CreateTriggerRequest req, BatchDbContext db, IConnectionMultiplexer mux) =>
{
    var report = await db.Reports.FindAsync(req.ReportId);
    if (report == null) return Results.BadRequest(new { error = "Report not found" });

    var trigger = new DataTriggerDefinition
    {
        DataTriggerId = Guid.NewGuid(),
        ReportId = req.ReportId,
        TriggerName = req.TriggerName,
        PanelId = report.PanelId ?? "AU",
        IsActive = req.IsActive,
        OutputFormat = req.OutputFormat ?? "CSV",
        Frequency = req.Frequency ?? "daily",
        EmailEnabled = req.EmailEnabled,
        EmailTo = req.EmailTo,
        Priority = req.Priority ?? 1000,
        DateCreated = DateTime.UtcNow,
        DateLastModified = DateTime.UtcNow
    };

    db.DataTriggerDefinitions.Add(trigger);
    await db.SaveChangesAsync();

    // Invalidate the triggers list cache
    await mux.GetDatabase().KeyDeleteAsync("triggers:all");

    return Results.Created($"/api/v1/triggers/{trigger.DataTriggerId}",
        new { trigger.DataTriggerId, trigger.TriggerName });
});

// UPDATE trigger
app.MapPut("/api/v1/triggers/{id}", async (Guid id, UpdateTriggerRequest req, BatchDbContext db, IConnectionMultiplexer mux) =>
{
    var trigger = await db.DataTriggerDefinitions.FindAsync(id);
    if (trigger == null) return Results.NotFound();

    if (req.TriggerName != null) trigger.TriggerName = req.TriggerName;
    if (req.IsActive.HasValue) trigger.IsActive = req.IsActive.Value;
    if (req.OutputFormat != null) trigger.OutputFormat = req.OutputFormat;
    if (req.Frequency != null) trigger.Frequency = req.Frequency;
    if (req.EmailEnabled.HasValue) trigger.EmailEnabled = req.EmailEnabled.Value;
    if (req.EmailTo != null) trigger.EmailTo = req.EmailTo;
    if (req.Priority.HasValue) trigger.Priority = req.Priority.Value;
    trigger.DateLastModified = DateTime.UtcNow;

    await db.SaveChangesAsync();

    // Invalidate caches
    var cache = mux.GetDatabase();
    await cache.KeyDeleteAsync("triggers:all");
    await cache.KeyDeleteAsync($"trigger:{id}");

    return Results.Ok(new { updated = true });
});

// DELETE trigger (soft delete)
app.MapDelete("/api/v1/triggers/{id}", async (Guid id, BatchDbContext db, IConnectionMultiplexer mux) =>
{
    var trigger = await db.DataTriggerDefinitions.FindAsync(id);
    if (trigger == null) return Results.NotFound();

    trigger.DateDeleted = DateTime.UtcNow;
    await db.SaveChangesAsync();

    var cache = mux.GetDatabase();
    await cache.KeyDeleteAsync("triggers:all");
    await cache.KeyDeleteAsync($"trigger:{id}");

    return Results.Ok(new { deleted = true });
});

// ============================================================
// RUN TRIGGER — The core pipeline entry point
// ============================================================
// This is the ad-hoc "run now" button. In production, the DatasetMonitorWorker
// does this automatically when new data arrives.
app.MapPost("/api/v1/triggers/{id}/run", async (Guid id, BatchDbContext db, ConnectionFactory factory, IConnectionMultiplexer mux) =>
{
    var trigger = await db.DataTriggerDefinitions
        .Include(t => t.Report)
        .FirstOrDefaultAsync(t => t.DataTriggerId == id);
    if (trigger == null) return Results.NotFound();

    // 1. Create a dataset (simulating new data arrival)
    var dataset = new Dataset
    {
        DatasetId = Guid.NewGuid(),
        PanelId = trigger.PanelId,
        ExternalDatasetId = $"manual-run-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
        DateCreated = DateTime.UtcNow
    };
    db.Datasets.Add(dataset);

    // 2. Create an execution record (Pending)
    var execution = new DataTriggerExecution
    {
        DataTriggerExecutionId = Guid.NewGuid(),
        InvocationId = Guid.NewGuid(),
        DataTriggerId = trigger.DataTriggerId,
        DatasetId = dataset.DatasetId,
        ResultStatus = ResultStatus.Pending,
        DateCreated = DateTime.UtcNow
    };
    db.DataTriggerExecutions.Add(execution);

    // 3. Create a job queue entry
    var job = new JobQueue
    {
        DataTriggerExecutionId = execution.DataTriggerExecutionId,
        PanelId = trigger.PanelId,
        DateCreated = DateTime.UtcNow
    };
    db.JobQueues.Add(job);
    await db.SaveChangesAsync();

    // 4. Publish "execution.created" event to RabbitMQ
    await PublishEvent(factory, "batch.events", "execution.created", new
    {
        executionId = execution.DataTriggerExecutionId,
        triggerId = trigger.DataTriggerId,
        triggerName = trigger.TriggerName,
        reportName = trigger.Report?.DisplayName,
        panelId = trigger.PanelId,
        datasetId = dataset.DatasetId,
        createdAt = DateTime.UtcNow
    });

    // 5. Invalidate stats cache
    await mux.GetDatabase().KeyDeleteAsync("jobs:statistics");

    return Results.Accepted(value: new
    {
        executionId = execution.DataTriggerExecutionId,
        jobId = job.JobQueueId,
        status = "Pending",
        message = "Trigger fired. Execution created and job queued."
    });
});

// ============================================================
// BATCH RUN — Run multiple triggers with shared InvocationId
// ============================================================
// This is the "Run (N selected)" button in the UI.
// All triggers in the batch share one InvocationId — this is critical
// for the email grouping logic: grouped triggers get ONE consolidated email.
app.MapPost("/api/v1/triggers/run-batch", async (RunBatchRequest req, BatchDbContext db, ConnectionFactory factory, IConnectionMultiplexer mux) =>
{
    if (req.TriggerIds == null || req.TriggerIds.Count == 0)
        return Results.BadRequest(new { error = "TriggerIds must not be empty" });

    var invocationId = Guid.NewGuid();
    var dataset = new Dataset
    {
        DatasetId = Guid.NewGuid(),
        PanelId = "AU",
        ExternalDatasetId = $"batch-run-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
        DateCreated = DateTime.UtcNow
    };
    db.Datasets.Add(dataset);

    var createdExecutions = new List<object>();
    var failedTriggerIds = new List<Guid>();

    foreach (var triggerId in req.TriggerIds)
    {
        var trigger = await db.DataTriggerDefinitions
            .Include(t => t.Report)
            .FirstOrDefaultAsync(t => t.DataTriggerId == triggerId);

        if (trigger == null)
        {
            failedTriggerIds.Add(triggerId);
            continue;
        }

        var execution = new DataTriggerExecution
        {
            DataTriggerExecutionId = Guid.NewGuid(),
            InvocationId = invocationId, // SHARED — this is the grouping key
            DataTriggerId = trigger.DataTriggerId,
            DatasetId = dataset.DatasetId,
            ResultStatus = ResultStatus.Pending,
            DateCreated = DateTime.UtcNow
        };
        db.DataTriggerExecutions.Add(execution);

        var job = new JobQueue
        {
            DataTriggerExecutionId = execution.DataTriggerExecutionId,
            PanelId = trigger.PanelId,
            DateCreated = DateTime.UtcNow
        };
        db.JobQueues.Add(job);

        createdExecutions.Add(new
        {
            executionId = execution.DataTriggerExecutionId,
            triggerId = trigger.DataTriggerId,
            triggerName = trigger.TriggerName
        });
    }

    await db.SaveChangesAsync();

    // Publish events for each execution
    foreach (var exec in createdExecutions)
    {
        await PublishEvent(factory, "batch.events", "execution.created", exec);
    }

    await mux.GetDatabase().KeyDeleteAsync("jobs:statistics");

    return Results.Accepted(value: new
    {
        invocationId,
        executionsCreated = createdExecutions.Count,
        failedTriggerIds,
        message = $"Batch run started. {createdExecutions.Count} executions share InvocationId {invocationId} for grouped email delivery."
    });
});

// ============================================================
// EXECUTIONS — History of trigger runs
// ============================================================
app.MapGet("/api/v1/triggers/{triggerId}/executions", async (Guid triggerId, BatchDbContext db) =>
{
    var executions = await db.DataTriggerExecutions
        .Where(e => e.DataTriggerId == triggerId)
        .OrderByDescending(e => e.DateCreated)
        .Take(50)
        .Select(e => new
        {
            e.DataTriggerExecutionId,
            e.InvocationId,
            Status = e.ResultStatus.ToString(),
            e.DateCreated,
            e.DateExecutionStarted,
            e.DateExecutionCompleted,
            e.ExecutionTimeSeconds,
            e.ResultPath,
            e.EmailSent
        })
        .ToListAsync();

    return Results.Ok(executions);
});

// ============================================================
// JOBS — Interface for report runners
// ============================================================

// GET next job (dequeue)
app.MapGet("/api/v1/jobs/next-job", async (BatchDbContext db, IConnectionMultiplexer mux, ConnectionFactory factory) =>
{
    // Dequeue: find the oldest pending job and lock it
    var job = await db.JobQueues
        .Where(j => j.DateExecutionStarted == null)
        .OrderBy(j => j.JobQueueId)
        .FirstOrDefaultAsync();

    if (job == null) return Results.NoContent();

    // Mark as started
    job.DateExecutionStarted = DateTime.UtcNow;
    var execution = await db.DataTriggerExecutions.FindAsync(job.DataTriggerExecutionId);
    if (execution != null) execution.DateExecutionStarted = DateTime.UtcNow;
    await db.SaveChangesAsync();

    // Load report metadata (with Redis cache)
    var trigger = await db.DataTriggerDefinitions
        .Include(t => t.Report)
        .FirstOrDefaultAsync(t => t.Executions.Any(e => e.DataTriggerExecutionId == job.DataTriggerExecutionId));

    // Publish "job.started" event
    await PublishEvent(factory, "batch.events", "job.started", new
    {
        jobId = job.JobQueueId,
        executionId = job.DataTriggerExecutionId,
        panelId = job.PanelId,
        startedAt = DateTime.UtcNow
    });

    await mux.GetDatabase().KeyDeleteAsync("jobs:statistics");

    return Results.Ok(new
    {
        jobId = job.JobQueueId,
        executionId = job.DataTriggerExecutionId,
        reportName = trigger?.Report?.InternalName ?? "unknown",
        reportDisplayName = trigger?.Report?.DisplayName ?? "Unknown Report",
        outputFormat = trigger?.OutputFormat ?? "CSV",
        averageTimeoutSeconds = trigger?.Report?.AverageExecutionTimeSeconds ?? -1
    });
});

// POST complete job
app.MapPost("/api/v1/jobs/{jobId}/complete", async (long jobId, CompleteJobRequest req, BatchDbContext db, ConnectionFactory factory, IConnectionMultiplexer mux) =>
{
    var job = await db.JobQueues.FirstOrDefaultAsync(j => j.JobQueueId == jobId);
    if (job == null) return Results.NotFound(new { error = "Job not found" });

    var execution = await db.DataTriggerExecutions.FindAsync(job.DataTriggerExecutionId);
    if (execution == null) return Results.NotFound(new { error = "Execution not found" });

    // Update execution with results
    execution.ResultStatus = req.Success ? ResultStatus.Success : ResultStatus.Failure;
    execution.DateExecutionCompleted = DateTime.UtcNow;
    execution.ExecutionTimeSeconds = req.ExecutionTimeSeconds;
    execution.ResultPath = req.ResultPath;
    execution.LogPath = req.LogPath;

    // Remove job from queue (it's done)
    db.JobQueues.Remove(job);
    await db.SaveChangesAsync();

    // ---- GROUP COMPLETION CHECK ----
    // Check if ALL executions in the same InvocationId have reached terminal state.
    // This is the key logic that enables grouped email delivery.
    var invocationId = execution.InvocationId;
    var totalInGroup = await db.DataTriggerExecutions
        .CountAsync(e => e.InvocationId == invocationId);
    var completedInGroup = await db.DataTriggerExecutions
        .CountAsync(e => e.InvocationId == invocationId && e.ResultStatus != ResultStatus.Pending);
    var groupComplete = completedInGroup >= totalInGroup;

    // Publish "job.completed" event to RabbitMQ WITH group state
    await PublishEvent(factory, "batch.events", "job.completed", new
    {
        jobId,
        executionId = execution.DataTriggerExecutionId,
        triggerId = execution.DataTriggerId,
        invocationId,
        status = execution.ResultStatus.ToString(),
        completedAt = DateTime.UtcNow,
        executionTimeSeconds = req.ExecutionTimeSeconds,
        // GROUP INFO — worker uses this to decide whether to send email now or defer
        groupComplete,
        groupProgress = $"{completedInGroup}/{totalInGroup}"
    });

    await mux.GetDatabase().KeyDeleteAsync("jobs:statistics");

    return Results.Ok(new
    {
        executionId = execution.DataTriggerExecutionId,
        status = execution.ResultStatus.ToString(),
        groupComplete,
        groupProgress = $"{completedInGroup}/{totalInGroup}",
        message = groupComplete
            ? "Job completed. Group is COMPLETE — email will be sent."
            : $"Job completed. Group NOT complete yet ({completedInGroup}/{totalInGroup}) — email DEFERRED."
    });
});

// GET queue statistics (with Redis cache)
app.MapGet("/api/v1/jobs/statistics", async (BatchDbContext db, IConnectionMultiplexer mux) =>
{
    var cache = mux.GetDatabase();
    const string cacheKey = "jobs:statistics";

    var cached = await cache.StringGetAsync(cacheKey);
    if (!cached.IsNullOrEmpty)
    {
        return Results.Json(JsonSerializer.Deserialize<object>(cached!), statusCode: 200);
    }

    var totalQueued = await db.JobQueues.CountAsync();
    var executing = await db.JobQueues.CountAsync(j => j.DateExecutionStarted != null);
    var waiting = totalQueued - executing;

    var recentExecutions = await db.DataTriggerExecutions
        .OrderByDescending(e => e.DateCreated)
        .Take(5)
        .Select(e => new
        {
            e.DataTriggerExecutionId,
            Status = e.ResultStatus.ToString(),
            e.DateCreated,
            e.DateExecutionCompleted
        })
        .ToListAsync();

    var stats = new
    {
        totalQueued,
        currentlyExecuting = executing,
        waitingForRunner = waiting,
        recentExecutions
    };

    await cache.StringSetAsync(cacheKey, JsonSerializer.Serialize(stats, jsonOptions), TimeSpan.FromSeconds(10));
    return Results.Ok(stats);
});

// ============================================================
// GROUP INSPECTION — See group state for an InvocationId
// ============================================================
app.MapGet("/api/v1/executions/group/{invocationId}", async (Guid invocationId, BatchDbContext db) =>
{
    var executions = await db.DataTriggerExecutions
        .Where(e => e.InvocationId == invocationId)
        .Include(e => e.DataTriggerDefinition)
        .OrderBy(e => e.DateCreated)
        .Select(e => new
        {
            e.DataTriggerExecutionId,
            triggerName = e.DataTriggerDefinition.TriggerName,
            status = e.ResultStatus.ToString(),
            e.DateCreated,
            e.DateExecutionStarted,
            e.DateExecutionCompleted,
            e.EmailSent
        })
        .ToListAsync();

    if (executions.Count == 0)
        return Results.NotFound(new { error = "No executions found for this InvocationId" });

    var total = executions.Count;
    var completed = executions.Count(e => e.status != "Pending");
    var groupComplete = completed >= total;

    return Results.Ok(new
    {
        invocationId,
        totalInGroup = total,
        completedInGroup = completed,
        groupComplete,
        message = groupComplete
            ? "✓ Group COMPLETE — consolidated email would be sent"
            : $"⏳ Group PENDING — {completed}/{total} done, email DEFERRED until all complete",
        executions
    });
});

// ============================================================
// REDIS CACHE INSPECTION (for learning/debugging)
// ============================================================
app.MapGet("/api/v1/cache/keys", async (IConnectionMultiplexer mux) =>
{
    var server = mux.GetServers().First();
    var keys = server.Keys(pattern: "*").Select(k => k.ToString()).ToList();
    var db = mux.GetDatabase();

    var details = new List<object>();
    foreach (var key in keys)
    {
        var ttl = await db.KeyTimeToLiveAsync(key);
        var type = await db.KeyTypeAsync(key);
        details.Add(new { key, type = type.ToString(), ttlSeconds = ttl?.TotalSeconds ?? -1 });
    }

    return Results.Ok(new { totalKeys = keys.Count, keys = details });
});

app.MapDelete("/api/v1/cache/flush", async (IConnectionMultiplexer mux) =>
{
    var server = mux.GetServers().First();
    await server.FlushDatabaseAsync();
    return Results.Ok(new { message = "All Redis keys flushed" });
});

// ============================================================
// RABBITMQ INSPECTION (for learning/debugging)
// ============================================================
app.MapGet("/api/v1/events/topology", () => Results.Ok(new
{
    exchange = "batch.events",
    type = "topic",
    queues = new[]
    {
        new { name = "execution-pipeline", bindingPattern = "execution.created", purpose = "Worker creates jobs from new executions" },
        new { name = "result-orchestrator", bindingPattern = "job.completed", purpose = "Sends emails/notifications when jobs finish" },
        new { name = "audit-log", bindingPattern = "#", purpose = "Logs ALL events for debugging" }
    }
}));

app.Run();

// ============================================================
// HELPER: Publish event to RabbitMQ
// ============================================================
static async Task PublishEvent(ConnectionFactory factory, string exchange, string routingKey, object payload)
{
    await using var connection = await factory.CreateConnectionAsync();
    await using var channel = await connection.CreateChannelAsync();

    var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, opts));
    await channel.BasicPublishAsync(exchange: exchange, routingKey: routingKey, body: body);
}

// ============================================================
// HELPER: Declare RabbitMQ exchanges and queues on startup
// ============================================================
static async Task DeclareRabbitMqTopology(ConnectionFactory factory)
{
    for (int i = 0; i < 30; i++)
    {
        try
        {
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();

            // Topic exchange: routes events by pattern
            await channel.ExchangeDeclareAsync("batch.events", ExchangeType.Topic, durable: true);

            // Queue: execution pipeline (consumes execution.created events)
            await channel.QueueDeclareAsync("execution-pipeline", durable: true, exclusive: false, autoDelete: false);
            await channel.QueueBindAsync("execution-pipeline", "batch.events", routingKey: "execution.created");

            // Queue: result orchestrator (consumes job.completed events)
            await channel.QueueDeclareAsync("result-orchestrator", durable: true, exclusive: false, autoDelete: false);
            await channel.QueueBindAsync("result-orchestrator", "batch.events", routingKey: "job.completed");

            // Queue: audit log (consumes ALL events)
            await channel.QueueDeclareAsync("audit-log", durable: true, exclusive: false, autoDelete: false);
            await channel.QueueBindAsync("audit-log", "batch.events", routingKey: "#");

            return;
        }
        catch
        {
            await Task.Delay(2000);
        }
    }
}

// ============================================================
// REQUEST DTOs
// ============================================================
record CreateTriggerRequest(
    Guid ReportId,
    string TriggerName,
    bool IsActive = true,
    string? OutputFormat = "CSV",
    string? Frequency = "daily",
    bool EmailEnabled = false,
    string? EmailTo = null,
    int? Priority = 1000);

record UpdateTriggerRequest(
    string? TriggerName = null,
    bool? IsActive = null,
    string? OutputFormat = null,
    string? Frequency = null,
    bool? EmailEnabled = null,
    string? EmailTo = null,
    int? Priority = null);

record CompleteJobRequest(
    bool Success,
    int? ExecutionTimeSeconds = null,
    string? ResultPath = null,
    string? LogPath = null);

record RunBatchRequest(List<Guid> TriggerIds);
