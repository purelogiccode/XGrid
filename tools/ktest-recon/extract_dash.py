import struct
import re
import importlib.util

spec = importlib.util.spec_from_file_location(
    "inj", r"C:\Users\HomePC\AppData\Local\Temp\opencode\worktrees\ktest-suite\wt-data\inject_dash.py"
)
inj = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inj)

q = inj.Qcow2(
    r"C:\Users\HomePC\AppData\Local\Temp\opencode\worktrees\ktest-suite\wt-data\xbox_hdd.orig.qcow2"
)
base = 0x8CA80000
fat = q.read(base + 0x1000, 128 * 512)


def fat_get(c):
    return struct.unpack("<H", fat[c * 2 : c * 2 + 2])[0]


# walk xboxdash.xbe chain from cluster 268 (orig size 770048)
out = bytearray()
c = 268
while c not in (0, 0xFFFF) and len(out) < 770048:
    out += q.read(base + (8 + 128 + (c - 1) * 32) * 512, 32 * 512)
    c = fat_get(c)
out = bytes(out[:770048])
assert out[0:4] == b"XBEH", out[0:8]
open(r"C:\Users\HomePC\AppData\Local\Temp\opencode\worktrees\ktest-suite\wt-data\xboxdash.xbe", "wb").write(out)
print("extracted xboxdash.xbe", len(out))

# find \??\%c: (ansi) and refs
for needle in (b"\\??\\%c:", b"\\??\\", b"%c:"):
    start = 0
    while True:
        i = out.find(needle, start)
        if i < 0:
            break
        if i + 0x10000 < 0x400000:  # code/rdata
            print(f"needle {needle!r} @file 0x{i:X} VA 0x{i+0x10000:X}")
        start = i + 1
        if start > 0x80000:
            break

# thunk table of the dash
thunk_stored = struct.unpack_from("<I", out, 0x158)[0]
thunk = thunk_stored ^ 0x5B6D40B6
print(f"dash thunk VA=0x{thunk:X}")
# read ordinals from .rdata-ish: VA - 0x10000 file offset (identity mapping confirmed)
ords = []
for i in range(400):
    off = thunk + 4 * i - 0x10000
    if off + 4 > len(out):
        break
    v = struct.unpack_from("<I", out, off)[0]
    if v == 0:
        break
    ords.append(v & 0x7FFFFFFF)
print("dash ordinals count", len(ords))