# Batch Processing System — Level 5

A fully working **7-container distributed system** that models a real production batch report processing pipeline. Built for learning .NET, PostgreSQL, Redis, RabbitMQ, and React — with interview-ready depth.

---

## What This Is

A local replica of a production batch-processing-service that automatically runs reports when new data arrives, queues them for processing, and delivers results via email and iPort. Every technology choice is explained in the accompanying guides.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          YOUR MACHINE                                    │
│                                                                          │
│   http://localhost:8080          → React Dashboard (via Nginx)           │
│   http://localhost:8080/api/...  → .NET 9 API (via Nginx)               │
│   http://localhost:15672         → RabbitMQ Management UI               │
│   localhost:5433                 → PostgreSQL (DBeaver/pgAdmin)          │
│                                                                          │
│              ┌──────────┐                                                │
│              │  NGINX   │  Routes /api/* → API, /* → React UI           │
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
│                      │  WORKER    │  Consumes events, sends emails       │
│                      └────────────┘                                      │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Tech Stack

| Technology | Role | Version |
|-----------|------|---------|
| .NET / C# | API + Worker | 9.0 |
| PostgreSQL | Database (source of truth) | 16 |
| Redis | Caching layer | 7 |
| RabbitMQ | Event-driven messaging | 3.13 |
| React | Dashboard UI | 19 |
| Nginx | Reverse proxy + routing | Alpine |
| Docker Compose | Orchestration | v5+ |
| Entity Framework Core | ORM | 9.0 |

---

## Prerequisites

- **Docker Desktop** — [Download](https://www.docker.com/products/docker-desktop/)
- **Git** — for cloning
- **DBeaver** (optional) — [Download](https://dbeaver.io/download/) for database inspection
- No .NET SDK, Node.js, or other runtimes needed — everything runs in Docker.

---

## Quick Start

```bash
# Clone the repo
git clone https://github.com/YOUR_USERNAME/YOUR_REPO.git
cd level5-batch-processing-system

# Build and start all 7 containers
docker compose up --build

# Wait ~40 seconds for RabbitMQ to fully start
# You'll see: "worker-1 | [ResultOrchestrator] Listening for job.completed events..."
```

Open in your browser:

| URL | What |
|-----|------|
| http://localhost:8080 | React Dashboard |
| http://localhost:15672 | RabbitMQ Management UI (guest/guest) |

---

## What You Can Do

### From the Dashboard (http://localhost:8080)

1. **See 3 pre-seeded triggers** — Finance Team Weekly Report, Monthly Client Export, Marketing Dashboard Feed
2. **Click "Run ▶"** on any trigger — creates an execution + queues a job
3. **Click "Dequeue & Process Job"** — simulates a report runner processing the job
4. **Click on a trigger name** — see execution history (status transitions)
5. **Click "Inspect Keys"** — see what's cached in Redis and TTL countdown
6. **Click "Flush Cache"** then "Refresh" — watch cache repopulate

### From the Terminal (curl)

```bash
# List all triggers
curl http://localhost:8080/api/v1/triggers | jq

# Run a single trigger
curl -X POST http://localhost:8080/api/v1/triggers/aaaa1111-1111-1111-1111-111111111111/run | jq

# Run a BATCH (shared InvocationId for grouped email)
curl -X POST http://localhost:8080/api/v1/triggers/run-batch \
  -H "Content-Type: application/json" \
  -d '{"triggerIds":["aaaa1111-1111-1111-1111-111111111111","aaaa3333-3333-3333-3333-333333333333"]}' | jq

# Dequeue a job (simulating a report runner)
curl http://localhost:8080/api/v1/jobs/next-job | jq

# Complete a job (replace JOB_ID)
curl -X POST http://localhost:8080/api/v1/jobs/JOB_ID/complete \
  -H "Content-Type: application/json" \
  -d '{"success":true,"executionTimeSeconds":5}' | jq

# Check queue statistics
curl http://localhost:8080/api/v1/jobs/statistics | jq

# Inspect group state (replace INVOCATION_ID from batch-run response)
curl http://localhost:8080/api/v1/executions/group/INVOCATION_ID | jq

# Inspect Redis cache
curl http://localhost:8080/api/v1/cache/keys | jq

# Flush Redis cache
curl -X DELETE http://localhost:8080/api/v1/cache/flush | jq

# See RabbitMQ topology
curl http://localhost:8080/api/v1/events/topology | jq
```

### From DBeaver (Database)

Connect with:
- Host: `localhost`
- Port: `5433`
- Database: `batchprocessing`
- Username: `batchuser`
- Password: `batchpass`

```sql
-- See all triggers
SELECT "DataTriggerId", "TriggerName", "EmailTo", "IsActive" FROM "DataTriggerDefinitions";

-- See executions with group info
SELECT "DataTriggerExecutionId", "InvocationId", "ResultStatus", "DateCreated" 
FROM "DataTriggerExecutions" ORDER BY "DateCreated" DESC;

-- See the job queue
SELECT * FROM "JobQueues";

-- See datasets
SELECT * FROM "Datasets";
```

### From RabbitMQ UI (http://localhost:15672)

- Login: `guest` / `guest`
- **Queues tab** — see message counts, consumer counts
- **Exchanges tab** — click `batch.events` → see routing bindings
- **Stop the worker** (`docker compose stop worker`) → run a trigger → watch messages accumulate → start worker → watch them drain

---

## The Pipeline Flow

```
1. User clicks "Run ▶" (or new data arrives automatically)
   → API creates: Dataset + DataTriggerExecution (Pending) + JobQueue entry
   → API publishes "execution.created" event to RabbitMQ

2. Report runner calls GET /api/v1/jobs/next-job
   → Dequeues oldest pending job (FOR UPDATE SKIP LOCKED)
   → API publishes "job.started" event

3. Runner processes the report, calls POST /api/v1/jobs/{id}/complete
   → Updates execution: status → Success, timestamps, paths
   → Deletes JobQueue row
   → Checks group completion (are all executions in this InvocationId done?)
   → Publishes "job.completed" event with groupComplete: true/false

4. Worker consumes "job.completed" event
   → If groupComplete: false → DEFERS email (waits for group)
   → If groupComplete: true → SENDS consolidated email for entire group
   → Always publishes to iPort (per-execution, not grouped)
```

---

## Key Features Demonstrated

| Feature | Where |
|---------|-------|
| **Redis Cache-Aside** | GET /triggers, GET /jobs/statistics — check Redis first, DB on miss |
| **Cache Invalidation** | POST/PUT/DELETE triggers → delete Redis keys |
| **RabbitMQ Topic Exchange** | One event routes to multiple queues by pattern |
| **Email Grouping** | Batch-run shares InvocationId → email deferred until group complete |
| **Strategy Pattern** | Worker dispatches to EmailExportHandler + IPortExportHandler independently |
| **FOR UPDATE SKIP LOCKED** | Concurrent job dequeue without conflicts |
| **Soft Delete** | Triggers/Reports use DateDeleted + EF query filters |
| **Multi-stage Docker Build** | SDK for build, slim runtime for production |
| **Network Isolation** | Frontend network (nginx↔api↔ui) vs Backend network (api↔db↔redis↔rabbitmq) |
| **Health Checks** | API checks PostgreSQL + Redis; Docker uses health for dependency ordering |

---

## Testing the Grouping Logic (End-to-End)

This is the most important test — it proves the email deferral and consolidated delivery:

```bash
# 1. Run a batch of 2 triggers
curl -X POST http://localhost:8080/api/v1/triggers/run-batch \
  -H "Content-Type: application/json" \
  -d '{"triggerIds":["aaaa1111-1111-1111-1111-111111111111","aaaa3333-3333-3333-3333-333333333333"]}' | jq
# Note the invocationId

# 2. Complete FIRST job
curl http://localhost:8080/api/v1/jobs/next-job | jq
curl -X POST http://localhost:8080/api/v1/jobs/JOB_ID/complete \
  -H "Content-Type: application/json" -d '{"success":true,"executionTimeSeconds":5}' | jq
# Response: "Group NOT complete yet (1/2) — email DEFERRED"

# 3. Check worker logs
docker compose logs worker --tail 10
# Shows: [EmailExportHandler] ⏳ DEFERRING email — group NOT complete (1/2)

# 4. Complete SECOND job
curl http://localhost:8080/api/v1/jobs/next-job | jq
curl -X POST http://localhost:8080/api/v1/jobs/JOB_ID/complete \
  -H "Content-Type: application/json" -d '{"success":true,"executionTimeSeconds":3}' | jq
# Response: "Group is COMPLETE — email will be sent"

# 5. Check worker logs
docker compose logs worker --tail 10
# Shows: [EmailExportHandler] ✓ Group COMPLETE (2/2). Sending consolidated email...
```

---

## Project Structure

```
level5-batch-processing-system/
├── src/
│   ├── Api/                          .NET 9 Web API
│   │   ├── Data/BatchDbContext.cs    EF Core DbContext + seed data
│   │   ├── Entities/                 Domain models (Trigger, Execution, Job, etc.)
│   │   ├── Program.cs               All endpoints + Redis + RabbitMQ
│   │   ├── Api.csproj               NuGet packages
│   │   └── appsettings.json         Configuration
│   │
│   ├── Worker/                       Background worker (RabbitMQ consumer)
│   │   ├── Program.cs               ResultOrchestratorWorker + AuditLogWorker
│   │   └── Worker.csproj            NuGet packages
│   │
│   └── ui/                           React dashboard
│       ├── src/App.jsx               Main dashboard component
│       ├── package.json              Dependencies
│       ├── Dockerfile                Multi-stage build (Node → Nginx)
│       └── vite.config.js            Vite config
│
├── nginx/nginx.conf                  Reverse proxy routing
├── Dockerfile.api                    Multi-stage .NET build
├── Dockerfile.worker                 Multi-stage .NET build
├── docker-compose.yml                7-service orchestration
├── .dockerignore                     Build exclusions
│
├── BATCH_PROCESSING_SYSTEM_GUIDE.md  Main guide (2500+ lines, 11 sections)
└── RABBITMQ_GUIDE.md                 RabbitMQ from zero to advanced (900+ lines)
```

---

## Guides

| Guide | Lines | What It Covers |
|-------|-------|----------------|
| [BATCH_PROCESSING_SYSTEM_GUIDE.md](./BATCH_PROCESSING_SYSTEM_GUIDE.md) | 2500+ | Full system architecture, every technology choice explained, PostgreSQL patterns, Redis caching strategies, RabbitMQ event-driven design, polling vs event-driven analysis, end-to-end verification walkthrough, 20+ interview Q&A |
| [RABBITMQ_GUIDE.md](./RABBITMQ_GUIDE.md) | 900+ | RabbitMQ from zero — core concepts, exchange patterns, Management UI tab-by-tab, message lifecycle, reliability, advanced concepts (DLQ, TTL, clustering), debugging, hands-on verification |

---

## Common Commands

```bash
# Start everything
docker compose up --build

# Start in background
docker compose up --build -d

# Stop everything (data preserved)
docker compose down

# Stop and DELETE all data (fresh start)
docker compose down -v

# View worker logs
docker compose logs worker -f

# View API logs
docker compose logs api -f

# Stop just the worker (to see messages queue up in RabbitMQ)
docker compose stop worker

# Start the worker back
docker compose start worker

# Scale workers (parallel processing)
docker compose up -d --scale worker=3

# Shell into PostgreSQL
docker exec -it level5-batch-processing-system-db-1 psql -U batchuser -d batchprocessing

# Shell into Redis
docker exec -it level5-batch-processing-system-redis-1 redis-cli
```

---

## Ports

| Port | Service | Notes |
|------|---------|-------|
| 8080 | Nginx → API + UI | Main entry point |
| 15672 | RabbitMQ Management UI | guest/guest |
| 5433 | PostgreSQL | Mapped to 5433 (not 5432) to avoid conflicts with local PostgreSQL |

---

## Environment Variables

All configured in `docker-compose.yml`:

| Variable | Service | Value |
|----------|---------|-------|
| `ConnectionStrings__BatchDb` | API | `Host=db;Database=batchprocessing;Username=batchuser;Password=batchpass` |
| `Redis__ConnectionString` | API | `redis:6379` |
| `RabbitMQ__Host` | API + Worker | `rabbitmq` |
| `ASPNETCORE_URLS` | API | `http://+:8080` |
| `POSTGRES_USER` | DB | `batchuser` |
| `POSTGRES_PASSWORD` | DB | `batchpass` |
| `POSTGRES_DB` | DB | `batchprocessing` |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| `localhost:8080` shows JSON instead of dashboard | Clear browser cache or hard refresh (Ctrl+Shift+R) |
| DBeaver "password authentication failed" | Use port **5433** (not 5432). Run `docker compose down -v` then `up --build` for fresh DB |
| Nginx crash loop | Check `nginx/nginx.conf` has `events {}` and `http {}` wrapper |
| Worker can't connect to RabbitMQ | Normal — RabbitMQ takes 20-30 seconds to start. Worker retries 30 times |
| Trigger names show as `..` | Click "Flush Cache" then "Refresh" in the dashboard |
| Port 5432 already in use | That's why we use 5433. Your machine has another PostgreSQL running |

---

## License

Learning project. Use freely.
