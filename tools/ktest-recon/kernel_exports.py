"""kernel_exports.py - parse the kernel PE export table from a RAM dump.

The kernel image sits at phys 0x10000 (VA 0x80010000). Prints the address and
first bytes of a requested ordinal so calling-convention facts (ret N vs ret)
can be confirmed (120 H3).

Usage: python kernel_exports.py <ram.bin> <ordinal> [<ordinal> ...]
"""

import struct
import sys

BASE_PHYS = 0x10000
BASE_VA = 0x80010000

data = open(sys.argv[1], 'rb').read()
img = data[BASE_PHYS:]

e_lfanew = struct.unpack_from('<I', img, 0x3C)[0]
assert img[e_lfanew:e_lfanew + 4] == b'PE\x00\x00', img[e_lfanew:e_lfanew + 4]
coff = e_lfanew + 4
nsections = struct.unpack_from('<H', img, coff + 2)[0]
opt_size = struct.unpack_from('<H', img, coff + 16)[0]
opt = coff + 20
magic = struct.unpack_from('<H', img, opt)[0]
assert magic == 0x10B, hex(magic)  # PE32
export_rva, export_size = struct.unpack_from('<II', img, opt + 96)
dd = opt + 96


def rva_to_off(rva):
    sec = opt + opt_size
    for i in range(nsections):
        s = sec + i * 40
        vsize, vaddr, rawsize, rawptr = struct.unpack_from('<IIII', img, s + 8)
        if vaddr <= rva < vaddr + max(vsize, rawsize):
            return rawptr + (rva - vaddr)
    raise ValueError(hex(rva))


exp = rva_to_off(export_rva)
ordinal_base = struct.unpack_from('<I', img, exp + 16)[0]
nfuncs = struct.unpack_from('<I', img, exp + 20)[0]
funcs_rva = struct.unpack_from('<I', img, exp + 28)[0]
names_rva = struct.unpack_from('<I', img, exp + 32)[0]
print('ordinal base', ordinal_base, 'nfuncs', nfuncs)

for arg in sys.argv[2:]:
    ordinal = int(arg)
    idx = ordinal - ordinal_base
    funcs = rva_to_off(funcs_rva)
    rva = struct.unpack_from('<I', img, funcs + idx * 4)[0]
    va = BASE_VA + rva
    off = rva_to_off(rva)
    head = img[off:off + 16]
    name = '(unnamed)'
    names = rva_to_off(names_rva)
    n = struct.unpack_from('<I', img, exp + 24)[0]
    ords = rva_to_off(struct.unpack_from('<I', img, exp + 36)[0])
    for j in range(n):
        ordv = struct.unpack_from('<H', img, ords + j * 2)[0]
        if ordv == idx:
            nrva = struct.unpack_from('<I', img, names + j * 4)[0]
            noff = rva_to_off(nrva)
            name = img[noff:noff + 64].split(b'\x00', 1)[0].decode('ascii', 'replace')
            break
    ret8 = head[:2] == b'\xc2'
    print('ordinal %3d %-24s VA=%08X head=%s %s' % (
        ordinal, name, va, head.hex(),
        'stdcall ret %d' % struct.unpack_from('<H', head, 2)[0] if ret8
        else ('ret' if head[:1] == b'\xc3' else '')))
