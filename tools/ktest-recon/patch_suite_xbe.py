"""patch_suite_xbe.py - kernel-test-suite XBE media patches for gate 120.

Ports the 116 shellcode experiment (IoCreateSymbolicLink from the ignored
load_name_file donor at VA 0x2E620) into a parameterized tool. Facts (from
suite sources, GPL - no code copied, only our own shellcode):

  * log path string 'D:\\kernel_tests.log' at file 0x43599 (VA 0x53599),
    in-place patchable to 'C:\\kernel_tests.log' (same length);
  * thunk slot 0x57F1C = ordinal 67 = IoCreateSymbolicLink (stdcall:
    SymbolicLinkName, DeviceName -> NTSTATUS);
  * donor: load_name_file (VA 0x2E620 = file 0x1E620), 164 bytes, result
    ignored by main().

Variants:
  --log C|D          patch the log path letter (default D = stock)
  --target <dev>     inject shellcode creating '\\??\\C:' -> <dev>
                     (omit = no shellcode, stock binary)
  --park <VA-hex>    overwrite 2 bytes at VA with EB FE (jmp $) so the suite
                     parks at main's return site instead of returning to the
                     kernel (which would relaunch the XBE and truncate the
                     log via CREATE_ALWAYS). §123 smoke uses --park 4E03A.
  --out <file.xbe>   output (default: alongside input, -patched suffix)

Usage:
  python patch_suite_xbe.py --in default.xbe --out out.xbe --log C
  python patch_suite_xbe.py --in default.xbe --out out.xbe --log C \\
      --target '\\Device\\Harddisk0\\Partition2'
"""

import argparse
import struct
from pathlib import Path

DONOR_VA = 0x2E620
DONOR_FILE = DONOR_VA - 0x10000
LOGPATH_FILE = 0x43599
THUNK_SLOT_IoCreateSymbolicLink = 0x57F1C
IMAGE_BASE = 0x10000


def build_shellcode(base: int, target: bytes) -> bytes:
    """Build ANSI_STRINGs and call IoCreateSymbolicLink.

    The Xbox kernel object manager uses OBJECT_STRING = STRING (ANSI), not
    UNICODE_STRING (Cxbx kernel/common/types.h:273 'typedef STRING
    OBJECT_STRING'; the kernel's own path parser/hash/compare at
    0x80021709/0x80020912/0x80022678 all walk BYTES). 116's wide-string
    shellcode and the follow-up stdcall fix still returned
    STATUS_INVALID_PARAMETER because the byte path walker saw NUL-padded
    garbage components.

    STRING1 (link) at [esp+0x10] -> '\\??\\C:'
    STRING2 (device) at [esp+0x60] -> <target>
    IoCreateSymbolicLink (ordinal 67) is STDCALL ('ret 8' at 0x80015E68).
    """
    code = bytearray()

    def emit(bs: bytes):
        code.extend(bs)

    emit(b"\x83\xEC\x70")                             # sub esp,0x70
    fix1 = len(code)
    emit(b"\xBE\x00\x00\x00\x00")                     # mov esi, src1 (fixup)
    emit(b"\x89\xE7")                                 # mov edi,esp
    emit(b"\xB9\x06\x00\x00\x00")                     # mov ecx,6
    emit(b"\xF3\xA4")                                 # rep movsb
    emit(b"\xC6\x07\x00")                             # mov byte[edi],0
    emit(b"\x66\xC7\x44\x24\x10\x06\x00")             # Length=6
    emit(b"\x66\xC7\x44\x24\x12\x07\x00")             # MaximumLength=7
    emit(b"\x89\x64\x24\x14")                         # Buffer=esp
    fix2 = len(code)
    emit(b"\xBE\x00\x00\x00\x00")                     # mov esi, src2 (fixup)
    emit(b"\x8D\x7C\x24\x20")                         # lea edi,[esp+0x20]
    ecx2 = len(target)
    emit(b"\xB9" + struct.pack("<I", ecx2))           # mov ecx,len(target)
    emit(b"\xF3\xA4")                                 # rep movsb
    emit(b"\xC6\x07\x00")                             # mov byte[edi],0
    emit(b"\x66\xC7\x44\x24\x60" + struct.pack("<H", ecx2))       # Length
    emit(b"\x66\xC7\x44\x24\x62" + struct.pack("<H", ecx2 + 1))   # Max
    emit(b"\x8D\x44\x24\x20\x89\x44\x24\x64")         # Buffer=esp+0x20
    emit(b"\x8D\x54\x24\x60")                         # lea edx,[esp+0x60]
    emit(b"\x8D\x44\x24\x10")                         # lea eax,[esp+0x10]
    emit(b"\x83\xEC\x08\x89\x04\x24\x89\x54\x24\x04")
    emit(b"\xFF\x15" + struct.pack("<I", THUNK_SLOT_IoCreateSymbolicLink))
    emit(b"\x31\xC0\x83\xC4\x70\xC3")                 # xor eax,eax; add esp,0x70; ret

    src1 = b"\\??\\C:"
    assert len(src1) == 6
    struct.pack_into("<I", code, fix1 + 1, base + len(code))
    code += src1
    struct.pack_into("<I", code, fix2 + 1, base + len(code))
    code += target
    assert len(code) <= 0xA4, hex(len(code))
    return bytes(code)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", dest="out", required=True)
    ap.add_argument("--log", choices=["C", "D"], default="D")
    ap.add_argument("--target", default=None,
                    help="device path for the \\??\\C: symlink (shellcode)")
    ap.add_argument("--park", default=None,
                    help="VA (hex, e.g. 4E03A) to overwrite with EB FE (jmp $) "
                         "so the suite parks instead of returning; lets a "
                         "monitor poll for completion without a relaunch race "
                         "that would truncate the log (CREATE_ALWAYS). "
                         "§123 smoke: --park 4E03A (main's return site).")
    args = ap.parse_args()

    data = bytearray(Path(args.inp).read_bytes())

    if args.park:
        va = int(args.park, 16)
        off = va - IMAGE_BASE
        assert 0 <= off + 2 <= len(data), (args.park, hex(len(data)))
        data[off:off + 2] = b"\xEB\xFE"
        print("parked VA 0x%X (file 0x%X) with EB FE" % (va, off))

    if args.log == "C":
        old = data[LOGPATH_FILE:LOGPATH_FILE + 20]
        assert old.startswith(b"D:\\kernel_tests.log"), old
        data[LOGPATH_FILE] = ord("C")

    if args.target:
        target = args.target.encode("ascii")
        code = build_shellcode(DONOR_VA, target)
        data[DONOR_FILE:DONOR_FILE + len(code)] = code

    Path(args.out).write_bytes(data)
    print("wrote", args.out, "log=%s:" % args.log,
          "shellcode target=%s" % (args.target or "-"))


if __name__ == "__main__":
    main()
