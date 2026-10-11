#!/usr/bin/env python3
"""Fails when line coverage of the core assemblies drops below the agreed minimum.

Usage: check-coverage.py <merged Cobertura.xml from ReportGenerator> [minimum, default 0.90]
"""
import sys
import xml.etree.ElementTree as ET

CORE = ("PersonalPlatform.Domain", "PersonalPlatform.Application", "PersonalPlatform.Infrastructure")

path = sys.argv[1]
minimum = float(sys.argv[2]) if len(sys.argv) > 2 else 0.90
packages = {p.get("name"): float(p.get("line-rate")) for p in ET.parse(path).getroot().iter("package")}
failed = False
for name in CORE:
    rate = packages.get(name)
    if rate is None:
        print(f"{name}: no coverage data")
        failed = True
        continue
    status = "ok" if rate >= minimum else "BELOW MINIMUM"
    print(f"{name}: {rate:.1%} ({status}, minimum {minimum:.0%})")
    failed |= rate < minimum
sys.exit(1 if failed else 0)
