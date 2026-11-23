"""ktest_diff.py - diff XGrid vs xemu kernel-test-suite subset logs (§123 smoke).

Parses the two `kernel_tests.log` files recovered from the guest FATX volume
(XGrid: read_writedump_fatx.py over --writedump; xemu: read_fatx_qcow2.py over
its private qcow2) and checks every subset test against the §120-recorded
expectations:

  051 InterlockedCompareExchange      PASS / PASS (silent stub, completes clean)
  071 IoFileObjectType                FAIL / FAIL (faithful: D: is read-only XDVDFS)
  224 NtResumeThread                  PASS / FAIL (XGrid ahead: xemu line-110 out-param quirk)
  231 NtSuspendThread                 PASS / FAIL (XGrid ahead, same quirk)
  282 RtlExtendedLargeIntegerDivide   PASS / PASS (former CMC #UD site, §120 fix)
  319 RtlWalkFrameChain               FAIL / FAIL (faithful: 10 frames vs expected 8)
  344 XcVerifyPKCS1Signature          FAIL / FAIL (faithful: Complex_4627 returns 0)

For the faithful-FAIL rows the exact §120 ERROR text is also required on both
emulators (proves the failure is the same guest-side expectation gap, not a
new divergence).

Usage: python ktest_diff.py <xgrid.log> <xemu.log>
Exit 0 = SMOKE PASS (all rows match expectations), 1 = SMOKE FAIL.
"""

import re
import sys
from pathlib import Path

TEST_LINE = re.compile(r"^(\d{3}) - ([^:]+): (.*)$")

# (number, name, xgrid_status, xemu_status, required ERROR substrings or None)
# Statuses: "PASS" | "FAIL".
EXPECTATIONS = [
    ("051", "InterlockedCompareExchange", "PASS", "PASS", None),
    ("071", "IoFileObjectType", "FAIL", "FAIL",
     ["Expected file_handle = 0x1, Got = 0x0"]),
    ("224", "NtResumeThread", "PASS", "FAIL", ["line 110"]),
    ("231", "NtSuspendThread", "PASS", "FAIL", ["line 110"]),
    ("282", "RtlExtendedLargeIntegerDivide", "PASS", "PASS", None),
    ("319", "RtlWalkFrameChain", "FAIL", "FAIL",
     ["Expected total = 0x8, Got = 0xA"]),
    ("344", "XcVerifyPKCS1Signature", "FAIL", "FAIL",
     ["Expected XcVerifyPKCS1Signature return = 0x1, Got = 0x0"]),
]


def parse(path):
    """Return (tests, end_of_tests) where tests maps NNN -> dict(status, errors)."""
    tests = {}
    end_of_tests = False
    current = None
    for raw in Path(path).read_text(errors="replace").splitlines():
        line = raw.strip()
        if "End of Tests" in line:
            end_of_tests = True
        m = TEST_LINE.match(line)
        if not m:
            # Indented per-assert lines (e.g. "ERROR(line 72): ...") belong to
            # the most recent test header.
            if current is not None and "ERROR(" in line:
                tests[current]["errors"].append(line)
            continue
        num, name, event = m.group(1), m.group(2), m.group(3)
        current = num
        t = tests.setdefault(num, {"name": name, "failed": False,
                                   "passed": False, "completed": False,
                                   "errors": []})
        t["name"] = name
        if "One or more tests FAILED" in event:
            t["failed"] = True
        if "All tests PASSED" in event:
            t["passed"] = True
        if "Test completed" in event:
            t["completed"] = True
        if "ERROR(" in event:
            t["errors"].append(event)
    status = {}
    for num, t in tests.items():
        if t["failed"]:
            status[num] = "FAIL"
        elif t["passed"] or (t["completed"] and not t["errors"]):
            status[num] = "PASS"
        elif t["completed"]:
            # Completed but printed ERRORs without a FAILED footer: treat as
            # FAIL (loud) rather than silently passing.
            status[num] = "FAIL"
        else:
            status[num] = "MISSING"
    return tests, status, end_of_tests


def main():
    xgrid_path, xemu_path = sys.argv[1], sys.argv[2]
    xgrid_tests, xgrid_status, xgrid_eot = parse(xgrid_path)
    xemu_tests, xemu_status, xemu_eot = parse(xemu_path)

    print("== ktest smoke diff ==")
    print("xgrid log: %s (EndOfTests=%s)" % (xgrid_path, xgrid_eot))
    print("xemu  log: %s (EndOfTests=%s)" % (xemu_path, xemu_eot))
    print("")
    print("%-5s %-32s %-8s %-8s %-8s %s"
          % ("test", "name", "xgrid", "xemu", "expect", "verdict"))

    failures = 0
    if not xgrid_eot:
        print("FAIL: xgrid log missing End of Tests footer (run incomplete)")
        failures += 1
    if not xemu_eot:
        print("FAIL: xemu log missing End of Tests footer (run incomplete)")
        failures += 1

    xgrid_pass = xemu_pass = 0
    for num, name, exp_xgrid, exp_xemu, err_subs in EXPECTATIONS:
        got_xgrid = xgrid_status.get(num, "MISSING")
        got_xemu = xemu_status.get(num, "MISSING")
        if got_xgrid == "PASS":
            xgrid_pass += 1
        if got_xemu == "PASS":
            xemu_pass += 1
        row_ok = (got_xgrid == exp_xgrid and got_xemu == exp_xemu)
        detail = ""
        if err_subs:
            for emu, tests, got, exp in (("xgrid", xgrid_tests, got_xgrid, exp_xgrid),
                                         ("xemu", xemu_tests, got_xemu, exp_xemu)):
                if got != "FAIL":
                    continue
                blob = "\n".join(tests[num]["errors"])
                for sub in err_subs:
                    if sub not in blob:
                        row_ok = False
                        detail += " [%s missing %r]" % (emu, sub)
        verdict = "green" if row_ok else "RED"
        if not row_ok:
            failures += 1
        print("%-5s %-32s %-8s %-8s %s/%s %s%s"
              % (num, name, got_xgrid, got_xemu, exp_xgrid, exp_xemu,
                 verdict, detail))

    print("")
    print("pass counts: xgrid %d/%d, xemu %d/%d"
          % (xgrid_pass, len(EXPECTATIONS), xemu_pass, len(EXPECTATIONS)))
    if failures:
        print("SMOKE: FAIL (%d mismatched rows)" % failures)
        return 1
    print("SMOKE: PASS (known deltas match, no unexpected failures)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
