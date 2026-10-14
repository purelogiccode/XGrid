"""read_writedump_fatx.py - extract a file the guest wrote from an xgrid
--writedump sector journal (120 acceptance evidence).

xgrid's HddOverlay journals guest sector writes without committing them to
the qcow2, so FATX readback tools cannot see them; this reconstructs the
file from the dumped sectors instead:

  * root directory sector (LBA 0x465488, C: partition base 0x465400 +
    8-sector header + 128-sector FAT => cluster 1) -> dirent (name,
    start cluster, size);
  * FAT sector(s) (LBA 0x465409+) -> chain;
  * data sectors -> payload (last state per sector as dumped).

Usage: python read_writedump_fatx.py <dumpdir> <outpath> [<filename>]
Default filename: kernel_tests.log
"""

import struct
import sys
from pathlib import Path

BASE_LBA = 0x465400
ROOT_LBA = 0x465488
FAT_LBA = BASE_LBA + 8
CLUSTER_SECTORS = 32


def main():
    dumpdir = Path(sys.argv[1])
    outpath = Path(sys.argv[2])
    want = sys.argv[3] if len(sys.argv) > 3 else 'kernel_tests.log'

    root = (dumpdir / f'{ROOT_LBA:08X}.bin').read_bytes()
    name_bytes = want.encode('ascii')
    dirent = None
    for off in range(0, len(root), 64):
        entry = root[off:off + 64]
        if len(entry) < 64:
            break
        namelen = entry[0]
        if namelen in (0x00, 0xFF) or namelen > 42:
            continue
        name = entry[2:2 + namelen]
        if name == name_bytes:
            cluster, size = struct.unpack_from('<II', entry, 44)
            dirent = (cluster, size)
            break
    if dirent is None:
        raise SystemExit(f'{want!r} not in dumped root dir')
    cluster, size = dirent
    print(f'{want}: start cluster {cluster}, size {size}')

    fat_ents = [0xFFFF] * 65536
    for p in sorted(dumpdir.glob('*.bin')):
        lba = int(p.name.replace('.bin', ''), 16)
        if FAT_LBA <= lba < ROOT_LBA:
            raw = p.read_bytes()
            base_index = (lba - FAT_LBA) * 256
            for i in range(len(raw) // 2):
                fat_ents[base_index + i] = struct.unpack_from('<H', raw, i * 2)[0]

    out = bytearray()
    while cluster not in (0xFFFF, 0x0000) and len(out) < size:
        lba = BASE_LBA + 8 + 128 + (cluster - 1) * CLUSTER_SECTORS
        sectors_left = min(CLUSTER_SECTORS, (size - len(out) + 511) // 512)
        for s in range(sectors_left):
            p = dumpdir / f'{lba + s:08X}.bin'
            if not p.exists():
                raise SystemExit(f'missing sector {lba + s:08X} '
                                 f'(cluster {cluster} not fully dumped)')
            out += p.read_bytes()
        cluster = fat_ents[cluster] if cluster < len(fat_ents) else 0xFFFF

    out = bytes(out[:size])
    outpath.write_bytes(out)
    print(f'wrote {outpath} ({len(out)} bytes)')


if __name__ == '__main__':
    main()
