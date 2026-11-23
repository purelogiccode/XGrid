#!/usr/bin/env python3
"""Fail CI when `dotnet list package --vulnerable --format json` finds advisories.

Usage: vulnerable_gate.py <package-audit.json>

The SDK emits a nested projects/frameworks/topLevelPackages/transitivePackages
document. This walks it generically so schema additions (new frameworks or
package buckets) keep working.
"""
from __future__ import annotations

import json
import os
import sys
from typing import Any, Iterator


def iter_project_packages(node: Any, project: str = "") -> Iterator[tuple[str, dict[str, Any]]]:
    """Yield (project path, package object) for every package record."""
    if isinstance(node, dict):
        current_project = node.get("path", project)
        if "id" in node and "resolvedVersion" in node:
            yield str(current_project), node
            return
        for key, value in node.items():
            if key == "path":
                continue
            yield from iter_project_packages(value, str(current_project))
    elif isinstance(node, list):
        for item in node:
            yield from iter_project_packages(item, project)


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: vulnerable_gate.py <package-audit.json>", file=sys.stderr)
        return 2

    with open(sys.argv[1], encoding="utf-8") as handle:
        audit = json.load(handle)

    findings: list[tuple[str, str, str, str, str]] = []
    for project, package in iter_project_packages(audit):
        for advisory in package.get("vulnerabilities") or []:
            findings.append(
                (
                    project,
                    str(package.get("id", "")),
                    str(package.get("resolvedVersion", "")),
                    str(advisory.get("severity", "")),
                    str(advisory.get("advisoryurl", advisory.get("advisoryUrl", ""))),
                )
            )

    if not findings:
        print("Package audit: no vulnerable packages.")
        return 0

    def cell(value: str) -> str:
        return value.replace("|", "\\|").replace("\n", " ")

    lines = [
        f"Package audit found {len(findings)} vulnerable package(s).",
        "",
        "| Project | Package | Version | Severity | Advisory |",
        "|---|---|---|---|---|",
    ]
    for project, package, version, severity, advisory in findings:
        lines.append(
            f"| {cell(project)} | {cell(package)} | {cell(version)} | "
            f"{cell(severity)} | {cell(advisory)} |"
        )

    report = "\n".join(lines)
    print(report)
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as handle:
            handle.write("### Vulnerable packages\n\n")
            handle.write(report)
            handle.write("\n")
    return 1


if __name__ == "__main__":
    sys.exit(main())
