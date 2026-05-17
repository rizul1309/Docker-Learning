# ============================================================
# MULTI-STAGE BUILD
# ============================================================
# Why? Your final production image should contain ONLY what's
# needed to RUN the app. Build tools, compilers, pip cache,
# dev headers — none of that should ship to production.
#
# Stage 1 (builder): Install dependencies (may need compilers)
# Stage 2 (runtime): Copy only the installed packages + your code
#
# Result: Image goes from ~400MB → ~80MB
# ============================================================


# -------------------- STAGE 1: BUILDER --------------------
FROM python:3.9 AS builder

WORKDIR /app

COPY requirements.txt .

# Install into a separate prefix so we can copy JUST the packages later
# --no-cache-dir = don't store pip's cache (saves space)
# --prefix=/install = install packages into /install instead of system-wide
RUN pip install --no-cache-dir --prefix=/install -r requirements.txt


# -------------------- STAGE 2: RUNTIME --------------------
# Start fresh with a slim image (no build tools, no compilers)
FROM python:3.9-slim

WORKDIR /app

# Copy ONLY the installed Python packages from the builder stage
# This is the magic — we get the packages without the build tools
COPY --from=builder /install /usr/local

# Copy application code
COPY . .

# ---------- SECURITY: Don't run as root ----------
# By default, containers run as root. If an attacker exploits your app,
# they get root access to the container. Creating a non-root user limits damage.
RUN addgroup --system appgroup && \
    adduser --system --ingroup appgroup appuser

# Switch to the non-root user for all subsequent commands and runtime
USER appuser

# Document the port
EXPOSE 5000

# ---------- HEALTHCHECK ----------
# Docker (and orchestrators like Kubernetes) will ping this endpoint
# every 30 seconds. If it fails 3 times in a row, the container is
# marked "unhealthy" and can be automatically restarted.
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:5000/health')" || exit 1

# ---------- Labels (image metadata) ----------
LABEL maintainer="your-team"
LABEL version="1.0.0"
LABEL description="Level 3 production Flask app"

# Start with gunicorn
# --workers=4 : spawn 4 worker processes (rule of thumb: 2 * CPU cores + 1)
# --access-logfile=- : print access logs to stdout (Docker captures stdout)
CMD ["gunicorn", "--bind", "0.0.0.0:5000", "--workers=4", "--access-logfile=-", "app:create_app()"]
