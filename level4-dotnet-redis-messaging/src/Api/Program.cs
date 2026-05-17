// ============================================================
// .NET 8 MINIMAL API — Entry Point
// ============================================================
// This is the equivalent of Flask's app.py but in C# / .NET.
//
// .NET "Minimal APIs" (introduced in .NET 6) let you write
// APIs without the ceremony of controllers, startup classes, etc.
// Think of it as ".NET's answer to Flask/Express."
//
// KEY DIFFERENCE FROM PYTHON:
//   Python (Flask):  Interpreted, dynamically typed, GIL limits concurrency
//   C# (.NET):       Compiled (JIT), statically typed, true multi-threading
//
// .NET is what most enterprise/corporate backends run on.
// If you've only used Python/Node, this is your bridge into that world.
// ============================================================

using StackExchange.Redis;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ---- REDIS CONNECTION ----
// StackExchange.Redis is the standard .NET Redis client.
// ConnectionMultiplexer is THREAD-SAFE and should be a SINGLETON.
// Creating one per request is a common mistake — it's expensive.
//
// "redis:6379" — "redis" is the Docker service name (same as Flask's "db" trick).
// Docker DNS resolves "redis" to the Redis container's IP.
var redisConnectionString = builder.Configuration.GetValue<string>("Redis__ConnectionString") ?? "redis:6379";
var redis = ConnectionMultiplexer.Connect(redisConnectionString);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// ---- RABBITMQ CONNECTION ----
// RabbitMQ is a MESSAGE BROKER — it sits between services and passes messages.
// Think of it as a post office: Service A drops off a letter, RabbitMQ delivers
// it to Service B. Service A doesn't need to know where B lives or wait for it.
var rabbitHost = builder.Configuration.GetValue<string>("RabbitMQ__Host") ?? "rabbitmq";
var factory = new ConnectionFactory
{
    HostName = rabbitHost,
    UserName = "guest",
    Password = "guest"
};

// Register the RabbitMQ connection factory as a singleton
builder.Services.AddSingleton(factory);

// ---- HEALTH CHECKS ----
// .NET has built-in health check infrastructure.
// AddHealthChecks() registers the health check service.
// AddRedis/AddRabbitMQ add checks for those specific dependencies.
builder.Services.AddHealthChecks()
    .AddRedis(redisConnectionString, name: "redis")
    .AddRabbitMQ(sp => factory.CreateConnectionAsync(), name: "rabbitmq");

var app = builder.Build();

// ============================================================
// ENDPOINTS (Routes)
// ============================================================
// In Flask:   @app.route("/")
// In .NET:    app.MapGet("/", ...)
//
// The lambda (=>) is like Python's lambda but more powerful.
// ============================================================

// ---- HOME ----
app.MapGet("/", () => new
{
    message = "Level 4 — .NET + Redis + RabbitMQ",
    framework = ".NET 8",
    language = "C#",
    timestamp = DateTime.UtcNow
});

// ---- HEALTH CHECK ----
// Maps to /health — same concept as Flask's /health endpoint.
// The built-in health check system pings Redis and RabbitMQ automatically.
app.MapHealthChecks("/health");

// ============================================================
// REDIS ENDPOINTS — Caching Deep Dive
// ============================================================
// Redis is an IN-MEMORY data store. It's blazing fast because
// data lives in RAM, not on disk.
//
// Common uses:
//   1. CACHING — store expensive query results (most common)
//   2. SESSION STORE — user sessions across multiple app instances
//   3. RATE LIMITING — track request counts per IP/user
//   4. PUB/SUB — real-time messaging between services
//   5. DISTRIBUTED LOCKS — prevent race conditions across instances
//
// Redis data types:
//   STRING — simple key-value (most used)
//   HASH   — like a Python dict / C# Dictionary
//   LIST   — ordered list (queue/stack)
//   SET    — unique unordered collection
//   SORTED SET — unique collection with scores (leaderboards)
// ============================================================

// ---- SET a value in Redis ----
// POST /cache/set?key=user:123&value=John
app.MapPost("/cache/set", async (string key, string value, IConnectionMultiplexer mux) =>
{
    var db = mux.GetDatabase();

    // StringSet stores a simple key-value pair.
    // TimeSpan.FromMinutes(5) = auto-expire after 5 minutes (TTL).
    // Without TTL, the key lives forever — memory leak risk!
    await db.StringSetAsync(key, value, TimeSpan.FromMinutes(5));

    return Results.Ok(new
    {
        status = "cached",
        key,
        value,
        expiresInSeconds = 300,
        explanation = "Key will auto-delete after 5 minutes (TTL)"
    });
});

// ---- GET a value from Redis ----
// GET /cache/get?key=user:123
app.MapGet("/cache/get", async (string key, IConnectionMultiplexer mux) =>
{
    var db = mux.GetDatabase();
    var value = await db.StringGetAsync(key);

    if (value.IsNullOrEmpty)
    {
        // CACHE MISS — the key doesn't exist (expired or never set).
        // In a real app, you'd fetch from the database here and then cache it.
        return Results.Ok(new
        {
            status = "miss",
            key,
            value = (string?)null,
            explanation = "Key not found in Redis. In production, you'd fetch from DB and cache the result."
        });
    }

    // CACHE HIT — found it in Redis, no need to hit the database.
    return Results.Ok(new
    {
        status = "hit",
        key,
        value = value.ToString(),
        explanation = "Found in Redis cache. Database was NOT queried — this is the speed win."
    });
});

// ---- CACHE-ASIDE PATTERN (the most common caching pattern) ----
// GET /cache/demo-aside?userId=42
//
// This demonstrates the "Cache-Aside" (or "Lazy Loading") pattern:
//   1. Check Redis first
//   2. If found (HIT) → return cached data
//   3. If not found (MISS) → fetch from "database", cache it, return it
//
// This is how 90% of production caching works.
app.MapGet("/cache/demo-aside", async (string userId, IConnectionMultiplexer mux) =>
{
    var db = mux.GetDatabase();
    var cacheKey = $"user:{userId}";

    // Step 1: Check cache
    var cached = await db.StringGetAsync(cacheKey);
    if (!cached.IsNullOrEmpty)
    {
        return Results.Ok(new
        {
            source = "cache",
            data = JsonSerializer.Deserialize<object>(cached!),
            explanation = "Data served from Redis. Database was NOT hit. Response time: ~1ms."
        });
    }

    // Step 2: Cache miss — simulate database query
    // In real life, this would be: var user = await dbContext.Users.FindAsync(userId);
    await Task.Delay(200); // Simulate 200ms database query
    var userData = new
    {
        id = userId,
        name = $"User {userId}",
        email = $"user{userId}@example.com",
        fetchedAt = DateTime.UtcNow
    };

    // Step 3: Store in cache for next time
    var json = JsonSerializer.Serialize(userData);
    await db.StringSetAsync(cacheKey, json, TimeSpan.FromMinutes(10));

    return Results.Ok(new
    {
        source = "database",
        data = userData,
        explanation = "Cache MISS. Fetched from database (200ms). Result now cached for 10 minutes. Next request will be ~1ms."
    });
});

// ============================================================
// RABBITMQ ENDPOINTS — Message Broker Deep Dive
// ============================================================
// A message broker DECOUPLES services. Instead of:
//   Service A calls Service B directly (tight coupling, B must be up)
// You get:
//   Service A → publishes message to RabbitMQ → Service B consumes it
//
// WHY THIS MATTERS:
//   1. Service B can be down — messages queue up, processed when B recovers
//   2. Service B can be slow — A doesn't wait, just drops the message and moves on
//   3. Multiple consumers — 10 instances of B can process messages in parallel
//   4. Retry logic — failed messages can be retried or sent to a dead-letter queue
//
// RABBITMQ CONCEPTS:
//   PRODUCER  — sends messages (your API endpoint)
//   QUEUE     — stores messages until consumed (like a mailbox)
//   CONSUMER  — reads and processes messages (background worker)
//   EXCHANGE  — routes messages to queues (like a post office sorting room)
//   BINDING   — rules that connect exchanges to queues
//
// EXCHANGE TYPES:
//   DIRECT  — message goes to queue with matching routing key (1-to-1)
//   FANOUT  — message goes to ALL bound queues (broadcast)
//   TOPIC   — message goes to queues matching a pattern (pub/sub)
//   HEADERS — routes based on message headers (rarely used)
// ============================================================

// ---- PUBLISH a message ----
// POST /messages/publish?message=Hello+World
app.MapPost("/messages/publish", async (string message, ConnectionFactory connFactory) =>
{
    // Create a connection and channel to RabbitMQ
    await using var connection = await connFactory.CreateConnectionAsync();
    await using var channel = await connection.CreateChannelAsync();

    // Declare a queue named "demo-queue".
    // Durable=true means the queue survives RabbitMQ restarts.
    // If the queue already exists, this is a no-op (safe to call repeatedly).
    await channel.QueueDeclareAsync(
        queue: "demo-queue",
        durable: true,       // Queue survives broker restart
        exclusive: false,    // Other connections can use this queue
        autoDelete: false,   // Don't delete when last consumer disconnects
        arguments: null
    );

    // Serialize the message to bytes (RabbitMQ deals in raw bytes)
    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        content = message,
        publishedAt = DateTime.UtcNow,
        id = Guid.NewGuid()
    }));

    // Publish to the default exchange ("") with routing key = queue name.
    // The default exchange routes directly to the queue with a matching name.
    await channel.BasicPublishAsync(
        exchange: "",
        routingKey: "demo-queue",
        body: body
    );

    return Results.Ok(new
    {
        status = "published",
        queue = "demo-queue",
        message,
        explanation = "Message is now sitting in RabbitMQ's 'demo-queue'. A consumer (background worker) will pick it up."
    });
});

// ---- CHECK queue status ----
// GET /messages/status
app.MapGet("/messages/status", async (ConnectionFactory connFactory) =>
{
    try
    {
        await using var connection = await connFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        // QueueDeclarePassive checks if the queue exists without creating it.
        // Returns the message count and consumer count.
        var queueInfo = await channel.QueueDeclarePassiveAsync("demo-queue");

        return Results.Ok(new
        {
            queue = "demo-queue",
            messageCount = queueInfo.MessageCount,
            consumerCount = queueInfo.ConsumerCount,
            explanation = $"There are {queueInfo.MessageCount} messages waiting to be consumed and {queueInfo.ConsumerCount} active consumers."
        });
    }
    catch
    {
        return Results.Ok(new
        {
            queue = "demo-queue",
            messageCount = 0,
            consumerCount = 0,
            explanation = "Queue doesn't exist yet. Publish a message first to create it."
        });
    }
});

// ---- FANOUT EXCHANGE DEMO (broadcast to multiple queues) ----
// POST /messages/broadcast?message=System+maintenance+at+midnight
app.MapPost("/messages/broadcast", async (string message, ConnectionFactory connFactory) =>
{
    await using var connection = await connFactory.CreateConnectionAsync();
    await using var channel = await connection.CreateChannelAsync();

    // Declare a FANOUT exchange.
    // Fanout = every message goes to ALL bound queues (broadcast).
    // Think: "Send this email to EVERYONE on the mailing list."
    await channel.ExchangeDeclareAsync(
        exchange: "notifications",
        type: ExchangeType.Fanout,
        durable: true
    );

    // Create and bind multiple queues to the fanout exchange.
    // Each queue represents a different "subscriber" or service.
    var queues = new[] { "email-notifications", "sms-notifications", "slack-notifications" };
    foreach (var queue in queues)
    {
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(queue, "notifications", routingKey: "");
    }

    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        content = message,
        broadcastAt = DateTime.UtcNow,
        type = "notification"
    }));

    // Publish to the "notifications" exchange.
    // Because it's FANOUT, the message goes to ALL 3 queues.
    await channel.BasicPublishAsync(
        exchange: "notifications",
        routingKey: "",  // Ignored for fanout exchanges
        body: body
    );

    return Results.Ok(new
    {
        status = "broadcast",
        exchange = "notifications",
        exchangeType = "fanout",
        deliveredTo = queues,
        message,
        explanation = "Message sent to ALL 3 queues simultaneously. Email service, SMS service, and Slack service each get a copy."
    });
});

// ---- TOPIC EXCHANGE DEMO (pattern-based routing) ----
// POST /messages/topic?routingKey=order.created&message=New+order+%23123
app.MapPost("/messages/topic", async (string routingKey, string message, ConnectionFactory connFactory) =>
{
    await using var connection = await connFactory.CreateConnectionAsync();
    await using var channel = await connection.CreateChannelAsync();

    // Declare a TOPIC exchange.
    // Topic exchanges route based on PATTERNS in the routing key.
    //   "order.created"  matches "order.*" and "order.#"
    //   "order.payment.failed" matches "order.#" but NOT "order.*"
    //
    // Wildcards:
    //   * = exactly one word     (order.* matches order.created, NOT order.payment.failed)
    //   # = zero or more words   (order.# matches order.created AND order.payment.failed)
    await channel.ExchangeDeclareAsync(
        exchange: "events",
        type: ExchangeType.Topic,
        durable: true
    );

    // Set up queues with different binding patterns
    // Each queue only receives messages matching its pattern
    var bindings = new Dictionary<string, string>
    {
        { "order-processing", "order.*" },           // Gets: order.created, order.cancelled
        { "payment-service", "order.payment.*" },    // Gets: order.payment.received, order.payment.failed
        { "audit-log", "order.#" },                  // Gets: ALL order events (catch-all)
        { "analytics", "#" }                         // Gets: EVERYTHING (wildcard)
    };

    foreach (var (queue, pattern) in bindings)
    {
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(queue, "events", routingKey: pattern);
    }

    var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        content = message,
        routingKey,
        publishedAt = DateTime.UtcNow
    }));

    await channel.BasicPublishAsync(
        exchange: "events",
        routingKey: routingKey,
        body: body
    );

    // Figure out which queues received the message based on the routing key
    var matchedQueues = bindings
        .Where(b => MatchesTopic(routingKey, b.Value))
        .Select(b => new { queue = b.Key, pattern = b.Value })
        .ToList();

    return Results.Ok(new
    {
        status = "published",
        exchange = "events",
        exchangeType = "topic",
        routingKey,
        message,
        deliveredTo = matchedQueues,
        explanation = $"Message with routing key '{routingKey}' was delivered to queues whose binding pattern matches.",
        tryThese = new[]
        {
            "order.created → goes to order-processing, audit-log, analytics",
            "order.payment.failed → goes to payment-service, audit-log, analytics",
            "order.shipped → goes to order-processing, audit-log, analytics",
            "user.registered → goes to analytics only"
        }
    });
});

// ---- REDIS PUB/SUB DEMO ----
// Redis also has pub/sub! Different from RabbitMQ:
//   RabbitMQ: messages are QUEUED (stored until consumed, guaranteed delivery)
//   Redis Pub/Sub: messages are FIRE-AND-FORGET (if no one is listening, message is lost)
//
// Use RabbitMQ when: you need guaranteed delivery, retries, dead-letter queues
// Use Redis Pub/Sub when: you need real-time notifications and can tolerate message loss
app.MapPost("/cache/publish", async (string channel, string message, IConnectionMultiplexer mux) =>
{
    var sub = mux.GetSubscriber();
    var receiverCount = await sub.PublishAsync(RedisChannel.Literal(channel), message);

    return Results.Ok(new
    {
        status = "published",
        channel,
        message,
        activeSubscribers = receiverCount,
        explanation = receiverCount > 0
            ? $"Message delivered to {receiverCount} subscriber(s) in real-time."
            : "No subscribers listening. Message is LOST. This is the key difference from RabbitMQ — no persistence."
    });
});

app.Run();

// ---- Helper: Simple topic pattern matcher ----
static bool MatchesTopic(string routingKey, string pattern)
{
    if (pattern == "#") return true;
    var routingParts = routingKey.Split('.');
    var patternParts = pattern.Split('.');

    int ri = 0, pi = 0;
    while (ri < routingParts.Length && pi < patternParts.Length)
    {
        if (patternParts[pi] == "#") return true;
        if (patternParts[pi] == "*" || patternParts[pi] == routingParts[ri])
        {
            ri++;
            pi++;
        }
        else return false;
    }
    return ri == routingParts.Length && pi == patternParts.Length;
}
