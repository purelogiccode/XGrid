#!/usr/bin/env python3
"""run_xemu_ktest.py - differential xemu run for the kernel-test-suite gate (120).

Boots kernel-test-suite-default.xbe (packed as ktest.iso) in a PRIVATE xemu
install (all media under this wave's wt-data/xemu - never the main checkout)
and samples the QEMU monitor so we can answer, without guessing:

  (a) does the suite run there (screendump shows its debug text)?
  (b) does the guest spin (deadlock) or advance (EIP changes over time)?
  (c) physical RAM dump for letter/NTSTATUS forensics (pmemsave).

Usage:
  python run_xemu_ktest.py [--seconds 180] [--port 4444] [--tag a]

Outputs (under wt-data/xemu/):
  <tag>_regs_<n>.txt   info registers samples (EIP history)
  <tag>_scr_<n>.png    screendumps converted to PNG (viewable)
  <tag>_ram.bin        64 MiB physical RAM dump at the end of the run
  <tag>_console.txt    xemu stdout/stderr

Requires: Pillow-free PNG conversion via ffmpeg (P6 PPM -> PNG).
"""

import argparse
import os
import socket
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
WORKTREE = os.path.dirname(os.path.dirname(HERE))
XEMU_DIR = os.path.join(WORKTREE, "wt-data", "xemu")
XEMU_EXE = os.path.join(XEMU_DIR, "xemu.exe")
PPM2PNG = r"C:\ProgramData\chocolatey\bin\ffmpeg.exe"


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
    for line in text.splitlines():
        line = line.strip()
        if line.startswith("EIP=") or line.startswith("EIP ="):
            return line.split("=")[1].split()[0].strip()
        # multi-line "info registers" on x86: "EAX=... EBX=..." etc
        for tok in line.replace(",", " ").split():
            if tok.startswith("EIP="):
                return tok.split("=")[1]
    return "?"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seconds", type=int, default=180)
    ap.add_argument("--interval", type=int, default=30)
    ap.add_argument("--port", type=int, default=4444)
    ap.add_argument("--tag", default="a")
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

    print("xemu pid", proc.pid, "- sampling for", args.seconds, "s")
    samples = 0
    start = time.time()
    try:
        while time.time() - start < args.seconds:
            regs = mon.cmd("info registers")
            samples += 1
            reg_path = os.path.join(XEMU_DIR, f"{args.tag}_regs_{samples}.txt")
            with open(reg_path, "wb") as fh:
                fh.write(regs.encode("latin-1", "replace"))
            print("t=%6.1fs EIP=%s" % (time.time() - start, eip_from_regs(regs)))
            ppm = os.path.join(XEMU_DIR, f"{args.tag}_scr_{samples}.ppm")
            mon.cmd("screendump " + ppm)
            png = os.path.join(XEMU_DIR, f"{args.tag}_scr_{samples}.png")
            if os.path.exists(ppm) and os.path.exists(PPM2PNG):
                subprocess.run([PPM2PNG, "-y", "-i", ppm, png],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            time.sleep(args.interval)
    finally:
        ram = os.path.join(XEMU_DIR, f"{args.tag}_ram.bin")
        try:
            mon.cmd("pmemsave 0 0x4000000 " + ram)
            print("ram dump:", ram, os.path.getsize(ram), "bytes")
        except Exception as exc:  # noqa: BLE001 - monitor may be gone
            print("pmemsave failed:", exc)
        try:
            mon.cmd("quit")
        except Exception:
            proc.kill()
        console.close()

    print("done; samples:", samples)
    return 0


if __name__ == "__main__":
    sys.exit(main())
