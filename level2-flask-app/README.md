# Level 2 Docker - Flask App with PostgreSQL

## What Did We Build?

We built a **real-world Docker setup** with TWO services running together:

1. **A Flask web app** (your Python code)
2. **A PostgreSQL database** (a real database server)

Both run in separate containers but can talk to each other — just like they would
in a real production environment.

---

## What's That Output in the Browser?

When you visited `http://localhost:5000`, you saw:

```json
{
  "database": "postgresql://user:pass@db:5432/mydb",
  "message": "Hello from the Flask app running in Docker!"
}
```

This tells you:
- Your Flask app is running inside a container and responding to HTTP requests
- It can see the `DATABASE_URL` environment variable that was set in `docker-compose.yml`
- The `db` in the URL is the PostgreSQL container's hostname (Docker networking magic)

---

## File-by-File Explanation

### 1. `app.py` — The Flask Application

```python
from flask import Flask, jsonify
import os

app = Flask(__name__)
DATABASE_URL = os.environ.get("DATABASE_URL", "not-configured")
```

**What's happening:**
- `Flask` is a lightweight Python web framework — it lets you create web APIs easily
- `os.environ.get("DATABASE_URL")` reads the environment variable we set in docker-compose.yml
- This is how real apps get their config — NOT hardcoded, but from environment variables
- If DATABASE_URL isn't set, it defaults to "not-configured"

```python
@app.route("/")
def home():
    return jsonify({"message": "Hello...", "database": DATABASE_URL})
```

**What's happening:**
- `@app.route("/")` means: when someone visits `http://localhost:5000/`, run this function
- `jsonify()` converts a Python dictionary into a JSON response (what you saw in the browser)

```python
@app.route("/health")
def health():
    return jsonify({"status": "healthy"})
```

**What's happening:**
- A `/health` endpoint — visit `http://localhost:5000/health` to see it
- In production, monitoring tools ping this endpoint to check if your app is alive
- If it stops responding, the system knows something is wrong and can restart the container

---

### 2. `requirements.txt` — Python Dependencies

```
flask==3.0.0
gunicorn==21.2.0
```

**What's happening:**
- Lists the Python packages your app needs
- `flask` — the web framework
- `gunicorn` — a production-grade web server (Flask's built-in server is only for development)
- Version pinning (`==3.0.0`) ensures everyone gets the exact same version — no surprises

**Why not just `pip install flask` in the Dockerfile directly?**
- Having a separate file makes dependencies trackable and reproducible
- Docker can cache this layer (more on this below)

---

### 3. `Dockerfile` — The Build Recipe

Think of this as a step-by-step recipe that Docker follows to create your app's image.

```dockerfile
FROM python:3.9-slim
```
**→ "Start with a mini Linux OS that has Python 3.9 installed"**
- `slim` variant is smaller (~120MB vs ~900MB for the full image)
- Less stuff = smaller image = faster deploys = fewer security vulnerabilities

```dockerfile
WORKDIR /app
```
**→ "Create a /app folder and work from there"**
- All subsequent commands run inside /app
- Like doing `cd /app` but also creates the folder if it doesn't exist

```dockerfile
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
```
**→ "Copy ONLY the dependency list first, then install them"**

This is the **LAYER CACHING TRICK** — the most important optimization to understand:


```
Scenario A: You change app.py (but NOT requirements.txt)

  Step 1: FROM python:3.9-slim        → CACHED (no change)
  Step 2: WORKDIR /app                 → CACHED (no change)
  Step 3: COPY requirements.txt .      → CACHED (file didn't change!)
  Step 4: RUN pip install ...          → CACHED (requirements same!)
  Step 5: COPY . .                     → RE-RUNS (app.py changed)
  Step 6: CMD ...                      → RE-RUNS

  Result: Build takes ~2 seconds (skips pip install!)

Scenario B: You add a new package to requirements.txt

  Step 1: FROM python:3.9-slim        → CACHED
  Step 2: WORKDIR /app                 → CACHED
  Step 3: COPY requirements.txt .      → RE-RUNS (file changed!)
  Step 4: RUN pip install ...          → RE-RUNS (must reinstall)
  Step 5: COPY . .                     → RE-RUNS
  Step 6: CMD ...                      → RE-RUNS

  Result: Build takes ~30 seconds (pip install runs again)
```

**If we had done `COPY . .` first (copying everything at once), pip install would
re-run EVERY time you change ANY file — even a typo fix in app.py. That wastes
minutes on every build.**

```dockerfile
COPY . .
```
**→ "Now copy everything else (app.py, etc.) into the container"**

```dockerfile
EXPOSE 5000
```
**→ "Document that this container listens on port 5000"**
- This does NOT actually open the port — it's just metadata/documentation
- The actual port mapping happens in docker-compose.yml (`ports: "5000:5000"`)
  or with `docker run -p 5000:5000`

```dockerfile
ENV FLASK_ENV=production
```
**→ "Set an environment variable inside the container"**
- Your Python code can read this with `os.environ["FLASK_ENV"]`
- Can be overridden at runtime (docker-compose environment section wins)

```dockerfile
CMD ["gunicorn", "--bind", "0.0.0.0:5000", "app:app"]
```
**→ "When the container starts, run this command"**
- `gunicorn` — production web server (handles multiple requests concurrently)
- `--bind 0.0.0.0:5000` — listen on all network interfaces, port 5000
- `app:app` — in the file `app.py`, use the variable named `app` (your Flask instance)
- `0.0.0.0` means "accept connections from anywhere" (not just localhost)

---

### 4. `docker-compose.yml` — Running Multiple Containers Together

This is where it gets interesting. Instead of running two separate `docker run`
commands, docker-compose lets you define and run everything with one command.

```yaml
version: '3.8'
```
**→ Compose file format version (just boilerplate)**

#### Service 1: `web` (Your Flask App)

```yaml
services:
  web:
    build: .
```
**→ "Build an image from the Dockerfile in the current directory"**
- Same as running `docker build .` manually

```yaml
    ports:
      - "5000:5000"
```
**→ "Map port 5000 on your machine to port 5000 in the container"**

How port mapping works:
```
Your Machine (Host)          Container
┌──────────────────┐        ┌──────────────────┐
│                  │        │                  │
│  localhost:5000 ──────────── 0.0.0.0:5000    │
│                  │        │  (gunicorn)      │
│  Browser hits    │        │                  │
│  this port       │        │  Flask app runs  │
│                  │        │  here            │
└──────────────────┘        └──────────────────┘
```
- Left side (`5000`) = port on YOUR machine (what you type in the browser)
- Right side (`5000`) = port INSIDE the container (where gunicorn listens)
- You could do `"8080:5000"` and then visit `localhost:8080` instead

```yaml
    environment:
      - DATABASE_URL=postgresql://user:pass@db:5432/mydb
```
**→ "Set this environment variable inside the Flask container"**
- `user:pass` — database username and password
- `@db` — hostname of the database. This is the KEY part!
- `db` is the name of the other service defined below
- Docker automatically creates a network and DNS so containers can find each other by service name
- `5432` — PostgreSQL's default port
- `mydb` — the database name

```yaml
    depends_on:
      - db
```
**→ "Start the db container BEFORE starting web"**
- Note: this only waits for the container to START, not for PostgreSQL to be READY
- In production, you'd add retry logic or a healthcheck

#### Service 2: `db` (PostgreSQL Database)

```yaml
  db:
    image: postgres:15
```
**→ "Pull the official PostgreSQL 15 image from Docker Hub"**
- No `build:` needed — we're using a pre-built image, not building our own
- Docker Hub has thousands of ready-to-use images (postgres, redis, nginx, etc.)

```yaml
    volumes:
      - pgdata:/var/lib/postgresql/data
```
**→ "Store database files in a persistent volume"**

Why this matters:
```
WITHOUT volumes:
  docker compose down → Container deleted → ALL DATA GONE FOREVER

WITH volumes:
  docker compose down → Container deleted → Data safe in "pgdata" volume
  docker compose up   → New container → Reconnects to "pgdata" → Data is back!
```
- `/var/lib/postgresql/data` is where PostgreSQL stores its files inside the container
- `pgdata` is a named volume that Docker manages on your host machine
- Only `docker compose down -v` (with -v flag) deletes volumes

```yaml
    environment:
      - POSTGRES_USER=user
      - POSTGRES_PASSWORD=pass
      - POSTGRES_DB=mydb
```
**→ "Configure the database with these credentials"**
- The official postgres image reads these env vars on first startup
- Creates the user, sets the password, and creates the database automatically

#### Volumes Section

```yaml
volumes:
  pgdata:
```
**→ "Declare a named volume called pgdata"**
- Docker manages where this is stored on your machine
- You can inspect it with `docker volume inspect level2-flask-app_pgdata`

---

### 5. `.dockerignore` — What Docker Should Skip

```
__pycache__
*.pyc
.git
.env
```

**→ "Don't copy these files into the container during COPY . ."**
- `__pycache__` / `*.pyc` — Python bytecode files (useless in the container)
- `.git` — your git history (can be huge, not needed in the container)
- `.env` — local environment files that might contain secrets

Without .dockerignore, `COPY . .` copies EVERYTHING, including stuff you don't want.

---

## How the Containers Talk to Each Other (Networking)

```
┌─────────────────────────────────────────────────┐
│           Docker Network (auto-created)          │
│                                                  │
│  ┌─────────────┐         ┌─────────────┐        │
│  │   web        │         │   db         │       │
│  │  (Flask)     │────────→│ (PostgreSQL) │       │
│  │             │  "db"    │              │       │
│  │  port 5000  │ resolves │  port 5432   │       │
│  └──────┬──────┘  to this └──────────────┘       │
│         │         container's IP                  │
└─────────┼────────────────────────────────────────┘
          │
          │ port mapping (5000:5000)
          │
    ┌─────┴──────┐
    │ Your Machine│
    │ Browser     │
    │ localhost:  │
    │ 5000        │
    └─────────────┘
```

- Docker Compose creates a private network for all services
- Containers use service names as hostnames (`web` can reach `db` by name)
- Only ports explicitly mapped in `ports:` are accessible from your machine
- The database (port 5432) is NOT exposed to your machine — only the Flask app can reach it

---

## Useful Commands

```bash
# Start everything
docker compose up

# Start in background (detached mode)
docker compose up -d

# See running containers
docker compose ps

# View logs
docker compose logs web        # just the Flask app
docker compose logs db         # just PostgreSQL
docker compose logs -f         # follow all logs in real-time

# Stop everything
docker compose down

# Stop and DELETE all data (including database)
docker compose down -v

# Rebuild after changing Dockerfile or requirements.txt
docker compose up --build

# Shell into a running container (for debugging)
docker exec -it level2-flask-app-web-1 /bin/sh
```

---

## Quick Recap

| Concept | What It Does | Why It Matters |
|---------|-------------|----------------|
| Layer caching | Reuses unchanged build steps | Faster builds (seconds vs minutes) |
| EXPOSE | Documents the port | Doesn't open it — just documentation |
| Port mapping | `-p 5000:5000` | Connects your machine to the container |
| Environment vars | Config without hardcoding | Same image works in dev/staging/prod |
| docker-compose | Runs multiple containers | One command starts your whole stack |
| Volumes | Persists data | Database survives container restarts |
| Networking | Containers find each other by name | No hardcoded IPs needed |
| .dockerignore | Excludes files from build | Smaller images, no leaked secrets |
