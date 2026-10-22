#!/usr/bin/env python3
"""Enforce coverage floors from a ReportGenerator TextSummary file.

Usage: coverage_gate.py <Summary.txt> [min-line-percent] [min-branch-percent]

ReportGenerator 5.5.x no longer ships the -minimumCoverageThresholds option, so
the gate is applied here instead. The step summary receives the measured values
whether the gate passes or fails.
"""
from __future__ import annotations

import os
import re
import sys


def read_metric(text: str, label: str) -> float | None:
    match = re.search(rf"{re.escape(label)}:\s*([0-9]+(?:\.[0-9]+)?)%", text)
    return float(match.group(1)) if match else None


def main() -> int:
    if len(sys.argv) not in (2, 4):
        print("usage: coverage_gate.py <Summary.txt> [min-line] [min-branch]", file=sys.stderr)
        return 2

    with open(sys.argv[1], encoding="utf-8") as handle:
        text = handle.read()

    line = read_metric(text, "Line coverage")
    branch = read_metric(text, "Branch coverage")
    if line is None or branch is None:
        print("Coverage summary did not contain line/branch coverage.", file=sys.stderr)
        return 2

    minimum_line = float(sys.argv[2]) if len(sys.argv) == 4 else 0.0
    minimum_branch = float(sys.argv[3]) if len(sys.argv) == 4 else 0.0
    failures = []
    if line < minimum_line:
        failures.append(f"line {line:.1f}% < {minimum_line:.1f}%")
    if branch < minimum_branch:
        failures.append(f"branch {branch:.1f}% < {minimum_branch:.1f}%")

    lines = [
        f"Line coverage: **{line:.1f}%** (minimum {minimum_line:.1f}%)",
        f"Branch coverage: **{branch:.1f}%** (minimum {minimum_branch:.1f}%)",
    ]
    if failures:
        lines.insert(0, "Coverage gate **failed**: " + ", ".join(failures))
    else:
        lines.insert(0, "Coverage gate passed.")

    report = "\n".join(lines)
    print(report)
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as handle:
            handle.write("### Coverage gate\n\n")
            handle.write(report)
            handle.write("\n")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
