# ---------- WHY THIS ORDER MATTERS (Layer Caching) ----------
#
# Docker caches each line (layer). If a layer hasn't changed,
# Docker reuses the cached version instead of re-running it.
#
# By copying requirements.txt FIRST and installing dependencies,
# Docker only re-installs packages when requirements.txt changes.
# If you only changed app.py, Docker skips the pip install step
# entirely — saving minutes on every build.

# Step 1: Base image with Python
FROM python:3.9-slim

# Step 2: Set working directory inside the container
WORKDIR /app

# Step 3: Copy ONLY requirements.txt first (layer caching trick)
COPY requirements.txt .

# Step 4: Install dependencies
#   --no-cache-dir = don't store pip's download cache (keeps image smaller)
RUN pip install --no-cache-dir -r requirements.txt

# Step 5: NOW copy the rest of your code
#   If you change app.py, only this layer and below re-run.
#   The pip install layer above stays cached.
COPY . .

# Step 6: Document which port the app listens on
#   EXPOSE doesn't actually publish the port — it's documentation.
#   You still need -p 5000:5000 at runtime (or ports: in compose).
EXPOSE 5000

# Step 7: Set environment variable
#   This can be overridden by docker-compose or docker run -e
ENV FLASK_ENV=production

# Step 8: Start the app with gunicorn (production-grade server)
#   "app:app" means: in the file app.py, use the variable called app
CMD ["gunicorn", "--bind", "0.0.0.0:5000", "app:app"]
