import struct
import importlib.util

spec = importlib.util.spec_from_file_location(
    "inj", r"C:\Users\HomePC\AppData\Local\Temp\opencode\worktrees\ktest-suite\wt-data\inject_dash.py"
)
inj = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inj)
q = inj.Qcow2(inj.QCOW)
base = 0x8CA80000
root_off = base + (8 + 128) * 512
cluster_size = 32 * 512
# walk first 4 root clusters (cluster 1..4 chain via FAT for continuation)
fat = q.read(base + 0x1000, 128 * 512)


def fat_get(c):
    return struct.unpack("<H", fat[c * 2 : c * 2 + 2])[0]


c = 1
seen = 0
while c not in (0, 0xFFFF) and seen < 16:
    off = base + (8 + 128 + (c - 1) * 32) * 512
    blk = q.read(off, cluster_size)
    # dirent parse: scan for plausible entries: u16 namelen <= 42 at some offset, name printable
    # empirical layout found earlier: [u16 namelen][name][FF pad to 42][u32 cluster][u32 size]
    # also entries may start with other fields before namelen; walk heuristically:
    i = 0
    while i < cluster_size - 64:
        # look for pattern: 2 bytes namelen (1..42), followed by that many printable ASCII
        nl = struct.unpack("<H", blk[i : i + 2])[0]
        if 1 <= nl <= 42 and i + 2 + nl <= cluster_size:
            name = blk[i + 2 : i + 2 + nl]
            if all(32 <= x < 127 for x in name):
                # candidate: name field padded to 42? then cluster,size
                pad_end = i + 2 + 42
                if pad_end + 8 <= cluster_size and all(
                    x == 0xFF or x == 0 for x in blk[i + 2 + nl : pad_end]
                ):
                    clu, sz = struct.unpack("<II", blk[pad_end : pad_end + 8])
                    print(f"cluster {c} @+0x{i:03X}: name={name.decode()!r} namelen={nl} cluster={clu} size={sz}")
                    i = pad_end + 8
                    continue
        i += 1
    c = fat_get(c)
    seen += 1
q.f.close()