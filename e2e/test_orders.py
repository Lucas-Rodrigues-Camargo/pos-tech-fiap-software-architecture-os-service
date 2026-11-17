"""BDD against actual HTTP hosts, PostgreSQL, DynamoDB and SQS in LocalStack.
Only Mercado Pago is simulated: no external credentials or real money.
Start isolated test containers before executing pytest; see README.
"""
import base64
import hashlib
import hmac
import json
import os
from pathlib import Path
import subprocess
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest
import requests
from pytest_bdd import scenarios, given, when, then

scenarios("features/orders.feature")
ROOT = Path(__file__).resolve().parents[2]
KEY = "local-phase4-only-jwt-key-over-32-bytes"
CPF = "52998224725"
URLS = {"os": "http://localhost:8181", "billing": "http://localhost:8182", "execution": "http://localhost:8183"}


def token(role):
    def enc(value):
        return base64.urlsafe_b64encode(json.dumps(value, separators=(",", ":")).encode()).decode().rstrip("=")
    data = enc({"alg": "HS256", "typ": "JWT"}) + "." + enc({
        "iss": "OficinaMecanica", "aud": "OficinaMecanica.Customer",
        "exp": int(time.time()) + 1800, "cpf": CPF, "role": role})
    return data + "." + base64.urlsafe_b64encode(hmac.new(KEY.encode(), data.encode(), hashlib.sha256).digest()).decode().rstrip("=")


def call(service, method, path, body=None, role="Admin"):
    response = requests.request(method, URLS[service] + path, json=body, timeout=10, headers={
        "Authorization": "Bearer " + token(role), "Idempotency-Key": str(uuid.uuid4())})
    assert response.ok, (response.status_code, response.text)
    return response.json() if response.content else None


def await_status(service, path, status):
    deadline = time.monotonic() + 100
    while time.monotonic() < deadline:
        response = requests.get(URLS[service] + path, headers={"Authorization": "Bearer " + token("Admin")}, timeout=5)
        if response.ok and response.json()["status"] == status:
            return response.json()
        time.sleep(.25)
    raise AssertionError(f"{service} never reached {status}: {response.text}")


class Provider(BaseHTTPRequestHandler):
    payments = {}
    def log_message(self, *_):
        pass
    def do_POST(self):
        if self.headers.get("Transfer-Encoding") == "chunked":
            chunks = []
            while True:
                length = int(self.rfile.readline().strip(), 16)
                if length == 0:
                    self.rfile.readline()
                    break
                chunks.append(self.rfile.read(length))
                self.rfile.read(2)
            raw = b"".join(chunks)
        else:
            raw = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        body = json.loads(raw or b"{}")
        if self.path == "/checkout/preferences":
            payment_id = str(len(self.payments) + 1000)
            self.payments[payment_id] = {"id": int(payment_id), "external_reference": body["external_reference"],
                "transaction_amount": body["items"][0]["unit_price"], "currency_id": "BRL", "status": "approved"}
            result = {"init_point": "https://checkout.test/" + payment_id, "sandbox_init_point": "https://checkout.test/" + payment_id}
        else:
            result = {"status": "approved"}
        self.reply(result)
    def do_GET(self):
        self.reply(self.payments[self.path.rsplit("/", 1)[1]])
    def reply(self, value):
        self.send_response(200)
        self.send_header("content-type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(value).encode())


@pytest.fixture(scope="session", autouse=True)
def runtime():
    server = ThreadingHTTPServer(("127.0.0.1", 9199), Provider)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    queues = {}
    for service in URLS:
        result = subprocess.run(["docker", "exec", "oficina-phase4-aws", "awslocal", "sqs", "create-queue",
            "--queue-name", "e2e-" + service, "--attributes", "VisibilityTimeout=120"], capture_output=True, text=True, check=True)
        queues[service] = json.loads(result.stdout)["QueueUrl"].replace("localhost.localstack.cloud", "localhost")
    subprocess.run(["docker", "exec", "oficina-phase4-aws", "awslocal", "dynamodb", "create-table",
        "--table-name", "oficina-execution", "--attribute-definitions", "AttributeName=id,AttributeType=S",
        "--key-schema", "AttributeName=id,KeyType=HASH", "--billing-mode", "PAY_PER_REQUEST"], capture_output=True)
    processes, logs = [], []
    try:
        for service in URLS:
            env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development",
                Jwt__Key=KEY, Internal__Key="local-internal-key", AWS__Endpoint="http://localhost:4566",
                AWS_ACCESS_KEY_ID="test", AWS_SECRET_ACCESS_KEY="test", AWS_REGION="us-east-1",
                Messaging__InputQueue=queues[service], MercadoPago__AccessToken="test",
                MercadoPago__WebhookSecret="webhook-test", MercadoPago__NotificationUrl="https://test/webhook",
                MercadoPago__BaseUrl="http://localhost:9199/")
            env.update({"Messaging__Queues__" + name: url for name, url in queues.items()})
            if service != "execution":
                port, password = (5541, "local-os-only") if service == "os" else (5542, "local-billing-only")
                env["ConnectionStrings__Database"] = f"Host=localhost;Port={port};Database={service};Username=postgres;Password={password}"
            log = open(Path(__file__).parent / (service + ".log"), "w", encoding="utf-8")
            logs.append(log)
            project = ROOT / f"pos-tech-fiap-software-architecture-{service}-service" / "src"
            processes.append(subprocess.Popen(["dotnet", "run", "--no-build", "-c", "Release",
                "--project", str(project), "--urls", URLS[service]], env=env, stdout=log, stderr=subprocess.STDOUT))
        for service, url in URLS.items():
            deadline = time.monotonic() + 60
            while True:
                try:
                    if requests.get(url + "/ready", timeout=1).ok:
                        break
                except requests.RequestException:
                    pass
                assert time.monotonic() < deadline, f"{service} failed to start; inspect {service}.log"
                time.sleep(.25)
        yield
    finally:
        for process in processes:
            process.terminate()
        for process in processes:
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
        server.shutdown()
        for log in logs:
            log.close()


@given("the three services and their isolated databases are running", target_fixture="order")
def order():
    return {"id": str(uuid.uuid4())}


@when("a customer opens an order and the mechanic diagnoses it")
def diagnose(order):
    call("os", "PUT", "/api/customers/" + CPF, {"name": "Cliente de teste", "active": True})
    call("os", "POST", "/api/orders", {"orderId": order["id"], "cpf": CPF, "plate": "BRA2E19", "description": "Oil leak"}, "Customer")
    path = "/api/jobs/" + order["id"]
    await_status("execution", path, "Diagnosing")
    queue = call("execution", "GET", "/api/jobs")
    assert any(item["id"] == order["id"] for item in queue["items"])
    call("execution", "POST", path + "/diagnosis", {"description": "Replace seal", "amount": 150})
    await_status("os", "/api/orders/" + order["id"], "WaitingApproval")


@when("the customer approves and pays the budget")
def pay(order):
    path = "/api/budgets/" + order["id"]
    call("billing", "POST", path + "/decision", {"approved": True}, "Customer")
    checkout = call("billing", "POST", path + "/checkout", role="Customer")["checkoutUrl"]
    payment_id = checkout.rsplit("/", 1)[1]
    ts = str(int(time.time()))
    signature = hmac.new(b"webhook-test", f"id:{payment_id};request-id:e2e;ts:{ts};".encode(), hashlib.sha256).hexdigest()
    response = requests.post(URLS["billing"] + "/webhooks/mercadopago", params={"data.id": payment_id},
        headers={"x-request-id": "e2e", "x-signature": f"ts={ts},v1={signature}"}, json={}, timeout=10)
    assert response.ok, response.text
    await_status("os", "/api/orders/" + order["id"], "InExecution")


@when("the mechanic completes the repair")
def finish(order):
    call("execution", "POST", "/api/jobs/" + order["id"] + "/finish")


@then("the order is finished across the services")
def finished(order):
    await_status("os", "/api/orders/" + order["id"], "Finished")
    await_status("execution", "/api/jobs/" + order["id"], "Finished")
    await_status("billing", "/api/budgets/" + order["id"], "Paid")


@when("the customer rejects the budget")
def reject(order):
    call("billing", "POST", "/api/budgets/" + order["id"] + "/decision", {"approved": False}, "Customer")


@then("execution and billing are canceled before the order is canceled")
def canceled(order):
    await_status("os", "/api/orders/" + order["id"], "Canceled")
    await_status("execution", "/api/jobs/" + order["id"], "Canceled")
    await_status("billing", "/api/budgets/" + order["id"], "Canceled")
