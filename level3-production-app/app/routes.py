from flask import Blueprint, jsonify
import os

main = Blueprint("main", __name__)


@main.route("/")
def home():
    return jsonify({
        "message": "Level 3 Production App",
        "environment": os.environ.get("FLASK_ENV", "unknown"),
        "version": os.environ.get("APP_VERSION", "unknown"),
    })


@main.route("/health")
def health():
    """Health check endpoint — used by Docker HEALTHCHECK and Kubernetes probes."""
    return jsonify({"status": "healthy"}), 200
