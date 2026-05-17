# Level 4 — .NET, Redis Deep Dive, and Message Brokers (RabbitMQ)

## What We Built

A **5-container** system that teaches you three major production concepts:

1. **.NET 8** — The framework behind most enterprise backends (C#, compiled, statically typed)
2. **Redis** — In-memory data store for caching, pub/sub, and session management
3. **RabbitMQ** — Message broker for async communication between services

```
┌──────────────────────────────────────────────────────────────────────┐
│                         YOUR MACHINE                                 │
│                                                                      │
│   Browser → http://localhost:8080      (API via Nginx)               │
│   Browser → http://localhost:15672     (RabbitMQ Management UI)      │
│                    │                                                  │
│              ┌─────┴──────┐                                          │
│              │   NGINX    │  ← Reverse proxy (same as Level 3)       │
│              │  port 8080 │                                          │
│              └─────┬──────┘                                          │
│                    │ forwards to port 8080                            │
│              ┌─────┴──────┐                                          │
│              │  .NET API  │  ← Your C# web API                      │
│              │  (api)     │                                          │
│              └──┬─────┬───┘                                          │
│                 │     │                                               │
│          ┌──────┴┐   ┌┴────────────┐                                 │
│          │ REDIS │   │  RABBITMQ   │                                 │
│          │ Cache │   │  Msg Broker │                                 │
│          └───────┘   └──────┬──────┘                                 │
│                             │                                        │
│                       ┌─────┴──────┐                                 │
│                       │   WORKER   │  ← Background message consumer  │
│                       │ (no HTTP)  │                                 │
│                       └────────────┘                                 │
└──────────────────────────────────────────────────────────────────────┘
```

---

## How to Run

```bash
cd level4-dotnet-redis-messaging

# Build and start all 5 services
docker compose up --build

# Once everything is healthy, test these URLs:
# API (through nginx):
curl http://localhost:8080
curl http://localhost:8080/health

# RabbitMQ Management UI:
# Open http://localhost:15672 in your browser
# Login: guest / guest
```

---

## What's Different from Level 3?

| Aspect | Level 3 (Python/Flask) | Level 4 (.NET/C#) |
|--------|----------------------|-------------------|
| Language | Python (interpreted) | C# (compiled to IL, then JIT) |
| Framework | Flask (micro) | ASP.NET Minimal API |
| Type system | Dynamic typing | Static typing |
| Concurrency | GIL limits threads | True multi-threading |
| Package manager | pip + requirements.txt | NuGet + .csproj |
| Build output | Source code (runs via interpreter) | Compiled DLL (runs via runtime) |
| Containers | 4 (nginx, web, db, redis) | 5 (nginx, api, worker, redis, rabbitmq) |
| Messaging | None | RabbitMQ (async processing) |
| Background work | None | Dedicated worker service |
| Cache usage | Redis present but unused | Full caching patterns demonstrated |

---

## WHY These Technologies? — The Decision Reasoning

> Before diving into HOW each technology works, let's answer the question
> a senior engineer should always ask first: **WHY did we pick this specific
> tool over the alternatives?** Every technology choice is a trade-off.
> Understanding the reasoning is more valuable than memorizing the API.

---

### Why .NET 8 (and not Python/Flask, Node.js, Go, or Java)?

```
THE DECISION MATRIX — We needed a backend framework. Here are the real options:

┌──────────────────┬────────────┬────────────┬────────────┬────────────┬────────────┐
│ Criteria         │ Python     │ Node.js    │ .NET 8     │ Go         │ Java       │
│                  │ (Flask)    │ (Express)  │ (C#)       │ (Gin)      │ (Spring)   │
├──────────────────┼────────────┼────────────┼────────────┼────────────┼────────────┤
│ Learning curve   │ ★★★★★      │ ★★★★☆      │ ★★★☆☆      │ ★★★☆☆      │ ★★☆☆☆      │
│ (from Python)    │ You know it│ JS is close│ New syntax │ Very diff  │ Very diff  │
├──────────────────┼────────────┼────────────┼────────────┼────────────┼────────────┤
│ Performance      │ ★★☆☆☆      │ ★★★☆☆      │ ★★★★★      │ ★★★★★      │ ★★★★☆      │
│ (req/sec)        │ ~2K        │ ~15K       │ ~50K+      │ ~100K+     │ ~30K       │
├──────────────────┼────────────┼────────────┼────────────┼────────────┼────────────┤
│ Type safety      │ ✗ Dynamic  │ ✗ Dynamic  │ ✓ Static   │ ✓ Static   │ ✓ Static   │
│                  │ (runtime)  │ (TS helps) │ (compile)  │ (compile)  │ (compile)  │
├──────────────────┼────────────┼────────────┼────────────┼────────────┼────────────┤
│ Enterprise       │ ★★★☆☆      │ ★★★☆☆      │ ★★★★★      │ ★★★☆☆      │ ★★★★★      │
│ adoption         │ Data/ML    │ Startups   │ Fortune500 │ Infra/CLI  │ Banks/Gov  │
├──────────────────┼────────────┼────────────┼────────────┼────────────┼────────────┤
│ Built-in         │ ★★☆☆☆      │ ★★☆☆☆      │ ★★★★★      │ ★★☆☆☆      │ ★★★★☆      │
│ features (DI,    │ Add-ons    │ Add-ons    │ All built  │ Minimal    │ Spring has │
│ health, config)  │ needed     │ needed     │ in         │ stdlib     │ everything │
├──────────────────┼────────────┼────────────┼────────────┼────────────┼────────────┤
│ AWS integration  │ ★★★★☆      │ ★★★★☆      │ ★★★★☆      │ ★★★★☆      │ ★★★★☆      │
│                  │ boto3      │ aws-sdk    │ AWSSDK     │ aws-sdk-go │ aws-sdk    │
├──────────────────┼────────────┼────────────┼────────────┼────────────┼────────────┤
│ Hiring pool      │ ★★★★★      │ ★★★★★      │ ★★★★☆      │ ★★★☆☆      │ ★★★★★      │
└──────────────────┴────────────┴────────────┴────────────┴────────────┴────────────┘

WHY WE CHOSE .NET 8 FOR THIS LEVEL:

  1. YOU ALREADY KNOW PYTHON — Levels 2 and 3 used Flask. Repeating Python
     teaches you nothing new about backend architecture. .NET forces you to
     think in a compiled, typed world — which is what most production backends
     at scale actually use.

  2. ENTERPRISE REALITY — Walk into any Fortune 500 company and you'll find
     .NET or Java. Your company likely has .NET services. Understanding C#
     makes you dangerous in code reviews and architecture discussions even
     if you don't write it daily.

  3. PERFORMANCE MATTERS AT SCALE — Python's GIL means one thread at a time.
     .NET handles 25x more requests per server. When you're paying for EC2
     instances, that's real money saved.

  4. BATTERIES INCLUDED — .NET has dependency injection, health checks,
     configuration binding, and middleware built into the framework. In Flask,
     you bolt these on with third-party packages. In .NET, they're first-class.

  5. MINIMAL API SYNTAX — .NET 8's Minimal APIs look almost like Flask.
     The learning curve is gentler than you'd expect:
       Flask:  @app.route("/")     →  .NET: app.MapGet("/", ...)
     You're not learning "enterprise Java ceremony." It's clean and modern.

WHAT WE REJECTED AND WHY:
  - Python again: No new learning. You already built Levels 2-3 with it.
  - Node.js: Good choice, but still dynamically typed. Doesn't teach the
    compiled/typed paradigm that enterprises rely on.
  - Go: Great for infrastructure tools (Docker, Kubernetes are written in Go),
    but its ecosystem for web APIs is thinner. Less relevant for your day job.
  - Java/Spring: Equally valid as .NET, but Spring Boot has more ceremony
    and boilerplate. .NET Minimal APIs are a gentler introduction to the
    compiled world.
```

---

### Why Redis (and not Memcached, Hazelcast, or just in-process caching)?

```
THE DECISION MATRIX — We needed a caching layer:

┌──────────────────┬────────────────┬────────────────┬────────────────┬────────────────┐
│ Criteria         │ In-Process     │ Memcached      │ Redis          │ Hazelcast      │
│                  │ (Dictionary)   │                │                │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Shared across    │ ✗ NO           │ ✓ YES          │ ✓ YES          │ ✓ YES          │
│ instances        │ Each process   │ Centralized    │ Centralized    │ Distributed    │
│                  │ has its own    │                │                │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Data types       │ Language-      │ Strings only   │ Strings, Hash, │ Maps, Lists,   │
│                  │ dependent      │                │ List, Set,     │ Sets, Queues   │
│                  │                │                │ Sorted Set,    │                │
│                  │                │                │ Streams        │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Persistence      │ ✗ Lost on      │ ✗ Lost on      │ ✓ RDB + AOF    │ ✓ Yes          │
│                  │ restart        │ restart        │ (survives      │                │
│                  │                │                │ restarts)      │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Pub/Sub          │ ✗ No           │ ✗ No           │ ✓ Built-in     │ ✓ Built-in     │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ AWS managed      │ N/A            │ ElastiCache    │ ElastiCache    │ Self-managed   │
│ service          │                │ (Memcached)    │ (Redis)        │ only           │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Community /      │ N/A            │ Shrinking      │ Massive        │ Niche          │
│ ecosystem        │                │                │ (industry std) │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Learning value   │ ★☆☆☆☆          │ ★★☆☆☆          │ ★★★★★          │ ★★☆☆☆          │
│ (for your career)│ Too simple     │ Outdated       │ Used everywhere│ Rare           │
└──────────────────┴────────────────┴────────────────┴────────────────┴────────────────┘

WHY WE CHOSE REDIS:

  1. INDUSTRY STANDARD — Redis is the #1 caching solution. Period.
     Stack Overflow, Twitter, GitHub, Pinterest, Snapchat all use Redis.
     AWS ElastiCache defaults to Redis. Learning Redis is a career investment.

  2. MORE THAN A CACHE — Memcached is ONLY a cache (strings in, strings out).
     Redis is a cache + data structure server + pub/sub system + stream processor.
     One tool teaches you five concepts:
       - Caching (STRING)
       - Object storage (HASH)
       - Job queues (LIST)
       - Unique tracking (SET)
       - Leaderboards (SORTED SET)
       - Real-time messaging (PUB/SUB)

  3. PERSISTENCE — Redis can save data to disk (RDB snapshots + AOF log).
     If Redis restarts, your cache is warm immediately. Memcached starts
     cold — every key is gone, every request hits the database until the
     cache refills. That's a thundering herd problem.

  4. AWS ELASTICACHE SUPPORT — Your company uses AWS. ElastiCache for Redis
     gives you managed Redis with automatic failover, Multi-AZ replication,
     encryption, and CloudWatch monitoring. The code you write locally with
     Docker Redis works identically against ElastiCache — just change the
     connection string.

  5. PUB/SUB FOR FREE — Redis Pub/Sub lets us demonstrate real-time messaging
     without adding another service. This teaches the concept before you
     encounter it in production with SNS or EventBridge.

WHAT WE REJECTED AND WHY:
  - In-process cache (Dictionary/ConcurrentDictionary): Doesn't work when you
    have multiple API instances behind a load balancer. Each instance has its
    own cache — 2 out of 3 requests still hit the database.
  - Memcached: Strings only, no persistence, no pub/sub, shrinking community.
    Redis does everything Memcached does and more.
  - Hazelcast: Powerful but niche. Not available as a managed AWS service.
    You'd need to run and manage it yourself. Overkill for learning.
```

---

### Why RabbitMQ (and not AWS SQS, Apache Kafka, or Redis Streams)?

```
THE DECISION MATRIX — We needed async messaging between services:

┌──────────────────┬────────────────┬────────────────┬────────────────┬────────────────┐
│ Criteria         │ RabbitMQ       │ AWS SQS+SNS    │ Apache Kafka   │ Redis Streams  │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Runs locally     │ ✓ Docker image │ ✗ AWS only     │ ✓ Docker image │ ✓ Already have │
│ (for learning)   │ (easy)         │ (need account) │ (heavy, 3+    │ Redis running  │
│                  │                │                │ containers)    │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Management UI    │ ✓ Built-in     │ ✗ AWS Console  │ ✗ Need Kafka   │ ✗ No UI        │
│ (see queues,     │ localhost:     │ only           │ UI (separate)  │                │
│ messages)        │ 15672          │                │                │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Exchange/routing │ ✓ Direct,      │ ✗ SNS topics   │ ✗ Topics +     │ ✗ Consumer     │
│ patterns         │ Fanout, Topic, │ (simpler)      │ partitions     │ groups only    │
│                  │ Headers        │                │ (different     │                │
│                  │                │                │ model)         │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Message          │ ✓ ACK/NACK     │ ✓ Visibility   │ ✓ Offset-based │ ✓ ACK via      │
│ acknowledgment   │ (explicit)     │ timeout        │ (consumer      │ XACK           │
│                  │                │ (auto-retry)   │ commits)       │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Dead letter      │ ✓ Built-in DLX │ ✓ Built-in DLQ │ ✗ Manual       │ ✗ Manual       │
│ queue            │                │                │                │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Throughput       │ ~50K msg/sec   │ ~3K msg/sec    │ ~1M msg/sec    │ ~100K msg/sec  │
│                  │ (good enough)  │ (standard)     │ (overkill)     │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Concepts taught  │ ★★★★★          │ ★★★☆☆          │ ★★★★★          │ ★★☆☆☆          │
│                  │ Exchanges,     │ Queue + Topic  │ Partitions,    │ Streams,       │
│                  │ bindings,      │ (simpler)      │ offsets,       │ groups         │
│                  │ routing, ACK,  │                │ consumer       │                │
│                  │ DLQ, QoS       │                │ groups (steep) │                │
├──────────────────┼────────────────┼────────────────┼────────────────┼────────────────┤
│ Maps to AWS      │ Amazon MQ      │ Native AWS     │ Amazon MSK     │ ElastiCache    │
│                  │ (managed       │ (fully managed │ (managed       │ (already have  │
│                  │ RabbitMQ)      │ serverless)    │ Kafka)         │ it)            │
└──────────────────┴────────────────┴────────────────┴────────────────┴────────────────┘

WHY WE CHOSE RABBITMQ:

  1. BEST LEARNING TOOL — RabbitMQ has the richest set of messaging concepts
     in one place: exchanges (4 types), bindings, routing keys, acknowledgments,
     prefetch/QoS, dead letter queues. Once you understand RabbitMQ, you can
     pick up SQS, Kafka, or any other broker in a day because you know the
     underlying concepts.

  2. BUILT-IN MANAGEMENT UI — Open http://localhost:15672 and you can SEE
     your queues, messages, consumers, and exchanges in real-time. This is
     invaluable for learning. SQS requires the AWS Console (needs an account).
     Kafka needs a separate UI tool. RabbitMQ ships with it out of the box.

  3. RUNS LOCALLY WITH ONE DOCKER IMAGE — `rabbitmq:3-management-alpine` gives
     you a full message broker with a web UI in one container. Kafka needs
     ZooKeeper + Kafka broker + Schema Registry (3+ containers, 2GB+ RAM).
     SQS doesn't run locally at all (LocalStack is a workaround, not the real thing).

  4. EXCHANGE PATTERNS TEACH REAL ARCHITECTURE — The 4 exchange types map
     directly to real-world patterns:
       Direct  → "Process this specific task" (job queue)
       Fanout  → "Notify all services" (event broadcast, like SNS)
       Topic   → "Route by event type" (event-driven architecture)
       Headers → "Route by metadata" (content-based routing)
     These patterns exist in EVERY message broker — RabbitMQ just names them
     explicitly so you can learn them.

  5. MAPS TO AWS — When you move to production, RabbitMQ maps to:
       Amazon MQ (managed RabbitMQ — same code, zero changes)
       OR SQS + SNS (AWS-native, different API but same concepts)
     Understanding RabbitMQ exchanges makes SQS+SNS trivial to learn:
       Fanout exchange = SNS topic
       Queue = SQS queue
       Binding = SNS subscription

WHAT WE REJECTED AND WHY:
  - AWS SQS: Can't run locally. Simpler model (no exchanges) teaches fewer
    concepts. Great in production, but not ideal for learning the fundamentals.
  - Apache Kafka: Overkill for this level. Needs 3+ containers and 2GB+ RAM.
    Kafka's model (log-based, offsets, partitions) is fundamentally different
    from traditional message queues — it deserves its own Level 5.
  - Redis Streams: We already have Redis running, so it's tempting. But Streams
    are a newer, less mature feature. The API is less intuitive than RabbitMQ's.
    And using Redis for BOTH caching AND messaging blurs the learning boundary
    between the two concepts.
```

---

### Why a Separate Worker Container (and not background threads in the API)?

```
THE OPTIONS:

  Option A: Background thread in the API process
    app.MapPost("/orders", async () => {
        await SaveOrder();
        _ = Task.Run(() => ProcessPayment());  // fire-and-forget
        return Results.Accepted();
    });

    PROBLEMS:
    ✗ If the API container restarts, in-flight background work is LOST
    ✗ Can't scale workers independently of the API
    ✗ Background work competes with HTTP requests for CPU/memory
    ✗ No retry mechanism if processing fails
    ✗ No visibility into what's being processed

  Option B: Separate worker container consuming from RabbitMQ ← WHAT WE CHOSE
    API publishes message → RabbitMQ stores it → Worker consumes it

    BENEFITS:
    ✓ Messages survive container restarts (RabbitMQ persists them)
    ✓ Scale workers independently: docker compose up --scale worker=5
    ✓ API stays fast (only publishes, doesn't process)
    ✓ Built-in retry: if worker crashes, message goes back to queue
    ✓ Visibility: RabbitMQ UI shows queue depth, consumer count, message rates
    ✓ Different resource profiles: API needs fast response, worker needs CPU

  Option C: AWS Lambda triggered by SQS
    Same concept as Option B, but serverless. Great in production,
    but can't run locally and adds AWS dependency for learning.

WHY OPTION B:
  It teaches the PRODUCER-CONSUMER pattern — the most important distributed
  systems pattern you'll encounter. Every company uses it:
    - API publishes "order.created" → Worker processes payment
    - API publishes "image.uploaded" → Worker generates thumbnails
    - API publishes "report.requested" → Worker generates PDF

  The separate container also teaches you that services don't have to be
  web servers. The worker has NO HTTP endpoints. It just sits there,
  consuming messages. This is a mental model shift from "everything is
  a web API" to "services are specialized processes."
```

---

## CONCEPT 1: .NET — What It Is and Why It Matters

### Python vs .NET — The Core Difference

```
PYTHON (what you know):
  You write app.py → Python INTERPRETER reads it line by line → runs it
  Like reading a recipe out loud and cooking as you go.

.NET / C# (what enterprises use):
  You write Program.cs → COMPILER turns it into a DLL (binary) → Runtime executes the DLL
  Like translating a recipe into a cooking robot's instructions, then the robot cooks.

The compilation step catches errors BEFORE your code runs:
  Python: "NameError: name 'usre' is not defined" → crashes at runtime, in production, at 3 AM
  C#:     "CS0103: The name 'usre' does not exist" → fails at BUILD time, before deploy
```

### Why Do Enterprises Use .NET?

```
1. PERFORMANCE
   Python Flask:  ~2,000 requests/second (limited by GIL)
   .NET Kestrel:  ~50,000+ requests/second (true multi-threading)
   For high-traffic APIs, .NET handles 25x more load per server.

2. TYPE SAFETY
   Python: def get_user(id):        ← id could be string, int, None, a list...
   C#:     User GetUser(int id)     ← id MUST be an int. Return MUST be a User.
   The compiler catches bugs that Python only finds at runtime.

3. ENTERPRISE ECOSYSTEM
   - Azure (Microsoft's cloud) has first-class .NET support
   - Most Fortune 500 companies run .NET backends
   - Banks, healthcare, government — anywhere reliability matters
   - Your company likely has .NET services alongside Python ones

4. TOOLING
   - Visual Studio / Rider = best-in-class IDE support
   - Built-in dependency injection, health checks, configuration
   - Entity Framework = ORM (like SQLAlchemy but with migrations built in)
```

### .NET Project Structure Explained

```
level4-dotnet-redis-messaging/
├── src/
│   ├── Api/                          ← The web API project
│   │   ├── Api.csproj                ← Project file (like requirements.txt + package.json combined)
│   │   ├── Program.cs                ← Entry point (like Flask's app.py)
│   │   └── appsettings.json          ← Configuration (like .env but structured)
│   │
│   └── Worker/                       ← Background worker project
│       ├── Worker.csproj             ← Its own project file (different dependencies)
│       ├── Program.cs                ← Entry point (runs forever, consumes messages)
│       └── appsettings.json          ← Worker-specific config
│
├── nginx/
│   └── nginx.conf                    ← Same reverse proxy concept as Level 3
│
├── Dockerfile.api                    ← Multi-stage build for the API
├── Dockerfile.worker                 ← Multi-stage build for the worker
├── docker-compose.yml                ← Orchestrates all 5 services
└── .dockerignore                     ← Excludes bin/, obj/ (like __pycache__)
```

### Flask vs .NET — Side by Side

```python
# ---- FLASK (Python) ----

from flask import Flask, jsonify
app = Flask(__name__)

@app.route("/")
def home():
    return jsonify({"message": "Hello"})

@app.route("/health")
def health():
    return jsonify({"status": "healthy"})

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000)
```

```csharp
// ---- .NET MINIMAL API (C#) ----

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => new { message = "Hello" });

app.MapGet("/health", () => new { status = "healthy" });

app.Run();
```

```
Line-by-line comparison:

Flask:  from flask import Flask        →  .NET:  (built-in, no import needed)
Flask:  app = Flask(__name__)          →  .NET:  var app = WebApplication.CreateBuilder(args).Build();
Flask:  @app.route("/")               →  .NET:  app.MapGet("/", ...)
Flask:  return jsonify({...})          →  .NET:  return new { ... }  (auto-serialized to JSON)
Flask:  app.run(host="0.0.0.0")       →  .NET:  app.Run()
```

### The .csproj File — .NET's requirements.txt

```xml
<!-- Api.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>    <!-- Like "python_requires >= 3.9" -->
  </PropertyGroup>

  <ItemGroup>
    <!-- Like pip install redis==5.0.1 -->
    <PackageReference Include="StackExchange.Redis" Version="2.7.33" />
    <!-- Like pip install pika==1.3.2 (RabbitMQ client) -->
    <PackageReference Include="RabbitMQ.Client" Version="7.1.2" />
  </ItemGroup>
</Project>
```

```
Python equivalent:
  pip install redis==5.0.1        →  dotnet add package StackExchange.Redis
  pip install -r requirements.txt →  dotnet restore (reads .csproj)
  pip freeze                      →  dotnet list package
```

### .NET Build Process (What Happens in the Dockerfile)

```
PYTHON BUILD:
  COPY requirements.txt .
  RUN pip install -r requirements.txt    ← Downloads packages
  COPY . .                               ← Copies source code
  CMD ["gunicorn", "app:app"]            ← Runs source code directly

.NET BUILD:
  COPY Api.csproj .
  RUN dotnet restore                     ← Downloads NuGet packages (like pip install)
  COPY . .
  RUN dotnet publish -c Release -o /out  ← COMPILES source code into a DLL binary
  CMD ["dotnet", "Api.dll"]              ← Runs the compiled binary (not source code)
                    ^^^
                    This is the key difference. Python ships source code.
                    .NET ships compiled binaries. Faster startup, smaller attack surface.
```

---

## CONCEPT 2: Redis Deep Dive — More Than Just a Cache

### What Is Redis?

```
Redis = Remote Dictionary Server

Think of it as a GIANT Python dictionary that lives on a separate server
and is accessible by ALL your application instances.

Python dict (in-memory, single process):
  cache = {"user:42": "John", "user:43": "Jane"}
  cache["user:42"]  # instant

Redis (in-memory, shared across ALL processes/servers):
  redis.set("user:42", "John")
  redis.get("user:42")  # instant, and ANY server can read it

WHY NOT JUST USE A PYTHON DICT?
  ┌─────────────────────────────────────────────────────────────┐
  │ Scenario: 3 instances of your API behind a load balancer     │
  │                                                              │
  │ With Python dict (in-process cache):                         │
  │   Instance 1: cache = {"user:42": "John"}                    │
  │   Instance 2: cache = {}  ← doesn't have it!                │
  │   Instance 3: cache = {}  ← doesn't have it!                │
  │   Each instance has its OWN cache. 2 out of 3 requests       │
  │   still hit the database. Useless.                           │
  │                                                              │
  │ With Redis (external shared cache):                          │
  │   Instance 1 ─┐                                              │
  │   Instance 2 ─┼── Redis: {"user:42": "John"}                │
  │   Instance 3 ─┘                                              │
  │   ALL instances share ONE cache. First request caches it,    │
  │   all subsequent requests from ANY instance get the cache hit.│
  └─────────────────────────────────────────────────────────────┘
```

### Redis Data Types (with real-world examples)

```
1. STRING — Simple key-value (most common, 80% of Redis usage)
   SET user:42:name "John"
   GET user:42:name → "John"
   Use: Caching API responses, session tokens, feature flags

2. HASH — Like a Python dict inside a key
   HSET user:42 name "John" email "john@example.com" age "30"
   HGET user:42 name → "John"
   HGETALL user:42 → {"name": "John", "email": "john@example.com", "age": "30"}
   Use: Storing objects (user profiles, product details)

3. LIST — Ordered list (can be used as queue or stack)
   LPUSH notifications:42 "You have a new order"
   LPUSH notifications:42 "Payment received"
   LRANGE notifications:42 0 -1 → ["Payment received", "You have a new order"]
   Use: Activity feeds, recent items, job queues

4. SET — Unique unordered collection
   SADD online-users "user:42" "user:43" "user:44"
   SISMEMBER online-users "user:42" → true
   SCARD online-users → 3
   Use: Tracking unique visitors, tags, online users

5. SORTED SET — Unique collection with scores (for ranking)
   ZADD leaderboard 1500 "player:alice" 2300 "player:bob" 1800 "player:charlie"
   ZREVRANGE leaderboard 0 2 → ["player:bob", "player:charlie", "player:alice"]
   Use: Leaderboards, priority queues, time-series data
```

### Caching Patterns — How Production Systems Use Redis

```
PATTERN 1: CACHE-ASIDE (Lazy Loading) — Most Common
═══════════════════════════════════════════════════

  Request comes in for user:42
       │
       ▼
  ┌─────────────┐     1. Check Redis
  │  Your API   │────────────────────→ Redis: GET user:42
  │             │                           │
  │             │     2a. CACHE HIT         │
  │             │←──── Found! Return it ────┘
  │             │      (skip database)       Response time: ~1ms
  │             │
  │             │     2b. CACHE MISS
  │             │←──── Not found ───────────┘
  │             │
  │             │     3. Query database
  │             │────────────────────→ PostgreSQL: SELECT * FROM users WHERE id=42
  │             │←──── {name: "John"} ──────┘     Response time: ~50ms
  │             │
  │             │     4. Store in Redis (for next time)
  │             │────────────────────→ Redis: SET user:42 '{"name":"John"}' EX 600
  │             │                                                           ^^^^^^
  │             │                                                    Expire after 10 min
  └─────────────┘

  Next request for user:42 → Cache HIT → 1ms instead of 50ms
  That's a 50x speedup.

  PROS: Simple, only caches what's actually requested
  CONS: First request is always slow (cache miss)


PATTERN 2: WRITE-THROUGH — Cache on Write
═════════════════════════════════════════

  User updates their profile
       │
       ▼
  ┌─────────────┐     1. Write to database
  │  Your API   │────────────────────→ PostgreSQL: UPDATE users SET name='Jane'
  │             │
  │             │     2. ALSO write to Redis
  │             │────────────────────→ Redis: SET user:42 '{"name":"Jane"}' EX 600
  └─────────────┘

  Cache is ALWAYS up-to-date because you update it on every write.

  PROS: Cache is always fresh, no stale data
  CONS: Every write is slower (two writes instead of one)
        Caches data that might never be read


PATTERN 3: WRITE-BEHIND (Write-Back) — Batch Writes
════════════════════════════════════════════════════

  User updates their profile
       │
       ▼
  ┌─────────────┐     1. Write to Redis ONLY (fast!)
  │  Your API   │────────────────────→ Redis: SET user:42 '{"name":"Jane"}'
  │             │                      Return 200 OK immediately
  └─────────────┘
                       2. Background process syncs to DB periodically
                       Redis ──batch write──→ PostgreSQL (every 5 seconds)

  PROS: Writes are super fast (Redis only), database gets batched writes
  CONS: Risk of data loss if Redis crashes before sync
        More complex to implement


PATTERN 4: CACHE INVALIDATION — The Hard Problem
═════════════════════════════════════════════════

  "There are only two hard things in Computer Science:
   cache invalidation and naming things." — Phil Karlton

  The problem: when data changes, how do you update/remove the cached version?

  Option A: TTL (Time To Live) — simplest
    SET user:42 '{"name":"John"}' EX 600    ← auto-deletes after 10 minutes
    PROS: Simple, automatic cleanup
    CONS: Data can be stale for up to 10 minutes

  Option B: Explicit invalidation — on write, delete the cache
    UPDATE users SET name='Jane' WHERE id=42;   ← update DB
    DEL user:42;                                 ← delete cache
    PROS: Cache is never stale
    CONS: Must remember to invalidate everywhere data changes

  Option C: Event-driven invalidation
    Database change → publishes event → consumer deletes cache
    PROS: Decoupled, works across services
    CONS: More infrastructure (needs a message broker)
```

### Redis vs Memcached (AWS ElastiCache offers both)

```
┌──────────────────┬──────────────────────┬──────────────────────┐
│ Feature          │ Redis                │ Memcached            │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Data types       │ Strings, Hashes,     │ Strings ONLY         │
│                  │ Lists, Sets, Sorted  │                      │
│                  │ Sets, Streams        │                      │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Persistence      │ Yes (RDB + AOF)      │ No (memory only)     │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Pub/Sub          │ Yes                  │ No                   │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Replication      │ Yes (primary/replica)│ No                   │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Clustering       │ Yes (Redis Cluster)  │ Yes (client-side)    │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Lua scripting    │ Yes                  │ No                   │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Multi-threading  │ Single-threaded*     │ Multi-threaded       │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Use case         │ Cache + data store   │ Pure cache only      │
│                  │ + message broker     │                      │
└──────────────────┴──────────────────────┴──────────────────────┘

* Redis 7+ has I/O threading, but command execution is still single-threaded.
  This is actually a FEATURE — no locks needed, no race conditions.

WHEN TO USE WHICH:
  Redis:     Almost always. It does everything Memcached does and more.
  Memcached: Only if you need simple key-value caching with multi-threaded
             performance and don't need any advanced features.

In AWS ElastiCache:
  Redis mode:     ElastiCache for Redis (most teams use this)
  Memcached mode: ElastiCache for Memcached (legacy or simple caching)
```

### Redis in AWS — ElastiCache

```
LOCAL (this project):
  Your .NET API → Docker network → Redis container (localhost)

AWS PRODUCTION:
  Your ECS Task → VPC network → ElastiCache Redis cluster (managed by AWS)

What ElastiCache gives you that a Docker Redis container doesn't:
  ✓ Automatic failover (primary dies → replica promoted in seconds)
  ✓ Multi-AZ replication (data in 2+ data centers)
  ✓ Automatic backups and point-in-time recovery
  ✓ Encryption at rest and in transit
  ✓ Monitoring via CloudWatch (hit rate, memory usage, connections)
  ✓ Patching and maintenance handled by AWS
  ✓ Scaling (add read replicas, resize nodes)

Connection string changes from:
  Local:  "redis:6379"
  AWS:    "my-cluster.abc123.0001.apse2.cache.amazonaws.com:6379"

Your code stays EXACTLY the same. Only the connection string changes.
This is why we use environment variables for configuration.
```

---

## CONCEPT 3: Message Brokers — RabbitMQ Deep Dive

### Why Do You Need a Message Broker?

```
WITHOUT a message broker (direct calls):
═══════════════════════════════════════

  User places order
       │
       ▼
  ┌─────────────┐     1. Save order to DB (50ms)
  │ Order API   │     2. Call Payment Service (200ms)
  │             │     3. Call Inventory Service (150ms)
  │             │     4. Call Email Service (300ms)
  │             │     5. Call Analytics Service (100ms)
  └─────────────┘
       │
       ▼
  Return 200 OK to user
  Total time: 50 + 200 + 150 + 300 + 100 = 800ms 😱

  PROBLEMS:
  - User waits 800ms for a response (slow!)
  - If Email Service is down → entire order fails (fragile!)
  - If Analytics Service is slow → everything is slow (coupled!)
  - Order API must know about ALL downstream services (tight coupling)


WITH a message broker (RabbitMQ):
═════════════════════════════════

  User places order
       │
       ▼
  ┌─────────────┐     1. Save order to DB (50ms)
  │ Order API   │     2. Publish "order.created" event to RabbitMQ (5ms)
  └─────────────┘
       │
       ▼
  Return 200 OK to user
  Total time: 50 + 5 = 55ms 🚀

  Meanwhile, RabbitMQ delivers the event to all interested services:

  RabbitMQ ──→ Payment Service (processes payment)
           ──→ Inventory Service (reserves stock)
           ──→ Email Service (sends confirmation)
           ──→ Analytics Service (tracks the order)

  BENEFITS:
  - User gets response in 55ms instead of 800ms (14x faster!)
  - If Email Service is down → messages queue up, processed when it recovers
  - If Analytics is slow → doesn't affect the user at all
  - Order API only knows about RabbitMQ, not downstream services (loose coupling)
  - New services can subscribe without changing Order API
```

### RabbitMQ Concepts Visualized

```
PRODUCER → EXCHANGE → BINDING → QUEUE → CONSUMER

┌──────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────┐
│ Producer │───→│   Exchange   │───→│    Queue     │───→│ Consumer │
│ (API)    │    │ (post office │    │ (mailbox)    │    │ (worker) │
│          │    │  sorting     │    │              │    │          │
│ Publishes│    │  room)       │    │ Stores msgs  │    │ Processes│
│ messages │    │              │    │ until read   │    │ messages │
└──────────┘    │ Routes msgs  │    └──────────────┘    └──────────┘
                │ to queues    │
                │ based on     │
                │ rules        │
                └──────────────┘

ANALOGY:
  Producer = You writing a letter
  Exchange = Post office sorting room (decides which mailbox)
  Binding  = Sorting rules ("letters to 90210 go to California bin")
  Queue    = Mailbox (stores letters until recipient picks them up)
  Consumer = Recipient reading their mail
```

### Exchange Types — The Four Ways to Route Messages

```
1. DIRECT EXCHANGE — One-to-one routing
═══════════════════════════════════════

  "Send this message to the queue with THIS EXACT routing key."

  Producer publishes with routing_key="payment"
       │
       ▼
  ┌─────────────────┐
  │ Direct Exchange  │
  │                  │──→ Queue "payment-queue" (binding key: "payment") ✅ MATCH
  │ routing_key =    │──→ Queue "email-queue"   (binding key: "email")   ✗ no match
  │ "payment"        │──→ Queue "sms-queue"     (binding key: "sms")     ✗ no match
  └─────────────────┘

  Use case: Task distribution. "Process this specific payment."


2. FANOUT EXCHANGE — Broadcast to ALL queues
════════════════════════════════════════════

  "Send this message to EVERY queue bound to this exchange."
  Routing key is IGNORED.

  Producer publishes (any routing key, doesn't matter)
       │
       ▼
  ┌─────────────────┐
  │ Fanout Exchange  │
  │                  │──→ Queue "email-notifications"  ✅ gets it
  │ Ignores routing  │──→ Queue "sms-notifications"    ✅ gets it
  │ key entirely     │──→ Queue "slack-notifications"   ✅ gets it
  └─────────────────┘

  Use case: Notifications. "Tell EVERYONE about this system event."
  This is what our /messages/broadcast endpoint demonstrates.


3. TOPIC EXCHANGE — Pattern-based routing (most flexible)
═════════════════════════════════════════════════════════

  "Send this message to queues whose binding PATTERN matches the routing key."

  Wildcards:
    *  = exactly ONE word     (order.* matches order.created, NOT order.payment.failed)
    #  = zero or MORE words   (order.# matches order.created AND order.payment.failed)

  Producer publishes with routing_key="order.payment.failed"
       │
       ▼
  ┌─────────────────┐
  │ Topic Exchange   │
  │                  │──→ Queue "order-processing"  (pattern: "order.*")          ✗ no match
  │ routing_key =    │      order.* needs exactly 2 words, but we have 3
  │ "order.payment.  │
  │  failed"         │──→ Queue "payment-service"   (pattern: "order.payment.*")  ✅ MATCH
  │                  │      order.payment.* matches order.payment.{anything}
  │                  │
  │                  │──→ Queue "audit-log"          (pattern: "order.#")          ✅ MATCH
  │                  │      order.# matches order.{anything, any depth}
  │                  │
  │                  │──→ Queue "analytics"          (pattern: "#")                ✅ MATCH
  │                  │      # matches EVERYTHING
  └─────────────────┘

  Use case: Event-driven architecture. Services subscribe to events they care about.
  This is what our /messages/topic endpoint demonstrates.


4. HEADERS EXCHANGE — Route based on message headers (rarely used)
═════════════════════════════════════════════════════════════════

  Routes based on header key-value pairs instead of routing keys.
  Rarely used in practice. Topic exchange covers most use cases.
```

### Message Acknowledgment — Guaranteed Delivery

```
THE PROBLEM: What if a worker crashes WHILE processing a message?

WITHOUT acknowledgment (autoAck: true):
  1. RabbitMQ sends message to Worker
  2. RabbitMQ IMMEDIATELY removes message from queue
  3. Worker crashes mid-processing
  4. Message is LOST FOREVER 💀

WITH acknowledgment (autoAck: false — what we use):
  1. RabbitMQ sends message to Worker
  2. RabbitMQ keeps message in queue (marked as "unacked")
  3. Worker processes the message
  4. Worker sends ACK → RabbitMQ removes message ✅
  OR
  3. Worker crashes → RabbitMQ sees connection lost
  4. RabbitMQ re-delivers message to another worker ✅

This is called AT-LEAST-ONCE delivery:
  - Every message is processed AT LEAST once
  - If a worker crashes, the message is retried
  - Your processing code must be IDEMPOTENT (safe to run twice)

IDEMPOTENT example:
  BAD:  "Add $10 to account"  → if run twice, adds $20 (wrong!)
  GOOD: "Set account balance to $110 (order #123)" → safe to run twice
```

### Dead Letter Queues — Where Failed Messages Go

```
What happens when a message fails repeatedly?

Without DLQ:
  Message fails → retry → fails → retry → fails → retry → infinite loop 😱

With DLQ (Dead Letter Queue):
  Message fails → retry 1 → fails → retry 2 → fails → retry 3 → fails
  → Message moved to Dead Letter Queue
  → Alert sent to team
  → Engineer inspects the message manually
  → Fixes the bug
  → Replays the message

┌──────────┐     ┌──────────────┐     ┌──────────┐
│ Producer │────→│  main-queue  │────→│ Consumer │
└──────────┘     └──────┬───────┘     └──────────┘
                        │ (after 3 failures)
                        ▼
                 ┌──────────────┐
                 │  dead-letter │  ← Messages that couldn't be processed
                 │  queue (DLQ) │  ← Engineers inspect these manually
                 └──────────────┘  ← Can be replayed after bug fix

This is a CRITICAL production pattern. Without it, you either:
  - Lose messages (bad)
  - Retry forever (bad, wastes resources)
  - Block the queue (bad, everything backs up)
```

### RabbitMQ vs Other Message Brokers

```
┌──────────────────┬──────────────────┬──────────────────┬──────────────────┐
│ Feature          │ RabbitMQ         │ AWS SQS          │ Apache Kafka     │
├──────────────────┼──────────────────┼──────────────────┼──────────────────┤
│ Type             │ Message broker   │ Message queue    │ Event streaming  │
│                  │ (smart broker)   │ (simple queue)   │ (log-based)      │
├──────────────────┼──────────────────┼──────────────────┼──────────────────┤
│ Hosting          │ Self-managed or  │ Fully managed    │ Self-managed or  │
│                  │ Amazon MQ        │ by AWS           │ Amazon MSK       │
├──────────────────┼──────────────────┼──────────────────┼──────────────────┤
│ Routing          │ Exchanges with   │ No routing       │ Topics with      │
│                  │ complex rules    │ (1 queue = 1     │ partitions       │
│                  │ (topic, fanout)  │  purpose)        │                  │
├──────────────────┼──────────────────┼──────────────────┼──────────────────┤
│ Message replay   │ No (consumed =   │ No (consumed =   │ Yes! Messages    │
│                  │ gone)            │ gone)            │ kept for days    │
├──────────────────┼──────────────────┼──────────────────┼──────────────────┤
│ Throughput       │ ~50K msg/sec     │ ~3K msg/sec      │ ~1M msg/sec      │
│                  │                  │ (standard)       │                  │
├──────────────────┼──────────────────┼──────────────────┼──────────────────┤
│ Ordering         │ Per-queue FIFO   │ Best-effort      │ Per-partition    │
│                  │                  │ (FIFO available) │ FIFO             │
├──────────────────┼──────────────────┼──────────────────┼──────────────────┤
│ Best for         │ Task queues,     │ Simple async     │ Event sourcing,  │
│                  │ complex routing, │ processing,      │ real-time data   │
│                  │ RPC patterns     │ serverless       │ pipelines, logs  │
├──────────────────┼──────────────────┼──────────────────┼──────────────────┤
│ Learning curve   │ Medium           │ Low              │ High             │
└──────────────────┴──────────────────┴──────────────────┴──────────────────┘

WHEN TO USE WHAT:
  RabbitMQ: Complex routing, task distribution, when you need exchanges
  SQS:      Simple queue, serverless (Lambda), don't want to manage infra
  Kafka:    High-throughput event streaming, need to replay events, data pipelines

YOUR COMPANY (AWS):
  SQS + SNS is the most common pattern:
    SNS (Simple Notification Service) = like a fanout exchange (broadcast)
    SQS (Simple Queue Service) = like a queue (each service gets its own)

  SNS + SQS together = "fan-out pattern":
    Event published to SNS topic
    → SNS delivers to SQS queue A (payment service)
    → SNS delivers to SQS queue B (email service)
    → SNS delivers to SQS queue C (analytics service)

  This is the AWS-native equivalent of what RabbitMQ does with exchanges.
```

### Redis Pub/Sub vs RabbitMQ — When to Use Which

```
┌──────────────────┬──────────────────────┬──────────────────────┐
│ Feature          │ Redis Pub/Sub        │ RabbitMQ             │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Persistence      │ NO — fire and forget │ YES — messages queued│
│                  │ If no one listens,   │ until consumed       │
│                  │ message is LOST      │                      │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Delivery         │ At-most-once         │ At-least-once        │
│ guarantee        │ (might lose msgs)    │ (guaranteed delivery)│
├──────────────────┼──────────────────────┼──────────────────────┤
│ Speed            │ Faster (~100K/sec)   │ Fast (~50K/sec)      │
├──────────────────┼──────────────────────┼──────────────────────┤
│ Use case         │ Real-time updates    │ Task processing      │
│                  │ (chat, live scores,  │ (orders, payments,   │
│                  │ notifications)       │ emails, reports)     │
├──────────────────┼──────────────────────┼──────────────────────┤
│ If consumer is   │ Message lost         │ Message waits in     │
│ offline          │                      │ queue                │
└──────────────────┴──────────────────────┴──────────────────────┘

RULE OF THUMB:
  "Can I afford to lose this message?"
    YES → Redis Pub/Sub (live dashboard updates, typing indicators)
    NO  → RabbitMQ/SQS (payments, orders, emails)
```

---

## CONCEPT 4: The Background Worker Pattern

### Why a Separate Worker Service?

```
WITHOUT a worker (everything in the API):

  POST /orders
       │
       ▼
  ┌─────────────┐
  │   API        │  1. Validate order (10ms)
  │              │  2. Save to DB (50ms)
  │              │  3. Process payment (2000ms)  ← USER IS WAITING
  │              │  4. Send email (500ms)         ← USER IS STILL WAITING
  │              │  5. Generate invoice PDF (1000ms) ← USER IS STILL WAITING
  └─────────────┘
       │
       ▼
  Return 200 OK (after 3560ms — user waited almost 4 seconds!)

  PROBLEMS:
  - User waits for ALL processing to finish (terrible UX)
  - If email service is slow, the entire request is slow
  - If PDF generation crashes, the order fails
  - API server is busy doing slow work instead of handling new requests


WITH a worker (API + background processing):

  POST /orders
       │
       ▼
  ┌─────────────┐
  │   API        │  1. Validate order (10ms)
  │              │  2. Save to DB (50ms)
  │              │  3. Publish "order.created" to RabbitMQ (5ms)
  └─────────────┘
       │
       ▼
  Return 202 Accepted (after 65ms — user barely noticed!)
  Response: { "orderId": "abc123", "status": "processing" }

  Meanwhile, in the background:

  ┌─────────────┐
  │   Worker     │  Picks up "order.created" message
  │              │  1. Process payment (2000ms)  ← user doesn't wait
  │              │  2. Send email (500ms)         ← user doesn't wait
  │              │  3. Generate invoice (1000ms)  ← user doesn't wait
  └─────────────┘

  The user can check order status later:
  GET /orders/abc123/status → { "status": "completed" }
```

### Scaling Workers Independently

```
docker compose up --scale worker=3

This starts 3 worker containers, all consuming from the same queue.

  RabbitMQ: demo-queue [msg1, msg2, msg3, msg4, msg5, msg6]
                          │      │      │      │      │      │
                          ▼      ▼      ▼      ▼      ▼      ▼
  Worker 1:             msg1   msg4                          (processes 2)
  Worker 2:             msg2   msg5                          (processes 2)
  Worker 3:             msg3   msg6                          (processes 2)

  With QoS(prefetchCount: 1), RabbitMQ distributes messages fairly.
  Fast workers get more messages. Slow workers don't get overwhelmed.

  Need to process messages faster? Add more workers.
  Need to save money? Scale down to 1 worker.
  The API doesn't change at all — it just publishes to the queue.
```

---

## CONCEPT 5: How This Maps to AWS

```
LOCAL (this project)              →  AWS PRODUCTION
════════════════════                 ═══════════════

Docker Compose                    →  ECS (Elastic Container Service)
  nginx container                 →  ALB (Application Load Balancer)
  api container                   →  ECS Fargate Task (API)
  worker container                →  ECS Fargate Task (Worker)
  redis container                 →  ElastiCache for Redis
  rabbitmq container              →  Amazon MQ for RabbitMQ (or SQS + SNS)

Docker network (frontend/backend) →  VPC subnets + security groups
Docker volumes                    →  EBS volumes / ElastiCache persistence
docker-compose.yml                →  CloudFormation / Terraform
localhost:8080                    →  ALB DNS name / Route 53 domain
localhost:15672                   →  Amazon MQ console / CloudWatch

SCALING:
  Local:  docker compose up --scale worker=3
  AWS:    ECS Service → desired count: 3 (or auto-scaling based on queue depth)

MONITORING:
  Local:  docker compose logs worker
  AWS:    CloudWatch Logs → /ecs/worker-service
          CloudWatch Metrics → SQS ApproximateNumberOfMessagesVisible
          CloudWatch Alarm → "If queue depth > 1000, scale up workers"
```

---

## Testing the Endpoints

Once everything is running, try these in order:

```bash
# 1. Check the API is working
curl http://localhost:8080

# 2. Check health (should show Redis and RabbitMQ are healthy)
curl http://localhost:8080/health

# 3. Set a value in Redis cache
curl -X POST "http://localhost:8080/cache/set?key=greeting&value=Hello+World"

# 4. Get it back (should be a cache HIT)
curl "http://localhost:8080/cache/get?key=greeting"

# 5. Try the cache-aside pattern (first call = slow, second = fast)
curl "http://localhost:8080/cache/demo-aside?userId=42"
curl "http://localhost:8080/cache/demo-aside?userId=42"

# 6. Publish a message to RabbitMQ
curl -X POST "http://localhost:8080/messages/publish?message=Hello+from+the+API"

# 7. Check the worker logs (should show it processed the message)
docker compose logs worker

# 8. Check queue status
curl "http://localhost:8080/messages/status"

# 9. Broadcast to multiple queues (fanout)
curl -X POST "http://localhost:8080/messages/broadcast?message=System+maintenance+tonight"

# 10. Topic-based routing (try different routing keys!)
curl -X POST "http://localhost:8080/messages/topic?routingKey=order.created&message=New+order"
curl -X POST "http://localhost:8080/messages/topic?routingKey=order.payment.failed&message=Payment+failed"
curl -X POST "http://localhost:8080/messages/topic?routingKey=user.registered&message=New+user"

# 11. Open RabbitMQ Management UI in your browser
# http://localhost:15672 (login: guest / guest)
# Go to "Queues" tab — see all queues and their message counts
# Go to "Exchanges" tab — see the exchanges we created
```

---

## CONCEPT 6: Verifying the Whole System End-to-End — From Frontend to Worker

> This is the section where you PROVE everything works together.
> Not just "curl returns 200" but actually watching data flow through
> Redis, RabbitMQ, and the Worker — the way you'd verify a real
> production job queue system.

---

### Step 0: Start Everything and Confirm Health

```bash
cd level4-dotnet-redis-messaging
docker compose up --build -d

# Wait ~30 seconds for RabbitMQ to fully start, then check:
docker compose ps

# You should see ALL 5 services as "healthy" or "running":
#   NAME       STATUS
#   nginx      running
#   api        healthy
#   worker     running
#   redis      healthy
#   rabbitmq   healthy

# If api shows "starting" or "unhealthy", wait and check again.
# RabbitMQ takes 20-30 seconds to boot — the API waits for it.
```

---

### Step 1: Verify Redis Caching (the Speed Difference You Can Feel)

```
WHAT YOU'RE PROVING:
  "Redis cache makes repeated requests 50x faster by skipping the database."

TEST IT:

  ┌─────────────────────────────────────────────────────────────────────┐
  │ CALL 1 — Cache MISS (first time, simulates database query):         │
  │                                                                     │
  │ curl "http://localhost:8080/cache/demo-aside?userId=42"             │
  │                                                                     │
  │ Response:                                                           │
  │ {                                                                   │
  │   "source": "database",          ← Fetched from DB (slow path)     │
  │   "data": {                                                         │
  │     "id": "42",                                                     │
  │     "name": "User 42",                                              │
  │     "email": "user42@example.com",                                  │
  │     "fetchedAt": "2026-04-19T..."                                   │
  │   },                                                                │
  │   "explanation": "Cache MISS. Fetched from database (200ms).        │
  │                   Result now cached for 10 minutes."                │
  │ }                                                                   │
  │                                                                     │
  │ Notice: source = "database". This request took ~200ms.              │
  ├─────────────────────────────────────────────────────────────────────┤
  │ CALL 2 — Cache HIT (same request, now served from Redis):           │
  │                                                                     │
  │ curl "http://localhost:8080/cache/demo-aside?userId=42"             │
  │                                                                     │
  │ Response:                                                           │
  │ {                                                                   │
  │   "source": "cache",             ← Served from Redis (fast path!)  │
  │   "data": { ... same data ... },                                    │
  │   "explanation": "Data served from Redis. Database was NOT hit.     │
  │                   Response time: ~1ms."                             │
  │ }                                                                   │
  │                                                                     │
  │ Notice: source = "cache". Database was NEVER queried.               │
  │ This is the entire point of caching.                                │
  └─────────────────────────────────────────────────────────────────────┘

VERIFY IN REDIS DIRECTLY (optional — peek inside the cache):

  docker exec -it level4-dotnet-redis-messaging-redis-1 redis-cli

  # Inside redis-cli:
  KEYS *                          # See all cached keys
  GET user:42                     # See the cached JSON
  TTL user:42                     # See seconds until expiry
  # Type "exit" to leave redis-cli

  You'll see the actual JSON blob sitting in Redis. This is what the API
  reads on the second call instead of hitting the database.
```

---

### Step 2: Verify RabbitMQ Message Publishing (API → Broker)

```
WHAT YOU'RE PROVING:
  "The API can publish messages to RabbitMQ, and they sit in a queue
   waiting to be consumed."

TEST IT:

  # Publish 3 messages
  curl -X POST "http://localhost:8080/messages/publish?message=Job+1+process+batch"
  curl -X POST "http://localhost:8080/messages/publish?message=Job+2+send+emails"
  curl -X POST "http://localhost:8080/messages/publish?message=Job+3+generate+report"

  # Check queue status
  curl "http://localhost:8080/messages/status"

  Response:
  {
    "queue": "demo-queue",
    "messageCount": 0,          ← Might be 0 if worker already consumed them!
    "consumerCount": 1,         ← The worker is connected and consuming
    "explanation": "..."
  }

  If messageCount is 0, that's GOOD — it means the worker is fast and
  already processed them. Check the worker logs to confirm:

  docker compose logs worker --tail 20

  You should see:
    worker-1 | Received message: {"content":"Job 1 process batch","publishedAt":"...","id":"..."}
    worker-1 | Successfully processed message: {"content":"Job 1 process batch",...}
    worker-1 | Received message: {"content":"Job 2 send emails",...}
    worker-1 | Successfully processed message: {"content":"Job 2 send emails",...}
    worker-1 | Received message: {"content":"Job 3 generate report",...}
    worker-1 | Successfully processed message: {"content":"Job 3 generate report",...}

  Each message was received, processed (1 second simulated work), and ACKed.
```

---

### Step 3: Verify with the RabbitMQ Management UI (the Visual Proof)

```
WHAT YOU'RE PROVING:
  "I can SEE the queues, messages, exchanges, and consumers in a real
   management dashboard — the same kind of dashboard you'd use in production."

OPEN IN YOUR BROWSER:
  http://localhost:15672
  Username: guest
  Password: guest

┌─────────────────────────────────────────────────────────────────────────┐
│ TAB 1: OVERVIEW                                                         │
│                                                                         │
│ Shows: message rates (published/sec, delivered/sec), connections,        │
│ channels, queues, consumers. This is your "is the system healthy?"      │
│ dashboard.                                                              │
│                                                                         │
│ WHAT TO LOOK FOR:                                                       │
│   - Message rates graph should show spikes when you publish             │
│   - Connections: at least 1 (the worker)                                │
│   - Queues: at least 1 (demo-queue)                                     │
├─────────────────────────────────────────────────────────────────────────┤
│ TAB 2: QUEUES (click "Queues and Streams" tab)                          │
│                                                                         │
│ You'll see all queues we created:                                       │
│                                                                         │
│ ┌─────────────────────────┬──────────┬───────────┬──────────┐          │
│ │ Name                    │ Messages │ Consumers │ State    │          │
│ ├─────────────────────────┼──────────┼───────────┼──────────┤          │
│ │ demo-queue              │ 0        │ 1         │ running  │          │
│ │ email-notifications     │ 1        │ 0         │ idle     │          │
│ │ sms-notifications       │ 1        │ 0         │ idle     │          │
│ │ slack-notifications     │ 1        │ 0         │ idle     │          │
│ │ order-processing        │ 0        │ 0         │ idle     │          │
│ │ payment-service         │ 0        │ 0         │ idle     │          │
│ │ audit-log               │ 0        │ 0         │ idle     │          │
│ │ analytics               │ 0        │ 0         │ idle     │          │
│ └─────────────────────────┴──────────┴───────────┴──────────┘          │
│                                                                         │
│ NOTICE:                                                                 │
│   - demo-queue has 1 consumer (the worker) and 0 messages (all consumed)│
│   - email/sms/slack-notifications have messages but 0 consumers         │
│     (we published via /broadcast but no worker consumes those queues)   │
│   - Click on any queue to see message details, publish test messages,   │
│     and purge the queue                                                 │
│                                                                         │
│ TRY THIS: Click on "email-notifications" → "Get messages" button        │
│ You'll see the actual JSON message sitting in the queue.                │
├─────────────────────────────────────────────────────────────────────────┤
│ TAB 3: EXCHANGES (click "Exchanges" tab)                                │
│                                                                         │
│ You'll see the exchanges we created:                                    │
│                                                                         │
│ ┌─────────────────────────┬──────────┐                                 │
│ │ Name                    │ Type     │                                 │
│ ├─────────────────────────┼──────────┤                                 │
│ │ (AMQP default)          │ direct   │  ← The default exchange        │
│ │ notifications           │ fanout   │  ← Our broadcast exchange      │
│ │ events                  │ topic    │  ← Our topic routing exchange   │
│ └─────────────────────────┴──────────┘                                 │
│                                                                         │
│ Click on "notifications" → see the 3 queue bindings (email, sms, slack)│
│ Click on "events" → see the 4 queue bindings with their patterns       │
│                                                                         │
│ This is the ROUTING MAP of your entire messaging system.                │
│ In production, this is how you debug "why didn't service X get the msg?"│
└─────────────────────────────────────────────────────────────────────────┘
```

---

### Step 4: Simulate a Real Job Queue Scenario (End-to-End)

```
SCENARIO: You're building a batch processing system (like your company's
batch-processing-service). A user submits a job, the API accepts it
immediately, and a worker processes it in the background.

STEP-BY-STEP WALKTHROUGH:

  ┌─────────────────────────────────────────────────────────────────────┐
  │ 1. STOP THE WORKER (simulate it being down or busy)                 │
  │                                                                     │
  │    docker compose stop worker                                       │
  │                                                                     │
  │    The worker container stops. No one is consuming from the queue.  │
  └─────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
  ┌─────────────────────────────────────────────────────────────────────┐
  │ 2. PUBLISH SEVERAL JOBS (API still works fine without the worker!)  │
  │                                                                     │
  │    curl -X POST "http://localhost:8080/messages/publish?message=Process+batch+A"
  │    curl -X POST "http://localhost:8080/messages/publish?message=Process+batch+B"
  │    curl -X POST "http://localhost:8080/messages/publish?message=Process+batch+C"
  │    curl -X POST "http://localhost:8080/messages/publish?message=Process+batch+D"
  │    curl -X POST "http://localhost:8080/messages/publish?message=Process+batch+E"
  │                                                                     │
  │    All 5 return 200 OK immediately. The API doesn't care that the   │
  │    worker is down — it just drops messages into RabbitMQ.           │
  └─────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
  ┌─────────────────────────────────────────────────────────────────────┐
  │ 3. CHECK THE QUEUE (messages are waiting)                           │
  │                                                                     │
  │    curl "http://localhost:8080/messages/status"                      │
  │                                                                     │
  │    Response:                                                        │
  │    {                                                                │
  │      "queue": "demo-queue",                                         │
  │      "messageCount": 5,        ← 5 messages waiting!               │
  │      "consumerCount": 0,       ← No consumers (worker is stopped)  │
  │      "explanation": "There are 5 messages waiting to be consumed    │
  │                      and 0 active consumers."                       │
  │    }                                                                │
  │                                                                     │
  │    ALSO CHECK THE UI: http://localhost:15672 → Queues tab           │
  │    demo-queue should show 5 messages, 0 consumers.                  │
  │    The messages are SAFE in RabbitMQ. Nothing is lost.              │
  └─────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
  ┌─────────────────────────────────────────────────────────────────────┐
  │ 4. START THE WORKER BACK UP (watch it drain the queue)              │
  │                                                                     │
  │    docker compose start worker                                      │
  │                                                                     │
  │    Now watch the worker logs in real-time:                          │
  │    docker compose logs worker -f                                    │
  │                                                                     │
  │    You'll see:                                                      │
  │    worker-1 | RabbitMQ is ready!                                    │
  │    worker-1 | Worker started. Waiting for messages on 'demo-queue'  │
  │    worker-1 | Received message: {"content":"Process batch A",...}   │
  │    worker-1 | Successfully processed message: ...                   │
  │    worker-1 | Received message: {"content":"Process batch B",...}   │
  │    worker-1 | Successfully processed message: ...                   │
  │    worker-1 | Received message: {"content":"Process batch C",...}   │
  │    worker-1 | Successfully processed message: ...                   │
  │    worker-1 | Received message: {"content":"Process batch D",...}   │
  │    worker-1 | Successfully processed message: ...                   │
  │    worker-1 | Received message: {"content":"Process batch E",...}   │
  │    worker-1 | Successfully processed message: ...                   │
  │                                                                     │
  │    All 5 messages processed! One per second (simulated work).       │
  │    Press Ctrl+C to stop following logs.                             │
  └─────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
  ┌─────────────────────────────────────────────────────────────────────┐
  │ 5. VERIFY THE QUEUE IS EMPTY                                        │
  │                                                                     │
  │    curl "http://localhost:8080/messages/status"                      │
  │                                                                     │
  │    Response:                                                        │
  │    {                                                                │
  │      "queue": "demo-queue",                                         │
  │      "messageCount": 0,        ← All consumed!                     │
  │      "consumerCount": 1,       ← Worker is back and listening      │
  │    }                                                                │
  │                                                                     │
  │    CHECK THE UI: http://localhost:15672 → Queues → demo-queue       │
  │    Message count should be 0. The "Message rates" graph should      │
  │    show a spike of deliveries when the worker came back.            │
  └─────────────────────────────────────────────────────────────────────┘

WHAT YOU JUST PROVED:
  ✓ The API works even when the worker is down (decoupled)
  ✓ Messages are persisted in RabbitMQ (durable queue, not lost)
  ✓ The worker processes all queued messages when it comes back
  ✓ You can monitor everything via the RabbitMQ Management UI
  ✓ This is EXACTLY how your company's batch-processing-service works
```

---

### Step 5: Verify Fanout Broadcasting (One Event → Multiple Services)

```
WHAT YOU'RE PROVING:
  "One event can be delivered to multiple services simultaneously."
  This is how notifications work — one event triggers email + SMS + Slack.

TEST IT:

  curl -X POST "http://localhost:8080/messages/broadcast?message=Deployment+complete+v2.1"

  Response:
  {
    "status": "broadcast",
    "exchange": "notifications",
    "exchangeType": "fanout",
    "deliveredTo": ["email-notifications", "sms-notifications", "slack-notifications"],
    "message": "Deployment complete v2.1"
  }

VERIFY IN THE UI:
  http://localhost:15672 → Queues tab

  All 3 notification queues should have 1 message each:
    email-notifications:  1 message
    sms-notifications:    1 message
    slack-notifications:  1 message

  Click on "email-notifications" → "Get messages" → you'll see the JSON.
  Same message in all 3 queues — that's fanout broadcasting.

  In production, each queue would have its own consumer service:
    email-notifications → Email Service (sends emails via SES)
    sms-notifications   → SMS Service (sends texts via SNS/Twilio)
    slack-notifications → Slack Service (posts to Slack webhook)
```

---

### Step 6: Verify Topic Routing (Smart Event Routing)

```
WHAT YOU'RE PROVING:
  "Different events get routed to different services based on patterns."
  This is event-driven architecture — services subscribe to events they care about.

TEST IT WITH DIFFERENT ROUTING KEYS:

  # Test 1: order.created → should go to order-processing, audit-log, analytics
  curl -X POST "http://localhost:8080/messages/topic?routingKey=order.created&message=New+order+123"

  # Test 2: order.payment.failed → should go to payment-service, audit-log, analytics
  curl -X POST "http://localhost:8080/messages/topic?routingKey=order.payment.failed&message=Payment+declined"

  # Test 3: user.registered → should go to analytics ONLY
  curl -X POST "http://localhost:8080/messages/topic?routingKey=user.registered&message=New+user+signup"

VERIFY IN THE UI:
  http://localhost:15672 → Queues tab

  After all 3 calls:
  ┌─────────────────────────┬──────────┬─────────────────────────────────┐
  │ Queue                   │ Messages │ Why                             │
  ├─────────────────────────┼──────────┼─────────────────────────────────┤
  │ order-processing        │ 1        │ Matched "order.*" (order.created)│
  │ payment-service         │ 1        │ Matched "order.payment.*"       │
  │ audit-log               │ 2        │ Matched "order.#" (both order   │
  │                         │          │ events, not user.registered)    │
  │ analytics               │ 3        │ Matched "#" (catches everything)│
  └─────────────────────────┴──────────┴─────────────────────────────────┘

  The analytics queue got ALL 3 messages (# matches everything).
  The audit-log got 2 (order.# matches both order events).
  The payment-service got 1 (order.payment.* only matches payment events).
  The order-processing got 1 (order.* only matches single-word after "order.").

  This is how your company's event-driven architecture works:
    order.created       → inventory reserves stock, email sends confirmation
    order.payment.failed → payment retries, customer support notified
    order.shipped       → tracking service updates, customer notified
```

---

### Step 7: Verify Worker Scaling (Parallel Processing)

```
WHAT YOU'RE PROVING:
  "Adding more workers processes messages faster, and RabbitMQ distributes
   work fairly across all workers."

TEST IT:

  # First, stop the worker and publish 10 messages
  docker compose stop worker

  for i in $(seq 1 10); do
    curl -X POST "http://localhost:8080/messages/publish?message=Job+$i"
  done

  # Verify 10 messages are queued
  curl "http://localhost:8080/messages/status"
  # → messageCount: 10, consumerCount: 0

  # Now start 3 workers instead of 1
  docker compose up -d --scale worker=3

  # Watch all worker logs
  docker compose logs worker -f

  You'll see messages distributed across 3 workers:
    worker-1 | Received: Job 1
    worker-2 | Received: Job 2
    worker-3 | Received: Job 3
    worker-1 | Received: Job 4    ← worker-1 finished first, gets next job
    worker-2 | Received: Job 5
    worker-3 | Received: Job 6
    ...

  With 1 worker:  10 jobs × 1 sec each = 10 seconds total
  With 3 workers: 10 jobs ÷ 3 workers  = ~4 seconds total

  CHECK THE UI: http://localhost:15672 → Queues → demo-queue
  Consumer count should show 3. Message rate graph shows faster consumption.

  # Scale back down when done
  docker compose up -d --scale worker=1
```

---

### Step 8: The Full Picture — How This Maps to Your Company's Job Queue

```
YOUR COMPANY'S BATCH-PROCESSING-SERVICE:

  ┌──────────────────────────────────────────────────────────────────────┐
  │                                                                      │
  │  What you built locally          What your company runs in AWS       │
  │  ════════════════════            ═══════════════════════════         │
  │                                                                      │
  │  curl → nginx → .NET API         Postman → ALB → ECS Task (API)     │
  │         │                                  │                         │
  │         ▼                                  ▼                         │
  │  RabbitMQ (demo-queue)           SQS Queue (batch-jobs)              │
  │         │                                  │                         │
  │         ▼                                  ▼                         │
  │  Worker container                ECS Task (Worker)                   │
  │         │                                  │                         │
  │         ▼                                  ▼                         │
  │  (simulated processing)          S3 read → process → DynamoDB write  │
  │                                                                      │
  │  Redis (caching)                 ElastiCache Redis (caching)         │
  │                                                                      │
  │  RabbitMQ UI (:15672)            CloudWatch Metrics + Alarms         │
  │                                  "If queue depth > 1000, page oncall"│
  │                                                                      │
  │  docker compose --scale=3        ECS Auto Scaling                    │
  │                                  "If queue depth > 100, add workers" │
  │                                                                      │
  └──────────────────────────────────────────────────────────────────────┘

  The ARCHITECTURE is identical. The INFRASTRUCTURE is different.
  That's the whole point of Docker and this learning progression:
    Level 2: Learn the app pattern locally
    Level 3: Learn production hardening locally
    Level 4: Learn distributed systems locally
    Production: Same patterns, managed AWS services instead of Docker containers
```

---

### Cleanup

```bash
# Stop everything
docker compose down

# Stop and delete all data (Redis cache, RabbitMQ messages, volumes)
docker compose down -v
```

---

## Quick Summary: The Level 4 Jump

```
Level 1: "I can build and run a container"
Level 2: "I can run multiple services together"
Level 3: "I can run this reliably in production"
Level 4: "I can build distributed systems with async processing"
         .NET (compiled, typed, enterprise-grade)
         Redis (caching patterns, pub/sub, data structures)
         RabbitMQ (message routing, guaranteed delivery, worker scaling)
         Background workers (producer-consumer pattern)
         Event-driven architecture (loose coupling between services)
```

The jump from Level 3 to Level 4 is where you go from "a single app that handles
everything synchronously" to "a distributed system where services communicate
asynchronously through messages." This is how real production systems at scale work.
