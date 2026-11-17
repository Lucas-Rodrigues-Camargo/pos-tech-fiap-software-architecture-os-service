"""Validate deploy inputs without contacting AWS or printing secret values."""
import argparse
import json
from pathlib import Path
import re
from urllib.parse import urlparse

SERVICE = "os"
PLACEHOLDER = re.compile(r"REPLACE|PLACEHOLDER|RDS_HOST|YOUR_HOST|ACCOUNT|local-only|test-only", re.I)


def validate(config):
    if not isinstance(config, dict):
        raise ValueError("Configuration must be a JSON object")
    for key, value in config.items():
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", key):
            raise ValueError("Invalid environment variable name")
        if not isinstance(value, str) or "\n" in value or "\r" in value:
            raise ValueError(f"{key}: expected a single-line string")
        if PLACEHOLDER.search(value):
            raise ValueError(f"{key}: replace the example value")
    required = ["Jwt__Key", "AWS__Region", "Messaging__InputQueue"]
    required += [f"Messaging__Queues__{service}" for service in ("os", "billing", "execution")]
    required += ["DynamoDB__Table"] if SERVICE == "execution" else ["ConnectionStrings__Database"]
    if SERVICE == "os":
        required += ["Internal__Key"]
    if SERVICE == "billing":
        required += ["MercadoPago__AccessToken", "MercadoPago__WebhookSecret", "MercadoPago__NotificationUrl"]
    for key in required:
        if not config.get(key, "").strip():
            raise ValueError(f"{key}: required")
    for key in ("Jwt__Key", "Internal__Key"):
        if key in config and len(config[key].encode()) < 32:
            raise ValueError(f"{key}: minimum 32 bytes")
    for key in ("AWS__Endpoint", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "MercadoPago__BaseUrl"):
        if key in config:
            raise ValueError(f"{key}: not allowed for this Kubernetes deployment")
    if config.get("Messaging__Enabled", "true").lower() == "false":
        raise ValueError("Messaging must be enabled")
    for key in ["Messaging__InputQueue"] + [f"Messaging__Queues__{s}" for s in ("os", "billing", "execution")]:
        url = urlparse(config[key])
        if url.scheme != "https" or not url.hostname or not url.hostname.endswith((".amazonaws.com", ".amazonaws.com.cn")):
            raise ValueError(f"{key}: expected HTTPS AWS SQS URL")
        if not url.hostname.startswith("sqs." + config["AWS__Region"] + "."):
            raise ValueError(f"{key}: queue region must match AWS__Region")
        if not re.fullmatch(r"/[0-9]{12}/[A-Za-z0-9_.-]+", url.path) or url.query or url.fragment:
            raise ValueError(f"{key}: invalid queue path")
    queues = [config[f"Messaging__Queues__{s}"] for s in ("os", "billing", "execution")]
    if len(set(queues)) != 3:
        raise ValueError("Each service must have its own input queue")
    if config["Messaging__InputQueue"] != config[f"Messaging__Queues__{SERVICE}"]:
        raise ValueError("Input queue must match this service")
    if SERVICE != "execution":
        connection = config["ConnectionStrings__Database"]
        if not re.search(r"(?:^|;)\s*SSL Mode\s*=\s*VerifyFull\s*(?:;|$)", connection, re.I):
            raise ValueError("Database connection must use SSL Mode=VerifyFull")
        if not re.search(r"(?:^|;)\s*Root Certificate\s*=\s*/app/rds-ca.pem\s*(?:;|$)", connection, re.I):
            raise ValueError("Database connection must use the bundled RDS CA")
    if SERVICE == "billing":
        url = urlparse(config["MercadoPago__NotificationUrl"])
        if url.scheme != "https" or not url.hostname or url.hostname in ("localhost", "127.0.0.1"):
            raise ValueError("MercadoPago__NotificationUrl: public HTTPS endpoint required")
        if len(config["MercadoPago__WebhookSecret"]) < 16:
            raise ValueError("MercadoPago__WebhookSecret: invalid secret length")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config", type=Path)
    args = parser.parse_args()
    try:
        validate(json.loads(args.config.read_text(encoding="utf-8")))
    except (ValueError, OSError):
        # Never echo exception data: invalid inputs may contain secret values.
        raise SystemExit("Invalid deployment configuration; check required keys, placeholders, URLs, TLS and secret lengths. See docs/aws-existing-infra.md.")
    print(f"Deployment configuration validated for {SERVICE}; no AWS request made.")
