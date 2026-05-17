// ============================================================
// BACKGROUND WORKER — RabbitMQ Consumer
// ============================================================
// This service runs in its own container and does ONE thing:
// consume messages from RabbitMQ's "demo-queue" and process them.
//
// WHY A SEPARATE SERVICE?
//   Your API should respond fast (< 200ms). If a request triggers
//   something slow (send email, generate PDF, process data), you
//   DON'T do it in the API. Instead:
//     1. API publishes a message to RabbitMQ ("please send this email")
//     2. API returns 202 Accepted immediately (fast!)
//     3. This worker picks up the message and does the slow work
//
// This is the PRODUCER-CONSUMER pattern. It's everywhere in production:
//   - Order placed → API returns fast → Worker processes payment
//   - Image uploaded → API returns fast → Worker generates thumbnails
//   - Report requested → API returns fast → Worker generates PDF
// ============================================================

using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<QueueConsumerWorker>();

var host = builder.Build();
host.Run();

// ---- The actual worker that consumes messages ----
public class QueueConsumerWorker : BackgroundService
{
    private readonly ILogger<QueueConsumerWorker> _logger;
    private IConnection? _connection;
    private IChannel? _channel;

    public QueueConsumerWorker(ILogger<QueueConsumerWorker> logger)
    {
        _logger = logger;
    }

    // ExecuteAsync runs when the service starts and keeps running
    // until the service is stopped (graceful shutdown via stoppingToken).
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for RabbitMQ to be ready (it takes a few seconds to start)
        await WaitForRabbitMQ(stoppingToken);

        var factory = new ConnectionFactory
        {
            HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq",
            UserName = "guest",
            Password = "guest"
        };

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declare the queue (idempotent — safe to call even if it already exists)
        await _channel.QueueDeclareAsync(
            queue: "demo-queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken
        );

        // BasicQos = Quality of Service.
        // prefetchCount: 1 means "give me ONE message at a time."
        // The worker must ACK (acknowledge) the current message before
        // RabbitMQ sends the next one.
        //
        // WHY? If you have 3 workers and 100 messages:
        //   Without QoS: RabbitMQ dumps 33 messages to each worker upfront.
        //                If Worker 1 is slow, its 33 messages are stuck.
        //   With QoS(1): Each worker gets 1 message, processes it, ACKs it,
        //                then gets the next one. Fast workers get more messages.
        //                This is FAIR DISPATCH.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        _logger.LogInformation("Worker started. Waiting for messages on 'demo-queue'...");

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            _logger.LogInformation("Received message: {Message}", message);

            try
            {
                // ---- PROCESS THE MESSAGE ----
                // In real life, this is where you'd:
                //   - Send an email
                //   - Generate a PDF
                //   - Call a third-party API
                //   - Update a database
                //   - Trigger a data pipeline
                await Task.Delay(1000, stoppingToken); // Simulate 1 second of work

                _logger.LogInformation("Successfully processed message: {Message}", message);

                // ACK = "I'm done with this message, you can remove it from the queue."
                // If we crash BEFORE this ACK, RabbitMQ will re-deliver the message
                // to another worker. This is AT-LEAST-ONCE delivery.
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message: {Message}", message);

                // NACK = "I couldn't process this message."
                // requeue: true = put it back in the queue for retry.
                // requeue: false = discard it (or send to dead-letter queue if configured).
                //
                // In production, you'd typically:
                //   1. Retry a few times (with exponential backoff)
                //   2. After N failures, send to a DEAD-LETTER QUEUE for manual inspection
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        // Start consuming. autoAck: false means WE control when messages are acknowledged.
        // If autoAck were true, RabbitMQ removes the message as soon as it's delivered,
        // even if our processing crashes. That means lost messages. Never use autoAck in production.
        await _channel.BasicConsumeAsync(
            queue: "demo-queue",
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken
        );

        // Keep the service running until shutdown is requested
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    // Wait for RabbitMQ to be ready (it can take 10-20 seconds to start)
    private async Task WaitForRabbitMQ(CancellationToken stoppingToken)
    {
        var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "rabbitmq";
        var factory = new ConnectionFactory { HostName = host, UserName = "guest", Password = "guest" };

        for (int i = 0; i < 30; i++)
        {
            try
            {
                using var conn = await factory.CreateConnectionAsync(stoppingToken);
                _logger.LogInformation("RabbitMQ is ready!");
                return;
            }
            catch
            {
                _logger.LogWarning("RabbitMQ not ready yet. Retrying in 2 seconds... (attempt {Attempt}/30)", i + 1);
                await Task.Delay(2000, stoppingToken);
            }
        }

        throw new Exception("Could not connect to RabbitMQ after 30 attempts");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Worker shutting down gracefully...");

        if (_channel is not null)
            await _channel.CloseAsync(cancellationToken);
        if (_connection is not null)
            await _connection.CloseAsync(cancellationToken);

        await base.StopAsync(cancellationToken);
    }
}
