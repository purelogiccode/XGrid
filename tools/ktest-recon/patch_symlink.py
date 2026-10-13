import struct
from pathlib import Path

p = Path(r"C:\Users\HomePC\AppData\Local\Temp\opencode\worktrees\ktest-suite\wt-data\ktest-iso\default.xbe")
b = bytearray(p.read_bytes())

# 1) restore log path to C:\kernel_tests.log (drive-letter form)
path = b"C:\\kernel_tests.log" + b"\x00"
assert len(path) == 20
b[0x43599 : 0x43599 + 20] = path

# 2) shellcode in load_name_file (VA 0x2E620, file 0x1E620): create \??\C: symlink
#    then main's ignored call returns 0; open_output_file then resolves C: via the letter.
base = 0x2E620
code = bytearray()

def emit(bs: bytes):
    code.extend(bs)

emit(b"\x83\xEC\x70")                 # sub esp,0x70
emit(b"\xBE" + struct.pack("<I", base + 0x82))  # mov esi, src1 (fixup checked below)
emit(b"\x89\xE7")                     # mov edi,esp
emit(b"\xB9\x06\x00\x00\x00")         # mov ecx,6
# L1 @ +0x0F
emit(b"\x8A\x06")                     # mov al,[esi]
emit(b"\x46")                         # inc esi
emit(b"\x0F\xB6\xC0")                 # movzx eax,al
emit(b"\x66\x89\x07")                 # mov [edi],ax
emit(b"\x83\xC7\x02")                 # add edi,2
emit(b"\xE2\xF2")                     # loop L1
emit(b"\x66\xC7\x44\x24\x10\x0C\x00")  # mov word[esp+0x10],12
emit(b"\x66\xC7\x44\x24\x12\x0E\x00")  # mov word[esp+0x12],14
emit(b"\x89\x64\x24\x14")             # mov [esp+0x14],esp
emit(b"\xBE" + struct.pack("<I", base + 0x88))  # mov esi, src2 (fixup checked below)
emit(b"\x8D\x7C\x24\x20")             # lea edi,[esp+0x20]
emit(b"\xB9\x1C\x00\x00\x00")         # mov ecx,28
# L2
emit(b"\x8A\x06")
emit(b"\x46")
emit(b"\x0F\xB6\xC0")
emit(b"\x66\x89\x07")
emit(b"\x83\xC7\x02")
emit(b"\xE2\xF2")
emit(b"\x66\xC7\x44\x24\x60\x38\x00")  # mov word[esp+0x60],56
emit(b"\x66\xC7\x44\x24\x62\x3A\x00")  # mov word[esp+0x62],58
emit(b"\x8D\x44\x24\x20")             # lea eax,[esp+0x20]
emit(b"\x89\x44\x24\x64")             # mov [esp+0x64],eax
emit(b"\x8D\x54\x24\x60")             # lea edx,[esp+0x60]  (device US)
emit(b"\x8D\x44\x24\x10")             # lea eax,[esp+0x10]  (link US)
emit(b"\x83\xEC\x08")                 # sub esp,8
emit(b"\x89\x04\x24")                 # mov [esp],eax
emit(b"\x89\x54\x24\x04")             # mov [esp+4],edx
emit(b"\xFF\x15\x1C\x7F\x05\x00")     # call [0x57F1C] IoCreateSymbolicLink
emit(b"\x83\xC4\x08")                 # add esp,8
emit(b"\x31\xC0")                     # xor eax,eax
emit(b"\x83\xC4\x70")                 # add esp,0x70
emit(b"\xC3")                         # ret

assert len(code) == 0x82, hex(len(code))
src1 = b"\\??\\C:"          # 6
src2 = b"\\Device\\Harddisk0\\Partition2"  # 27
assert len(src1) == 6 and len(src2) == 28
code += src1 + src2
assert len(code) <= 0x270, hex(len(code))

# sanity on the two fixups
assert struct.unpack_from("<I", code, 4)[0] == base + 0x82, hex(struct.unpack_from("<I", code, 4)[0])
be_positions = [i for i in range(len(code) - 4) if code[i] == 0xBE and i != 3]
assert len(be_positions) == 1, be_positions
assert struct.unpack_from("<I", code, be_positions[0] + 1)[0] == base + 0x88

donor = bytes(b[0x1E620 : 0x1E620 + len(code)])
b[0x1E620 : 0x1E620 + len(code)] = code

p.write_bytes(b)
print(f"shellcode {len(code)} bytes at VA 0x{base:X}")
print("old donor head:", donor[:16].hex())
print("new head:", bytes(code[:16]).hex())
print("path slot:", bytes(b[0x43599:0x43599+20]))




