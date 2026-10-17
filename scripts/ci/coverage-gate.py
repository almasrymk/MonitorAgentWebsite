#!/usr/bin/env python3
"""Fails when line coverage of the Domain and Application projects is under the plan's 80%.

Usage: coverage-gate.py <coverage.cobertura.xml> [minimum percent]

Infrastructure and the host are left out on purpose: they are mostly EF migrations and wiring, which the API tests
exercise end to end but which would only dilute the number that says whether business rules are tested.
"""
import sys
import xml.etree.ElementTree as ET

path = sys.argv[1]
minimum = float(sys.argv[2]) if len(sys.argv) > 2 else 80.0

covered = valid = 0
rows = []
for package in ET.parse(path).getroot().iter("package"):
    name = package.get("name", "")
    if not (name.endswith(".Domain") or name.endswith(".Application") or name == "MonitorAgent.SharedKernel"):
        continue
    lines = [line for line in package.iter("line")]
    hits = sum(1 for line in lines if int(line.get("hits", "0")) > 0)
    covered += hits
    valid += len(lines)
    rows.append((hits / len(lines) * 100 if lines else 100.0, name))

for percent, name in sorted(rows):
    print(f"{percent:6.1f}%  {name}")
total = covered / valid * 100 if valid else 0.0
print(f"{total:6.1f}%  Domain + Application + SharedKernel ({covered}/{valid} lines), minimum {minimum:.0f}%")
sys.exit(0 if total >= minimum else 1)
