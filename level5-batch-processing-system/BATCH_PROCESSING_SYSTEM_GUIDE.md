# Level 5 — Batch Processing System: .NET 9, PostgreSQL, Redis, RabbitMQ, React UI

> A fully working local replica of a production batch-processing-service.
> Every technology choice is explained with interview-ready depth.
> Run it, break it, inspect it, explain it in an interview.

---

## Table of Contents

1. [What We Built and Why](#1-what-we-built-and-why)
2. [The Domain — What This System Does](#2-the-domain)
3. [Architecture — How the Pieces Fit Together](#3-architecture)
4. [Why Each Technology Was Chosen](#4-why-each-technology-was-chosen)
5. [PostgreSQL + Entity Framework — The Source of Truth](#5-postgresql--entity-framework--the-source-of-truth)
6. [Redis — Caching Layer Deep Dive](#6-redis--caching-layer-deep-dive)
7. [RabbitMQ — Event-Driven Messaging Deep Dive](#7-rabbitmq--event-driven-messaging-deep-dive)
7.5. [Polling vs Event-Driven — What We Changed and What We Couldn't](#75-polling-vs-event-driven--what-we-changed-and-what-we-couldnt)
8. [Background Workers — The Consumer Pattern (Strategy + Grouping)](#8-background-workers--the-consumer-pattern-strategy--grouping)
9. [The React UI — Frontend Verification](#9-the-react-ui--frontend-verification)
10. [End-to-End Verification Walkthrough](#10-end-to-end-verification-walkthrough)
11. [Interview Questions and Answers](#11-interview-questions-and-answers)

---

## 1. What We Built and Why

A **7-container** system that models a real batch processing pipeline:

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          YOUR MACHINE                                    │
│                                                                          │
│   http://localhost:8080          → React Dashboard (via Nginx)           │
│   http://localhost:8080/api/...  → .NET API (via Nginx)                 │
│   http://localhost:15672         → RabbitMQ Management UI               │
│                                                                          │
│              ┌──────────┐                                                │
│              │  NGINX   │  ← Routes /api/* to API, /* to React UI       │
│              └────┬─────┘                                                │
│           ┌───────┴────────┐                                             │
│     ┌─────┴──────┐   ┌────┴────┐                                        │
│     │  .NET API  │   │ React   │                                        │
│     │  (C#)      │   │ UI      │                                        │
│     └──┬────┬────┘   └─────────┘                                        │
│        │    │    │                                                        │
│   ┌────┴┐ ┌┴────┴──┐ ┌──────────┐                                      │
│   │ DB  │ │ REDIS  │ │ RABBITMQ │                                       │
│   │Pg16 │ │ Cache  │ │ Events   │                                       │
│   └─────┘ └────────┘ └────┬─────┘                                       │
│                            │                                             │
│                      ┌─────┴──────┐                                      │
│                      │  WORKER    │  ← Consumes events, sends emails    │
│                      └────────────┘                                      │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## 2. The Domain

This system models a **batch report processing pipeline**. Here's the real-world scenario:

```
A media company tracks TV audience data. Every day, new audience data arrives
for different "panels" (markets — Australia, UK, etc.). When new data arrives,
the system automatically runs reports that clients have configured.

THE ENTITIES:

  Report
    "Weekly Audience Summary" — a report template uploaded by a user.
    Think of it as an Excel template that gets filled with fresh data.

  DataTriggerDefinition
    "Every time new AU data arrives, run the Weekly Audience Summary
     in CSV format and email it to finance@example.com."
    This is the CONFIG — what to run, how to format it, where to send it.

  Dataset
    "January 2026 AU audience data" — a snapshot of data that arrived.
    When this lands, all triggers for the AU panel fire.

  DataTriggerExecution
    "On Jan 15, the Finance Team Weekly Report trigger fired against
     the January 2026 dataset. Status: Success. Took 30 seconds."
    This is ONE RUN of a trigger. History/audit trail.

  JobQueue
    "Execution abc123 is waiting for a report runner to pick it up."
    This is the WORK QUEUE. Runners poll for jobs, process them, report back.

THE RELATIONSHIPS:

  Report (1) ──→ (N) DataTriggerDefinition
    One report can have many triggers (different clients, different schedules)

  DataTriggerDefinition (1) ──→ (N) DataTriggerExecution
    One trigger fires many times (once per dataset arrival)

  DataTriggerExecution (1) ──→ (1) JobQueue
    Each execution creates one job. Job is deleted when complete.

  Dataset (1) ──→ (N) DataTriggerExecution
    One dataset triggers many executions (all triggers for that panel)
```

---

## 3. Architecture

### The Pipeline Flow

```
STEP 1: New data arrives (or user clicks "Run" in the UI)
         │
         ▼
STEP 2: API creates:
         ├── Dataset record (what data triggered this)
         ├── DataTriggerExecution record (status: Pending)
         └── JobQueue record (waiting for a runner)
         │
         ▼
STEP 3: API publishes "execution.created" event to RabbitMQ
         │
         ├──→ audit-log queue (logs everything)
         │
         ▼
STEP 4: A report runner (or you via the UI) calls GET /api/v1/jobs/next-job
         ├── Dequeues the oldest pending job
         ├── Marks it as started (DateExecutionStarted = now)
         └── Returns report metadata (name, format, timeout)
         │
         ▼
STEP 5: Runner processes the report (simulated as 2-second delay in UI)
         │
         ▼
STEP 6: Runner calls POST /api/v1/jobs/{id}/complete
         ├── Updates execution: status → Success, timestamps, paths
         ├── Deletes the JobQueue row (work is done)
         └── Publishes "job.completed" event to RabbitMQ
         │
         ├──→ result-orchestrator queue (sends emails, exports)
         ├──→ audit-log queue (logs everything)
         │
         ▼
STEP 7: Worker consumes "job.completed" event
         ├── Sends email notification (simulated)
         ├── Publishes to export channel (simulated)
         └── Logs completion
```

### Where Redis Fits

```
Redis is NOT in the main pipeline. It's a PERFORMANCE OPTIMIZATION layer
that sits alongside PostgreSQL:

  WITHOUT Redis:
    GET /api/v1/triggers → PostgreSQL query (5-50ms every time)
    GET /api/v1/jobs/statistics → PostgreSQL COUNT(*) (10-100ms every time)
    GET /api/v1/triggers/{id} → PostgreSQL query (5-20ms every time)

  WITH Redis:
    GET /api/v1/triggers → Redis GET (0.1ms) or PostgreSQL (5ms, then cached)
    GET /api/v1/jobs/statistics → Redis GET (0.1ms) or PostgreSQL (10ms, then cached)
    GET /api/v1/triggers/{id} → Redis GET (0.1ms) or PostgreSQL (5ms, then cached)

  The UI polls /jobs/statistics every 5 seconds. Without Redis, that's
  a COUNT(*) query on the JobQueue table every 5 seconds from every
  connected browser tab. With Redis, it's a 0.1ms cache hit.
```

### Where RabbitMQ Fits

```
RabbitMQ DECOUPLES the pipeline steps. Without it, the API would have to:
  1. Create execution + job (database work)
  2. Send email notification (slow, might fail)
  3. Publish to export channel (slow, might fail)
  4. Log to audit system (might fail)
  ALL IN ONE HTTP REQUEST. If email fails, does the whole request fail?

With RabbitMQ:
  1. API creates execution + job (database work) — 50ms
  2. API publishes event to RabbitMQ — 5ms
  3. API returns 202 Accepted — user sees response in 55ms
  4. Worker handles email, export, logging ASYNCHRONOUSLY
     If email fails, the message stays in the queue and gets retried.
     The user's request already succeeded.
```

---

## 4. Why Each Technology Was Chosen

### Why PostgreSQL (not MySQL, SQLite, or DynamoDB)?

```
┌──────────────────┬──────────────┬──────────────┬──────────────┬──────────────┐
│ Need             │ PostgreSQL   │ MySQL        │ SQLite       │ DynamoDB     │
├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
│ FOR UPDATE       │ ✓ SKIP       │ ✓ Basic      │ ✗ No         │ ✗ No SQL     │
│ SKIP LOCKED      │   LOCKED     │   (no SKIP)  │              │              │
│ (job queue)      │   (critical!)│              │              │              │
├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
│ JSONB columns    │ ✓ Native     │ ✗ JSON only  │ ✗ Text only  │ ✓ Native     │
│ (trigger attrs)  │   (indexed!) │   (no index) │              │   (document) │
├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
│ Advisory locks   │ ✓ Built-in   │ ✗ No         │ ✗ No         │ ✗ No         │
│ (single-leader)  │              │              │              │              │
├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
│ Partial indexes  │ ✓ Yes        │ ✗ No         │ ✗ No         │ ✗ No         │
│ (pending jobs)   │              │              │              │              │
├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
│ AWS managed      │ RDS/Aurora   │ RDS/Aurora   │ ✗ No         │ Native       │
└──────────────────┴──────────────┴──────────────┴──────────────┴──────────────┘

THE KILLER FEATURE: FOR UPDATE SKIP LOCKED

  This is how the job queue works. When 10 runners call GET /next-job
  simultaneously:

    SELECT * FROM job_queue
    WHERE date_execution_started IS NULL
    ORDER BY job_queue_id ASC
    LIMIT 1
    FOR UPDATE SKIP LOCKED;

  FOR UPDATE = lock this row (no one else can grab it)
  SKIP LOCKED = if a row is already locked, skip it and grab the next one

  Without SKIP LOCKED (MySQL): runners would BLOCK waiting for the lock.
  With SKIP LOCKED (PostgreSQL): runners instantly get different jobs. No waiting.

  This is why your company uses PostgreSQL for the batch processing service.
```

### Why Redis (not Memcached, in-process cache, or no cache)?

```
THE QUESTION: "Why not just query PostgreSQL every time?"

Let's do the math for the /jobs/statistics endpoint:

  Your React UI polls this every 5 seconds.
  10 browser tabs open = 10 requests every 5 seconds = 2 requests/second.
  Each request runs: SELECT COUNT(*) FROM job_queue (full table scan).

  In production with 50 users watching the dashboard:
    50 users × 1 request/5sec = 10 COUNT(*) queries per second
    On a table that runners are constantly locking with FOR UPDATE SKIP LOCKED.
    That's contention. That's slow. That's unnecessary.

  With Redis:
    First request → PostgreSQL COUNT(*) → result cached in Redis for 10 seconds
    Next 99 requests in those 10 seconds → Redis GET → 0.1ms, zero DB load
    PostgreSQL is free to handle actual job dequeue operations.

WHERE WE CACHE IN THIS SYSTEM:

  ┌─────────────────────────┬──────────┬──────────────────────────────────────┐
  │ Endpoint                │ TTL      │ Why                                  │
  ├─────────────────────────┼──────────┼──────────────────────────────────────┤
  │ GET /triggers           │ 2 min    │ Trigger list changes rarely.         │
  │                         │          │ 100 UI loads = 1 DB query.           │
  ├─────────────────────────┼──────────┼──────────────────────────────────────┤
  │ GET /triggers/{id}      │ 5 min    │ Single trigger even more stable.     │
  │                         │          │ Cached per-trigger for fast lookups. │
  ├─────────────────────────┼──────────┼──────────────────────────────────────┤
  │ GET /jobs/statistics    │ 10 sec   │ Short TTL because stats change fast. │
  │                         │          │ But 10 sec is enough to absorb       │
  │                         │          │ 50 concurrent dashboard polls.       │
  └─────────────────────────┴──────────┴──────────────────────────────────────┘

WHERE WE INVALIDATE:

  POST /triggers (create)     → DELETE "triggers:all"
  PUT  /triggers/{id} (update)→ DELETE "triggers:all" + DELETE "trigger:{id}"
  DELETE /triggers/{id}       → DELETE "triggers:all" + DELETE "trigger:{id}"
  POST /triggers/{id}/run     → DELETE "jobs:statistics"
  POST /jobs/{id}/complete    → DELETE "jobs:statistics"

  This is the CACHE-ASIDE pattern with EXPLICIT INVALIDATION:
    Read:  check Redis → miss → query DB → store in Redis → return
    Write: update DB → delete from Redis (next read will re-cache)

WHY NOT MEMCACHED:
  We also use Redis for cache key inspection (GET /cache/keys endpoint).
  Redis supports KEYS, TTL, TYPE commands for debugging.
  Memcached has no equivalent — you can't list what's cached.
  For a learning system, visibility into the cache is essential.

WHY NOT IN-PROCESS CACHE (e.g., IMemoryCache):
  If you scale the API to 3 instances behind a load balancer:
    Instance 1 caches triggers → user updates a trigger on Instance 2
    → Instance 1 still serves stale data until its local cache expires.
  Redis is shared across all instances. One invalidation clears it for everyone.
```

### Why RabbitMQ (not SQS, Kafka, or just polling the DB)?

```
YOUR REAL SYSTEM TODAY: The ResultOrchestratorWorker POLLS the database:

  while (!stoppingToken.IsCancellationRequested)
  {
      var executions = await repository.FetchAndLockTerminalExecutionsAsync(ct);
      // process...
      await Task.Delay(pollingInterval, stoppingToken);
  }

  Every N seconds: "Hey database, any completed executions I should process?"
  Database: "Nope." (99% of the time)
  Database: "Nope."
  Database: "Nope."
  Database: "Yes, here's one!" (1% of the time)

  That's a lot of wasted queries. And there's a delay — if polling interval
  is 30 seconds, a completed job waits up to 30 seconds before email is sent.

WITH RABBITMQ (what Level 5 does):

  Job completes → API publishes "job.completed" event → Worker gets it INSTANTLY

  No polling. No wasted queries. No delay.
  The worker is idle until a message arrives, then processes it immediately.

  ┌──────────────────────────────────────────────────────────────────────┐
  │ POLLING (your current system)     │ EVENT-DRIVEN (Level 5)          │
  ├───────────────────────────────────┼─────────────────────────────────┤
  │ Worker queries DB every 30 sec    │ Worker sleeps until event       │
  │ 99% of queries return nothing     │ 0% wasted work                 │
  │ Up to 30 sec delay after job done │ Sub-second delay                │
  │ DB load increases with more       │ DB load stays constant          │
  │ workers polling                   │ (events go through RabbitMQ)    │
  │ Simple to implement               │ Slightly more infrastructure    │
  └───────────────────────────────────┴─────────────────────────────────┘

WHY NOT AWS SQS:
  Can't run locally. This is a learning project — you need to see queues,
  messages, and consumers in the RabbitMQ Management UI at localhost:15672.
  SQS is the right choice in production on AWS, but not for local learning.

WHY NOT KAFKA:
  Overkill. Kafka is for high-throughput event streaming (millions of events/sec).
  A batch processing system handles hundreds of jobs/day, not millions/second.
  Kafka also needs ZooKeeper + broker + schema registry (3+ containers, 2GB RAM).
  RabbitMQ is one container, 512MB, and teaches the same messaging concepts.

WHY NOT JUST POLLING (keep the current approach):
  Polling works. Your production system proves it. But this is Level 5 —
  the goal is to learn event-driven architecture. Understanding RabbitMQ
  makes you dangerous in system design interviews where "how would you
  decouple these services?" is a standard question.
```

### Why .NET 9 (not Python/Flask again)?

```
Already covered in Level 4's guide, but the short version for this system:

  1. YOUR REAL CODEBASE IS .NET — BatchProcessing.Service is a .NET project.
     Level 5 mirrors it so you can explain the real code in interviews.

  2. Entity Framework Core — the ORM that maps C# classes to PostgreSQL tables.
     Your real system uses it. Level 5 uses it. Same patterns, same code.

  3. BackgroundService — .NET's built-in pattern for long-running workers.
     Your real DatasetMonitorWorker, ResultOrchestratorWorker, and
     JobMaintenanceWorker all extend BackgroundService. Level 5's worker does too.

  4. Dependency Injection — .NET's built-in DI container. Your real system
     registers services with AddScoped/AddSingleton. Level 5 does the same.
```

### Why a React UI (not just curl)?

```
  curl is great for testing individual endpoints. But it can't show you:
    - Queue depth changing in real-time as jobs are created and consumed
    - Execution status transitioning from Pending → Success
    - Redis cache keys appearing and expiring
    - The full pipeline flow from trigger → execution → job → completion

  The React dashboard gives you a VISUAL PROOF that the entire system works.
  You click "Run ▶" on a trigger and WATCH:
    1. Activity log: "Running trigger: Finance Team Weekly Report..."
    2. Activity log: "✓ Execution created: abc123 (Job #1)"
    3. Queue stats: Total Queued goes from 0 → 1
    4. You click "Dequeue & Process Job"
    5. Activity log: "✓ Dequeued Job #1: Weekly Audience Summary (CSV)"
    6. Activity log: "Processing Job #1..."
    7. 2 seconds later: "✓ Job #1 completed successfully"
    8. Queue stats: Total Queued goes from 1 → 0
    9. Execution history: status changes from Pending → Success

  This is the kind of demo you can show in an interview:
  "Here's the system running. Watch me trigger a report, see it queue,
   process it, and watch the worker send the notification."
```

### Why Nginx (not expose the API directly, not Traefik, not AWS ALB locally)?

```
WHAT IS NGINX?
  Nginx (pronounced "engine-x") is a reverse proxy and web server.
  It sits BETWEEN the internet (your browser) and your application.

  Browser → Nginx → Your App

  Without Nginx:
    Browser → Your App (directly exposed)

  Think of Nginx as a RECEPTIONIST at a building entrance:
    - Checks who you are (authentication, rate limiting)
    - Directs you to the right floor (routing: /api/* → API, /* → UI)
    - Handles multiple visitors simultaneously (10,000+ concurrent connections)
    - Doesn't bother the workers with trivial requests (serves static files itself)

WHY WE CHOSE NGINX (not alternatives):

  ┌──────────────────┬──────────────┬──────────────┬──────────────┬──────────────┐
  │ Need             │ Nginx        │ Traefik      │ No proxy     │ Caddy        │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Static file      │ ✓ Blazing    │ ✗ Not its    │ ✗ App does   │ ✓ Good       │
  │ serving          │   fast (C)   │   job        │   it (slow)  │              │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Reverse proxy    │ ✓ Industry   │ ✓ Good       │ ✗ N/A        │ ✓ Good       │
  │                  │   standard   │              │              │              │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ SSL termination  │ ✓ Built-in   │ ✓ Auto HTTPS │ ✗ Manual     │ ✓ Auto HTTPS │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Load balancing   │ ✓ Built-in   │ ✓ Built-in   │ ✗ N/A        │ ✓ Built-in   │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Learning value   │ ★★★★★        │ ★★★☆☆        │ ★☆☆☆☆        │ ★★★☆☆        │
  │ (industry usage) │ #1 worldwide │ K8s focused  │              │ Newer        │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Config style     │ File-based   │ Labels/API   │ N/A          │ Caddyfile    │
  │                  │ (nginx.conf) │ (dynamic)    │              │              │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ AWS equivalent   │ ALB / NLB    │ ALB / NLB    │ Direct ECS   │ ALB / NLB    │
  └──────────────────┴──────────────┴──────────────┴──────────────┴──────────────┘

WHY IT FITS OUR USE CASE:

  1. ROUTING: We have TWO frontends behind one port (8080):
     /api/*  → .NET API container (port 8080)
     /*      → React UI container (port 80)
     Nginx handles this with two location blocks. Without it, you'd need
     two separate ports (8080 for API, 3000 for UI) — confusing for users.

  2. SECURITY: Only Nginx is exposed to your machine. The API, UI, Redis,
     RabbitMQ, PostgreSQL — none of them have ports mapped. Even if someone
     finds your machine's IP, they can only reach Nginx. Defense in depth.

  3. PRODUCTION PARITY: In AWS, your ALB does exactly what Nginx does here:
     routes /api/* to ECS API tasks, /* to S3/CloudFront for the frontend.
     Learning Nginx locally = understanding ALB routing in production.

  4. INTERVIEW: "Why don't you expose Kestrel (.NET's built-in server) directly?"
     Kestrel is fast but not hardened for direct internet exposure. It doesn't
     handle slow clients well, doesn't serve static files efficiently, and
     doesn't do rate limiting. Nginx handles all of that, letting Kestrel
     focus on running your C# code.

WHAT OUR NGINX DOES (from nginx.conf):

  events { worker_connections 1024; }    ← Handle up to 1024 simultaneous connections

  http {
    upstream dotnet_api { server api:8080; }   ← "api" = Docker service name

    server {
      listen 80;

      location /api/ { proxy_pass http://dotnet_api; }   ← API routes
      location /health { proxy_pass http://dotnet_api; } ← Health check
      location / { proxy_pass http://ui:80; }            ← Everything else → React
    }
  }
```

### Why Docker & Docker Compose (not VMs, not bare metal, not Kubernetes)?

```
WHAT IS DOCKER?
  Docker packages your application + all its dependencies into a single
  portable unit called a CONTAINER. The container runs identically on
  your laptop, your colleague's laptop, and the production server.

  Without Docker: "It works on my machine" → doesn't work in production
  With Docker:    Same container everywhere → works everywhere

WHAT IS DOCKER COMPOSE?
  Docker Compose lets you define and run MULTIPLE containers together
  with one command. Our system has 7 containers — starting them manually
  with 7 separate `docker run` commands would be painful.

  docker compose up = start all 7 containers with networking, volumes,
  health checks, dependencies, and resource limits. One command.

WHY WE CHOSE DOCKER COMPOSE (not alternatives):

  ┌──────────────────┬──────────────┬──────────────┬──────────────┬──────────────┐
  │ Need             │ Docker       │ Kubernetes   │ VMs          │ Bare metal   │
  │                  │ Compose      │ (K8s)        │ (Vagrant)    │ (install     │
  │                  │              │              │              │ everything)  │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Local dev setup  │ ✓ One command│ ✗ Overkill   │ ✗ Heavy      │ ✗ Hours of   │
  │                  │   30 seconds │   (minikube) │   (GBs RAM)  │   setup      │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Multi-container  │ ✓ Built for  │ ✓ Built for  │ ✗ Manual     │ ✗ Manual     │
  │ orchestration    │   this       │   this       │              │              │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Production-ready │ ✗ Single host│ ✓ Multi-host │ ✓ Multi-host │ ✓ Multi-host │
  │                  │   only       │   auto-scale │              │              │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Learning curve   │ ★★☆☆☆        │ ★★★★★        │ ★★★☆☆        │ ★★★★☆        │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ AWS equivalent   │ ECS (single  │ EKS          │ EC2          │ EC2          │
  │                  │ task def)    │              │              │              │
  └──────────────────┴──────────────┴──────────────┴──────────────┴──────────────┘

WHY IT FITS OUR USE CASE:

  1. REPRODUCIBILITY: You ran `docker compose up --build` and got the exact
     same system I designed — PostgreSQL 16, Redis 7, RabbitMQ 3.13, .NET 9.
     No "install PostgreSQL, configure it, create the database, install Redis..."
     One command. Done.

  2. ISOLATION: Each service runs in its own container with its own filesystem.
     Redis can't accidentally read PostgreSQL's files. The worker can't access
     the API's memory. This mirrors production where each service runs on
     separate ECS tasks.

  3. NETWORKING: Docker Compose creates a private network. Containers find
     each other by service name ("redis", "db", "rabbitmq"). This is the same
     pattern as ECS Service Discovery or Kubernetes DNS.

  4. DISPOSABILITY: `docker compose down -v` wipes everything. Fresh start.
     No leftover state from previous experiments. This is critical for learning —
     you can break things and reset in 5 seconds.

  5. PRODUCTION PATH: docker-compose.yml → CloudFormation/Terraform.
     The concepts map directly:
       services → ECS Task Definitions
       networks → VPC Subnets + Security Groups
       volumes → EBS Volumes / EFS
       ports → ALB Target Groups
       depends_on → ECS Service Dependencies
       healthcheck → ECS Health Checks
```

### Why Entity Framework Core (not raw SQL, not Dapper, not a different ORM)?

```
WHAT IS ENTITY FRAMEWORK CORE (EF CORE)?
  EF Core is an ORM (Object-Relational Mapper) for .NET. It lets you
  work with database tables as C# objects instead of writing raw SQL.

  Without ORM:
    var sql = "SELECT * FROM data_trigger_definition WHERE \"IsActive\" = true";
    var reader = await command.ExecuteReaderAsync();
    while (reader.Read()) { /* manually map columns to objects */ }

  With EF Core:
    var triggers = await db.DataTriggerDefinitions
        .Where(t => t.IsActive)
        .ToListAsync();
    // Returns List<DataTriggerDefinition> — fully typed, no manual mapping

WHY WE CHOSE EF CORE (not alternatives):

  ┌──────────────────┬──────────────┬──────────────┬──────────────┬──────────────┐
  │ Need             │ EF Core      │ Dapper       │ Raw SQL      │ NHibernate   │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Type safety      │ ✓ Compile-   │ ~ Semi       │ ✗ Strings    │ ✓ Compile-   │
  │                  │   time checks│   (manual)   │   (runtime)  │   time       │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Migrations       │ ✓ Built-in   │ ✗ Manual     │ ✗ Manual     │ ✓ Built-in   │
  │ (schema changes) │   (codegen)  │              │              │              │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ LINQ queries     │ ✓ Full LINQ  │ ✗ Raw SQL    │ ✗ Raw SQL    │ ✓ HQL/LINQ   │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Performance      │ Good (95%    │ ✓ Fastest    │ ✓ Fastest    │ Good         │
  │                  │   of Dapper) │   (thin)     │   (no ORM)   │              │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Query filters    │ ✓ Global     │ ✗ Manual     │ ✗ Manual     │ ✓ Filters    │
  │ (soft delete)    │   (automatic)│              │              │              │
  ├──────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
  │ Learning value   │ ★★★★★        │ ★★★☆☆        │ ★★☆☆☆        │ ★★☆☆☆        │
  │ (industry usage) │ .NET default │ Micro-ORM    │ Legacy       │ Declining    │
  └──────────────────┴──────────────┴──────────────┴──────────────┴──────────────┘

WHY IT FITS OUR USE CASE:

  1. YOUR REAL CODEBASE USES IT: BatchProcessing.Data uses EF Core with
     Npgsql. Same DbContext pattern, same repository pattern, same migrations.
     Learning EF Core here = reading your production code fluently.

  2. QUERY FILTERS FOR SOFT DELETE: We define once:
       modelBuilder.Entity<DataTriggerDefinition>()
           .HasQueryFilter(d => d.DateDeleted == null);
     Now EVERY query automatically excludes deleted triggers. No risk of
     accidentally returning deleted data. Your real system does this too.

  3. SEED DATA: EF Core's HasData() lets us pre-populate the database with
     test triggers and reports. The system works out of the box — no manual
     SQL scripts needed.

  4. NAVIGATION PROPERTIES: `trigger.Report.DisplayName` — EF Core handles
     the JOIN automatically. In raw SQL, you'd write the JOIN yourself every time.

  5. INTERVIEW: "When would you NOT use EF Core?"
     For the job dequeue query (FOR UPDATE SKIP LOCKED), we'd use raw SQL
     in production because EF Core can't express PostgreSQL-specific locking
     hints. The real system does exactly this — EF Core for CRUD, raw SQL
     for the performance-critical dequeue operation.
```

---

## 5. PostgreSQL + Entity Framework — The Source of Truth

### The Database Schema

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        ENTITY RELATIONSHIP DIAGRAM                       │
│                                                                          │
│  ┌──────────────┐     ┌──────────────────────┐     ┌──────────────────┐ │
│  │   Report      │     │ DataTriggerDefinition │     │    Dataset       │ │
│  │──────────────│     │──────────────────────│     │────────────────│ │
│  │ ReportId (PK) │◄────│ ReportId (FK)         │     │ DatasetId (PK)  │ │
│  │ DisplayName   │  1:N│ DataTriggerId (PK)    │     │ PanelId         │ │
│  │ InternalName  │     │ TriggerName           │     │ ExternalId      │ │
│  │ PanelId       │     │ PanelId               │     │ DateCreated     │ │
│  │ AvgExecTime   │     │ IsActive              │     └────────┬───────┘ │
│  │ DateDeleted   │     │ OutputFormat           │              │         │
│  └──────────────┘     │ Frequency              │              │         │
│                        │ EmailEnabled           │              │         │
│                        │ EmailTo                │              │         │
│                        │ Priority               │              │         │
│                        │ DateCreated            │              │         │
│                        │ DateDeleted (soft del) │              │         │
│                        └──────────┬─────────────┘              │         │
│                                   │ 1:N                        │ 1:N     │
│                                   ▼                            ▼         │
│                        ┌──────────────────────┐                          │
│                        │ DataTriggerExecution  │                          │
│                        │──────────────────────│                          │
│                        │ ExecutionId (PK)      │                          │
│                        │ InvocationId          │                          │
│                        │ DataTriggerId (FK)    │                          │
│                        │ DatasetId (FK)        │                          │
│                        │ ResultStatus          │                          │
│                        │ DateCreated           │                          │
│                        │ DateExecStarted       │                          │
│                        │ DateExecCompleted      │                          │
│                        │ ExecutionTimeSeconds   │                          │
│                        │ ResultPath             │                          │
│                        │ LogPath                │                          │
│                        │ EmailSent              │                          │
│                        └──────────┬─────────────┘                          │
│                                   │ 1:1                                    │
│                                   ▼                                        │
│                        ┌──────────────────────┐                            │
│                        │     JobQueue          │                            │
│                        │──────────────────────│                            │
│                        │ JobQueueId (PK, auto) │                            │
│                        │ ExecutionId (FK)      │                            │
│                        │ PanelId               │                            │
│                        │ DateCreated           │                            │
│                        │ DateExecStarted       │                            │
│                        └──────────────────────┘                            │
│                                                                            │
│  LIFECYCLE:                                                                │
│    JobQueue row is CREATED when a trigger fires.                           │
│    JobQueue row is DELETED when the runner completes the job.              │
│    Completed state lives in DataTriggerExecution, not JobQueue.            │
│    This keeps the JobQueue table small and fast for dequeue queries.       │
└────────────────────────────────────────────────────────────────────────────┘
```

### Entity Framework — How C# Maps to SQL

```csharp
// C# entity class:
[Table("data_trigger_definition")]
public class DataTriggerDefinition
{
    [Key]
    public Guid DataTriggerId { get; set; }
    public Guid ReportId { get; set; }
    public string TriggerName { get; set; }
    // ...

    [ForeignKey(nameof(ReportId))]
    public Report Report { get; set; }  // Navigation property
}
```

```sql
-- What EF generates in PostgreSQL:
CREATE TABLE data_trigger_definition (
    "DataTriggerId" uuid PRIMARY KEY,
    "ReportId" uuid NOT NULL REFERENCES report("ReportId"),
    "TriggerName" varchar(256) NOT NULL,
    -- ...
);
```

```
INTERVIEW: "How does Entity Framework work?"

  1. You define C# classes with [Table], [Key], [ForeignKey] attributes
  2. EF reads these at startup and builds an internal model of your schema
  3. When you write LINQ queries, EF translates them to SQL:

     C#:   db.DataTriggerDefinitions
              .Include(t => t.Report)
              .Where(t => t.IsActive)
              .OrderBy(t => t.Priority)
              .ToListAsync();

     SQL:  SELECT d.*, r.*
           FROM data_trigger_definition d
           JOIN report r ON d."ReportId" = r."ReportId"
           WHERE d."IsActive" = true AND d."DateDeleted" IS NULL
           ORDER BY d."Priority" ASC;

  4. The "DateDeleted IS NULL" comes from the QUERY FILTER:
     modelBuilder.Entity<DataTriggerDefinition>()
         .HasQueryFilter(d => d.DateDeleted == null);

     This is SOFT DELETE — rows aren't physically removed, just marked.
     EF automatically adds the filter to every query. You never accidentally
     return deleted triggers.
```

### Key Database Patterns in This System

```
PATTERN 1: SOFT DELETE (DataTriggerDefinition, Report)
══════════════════════════════════════════════════════

  DELETE /api/v1/triggers/{id} does NOT run:
    DELETE FROM data_trigger_definition WHERE id = ...

  Instead it runs:
    UPDATE data_trigger_definition SET "DateDeleted" = NOW() WHERE id = ...

  WHY?
    - Audit trail: you can see what was deleted and when
    - Undo: set DateDeleted back to NULL to "undelete"
    - Foreign keys: executions reference the trigger. Hard delete would
      either cascade-delete execution history or fail on FK constraint.
    - EF query filter hides deleted rows automatically

  INTERVIEW: "What's the downside of soft delete?"
    - Table grows forever (deleted rows stay)
    - Every query needs the filter (EF handles this, but raw SQL doesn't)
    - GDPR: "right to be forgotten" may require actual deletion


PATTERN 2: FOR UPDATE SKIP LOCKED (JobQueue dequeue)
════════════════════════════════════════════════════

  This is the MOST IMPORTANT pattern in the system.

  10 report runners call GET /api/v1/jobs/next-job at the same time:

  Runner 1:  SELECT * FROM job_queue WHERE started IS NULL
             ORDER BY id LIMIT 1 FOR UPDATE SKIP LOCKED;
             → Gets Job #1, locks it

  Runner 2:  (same query)
             → Job #1 is locked, SKIP LOCKED skips it
             → Gets Job #2, locks it

  Runner 3:  (same query)
             → Jobs #1 and #2 are locked, skips both
             → Gets Job #3, locks it

  No conflicts. No retries. No waiting. Each runner gets a unique job instantly.

  INTERVIEW: "What happens without SKIP LOCKED?"
    Runner 1 locks Job #1.
    Runner 2 tries to lock Job #1 → BLOCKS waiting for Runner 1 to finish.
    Runner 3 tries to lock Job #1 → BLOCKS waiting too.
    All runners serialize on the same row. Throughput collapses.

  INTERVIEW: "Why not use RabbitMQ as the job queue instead of PostgreSQL?"
    You could! But the real system uses PostgreSQL because:
    - Jobs need to JOIN with executions, triggers, and reports for metadata
    - The dequeue query loads all context in one transaction
    - Advisory locks coordinate the DatasetMonitorWorker (single-leader)
    - One fewer infrastructure dependency (no RabbitMQ needed for the queue)
    RabbitMQ is used for EVENTS (notifications), not for the job queue itself.


PATTERN 3: SEED DATA (BatchDbContext.OnModelCreating)
════════════════════════════════════════════════════

  The system seeds 2 reports and 3 triggers on first startup:

    Report: "Weekly Audience Summary" (AU panel, ~30 sec avg)
    Report: "Monthly Ratings Export" (AU panel, ~120 sec avg)

    Trigger: "Finance Team Weekly Report" → Weekly Audience Summary, CSV, email
    Trigger: "Marketing Dashboard Feed" → Weekly Audience Summary, JSON, no email
    Trigger: "Monthly Client Export" → Monthly Ratings Export, Excel, email

  WHY SEED DATA?
    So the system works out of the box. You run docker compose up, open the UI,
    and immediately see triggers you can run. No manual setup needed.
    This is critical for a learning project — zero friction to first interaction.
```

---

## 6. Redis — Caching Layer Deep Dive

### What Gets Cached and Why

```
ENDPOINT: GET /api/v1/triggers
CACHE KEY: "triggers:all"
TTL: 2 minutes

  WHY CACHE THIS?
    The trigger list is the first thing the React UI loads.
    It includes a JOIN with the Report table (for report names).
    Triggers change rarely (users create/edit them occasionally).
    But the UI loads them on every page visit.

  THE CODE:
    var cached = await cache.StringGetAsync("triggers:all");
    if (!cached.IsNullOrEmpty)
        return Results.Json(JsonSerializer.Deserialize<object>(cached!));

    // Cache miss — query PostgreSQL
    var triggers = await db.DataTriggerDefinitions
        .Include(t => t.Report)
        .OrderBy(t => t.Priority)
        .Select(t => new { ... })
        .ToListAsync();

    // Store in Redis
    await cache.StringSetAsync("triggers:all", JsonSerializer.Serialize(triggers),
        TimeSpan.FromMinutes(2));

  WHAT HAPPENS:
    Request 1: Redis MISS → PostgreSQL query (5ms) → cache result → return
    Request 2: Redis HIT → return cached JSON (0.1ms) → PostgreSQL not touched
    Request 3: Redis HIT → 0.1ms
    ...
    Request N (after 2 min): Redis key expired → MISS → PostgreSQL again

  INVALIDATION:
    When a trigger is created, updated, or deleted:
      await cache.KeyDeleteAsync("triggers:all");
    Next request will be a cache miss → fresh data from PostgreSQL.


ENDPOINT: GET /api/v1/jobs/statistics
CACHE KEY: "jobs:statistics"
TTL: 10 seconds

  WHY ONLY 10 SECONDS?
    Queue stats change frequently (jobs are created and completed constantly).
    A 2-minute TTL would show stale numbers — user clicks "Run", but the
    dashboard still shows 0 queued for 2 minutes. That's confusing.

    10 seconds is the sweet spot:
      - Absorbs burst polling (50 users × 1 req/5sec = 10 req/sec → 1 DB query)
      - Stats are at most 10 seconds stale (acceptable for a dashboard)

  INVALIDATION:
    When a trigger is run (job created) or a job completes:
      await cache.KeyDeleteAsync("jobs:statistics");
    This gives immediate feedback — run a trigger, stats update instantly.
```

### The Cache Inspector Endpoint

```
GET /api/v1/cache/keys

  This endpoint exists ONLY for learning. It lets you SEE what's in Redis:

  Response:
  {
    "totalKeys": 3,
    "keys": [
      { "key": "triggers:all", "type": "String", "ttlSeconds": 87 },
      { "key": "trigger:aaaa1111-...", "type": "String", "ttlSeconds": 245 },
      { "key": "jobs:statistics", "type": "String", "ttlSeconds": 6 }
    ]
  }

  The React UI has a "Redis Cache Inspector" panel that calls this endpoint.
  You can:
    1. Click "Inspect Keys" → see what's cached and how long until expiry
    2. Click "Flush Cache" → DELETE /api/v1/cache/flush → all keys gone
    3. Reload the trigger list → watch "triggers:all" reappear in the cache
    4. Wait 2 minutes → watch it disappear (TTL expired)

  INTERVIEW: "How would you debug a caching issue in production?"
    1. Check if the key exists: redis-cli GET "triggers:all"
    2. Check TTL: redis-cli TTL "triggers:all" (returns seconds remaining)
    3. Check memory: redis-cli INFO memory (how much RAM is Redis using?)
    4. Check hit rate: redis-cli INFO stats → keyspace_hits / keyspace_misses
    5. In AWS: CloudWatch metrics for ElastiCache → CacheHitRate, CurrConnections

  IN PRODUCTION you would NOT expose /cache/keys — it's a security risk.
  You'd use redis-cli directly or CloudWatch metrics.
```

### Cache Stampede Prevention

```
INTERVIEW: "What is a cache stampede and how do you prevent it?"

  SCENARIO: The "triggers:all" cache key expires.
  At that exact moment, 100 requests arrive simultaneously.
  All 100 see a cache MISS. All 100 query PostgreSQL. All 100 write to Redis.

  That's 100 identical queries hitting the database at once = STAMPEDE.

  ┌─────────────────────────────────────────────────────────────────────┐
  │ Time 0:00 — cache key expires                                       │
  │                                                                     │
  │ Request 1:  Redis MISS → query PostgreSQL → write to Redis          │
  │ Request 2:  Redis MISS → query PostgreSQL → write to Redis          │
  │ Request 3:  Redis MISS → query PostgreSQL → write to Redis          │
  │ ...                                                                 │
  │ Request 100: Redis MISS → query PostgreSQL → write to Redis         │
  │                                                                     │
  │ Result: 100 identical queries. Database CPU spikes. Latency spikes. │
  └─────────────────────────────────────────────────────────────────────┘

  PREVENTION STRATEGIES:

  1. LOCK-BASED (most common):
     First request acquires a Redis lock (SETNX), queries DB, caches result.
     Other requests wait for the lock, then read from cache.
     Only 1 DB query instead of 100.

  2. STALE-WHILE-REVALIDATE:
     Serve the expired cached value while ONE background thread refreshes it.
     Users get slightly stale data for a few milliseconds. No stampede.

  3. EARLY EXPIRATION (probabilistic):
     Each request has a small chance of refreshing the cache BEFORE it expires.
     Spreads the refresh load over time instead of a single spike.

  OUR SYSTEM uses explicit invalidation (delete on write) which mostly avoids
  stampedes because the cache is refreshed by the next single request, not by
  TTL expiry of a hot key. But it's a critical interview topic.
```

---

## 7. RabbitMQ — Event-Driven Messaging Deep Dive

### The Topology (Exchange + Queues + Bindings)

```
Our system declares this topology on API startup:

  EXCHANGE: "batch.events" (type: TOPIC, durable: true)
  │
  ├── QUEUE: "execution-pipeline"
  │   Binding: "execution.created"
  │   Purpose: Could trigger additional processing when executions are created
  │
  ├── QUEUE: "result-orchestrator"
  │   Binding: "job.completed"
  │   Purpose: Worker sends emails and exports when jobs finish
  │
  └── QUEUE: "audit-log"
      Binding: "#" (wildcard — matches ALL events)
      Purpose: Logs every event for debugging and audit trail

WHY A TOPIC EXCHANGE?

  Topic exchanges route messages based on PATTERNS in the routing key.
  This lets us add new event types without changing existing consumers:

  Today:
    "execution.created" → execution-pipeline queue
    "job.completed"     → result-orchestrator queue
    "#"                 → audit-log queue (gets everything)

  Tomorrow (no code changes to existing consumers):
    "execution.created" → execution-pipeline queue
    "job.completed"     → result-orchestrator queue
    "job.started"       → NEW: monitoring-dashboard queue
    "execution.failed"  → NEW: alerting-service queue
    "#"                 → audit-log queue (still gets everything)

  The API just publishes events. It doesn't know or care who consumes them.
  New consumers subscribe by creating a queue and binding it to the exchange.
  This is LOOSE COUPLING — the core principle of event-driven architecture.
```

### Events Published by the API

```
EVENT 1: "execution.created"
  Published when: POST /api/v1/triggers/{id}/run
  Routing key: "execution.created"
  Payload:
  {
    "executionId": "abc123",
    "triggerId": "aaaa1111-...",
    "triggerName": "Finance Team Weekly Report",
    "reportName": "Weekly Audience Summary",
    "panelId": "AU",
    "datasetId": "def456",
    "createdAt": "2026-04-19T10:30:00Z"
  }

  WHO CONSUMES IT:
    audit-log queue (logs it)
    execution-pipeline queue (could trigger additional processing)

  INTERVIEW: "Why publish this event if the API already created the execution?"
    Because other services might need to know. In production:
      - A monitoring dashboard could show real-time execution creation rates
      - An alerting service could detect unusual patterns (100 executions/minute?)
      - A billing service could track usage per organisation
    The API doesn't need to know about any of these. It just publishes the event.


EVENT 2: "job.started"
  Published when: GET /api/v1/jobs/next-job (runner dequeues a job)
  Routing key: "job.started"
  Payload:
  {
    "jobId": 1,
    "executionId": "abc123",
    "panelId": "AU",
    "startedAt": "2026-04-19T10:30:05Z"
  }

  WHO CONSUMES IT:
    audit-log queue (logs it)


EVENT 3: "job.completed"
  Published when: POST /api/v1/jobs/{id}/complete
  Routing key: "job.completed"
  Payload:
  {
    "jobId": 1,
    "executionId": "abc123",
    "triggerId": "aaaa1111-...",
    "status": "Success",
    "completedAt": "2026-04-19T10:30:35Z",
    "executionTimeSeconds": 30
  }

  WHO CONSUMES IT:
    result-orchestrator queue → Worker sends email, publishes export
    audit-log queue → logs it

  THIS IS THE KEY EVENT. It replaces the polling-based ResultOrchestratorWorker
  from your real system. Instead of polling the DB every 30 seconds, the worker
  reacts INSTANTLY when a job completes.
```

### Message Durability — What Survives a Crash

```
INTERVIEW: "What happens if RabbitMQ crashes? Are messages lost?"

  Our configuration:
    Exchange: durable: true    → exchange definition survives restart
    Queue: durable: true       → queue definition survives restart
    Messages: default delivery → messages are persisted to disk

  SCENARIO: RabbitMQ crashes with 5 messages in the result-orchestrator queue.
    1. RabbitMQ restarts
    2. Exchange "batch.events" is restored (durable)
    3. Queue "result-orchestrator" is restored (durable)
    4. All 5 messages are restored from disk
    5. Worker reconnects and processes them

  NOTHING IS LOST. This is why we set durable: true on everything.

  INTERVIEW: "What if the WORKER crashes mid-processing?"
    We use manual acknowledgment (autoAck: false).
    Worker receives message → processes it → sends ACK.
    If worker crashes before ACK:
      RabbitMQ sees the connection drop → message goes back to "ready" state
      → another worker (or the same one after restart) picks it up.
    This is AT-LEAST-ONCE delivery.
```

---

## 7.5 Polling vs Event-Driven — What We Changed and What We Couldn't

> Your real system (BackgroundService_3) uses polling everywhere.
> Level 5 replaces SOME polling with RabbitMQ events — but not all.
> Understanding WHERE each approach fits is the senior engineer insight.

---

### Polling Point 1: DatasetMonitorWorker — Polls External Data Source

```
YOUR REAL SYSTEM:
  Every N minutes, checks if new audience data has arrived for each panel.

  while (!stoppingToken.IsCancellationRequested)
  {
      await monitorService.ExecuteCycleAsync(panel, config, ct);
      await Task.Delay(cycleDelay, stoppingToken);
  }

LEVEL 5 STATUS: ❌ NOT REPLACED — Still polling

WHY WE CAN'T REPLACE THIS WITH RABBITMQ:
  The data source is EXTERNAL. It's an outside system (audience measurement
  platform) that doesn't know about your RabbitMQ. It doesn't publish events.
  You HAVE to poll it because you can't control it.

  This is like checking your physical mailbox — the postman doesn't ring
  your doorbell, so you have to walk out and check periodically.

  The only way to make this event-driven would be if the external system
  supported webhooks (HTTP callbacks) or pushed to an S3 bucket with
  S3 Event Notifications → SNS → SQS. But that requires the external
  system to cooperate, which isn't always possible.

INTERVIEW ANSWER:
  "We poll the external data source because it's a third-party system we
  don't control. If they offered webhooks or event notifications, we'd
  switch to event-driven. But polling with advisory locks ensures exactly
  one pod checks at a time, and the cycle delay keeps the load minimal."
```

---

### Polling Point 2: ResultOrchestratorWorker — Polls DB for Completed Executions

```
YOUR REAL SYSTEM:
  Every N seconds, queries the database for executions that have completed
  (terminal status) but haven't been fully processed (email not sent,
  iPort not published).

  while (!stoppingToken.IsCancellationRequested)
  {
      var executions = await repository.FetchAndLockTerminalExecutionsAsync(ct);
      await ProcessInvocationGroupAsync(...);
      await Task.Delay(interval, stoppingToken);
  }

  The query: "SELECT * FROM executions WHERE status IN (Success, Failure)
              AND export_status->>'isProcessed' = false"

LEVEL 5 STATUS: ✅ REPLACED WITH RABBITMQ

HOW WE REPLACED IT:
  Instead of the worker polling the DB, the API publishes a "job.completed"
  event to RabbitMQ when a runner finishes a job:

  // In the API (POST /jobs/{id}/complete):
  await PublishEvent(factory, "batch.events", "job.completed", new {
      jobId, executionId, triggerId, status, completedAt
  });

  // In the Worker (ResultOrchestratorWorker):
  consumer.ReceivedAsync += async (_, ea) => {
      // Process immediately — no polling, no delay
      await channel.BasicAckAsync(ea.DeliveryTag, false);
  };

WHY THIS IS BETTER:

  ┌─────────────────────────────────┬─────────────────────────────────┐
  │ POLLING (your real system)      │ EVENT-DRIVEN (Level 5)          │
  ├─────────────────────────────────┼─────────────────────────────────┤
  │ Checks DB every 30 seconds      │ Reacts instantly when job done  │
  │ 99% of queries return nothing   │ Zero wasted queries             │
  │ Email sent up to 30 sec late    │ Email sent within 1 second      │
  │ DB load: 2 queries/min/worker   │ DB load: 0 (events via RabbitMQ)│
  │ Scales poorly (more workers =   │ Scales well (RabbitMQ distributes│
  │ more polling = more DB load)    │ events, no DB contention)       │
  │ Simple (no extra infra)         │ Needs RabbitMQ (extra service)  │
  └─────────────────────────────────┴─────────────────────────────────┘

  The LATENCY improvement is the biggest win:
    Polling: job completes at T=0, worker checks at T=28 → email at T=29
    Events:  job completes at T=0, event arrives at T=0.1 → email at T=1

WHY YOUR REAL SYSTEM STILL USES POLLING:
  1. Simplicity — no RabbitMQ dependency to manage
  2. Reliability — if RabbitMQ goes down, polling still works
  3. Catch-all — polling catches ANY terminal execution, even ones where
     the event was lost (belt AND suspenders)
  4. Historical — the system was built before event-driven was considered

INTERVIEW ANSWER:
  "We replaced the polling-based result orchestrator with an event-driven
  approach. When a job completes, the API publishes a 'job.completed' event
  to RabbitMQ. The worker consumes it instantly — no delay, no wasted DB
  queries. In production, you might keep BOTH: events for speed, polling
  as a fallback to catch any missed events. That's the belt-and-suspenders
  pattern."
```

---

### Polling Point 3: JobMaintenanceWorker — Polls DB for Stuck/Stale Jobs

```
YOUR REAL SYSTEM:
  Every N minutes, finds jobs that have been running too long (stuck)
  or sitting in the queue too long (stale), and cleans them up.

  while (!stoppingToken.IsCancellationRequested)
  {
      await service.PerformMaintenanceAsync();
      await Task.Delay(interval, stoppingToken);
  }

  Stuck job: DateExecutionStarted is set but no completion after 4 hours
  → Mark as Failed, remove from queue

  Stale job: DateExecutionStarted is NULL and DateCreated > 24 hours ago
  → Remove from queue (runner never picked it up)

LEVEL 5 STATUS: ❌ NOT REPLACED — Still polling (conceptually)

WHY WE CAN'T REPLACE THIS WITH RABBITMQ:
  This is a COMPENSATING TRANSACTION — it fixes problems that shouldn't
  happen but do (runner crashes, network issues, bugs). There's no event
  to trigger it because the whole point is that the EXPECTED event (job
  completion) NEVER ARRIVED.

  You can't say "when a job gets stuck, publish an event" because the
  system doesn't KNOW it's stuck until someone checks. That's the job
  of periodic maintenance.

  This is like a janitor doing rounds — they don't wait for someone to
  report a mess, they proactively check every room on a schedule.

COULD WE PARTIALLY IMPROVE IT?
  Yes — with RabbitMQ's TTL (Time To Live) and Dead Letter Queues:
    - Publish a "job.timeout.check" delayed message when a job starts
    - Set TTL to 4 hours
    - After 4 hours, the message arrives at a DLQ
    - A consumer checks if the job is still running → if yes, mark as stuck

  But this adds complexity for minimal gain. The maintenance worker runs
  once every 10 minutes, executes a simple query, and handles edge cases.
  It's not a performance bottleneck — it's a safety net.

INTERVIEW ANSWER:
  "Job maintenance can't be event-driven because it detects the ABSENCE
  of events — a job that should have completed but didn't. You need
  periodic scanning for that. We could use delayed messages with TTL,
  but a simple periodic query is more maintainable and equally effective
  for a safety-net operation that runs every 10 minutes."
```

---

### Polling Point 4: Job Queue — Runners Poll for Work

```
YOUR REAL SYSTEM:
  Report runners call GET /api/v1/jobs/next-job to dequeue work.
  This is ALSO polling — runners ask "any work for me?" repeatedly.

  // Runner (external fleet):
  while (true) {
      var job = await httpClient.GetAsync("/api/v1/jobs/next-job");
      if (job.StatusCode == 204) { await Task.Delay(5000); continue; }
      await ProcessJob(job);
      await httpClient.PostAsync($"/api/v1/jobs/{jobId}/complete", result);
  }

LEVEL 5 STATUS: ❌ NOT REPLACED — Still polling (by design)

WHY WE DIDN'T REPLACE THIS WITH RABBITMQ:
  We COULD put jobs in a RabbitMQ queue and have runners consume from it.
  But the real system uses PostgreSQL FOR UPDATE SKIP LOCKED because:

  1. CONTEXT IN ONE ROUND-TRIP: The dequeue query JOINs with executions,
     triggers, and reports to return all metadata at once. With RabbitMQ,
     you'd dequeue a message (just an ID), then query the DB for context
     — two round-trips instead of one.

  2. FAIR QUEUING: The real system uses a complex SQL query (WITH
     ValidTriggers, RankedByOrg) to ensure no single organisation
     monopolizes the queue. RabbitMQ doesn't support this kind of
     priority logic natively.

  3. PANEL FILTERING: Runners request jobs for specific panels:
     GET /jobs/next-job?panels=AU,NZ
     This is a WHERE clause in SQL. In RabbitMQ, you'd need separate
     queues per panel or message filtering (more complex).

  4. TRANSACTIONAL CONSISTENCY: Dequeue + mark as started happens in ONE
     database transaction. With RabbitMQ, you'd dequeue (RabbitMQ) then
     update the DB (separate system) — if the DB update fails, the
     message is already consumed. Harder to keep consistent.

INTERVIEW ANSWER:
  "We keep the job queue in PostgreSQL rather than RabbitMQ because the
  dequeue operation needs transactional consistency with the execution
  record, complex priority logic (fair queuing across organisations),
  and panel-based filtering. FOR UPDATE SKIP LOCKED gives us concurrent,
  lock-free dequeuing without the complexity of coordinating between
  RabbitMQ and PostgreSQL. RabbitMQ is used for EVENTS (notifications
  after completion), not for the work queue itself."
```

---

### The Complete Picture — What Changed, What Didn't, and Why

```
┌─────────────────────────────────┬──────────────┬─────────────────────────────────┐
│ Component                       │ Level 5      │ Why                             │
├─────────────────────────────────┼──────────────┼─────────────────────────────────┤
│ DatasetMonitorWorker            │ ❌ Still polls│ External system, can't control  │
│ (checks for new data)           │              │ No webhook/event available      │
├─────────────────────────────────┼──────────────┼─────────────────────────────────┤
│ ResultOrchestratorWorker        │ ✅ Event-     │ API knows when job completes    │
│ (sends emails after completion) │ driven       │ Can publish event at that moment│
│                                 │              │ Instant reaction, zero waste    │
├─────────────────────────────────┼──────────────┼─────────────────────────────────┤
│ JobMaintenanceWorker            │ ❌ Still polls│ Detects ABSENCE of events       │
│ (cleans stuck/stale jobs)       │              │ Can't be triggered by an event  │
├─────────────────────────────────┼──────────────┼─────────────────────────────────┤
│ Job Queue (runner dequeue)      │ ❌ Still polls│ Needs JOINs, fair queuing,      │
│ (GET /jobs/next-job)            │              │ panel filtering, transactions   │
├─────────────────────────────────┼──────────────┼─────────────────────────────────┤
│ Audit logging                   │ ✅ Event-     │ All events flow through RabbitMQ│
│ (track all system events)       │ driven       │ "#" binding catches everything  │
└─────────────────────────────────┴──────────────┴─────────────────────────────────┘

THE RULE OF THUMB:
  "Can I publish an event at the MOMENT this thing happens?"
    YES → Event-driven (RabbitMQ)    → ResultOrchestrator, Audit Log
    NO  → Polling (periodic check)   → DatasetMonitor, Maintenance, Job Queue

  Job completes → YES, the API knows immediately → publish event ✅
  New data arrives externally → NO, we have to check → poll ❌
  Job is stuck (timeout) → NO, absence of event → poll ❌
  Runner needs work → COULD be event-driven, but SQL gives us richer
                      querying (JOINs, filtering, fair scheduling) → poll ❌
```

---

## 8. Background Workers — The Consumer Pattern (Strategy + Grouping)

### The Strategy Pattern — How Export Handlers Work

```
YOUR REAL SYSTEM'S REVAMP:

  BEFORE (monolithic):
    ResultOrchestratorService had ALL export logic inline:
      - Email building, sending, tracking
      - iPort credential decryption, upload, tracking
    One giant method. Hard to test. Hard to add new channels.

  AFTER (strategy pattern):
    ResultOrchestratorService dispatches to IResultExportHandler implementations:
      - EmailExportHandler (handles email grouping, sending, tracking)
      - IPortExportHandler (handles iPort upload, tracking)
      - (Future: SlackExportHandler, WebhookExportHandler — just add a class)

  The interface:
    public interface IResultExportHandler
    {
        bool AppliesTo(ExecutionWithDefinition execution);
        Task<bool> HandleAsync(ExecutionWithDefinition execution, CancellationToken ct);
    }

  The orchestrator:
    var applicableHandlers = _exportHandlers.Where(h => h.AppliesTo(execWithDef));
    foreach (var handler in applicableHandlers)
    {
        var success = await handler.HandleAsync(execWithDef, ct);
        if (!success) allSucceeded = false;
    }

  Each handler is:
    - INDEPENDENT: email failure doesn't block iPort
    - IDEMPOTENT: safe to retry (checks if already processed)
    - TESTABLE: mock one handler, test another in isolation

INTERVIEW: "What design pattern does your result orchestrator use?"
  "Strategy pattern via DI. IEnumerable<IResultExportHandler> is injected.
  Each handler decides if it applies and processes independently. Adding
  a new export channel = register a new class. No existing code changes."
```

### Email Grouping Logic — The Key Nuance

```
THE PROBLEM:
  User has 3 triggers in the same group: P5+, 65+, P18-39
  User clicks "Run (3 selected)" → all 3 get InvocationId = abc123

  WITHOUT grouping: 3 separate emails. Clutters inbox.
  WITH grouping: 1 consolidated email with all 3 results.

THE FLOW:

  P5+ completes  → EmailExportHandler: "Is group complete?" → No → DEFER
  65+ completes  → EmailExportHandler: "Is group complete?" → No → DEFER
  P18-39 completes → EmailExportHandler: "Is group complete?" → Yes → SEND

  One email to rizul1309@gmail.com with P5+, 65+, and P18-39 results.

THE LOGIC (from EmailExportHandler.HandleAsync):

  1. HasPendingExecutionsInGroupAsync(invocationId, groupId)?
     ├── YES and within timeout (2 hours) → DEFER (return true)
     ├── YES but timeout exceeded → SEND ANYWAY (partial results)
     └── NO (all complete) → SEND CONSOLIDATED EMAIL

  2. Fetch ALL terminal executions in the group (not just this one)
  3. Build recipient subsets (different triggers may have different recipients)
  4. Build HTML email with status table + HMAC-signed download links
  5. Send via SMTP
  6. Track: ExportStatus.EmailsSent[recipient] = DateTime.UtcNow (deep-merge)

IDEMPOTENCY:
  Handler checks EmailsSent dict before sending. If recipient already
  emailed → skip. Safe to retry after crash. No duplicate emails.

INTERVIEW: "How do you handle grouped email delivery?"
  "Triggers in the same group share an InvocationId. The EmailExportHandler
  defers until all executions in the group reach terminal state (or 2-hour
  timeout). Then sends one consolidated email per recipient. Idempotent via
  deep-merge on ExportStatus JSONB — safe for concurrent workers."
```

### Dynamic Delay — No Fixed Sleep

```
  BEFORE (fixed delay):
    while (!stopping) {
        await ProcessBatch();
        await Task.Delay(30_000);  // Always sleep 30 seconds
    }

  AFTER (dynamic delay):
    while (!stopping) {
        bool processed = await service.ProcessPendingResultAsync();
        if (!processed)
            await Task.Delay(interval);  // Only sleep when IDLE
    }

  ProcessPendingResultAsync() handles ONE execution and returns bool.
  If work found → loop immediately. If idle → sleep.
  Burst of 100 completions → processed in seconds, not minutes.

INTERVIEW: "How does your worker handle burst workloads?"
  "Dynamic delay with single-execution processing. Only sleeps when idle.
  Each execution gets its own DI scope and error boundary. One failure
  doesn't block others. Throughput scales linearly with work available."
```

### Batch Run Endpoint — Shared InvocationId

```
  POST /api/v1/triggers/run-batch
  Body: { "triggerIds": ["id-1", "id-2", "id-3"] }

  Response: { "invocationId": "shared-uuid", "executionsCreated": 3 }

  All executions share one InvocationId → grouped email delivery.
  Calling /triggers/{id}/run separately would give each its OWN
  InvocationId → separate emails. The batch endpoint links them.
```

### AuditLogWorker

```
WHAT IT DOES:
  Listens on the "audit-log" queue with binding "#" (matches ALL events).
  Logs every event that flows through the system.

WHY THIS EXISTS:
  1. DEBUGGING: "Did the API actually publish the event? Let me check audit logs."
  2. DEMONSTRATES TOPIC EXCHANGE: The same event goes to multiple queues.
     "job.completed" goes to BOTH result-orchestrator AND audit-log.
  3. PRODUCTION PATTERN: Every event-driven system needs an audit trail.
     In production, this would write to CloudWatch Logs or a data lake.

THE BINDING PATTERN:
  "#" = match zero or more words in the routing key.
  This means:
    "execution.created" → matches "#" → audit-log gets it
    "job.started"       → matches "#" → audit-log gets it
    "job.completed"     → matches "#" → audit-log gets it
    "anything.else.new" → matches "#" → audit-log gets it

  The audit-log queue is a CATCH-ALL. It sees everything.
  The result-orchestrator queue only sees "job.completed".
  This is the power of topic exchanges — selective subscription.
```

### Worker Scaling

```
docker compose up -d --scale worker=3

  This starts 3 worker containers. Each has a ResultOrchestratorWorker
  and an AuditLogWorker. RabbitMQ distributes messages across them:

  "job.completed" event arrives in result-orchestrator queue:
    → Worker 1 gets it (BasicQos prefetchCount: 1)
    → Worker 1 is busy processing → next event goes to Worker 2
    → Worker 2 is busy → next event goes to Worker 3

  With prefetchCount: 1, each worker processes ONE message at a time.
  RabbitMQ sends the next message to whichever worker is free.
  Fast workers get more messages. Slow workers don't get overwhelmed.

  INTERVIEW: "How would you auto-scale workers in production?"
    AWS ECS + CloudWatch alarm:
      Metric: RabbitMQ queue depth (ApproximateNumberOfMessagesVisible)
      Alarm: "If queue depth > 50 for 5 minutes, add 2 workers"
      Alarm: "If queue depth = 0 for 10 minutes, remove 1 worker"
    This is reactive auto-scaling based on actual workload.
```

---

## 9. The React UI — Frontend Verification

### What the Dashboard Shows

```
┌──────────────────────────────────────────────────────────────────────────┐
│  BATCH PROCESSING DASHBOARD                                              │
│                                                                          │
│  ┌─────────────┐  ┌──────────────────────────┐  ┌────────────────────┐  │
│  │ System      │  │ Queue Statistics          │  │ Actions            │  │
│  │ Health      │  │                           │  │                    │  │
│  │  Healthy    │  │  Total: 2  Exec: 1  Wait:1│  │ [Dequeue & Process]│  │
│  └─────────────┘  └──────────────────────────┘  └────────────────────┘  │
│                                                                          │
│  ┌─────────────────────────┐  ┌──────────────────────────────────────┐  │
│  │ Trigger Definitions (3) │  │ Execution History                    │  │
│  │                         │  │                                      │  │
│  │ Finance Team Weekly     │  │ Status  Created   Started  Completed │  │
│  │ Weekly Audience · CSV   │  │ Success 10:30:00  10:30:05 10:30:35  │  │
│  │ daily · 📧 finance@... │  │ Pending 10:31:00  -        -         │  │
│  │              [Run ▶]    │  │                                      │  │
│  │                         │  │                                      │  │
│  │ Marketing Dashboard     │  ├──────────────────────────────────────┤  │
│  │ Weekly Audience · JSON  │  │ Activity Log                         │  │
│  │ daily                   │  │                                      │  │
│  │              [Run ▶]    │  │ [10:30:35] ✓ Job #1 completed        │  │
│  │                         │  │ [10:30:33] Processing Job #1...      │  │
│  │ Monthly Client Export   │  │ [10:30:05] ✓ Dequeued Job #1         │  │
│  │ Monthly Ratings · Excel │  │ [10:30:00] ✓ Execution created       │  │
│  │ monthly · 📧 clients@  │  │ [10:30:00] Running trigger: Finance  │  │
│  │              [Run ▶]    │  │                                      │  │
│  ├─────────────────────────┤  └──────────────────────────────────────┘  │
│  │ Redis Cache Inspector   │                                            │
│  │                         │                                            │
│  │ [Inspect Keys] [Flush]  │                                            │
│  │ triggers:all  TTL: 87s  │                                            │
│  │ jobs:stats    TTL: 6s   │                                            │
│  └─────────────────────────┘                                            │
└──────────────────────────────────────────────────────────────────────────┘
```

### How the UI Talks to the API

```
The React UI runs in its own container (port 80 internally).
Nginx routes requests:

  Browser → http://localhost:8080/           → Nginx → React UI container
  Browser → http://localhost:8080/api/v1/... → Nginx → .NET API container
  Browser → http://localhost:8080/health     → Nginx → .NET API container

The UI makes fetch() calls to /api/v1/... which Nginx proxies to the API.
The UI never talks to the API directly — always through Nginx.

  // In App.jsx:
  const API = '/api/v1'
  const res = await fetch(`${API}/triggers`)

  // Nginx routes /api/* to the .NET API:
  location /api/ {
      proxy_pass http://dotnet_api;
  }

This is the same pattern as production:
  Browser → CloudFront/ALB → API
  The frontend never knows the API's internal address.
```

### What the "Dequeue & Process Job" Button Does

```
This button simulates what a report runner does in production.
It's the ENTIRE job lifecycle in one click:

  1. fetch('/api/v1/jobs/next-job')
     → API dequeues the oldest pending job
     → API marks it as started
     → API publishes "job.started" event
     → Returns: { jobId, executionId, reportName, outputFormat }

  2. setTimeout(2000) — simulates 2 seconds of report processing

  3. fetch('/api/v1/jobs/{jobId}/complete', {
       method: 'POST',
       body: { success: true, executionTimeSeconds: 3, resultPath: '...' }
     })
     → API updates execution: status → Success, timestamps, paths
     → API deletes the JobQueue row
     → API publishes "job.completed" event
     → Worker picks up the event, sends email notification

  4. UI refreshes stats and execution history

In production, steps 1-3 are done by a fleet of report runner EC2 instances.
The UI simulates this so you can see the full pipeline without running
actual report generation software.
```

---

## 10. End-to-End Verification Walkthrough

> Follow these steps in order. Each step proves a different part of the system.
> Have three browser tabs open:
>   Tab 1: http://localhost:8080 (React Dashboard)
>   Tab 2: http://localhost:15672 (RabbitMQ Management UI, login: guest/guest)
>   Tab 3: Terminal (for docker compose logs)

---

### Step 0: Start Everything

```bash
cd level5-batch-processing-system
docker compose up --build -d

# Wait ~40 seconds for all services to start (RabbitMQ is slowest)
docker compose ps

# You should see 7 services:
#   nginx     running
#   api       healthy
#   worker    running
#   ui        running
#   db        healthy
#   redis     healthy
#   rabbitmq  healthy

# If api shows "starting", wait 10 more seconds and check again.
# The API waits for PostgreSQL, Redis, AND RabbitMQ to be healthy.
```

---

### Step 1: Verify the Dashboard Loads (React UI + API + PostgreSQL + Redis)

```
OPEN: http://localhost:8080

WHAT YOU SHOULD SEE:
  ✓ System Health: "Healthy" (green badge)
  ✓ Queue Statistics: Total 0, Executing 0, Waiting 0
  ✓ 3 Trigger Definitions (seeded from the database):
    - Finance Team Weekly Report (CSV, daily, email)
    - Marketing Dashboard Feed (JSON, daily, no email)
    - Monthly Client Export (Excel, monthly, email)

WHAT THIS PROVES:
  ✓ Nginx is routing / to the React UI container
  ✓ Nginx is routing /api/* to the .NET API container
  ✓ The API connected to PostgreSQL and ran EnsureCreated() (tables + seed data)
  ✓ The API connected to Redis (health check passed)
  ✓ The API connected to RabbitMQ and declared the topology
  ✓ React fetched /api/v1/triggers and rendered the trigger list
  ✓ React fetched /api/v1/jobs/statistics and rendered queue stats

IF SOMETHING IS WRONG:
  docker compose logs api --tail 30    # Check API startup errors
  docker compose logs db --tail 10     # Check PostgreSQL
  docker compose logs redis --tail 10  # Check Redis
  docker compose logs rabbitmq --tail 10  # Check RabbitMQ
```

---

### Step 2: Verify Redis Caching (Cache Inspector)

```
IN THE DASHBOARD:
  1. Click "Inspect Keys" in the Redis Cache Inspector panel

  WHAT YOU SHOULD SEE:
    Total keys: 2 (or more)
    - triggers:all    (String, TTL: ~120s)
    - jobs:statistics  (String, TTL: ~10s)

  These were cached when the UI loaded and fetched /triggers and /jobs/statistics.

  2. Click "Flush Cache"
     Activity log: "Redis cache flushed"

  3. Click "Inspect Keys" again
     Total keys: 0 — all keys are gone

  4. Click "Refresh" on the trigger list
     The UI fetches /api/v1/triggers → cache MISS → PostgreSQL query → cached

  5. Click "Inspect Keys" again
     Total keys: 1 — "triggers:all" is back with a fresh TTL

WHAT THIS PROVES:
  ✓ Redis is storing cached API responses
  ✓ Cache keys have TTLs (they auto-expire)
  ✓ Flushing the cache works (all keys deleted)
  ✓ The next API call re-populates the cache (cache-aside pattern)

BONUS — VERIFY FROM TERMINAL:
  docker exec -it level5-batch-processing-system-redis-1 redis-cli
  > KEYS *
  > GET "triggers:all"
  > TTL "triggers:all"
  > exit
```

---

### Step 3: Run a Trigger (API → PostgreSQL → RabbitMQ)

```
IN THE DASHBOARD:
  1. Click "Run ▶" on "Finance Team Weekly Report"

  ACTIVITY LOG SHOWS:
    [10:30:00] Running trigger: Finance Team Weekly Report...
    [10:30:00] ✓ Execution created: abc123-... (Job #1)

  QUEUE STATISTICS UPDATE:
    Total Queued: 0 → 1
    Waiting: 0 → 1

  2. Click on "Finance Team Weekly Report" to select it
     The Execution History panel appears on the right

  EXECUTION HISTORY SHOWS:
    Status: Pending
    Created: 10:30:00
    Started: -
    Completed: -

WHAT THIS PROVES:
  ✓ POST /api/v1/triggers/{id}/run worked
  ✓ API created a Dataset record in PostgreSQL
  ✓ API created a DataTriggerExecution record (status: Pending)
  ✓ API created a JobQueue record
  ✓ API published "execution.created" event to RabbitMQ
  ✓ API invalidated the "jobs:statistics" cache key
  ✓ UI refreshed and shows the new execution and updated stats

VERIFY IN RABBITMQ UI (Tab 2):
  Go to http://localhost:15672 → Queues tab
  - audit-log: 1 message (it received the "execution.created" event)
  - execution-pipeline: 1 message (it received the "execution.created" event)
  - result-orchestrator: 0 messages (no "job.completed" event yet)
```

---

### Step 4: Dequeue and Process the Job (Simulating a Report Runner)

```
IN THE DASHBOARD:
  1. Click "Dequeue & Process Job"

  ACTIVITY LOG SHOWS (in sequence):
    [10:30:05] Dequeuing next job...
    [10:30:05] ✓ Dequeued Job #1: Weekly Audience Summary (CSV)
    [10:30:05] Processing Job #1...
    [10:30:07] ✓ Job #1 completed successfully    ← 2 seconds later

  QUEUE STATISTICS UPDATE:
    Total Queued: 1 → 0
    Executing: 0 → 1 → 0

  EXECUTION HISTORY UPDATES:
    Status: Pending → Success
    Started: 10:30:05
    Completed: 10:30:07
    Time: 3s

WHAT THIS PROVES:
  ✓ GET /api/v1/jobs/next-job dequeued the oldest pending job
  ✓ API marked the job as started (DateExecutionStarted set)
  ✓ API published "job.started" event to RabbitMQ
  ✓ POST /api/v1/jobs/{id}/complete updated the execution to Success
  ✓ API deleted the JobQueue row (job is done)
  ✓ API published "job.completed" event to RabbitMQ
  ✓ UI shows the execution transitioning through the full lifecycle

VERIFY IN RABBITMQ UI (Tab 2):
  Go to Queues tab:
  - audit-log: 3 messages (execution.created + job.started + job.completed)
  - result-orchestrator: should be 0 (worker consumed the job.completed event)

  If result-orchestrator shows 1, the worker hasn't consumed it yet.
  Wait a second and refresh.
```

---

### Step 5: Verify the Worker Processed the Event

```
IN TERMINAL (Tab 3):
  docker compose logs worker --tail 20

  YOU SHOULD SEE:
    worker-1 | [ResultOrchestrator] Processing completed job: execution=abc123, status=Success
    worker-1 | [ResultOrchestrator] → Sending email notification for execution abc123
    worker-1 | [ResultOrchestrator] → Email sent successfully
    worker-1 | [ResultOrchestrator] → Publishing results to export channel for execution abc123
    worker-1 | [ResultOrchestrator] → Export complete
    worker-1 | [ResultOrchestrator] ✓ Finished processing execution abc123

    worker-1 | [AuditLog] Event: routingKey=execution.created, body={...}
    worker-1 | [AuditLog] Event: routingKey=job.started, body={...}
    worker-1 | [AuditLog] Event: routingKey=job.completed, body={...}

WHAT THIS PROVES:
  ✓ Worker consumed "job.completed" from the result-orchestrator queue
  ✓ Worker simulated sending email notification
  ✓ Worker simulated publishing to export channel
  ✓ Worker ACKed the message (RabbitMQ removed it from the queue)
  ✓ AuditLogWorker received ALL 3 events (execution.created, job.started, job.completed)
  ✓ The "#" binding pattern works — audit-log catches everything

THIS IS THE FULL PIPELINE:
  UI click → API → PostgreSQL + RabbitMQ → Worker → email + export
  All verified. All visible. All explainable in an interview.
```

---

### Step 6: Run Multiple Triggers and Watch Parallel Processing

```
IN THE DASHBOARD:
  1. Click "Run ▶" on ALL 3 triggers (click each one quickly)

  QUEUE STATISTICS:
    Total Queued: 0 → 1 → 2 → 3

  ACTIVITY LOG:
    ✓ Execution created: ... (Job #2)
    ✓ Execution created: ... (Job #3)
    ✓ Execution created: ... (Job #4)

  2. Click "Dequeue & Process Job" three times (or wait for each to finish)

  WATCH:
    - Each click dequeues a DIFFERENT job (FIFO order)
    - Each job completes after 2 seconds
    - Queue stats count down: 3 → 2 → 1 → 0
    - Execution history shows all 3 transitioning to Success

  3. Check worker logs:
     docker compose logs worker --tail 30

     You'll see 3 "job.completed" events processed by the worker.
     Each one triggered email + export simulation.

WHAT THIS PROVES:
  ✓ Multiple triggers can fire independently
  ✓ Jobs are dequeued in FIFO order (oldest first)
  ✓ Each job gets a unique execution and job ID
  ✓ The worker processes each completion event independently
  ✓ The system handles concurrent workload correctly
```

---

### Step 7: Verify RabbitMQ Topology (Exchange + Bindings)

```
IN RABBITMQ UI (http://localhost:15672):

  1. Click "Exchanges" tab
     Find "batch.events" — Type: topic, Durable: true

  2. Click on "batch.events"
     Scroll to "Bindings" section:
       audit-log           ← #                  (catches everything)
       execution-pipeline  ← execution.created   (only new executions)
       result-orchestrator ← job.completed        (only completed jobs)

  3. Click "Queues and Streams" tab
     See all 3 queues with their message counts and consumer counts:
       audit-log:           consumers: 1 (AuditLogWorker)
       execution-pipeline:  consumers: 0 (no consumer in Level 5)
       result-orchestrator: consumers: 1 (ResultOrchestratorWorker)

WHAT THIS PROVES:
  ✓ The topic exchange was created correctly
  ✓ All 3 queues are bound with the correct routing patterns
  ✓ Workers are connected as consumers
  ✓ You can visually inspect the entire messaging topology

INTERVIEW: "How would you debug 'my service isn't receiving events'?"
  1. Check the exchange exists (Exchanges tab)
  2. Check the queue exists and has the correct binding (Queues tab → Bindings)
  3. Check the consumer count (is the worker connected?)
  4. Check the message count (are messages piling up? → consumer is slow/dead)
  5. Publish a test message from the RabbitMQ UI to verify routing
```

---

### Step 8: Verify with curl (API-Level Proof)

```bash
# Health check
curl http://localhost:8080/health

# List all triggers (first call = DB, subsequent = Redis cache)
curl http://localhost:8080/api/v1/triggers | jq

# List reports
curl http://localhost:8080/api/v1/reports | jq

# Run a trigger
curl -X POST http://localhost:8080/api/v1/triggers/aaaa1111-1111-1111-1111-111111111111/run | jq

# Check queue stats
curl http://localhost:8080/api/v1/jobs/statistics | jq

# Dequeue a job (simulating a report runner)
curl http://localhost:8080/api/v1/jobs/next-job | jq

# Complete the job (replace {jobId} with the actual job ID from above)
curl -X POST http://localhost:8080/api/v1/jobs/1/complete \
  -H "Content-Type: application/json" \
  -d '{"success": true, "executionTimeSeconds": 5, "resultPath": "results/test.csv"}' | jq

# Check execution history
curl http://localhost:8080/api/v1/triggers/aaaa1111-1111-1111-1111-111111111111/executions | jq

# Inspect Redis cache
curl http://localhost:8080/api/v1/cache/keys | jq

# See RabbitMQ topology
curl http://localhost:8080/api/v1/events/topology | jq
```

---

### Step 9: Verify Email Grouping Logic (Defer → Complete → Send)

```
THIS IS THE KEY TEST. It proves that grouped triggers get ONE consolidated
email instead of separate emails per trigger.

SETUP: You need 4 things open:
  Tab 1: http://localhost:8080 (Dashboard)
  Tab 2: http://localhost:15672 (RabbitMQ UI, guest/guest)
  Tab 3: DBeaver (localhost:5433, batchuser/batchpass)
  Tab 4: Terminal for curl + worker logs
```

**Step 9a: Run a batch of 2 triggers (shared InvocationId)**

```bash
curl -s -X POST http://localhost:8080/api/v1/triggers/run-batch \
  -H "Content-Type: application/json" \
  -d "{\"triggerIds\":[\"aaaa1111-1111-1111-1111-111111111111\",\"aaaa3333-3333-3333-3333-333333333333\"]}" | jq
```

```
Response:
{
  "invocationId": "COPY-THIS-UUID",    ← Save this!
  "executionsCreated": 2,
  "message": "Batch run started. 2 executions share InvocationId..."
}

UI: Queue stats → Total: 2, Waiting: 2
RabbitMQ UI: Queues → audit-log has 2 messages (execution.created events)
```

**Step 9b: Verify in DB — both executions share InvocationId**

```sql
SELECT "DataTriggerExecutionId", "InvocationId", "ResultStatus"
FROM "DataTriggerExecutions"
WHERE "InvocationId" = 'PASTE-INVOCATION-ID'
ORDER BY "DateCreated";
```

```
Result: 2 rows, same InvocationId, both ResultStatus = 0 (Pending)
```

**Step 9c: Complete FIRST job — email should be DEFERRED**

```bash
# Dequeue
curl -s http://localhost:8080/api/v1/jobs/next-job | jq
# Note the jobId, then complete it:
curl -s -X POST http://localhost:8080/api/v1/jobs/JOB_ID/complete \
  -H "Content-Type: application/json" \
  -d "{\"success\":true,\"executionTimeSeconds\":5}" | jq
```

```
Response:
{
  "groupComplete": false,
  "groupProgress": "1/2",
  "message": "Job completed. Group NOT complete yet (1/2) — email DEFERRED."
}
```

**Step 9d: Check worker logs — see the DEFER**

```bash
docker compose logs worker --tail 15
```

```
Expected output:
  [ResultOrchestrator] Processing execution ... (group=1/2, complete=False)
  [EmailExportHandler] ⏳ DEFERRING email — group NOT complete (1/2).
                       Waiting for all executions to finish.
  [IPortExportHandler] Publishing CSV to iPort...
  [IPortExportHandler] → ✓ Published to iPort
```

```
KEY INSIGHT: Email was DEFERRED but iPort still published.
  - Email is GROUPED (waits for all in group)
  - iPort is PER-EXECUTION (publishes immediately)
  This matches your real system's behavior.
```

**Step 9e: Complete SECOND job — email should be SENT**

```bash
curl -s http://localhost:8080/api/v1/jobs/next-job | jq
curl -s -X POST http://localhost:8080/api/v1/jobs/JOB_ID/complete \
  -H "Content-Type: application/json" \
  -d "{\"success\":true,\"executionTimeSeconds\":3}" | jq
```

```
Response:
{
  "groupComplete": true,
  "groupProgress": "2/2",
  "message": "Job completed. Group is COMPLETE — email will be sent."
}
```

**Step 9f: Check worker logs — see the SEND**

```bash
docker compose logs worker --tail 15
```

```
Expected output:
  [ResultOrchestrator] Processing execution ... (group=2/2, complete=True)
  [EmailExportHandler] ✓ Group COMPLETE (2/2). Building consolidated email...
  [EmailExportHandler] → Sending consolidated email to rizul1309@gmail.com
                         (covers ALL executions in group)
  [EmailExportHandler] → ✓ Email sent successfully
  [IPortExportHandler] Publishing CSV to iPort...
  [IPortExportHandler] → ✓ Published to iPort
  [ResultOrchestrator] ✓ Execution fully processed (all handlers succeeded)
```

**Step 9g: Inspect group state via API**

```bash
curl -s http://localhost:8080/api/v1/executions/group/PASTE-INVOCATION-ID | jq
```

```
Response:
{
  "invocationId": "...",
  "totalInGroup": 2,
  "completedInGroup": 2,
  "groupComplete": true,
  "message": "✓ Group COMPLETE — consolidated email would be sent",
  "executions": [
    { "triggerName": "Finance Team Weekly Report", "status": "Success", ... },
    { "triggerName": "Monthly Client Export", "status": "Success", ... }
  ]
}
```

**Step 9h: Verify in DB — both executions now complete**

```sql
SELECT "DataTriggerExecutionId", "ResultStatus", "DateExecutionCompleted"
FROM "DataTriggerExecutions"
WHERE "InvocationId" = 'PASTE-INVOCATION-ID'
ORDER BY "DateCreated";
```

```
Result: 2 rows, both ResultStatus = 1 (Success), both have completion timestamps.
```

**Step 9i: RabbitMQ UI — verify events flowed correctly**

```
Go to http://localhost:15672 → Queues tab:
  - result-orchestrator: 0 messages (worker consumed both job.completed events)
  - audit-log: has messages (logged execution.created + job.started + job.completed)
  - execution-pipeline: has messages (execution.created events, no consumer)

Click on "audit-log" → "Get messages" → you'll see the full JSON payloads
including groupComplete: false (first event) and groupComplete: true (second event).
```

**Summary of what you just proved:**

```
┌──────────────┬────────────────────────────────────────────────────────┐
│ Layer        │ What You Verified                                      │
├──────────────┼────────────────────────────────────────────────────────┤
│ UI           │ Queue stats: 0→2→1→0 as jobs created and consumed     │
│ API          │ Batch-run creates shared InvocationId                  │
│              │ Completion response shows groupProgress and groupComplete│
│ DB           │ Both executions share InvocationId                     │
│              │ Status transitions: Pending → Success                  │
│ RabbitMQ     │ Events routed through topic exchange to correct queues │
│              │ Worker consumed events, audit-log captured everything  │
│ Worker       │ First completion → DEFERRED email (group incomplete)   │
│              │ Second completion → SENT consolidated email            │
│              │ iPort published per-execution (not grouped)            │
└──────────────┴────────────────────────────────────────────────────────┘
```

---

### Cleanup

```bash
# Stop everything
docker compose down

# Stop and delete all data (PostgreSQL, Redis, RabbitMQ volumes)
docker compose down -v
```

---

## 11. Interview Questions and Answers

> These are real questions you'll get in SDE-3 interviews about distributed systems,
> caching, messaging, and background processing. Every answer references THIS system.

---

### System Design Questions

```
Q: "Walk me through how your batch processing system works."

A: "We have a pipeline that processes reports when new data arrives.

   The core entities are:
   - DataTriggerDefinition: the config — what report to run, what format, who to email
   - DataTriggerExecution: one run of a trigger — tracks status, timing, result paths
   - JobQueue: the work queue — runners poll for jobs, process them, report back

   The flow:
   1. New data arrives (or user clicks Run) → API creates an execution + job
   2. API publishes 'execution.created' event to RabbitMQ
   3. A report runner dequeues the job via GET /jobs/next-job
      (uses FOR UPDATE SKIP LOCKED for concurrent, lock-free dequeuing)
   4. Runner processes the report, calls POST /jobs/{id}/complete
   5. API publishes 'job.completed' event to RabbitMQ
   6. Background worker consumes the event, sends email, publishes export

   Redis caches frequently-read data (trigger list, queue stats) to reduce
   DB load from the dashboard polling every 5 seconds.

   The whole thing runs as 7 Docker containers: nginx, API, worker, UI,
   PostgreSQL, Redis, RabbitMQ."


Q: "Why did you use PostgreSQL for the job queue instead of RabbitMQ?"

A: "The job queue needs to JOIN with executions, triggers, and reports to
   return metadata to the runner (report name, format, S3 path, JWT token).
   If the queue were in RabbitMQ, we'd need to look up all that context
   from the database anyway after dequeuing.

   PostgreSQL's FOR UPDATE SKIP LOCKED gives us a concurrent, lock-free
   queue with zero contention. 10 runners dequeue simultaneously, each
   gets a unique job instantly.

   We use RabbitMQ for EVENTS (notifications after job completion), not
   for the job queue itself. This keeps the architecture simple — one
   fewer round-trip between the queue and the database."


Q: "What happens if a report runner crashes mid-processing?"

A: "The JobQueue row still exists with DateExecutionStarted set.
   The execution status is still Pending.

   The JobMaintenanceWorker runs periodically and finds 'stuck' jobs:
   jobs where DateExecutionStarted is set but the execution hasn't
   completed within the timeout threshold (e.g., 4 hours).

   It marks the execution as Failed and removes the JobQueue row.
   The trigger can be re-run manually or will fire again on the next
   dataset arrival.

   This is a COMPENSATING TRANSACTION pattern — we detect and recover
   from partial failures rather than trying to prevent them."
```

---

### Caching Questions

```
Q: "Explain your caching strategy."

A: "We use Redis as a cache-aside layer for read-heavy endpoints.

   Three things are cached:
   1. Trigger list (TTL: 2 min) — changes rarely, read on every page load
   2. Individual triggers (TTL: 5 min) — even more stable
   3. Queue statistics (TTL: 10 sec) — changes frequently, short TTL

   On writes, we explicitly invalidate:
   - Create/update/delete trigger → delete 'triggers:all' + 'trigger:{id}'
   - Run trigger or complete job → delete 'jobs:statistics'

   This gives us immediate consistency on writes (next read gets fresh data)
   while absorbing read load (50 dashboard users polling = 1 DB query per TTL)."


Q: "What is a cache stampede and how would you prevent it?"

A: "A cache stampede happens when a hot cache key expires and hundreds of
   requests simultaneously see a cache miss, all querying the database.

   Prevention strategies:
   1. Lock-based: first request acquires a Redis lock (SETNX), queries DB,
      caches result. Others wait for the lock, then read from cache.
   2. Stale-while-revalidate: serve expired data while one thread refreshes.
   3. Early probabilistic expiration: randomly refresh before TTL expires.

   In our system, we mostly avoid stampedes through explicit invalidation
   on writes rather than relying on TTL expiry. But for a hot key like
   'jobs:statistics' with a 10-second TTL, a lock-based approach would
   be the right production hardening."


Q: "Why Redis over Memcached?"

A: "Redis gives us data structure support (not just strings), persistence
   (survives restarts), pub/sub (for real-time features), and introspection
   (KEYS, TTL, TYPE commands for debugging).

   Memcached is strings-only, no persistence, no pub/sub, and you can't
   list what's cached. For a system where we need cache visibility
   (the /cache/keys endpoint) and might add pub/sub later, Redis is
   the clear choice. AWS ElastiCache supports both, but most teams
   default to Redis."


Q: "How do you handle cache invalidation across multiple API instances?"

A: "Redis is external and shared. When Instance 1 creates a trigger and
   deletes the 'triggers:all' key, that deletion is visible to Instance 2
   and Instance 3 immediately. The next request to any instance will see
   a cache miss and re-populate from the database.

   If we used in-process caching (IMemoryCache), each instance would have
   its own cache. Instance 1 invalidates its local cache, but Instance 2
   still serves stale data until its local TTL expires. That's why we use
   Redis — one cache, shared across all instances, one invalidation clears
   it for everyone."
```

---

### Messaging Questions

```
Q: "Why RabbitMQ? Why not just call the email service directly?"

A: "Direct calls create tight coupling and fragility.

   If the API calls the email service directly:
   - Email service is down → API request fails (or needs retry logic)
   - Email service is slow → API response is slow (user waits)
   - Adding a new consumer (Slack notifications) → change the API code

   With RabbitMQ:
   - Email service is down → messages queue up, processed when it recovers
   - Email service is slow → doesn't affect API response time
   - Adding Slack notifications → create a new queue, bind it, deploy a consumer
     The API code doesn't change at all.

   The API's job is to create executions and jobs. Post-processing
   (email, export, audit) is someone else's problem."


Q: "Explain the difference between your topic exchange and a fanout exchange."

A: "A fanout exchange broadcasts every message to ALL bound queues.
   A topic exchange routes based on PATTERNS in the routing key.

   Our 'batch.events' exchange is a topic exchange:
   - 'execution.created' → goes to execution-pipeline and audit-log
   - 'job.completed' → goes to result-orchestrator and audit-log
   - audit-log gets everything because its binding is '#' (wildcard)
   - result-orchestrator only gets job completions

   If we used fanout, result-orchestrator would receive execution.created
   events too, which it doesn't care about. It would have to filter and
   discard them. Topic exchanges let consumers subscribe to exactly the
   events they need."


Q: "What is at-least-once delivery and why does it matter?"

A: "At-least-once means every message is guaranteed to be processed at
   least one time, but might be processed more than once if a worker
   crashes after processing but before acknowledging.

   In our system: worker receives 'job.completed', sends the email,
   then ACKs. If it crashes after sending the email but before ACK,
   RabbitMQ re-delivers the message, and the email gets sent twice.

   This means our processing must be IDEMPOTENT — safe to run twice.
   For email: sending a duplicate notification is annoying but not
   catastrophic. For payment processing, you'd use a deduplication
   key (e.g., executionId) to detect and skip duplicates.

   The alternative is at-most-once (autoAck: true) where messages
   can be lost. For our use case, duplicate email > lost email."


Q: "How would you monitor this messaging system in production?"

A: "Four key metrics:

   1. Queue depth (messages waiting): if it's growing, consumers are
      slower than producers. Scale up workers or investigate slowness.

   2. Consumer count: if it drops to 0, no one is processing messages.
      Alert immediately.

   3. Message rate (published/sec vs delivered/sec): if published > delivered
      consistently, the queue is backing up.

   4. Unacknowledged messages: if high, workers are receiving but not
      finishing. Could indicate slow processing or a bug.

   In AWS: CloudWatch metrics for Amazon MQ or SQS.
   Locally: RabbitMQ Management UI at localhost:15672 shows all of these."
```

---

### Architecture Questions

```
Q: "How does this system scale?"

A: "Each component scales independently:

   API: Horizontal. Run 3 instances behind the ALB. They share PostgreSQL
   and Redis. Stateless — any instance can handle any request.

   Workers: Horizontal. docker compose up --scale worker=5. RabbitMQ
   distributes messages across all workers with fair dispatch (QoS=1).

   PostgreSQL: Vertical (bigger instance) or read replicas for read-heavy
   queries. The job queue uses FOR UPDATE SKIP LOCKED which scales well
   with concurrent runners.

   Redis: ElastiCache with read replicas for read-heavy caching.
   Or Redis Cluster for sharding across multiple nodes.

   RabbitMQ: Clustering for HA. Or replace with SQS+SNS in AWS
   for fully managed, auto-scaling messaging.

   The bottleneck is usually the report runner fleet (CPU-bound report
   generation), not the infrastructure. Add more runners to process
   more jobs in parallel."


Q: "What would you change for production?"

A: "Seven things:

   1. Replace Docker Compose with ECS/Kubernetes for orchestration
   2. Replace Docker PostgreSQL with RDS/Aurora (managed, Multi-AZ)
   3. Replace Docker Redis with ElastiCache (managed, failover)
   4. Replace Docker RabbitMQ with Amazon MQ or SQS+SNS (managed)
   5. Add authentication (JWT validation on API endpoints)
   6. Add structured logging (Serilog → CloudWatch Logs)
   7. Add metrics (Prometheus/CloudWatch for queue depth, cache hit rate,
      API latency, error rates)

   The CODE stays almost identical. The INFRASTRUCTURE changes from
   self-managed Docker containers to managed AWS services.
   That's the whole point of this learning progression."


Q: "Walk me through what happens when a new dataset arrives in production."

A: "1. DatasetMonitorWorker (background service) polls the external data
      source every N minutes for each panel (AU, UK, etc.).

   2. It acquires a PostgreSQL advisory lock to ensure only ONE pod
      runs the monitor for each panel (single-leader pattern).

   3. It detects a new dataset (e.g., new external dataset ID).

   4. It creates a Dataset record in PostgreSQL.

   5. It queries for all active DataTriggerDefinitions for that panel,
      using fair queuing (round-robin across organisations, priority-ordered).

   6. For each trigger, it creates a DataTriggerExecution (Pending) and
      a JobQueue entry. This is done in a batch with idempotency checks
      (skip if a pending/successful execution already exists for this
      trigger + dataset combination).

   7. Report runners poll GET /jobs/next-job, dequeue jobs, process reports,
      and call POST /jobs/{id}/complete with results.

   8. ResultOrchestratorWorker picks up completed executions, groups them
      by InvocationId and GroupId, sends emails, publishes to iPort,
      and updates the ExportStatus jsonb column.

   9. JobMaintenanceWorker periodically cleans up stuck jobs (running
      too long) and stale queue entries (never picked up).

   The Level 5 system models steps 4-8 with RabbitMQ replacing the
   polling in step 8. Steps 1-3 and 9 are simplified (manual trigger
   run instead of automatic dataset monitoring)."
```

---

### Quick-Fire Questions

```
Q: "What is FOR UPDATE SKIP LOCKED?"
A: "PostgreSQL row-level locking for concurrent job queues. FOR UPDATE locks
   the row. SKIP LOCKED skips already-locked rows instead of waiting.
   10 runners dequeue simultaneously without conflicts."

Q: "What is cache-aside?"
A: "Check cache first. Miss → query DB → store in cache → return.
   Writes invalidate the cache. Next read re-populates it."

Q: "What is a dead letter queue?"
A: "A queue where messages go after failing N times. Instead of retrying
   forever or losing the message, it's parked for manual inspection."

Q: "What is idempotency?"
A: "An operation that produces the same result whether run once or multiple
   times. Critical for at-least-once delivery — if a message is processed
   twice, the system state should be the same as processing it once."

Q: "What is an advisory lock?"
A: "A PostgreSQL application-level lock (not tied to a row or table).
   Used to ensure only one instance of the DatasetMonitorWorker runs
   per panel. If another pod tries to acquire the same lock, it fails
   and skips the cycle."

Q: "What is the difference between a queue and a topic exchange?"
A: "A queue stores messages until consumed (point-to-point).
   A topic exchange ROUTES messages to queues based on pattern matching
   (pub/sub). One event can go to multiple queues based on bindings."

Q: "What is backpressure?"
A: "When a consumer can't keep up with the producer. Messages pile up
   in the queue. Solutions: scale consumers, slow down producers,
   or use QoS/prefetch to limit how many messages a consumer receives."

Q: "What is eventual consistency?"
A: "After a write, not all readers see the update immediately.
   Our Redis cache is eventually consistent — after updating a trigger,
   the cache might serve stale data for up to 2 minutes (TTL).
   We mitigate this with explicit invalidation on writes."
```

---

## Quick Summary: The Level 5 Jump

```
Level 1: "I can build and run a container"
Level 2: "I can run multiple services together"
Level 3: "I can run this reliably in production"
Level 4: "I can build distributed systems with async processing"
Level 5: "I can design and explain a real production system end-to-end"
         PostgreSQL (FOR UPDATE SKIP LOCKED, advisory locks, soft delete, EF Core)
         Redis (cache-aside, explicit invalidation, TTL strategy, stampede prevention)
         RabbitMQ (topic exchange, event-driven, at-least-once, worker scaling)
         Background workers (consumer pattern, result orchestration, audit logging)
         React UI (visual proof of the entire pipeline)
         Interview-ready (can explain every design decision and trade-off)
```

The jump from Level 4 to Level 5 is where you go from "I understand these
technologies in isolation" to "I can explain how they work together in a
real production system, why each one was chosen, and what I'd change for
scale." That's the SDE-3 bar.
