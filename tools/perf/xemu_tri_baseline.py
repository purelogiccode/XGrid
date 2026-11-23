"""xemu triangle perf baseline: boot triangle.iso under xemu, dump the
framebuffer at two wall-time points (pmemsave), convert to PNG, then quit.

The nxdk triangle sample prints its own "Frames:"/"FPS:" counters on screen,
so the PNGs give a frames-per-wall-second measurement directly comparable to
XGrid's flip counter. The framebuffer lives at guest physical 0x3D00000
(640x480, pitch 2560, BGRA) in both emulators.

Usage: python tools/perf/xemu_tri_baseline.py --dir <profile> [--seconds 30]
"""
import argparse
import os
import socket
import struct
import subprocess
import sys
import time
import zlib


def connect(port, timeout=30.0):
    deadline = time.time() + timeout
    while True:
        try:
            sock = socket.create_connection(("127.0.0.1", port), timeout=2.0)
            sock.settimeout(10.0)
            return sock
        except OSError:
            if time.time() > deadline:
                raise
            time.sleep(0.5)


def read_until_prompt(sock):
    data = b""
    while not data.endswith(b"(qemu) "):
        try:
            chunk = sock.recv(4096)
        except socket.timeout:
            break
        if not chunk:
            break
        data += chunk
    return data


def command(sock, text):
    sock.sendall(text.encode("ascii") + b"\n")
    time.sleep(0.3)
    try:
        return read_until_prompt(sock)
    except OSError:
        return b"<closed>"


def write_png(path, width, height, bgra):
    """Minimal PNG writer for an 8-bit RGBA image from BGRA rows."""
    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)  # filter: none
        row = bgra[y * stride:(y + 1) * stride]
        for x in range(0, stride, 4):
            raw += bytes((row[x + 2], row[x + 1], row[x], 255))

    def chunk(tag, payload):
        data = tag + payload
        return struct.pack(">I", len(payload)) + data + struct.pack(">I", zlib.crc32(data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 6))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as handle:
        handle.write(png)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", required=True)
    ap.add_argument("--port", type=int, default=4470)
    ap.add_argument("--seconds", type=float, default=30.0)
    ap.add_argument("--fb", type=lambda s: int(s, 0), default=0x3D00000)
    ap.add_argument("--size", type=lambda s: int(s, 0), default=640 * 480 * 4)
    args = ap.parse_args()

    profile = os.path.abspath(args.dir)
    exe = os.path.join(profile, "xemu.exe")
    console = open(os.path.join(profile, "tri_console.txt"), "wb")
    proc = subprocess.Popen(
        [exe, "-bios", "Complex_4627.bin", "-snapshot",
         "-monitor", f"tcp:127.0.0.1:{args.port},server,nowait"],
        cwd=profile, stdout=console, stderr=subprocess.STDOUT)
    print("xemu pid", proc.pid, flush=True)
    try:
        sock = connect(args.port)
        read_until_prompt(sock)
        command(sock, "cont")
        for i in (1, 2):
            time.sleep(args.seconds)
            raw_path = os.path.join(profile, f"tri-{i}.raw")
            if os.path.exists(raw_path):
                os.remove(raw_path)
            command(sock, f"pmemsave 0x{args.fb:X} 0x{args.size:X} {raw_path}")
            ok = os.path.exists(raw_path)
            if ok:
                with open(raw_path, "rb") as handle:
                    data = handle.read()
                write_png(os.path.join(profile, f"tri-{i}.png"), 640, 480, data)
            print(f"dump {i}: exists={ok} size={os.path.getsize(raw_path) if ok else 0}", flush=True)
        command(sock, "quit")
    finally:
        time.sleep(1.0)
        if proc.poll() is None:
            proc.terminate()
        console.close()
    print("done", flush=True)


if __name__ == "__main__":
    sys.exit(main())
