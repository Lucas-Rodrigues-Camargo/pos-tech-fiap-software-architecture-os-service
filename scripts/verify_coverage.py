from pathlib import Path
import xml.etree.ElementTree as ET
files = list(Path("tests/TestResults").glob("**/coverage.cobertura.xml"))
assert files, "No coverage report found"
report = ET.parse(max(files, key=lambda p: p.stat().st_mtime)).getroot()
rate = float(report.attrib["line-rate"])
print(f"Line coverage: {rate:.2%}")
assert rate >= .80, f"Minimum coverage 80%; obtained {rate:.2%}"
