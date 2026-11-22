#!/usr/bin/env python3
"""Fail CI only on NEW InspectCode findings (WARNING or ERROR).

A zero-warning gate cannot be switched on for a codebase this size: the tree
carries 45 accepted findings - unused test fixtures, fixed-point integer
division in the APU, deliberately qualified names, a diagnostic-only field.
Fixing them is churn with no behaviour change, and hiding the gate loses the
only static-analysis signal CI has.

So the gate is a ratchet. `inspectcode-baseline.json` (beside this script)
records the accepted findings as `"<rule>|<file>": count`:

* a finding whose key is absent from the baseline, or whose count exceeds the
  recorded one, **fails** the build;
* a key whose count fell is reported as stale so the baseline can be tightened
  in the same change;
* `--write` regenerates the baseline from the current report.

Keying on rule+file rather than rule+line is deliberate: line numbers move on
every refactor, and a baseline that churns on unrelated edits gets deleted
instead of trusted.

Usage:
  inspectcode_gate.py inspectcode.sarif           # check
  inspectcode_gate.py inspectcode.sarif --write   # accept the current report
"""
from __future__ import annotations

import json
import os
import sys
from collections import Counter

BASELINE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "inspectcode-baseline.json")


def read_findings(sarif_path: str) -> Counter[tuple[str, str]]:
    """Returns a counter of (rule, file) pairs from an InspectCode SARIF report."""
    with open(sarif_path, encoding="utf-8") as handle:
        sarif = json.load(handle)

    findings: Counter[tuple[str, str]] = Counter()
    for run in sarif.get("runs", []):
        for result in run.get("results", []):
            rule = str(result.get("ruleId", ""))
            location = ""
            locations = result.get("locations") or []
            if locations:
                physical = locations[0].get("physicalLocation", {})
                location = physical.get("artifactLocation", {}).get("uri", "")
            findings[(rule, location)] += 1
    return findings


def to_json(findings: Counter[tuple[str, str]]) -> dict[str, int]:
    return {f"{rule}|{path}": count for (rule, path), count in sorted(findings.items())}


def from_json(document: dict[str, int]) -> Counter[tuple[str, str]]:
    findings: Counter[tuple[str, str]] = Counter()
    for key, count in document.items():
        rule, _, path = key.partition("|")
        findings[(rule, path)] = int(count)
    return findings


def load_baseline() -> Counter[tuple[str, str]]:
    if not os.path.exists(BASELINE_PATH):
        return Counter()
    with open(BASELINE_PATH, encoding="utf-8") as handle:
        return from_json(json.load(handle))


def describe(findings: Counter[tuple[str, str]]) -> list[str]:
    lines = ["| Rule | File | Count |", "|---|---|---|"]
    for (rule, path), count in sorted(findings.items()):
        lines.append(f"| {rule} | {path} | {count} |")
    return lines


def publish(title: str, lines: list[str]) -> None:
    report = "\n".join(lines)
    print(report)
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as handle:
            handle.write(f"### {title}\n\n{report}\n\n")


def main() -> int:
    args = [arg for arg in sys.argv[1:] if not arg.startswith("--")]
    flags = {arg for arg in sys.argv[1:] if arg.startswith("--")}
    unknown = flags - {"--write"}
    if len(args) != 1 or unknown:
        print("usage: inspectcode_gate.py <inspectcode.sarif> [--write]", file=sys.stderr)
        return 2

    current = read_findings(args[0])

    if "--write" in flags:
        with open(BASELINE_PATH, "w", encoding="utf-8", newline="\n") as handle:
            json.dump(to_json(current), handle, indent=2)
            handle.write("\n")
        print(f"InspectCode: baseline written to {BASELINE_PATH} ({sum(current.values())} findings).")
        return 0

    baseline = load_baseline()

    new_findings: Counter[tuple[str, str]] = Counter()
    for key, count in current.items():
        accepted = baseline.get(key, 0)
        if count > accepted:
            new_findings[key] = count - accepted

    stale: Counter[tuple[str, str]] = Counter()
    for key, count in baseline.items():
        remaining = current.get(key, 0)
        if remaining < count:
            stale[key] = count - remaining

    if new_findings:
        lines = [
            f"InspectCode reported {sum(new_findings.values())} new finding(s); "
            f"the baseline accepts {sum(baseline.values())}.",
            "",
        ]
        lines.extend(describe(new_findings))
        lines.extend(["", "Fix them, or accept them with `--write` in a reviewed change."])
        publish("InspectCode: new findings", lines)
        return 1

    print(
        f"InspectCode: no new findings "
        f"({sum(current.values())} known, {sum(baseline.values())} accepted)."
    )
    if stale:
        lines = ["These accepted findings no longer reproduce; tighten the baseline.", ""]
        lines.extend(describe(stale))
        publish("InspectCode: stale baseline entries", lines)
    return 0


if __name__ == "__main__":
    sys.exit(main())
