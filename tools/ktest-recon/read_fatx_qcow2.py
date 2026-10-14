"""read_fatx_qcow2.py - extract a file from the C: FATX partition of a qcow2.

Used to read back guest-written files (e.g. kernel_tests.log) from an xemu
private HDD copy after a run: xemu commits guest writes straight into the
qcow2, so once xemu exits the file can be extracted offline.

qcow2 v2/v3 read algorithm mirrors XGrid.Storage.Qcow2DiskImage / inject_dash.py;
FATX facts from research-notes 47 + XboxDevWiki (same as inject_dash.py):
superblock at byte 0x8CA80000 (sector 0x465400), 8-sector header, 128-sector
u16-LE FAT, 32-sector clusters, root = cluster 1, dirent =
[u16 namelen][name][FF pad to 42][u32 cluster LE][u32 size LE].

Usage: python read_fatx_qcow2.py <image.qcow2> <outpath> [<filename>]
Default filename: kernel_tests.log
"""

import struct
import sys
from pathlib import Path

OFFSET_MASK = 0x00FFFFFFFFFFFE00
ZERO_FLAG = 0x01
BASE = 0x8CA80000
CLUSTER_SECTORS = 32
CLUSTER_SIZE = CLUSTER_SECTORS * 512


class Qcow2:
    def __init__(self, path: Path):
        self.f = open(path, "rb")
        h = self.f.read(104)
        magic, ver = struct.unpack(">II", h[0:8])
        assert magic == 0x514649FB, hex(magic)
        assert ver in (2, 3), ver
        self.cluster_bits = struct.unpack(">I", h[20:24])[0]
        self.vsize = struct.unpack(">Q", h[24:32])[0]
        l1_size, l1_off = struct.unpack(">IQ", h[36:48])
        self.f.seek(l1_off)
        self.l1 = list(struct.unpack(">%dQ" % l1_size, self.f.read(8 * l1_size)))
        self.l2_entries = 1 << (self.cluster_bits - 3)

    def read(self, offset, size):
        out = bytearray()
        cluster_size = 1 << self.cluster_bits
        while size:
            ci = offset // cluster_size
            within = offset % cluster_size
            n = min(size, cluster_size - within)
            l1i, l2i = divmod(ci, self.l2_entries)
            l2o = self.l1[l1i] & OFFSET_MASK
            if l2o == 0:
                out += b"\x00" * n
            else:
                self.f.seek(l2o + l2i * 8)
                (ent,) = struct.unpack(">Q", self.f.read(8))
                co = ent & OFFSET_MASK
                if (ent & ZERO_FLAG) or co == 0:
                    out += b"\x00" * n
                else:
                    self.f.seek(co + within)
                    out += self.f.read(n)
            offset += n
            size -= n
        return bytes(out)


def main():
    image = Path(sys.argv[1])
    outpath = Path(sys.argv[2])
    want = sys.argv[3] if len(sys.argv) > 3 else "kernel_tests.log"

    q = Qcow2(image)
    fat = q.read(BASE + 0x1000, 128 * 512)
    root = q.read(BASE + (8 + 128) * 512, CLUSTER_SIZE)

    name_bytes = want.encode("ascii")
    dirent = None
    for off in range(0, len(root), 64):
        entry = root[off:off + 64]
        namelen = entry[0]
        if namelen in (0x00, 0xFF) or namelen > 42:
            continue
        if entry[2:2 + namelen] == name_bytes:
            cluster, size = struct.unpack_from("<II", entry, 44)
            dirent = (cluster, size)
            break
    if dirent is None:
        raise SystemExit(f"{want!r} not in FATX root")
    cluster, size = dirent
    print(f"{want}: start cluster {cluster}, size {size}")

    out = bytearray()
    while cluster not in (0xFFFF, 0x0000) and len(out) < size:
        out += q.read(
            BASE + (8 + 128 + (cluster - 1) * CLUSTER_SECTORS) * 512, CLUSTER_SIZE
        )
        cluster = struct.unpack_from("<H", fat, cluster * 2)[0]

    out = bytes(out[:size])
    outpath.write_bytes(out)
    print(f"wrote {outpath} ({len(out)} bytes)")


if __name__ == "__main__":
    main()
