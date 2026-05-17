# Level 3 Docker — Production-Grade Setup (Senior Engineer Level)

## What We Built

A production-ready Docker setup with **4 containers** working together:

```
┌──────────────────────────────────────────────────────────────┐
│                    YOUR MACHINE                              │
│                                                              │
│   Browser → http://localhost:8080                            │
│                    │                                         │
│              ┌─────┴──────┐                                  │
│              │   NGINX    │  ← Reverse proxy (the bouncer)   │
│              │  port 8080 │                                  │
│              └─────┬──────┘                                  │
│                    │ forwards to port 5000                   │
│              ┌─────┴──────┐                                  │
│              │   FLASK    │  ← Your Python app               │
│              │  (web)     │                                  │
│              └──┬─────┬───┘                                  │
│                 │     │                                      │
│          ┌──────┴┐   ┌┴────────┐                             │
│          │  DB   │   │  REDIS  │                             │
│          │Postgres│   │  Cache  │                             │
│          └───────┘   └─────────┘                             │
└──────────────────────────────────────────────────────────────┘
```

## What You Saw in the Browser

When you visited `http://localhost:8080`, you got:

```json
{
  "environment": "production",
  "message": "Level 3 Production App",
  "version": "1.0.0"
}
```

Here's what happened behind the scenes for that one request:

```
1. You typed http://localhost:8080 in your browser
2. Your machine's port 8080 is mapped to nginx container's port 80
3. Nginx received the request and forwarded it to the Flask container (port 5000)
4. Flask read the environment variables (FLASK_ENV, APP_VERSION) from docker-compose.yml
5. Flask returned the JSON response
6. Nginx passed it back to your browser
```

You did NOT hit Flask directly. Nginx sat in between. That's the production way.

---

## What's Different from Level 2?

| Aspect | Level 2 (Basic) | Level 3 (Production) |
|--------|-----------------|---------------------|
| Containers | 2 (web + db) | 4 (nginx + web + db + redis) |
| Image size | ~400MB | ~80MB (multi-stage build) |
| Runs as | root (dangerous) | non-root user (secure) |
| Internet access | Flask exposed directly | Only nginx exposed |
| Health checks | None | All 4 services have them |
| Resource limits | None | CPU + memory caps on all |
| Networks | 1 shared network | 2 isolated networks |
| Auto-restart | None | Self-healing on crash |
| Logs | Grow forever | Rotated (max 30MB) |
| depends_on | Just start order | Waits for HEALTHY status |

---

## File Structure

```
level3-production-app/
├── app/
│   ├── __init__.py          ← App factory pattern (create_app)
│   └── routes.py            ← API endpoints (/ and /health)
├── nginx/
│   └── nginx.conf           ← Reverse proxy configuration
├── Dockerfile               ← Multi-stage build with security
├── docker-compose.yml       ← 4 services, networks, volumes
├── requirements.txt         ← Python packages
├── .dockerignore            ← Files to exclude from build
└── README.md                ← This file
```

---

## CONCEPT 1: Multi-Stage Builds (The Dockerfile)

### The Problem

In Level 2, our Dockerfile used `python:3.9-slim` and installed packages directly.
That works, but some Python packages (like `psycopg2` for PostgreSQL) need C
compilers and header files to install. Those build tools add ~300MB+ to your image
but are NEVER needed when the app is actually running.

### The Solution: Two Stages

Our Dockerfile has TWO `FROM` lines. Each one starts a new "stage":

```dockerfile
# ---- STAGE 1: "builder" ----
FROM python:3.9 AS builder          # Full Python image (~900MB, has compilers)
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir --prefix=/install -r requirements.txt
#                                    ^^^^^^^^^^^^^^^^
#                                    Install packages into /install folder
#                                    (not the normal system location)

# ---- STAGE 2: "runtime" (this is what actually ships) ----
FROM python:3.9-slim                 # Slim image (~120MB, NO compilers)
WORKDIR /app
COPY --from=builder /install /usr/local    # ← THE MAGIC LINE
#    ^^^^^^^^^^^^^^^
#    Reach into Stage 1 and grab ONLY the installed packages
#    Stage 1 (with all its compilers) is thrown away

COPY . .                             # Copy your app code
```

### What Happens Visually

```
STAGE 1 "builder" (TEMPORARY)         STAGE 2 "runtime" (FINAL IMAGE)
┌──────────────────────────┐          ┌──────────────────────────┐
│ Full Python 3.9 (~900MB) │          │ Slim Python 3.9 (~120MB) │
│                          │          │                          │
│ ✓ C compilers            │          │ ✗ No compilers           │
│ ✓ Build headers          │          │ ✗ No build tools         │
│ ✓ pip cache              │          │ ✗ No pip cache           │
│                          │          │                          │
│ pip install runs here    │  COPY    │ ✓ Flask, gunicorn, etc.  │
│ packages saved to        │ ──────→  │   (copied from Stage 1)  │
│ /install folder          │ packages │ ✓ Your app code          │
│                          │  only    │                          │
│ Total: ~900MB            │          │ Total: ~80MB             │
└──────────────────────────┘          └──────────────────────────┘
        DISCARDED                            SHIPPED
        (not in final image)                 (this is your image)
```

### Why This Matters

- 50 servers pulling a 400MB image = 20GB network transfer per deploy
- 50 servers pulling an 80MB image = 4GB network transfer per deploy
- Deploys go from ~3 minutes to ~30 seconds
- Smaller image = fewer files = fewer potential security vulnerabilities

---

## CONCEPT 2: Non-Root User (Security)

### The Problem

By default, everything inside a Docker container runs as `root`. If an attacker
finds a vulnerability in your Flask app and exploits it, they get root access
inside the container. Root can:
- Read/modify any file
- Install malicious packages
- Potentially escape the container

### The Fix

```dockerfile
# Create a system group and user (no home directory, no login shell)
RUN addgroup --system appgroup && \
    adduser --system --ingroup appgroup appuser

# All commands after this line run as "appuser", not root
USER appuser
```

### What Changes

```
WITHOUT USER instruction:              WITH USER instruction:
┌─────────────────────────┐           ┌─────────────────────────┐
│ Container runs as ROOT  │           │ Container runs as       │
│                         │           │ "appuser"               │
│ Attacker gets:          │           │                         │
│ ✓ Read all files        │           │ Attacker gets:          │
│ ✓ Modify system files   │           │ ✗ Can't modify system   │
│ ✓ Install packages      │           │ ✗ Can't install anything│
│ ✓ Access other services │           │ ✗ Limited file access   │
│                         │           │                         │
│ DANGEROUS               │           │ DAMAGE LIMITED          │
└─────────────────────────┘           └─────────────────────────┘
```

### Rule of Thumb
Every production Dockerfile should have a `USER` instruction. If you see one
without it in a code review, flag it.

---

## CONCEPT 3: Health Checks

### What You Saw in the Logs

```
Container level3-production-app-db-1    Healthy
Container level3-production-app-redis-1 Healthy
Container level3-production-app-web-1   Healthy
```

Docker didn't just start the containers — it verified they were actually working.

### How It Works

Every X seconds, Docker runs a command inside the container. If it succeeds,
the container is "healthy". If it fails multiple times, it's "unhealthy".

```
Docker: "Hey Flask, are you alive?"
        → GET http://localhost:5000/health
        → Response: 200 OK {"status": "healthy"}
        → ✓ Container is HEALTHY

Docker: "Hey Flask, are you alive?"
        → GET http://localhost:5000/health
        → No response (timeout after 3s)
        → ✗ Retry 1 of 3...
        → ✗ Retry 2 of 3...
        → ✗ Retry 3 of 3...
        → Container marked UNHEALTHY → restart it
```

### Health Checks for Each Service

**Flask app** (in docker-compose.yml):
```yaml
healthcheck:
  test: ["CMD", "python", "-c", "import urllib.request; urllib.request.urlopen('http://localhost:5000/health')"]
  interval: 30s        # Check every 30 seconds
  timeout: 3s          # Give up after 3 seconds
  retries: 3           # Mark unhealthy after 3 failures
  start_period: 10s    # Wait 10s before first check (app needs time to boot)
```

**PostgreSQL**:
```yaml
healthcheck:
  test: ["CMD-SHELL", "pg_isready -U user -d mydb"]   # Built-in postgres check
  interval: 10s
  timeout: 5s
  retries: 5
```

**Redis**:
```yaml
healthcheck:
  test: ["CMD", "redis-cli", "ping"]    # Redis responds with "PONG"
  interval: 10s
  timeout: 3s
  retries: 3
```

### Why This Matters: depends_on with condition

In Level 2, `depends_on` only controlled START ORDER:
```yaml
# Level 2: "Start db container, then start web container"
# Problem: db container starts but PostgreSQL isn't ready yet
# Flask tries to connect → CONNECTION REFUSED error
depends_on:
  - db
```

In Level 3, we wait for HEALTHY status:
```yaml
# Level 3: "Start db, wait until pg_isready says OK, THEN start web"
# Flask only starts when PostgreSQL is actually accepting connections
depends_on:
  db:
    condition: service_healthy
```

This is why in your logs you saw:
```
Container level3-production-app-db-1    Waiting
Container level3-production-app-redis-1 Waiting
...
Container level3-production-app-db-1    Healthy     ← PostgreSQL ready
Container level3-production-app-redis-1 Healthy     ← Redis ready
Container level3-production-app-web-1   Waiting     ← NOW Flask starts
...
web-1 | Starting gunicorn 21.2.0                    ← Flask boots up
web-1 | GET /health HTTP/1.1 200                    ← Health check passes
Container level3-production-app-web-1   Healthy     ← Flask ready
...
nginx starts                                         ← FINALLY nginx starts
```

Everything started in the right order, and each service was verified healthy
before the next one began.

---

## CONCEPT 4: Nginx Reverse Proxy

### Why Not Just Expose Flask Directly?

In Level 2, your browser talked directly to Flask:
```
Level 2:  Browser → Flask (port 5000)     ← BAD for production
Level 3:  Browser → Nginx → Flask         ← GOOD for production
```

### What Nginx Does That Flask Can't (or Shouldn't)

| Task | Flask | Nginx |
|------|-------|-------|
| SSL/HTTPS termination | Slow, complex | Built for it |
| Serve static files (CSS/JS/images) | Slow (Python) | Blazing fast (C) |
| Handle 10,000 concurrent connections | Crashes | No problem |
| Rate limiting (block abusive clients) | Manual code | Built-in |
| Load balance across multiple app instances | Can't | Built-in |
| Buffer slow client uploads | Blocks a worker | Handles it |

### How the Request Flows

```
1. Browser sends request to localhost:8080
                    │
2. Docker maps port 8080 → nginx container port 80
                    │
3. Nginx receives the request
                    │
4. nginx.conf says: proxy_pass http://flask_app
   "flask_app" is defined as: server web:5000
   "web" is the Docker service name → Docker DNS resolves it
                    │
5. Nginx forwards request to Flask container on port 5000
                    │
6. Flask processes it, returns JSON
                    │
7. Nginx passes response back to browser
```

### The nginx.conf Explained

```nginx
upstream flask_app {
    server web:5000;        # "web" = Docker service name, resolved by Docker DNS
    # If scaling: server web_1:5000; server web_2:5000; (load balancing)
}

server {
    listen 80;              # Nginx listens on port 80 inside its container

    location / {
        proxy_pass http://flask_app;                    # Forward to Flask
        proxy_set_header Host $host;                    # Pass original hostname
        proxy_set_header X-Real-IP $remote_addr;        # Pass real client IP
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;     # HTTP or HTTPS
    }
}
```

Without those `proxy_set_header` lines, Flask would see every request as coming
from nginx's internal IP (like 172.18.0.2) instead of the actual client's IP.

### Security Headers

```nginx
add_header X-Frame-Options "SAMEORIGIN" always;       # Prevents clickjacking
add_header X-Content-Type-Options "nosniff" always;    # Prevents MIME sniffing
add_header X-XSS-Protection "1; mode=block" always;    # XSS protection
```

These headers tell browsers to enable security features. They're one-liners in
nginx but would require middleware in Flask.

### Key Point: Only Nginx is Exposed

In docker-compose.yml, only nginx has a `ports:` section:
```yaml
nginx:
  ports:
    - "8080:80"     # ← Accessible from your machine

web:
  # NO ports: section ← NOT accessible from your machine, only from nginx
```

Flask is completely hidden from the outside world. Only nginx can reach it.

---

## CONCEPT 5: Network Isolation

### The Setup

We defined TWO separate Docker networks:

```yaml
networks:
  frontend:    # For public-facing traffic
  backend:     # For internal data services
```

### Which Service Is on Which Network

| Service | frontend | backend | Why |
|---------|----------|---------|-----|
| nginx | ✓ | ✗ | Only handles incoming traffic |
| web (Flask) | ✓ | ✓ | Receives from nginx, talks to db/redis |
| db (PostgreSQL) | ✗ | ✓ | Internal only |
| redis | ✗ | ✓ | Internal only |

### Visualized

```
┌──────────── FRONTEND NETWORK ────────────┐
│                                          │
│   ┌───────┐           ┌──────┐          │
│   │ nginx │──────────→│ web  │          │
│   └───────┘           └──┬───┘          │
│                          │               │
└──────────────────────────┼───────────────┘
                           │
┌──────────── BACKEND NETWORK ─────────────┐
│                          │               │
│                       ┌──┴───┐           │
│                       │ web  │           │
│                       └──┬───┘           │
│                 ┌────────┼────────┐      │
│                 ▼                 ▼      │
│            ┌────────┐      ┌────────┐   │
│            │   db   │      │ redis  │   │
│            └────────┘      └────────┘   │
│                                          │
└──────────────────────────────────────────┘
```

### Why This Matters (Security)

Imagine nginx gets hacked. The attacker is now on the frontend network.

```
WITHOUT network isolation:
  Attacker → nginx → can reach db directly → STEAL ALL DATA

WITH network isolation (our setup):
  Attacker → nginx → can reach web → but NOT db or redis directly
  The database is on a completely separate network
```

The attacker would have to also compromise the Flask app to reach the database.
That's defense in depth — multiple layers of security.

---

## CONCEPT 6: Resource Limits

### The Problem

Without limits, one misbehaving container can eat ALL your server's resources:

```
Scenario: Flask app has a memory leak
WITHOUT limits: Flask slowly eats 16GB RAM → PostgreSQL starved → Redis dies → EVERYTHING CRASHES
WITH limits:    Flask hits 256MB cap → Docker kills JUST Flask → restarts it → db/redis fine
```

### How We Set Them

```yaml
web:
  deploy:
    resources:
      limits:                    # CEILING — container gets killed if it exceeds this
        cpus: '0.50'             # Max 50% of one CPU core
        memory: 256M             # Max 256MB RAM
      reservations:              # FLOOR — Docker guarantees at least this much
        cpus: '0.25'             # Guaranteed 25% CPU
        memory: 128M             # Guaranteed 128MB RAM
```

### Resource Budget for Our Stack

| Service | CPU Limit | Memory Limit | Why |
|---------|-----------|-------------|-----|
| nginx | 0.25 cores | 128MB | Lightweight, just proxying |
| web (Flask) | 0.50 cores | 256MB | App logic, needs more |
| db (PostgreSQL) | 0.50 cores | 512MB | Database needs most memory |
| redis | 0.25 cores | 128MB | In-memory cache, small dataset |
| **Total** | **1.50 cores** | **1024MB** | Fits on a small server |

### What Happens When a Limit is Hit

- **Memory limit exceeded** → Docker kills the container with OOMKilled (Out Of Memory)
  → `restart: unless-stopped` brings it back automatically
- **CPU limit exceeded** → Container is throttled (slowed down), NOT killed
  → Requests take longer but nothing crashes

---

## CONCEPT 7: Restart Policies (Self-Healing)

```yaml
restart: unless-stopped
```

### All Restart Options

| Policy | Behavior | Use Case |
|--------|----------|----------|
| `no` | Never restart (default) | One-off scripts, batch jobs |
| `always` | Restart no matter what, even after `docker stop` | Critical services |
| `on-failure` | Restart only if container exits with error code | Background workers |
| `unless-stopped` | Restart always EXCEPT when you manually stop it | **Most production apps** |

### Why `unless-stopped` Is the Sweet Spot

```
Container crashes at 3 AM?     → Auto-restarts ✓
You run `docker compose down`? → Stays stopped ✓ (respects your manual stop)
Server reboots?                → Auto-restarts ✓
```

Combined with health checks, this gives you self-healing:
1. App crashes → container exits → Docker restarts it
2. App hangs (not responding) → health check fails → Docker restarts it
3. No human intervention needed for common failures

---

## CONCEPT 8: Log Rotation

### The Problem

```yaml
# WITHOUT log rotation:
# Day 1:   logs = 50MB
# Day 30:  logs = 1.5GB
# Day 180: logs = 9GB
# Day 365: DISK FULL → everything crashes at 3 AM
```

### The Fix

```yaml
logging:
  driver: "json-file"
  options:
    max-size: "10m"       # Each log file maxes out at 10MB
    max-file: "3"         # Keep only 3 rotated files
```

### How It Works

```
app.log    (current, writing to this)     ← 10MB max
app.log.1  (previous, rotated)            ← 10MB max
app.log.2  (oldest, rotated)              ← 10MB max
                                          ─────────
                                          30MB TOTAL MAX

When app.log hits 10MB:
  app.log.2 is DELETED
  app.log.1 becomes app.log.2
  app.log   becomes app.log.1
  New empty app.log is created
```

Your logs will never exceed 30MB per service. This is one of those things that
seems minor but saves you from a 3 AM production outage.

---

## CONCEPT 9: Gunicorn Workers

### What You Saw in the Logs

```
web-1 | Starting gunicorn 21.2.0
web-1 | Listening at: http://0.0.0.0:5000
web-1 | Using worker: sync
web-1 | Booting worker with pid: 7
web-1 | Booting worker with pid: 8
web-1 | Booting worker with pid: 9
web-1 | Booting worker with pid: 10
```

Gunicorn spawned 4 worker processes. Each worker can handle one request at a time.

### Why 4 Workers?

```dockerfile
CMD ["gunicorn", "--bind", "0.0.0.0:5000", "--workers=4", ...]
```

Rule of thumb: `workers = 2 × CPU_CORES + 1`

For a 2-core machine: `2 × 2 + 1 = 5` (we used 4, close enough)

### Why Not Flask's Built-in Server?

```
Flask dev server:                    Gunicorn:
- Single process                    - Multiple worker processes
- Single thread                     - Each worker handles requests
- 1 request at a time               - 4 requests simultaneously
- Crashes under load                 - Production-grade
- No process management              - Auto-restarts dead workers
```

Flask's built-in server is for development only. Gunicorn (or uWSGI) is what
you use in production.

---

## CONCEPT 10: The App Factory Pattern

### Level 2 App (Simple)

```python
# Level 2: app.py
from flask import Flask
app = Flask(__name__)

@app.route("/")
def home():
    return "Hello"
```

### Level 3 App (Factory Pattern)

```python
# Level 3: app/__init__.py
def create_app():
    app = Flask(__name__)
    app.config["SQLALCHEMY_DATABASE_URI"] = os.environ.get("DATABASE_URL")
    from app.routes import main
    app.register_blueprint(main)
    return app
```

### Why the Change?

1. **Testability** — You can call `create_app()` with different configs for testing
2. **Blueprints** — Routes are organized in separate files (`routes.py`), not one giant file
3. **Config from environment** — `os.environ.get("DATABASE_URL")` reads from docker-compose
4. **Gunicorn compatibility** — `CMD ["gunicorn", ... "app:create_app()"]` calls the factory

This is how real Flask apps are structured. The single-file approach from Level 2
doesn't scale past a few routes.

---

## CONCEPT 11: .dockerignore (What NOT to Ship)

```
__pycache__          # Python bytecode (regenerated at runtime)
*.pyc                # Compiled Python files
.git                 # Git history (can be huge, not needed)
.gitignore           # Git config
.env                 # Local secrets (NEVER put in an image!)
*.md                 # Documentation (not needed at runtime)
tests/               # Test files (not needed in production)
.pytest_cache        # Test cache
.coverage            # Coverage reports
docker-compose*.yml  # Compose files (not needed inside the container)
```

Without `.dockerignore`, `COPY . .` copies EVERYTHING into the image, including:
- Your `.env` file with real passwords → **security risk**
- Your `.git` folder (could be 500MB+) → **bloated image**
- Test files → **unnecessary size**

---

## The Port 80 Error You Hit

```
Error: listen tcp 0.0.0.0:80: bind: An attempt was made to access a socket
in a way forbidden by its access permissions
```

Port 80 was already in use on your Windows machine (likely IIS, Skype, or
Windows HTTP service). We changed nginx to use port 8080 instead:

```yaml
# Before (failed):
ports:
  - "80:80"

# After (works):
ports:
  - "8080:80"     # Your machine's 8080 → nginx's 80
```

This is a common issue on Windows. In production on a Linux server, port 80
would typically be available.

---

## How to Run

```powershell
cd level3-production-app

# Start all 4 services (build + run)
docker compose up --build

# Visit in browser
http://localhost:8080           # Through nginx → Flask
http://localhost:8080/health    # Health check endpoint

# Run in background (detached)
docker compose up -d --build

# Check status of all containers
docker compose ps

# Check health status specifically
docker inspect --format='{{.State.Health.Status}}' level3-production-app-web-1

# View real-time resource usage (CPU, memory)
docker stats

# View logs
docker compose logs web        # Just Flask
docker compose logs nginx      # Just nginx
docker compose logs -f         # Follow all logs live

# Shell into a running container (for debugging)
docker exec -it level3-production-app-web-1 /bin/sh

# Stop everything
docker compose down

# Stop and DELETE all data (including database volume)
docker compose down -v

# Rebuild after changing code
docker compose up --build
```

---

## Quick Summary: What a Senior Engineer Knows

```
Level 1: "I can build and run a container"
         docker build, docker run, Dockerfile basics

Level 2: "I can run multiple services together"
         docker-compose, volumes, networking, environment variables

Level 3: "I can run this reliably in production"
         Multi-stage builds, non-root users, reverse proxy, health checks,
         resource limits, network isolation, log rotation, restart policies,
         production web servers (gunicorn), app factory pattern
```

The jump from Level 2 to Level 3 is where you go from "it works on my machine"
to "it works reliably at scale with security and observability."
