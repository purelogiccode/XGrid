"""va2pa.py - translate a guest VA via the page tables in a RAM dump.

Usage: python va2pa.py <ram.bin> <cr3_hex> <va_hex> [<va_hex> ...]
"""

import struct
import sys

path, cr3 = sys.argv[1], int(sys.argv[2], 16)
data = open(path, 'rb').read()


def read32(phys):
    return struct.unpack_from('<I', data, phys)[0]


for arg in sys.argv[3:]:
    va = int(arg, 16)
    pde_addr = cr3 + 4 * (va >> 22)
    pde = read32(pde_addr)
    if not pde & 1:
        print('%08X: PDE %08X not present' % (va, pde))
        continue
    pt = pde & 0xFFFFF000
    pte_addr = pt + 4 * ((va >> 12) & 0x3FF)
    pte = read32(pte_addr)
    if not pte & 1:
        print('%08X: PTE %08X not present (pt=%08X)' % (va, pte, pt))
        continue
    phys = (pte & 0xFFFFF000) | (va & 0xFFF)
    print('%08X -> %08X (pde=%08X pte=%08X)' % (va, phys, pde, pte))
