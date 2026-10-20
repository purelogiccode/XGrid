#!/usr/bin/env python3
"""Differential CPU oracle for XGrid.

Executes the same instruction bytes from the same architectural state on an
independent x86 implementation (QEMU's TCG core via Unicorn, or a QEMU system
machine once installed under References2/qemu) and records the resulting
architectural state for the C# test suite.

Per case the oracle stores:

  * the full code bytes (flag-neutral setup + tested block + epilogue),
  * the initial GPRs and EFLAGS,
  * the final GPRs and EFLAGS (in cases.json),
  * the final FXSAVE image, 512 bytes (concatenated in cases.bin).

The epilogue is `fxsave [0x1f0000]` followed by `int $0x30`.  Both sides run
it, so x87/MMX/SSE state is compared through the architectural FXSAVE layout
rather than through emulator-specific register APIs.

Usage:
    python oracle.py --list
    python oracle.py --emulator unicorn --out ../../data/cpu-oracle/int
    python oracle.py --emulator qemu --out ...      (needs References2/qemu)
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))

CODE_BASE = 0x40000
CASE_STRIDE = 128
PAGE_BASE = 0x200000
PAGE_STRIDE = 0x2000
FXSAVE_ADDR = 0x1F0000
MXCSR_ADDR = 0x1F1000
MEM_SIZE = 0x1000000
SENTINEL = bytes.fromhex("0fae0500001f00cd30")
FXSAVE_SIZE = 512
PAGE_DUMP_SIZE = 256
RECORD_SIZE = FXSAVE_SIZE + PAGE_DUMP_SIZE
SYSTEM_STRIDE = 832  # guest harness: 576-byte state + 256-byte page dump

DEFAULT_REGS = {
    "eax": 0x11111111,
    "ecx": 0x22222222,
    "edx": 0x33333333,
    "ebx": 0x44444444,
    "ebp": 0x55555555,
}


class Case:
    def __init__(self, name, setup, body, regs=None, eflags=0x202):
        self.name = name
        self.setup = setup
        self.body = body
        self.regs = dict(DEFAULT_REGS)
        if regs:
            self.regs.update(regs)
        self.eflags = eflags


def asm_tool(name):
    tool = shutil.which(name)
    if tool:
        return tool
    candidate = r"C:\mingw64\bin" + os.sep + name + ".exe"
    if os.path.exists(candidate):
        return candidate
    raise RuntimeError(f"{name} not found on PATH")


def run(args):
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"{' '.join(args)} failed ({result.returncode}):\n{result.stderr}")


def build_cases_blob(cases, workdir):
    path = os.path.join(workdir, "cases.s")
    lines = [".section .text", ".code32", ".globl _case_start", "_case_start:"]
    for index, case in enumerate(cases):
        page = PAGE_BASE + index * PAGE_STRIDE
        lines.append(f"        .org    {index * CASE_STRIDE}")
        lines.append(f"case_{index:04d}:")
        lines.append(f"        fninit")
        lines.append(f"        emms")
        lines.append(f"        ldmxcsr 0x{MXCSR_ADDR:x}")
        for line in case.setup:
            lines.append("        " + line.format(p=f"0x{page:x}", x=f"0x{FXSAVE_ADDR:x}"))
        for line in case.body:
            lines.append("        " + line.format(p=f"0x{page:x}", x=f"0x{FXSAVE_ADDR:x}"))
        lines.append(f"        fxsave  0x{FXSAVE_ADDR:x}")
        lines.append("        int     $0x30")
    lines.append(f"        .org    {len(cases) * CASE_STRIDE}")
    with open(path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    obj = path + ".o"
    run([asm_tool("as"), "--32", "-o", obj, path])
    binary = path + ".bin"
    run([asm_tool("objcopy"), "-O", "binary", "-j", ".text", obj, binary])
    with open(binary, "rb") as handle:
        return handle.read()


def slice_cases(cases, blob):
    """Returns one code blob per case (setup + body + fxsave + int)."""
    result = []
    for index in range(len(cases)):
        start = index * CASE_STRIDE
        sentinel = blob.find(SENTINEL, start)
        if sentinel < 0 or sentinel >= start + CASE_STRIDE:
            raise RuntimeError(f"case {index}: sentinel not found inside its stride")
        result.append(blob[start:sentinel + len(SENTINEL)])
    return result


def initial_regs(case, index):
    page = PAGE_BASE + index * PAGE_STRIDE
    regs = dict(DEFAULT_REGS)
    regs.update(case.regs)
    page = PAGE_BASE + index * PAGE_STRIDE
    regs.setdefault("esi", page + 0x40)
    regs.setdefault("edi", page + 0x80)
    regs.setdefault("esp", page + 0x1800)
    return regs


def run_unicorn(cases, codes):
    from unicorn import Uc, UcError, UC_ARCH_X86, UC_MODE_32
    from unicorn.x86_const import (
        UC_X86_REG_CR0, UC_X86_REG_CR4, UC_X86_REG_EAX, UC_X86_REG_ECX,
        UC_X86_REG_EDX, UC_X86_REG_EBX, UC_X86_REG_ESP, UC_X86_REG_EBP,
        UC_X86_REG_ESI, UC_X86_REG_EDI, UC_X86_REG_EFLAGS, UC_X86_REG_EIP,
    )

    records = []
    expected = []
    gprs = {
        "eax": UC_X86_REG_EAX, "ecx": UC_X86_REG_ECX, "edx": UC_X86_REG_EDX,
        "ebx": UC_X86_REG_EBX, "esp": UC_X86_REG_ESP, "ebp": UC_X86_REG_EBP,
        "esi": UC_X86_REG_ESI, "edi": UC_X86_REG_EDI,
    }
    for index, (case, code) in enumerate(zip(cases, codes)):
        regs = initial_regs(case, index)
        code_addr = CODE_BASE + index * CASE_STRIDE
        uc = Uc(UC_ARCH_X86, UC_MODE_32)
        uc.mem_map(0, MEM_SIZE)
        uc.mem_write(MXCSR_ADDR, struct.pack("<I", 0x00001F80))
        uc.mem_write(code_addr, code)
        uc.mem_write(FXSAVE_ADDR, b"\xAA" * 512)
        for name, value in regs.items():
            uc.reg_write(gprs[name], value)
        uc.reg_write(UC_X86_REG_CR0, 0x1)
        uc.reg_write(UC_X86_REG_CR4, 0x600)
        uc.reg_write(UC_X86_REG_EFLAGS, (case.eflags & ~0x200) | 0x2)
        uc.reg_write(UC_X86_REG_EIP, code_addr)
        end = code_addr + len(code) - 2
        try:
            uc.emu_start(code_addr, end)
        except UcError as error:
            raise RuntimeError(f"case {index} ({case.name}) failed: {error}") from error
        eip = uc.reg_read(UC_X86_REG_EIP)
        if eip != end:
            raise RuntimeError(f"case {index} ({case.name}) stopped at 0x{eip:x}, expected 0x{end:x}")
        final = {name: uc.reg_read(register) for name, register in gprs.items()}
        final["eflags"] = uc.reg_read(UC_X86_REG_EFLAGS)
        final["eip"] = eip
        expected.append(final)
        records.append(bytes(uc.mem_read(FXSAVE_ADDR, FXSAVE_SIZE))
                       + bytes(uc.mem_read(PAGE_BASE + index * PAGE_STRIDE, PAGE_DUMP_SIZE)))
    return expected, b"".join(records)


def build_boot_image(cases, codes, workdir):
    """QEMU/Bochs system-emulator path (same cases, guest-side harness)."""
    boot_obj = os.path.join(workdir, "boot.o")
    run([asm_tool("as"), "--32", "-o", boot_obj, os.path.join(HERE, "boot.s")])
    boot = os.path.join(workdir, "boot.bin")
    run([asm_tool("objcopy"), "-O", "binary", "-j", ".text", boot_obj, boot])
    if os.path.getsize(boot) != 512:
        raise RuntimeError("boot sector is not exactly 512 bytes")

    harness_obj = os.path.join(workdir, "harness.o")
    run([asm_tool("as"), "--32", "-o", harness_obj, os.path.join(HERE, "harness.s")])
    harness = os.path.join(workdir, "harness.bin")
    run([asm_tool("objcopy"), "-O", "binary", "-j", ".text", harness_obj, harness])

    blob = bytearray()
    for index, code in enumerate(codes):
        if len(code) > CASE_STRIDE:
            raise RuntimeError(f"case {index} code is {len(code)} bytes (stride {CASE_STRIDE})")
        blob.extend(code)
        blob.extend(b"\x90" * (CASE_STRIDE - len(code)))

    with open(boot, "rb") as handle:
        image = bytearray(handle.read())
    with open(harness, "rb") as handle:
        payload = bytearray(handle.read())

    struct.pack_into("<I", payload, 0x1B000 - 0x8000, len(cases))
    payload[0x3000:0x3000 + 0x40 * len(cases)] = build_descriptors(cases, len(cases))
    payload.extend(b"\x00" * max(0, (CODE_BASE - 0x8000) - len(payload)))
    payload[CODE_BASE - 0x8000:CODE_BASE - 0x8000] = blob

    image.extend(payload)
    sectors = (len(image) + 511) // 512
    if sectors > 2880:
        raise RuntimeError(f"image needs {sectors} sectors (max 2880)")
    struct.pack_into("<H", image, 2, sectors)
    image.extend(b"\x00" * (2880 * 512 - len(image)))
    path = os.path.join(workdir, "oracle.img")
    with open(path, "wb") as handle:
        handle.write(image)
    return path


def build_descriptors(cases, count):
    blob = bytearray(count * 64)
    for index, case in enumerate(cases):
        regs = initial_regs(case, index)
        values = [
            CODE_BASE + index * CASE_STRIDE,
            case.eflags & ~0x200,
            regs["eax"], regs["ecx"], regs["edx"], regs["ebx"],
            regs["esp"], regs["ebp"], regs["esi"], regs["edi"],
        ]
        struct.pack_into("<10I", blob, index * 64, *values)
    return blob


def run_qemu(cases, codes, workdir, timeout):
    image = build_boot_image(cases, codes, workdir)
    serial = os.path.join(workdir, "serial.bin")
    qemu = find_qemu()
    args = [
        qemu, "-m", "32", "-fda", image, "-boot", "a", "-display", "none",
        "-serial", f"file:{serial}", "-no-reboot",
        "-device", "isa-debug-exit,iobase=0x501,iosize=0x4",
        "-accel", "tcg,thread=single", "-cpu", "pentium3",
    ]
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    if result.returncode != 1:
        raise RuntimeError(f"qemu exit {result.returncode}\n{result.stdout}\n{result.stderr}")
    with open(serial, "rb") as handle:
        raw = handle.read()
    if len(raw) != len(cases) * SYSTEM_STRIDE:
        raise RuntimeError(f"serial returned {len(raw)} bytes, expected {len(cases) * SYSTEM_STRIDE}")
    expected = []
    records = []
    for index in range(len(cases)):
        record = raw[index * SYSTEM_STRIDE:(index + 1) * SYSTEM_STRIDE]
        values = struct.unpack_from("<10I", record, 0)
        expected.append({
            "eax": values[0], "ecx": values[1], "edx": values[2], "ebx": values[3],
            "esp": values[4], "ebp": values[5], "esi": values[6], "edi": values[7],
            "eflags": values[8], "eip": values[9],
        })
        records.append(record[0x30:0x30 + FXSAVE_SIZE] + record[576:576 + PAGE_DUMP_SIZE])
    return expected, b"".join(records)


def find_qemu():
    candidates = [
        os.environ.get("XGRID_QEMU", ""),
        os.path.join(REPO, "References2", "qemu", "qemu-system-i386.exe"),
        shutil.which("qemu-system-i386") or "",
    ]
    for candidate in candidates:
        if candidate and os.path.exists(candidate):
            return candidate
    raise RuntimeError("qemu-system-i386 not found; set XGRID_QEMU or install under References2/qemu")


def write_corpus(cases, codes, expected, records, json_path, bin_path):
    entries = []
    for index, (case, code) in enumerate(zip(cases, codes)):
        regs = initial_regs(case, index)
        entries.append({
            "name": case.name,
            "code": code.hex(),
            "registers": {name: f"0x{value:08x}" for name, value in regs.items()},
            "eflags": f"0x{case.eflags & ~0x200:08x}",
            "flagsMask": f"0x{case.flags_mask:08x}",
            "expect": {
                "registers": {name: f"0x{value:08x}" for name, value in expected[index].items()
                              if name not in ("eflags", "eip")},
                "eflags": f"0x{expected[index]['eflags']:08x}",
                "eip": f"0x{expected[index]['eip']:08x}",
            },
            "record": index,
        })
    document = {"version": 1, "recordSize": RECORD_SIZE, "cases": entries}
    with open(json_path, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=1)
        handle.write("\n")
    with open(bin_path, "wb") as handle:
        handle.write(records)


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("--emulator", choices=["unicorn", "qemu"], default="unicorn")
    parser.add_argument("--out", default=os.path.join(REPO, "tests", "XGrid.Cpu.Tests", "cpu-oracle", "int"))
    parser.add_argument("--timeout", type=int, default=600)
    parser.add_argument("--keep", action="store_true")
    parser.add_argument("--list", action="store_true")
    options = parser.parse_args(argv)

    sys.path.insert(0, HERE)
    from corpus import build_corpus

    cases = build_corpus()
    if options.list:
        for case in cases:
            print(case.name)
        return 0

    workdir = tempfile.mkdtemp(prefix="xgrid-cpu-oracle-")
    try:
        blob = build_cases_blob(cases, workdir)
        codes = slice_cases(cases, blob)
        if options.emulator == "unicorn":
            expected, records = run_unicorn(cases, codes)
        else:
            expected, records = run_qemu(cases, codes, workdir, options.timeout)
        os.makedirs(os.path.dirname(options.out), exist_ok=True)
        write_corpus(cases, codes, expected, records, options.out + ".json", options.out + ".dat")
        print(f"{len(cases)} cases -> {options.out}.json/.dat")
    finally:
        if options.keep:
            print(f"workdir: {workdir}")
        else:
            shutil.rmtree(workdir, ignore_errors=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
