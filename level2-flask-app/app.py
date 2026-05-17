from flask import Flask, jsonify
import os

app = Flask(__name__)

# Read database URL from environment variable (set in docker-compose.yml)
DATABASE_URL = os.environ.get("DATABASE_URL", "not-configured")


@app.route("/")
def home():
    return jsonify({
        "message": "Hello from the Flask app running in Docker!",
        "database": DATABASE_URL
    })


@app.route("/health")
def health():
    return jsonify({"status": "healthy"})


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000)
