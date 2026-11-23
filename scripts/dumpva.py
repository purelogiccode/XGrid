"""Extract a guest VA window from an XGrid --ramdump file via x86 page tables.

Usage: python scripts/dumpva.py <ramdump> <cr3> <va> <length> <out.bin>
Also prints VA->phys mapping for each 4KB page in the window.
"""
import sys


def main() -> int:
    ram_path, cr3_text, va_text, len_text, out_path = sys.argv[1:6]
    cr3 = int(cr3_text, 16)
    va0 = int(va_text, 16)
    length = int(len_text, 16)
    ram = open(ram_path, "rb").read()

    def rd32(addr: int) -> int:
        if addr + 4 > len(ram):
            return 0
        return int.from_bytes(ram[addr : addr + 4], "little")

    out = bytearray()
    page_map = []
    for off in range(0, length, 0x1000):
        va = va0 + off
        pde = rd32(cr3 + 4 * (va >> 22))
        if not (pde & 1):
            out.extend(b"\xCC" * 0x1000)
            page_map.append((va, None))
            continue
        if pde & 0x80:
            phys = (pde & 0xFFC00000) | (va & 0x3FFFFF)
        else:
            pte = rd32((pde & 0xFFFFF000) + 4 * ((va >> 12) & 0x3FF))
            if not (pte & 1):
                out.extend(b"\xCC" * 0x1000)
                page_map.append((va, None))
                continue
            phys = (pte & 0xFFFFF000) | (va & 0xFFF)
        chunk = ram[phys : phys + 0x1000]
        if len(chunk) < 0x1000:
            chunk = chunk + b"\xCC" * (0x1000 - len(chunk))
        out.extend(chunk)
        page_map.append((va, phys))

    open(out_path, "wb").write(out)
    for va, phys in page_map:
        print(f"VA {va:08X} -> phys {phys if phys is None else format(phys, '08X')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
