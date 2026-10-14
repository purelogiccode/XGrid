"""Scan an extracted module .text binary for state-block handshake idiom sites.

Usage: python scripts/scanstate.py <modtext.bin> <base_va_hex>
Prints every site of:
  - add reg,0x800 followed within 24 bytes by [reg+0x10] cmp/mov
  - mov dword [reg+0x10], imm
"""
import re
import sys


def main() -> int:
    path, base_text = sys.argv[1], sys.argv[2]
    base = int(base_text, 16)
    data = open(path, "rb").read()

    def va(off: int) -> int:
        return base + off

    print("== add reg,0x800 sites ==")
    # 05 00 08 00 00 (add eax,0x800), 81 c0..cf 00 08 00 00, 81 e?..??
    for m in re.finditer(rb"\x05\x00\x08\x00\x00", data):
        off = m.start()
        window = data[off : off + 0x30]
        print(f"VA {va(off):08X}  add eax,0x800  bytes={window.hex(' ')}")
    for m in re.finditer(rb"\x81[\xc0-\xcf]\x00\x08\x00\x00", data):
        off = m.start()
        window = data[off : off + 0x30]
        print(f"VA {va(off):08X}  add r32,0x800  bytes={window.hex(' ')}")

    print("== mov dword [reg+0x10],imm8/32 ==")
    # C7 40/41/42/43/45/46/47 10 imm32   and  C6 40.. 10 imm8
    for m in re.finditer(rb"\xc7[\x40-\x47]\x10", data):
        off = m.start()
        imm = int.from_bytes(data[off + 3 : off + 7], "little", signed=True)
        print(f"VA {va(off):08X}  mov dword [r+0x10],{imm}  ctx={data[max(0,off-8):off+7].hex(' ')}")
    for m in re.finditer(rb"\xc7\x80\x10\x00\x00\x00", data):
        off = m.start()
        imm = int.from_bytes(data[off + 6 : off + 10], "little", signed=True)
        print(f"VA {va(off):08X}  mov dword [eax+0x10d],{imm} ctx={data[max(0,off-8):off+10].hex(' ')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
