"""Probe xemu's monitor command list (help) for memory/screen dump commands."""
import os
import socket
import subprocess
import sys
import time


def main():
    profile = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else "wt-data/xemu-tri")
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 4469
    console = open(os.path.join(profile, "probe_console.txt"), "wb")
    proc = subprocess.Popen(
        [os.path.join(profile, "xemu.exe"), "-bios", "Complex_4627.bin", "-snapshot",
         "-monitor", f"tcp:127.0.0.1:{port},server,nowait"],
        cwd=profile, stdout=console, stderr=subprocess.STDOUT)
    try:
        time.sleep(4)
        sock = socket.create_connection(("127.0.0.1", port), timeout=5.0)
        sock.settimeout(3.0)
        time.sleep(0.5)
        try:
            sock.recv(4096)
        except socket.timeout:
            pass
        sock.sendall(b"help\n")
        time.sleep(1.5)
        data = b""
        try:
            while True:
                chunk = sock.recv(65536)
                if not chunk:
                    break
                data += chunk
        except socket.timeout:
            pass
        text = data.decode("ascii", "replace")
        lines = [line.strip() for line in text.splitlines()]
        interesting = [line for line in lines
                       if any(k in line for k in ("dump", "mem", "screen", "xp", "print", "info", "reg"))]
        print("\n".join(interesting[:60]))
        print("--- total help lines:", len(lines))
    finally:
        proc.terminate()
        console.close()


if __name__ == "__main__":
    sys.exit(main())
