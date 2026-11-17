import unittest
from validate_deployment import validate, SERVICE


def valid():
    queues = {s: f"https://sqs.us-east-1.amazonaws.com/123456789012/oficina-{s}" for s in ("os", "billing", "execution")}
    return {
        "Jwt__Key": "x" * 40, "Internal__Key": "y" * 40, "AWS__Region": "us-east-1",
        "Messaging__InputQueue": queues[SERVICE],
        **{f"Messaging__Queues__{s}": url for s, url in queues.items()},
        "ConnectionStrings__Database": "Host=db.workshop.internal;Database=workshop;Username=app;Password=secret;SSL Mode=VerifyFull;Root Certificate=/app/rds-ca.pem",
        "DynamoDB__Table": "oficina-execution", "MercadoPago__AccessToken": "sandbox-credential",
        "MercadoPago__WebhookSecret": "z" * 32,
        "MercadoPago__NotificationUrl": "https://billing.workshop.com/webhooks/mercadopago"
    }


class DeploymentConfigTests(unittest.TestCase):
    def test_valid_configuration(self):
        validate(valid())

    def test_missing_configuration(self):
        config = valid()
        del config["Jwt__Key"]
        with self.assertRaises(ValueError):
            validate(config)

    def test_wrong_queue(self):
        config = valid()
        other = "billing" if SERVICE == "os" else "os"
        config["Messaging__InputQueue"] = config["Messaging__Queues__" + other]
        with self.assertRaises(ValueError):
            validate(config)

    def test_region_mismatch(self):
        config = valid()
        config["AWS__Region"] = "us-west-2"
        with self.assertRaises(ValueError):
            validate(config)

    def test_placeholders_and_short_keys(self):
        for value in ("REPLACE_WITH_SECRET", "short"):
            config = valid()
            config["Jwt__Key"] = value
            with self.assertRaises(ValueError):
                validate(config)

    def test_local_aws_and_static_credentials_rejected(self):
        for key in ("AWS__Endpoint", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY"):
            config = valid()
            config[key] = "unsafe"
            with self.assertRaises(ValueError):
                validate(config)

    def test_multiline_config_and_invalid_names_rejected(self):
        for key, value in (("SAFE", "one\ntwo"), ("bad=key", "value"), ("key", 123)):
            config = valid()
            config[key] = value
            with self.assertRaises(ValueError):
                validate(config)

    def test_tls_and_webhook(self):
        config = valid()
        config["ConnectionStrings__Database"] = "Host=database;SSL Mode=Require"
        if SERVICE != "execution":
            with self.assertRaises(ValueError):
                validate(config)
        config = valid()
        config["MercadoPago__NotificationUrl"] = "http://localhost"
        if SERVICE == "billing":
            with self.assertRaises(ValueError):
                validate(config)


if __name__ == "__main__":
    unittest.main()
