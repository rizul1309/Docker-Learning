# Docker Learning — From Zero to Production Distributed Systems

A progressive, hands-on Docker learning path that takes you from running your first container to building a fully distributed 7-service production system with .NET, PostgreSQL, Redis, RabbitMQ, and React.

Each level builds on the previous one, introducing new concepts with heavily commented code and detailed guides explaining **why** each technology choice was made — not just how to use it.

---

## Architecture Overview

```
Level 1: Hello World          → 1 container  (Python print statement)
Level 2: Flask + PostgreSQL   → 2 containers (web app + database)
Level 3: Production Setup     → 4 containers (nginx + Flask + PostgreSQL + Redis)
Level 4: .NET + Messaging     → 5 containers (nginx + .NET API + worker + Redis + RabbitMQ)
Level 5: Batch Processing     → 7 containers (nginx + .NET API + worker + React UI + PostgreSQL + Redis + RabbitMQ)
```

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Level 1 — Hello Docker](#level-1--hello-docker)
- [Level 2 — Flask App with PostgreSQL](#level-2--flask-app-with-postgresql)
- [Level 3 — Production-Grade Setup](#level-3--production-grade-setup)
- [Level 4 — .NET, Redis & RabbitMQ](#level-4--net-redis--rabbitmq)
- [Level 5 — Batch Processing System](#level-5--batch-processing-system)
- [AWS & Observability Guide](#aws--observability-guide)
- [Technologies Covered](#technologies-covered)
- [Learning Path](#learning-path)
- [Project Structure](#project-structure)
- [Common Commands](#common-commands)
- [Troubleshooting](#troubleshooting)
- [License](#license)

---

## Prerequisites

- **Docker Desktop** — [Download](https://www.docker.com/products/docker-desktop/) (includes Docker Engine + Docker Compose)
- **Git** — for cloning the repository
- **A web browser** — for accessing UIs (RabbitMQ Management, React Dashboard)
- **curl or Postman** (optional) — for testing API endpoints
- **DBeaver** (optional) — [Download](https://dbeaver.io/download/) for database inspection

> No Python, .NET SDK, Node.js, or other runtimes needed on your machine — everything runs inside Docker containers.

---

## Quick Start

```bash
# Clone the repository
git clone https://github.com/YOUR_USERNAME/docker-learning.git
cd docker-learning

# Start with Level 1 (simplest)
docker build -t hello-docker .
docker run hello-docker

# Or jump straight to Level 5 (full system)
cd level5-batch-processing-system
docker compose up --build
# Open http://localhost:8080 for the React Dashboard
# Open http://localhost:15672 for RabbitMQ Management UI (guest/guest)
```

---

## Level 1 — Hello Docker

**Folder:** Root (`Dockerfile` + `app.py`)

**What you learn:**
- What a Docker image and container are
- Writing your first `Dockerfile`
- `FROM`, `WORKDIR`, `COPY`, `CMD` instructions
- Building and running a container

**Architecture:**
```
┌─────────────────────┐
│  Python Container    │
│  prints "Hello..."   │
└─────────────────────┘
```

**How to run:**
```bash
docker build -t hello-docker .
docker run hello-docker
# Output: Hello, i am running inside the docker container
```

**Key concepts:**
| Concept | What It Does |
|---------|-------------|
| `FROM python:3.9-slim` | Base image with Python pre-installed |
| `WORKDIR /app` | Sets the working directory inside the container |
| `COPY app.py .` | Copies your code into the container |
| `CMD ["python", "app.py"]` | Command to run when container starts |

---

## Level 2 — Flask App with PostgreSQL

**Folder:** `level2-flask-app/`

**What you learn:**
- Docker Compose for multi-container applications
- Container networking (services communicate by name)
- Environment variables for configuration
- Volumes for data persistence
- Layer caching optimization in Dockerfiles
- `.dockerignore` for excluding files from builds

**Architecture:**
```
┌─────────────────────────────────────────────┐
│           Docker Network                     │
│                                              │
│  ┌──────────┐         ┌──────────────┐      │
│  │  Flask   │────────→│  PostgreSQL  │      │
│  │  (web)   │  "db"   │  (db)        │      │
│  │ port 5000│         │  port 5432   │      │
│  └────┬─────┘         └──────────────┘      │
└───────┼──────────────────────────────────────┘
        │ port mapping 5000:5000
   http://localhost:5000
```

**How to run:**
```bash
cd level2-flask-app
docker compose up --build
# Visit http://localhost:5000
```

**Key concepts:**
| Concept | What It Does |
|---------|-------------|
| `docker-compose.yml` | Defines and runs multiple containers together |
| `depends_on` | Controls container start order |
| `volumes: pgdata` | Persists database data across restarts |
| Service name as hostname | `db` resolves to PostgreSQL container's IP |
| Layer caching | Copy `requirements.txt` before code for faster rebuilds |

---

## Level 3 — Production-Grade Setup

**Folder:** `level3-production-app/`

**What you learn:**
- Multi-stage Docker builds (image size: ~400MB → ~80MB)
- Non-root user for security
- Nginx as a reverse proxy
- Health checks with `depends_on: condition: service_healthy`
- Resource limits (CPU + memory caps)
- Network isolation (frontend vs backend)
- Log rotation
- Gunicorn production web server
- Flask app factory pattern

**Architecture:**
```
┌──────────────────────────────────────────────────────┐
│                                                       │
│   http://localhost:8080                               │
│              │                                        │
│        ┌─────┴──────┐                                │
│        │   NGINX    │  Reverse proxy                 │
│        └─────┬──────┘                                │
│              │                                        │
│        ┌─────┴──────┐                                │
│        │   FLASK    │  Python app (gunicorn)         │
│        └──┬─────┬───┘                                │
│           │     │                                    │
│     ┌─────┴┐  ┌─┴──────┐                            │
│     │  DB  │  │  REDIS  │                            │
│     │ Pg15 │  │  Cache  │                            │
│     └──────┘  └─────────┘                            │
└──────────────────────────────────────────────────────┘
```

**How to run:**
```bash
cd level3-production-app
docker compose up --build
# Visit http://localhost:8080
```

**Key concepts:**
| Concept | Level 2 (Basic) | Level 3 (Production) |
|---------|-----------------|---------------------|
| Image size | ~400MB | ~80MB (multi-stage) |
| Runs as | root | non-root user |
| Internet access | Flask exposed directly | Only nginx exposed |
| Health checks | None | All services |
| Resource limits | None | CPU + memory caps |
| Networks | 1 shared | 2 isolated (frontend/backend) |
| Restart policy | None | Self-healing |
| Logs | Grow forever | Rotated (max 30MB) |

---

## Level 4 — .NET, Redis & RabbitMQ

**Folder:** `level4-dotnet-redis-messaging/`

**What you learn:**
- .NET 8 Minimal APIs (C# — compiled, statically typed)
- Redis caching patterns (Cache-Aside, TTL, eviction policies)
- Redis Pub/Sub for real-time messaging
- RabbitMQ message broker (exchanges, queues, bindings)
- Exchange types: Direct, Fanout, Topic
- Producer-Consumer pattern with background workers
- Why enterprises choose .NET over Python for backends
- Separate worker containers for async processing

**Architecture:**
```
┌──────────────────────────────────────────────────────┐
│                                                       │
│   http://localhost:8080      (API via Nginx)          │
│   http://localhost:15672     (RabbitMQ UI)            │
│              │                                        │
│        ┌─────┴──────┐                                │
│        │   NGINX    │                                │
│        └─────┬──────┘                                │
│              │                                        │
│        ┌─────┴──────┐                                │
│        │  .NET API  │  C# Web API                    │
│        └──┬─────┬───┘                                │
│           │     │                                    │
│     ┌─────┴┐  ┌─┴──────────┐                        │
│     │REDIS │  │  RABBITMQ  │                         │
│     │Cache │  │  Msg Broker│                         │
│     └──────┘  └──────┬─────┘                         │
│                      │                               │
│                ┌─────┴──────┐                        │
│                │   WORKER   │  Background consumer   │
│                └────────────┘                        │
└──────────────────────────────────────────────────────┘
```

**How to run:**
```bash
cd level4-dotnet-redis-messaging
docker compose up --build
# API: http://localhost:8080
# RabbitMQ UI: http://localhost:15672 (guest/guest)
```

**Key API endpoints:**
```bash
# Redis caching
curl "http://localhost:8080/cache/set?key=user:1&value=John"
curl "http://localhost:8080/cache/get?key=user:1"
curl "http://localhost:8080/cache/demo-aside?userId=42"

# RabbitMQ messaging
curl -X POST "http://localhost:8080/messages/publish?message=Hello"
curl "http://localhost:8080/messages/status"
curl -X POST "http://localhost:8080/messages/broadcast?message=System+update"
curl -X POST "http://localhost:8080/messages/topic?routingKey=order.created&message=New+order"
```

**Includes detailed guide:** `DOTNET_REDIS_RABBITMQ_GUIDE.md` — 600+ lines covering technology decision matrices, Redis data types, caching patterns, RabbitMQ exchange types, and the producer-consumer pattern.

---

## Level 5 — Batch Processing System

**Folder:** `level5-batch-processing-system/`

**What you learn:**
- Full distributed system design (7 containers)
- PostgreSQL with Entity Framework Core ORM
- Redis Cache-Aside pattern with invalidation
- RabbitMQ Topic Exchange for event-driven architecture
- Job queue with `FOR UPDATE SKIP LOCKED` (concurrent dequeue)
- Email grouping logic (batch runs share InvocationId)
- Strategy Pattern for export handlers
- React dashboard with Vite
- Multi-stage builds for .NET 9, Node.js, and Nginx
- Network isolation (frontend vs backend)
- Health checks with dependency ordering

**Architecture:**
```
┌──────────────────────────────────────────────────────────────────┐
│                                                                   │
│   http://localhost:8080          → React Dashboard (via Nginx)    │
│   http://localhost:8080/api/...  → .NET 9 API (via Nginx)        │
│   http://localhost:15672         → RabbitMQ Management UI        │
│   localhost:5433                 → PostgreSQL                     │
│                                                                   │
│              ┌──────────┐                                        │
│              │  NGINX   │  Routes /api/* → API, /* → React UI   │
│              └────┬─────┘                                        │
│           ┌───────┴────────┐                                     │
│     ┌─────┴──────┐   ┌────┴────┐                                │
│     │  .NET API  │   │ React   │                                 │
│     │  (C#)      │   │ UI      │                                 │
│     └──┬────┬────┘   └─────────┘                                 │
│        │    │    │                                                │
│   ┌────┴┐ ┌┴────┴──┐ ┌──────────┐                               │
│   │ DB  │ │ REDIS  │ │ RABBITMQ │                                │
│   │Pg16 │ │ Cache  │ │ Events   │                                │
│   └─────┘ └────────┘ └────┬─────┘                                │
│                            │                                     │
│                      ┌─────┴──────┐                              │
│                      │  WORKER    │  Consumes events             │
│                      └────────────┘                              │
└──────────────────────────────────────────────────────────────────┘
```

**How to run:**
```bash
cd level5-batch-processing-system
docker compose up --build
# Wait ~40 seconds for RabbitMQ to fully start
# Dashboard: http://localhost:8080
# RabbitMQ UI: http://localhost:15672 (guest/guest)
```

**What you can do from the dashboard:**
1. See 3 pre-seeded triggers (Finance, Marketing, Monthly Client)
2. Click "Run ▶" to fire a trigger → creates execution + queues job
3. Click "Dequeue & Process Job" → simulates report runner
4. Click trigger name → see execution history with status transitions
5. Click "Inspect Keys" → see Redis cache contents and TTL
6. Click "Flush Cache" → watch cache repopulate on next request

**The pipeline flow:**
```
1. User clicks "Run ▶" (or new data arrives)
   → API creates: Dataset + Execution (Pending) + JobQueue entry
   → API publishes "execution.created" event to RabbitMQ

2. Report runner calls GET /api/v1/jobs/next-job
   → Dequeues oldest pending job (FOR UPDATE SKIP LOCKED)

3. Runner completes: POST /api/v1/jobs/{id}/complete
   → Updates execution status
   → Checks group completion (all executions in InvocationId done?)
   → Publishes "job.completed" event with groupComplete flag

4. Worker consumes "job.completed" event
   → If groupComplete: false → DEFERS email (waits for group)
   → If groupComplete: true → SENDS consolidated email
```

**Testing email grouping (end-to-end):**
```bash
# 1. Run a batch of 2 triggers (shared InvocationId)
curl -X POST http://localhost:8080/api/v1/triggers/run-batch \
  -H "Content-Type: application/json" \
  -d '{"triggerIds":["aaaa1111-1111-1111-1111-111111111111","aaaa3333-3333-3333-3333-333333333333"]}'

# 2. Complete first job → email DEFERRED
curl http://localhost:8080/api/v1/jobs/next-job
curl -X POST http://localhost:8080/api/v1/jobs/JOB_ID/complete \
  -H "Content-Type: application/json" -d '{"success":true,"executionTimeSeconds":5}'

# 3. Complete second job → email SENT (group complete)
curl http://localhost:8080/api/v1/jobs/next-job
curl -X POST http://localhost:8080/api/v1/jobs/JOB_ID/complete \
  -H "Content-Type: application/json" -d '{"success":true,"executionTimeSeconds":3}'

# 4. Check worker logs to see grouping in action
docker compose logs worker --tail 20
```

**Includes detailed guides:**
- `BATCH_PROCESSING_SYSTEM_GUIDE.md` — 2500+ lines covering full system architecture, every technology explained, PostgreSQL patterns, Redis strategies, RabbitMQ design, 20+ interview Q&A
- `RABBITMQ_GUIDE.md` — 900+ lines on RabbitMQ from zero to advanced

---

## AWS & Observability Guide

**File:** `AWS_AND_OBSERVABILITY_SDE3_GUIDE.md`

A comprehensive 24-section reference for SDE-3 level AWS infrastructure and observability, covering:

| Section | Topics |
|---------|--------|
| Networking | VPC, Subnets, Security Groups, CIDR |
| Compute | EC2, Auto Scaling, Launch Templates |
| Containers | ECS, EKS |
| Load Balancing | ALB, NLB, Target Groups |
| CI/CD | CodePipeline, CodeBuild, CodeDeploy, ECR |
| IaC | CloudFormation, Terraform |
| Storage | S3 lifecycle, replication |
| Caching | ElastiCache (Redis/Memcached), CloudFront |
| Databases | RDS, DynamoDB, Aurora |
| Messaging | SQS, SNS, EventBridge |
| Secrets | Secrets Manager, Parameter Store, KMS |
| IAM | Roles, Policies, Cross-Account |
| Observability | CloudWatch, X-Ray, Grafana, Loki, Tempo |
| Operations | Incident Response, Cost Optimization, DR |

---

## Technologies Covered

| Technology | First Introduced | Purpose |
|-----------|-----------------|---------|
| Docker | Level 1 | Containerization |
| Python / Flask | Level 1-3 | Web application (interpreted) |
| PostgreSQL | Level 2 | Relational database |
| Docker Compose | Level 2 | Multi-container orchestration |
| Nginx | Level 3 | Reverse proxy, load balancing |
| Redis | Level 3-5 | Caching, pub/sub |
| Gunicorn | Level 3 | Production WSGI server |
| C# / .NET | Level 4-5 | Web API + Worker (compiled) |
| RabbitMQ | Level 4-5 | Message broker, event-driven architecture |
| Entity Framework Core | Level 5 | ORM for PostgreSQL |
| React + Vite | Level 5 | Frontend dashboard |
| Multi-stage builds | Level 3+ | Optimized Docker images |

---

## Learning Path

```
YOU ARE HERE
     │
     ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Level 1: "I can build and run a container"                          │
│   → Dockerfile basics, docker build, docker run                     │
├─────────────────────────────────────────────────────────────────────┤
│ Level 2: "I can run multiple services together"                     │
│   → docker-compose, volumes, networking, environment variables      │
├─────────────────────────────────────────────────────────────────────┤
│ Level 3: "I can run this reliably in production"                    │
│   → Multi-stage builds, security, health checks, resource limits    │
├─────────────────────────────────────────────────────────────────────┤
│ Level 4: "I understand enterprise backends and messaging"           │
│   → .NET, Redis caching patterns, RabbitMQ exchanges, workers      │
├─────────────────────────────────────────────────────────────────────┤
│ Level 5: "I can design and build distributed systems"               │
│   → Full pipeline: API + DB + Cache + Queue + Worker + UI           │
│   → Event-driven architecture, job queuing, batch processing        │
├─────────────────────────────────────────────────────────────────────┤
│ AWS Guide: "I can own infrastructure and observability at scale"    │
│   → VPC, ECS, ALB, CI/CD, CloudWatch, Grafana, incident response   │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Project Structure

```
docker-learning/
│
├── app.py                              Level 1: Hello World script
├── Dockerfile                          Level 1: Basic Dockerfile
├── README.md                           This file
│
├── level2-flask-app/                   Level 2: Flask + PostgreSQL
│   ├── app.py                          Flask application
│   ├── Dockerfile                      Layer caching, gunicorn
│   ├── docker-compose.yml              2 services (web + db)
│   ├── requirements.txt                Python dependencies
│   ├── .dockerignore                   Build exclusions
│   └── README.md                       Detailed walkthrough
│
├── level3-production-app/              Level 3: Production Setup
│   ├── app/
│   │   ├── __init__.py                 App factory pattern
│   │   └── routes.py                   API endpoints
│   ├── nginx/nginx.conf                Reverse proxy config
│   ├── Dockerfile                      Multi-stage build + non-root user
│   ├── docker-compose.yml              4 services + networks + health checks
│   ├── requirements.txt                Flask + gunicorn + psycopg2 + redis
│   ├── .dockerignore
│   └── README.md                       Production concepts explained
│
├── level4-dotnet-redis-messaging/      Level 4: .NET + Redis + RabbitMQ
│   ├── src/
│   │   ├── Api/                        .NET 8 Minimal API
│   │   │   ├── Program.cs             All endpoints (Redis + RabbitMQ demos)
│   │   │   ├── Api.csproj             NuGet packages
│   │   │   └── appsettings.json       Configuration
│   │   └── Worker/                     Background RabbitMQ consumer
│   │       ├── Program.cs             Message processing logic
│   │       └── Worker.csproj          Dependencies
│   ├── nginx/nginx.conf               Proxy to .NET API
│   ├── Dockerfile.api                  Multi-stage .NET build (SDK → runtime)
│   ├── Dockerfile.worker               Multi-stage .NET build (SDK → runtime)
│   ├── docker-compose.yml              5 services
│   ├── DOTNET_REDIS_RABBITMQ_GUIDE.md  Technology deep-dive guide
│   └── .dockerignore
│
├── level5-batch-processing-system/     Level 5: Full Distributed System
│   ├── src/
│   │   ├── Api/                        .NET 9 Web API
│   │   │   ├── Data/BatchDbContext.cs  EF Core DbContext + seed data
│   │   │   ├── Entities/              Domain models
│   │   │   ├── Program.cs            All endpoints + pipeline logic
│   │   │   └── Api.csproj            NuGet packages
│   │   ├── Worker/                     RabbitMQ consumer (Strategy Pattern)
│   │   │   ├── Program.cs            ResultOrchestrator + AuditLog workers
│   │   │   └── Worker.csproj
│   │   └── ui/                         React dashboard
│   │       ├── src/App.jsx            Dashboard component
│   │       ├── package.json           React + Vite
│   │       └── Dockerfile             Multi-stage (Node → Nginx)
│   ├── nginx/nginx.conf               Routes /api/* → API, /* → UI
│   ├── Dockerfile.api                  .NET 9 multi-stage build
│   ├── Dockerfile.worker               .NET 9 multi-stage build
│   ├── docker-compose.yml              7 services
│   ├── BATCH_PROCESSING_SYSTEM_GUIDE.md  System architecture guide (2500+ lines)
│   ├── RABBITMQ_GUIDE.md              RabbitMQ from zero (900+ lines)
│   └── README.md                       Level 5 specific instructions
│
├── AmazonCloudWatchAgent/              CloudWatch Agent config samples
│   ├── amazon-cloudwatch-agent.yaml
│   ├── env-config.json
│   ├── log-config.json
│   └── Configs/                        SSM parameter store configs
│
└── AWS_AND_OBSERVABILITY_SDE3_GUIDE.md  24-section AWS/Observability reference
```

---

## Common Commands

### Docker Basics (Level 1)
```bash
docker build -t my-app .                    # Build an image
docker run my-app                           # Run a container
docker images                               # List images
docker ps                                   # List running containers
docker ps -a                                # List all containers (including stopped)
docker logs <container-id>                  # View container logs
docker exec -it <container-id> /bin/sh      # Shell into a container
```

### Docker Compose (Level 2+)
```bash
docker compose up --build                   # Build and start all services
docker compose up --build -d                # Same but in background (detached)
docker compose down                         # Stop all services (data preserved)
docker compose down -v                      # Stop and DELETE all data (fresh start)
docker compose ps                           # List running services
docker compose logs <service> -f            # Follow logs for a service
docker compose logs --tail 20               # Last 20 lines from all services
docker compose restart <service>            # Restart a specific service
docker compose stop <service>               # Stop a specific service
docker compose up -d --scale worker=3       # Scale a service to 3 instances
docker stats                                # Real-time resource usage
```

### Database Access (Level 5)
```bash
# Shell into PostgreSQL
docker exec -it level5-batch-processing-system-db-1 psql -U batchuser -d batchprocessing

# Or connect with DBeaver/pgAdmin:
# Host: localhost, Port: 5433, DB: batchprocessing, User: batchuser, Pass: batchpass
```

### Redis Access (Level 4-5)
```bash
# Shell into Redis
docker exec -it level5-batch-processing-system-redis-1 redis-cli

# Useful Redis commands:
# KEYS *              → list all keys
# GET <key>           → get a value
# TTL <key>           → time to live in seconds
# FLUSHALL            → delete everything
```

---

## Ports Reference

| Port | Service | Level | Notes |
|------|---------|-------|-------|
| 5000 | Flask (direct) | 2 | Level 2 only |
| 8080 | Nginx → App | 3, 4, 5 | Main entry point |
| 15672 | RabbitMQ Management UI | 4, 5 | Login: guest/guest |
| 5433 | PostgreSQL | 5 | Mapped to 5433 to avoid conflicts |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Port 80 already in use | Use port 8080 (already configured in Level 3+) |
| Port 5432 already in use | Level 5 uses 5433 to avoid conflicts |
| `localhost:8080` shows JSON instead of dashboard | Hard refresh (Ctrl+Shift+R) or clear browser cache |
| RabbitMQ connection refused | Normal on startup — takes 20-30s. Services retry automatically |
| DBeaver "password authentication failed" | Use port 5433, user: batchuser, pass: batchpass |
| `docker compose up` fails with "network not found" | Run `docker compose down` first, then `up --build` |
| Worker can't connect to RabbitMQ | Wait for RabbitMQ health check to pass (~30s) |
| Image build fails | Run `docker compose down -v` then `docker compose up --build` |
| Out of disk space | Run `docker system prune -a` to clean unused images/containers |

---

## Key Design Patterns Demonstrated

| Pattern | Where | What It Does |
|---------|-------|-------------|
| Cache-Aside (Lazy Loading) | Level 4, 5 | Check cache → miss → query DB → cache result |
| Cache Invalidation | Level 5 | Write to DB → delete stale cache keys |
| Producer-Consumer | Level 4, 5 | API publishes events → Worker consumes them |
| Strategy Pattern | Level 5 | Worker dispatches to EmailHandler + IPortHandler |
| FOR UPDATE SKIP LOCKED | Level 5 | Concurrent job dequeue without conflicts |
| Event-Driven Architecture | Level 5 | Topic exchange routes events to multiple consumers |
| Multi-Stage Build | Level 3+ | Separate build tools from runtime (smaller images) |
| Network Isolation | Level 3+ | Frontend/backend network separation |
| Health Check Dependency | Level 3+ | Services wait for dependencies to be healthy |
| Soft Delete | Level 5 | `DateDeleted` + EF query filters |

---

## License

Learning project. Use freely.
