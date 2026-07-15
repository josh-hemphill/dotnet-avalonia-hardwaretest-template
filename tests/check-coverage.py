#!/usr/bin/env python3
"""Fail if Core coverage floors are not met (Engine/Hardware 80%, Core 70%)."""
from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def line_rate(el: ET.Element) -> float:
    return float(el.attrib.get("line-rate", "0")) * 100.0


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: check-coverage.py <cobertura.xml>", file=sys.stderr)
        return 2

    path = Path(sys.argv[1])
    if not path.exists():
        print(f"coverage file not found: {path}", file=sys.stderr)
        return 2

    root = ET.parse(path).getroot()
    packages = root.find("packages")
    if packages is None:
        print("no packages in cobertura report", file=sys.stderr)
        return 2

    core_lines = missed = covered = 0
    engine_lines = engine_missed = engine_covered = 0
    hardware_lines = hardware_missed = hardware_covered = 0

    for pkg in packages.findall("package"):
        name = pkg.attrib.get("name", "")
        if "HardwareTest.Core" not in name:
            continue
        classes = pkg.find("classes")
        if classes is None:
            continue
        for cls in classes.findall("class"):
            filename = cls.attrib.get("filename", "") + " " + cls.attrib.get("name", "")
            lines = cls.find("lines")
            if lines is None:
                continue
            for line in lines.findall("line"):
                hits = int(line.attrib.get("hits", "0"))
                core_lines += 1
                if hits > 0:
                    covered += 1
                else:
                    missed += 1
                if ".Engine." in filename or "/Engine/" in filename or "\\Engine\\" in filename or "Engine." in filename:
                    engine_lines += 1
                    if hits > 0:
                        engine_covered += 1
                    else:
                        engine_missed += 1
                if ".Hardware." in filename or "/Hardware/" in filename or "\\Hardware\\" in filename or "Hardware." in filename:
                    hardware_lines += 1
                    if hits > 0:
                        hardware_covered += 1
                    else:
                        hardware_missed += 1

    def pct(c: int, t: int) -> float:
        return 100.0 if t == 0 else (c * 100.0 / t)

    core_pct = pct(covered, core_lines)
    engine_pct = pct(engine_covered, engine_lines)
    hardware_pct = pct(hardware_covered, hardware_lines)

    print(f"Core line coverage: {core_pct:.1f}% ({covered}/{core_lines})")
    print(f"Engine line coverage: {engine_pct:.1f}% ({engine_covered}/{engine_lines})")
    print(f"Hardware line coverage: {hardware_pct:.1f}% ({hardware_covered}/{hardware_lines})")

    ok = True
    if core_pct < 70.0:
        print("FAIL: Core coverage below 70%", file=sys.stderr)
        ok = False
    if engine_pct < 80.0:
        print("FAIL: Engine coverage below 80%", file=sys.stderr)
        ok = False
    if hardware_pct < 80.0:
        print("FAIL: Hardware coverage below 80%", file=sys.stderr)
        ok = False

    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
