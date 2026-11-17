"""Generate reviewable evidence from test output; never invent test counts."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
reports = root / "tests" / "TestResults"
coverage_files = list(reports.glob("**/coverage.cobertura.xml"))
test_files = list(reports.glob("**/*.trx"))
if not coverage_files or not test_files:
    raise SystemExit("Run dotnet test with --logger trx and coverage collection first")
coverage = ET.parse(max(coverage_files, key=lambda p: p.stat().st_mtime)).getroot()
trx = ET.parse(max(test_files, key=lambda p: p.stat().st_mtime)).getroot()
counters = trx.find(".//{*}Counters")
if counters is None or int(counters.attrib.get("executed", 0)) == 0 or int(counters.attrib.get("failed", 0)):
    raise SystemExit("Latest TRX must contain passing tests")
data = {
    "service": "os",
    "tests": {key: int(counters.attrib.get(key, 0)) for key in ("total", "executed", "passed", "failed")},
    "coverage": {
        "lines": round(100 * float(coverage.attrib["line-rate"]), 2),
        "branches": round(100 * float(coverage.attrib["branch-rate"]), 2),
        "linesCovered": int(coverage.attrib["lines-covered"]),
        "linesValid": int(coverage.attrib["lines-valid"]),
        "threshold": 80,
        "excluded": "generated source under obj only"
    },
    "scope": "Local/CI tests; PostgreSQL or DynamoDB LocalStack, API host, workflow and SQS adapter",
    "awsDeploymentVerified": False,
    "mercadoPagoSandboxVerified": False,
    "sonarServerVerified": False,
}
if data["coverage"]["lines"] < 80:
    raise SystemExit("Coverage threshold not reached")
(root / "docs" / "verification.json").write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
markdown = f"""# Evidências verificadas — {data['service']}

Gerado por scripts/record_evidence.py a partir do último TRX aprovado e do relatório Cobertura.

| Medida | Resultado |
|---|---:|
| Testes executados | {data['tests']['executed']} |
| Testes aprovados | {data['tests']['passed']} |
| Testes falhos | {data['tests']['failed']} |
| Cobertura de linhas | {data['coverage']['lines']:.2f}% |
| Cobertura de branches | {data['coverage']['branches']:.2f}% |
| Mínimo de linhas exigido no CI | 80% |

Código gerado em obj é excluído; código de negócio, APIs e adaptadores utilizados são incluídos. Classes de bootstrap sem uso foram removidas; os testes exercitam o workflow real.

Os testes de persistência usam somente o banco correspondente ao serviço: PostgreSQL em container ou DynamoDB no LocalStack. APIs, autorização, idempotência, fila e Saga são exercitados conforme a matriz dos requisitos. O BDD integrado possui três cenários (sucesso, rejeição e cancelamento pago com estorno).

Os resultados locais podem ser obtidos em Linux/Docker quando a política de aplicativos do Windows bloquear DLLs de teste. O CI usa Linux. Artefatos TRX/Cobertura/OpenCover são publicados pelo GitHub Actions.

Esses resultados não comprovam deploy AWS, aprovação de SonarQube externo ou operação real de Mercado Pago sandbox. Nenhum recurso AWS é criado pelo script de evidências.
"""
(root / "docs" / "verification.md").write_text(markdown, encoding="utf-8")
print(json.dumps({"service": data["service"], "passed": data["tests"]["passed"], "coverage": data["coverage"]["lines"]}))
