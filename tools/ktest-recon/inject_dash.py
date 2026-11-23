#!/usr/bin/env python3
"""Inject kernel-test-suite (C:-patched log path) as C:\\xboxdash.xbe into the private qcow2.

Mirrors XGrid's Qcow2DiskImage read algorithm; writes back only into allocated
clusters (in-place qcow2 edit). FATX facts from research-notes 47 + XboxDevWiki:
  header 0x1000 (8 sectors), FAT u16-BE, root dir = cluster 1, 32-sector clusters.
"""
import struct
import sys
from pathlib import Path

QCOW = Path(r"C:\Users\HomePC\AppData\Local\Temp\opencode\worktrees\ktest-suite\wt-data\xbox_hdd.qcow2")
XBE = Path(r"C:\Users\HomePC\AppData\Local\Temp\opencode\worktrees\ktest-suite\wt-data\ktest-iso\default.xbe")  # C:-patched
MODE = sys.argv[1] if len(sys.argv) > 1 else "scan"

OFFSET_MASK = 0x00FFFFFFFFFFFE00
ZERO_FLAG = 0x01


class Qcow2:
    def __init__(self, path: Path):
        self.f = open(path, "r+b")
        h = self.f.read(104)
        magic, ver = struct.unpack(">II", h[0:8])
        assert magic == 0x514649FB, hex(magic)
        assert ver in (2, 3), ver
        self.cluster_bits = struct.unpack(">I", h[20:24])[0]
        self.vsize = struct.unpack(">Q", h[24:32])[0]
        l1_size, l1_off = struct.unpack(">IQ", h[36:48])
        # qcow2 v3: check unsupported features quickly (compression type at 0x68 for v3)
        if ver == 3:
            # header_length u32 @0x64; compression_type u16 @0x68 only if long header
            self.f.seek(0x48)
            feats = struct.unpack(">Q", self.f.read(8))[0]
            if feats & 0x7:  # corrupt | external data | encryption
                raise SystemExit(f"unsupported qcow2 incompatible_features=0x{feats:X}")
            self.f.seek(0x64)
            (hlen,) = struct.unpack(">I", self.f.read(4))
            if hlen >= 0x6A:
                (comp,) = struct.unpack(">H", self.f.read(2))
                if comp != 0:
                    raise SystemExit(f"compressed qcow2 unsupported (type={comp})")
        self.l2_entries = 1 << (self.cluster_bits - 3)
        self.f.seek(l1_off)
        self.l1 = list(struct.unpack(f">{l1_size}Q", self.f.read(8 * l1_size)))
        self.dirty_l2 = set()

    def _l2_table(self, l1_index: int) -> int:
        return self.l1[l1_index] & OFFSET_MASK

    def read(self, offset: int, size: int) -> bytes:
        out = bytearray()
        cluster_size = 1 << self.cluster_bits
        while size:
            ci = offset // cluster_size
            within = offset % cluster_size
            n = min(size, cluster_size - within)
            l1i, l2i = divmod(ci, self.l2_entries)
            l2o = self._l2_table(l1i)
            if l2o == 0:
                out += b"\0" * n
            else:
                self.f.seek(l2o + l2i * 8)
                (ent,) = struct.unpack(">Q", self.f.read(8))
                co = ent & OFFSET_MASK
                if (ent & ZERO_FLAG) or co == 0:
                    out += b"\0" * n
                else:
                    self.f.seek(co + within)
                    out += self.f.read(n)
            offset += n
            size -= n
        return bytes(out)

    def write(self, offset: int, data: bytes) -> None:
        size = len(data)
        cluster_size = 1 << self.cluster_bits
        pos = 0
        while pos < size:
            off = offset + pos
            ci = off // cluster_size
            within = off % cluster_size
            n = min(size - pos, cluster_size - within)
            l1i, l2i = divmod(ci, self.l2_entries)
            l2o = self._l2_table(l1i)
            if l2o == 0:
                raise SystemExit(f"write to unallocated L1 (off=0x{off:X}) - sparse cluster, abort")
            self.f.seek(l2o + l2i * 8)
            (ent,) = struct.unpack(">Q", self.f.read(8))
            co = ent & OFFSET_MASK
            if (ent & ZERO_FLAG) or co == 0:
                raise SystemExit(f"write to zero/sparse cluster (off=0x{off:X}) - abort")
            self.f.seek(co + within)
            self.f.write(data[pos : pos + n])
            pos += n
        self.f.flush()

    def virt_size(self) -> int:
        return self.vsize


def sector(q: Qcow2, lba: int) -> bytes:
    return q.read(lba * 512, 512)


def scan_fatx(q: Qcow2):
    hits = []
    probe = min(q.virt_size(), 512 * 1024 * 1024 * 6)  # first 3 GiB of virtual space
    chunk = 64 * 1024 * 1024
    off = 0
    tail = b""
    while off < probe:
        n = min(chunk, probe - off)
        data = tail + q.read(off, n)
        base_off = off - len(tail)
        start = 0
        while True:
            i = data.find(b"FATX", start)
            if i < 0 or i + 0x50 > len(data):
                break
            abs_i = base_off + i
            if abs_i % 512 == 0:
                serial, clus, fat_clus = struct.unpack("<III", data[i + 4 : i + 16])
                hits.append((abs_i, serial, clus, fat_clus))
            start = i + 4
        tail = data[-64:]
        off += n
    return hits


def scan_xbe_name(q: Qcow2, needle: bytes):
    probe = min(q.virt_size(), 512 * 512 * 1024)
    data = q.read(0, probe)
    hits = []
    start = 0
    while True:
        i = data.find(needle, start)
        if i < 0:
            break
        hits.append(i)
        start = i + 1
    return hits, data


def main():
    q = Qcow2(QCOW)
    print(f"qcow2 vsize=0x{q.virt_size():X} ({q.virt_size()/1024**3:.2f} GiB) cluster_bits={q.cluster_bits}")

    if MODE == "scan":
        for off, serial, clus, fat_clus in scan_fatx(q):
            print(f"FATX @0x{off:X} serial=0x{serial:08X} cluster_sectors={clus} fat_clus={fat_clus}")
        for needle in (b"XBOXDASH.XBE", b"xboxdash.xbe", b"XboxDash.xbe", b"XBOXDASH"):
            hits, data = scan_xbe_name(q, needle)
            for i in hits:
                ctx = data[max(0, i - 16) : i + 64]
                print(f"name {needle!r} @0x{i:X}: {ctx.hex()}")
                # look for u32 BE/LE 770048 (0xBC000) nearby
                for j in range(max(0, i - 64), i + 80):
                    if j + 4 <= len(data):
                        be = struct.unpack(">I", data[j : j + 4])[0]
                        le = struct.unpack("<I", data[j : j + 4])[0]
                        if be == 770048 or le == 770048:
                            print(f"  size 770048 {'BE' if be==770048 else 'LE'} @0x{j:X} (rel {j-i})")
        return

    if MODE != "inject":
        raise SystemExit("mode: scan | inject")

    # ---- inject ----
    xbe = XBE.read_bytes()
    assert xbe[0:4] == b"XBEH"
    print(f"suite XBE {len(xbe)} bytes, log path {xbe[0x43599:0x43599+20]!r}")

    fatx = scan_fatx(q)
    # C: partition = research-notes 47's base LBA 0x465400 => byte 0x8CA80000
    # (contains xboxdash.xbe; serial 0x5B206DDB on this image)
    cands = [h for h in fatx if h[0] == 0x8CA80000] or [
        h for h in fatx if h[1] == 0x5B206DDB
    ] or fatx
    if not cands:
        raise SystemExit("no FATX superblock found")
    base, serial, clus_sz, fat_clus = cands[0]
    print(f"C: base sector=0x{base//512:X} (byte 0x{base:X}) serial=0x{serial:08X} cluster={clus_sz} fat_clus={fat_clus}")

    hdr_sectors = 0x1000 // 512  # 8
    # FAT size: determine from root dir location: root=cluster1. Find FAT size by
    # locating 'FATX' header then reading... we know from 47: FAT=128 sectors u16.
    # Verify: root dir cluster 1 should contain dirents; try fat sizes {128}.
    root_lba = None
    fat_lba = base // 512 + hdr_sectors
    fat_sectors = 128
    for fsz in (128, 64, 256, 32, 512):
        cand_root = base + (hdr_sectors + fsz) * 512
        blk = q.read(cand_root, 4096)
        if any(32 <= b < 127 for b in blk[0:16]) and blk[0] not in (0x00, 0xFF):
            # plausible directory content (attr/namelen bytes then ASCII name)
            if b"XBOX" in blk or b"xbox" in blk or b"evox" in blk or b"EVOX" in blk or b"Disk" in blk:
                fat_sectors = fsz
                root_lba = base // 512 + hdr_sectors + fsz
                print(f"root dir @LBA 0x{root_lba:X} (fat_sectors={fsz})")
                break
    if root_lba is None:
        # fallback: search for XBOXDASH within first 1 MiB after FATX base
        blk = q.read(base, 1024 * 1024)
        i = blk.find(b"XBOXDASH")
        if i < 0:
            i = blk.find(b"xboxdash")
        if i < 0:
            raise SystemExit("xboxdash name not found near FATX base")
        fat_sectors = 128
        root_off = i - (i % 64)  # dirent-aligned guess; refine below
        root_lba = None
        print(f"name found at base+0x{i:X}")

    # find the dirent for the dash by scanning root cluster (cluster 1) - root may be
    # larger than one cluster; walk up to 64 clusters.
    cluster_sectors = clus_sz
    data_start_lba = base // 512 + hdr_sectors + fat_sectors
    # cluster 1 offset: LBA data_start_lba + (1-1)*cluster_sectors  (cluster numbering starts at 1 == first data cluster)
    found = None
    for cl in range(1, 64):
        off = base + (hdr_sectors + fat_sectors + (cl - 1) * cluster_sectors) * 512
        blk = q.read(off, cluster_sectors * 512)
        for needle in (b"XBOXDASH.XBE", b"xboxdash.xbe", b"XBOXDASH"):
            j = blk.find(needle)
            if j >= 0:
                found = (cl, off, j, blk)
                break
        if found:
            break
    if not found:
        raise SystemExit("dash dirent not found in root clusters")

    cl, doff, j, blk = found
    ctx = blk[max(0, j - 16) : j + 64]
    print(f"root cluster={cl} name @+0x{j:X}: {ctx.hex()}")

    # FATX dirent (fatxfs/xbox-linux): u8 attr, u8 namelen, name[up to 42], u32 cluster(BE), u32 size(BE)
    # verify: namelen == len(name), attr typical 0x20
    name_off = j
    namelen = blk[name_off - 1]
    attr = blk[name_off - 2]
    print(f"attr=0x{attr:02X} namelen={namelen}")
    # cluster+size right after padded name field. FATX name field is variable:
    # dirent = [attr:1][namelen:1][name:namelen][pad to 4][cluster:4][size:4]?
    # Empirically find u32 pair after name: search for plausible cluster (small) and size 770048.
    after = name_off + namelen
    p = None
    endian = None
    first_cluster = None
    old_size = None
    # accept either original size 770048 or previously-injected 319488
    for want in (770048, 319488):
        for pad in range(0, 8):
            pp = after + pad
            for e in (">", "<"):
                clu, sz = struct.unpack(e + "II", blk[pp : pp + 8])
                if sz == want and 1 <= clu <= 0x10000:
                    p, endian, first_cluster, old_size = pp, e, clu, sz
            if p is not None:
                break
        if p is not None:
            break
    if p is None:
        # maybe name field fixed 40/42/44 bytes after attr/namelen
        for namelen_field in (40, 42, 44):
            pp = name_off - 2 + 2 + namelen_field
            for e in (">", "<"):
                clu, sz = struct.unpack(e + "II", blk[pp : pp + 8])
                if sz in (770048, 319488) and 1 <= clu <= 0x10000:
                    p, endian, first_cluster, old_size = pp, e, clu, sz
            if p is not None:
                break
    entry = None if p is None else (p, endian, first_cluster, old_size)
    if not entry:
        raise SystemExit("could not parse dirent cluster/size fields")
    p, endian, first_cluster, old_size = entry
    print(f"dirent: cluster_field_off=+0x{p:X} endian={endian} cluster={first_cluster} size={old_size}")

    # walk FAT chain for first_cluster and write suite bytes
    fat_bytes = q.read(base + hdr_sectors * 512, fat_sectors * 512)

    def fat_get(c: int) -> int:
        (v,) = struct.unpack(endian + "H", fat_bytes[c * 2 : c * 2 + 2])
        return v

    need = len(xbe)
    chain = []
    c = first_cluster
    while c not in (0, 0xFFFF) and len(chain) < 100000:
        chain.append(c)
        need -= cluster_sectors * 512
        if need <= 0:
            break
        c = fat_get(c)
        if c >= 0xFFF8:
            break
    capacity = len(chain) * cluster_sectors * 512
    print(f"chain clusters={len(chain)} capacity={capacity} need={len(xbe)}")
    if capacity < len(xbe):
        raise SystemExit("cluster chain too small for suite XBE")

    for idx, cnum in enumerate(chain):
        chunk = xbe[idx * cluster_sectors * 512 : (idx + 1) * cluster_sectors * 512]
        if not chunk:
            break
        coff = base + (hdr_sectors + fat_sectors + (cnum - 1) * cluster_sectors) * 512
        if len(chunk) < cluster_sectors * 512:
            chunk = chunk + b"\0" * (cluster_sectors * 512 - len(chunk))
        q.write(coff, chunk)
    print(f"wrote {len(chain)} clusters starting cluster {first_cluster}")

    # patch dirent size = len(xbe)
    patch_off = doff + p + (4 if endian == ">" else 4)  # size is second u32
    # locate size field precisely: entry started at p (cluster u32), size at p+4
    size_off_in_blk = p + 4
    old = blk[size_off_in_blk : size_off_in_blk + 4]
    new = struct.pack(endian + "I", len(xbe))
    # blk was a copy; write only the 4 size bytes back at absolute offset
    abs_off = doff + size_off_in_blk
    q.write(abs_off, new)
    print(f"dirent size @0x{abs_off:X}: {old.hex()} -> {new.hex()}")

    # verify readback
    rb = q.read(doff + 0, 64)
    print(f"root first bytes now: {rb.hex()}")
    rb_xbe = q.read(base + (hdr_sectors + fat_sectors + (first_cluster - 1) * cluster_sectors) * 512, 16)
    print(f"file head now: {rb_xbe!r}")
    q.f.close()


if __name__ == "__main__":
    main()