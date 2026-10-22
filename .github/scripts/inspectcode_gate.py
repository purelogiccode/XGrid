#!/usr/bin/env python3
"""Fail CI when InspectCode reports WARNING or ERROR findings.

Usage: inspectcode_gate.py <inspectcode.sarif>

The SARIF file is also rendered into the GitHub step summary so the failure is
actionable without downloading the artifact.
"""
from __future__ import annotations

import json
import os
import sys


def summarize(sarif_path: str) -> tuple[int, list[str]]:
    with open(sarif_path, encoding="utf-8") as handle:
        sarif = json.load(handle)

    findings: list[tuple[str, str, str, str]] = []
    for run in sarif.get("runs", []):
        for result in run.get("results", []):
            location = ""
            locations = result.get("locations") or []
            if locations:
                physical = locations[0].get("physicalLocation", {})
                uri = physical.get("artifactLocation", {}).get("uri", "")
                line = physical.get("region", {}).get("startLine", "")
                location = f"{uri}:{line}" if uri else str(line)
            findings.append(
                (
                    str(result.get("level", "warning")).upper(),
                    str(result.get("ruleId", "")),
                    location,
                    str(result.get("message", {}).get("text", "")),
                )
            )

    if not findings:
        return 0, []

    def cell(value: str) -> str:
        return value.replace("|", "\\|").replace("\n", " ")

    lines = [
        f"InspectCode reported {len(findings)} finding(s).",
        "",
        "| Severity | Rule | Location | Message |",
        "|---|---|---|---|",
    ]
    for severity, rule, location, message in findings:
        lines.append(
            f"| {cell(severity)} | {cell(rule)} | {cell(location)} | {cell(message)} |"
        )
    return len(findings), lines


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: inspectcode_gate.py <inspectcode.sarif>", file=sys.stderr)
        return 2

    count, lines = summarize(sys.argv[1])
    if count == 0:
        print("InspectCode: no warnings or errors.")
        return 0

    report = "\n".join(lines)
    print(report)
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as handle:
            handle.write("### InspectCode findings\n\n")
            handle.write(report)
            handle.write("\n")
    return 1


if __name__ == "__main__":
    sys.exit(main())
