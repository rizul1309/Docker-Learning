# 1. Start with a base Operating System with Python installed
FROM python:3.9-slim

# 2. Create a folder inside the container called /app
WORKDIR /app

# 3. Copy the file from your computer (current folder) into the container
COPY app.py .

# 4. The command to run when the container starts
CMD ["python", "app.py"]