#!/usr/bin/env python3
"""run_xemu_ktest_full.py - xemu differential for the FULL kernel-test-suite (120).

Boots the media-patched suite (C:\\kernel_tests.log + ANSI IoCreateSymbolicLink
shellcode -> \\Device\\Harddisk0\\Partition2; see patch_suite_xbe.py) in a PRIVATE
xemu profile (wt-data/xemu2) and quits when the suite is inside its final
Sleep(10000) - i.e. after close_output_file, so the FATX log on the private
qcow2 is complete. Then the qcow2 is extracted with a FATX reader
(read_fatx_qcow2.py) and compared with the XGrid run.

Detection: sample 'info registers' every 2 s. Suite code lives at guest EIPs
0x10000..0x5B000 (XBE base 0x10000); kernel is >= 0x80000000. After the first
sample that saw a suite-range EIP, N consecutive samples without suite EIPs
means the suite finished tests and is sleeping (the sleep is 10 s guest-time =
10 s wall in xemu). Quit then; do NOT wait for the relaunch (which truncates
the log via CREATE_ALWAYS).

Usage:
  python run_xemu_ktest_full.py [--tag f] [--port 4445] [--settle 4]
                                [--interval 2] [--timeout 900]

Outputs (under wt-data/xemu2/):
  <tag>_console.txt   xemu stdout/stderr
  <tag>_regs_*.txt    info registers samples
  <tag>_ram.bin       64 MiB pmemsave at quit
"""

import argparse
import os
import re
import socket
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
WORKTREE = os.path.dirname(os.path.dirname(HERE))
XEMU_DIR = os.path.join(WORKTREE, "wt-data", "xemu2")
XEMU_EXE = os.path.join(XEMU_DIR, "xemu.exe")

EIP_RE = re.compile(r"EIP=([0-9a-fA-F]+)")


class Monitor:
    def __init__(self, port, timeout=15.0):
        self.sock = socket.create_connection(("127.0.0.1", port), timeout=timeout)
        self.sock.settimeout(timeout)
        self.buf = b""
        self._read_until_prompt()

    def _read_until_prompt(self):
        while True:
            chunk = self.sock.recv(4096)
            if not chunk:
                raise ConnectionError("monitor closed")
            self.buf += chunk
            if self.buf.endswith(b"(qemu) "):
                text = self.buf[: -len(b"(qemu) ")]
                self.buf = b""
                return text

    def cmd(self, command):
        self.sock.sendall(command.encode("ascii") + b"\n")
        return self._read_until_prompt().decode("latin-1", "replace")


def eip_from_regs(text):
    m = EIP_RE.search(text)
    return int(m.group(1), 16) if m else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--tag", default="f")
    ap.add_argument("--port", type=int, default=4445)
    ap.add_argument("--settle", type=int, default=4,
                    help="consecutive non-suite samples after first suite EIP")
    ap.add_argument("--interval", type=float, default=2.0)
    ap.add_argument("--timeout", type=float, default=900.0)
    args = ap.parse_args()

    console_path = os.path.join(XEMU_DIR, f"{args.tag}_console.txt")
    console = open(console_path, "wb")
    proc = subprocess.Popen(
        [XEMU_EXE, "-monitor", f"tcp:127.0.0.1:{args.port},server,nowait"],
        cwd=XEMU_DIR,
        stdout=console,
        stderr=subprocess.STDOUT,
    )

    mon = None
    deadline = time.time() + 60
    while time.time() < deadline:
        if proc.poll() is not None:
            print("xemu exited early, see", console_path)
            return 2
        try:
            mon = Monitor(args.port, timeout=5.0)
            break
        except OSError:
            time.sleep(1.0)
    if mon is None:
        proc.kill()
        print("monitor never came up")
        return 2

    print("xemu pid", proc.pid)
    start = time.time()
    saw_suite = 0
    settle = 0
    samples = 0
    quit_reason = "timeout"
    dumps_at = (75.0, 135.0, 210.0)
    dumped = [False] * len(dumps_at)

    def pmemsave(name):
        ram = os.path.join(XEMU_DIR, f"{args.tag}_ram{name}.bin")
        mon.cmd("pmemsave 0 0x4000000 " + ram)
        print("ram dump:", ram, os.path.getsize(ram), "bytes")

    try:
        while time.time() - start < args.timeout:
            regs = mon.cmd("info registers")
            samples += 1
            eip = eip_from_regs(regs)
            in_suite = eip is not None and 0x10000 <= eip < 0x5B000
            if in_suite:
                saw_suite += 1
                settle = 0
            elif saw_suite:
                settle += 1
            elapsed = time.time() - start
            print("t=%6.1fs EIP=%s suite=%d settle=%d" % (
                elapsed,
                ("%08x" % eip) if eip is not None else "?",
                saw_suite, settle))
            for i, when in enumerate(dumps_at):
                if not dumped[i] and elapsed >= when:
                    try:
                        pmemsave("_%d" % int(when))
                        dumped[i] = True
                    except Exception as exc:  # noqa: BLE001
                        print("pmemsave _%d failed:" % int(when), exc)
                        raise
            if saw_suite and settle >= args.settle:
                quit_reason = "settled"
                break
            time.sleep(args.interval)
    except Exception as exc:  # noqa: BLE001 - monitor may drop if xemu dies
        print("monitor lost:", exc)
        quit_reason = "monitor-lost"
    finally:
        try:
            pmemsave("_end")
        except Exception as exc:  # noqa: BLE001 - monitor may be gone
            print("pmemsave failed:", exc)
        try:
            mon.cmd("quit")
        except Exception:
            proc.kill()
        console.close()
        try:
            proc.wait(timeout=15)
        except Exception:
            proc.kill()

    print("done; reason=%s samples=%d saw_suite=%d" % (quit_reason, samples, saw_suite))
    return 0


if __name__ == "__main__":
    sys.exit(main())
