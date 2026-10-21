#!/usr/bin/env python3
"""Instruction corpus for the differential CPU oracle.

Every case is a flag-neutral setup (mov/fld only) followed by the block under
test.  The oracle appends `fxsave [0x1f0000]` + `int $0x30` and every case
starts with `fninit; emms; ldmxcsr [0x1f1000]`, so each case is independent
of the previous one.

The selection mirrors the shapes the raw JIT actually executes at 1B steps
(`XGRID_RAW_PROFILE` mix: mov load/store, inc/dec, ALU reg/reg and reg/imm,
cmp/test, shifts, x87 loads/stores/arithmetic, MMX moves, string movs,
high-byte moves, fnstsw, rotates).
"""

from __future__ import annotations


FULL_FLAGS = 0x8D5  # CF | PF | AF | ZF | SF | OF


class Case:
    def __init__(self, name, setup, body, regs=None, eflags=0x202, flags_mask=FULL_FLAGS):
        self.name = name
        self.setup = list(setup)
        self.body = list(body)
        self.regs = {} if regs is None else dict(regs)
        self.eflags = eflags
        self.flags_mask = flags_mask


def case(name, body, regs=None, eflags=0x202, setup=None, flags_mask=FULL_FLAGS):
    return Case(name, [] if setup is None else setup, body, regs, eflags, flags_mask)


def mem32(offset, value):
    return f"movl    ${value & 0xFFFFFFFF:#010x}, {{p}}+{offset:#x}"


def mem16(offset, value):
    return f"movw    ${value & 0xFFFF:#06x}, {{p}}+{offset:#x}"


def mem8(offset, value):
    return f"movb    ${value & 0xFF:#04x}, {{p}}+{offset:#x}"


ALU_OPS = ["add", "or", "adc", "sbb", "and", "sub", "xor", "cmp"]


def alu_cases():
    cases = []
    pairs = [
        (0x7FFFFFFF, 0x00000001),
        (0x80000000, 0xFFFFFFFF),
        (0xFFFFFFFF, 0x00000001),
        (0x12345678, 0x87654321),
    ]
    for op in ALU_OPS:
        for index, (a, b) in enumerate(pairs):
            cases.append(case(
                f"alu_{op}_32_{index}",
                [f"{op}l    %ebx, %eax"],
                regs={"eax": a, "ebx": b},
            ))
        cases.append(case(
            f"alu_{op}_32_cf1",
            [f"{op}l    %ebx, %eax"],
            regs={"eax": 0xFFFFFFFF, "ebx": 0x00000000},
            eflags=0x203,
        ))
        cases.append(case(
            f"alu_{op}_8",
            [f"{op}b    %bl, %al"],
            regs={"eax": 0x000000FF, "ebx": 0x00000001},
            eflags=0x203,
        ))
        cases.append(case(
            f"alu_{op}_16",
            [f"{op}w    %bx, %ax"],
            regs={"eax": 0x00008000, "ebx": 0x0000FFFF},
            eflags=0x203,
        ))
        cases.append(Case(
            f"alu_{op}_32_rm",
            [mem32(0x00, 0x80000001)],
            [f"{op}l    {{p}}+0x0, %eax"],
            regs={"eax": 0x7FFFFFFF},
        ))
        cases.append(Case(
            f"alu_{op}_32_mr",
            [mem32(0x00, 0x7FFFFFFF)],
            [f"{op}l    %eax, {{p}}+0x0", "movl    {p}+0x0, %ebx"],
            regs={"eax": 0x00000001},
        ))
        cases.append(Case(
            f"alu_{op}_32_mi",
            [mem32(0x00, 0xFFFFFFFE)],
            [f"{op}l    $0x2, {{p}}+0x0", "movl    {p}+0x0, %eax"],
        ))
    return cases


def inc_dec_cases():
    cases = []
    for mnemonic, value in [("inc", 0x7FFFFFFF), ("dec", 0x80000000),
                            ("neg", 0x80000000), ("not", 0x12345678)]:
        cases.append(case(f"unary_{mnemonic}_32", [f"{mnemonic}l  %eax"], regs={"eax": value}))
        cases.append(Case(
            f"unary_{mnemonic}_mem",
            [mem32(0x00, value)],
            [f"{mnemonic}l  {{p}}+0x0", "movl    {p}+0x0, %ebx"],
        ))
    cases.append(case("unary_inc_8", ["incb    %al"], regs={"eax": 0x000000FF}))
    cases.append(case("unary_dec_16", ["decw    %ax"], regs={"eax": 0x00008000}))
    cases.append(case("unary_neg_8", ["negb    %al"], regs={"eax": 0x00000080}))
    cases.append(case("unary_not_16", ["notw    %ax"], regs={"eax": 0x0000FF00}))
    return cases


def shift_cases():
    cases = []
    for mnemonic in ["shl", "shr", "sar", "rol", "ror", "rcl", "rcr"]:
        for count in [0, 1, 15, 31, 32, 33]:
            # OF is defined only for count 1; AF is undefined for any shift.
            # ROL/ROR/RCL/RCR leave SF/ZF/PF/AF untouched.
            if mnemonic in ("rol", "ror", "rcl", "rcr"):
                mask = FULL_FLAGS if count == 0 else (0x801 if count == 1 else 0x001)
                if count == 0:
                    mask = FULL_FLAGS
            else:
                mask = FULL_FLAGS if count == 0 else (0x8C5 if count == 1 else 0x0C5)
            cases.append(case(
                f"shift_{mnemonic}_imm_{count}",
                [f"{mnemonic}l  ${count}, %eax"],
                regs={"eax": 0x80000001},
                eflags=0x203,
                flags_mask=mask,
            ))
        cases.append(case(
            f"shift_{mnemonic}_cl",
            [f"{mnemonic}l  %cl, %eax"],
            regs={"eax": 0x12345678, "ecx": 0x0000000C},
            eflags=0x203,
            flags_mask=0x001 if mnemonic in ("rol", "ror", "rcl", "rcr") else 0x0C5,
        ))
    cases.append(case("shift_shl_8", ["shlb    $1, %al"], regs={"eax": 0x00000081},
                      eflags=0x203, flags_mask=0x8C5))
    cases.append(case("shift_sar_16", ["sarw    $3, %ax"], regs={"eax": 0x00008000},
                      flags_mask=0x0C5))
    cases.append(case("shift_shld", ["shldl   $4, %ebx, %eax"],
                      regs={"eax": 0x12345678, "ebx": 0x9ABCDEF0}, flags_mask=0x0C5))
    cases.append(case("shift_shrd", ["shrdl   %cl, %ebx, %eax"],
                      regs={"eax": 0x12345678, "ebx": 0x9ABCDEF0, "ecx": 5}, flags_mask=0x0C5))
    return cases


def muldiv_cases():
    cases = []
    pairs = [(0x00001234, 0x00005678), (0xFFFFFFFF, 0x00000002), (0x80000000, 0x00000001)]
    for index, (a, b) in enumerate(pairs):
        cases.append(case(f"mul_32_{index}", ["mull    %ebx"], regs={"eax": a, "ebx": b}, flags_mask=0x801))
        cases.append(case(f"imul_32_{index}", ["imull   %ebx"], regs={"eax": a, "ebx": b}, flags_mask=0x801))
        cases.append(case(f"imul_2op_{index}", ["imull   %ebx, %eax"], regs={"eax": a, "ebx": b}, flags_mask=0x801))
        cases.append(case(f"imul_3op8_{index}", ["imull   $7, %ebx, %eax"], regs={"eax": a, "ebx": b}, flags_mask=0x801))
        cases.append(case(f"imul_3op32_{index}", ["imull   $0x1234, %ebx, %eax"],
                          regs={"eax": a, "ebx": b}, flags_mask=0x801))
    cases.append(case("mul_16", ["mulw    %bx"], regs={"eax": 0xFFFF0000, "ebx": 0x00000003}, flags_mask=0x801))
    cases.append(case("imul_16", ["imulw   %bx"], regs={"eax": 0x80000000, "ebx": 0x00000003}, flags_mask=0x801))
    cases.append(case("mul_8", ["mulb    %bl"], regs={"eax": 0x000000FF, "ebx": 0x000000FF}, flags_mask=0x801))
    cases.append(case("div_32", ["divl    %ebx"], regs={"eax": 1000, "edx": 0, "ebx": 7}, flags_mask=0))
    cases.append(case("div_32_large", ["divl    %ebx"],
                      regs={"eax": 0xFFFFFFFF, "edx": 0x00000001, "ebx": 3}, flags_mask=0))
    cases.append(case("idiv_32_neg", ["idivl   %ebx"],
                      regs={"eax": 0xFFFFFF9C, "edx": 0xFFFFFFFF, "ebx": 7}, flags_mask=0))
    cases.append(case("idiv_32_pos", ["idivl   %ebx"], regs={"eax": 1000, "edx": 0, "ebx": 7}, flags_mask=0))
    cases.append(case("div_16", ["divw    %bx"], regs={"eax": 1000 << 16, "edx": 0, "ebx": 7}, flags_mask=0))
    cases.append(case("idiv_16", ["idivw   %bx"],
                      regs={"eax": 0xFFFF0000, "edx": 0xFFFFFFFF, "ebx": 7}, flags_mask=0))
    cases.append(case("div_8", ["divb    %bl"], regs={"eax": 1000, "ebx": 7}, flags_mask=0))
    cases.append(case("idiv_8", ["idivb   %bl"], regs={"eax": 0xFFFFFF00, "ebx": 7}, flags_mask=0))
    return cases


def move_cases():
    return [
        case("mov_r32", ["movl    %ebx, %eax"], regs={"eax": 0, "ebx": 0xDEADBEEF}),
        case("mov_r16", ["movw    %bx, %ax"], regs={"eax": 0x11111111, "ebx": 0x2222BEEF}),
        case("mov_r8", ["movb    %bl, %al"], regs={"eax": 0x11111111, "ebx": 0x22222234}),
        case("mov_high8", ["movb    %ah, %cl"], regs={"eax": 0x12345678, "ecx": 0xAAAAAAAA}),
        case("mov_high8_2", ["movb    %ch, %dh"], regs={"ecx": 0x12345678, "edx": 0xAAAAAAAA}),
        case("mov_imm_high8", ["movb    $0x7F, %ah"], regs={"eax": 0xAAAAAAAA}),
        case("movzx_8", ["movzbl  %bl, %eax"], regs={"eax": 0x11111111, "ebx": 0x000000FF}),
        case("movzx_16", ["movzwl  %bx, %eax"], regs={"eax": 0x11111111, "ebx": 0x0000FFFF}),
        case("movsx_8", ["movsbl  %bl, %eax"], regs={"eax": 0x11111111, "ebx": 0x00000080}),
        case("movsx_16", ["movswl  %bx, %eax"], regs={"eax": 0x11111111, "ebx": 0x00008000}),
        case("movzx_byte_high", ["movzbl  %bh, %eax"], regs={"eax": 0x11111111, "ebx": 0x0000AB00}),
        case("xchg_rr", ["xchgl   %ebx, %eax"], regs={"eax": 0x11111111, "ebx": 0x22222222}),
        Case("xchg_rm", [mem32(0x00, 0x33333333)],
             ["xchgl   %ebx, {p}+0x0"], regs={"ebx": 0x22222222}),
        case("bswap", ["bswapl  %eax"], regs={"eax": 0x12345678}),
        case("cbw", ["cbw"], regs={"eax": 0x00000080}),
        case("cwde", ["cwde"], regs={"eax": 0x00008000}),
        case("cdq", ["cdq"], regs={"eax": 0x80000000, "edx": 0x11111111}),
        case("cwd", ["cwd"], regs={"eax": 0x00008000, "edx": 0x11111111}),
        case("lea_disp32", ["leal    0x12345678, %eax"]),
        case("lea_reg_disp8", ["leal    0x10(%ebx), %eax"], regs={"ebx": 0x2000}),
        case("lea_sib", ["leal    0x10(%ebx,%ecx,4), %eax"], regs={"ebx": 0x2000, "ecx": 0x100}),
        case("lea_ebp_only", ["leal    0x20(%ebp), %eax"], regs={"ebp": 0x3000}),
        case("lea_sib_esp", ["leal    0x10(%esp,%eax,2), %eax"], regs={"esp": 0x1000, "eax": 4}),
    ]


def stack_cases():
    return [
        case("push_pop_r", ["pushl   %eax", "popl     %ebx"], regs={"eax": 0xDEADBEEF}),
        case("push_imm", ["pushl   $0x12345678", "popl     %eax"]),
        case("push_imm8", ["pushl   $0x7F", "popl     %eax"], eflags=0x203),
        Case("push_mem", [mem32(0x00, 0xCAFEBABE)], ["pushl   {p}+0x0", "popl     %eax"]),
        Case("pop_mem", [mem32(0x00, 0xCAFEBABE)], ["popl    {p}+0x0"]),
        case("pusha_popa", ["pushal", "movl    $0x11111111, %eax", "popal"],
             regs={"eax": 0x12345678, "ebx": 0x9ABCDEF0}),
        case("pushfd_popfd", ["pushfl", "popfl"]),
    ]


def bit_cases():
    cases = []
    for mnemonic in ["bt", "bts", "btr", "btc"]:
        cases.append(case(f"{mnemonic}_imm", [f"{mnemonic}l   $5, %eax"], regs={"eax": 0xFFFFFFDF}, flags_mask=0x001))
        cases.append(case(f"{mnemonic}_reg", [f"{mnemonic}l   %ebx, %eax"],
                          regs={"eax": 0xFFFFFFDF, "ebx": 5}, flags_mask=0x001))
        cases.append(Case(
            f"{mnemonic}_mem",
            [mem32(0x00, 0xFFFFFFDF)],
            [f"{mnemonic}l   $5, {{p}}+0x0", "movl    {p}+0x0, %eax"],
        ))
    cases.append(case("bsf", ["bsfl    %ebx, %eax"], regs={"eax": 0x11111111, "ebx": 0x00000100}, flags_mask=0x040))
    cases.append(case("bsr", ["bsrl    %ebx, %eax"], regs={"eax": 0x11111111, "ebx": 0x00000100}, flags_mask=0x040))
    cases.append(case("bsf_zero", ["bsfl    %ebx, %eax"], regs={"eax": 0x11111111, "ebx": 0}, flags_mask=0x040))
    cases.append(case("bsr_zero", ["bsrl    %ebx, %eax"], regs={"eax": 0x11111111, "ebx": 0}, flags_mask=0x040))
    return cases


def atomic_cases():
    return [
        case("cmpxchg_eq", ["cmpxchgl %ebx, %ecx"],
             regs={"eax": 0x11111111, "ebx": 0x22222222, "ecx": 0x11111111}),
        case("cmpxchg_ne", ["cmpxchgl %ebx, %ecx"],
             regs={"eax": 0x11111111, "ebx": 0x22222222, "ecx": 0x33333333}),
        Case("cmpxchg_mem", [mem32(0x00, 0x11111111)],
             ["cmpxchgl %ebx, {p}+0x0", "movl    {p}+0x0, %ecx"],
             regs={"eax": 0x11111111, "ebx": 0x22222222}),
        case("xadd", ["xaddl   %ebx, %eax"], regs={"eax": 0x11111111, "ebx": 0x22222222}),
        Case("xadd_mem", [mem32(0x00, 0x11111111)],
             ["xaddl   %ebx, {p}+0x0", "movl    {p}+0x0, %eax"],
             regs={"ebx": 0x22222222}),
    ]


def condition_cases():
    conditions = ["o", "no", "b", "ae", "e", "ne", "be", "a"]
    cases = []
    for index, condition in enumerate(conditions):
        eflags = [0x202, 0x203, 0x242, 0x207, 0x282, 0x286, 0x200, 0x2C3][index]
        cases.append(case(f"set{condition}", [f"set{condition} %al"],
                          regs={"eax": 0xFFFFFF00}, eflags=eflags))
    for condition in ["e", "ne", "l", "ge"]:
        cases.append(case(f"cmov{condition}_taken", [f"cmov{condition}l %ebx, %eax"],
                          regs={"eax": 0x11111111, "ebx": 0x22222222}, eflags=0x202))
        cases.append(case(f"cmov{condition}_clear", [f"cmov{condition}l %ebx, %eax"],
                          regs={"eax": 0x11111111, "ebx": 0x22222222}, eflags=0x2C3))
    for condition in ["z", "nz"]:
        for index, eflags in enumerate([0x202, 0x242]):
            cases.append(case(
                f"j{condition}_{index}",
                [f"j{condition} 1f", "movl    $0x11111111, %eax", "1:", "movl    $0x22222222, %ebx"],
                eflags=eflags,
            ))
    cases.append(case("jecxz_taken", ["jecxz   1f", "movl    $0x11111111, %eax", "1:", "nop"],
                      regs={"ecx": 0}))
    cases.append(case("jecxz_clear", ["jecxz   1f", "movl    $0x11111111, %eax", "1:", "nop"],
                      regs={"ecx": 1}))
    return cases


def string_cases():
    # ESI starts at page+0x40, EDI at page+0x80 (oracle defaults).
    return [
        Case("movsb", [mem8(0x40, 0xAB), mem8(0x41, 0xCD)], ["movsb"]),
        Case("movsl", [mem32(0x40, 0x11223344)], ["movsl"]),
        Case("stosb", [], ["stosb"], regs={"eax": 0x0000005A}),
        Case("stosl", [], ["stosl"], regs={"eax": 0xDEADBEEF}),
        Case("lodsb", [mem8(0x40, 0x7E)], ["lodsb"]),
        Case("lodsl", [mem32(0x40, 0xCAFEBABE)], ["lodsl"]),
        Case("scasb", [mem8(0x80, 0x42)], ["scasb"], regs={"eax": 0x00000042}),
        Case("cmpsb", [mem8(0x40, 0x10), mem8(0x80, 0x20)], ["cmpsb"]),
        Case("rep_movsb", [mem8(0x40, 0x11), mem8(0x41, 0x22), mem8(0x42, 0x33)],
             ["rep movsb"], regs={"ecx": 3}),
        Case("rep_stosl", [], ["rep stosl"], regs={"eax": 0xA5A5A5A5, "ecx": 4}),
        Case("std_movsb", [mem8(0x40, 0x99)], ["std", "movsb", "cld"]),
        Case("movsw", [mem16(0x40, 0xBEEF), mem16(0x42, 0x1234)], ["movsw"]),
    ]


def mem64(offset, value):
    return f"movq    ${value & 0xFFFFFFFFFFFFFFFF:#x}, {{p}}+{offset:#x}"


def packed_pattern_cases():
    """The MMX/SSE1 packed-integer shapes with sign-sensitive data.

    The base corpus feeds one positive pattern. These cases feed a pattern that
    is negative in every element (so a signed/unsigned slip cannot hide), a
    saturating pattern for the add/sub pair, and the PMADDWD wraparound pair.
    """
    negative = [
        mem32(0x00, 0x8000FFFF), mem32(0x04, 0xFFFE8001),
        mem32(0x20, 0x7FFF0001), mem32(0x24, 0x00027FFE),
    ]
    saturating = [
        mem32(0x00, 0x7F7F8080), mem32(0x04, 0xFF80FF7F),
        mem32(0x20, 0x01010101), mem32(0x24, 0x7F7F8080),
    ]
    wrap = [
        mem32(0x00, 0x80008000), mem32(0x04, 0x80008000),
        mem32(0x20, 0x80008000), mem32(0x24, 0x80008000),
    ]
    bodies = [
        ("paddsb", ["paddsb  {p}+0x20, %mm0"]),
        ("paddsw", ["paddsw  {p}+0x20, %mm0"]),
        ("paddusb", ["paddusb {p}+0x20, %mm0"]),
        ("paddusw", ["paddusw {p}+0x20, %mm0"]),
        ("psubsb", ["psubsb  {p}+0x20, %mm0"]),
        ("psubsw", ["psubsw  {p}+0x20, %mm0"]),
        ("psubusb", ["psubusb {p}+0x20, %mm0"]),
        ("psubusw", ["psubusw {p}+0x20, %mm0"]),
        ("pmullw", ["pmullw  {p}+0x20, %mm0"]),
        ("pmulhw", ["pmulhw  {p}+0x20, %mm0"]),
        ("pmulhuw", ["pmulhuw {p}+0x20, %mm0"]),
        ("pmaddwd", ["pmaddwd {p}+0x20, %mm0"]),
        ("pavgb", ["pavgb   {p}+0x20, %mm0"]),
        ("pavgw", ["pavgw   {p}+0x20, %mm0"]),
        ("packsswb", ["packsswb {p}+0x20, %mm0"]),
        ("packuswb", ["packuswb {p}+0x20, %mm0"]),
        ("packssdw", ["packssdw {p}+0x20, %mm0"]),
        ("pcmpgtb", ["pcmpgtb {p}+0x20, %mm0"]),
        ("pcmpgtw", ["pcmpgtw {p}+0x20, %mm0"]),
        ("pcmpeqb", ["pcmpeqb {p}+0x20, %mm0"]),
        ("psadbw", ["psadbw  {p}+0x20, %mm0"]),
        ("punpcklbw", ["punpcklbw {p}+0x20, %mm0"]),
        ("punpckhbw", ["punpckhbw {p}+0x20, %mm0"]),
        ("pshufw", ["pshufw  $0x1B, %mm0, %mm1"]),
    ]
    cases = []
    for label, setup in (("neg", negative), ("sat", saturating), ("wrap", wrap)):
        for mnemonic, body in bodies:
            store = "movq    %mm0, {p}+0x40"
            if "pshufw" in mnemonic:
                store = "movq    %mm1, {p}+0x40"
            cases.append(Case(f"mmx_{label}_{mnemonic}", setup,
                              ["movq    {p}+0x0, %mm0"] + body + [store]))
    return cases


def shift_edge_cases():
    """Shift counts at and past the element width.

    PSRAW/PSRAD saturate their count (per-element sign fill), PSLLW/PSLLD/PSRLW/
    PSRLD become zero, and the immediate forms only encode one count byte.
    """
    setup = [
        mem32(0x00, 0x8000FFFF), mem32(0x04, 0x7FFF0001),
        mem32(0x20, 0x00000010), mem32(0x24, 0x00000000),
    ]
    cases = []
    for mnemonic in ["psraw", "psrad", "psrlw", "psrld", "psllw", "pslld"]:
        for count in [16, 17, 31, 32, 33, 63, 64, 255]:
            cases.append(Case(f"mmx_{mnemonic}_imm{count}", setup,
                              ["movq    {p}+0x0, %mm0", f"{mnemonic} ${count}, %mm0",
                               "movq    %mm0, {p}+0x40"]))
    for mnemonic in ["psraw", "psrad", "psrlw", "psrld", "psllw", "pslld"]:
        for count in [17, 32, 255]:
            cases.append(Case(f"mmx_{mnemonic}_reg{count}", setup,
                              ["movq    {p}+0x0, %mm0", "movq    {p}+0x20, %mm1",
                               f"{mnemonic} %mm1, %mm0", "movq    %mm0, {p}+0x40"]))
    for count in [63, 64, 65, 255]:
        cases.append(Case(f"mmx_psllq_imm{count}", setup,
                          ["movq    {p}+0x0, %mm0", f"psllq ${count}, %mm0",
                           "movq    %mm0, {p}+0x40"]))
        cases.append(Case(f"mmx_psrlq_imm{count}", setup,
                          ["movq    {p}+0x0, %mm0", f"psrlq ${count}, %mm0",
                           "movq    %mm0, {p}+0x40"]))
    # Seed the register-count variants with a count that byte-shift code masks.
    cases.append(Case("mmx_psllq_reg_high",
                      setup + [mem32(0x30, 0x00000041)],
                      ["movq    {p}+0x0, %mm0", "movd    {p}+0x30, %mm1", "psllq %mm1, %mm0",
                       "movq    %mm0, {p}+0x40"]))
    return cases


def mmx_transfer_cases():
    return [
        Case("mmx_movd_load", [mem32(0x00, 0x89ABCDEF)], ["movd    {p}+0x0, %mm3",
                                                        "movd    %mm3, {p}+0x10"]),
        Case("mmx_movd_store_gpr", [], ["movd    %mm5, %eax"], regs={"eax": 0}),
        Case("mmx_movq_reg", [], ["movq    %mm2, %mm6"],
             regs={"eax": 0}),
        Case("mmx_movq_mem_roundtrip", [mem32(0x00, 0x01234567), mem32(0x04, 0x89ABCDEF)],
             ["movq    {p}+0x0, %mm1", "movq    %mm1, {p}+0x10"]),
        Case("mmx_pextrw", [], ["pextrw  $3, %mm0, %eax"]),
        Case("mmx_pinsrw_high", [], ["pinsrw  $3, %eax, %mm0", "movq %mm0, {p}+0x40"],
             regs={"eax": 0x0000BEEF}),
        Case("mmx_pmovmskb", [], ["pmovmskb %mm0, %eax"]),
        Case("mmx_emms", [mem32(0x00, 0x55667788), mem32(0x04, 0x11223344)],
             ["movq    {p}+0x0, %mm0", "emms", "fld1", "fstps   {p}+0x10"]),
        Case("mmx_maskmovq", [], ["maskmovq %mm1, %mm0"]),
        Case("mmx_movntq", [], ["movntq  %mm0, {p}+0x40"]),
    ]


def x87_transcendental_cases():
    cases = []
    for mnemonic in ["fsin", "fcos", "fsqrt", "frndint", "f2xm1", "fxtract"]:
        cases.append(Case(f"x87_{mnemonic}", [], [mnemonic, "fstps   {p}+0x0"]))
    cases.append(Case("x87_fsincos", [], ["fsincos", "fstps   {p}+0x0", "fstps   {p}+0x4"]))
    cases.append(Case("x87_fptan", [], ["fptan", "fstps   {p}+0x0", "fstps   {p}+0x4"]))
    cases.append(Case("x87_fpatan", [], ["fld1", "fld1", "fpatan", "fstps   {p}+0x0"]))
    cases.append(Case("x87_fpatan_quadrant", [mem32(0x00, 0xBF800000)],
                      ["flds    {p}+0x0", "fld1", "fpatan", "fstps   {p}+0x4"]))
    cases.append(Case("x87_fyl2x", [], ["fld1", "fld1", "fyl2x", "fstps   {p}+0x0"]))
    cases.append(Case("x87_fyl2xp1", [], ["fld1", "fld1", "fyl2xp1", "fstps   {p}+0x0"]))
    cases.append(Case("x87_fprem", [mem32(0x00, 0x40490FDB), mem32(0x04, 0x3F800000)],
                      ["flds    {p}+0x0", "flds    {p}+0x4", "fprem", "fstps   {p}+0x8",
                       "fstps   {p}+0xC"]))
    cases.append(Case("x87_fprem1", [mem32(0x00, 0x40490FDB), mem32(0x04, 0x3F800000)],
                      ["flds    {p}+0x0", "flds    {p}+0x4", "fprem1", "fstps   {p}+0x8",
                       "fstps   {p}+0xC"]))
    cases.append(Case("x87_fscale_neg", [], ["fld1", "fld1", "fchs", "fscale",
                                             "fstps   {p}+0x0"]))
    cases.append(Case("x87_fscale_wide", [], ["fld1", "fld1", "fscale", "fstps   {p}+0x0"]))
    cases.append(Case("x87_fincstp_fdecstp", [], ["fld1", "fld1", "fincstp", "fdecstp",
                                                 "fstps   {p}+0x0"]))
    cases.append(Case("x87_ffree", [], ["fld1", "ffree %st(1)", "fstps   {p}+0x0"]))
    cases.append(Case("x87_fcomi", [], ["fldz", "fld1", "fcomi %st(1)", "fstps {p}+0x0"]))
    cases.append(Case("x87_fucomi", [], ["fldz", "fld1", "fucomi %st(1)", "fstps {p}+0x0"]))
    cases.append(Case("x87_fucomip", [], ["fldz", "fld1", "fucomip %st(1)", "fstps {p}+0x0"]))
    cases.append(Case("x87_fcmov", [], ["fldz", "fld1", "fcomi %st(1)",
                                        "fldz", "fcmovb %st(1), %st(0)", "fstps {p}+0x0"]))
    cases.append(Case("x87_fnstsw_m16", [], ["fld1", "fnstsw  {p}+0x0"]))
    cases.append(Case("x87_fldcw_stcw", [mem16(0x00, 0x0E7F)],
                      ["fldcw   {p}+0x0", "fnstcw  {p}+0x2"]))
    cases.append(Case("x87_fldcw_round_up", [mem16(0x00, 0x0F7F), mem32(0x04, 0x40000000)],
                      ["fldcw   {p}+0x0", "flds    {p}+0x4", "frndint", "fistpl {p}+0x8"]))
    cases.append(Case("x87_fistp_m64", ["movl    $0x40000000, {p}+0x0",
                                       "movl    $0x00000000, {p}+0x4"],
                      ["flds    {p}+0x0", "fistpll {p}+0x8"]))
    cases.append(Case("x87_fistp_m64_neg", ["movl    $0xC0490FDB, {p}+0x0",
                                            "movl    $0x00000000, {p}+0x4"],
                      ["flds    {p}+0x0", "fistpll {p}+0x8"]))
    cases.append(Case("x87_fistp_m16", ["movw    $0x0000, {p}+0x0"],
                      ["filds   {p}+0x0", "fistps  {p}+0x2"]))
    cases.append(Case("x87_fist_m64", ["movl    $0x40400000, {p}+0x0",
                                      "movl    $0x00000000, {p}+0x4"],
                      ["flds    {p}+0x0", "fistpll {p}+0x8"]))
    cases.append(Case("x87_fild_m64", ["movl    $0x00000064, {p}+0x0",
                                      "movl    $0xFFFFFFFE, {p}+0x4"],
                      ["fildll  {p}+0x0", "fistpll {p}+0x10"]))
    cases.append(Case("x87_fbld", ["movl    $0x00000000, {p}+0x0",
                                   "movl    $0x00000000, {p}+0x4",
                                   "movw    $0x0B33, {p}+0x8"],
                      ["fbld    {p}+0x0", "fstps   {p}+0x10"]))
    cases.append(Case("x87_fbstp", ["movl    $0x00000000, {p}+0x0",
                                    "movl    $0x00000000, {p}+0x4",
                                    "movw    $0x0B33, {p}+0x8"],
                      ["fbld    {p}+0x0", "fbstp   {p}+0x10"]))
    cases.append(Case("x87_fld_m80", ["movl    $0x00000000, {p}+0x0",
                                      "movl    $0xC0000000, {p}+0x4",
                                      "movw    $0x3FFF, {p}+0x8"],
                      ["fldt    {p}+0x0", "fstpt   {p}+0x10"]))
    cases.append(Case("x87_fclex", [],
                      ["fld1", "fldz", "fdivp", "fnclex", "fnstsw  {p}+0x0"]))
    cases.append(Case("x87_nan_compare", [mem32(0x00, 0x7FC00000)],
                      ["flds    {p}+0x0", "fld1", "fucompp"]))
    cases.append(Case("x87_nan_fcomi", [mem32(0x00, 0x7FC00000)],
                      ["flds    {p}+0x0", "fld1", "fucomi %st(1)", "fstps {p}+0x4",
                       "fstps {p}+0x8"]))
    cases.append(Case("x87_neg_zero_compare", [mem32(0x00, 0x80000000)],
                      ["fldz", "flds    {p}+0x0", "fucompp"]))
    cases.append(Case("x87_nan_single_roundtrip", [mem32(0x00, 0x7FC00000)],
                      ["flds    {p}+0x0", "fstps   {p}+0x4"]))
    cases.append(Case("x87_nan_negative_roundtrip", [mem32(0x00, 0xFFC00000)],
                      ["flds    {p}+0x0", "fstps   {p}+0x4"]))
    cases.append(Case("x87_nan_payload_roundtrip", [mem32(0x00, 0x7FC12345)],
                      ["flds    {p}+0x0", "fstps   {p}+0x4"]))
    cases.append(Case("x87_nan_double_roundtrip",
                      ["movl    $0x7FF80000, {p}+0x0", "movl    $0x00000000, {p}+0x4"],
                      ["fldl    {p}+0x0", "fstpl   {p}+0x8"]))
    cases.append(Case("x87_snan_single_roundtrip", [mem32(0x00, 0x7F800001)],
                      ["flds    {p}+0x0", "fstps   {p}+0x4"]))
    cases.append(Case("x87_inf_roundtrip", [mem32(0x00, 0xFF800000)],
                      ["flds    {p}+0x0", "fstps   {p}+0x4"]))
    return cases


def integer_edge_cases():
    # Every case keeps its stack and data inside its own page (`{p}`); the
    # test harness reuses one memory image across cases, so a case must never
    # read an absolute low address another case may have written.
    return [
        case("enter_leave", ["enterw  $0x10, $0", "leave"]),
        case("enter_level3", ["enterw  $0x8, $3", "leave"],
             setup=["leal    {p}+0x1E00, %ebp"]),
        case("arpl", ["arpl    %bx, %ax"], regs={"eax": 0x00000005, "ebx": 0x0000000F}),
        case("xlat", ["xlat"], regs={"eax": 0x00000010},
             setup=["leal    {p}+0x0, %ebx", mem8(0x10, 0x5A)]),
        case("xlat_address16_wrap", ["xlatb"],
             regs={"eax": 0x00000020, "ebx": 0x0000FFF0},
             setup=["leal    {p}+0x0, %ebx", mem8(0x10, 0x5A)]),
        case("xlat_fs", ["xlatb   %fs:(%ebx)"], regs={"eax": 0x00000010},
             setup=["leal    {p}+0x0, %ebx", mem8(0x10, 0x77)]),
        case("loop_taken", ["movl    $0, %edx", "1:", "incl %edx", "loop 1b"],
             regs={"ecx": 3}),
        case("loope_clear", ["movl    $0x11111111, %eax", "loope 1f",
                             "movl    $0x22222222, %edx", "1:", "nop"],
             regs={"ecx": 1}, eflags=0x202),
        case("loopne_taken", ["movl    $0x11111111, %eax", "loopne 1f",
                              "movl    $0x22222222, %edx", "1:", "nop"],
             regs={"ecx": 2}, eflags=0x202),
        case("movsx_to_16", ["movsbw  %al, %bx"], regs={"eax": 0x00000080}),
        case("movzx_to_16", ["movzbw  %al, %bx"], regs={"eax": 0x000000FF}),
        case("movsx_high8", ["movsbl  %ah, %ebx"], regs={"eax": 0x80000000}),
        case("bt_mem_offset", ["btl     %eax, {p}+0x0"], regs={"eax": 33},
             flags_mask=0x001,
             setup=[mem32(0x00, 0x00000000), mem32(0x04, 0x00000001)]),
        case("bts_mem_offset", ["btsl    %eax, {p}+0x0"], regs={"eax": 40},
             flags_mask=0x001,
             setup=[mem32(0x00, 0x00000000), mem32(0x04, 0x00000000)]),
        case("shld_count_zero", ["shldl   $0, %ebx, %eax"],
             regs={"eax": 0x12345678, "ebx": 0x9ABCDEF0}),
        case("shld_count_32", ["shldl   $32, %ebx, %eax"],
             regs={"eax": 0x12345678, "ebx": 0x9ABCDEF0}),
        case("shrd_count_33", ["shrdl   $33, %ebx, %eax"],
             regs={"eax": 0x12345678, "ebx": 0x9ABCDEF0}, flags_mask=0x0C5),
        case("rcr_8", ["rcrb    $1, %al"], regs={"eax": 0x00000001}),
        case("rcr_16_cf", ["rcrw    $1, %ax"], regs={"eax": 0x00000001}, eflags=0x203),
        case("rcl_32_cf", ["rcll    $1, %eax"], regs={"eax": 0x80000000}, eflags=0x203),
        case("sar_31_neg", ["sarl    $31, %eax"], regs={"eax": 0x80000001}),
        case("sar_8_31", ["sarb    $31, %al"], regs={"eax": 0x00000081}, flags_mask=0x0C4),
        case("neg_zero", ["negl    %eax"], regs={"eax": 0}),
        case("neg_min", ["negl    %eax"], regs={"eax": 0x80000000}),
        case("inc_high8", ["incb    %ah"], regs={"eax": 0x0000FF00}),
        case("dec_high8", ["decb    %bh"], regs={"eax": 0x00000000, "ebx": 0x00008000}),
        case("xchg_high8", ["xchgb   %ah, %bl"],
             regs={"eax": 0x12345678, "ebx": 0xAAAAAAEF}),
        case("xadd_16", ["xaddw   %bx, %ax"], regs={"eax": 0xFFFF0001, "ebx": 0x0002FFFF}),
        Case("cmpxchg8b_match", [mem32(0x00, 0x11111111), mem32(0x04, 0x22222222)],
             ["cmpxchg8b {p}+0x0"],
             regs={"eax": 0x11111111, "edx": 0x22222222,
                   "ebx": 0x33333333, "ecx": 0x44444444}),
        Case("cmpxchg8b_miss", [mem32(0x00, 0x11111111), mem32(0x04, 0x22222222)],
             ["cmpxchg8b {p}+0x0"],
             regs={"eax": 0x55555555, "edx": 0x66666666,
                   "ebx": 0x33333333, "ecx": 0x44444444}),
        case("cmpxchg_8", ["cmpxchgb %bl, %cl"],
             regs={"eax": 0x11111111, "ebx": 0x22222222, "ecx": 0x33333311}),
        case("imul_8", ["imulb   %bl"], regs={"eax": 0x00000080, "ebx": 0x00000002},
             flags_mask=0x801),
        case("imul_2op_16", ["imulw   %bx, %ax"], regs={"eax": 0x00008000, "ebx": 0x00000003},
             flags_mask=0x801),
        case("idiv_16_neg_result", ["idivw   %bx"],
             regs={"eax": 0x00070000, "edx": 0xFFFFFFFF, "ebx": 0x00000002}, flags_mask=0),
        case("push_16", ["pushw   %ax", "popl    %ebx"], regs={"eax": 0x12345678}),
        case("test_16_imm", ["testw   $0x00FF, %ax"], regs={"eax": 0x00001234}),
        case("or_high8", ["orb     $0x0F, %ah"], regs={"eax": 0x00001200}),
        case("setcc_high8", ["setne   %ah"], regs={"eax": 0x00000000}, eflags=0x202),
        case("mov_imm_to_high8", ["movb    $0x7F, %bh"], regs={"ebx": 0xAAAAAAAA}),
    ]


def addressing_cases():
    """16-bit addressing forms and segment overrides, all page-relative."""
    return [
        case("addr16_lea_si", ["leal    (%si), %eax"],
             setup=["leal    {p}+0x1010, %esi"]),
        case("addr16_lea_di_disp", ["leal    0x10(%di), %eax"],
             setup=["leal    {p}+0x1010, %edi"]),
        case("addr16_lea_bp_disp", ["leal    (%bp), %eax"],
             setup=["leal    {p}+0x1010, %ebp"]),
        case("addr16_lea_bx_si", ["leal    (%bx,%si), %eax"],
             setup=["leal    {p}+0x1000, %ebx", "leal    {p}+0x1000, %esi"]),
        case("addr16_mov_bp", ["movl    (%bp), %eax"],
             setup=["leal    {p}+0x1000, %ebp", mem32(0x1000, 0xCAFEBABE)]),
        case("addr16_store", ["movl    %eax, 0x10(%bx)"],
             regs={"eax": 0xDEADBEEF},
             setup=["leal    {p}+0x1000, %ebx"]),
        case("seg_fs_override", ["movl    %fs:(%ebx), %eax"],
             setup=["leal    {p}+0x1000, %ebx", mem32(0x1000, 0x12345678)]),
        case("seg_gs_override_store", ["movl    %eax, %gs:(%ebx)"],
             regs={"eax": 0x87654321},
             setup=["leal    {p}+0x1000, %ebx"]),
        case("seg_es_string", ["rep movsb"], regs={"ecx": 2},
             setup=[mem8(0x40, 0x11), mem8(0x41, 0x22)]),
    ]


def string_edge_cases():
    return [
        Case("rep_movsd", [mem32(0x40, 0x11223344), mem32(0x44, 0x55667788)],
             ["rep movsl"], regs={"ecx": 2}),
        Case("rep_movsb_zero", [], ["rep movsb"], regs={"ecx": 0}),
        Case("rep_stosb_zero", [], ["rep stosb"], regs={"eax": 0x5A, "ecx": 0}),
        Case("repne_scasb_found", [mem8(0x80, 0x42), mem8(0x81, 0x43)],
             ["repne scasb"], regs={"eax": 0x43, "ecx": 4}),
        Case("repne_scasb_missing", [mem8(0x80, 0x42), mem8(0x81, 0x42)],
             ["repne scasb"], regs={"eax": 0x43, "ecx": 2}),
        Case("repe_cmpsb_equal", [mem8(0x40, 0x10), mem8(0x41, 0x11),
                                  mem8(0x80, 0x10), mem8(0x81, 0x11)],
             ["repe cmpsb"], regs={"ecx": 2}),
        Case("rep_lodsw", [mem16(0x40, 0x1234), mem16(0x42, 0x5678)],
             ["rep lodsw"], regs={"ecx": 2}),
        Case("rep_movsb_df1", [mem8(0x40, 0x11), mem8(0x41, 0x22)],
             ["std", "rep movsb", "cld"], regs={"ecx": 2}),
    ]


def x87_cases():
    cases = []
    cases.append(Case("fld_m32", [mem32(0x00, 0x40490FDB)], ["flds    {p}+0x0", "fstps   {p}+0x8"]))
    cases.append(Case("fld_m64", ["movl    $0x400921FB, {p}+0x0", "movl $0x54442D18, {p}+0x4"],
                      ["fldl    {p}+0x0", "fstpl   {p}+0x8"]))
    cases.append(Case("fild_m32", [mem32(0x00, 0xFFFFFC18)], ["fildl   {p}+0x0", "fistpl  {p}+0x8"]))
    cases.append(Case("fild_m16", [mem16(0x00, 0x8000)], ["filds   {p}+0x0", "fistps  {p}+0x8"]))
    for mnemonic in ["fadd", "fsub", "fsubr", "fmul", "fdiv", "fdivr"]:
        cases.append(Case(f"{mnemonic}_m32", [mem32(0x00, 0x40400000)],
                          ["flds    {p}+0x0", f"{mnemonic}s   {{p}}+0x0", "fstps   {p}+0x8"]))
        cases.append(Case(f"{mnemonic}_st", [],
                          ["fldz", "fld1", f"{mnemonic} %st(1), %st(0)", "fstps   {p}+0x0"]))
    cases.append(Case("faddp", [], ["fld1", "fld1", "faddp", "fstps   {p}+0x0"]))
    cases.append(Case("fsubp", [], ["fld1", "fld1", "fsubp", "fstps   {p}+0x0"]))
    cases.append(Case("fmulp", [], ["fld1", "fld1", "fmulp", "fstps   {p}+0x0"]))
    cases.append(Case("fdivp", [], ["fld1", "fld1", "fdivp", "fstps   {p}+0x0"]))
    cases.append(case("fcom", ["fldz", "fld1", "fcom %st(1)"], eflags=0x202))
    cases.append(case("fucom", ["fldz", "fld1", "fucom %st(1)"]))
    cases.append(case("fcompp", ["fld1", "fld1", "fcompp"]))
    cases.append(case("fucompp", ["fldz", "fld1", "fucompp"]))
    cases.append(Case("fcom_m32", [mem32(0x00, 0x3F800000)], ["fld1", "fcoms {p}+0x0"]))
    cases.append(case("fnstsw_ax", ["fld1", "fnstsw %ax"], eflags=0x202))
    cases.append(Case("fnstcw_m", [], ["fld1", "fnstcw  {p}+0x0"]))
    cases.append(Case("fldcw_round", [mem16(0x00, 0x0F7F)], ["fldcw   {p}+0x0", "fnstcw  {p}+0x2"]))
    cases.append(Case("fsqrt", [mem32(0x00, 0x40800000)],
                      ["flds    {p}+0x0", "fsqrt", "fstps   {p}+0x4"]))
    cases.append(Case("fabs", [], ["fld1", "fchs", "fabs", "fstps   {p}+0x0"]))
    cases.append(Case("fchs", [], ["fld1", "fchs", "fstps   {p}+0x0"]))
    cases.append(Case("fxch", [], ["fld1", "fldz", "fxch", "fstps   {p}+0x0", "fstps   {p}+0x4"]))
    cases.append(Case("frndint", [], ["fld1", "frndint", "fstps   {p}+0x0"]))
    cases.append(Case("fprem", [], ["fld1", "fld1", "fprem", "fstps   {p}+0x0"]))
    cases.append(Case("fscale", [], ["fld1", "fld1", "fscale", "fstps   {p}+0x0"]))
    for mnemonic in ["fiadd", "fimul", "fisub", "fisubr", "fidiv", "fidivr"]:
        cases.append(Case(f"{mnemonic}_m16", [mem16(0x00, 0x0007)],
                          ["fld1", f"{mnemonic}s  {{p}}+0x0", "fstps   {p}+0x8"]))
    return cases


def mmx_cases():
    setup = [
        mem32(0x00, 0x80000001), mem32(0x04, 0x7FFFFFFF),
        mem32(0x08, 0x00010002), mem32(0x0C, 0x00030004),
    ]
    bodies = [
        ("mmx_movq_load", ["movq    {p}+0x0, %mm0", "movq    %mm0, {p}+0x10"]),
        ("mmx_paddd", ["movq    {p}+0x0, %mm0", "paddd   {p}+0x8, %mm0", "movq %mm0, {p}+0x10"]),
        ("mmx_psubd", ["movq    {p}+0x0, %mm1", "psubd   {p}+0x8, %mm1", "movq %mm1, {p}+0x10"]),
        ("mmx_pmullw", ["movq    {p}+0x0, %mm2", "pmullw  {p}+0x8, %mm2", "movq %mm2, {p}+0x10"]),
        ("mmx_pmulhw", ["movq    {p}+0x0, %mm3", "pmulhw  {p}+0x8, %mm3", "movq %mm3, {p}+0x10"]),
        ("mmx_pand", ["movq    {p}+0x0, %mm4", "pand    {p}+0x8, %mm4", "movq %mm4, {p}+0x10"]),
        ("mmx_por", ["movq    {p}+0x0, %mm5", "por     {p}+0x8, %mm5", "movq %mm5, {p}+0x10"]),
        ("mmx_pxor", ["movq    {p}+0x0, %mm6", "pxor    {p}+0x8, %mm6", "movq %mm6, {p}+0x10"]),
        ("mmx_psllq", ["movq    {p}+0x0, %mm7", "psllq   $5, %mm7", "movq %mm7, {p}+0x10"]),
        ("mmx_psrlq", ["movq    {p}+0x0, %mm0", "psrlq   $3, %mm0", "movq %mm0, {p}+0x10"]),
        ("mmx_punpcklbw", ["movq    {p}+0x0, %mm0", "punpcklbw {p}+0x8, %mm0", "movq %mm0, {p}+0x10"]),
        ("mmx_packssdw", ["movq    {p}+0x0, %mm0", "packssdw {p}+0x8, %mm0", "movq %mm0, {p}+0x10"]),
        ("mmx_movd", ["movd    {p}+0x0, %mm0", "movd    %mm0, {p}+0x4"]),
    ]
    return [Case(name, setup, body) for name, body in bodies]


def sse_cases():
    setup32 = [
        mem32(0x00, 0x3F800000),  # 1.0f
        mem32(0x04, 0x40000000),  # 2.0f
        mem32(0x08, 0x40400000),  # 3.0f
        mem32(0x0C, 0x40800000),  # 4.0f
    ]
    cases = []
    for mnemonic in ["addss", "subss", "mulss", "divss", "minss", "maxss"]:
        cases.append(Case(f"sse_{mnemonic}", setup32,
                          ["movss   {p}+0x0, %xmm0", f"{mnemonic} {{p}}+0x4, %xmm0",
                           "movss   %xmm0, {p}+0x10"]))
    cases.append(Case("sse_sqrtss", setup32,
                      ["movss   {p}+0x8, %xmm0", "sqrtss  %xmm0, %xmm1", "movss %xmm1, {p}+0x10"]))
    cases.append(Case("sse_rcpss", setup32,
                      ["movss   {p}+0x8, %xmm0", "rcpss   %xmm0, %xmm1", "movss %xmm1, {p}+0x10"]))
    cases.append(Case("sse_rsqrtss", setup32,
                      ["movss   {p}+0x8, %xmm0", "rsqrtss %xmm0, %xmm1", "movss %xmm1, {p}+0x10"]))
    cases.append(Case("sse_cvtsi2ss", [],
                      ["movl    $1000, %eax", "cvtsi2ss %eax, %xmm0", "movss %xmm0, {p}+0x10"]))
    cases.append(Case("sse_cvttss2si", setup32,
                      ["movss   {p}+0x8, %xmm0", "cvttss2si %xmm0, %eax"]))
    cases.append(Case("sse_cvtss2si", setup32,
                      ["movss   {p}+0x8, %xmm0", "cvtss2si %xmm0, %eax"]))
    cases.append(Case("sse_movups", setup32,
                      ["movups  {p}+0x0, %xmm0", "movups  %xmm0, {p}+0x10"]))
    cases.append(Case("sse_movaps_roundtrip", setup32,
                      ["movaps  {p}+0x0, %xmm0", "movaps  %xmm0, {p}+0x10"]))
    cases.append(Case("sse_addps", setup32,
                      ["movaps  {p}+0x0, %xmm0", "addps   {p}+0x0, %xmm0", "movaps %xmm0, {p}+0x10"]))
    cases.append(Case("sse_mulps", setup32,
                      ["movaps  {p}+0x0, %xmm0", "mulps   {p}+0x0, %xmm0", "movaps %xmm0, {p}+0x10"]))
    cases.append(Case("sse_andps", setup32,
                      ["movaps  {p}+0x0, %xmm0", "andps   {p}+0x0, %xmm0", "movaps %xmm0, {p}+0x10"]))
    cases.append(Case("sse_xorps", setup32,
                      ["movaps  {p}+0x0, %xmm0", "xorps   %xmm1, %xmm1", "xorps   %xmm1, %xmm0",
                       "movaps  %xmm0, {p}+0x10"]))
    cases.append(Case("sse_shufps", setup32,
                      ["movaps  {p}+0x0, %xmm0", "shufps  $0x1B, %xmm0, %xmm0",
                       "movaps  %xmm0, {p}+0x10"]))
    cases.append(Case("sse_comiss", setup32,
                      ["movss   {p}+0x0, %xmm0", "comiss  {p}+0x4, %xmm0"]))
    cases.append(Case("sse_ucomiss", setup32,
                      ["movss   {p}+0x0, %xmm0", "ucomiss {p}+0x0, %xmm0"]))
    cases.append(Case("sse_cmpps", setup32,
                      ["movaps  {p}+0x0, %xmm0", "cmpps   $0, {p}+0x4, %xmm0",
                       "movaps  %xmm0, {p}+0x10"]))
    cases.append(Case("sse_movmskps", setup32,
                      ["movaps  {p}+0x0, %xmm0", "movmskps %xmm0, %eax"]))
    return cases


def x87_arithmetic_edge_cases():
    return [
        Case("x87_inf_times_zero", [mem32(0x00, 0x7F800000)],
             ["flds    {p}+0x0", "fldz", "fmulp", "fstps   {p}+0x4"]),
        Case("x87_inf_minus_inf", [mem32(0x00, 0x7F800000)],
             ["flds    {p}+0x0", "flds    {p}+0x0", "fsubp", "fstps   {p}+0x4"]),
        Case("x87_denormal_load", [mem32(0x00, 0x00000001)],
             ["flds    {p}+0x0", "fstps   {p}+0x4"]),
        Case("x87_denormal_arith", [mem32(0x00, 0x00000001), mem32(0x04, 0x3F800000)],
             ["flds    {p}+0x0", "flds    {p}+0x4", "faddp", "fstps   {p}+0x8"]),
        Case("x87_precision_single", [mem16(0x00, 0x007F), mem32(0x04, 0x3F800000),
                                      mem32(0x08, 0x322BCC77)],
             ["fldcw   {p}+0x0", "flds    {p}+0x4", "flds    {p}+0x8", "faddp",
              "fstps   {p}+0x10"]),
        Case("x87_round_up_int", [mem16(0x00, 0x0B7F), mem32(0x04, 0x40200000)],
             ["fldcw   {p}+0x0", "flds    {p}+0x4", "fistpl {p}+0x8"]),
        Case("x87_round_down_int", [mem16(0x00, 0x077F), mem32(0x04, 0x40200000)],
             ["fldcw   {p}+0x0", "flds    {p}+0x4", "fistpl {p}+0x8"]),
        Case("x87_fxam_zero", [], ["fldz", "fxam", "fnstsw  {p}+0x0"]),
        Case("x87_fxam_inf", [mem32(0x00, 0xFF800000)],
             ["flds    {p}+0x0", "fxam", "fnstsw  {p}+0x4"]),
        Case("x87_fxam_nan", [mem32(0x00, 0x7FC00000)],
             ["flds    {p}+0x0", "fxam", "fnstsw  {p}+0x4"]),
        Case("x87_fxam_denormal", [mem32(0x00, 0x00000001)],
             ["flds    {p}+0x0", "fxam", "fnstsw  {p}+0x4"]),
        Case("x87_fxam_empty", [], ["fxam", "fnstsw  {p}+0x0"]),
        Case("x87_fabs_nan", [mem32(0x00, 0xFFC00000)],
             ["flds    {p}+0x0", "fabs", "fstps   {p}+0x4"]),
        Case("x87_fchs_nan", [mem32(0x00, 0x7FC00000)],
             ["flds    {p}+0x0", "fchs", "fstps   {p}+0x4"]),
        Case("x87_fadd_neg_zero", [mem32(0x00, 0x80000000), mem32(0x04, 0x80000000)],
             ["flds    {p}+0x0", "flds    {p}+0x4", "faddp", "fstps   {p}+0x8"]),
        # FSQRT of a negative value is deliberately not a corpus case: the
        # reference model reports only C2, while the SDM (and XGrid) also raise
        # the invalid exception (see the README caveats).
        Case("x87_fprem_quotient", [mem32(0x00, 0x40C00000), mem32(0x04, 0x3F000000)],
             ["flds    {p}+0x0", "flds    {p}+0x4", "fprem", "fnstsw  {p}+0x8",
              "fstps   {p}+0xC"]),
    ]


def sse_edge_cases():
    data = [mem32(0x00, 0x00000000), mem32(0x04, 0x80000000),
            mem32(0x08, 0x3F800000), mem32(0x0C, 0xBF800000)]
    return [
        Case("sse_cvtsi2ss_large", [], ["movl    $0x80000000, %eax", "cvtsi2ss %eax, %xmm0",
                                        "movss   %xmm0, {p}+0x0"]),
        Case("sse_cvttss2si_overflow", [mem32(0x00, 0x60AD78EC)],
             ["movss   {p}+0x0, %xmm0", "cvttss2si %xmm0, %eax"]),
        Case("sse_cvtss2si_negative", [mem32(0x00, 0xBF000000)],
             ["movss   {p}+0x0, %xmm0", "cvtss2si %xmm0, %eax"]),
        Case("sse_sqrtss_negative", [mem32(0x00, 0xBF800000)],
             ["movss   {p}+0x0, %xmm0", "sqrtss  %xmm0, %xmm1", "movss %xmm1, {p}+0x4"]),
        Case("sse_divss_by_zero", [mem32(0x00, 0x3F800000)],
             ["movss   {p}+0x0, %xmm0", "xorps   %xmm1, %xmm1", "divss %xmm1, %xmm0",
              "movss   %xmm0, {p}+0x4"]),
        Case("sse_rcpss_zero", [], ["xorps   %xmm0, %xmm0", "rcpss %xmm0, %xmm1",
                                    "movss %xmm1, {p}+0x0"]),
        Case("sse_minps_zeros", data,
             ["movups  {p}+0x0, %xmm0", "minps   {p}+0x4, %xmm0", "movups  %xmm0, {p}+0x10"]),
        Case("sse_maxps_zeros", data,
             ["movups  {p}+0x0, %xmm0", "maxps   {p}+0x4, %xmm0", "movups  %xmm0, {p}+0x10"]),
        Case("sse_comiss_neg_zero", [mem32(0x00, 0x00000000), mem32(0x04, 0x80000000)],
             ["movss   {p}+0x0, %xmm0", "comiss  {p}+0x4, %xmm0"], flags_mask=0x045),
        Case("sse_ucomiss_lt", [mem32(0x00, 0x3F800000), mem32(0x04, 0x40000000)],
             ["movss   {p}+0x0, %xmm0", "ucomiss {p}+0x4, %xmm0"], flags_mask=0x045),
        Case("sse_movmskps_neg", data,
             ["movups  {p}+0x0, %xmm0", "movmskps %xmm0, %eax"]),
        Case("sse_shufps_zero", data,
             ["movups  {p}+0x0, %xmm0", "movups  {p}+0x0, %xmm1", "shufps $0x00, %xmm1, %xmm0",
              "movups  %xmm0, {p}+0x10"]),
        Case("sse_mulps_neg", data,
             ["movups  {p}+0x0, %xmm0", "movups  {p}+0x8, %xmm1", "mulps %xmm1, %xmm0",
              "movups  %xmm0, {p}+0x10"]),
        Case("sse_andps_zero", data,
             ["movups  {p}+0x0, %xmm0", "andps   {p}+0x8, %xmm0", "movups  %xmm0, {p}+0x10"]),
    ]


def lock_prefix_cases():
    return [
        Case("lock_xadd", [mem32(0x00, 0x11111111)],
             ["lock xaddl %ebx, {p}+0x0", "movl    {p}+0x0, %eax"], regs={"ebx": 0x22222222}),
        Case("lock_cmpxchg", [mem32(0x00, 0x11111111)],
             ["lock cmpxchgl %ebx, {p}+0x0", "movl    {p}+0x0, %ecx"],
             regs={"eax": 0x11111111, "ebx": 0x22222222}),
        Case("lock_add", [mem32(0x00, 0x11111111)],
             ["lock addl %eax, {p}+0x0", "movl    {p}+0x0, %ecx"], regs={"eax": 1}),
    ]


def stack_edge_cases():
    return [
        case("push_esp_relative", ["pushl   -4(%esp)", "popl    %eax"],
             setup=[mem32(0x17FC, 0xCAFEBABE)]),
        case("push_esp_relative_current", ["pushl   (%esp)", "popl    %eax"]),
        case("pop_esp", ["popl    %esp"], setup=[mem32(0x1800, 0x00001F00)]),
        case("pusha_mem", ["pushal", "popal"]),
        case("addr16_movsw", ["rep movsw"], regs={"ecx": 2},
             setup=["leal    {p}+0x40, %esi", "leal    {p}+0x80, %edi",
                    mem16(0x40, 0x1234), mem16(0x42, 0x5678)]),
        case("or_8_mem_imm", ["orb     $0x0F, (%ebx)"],
             setup=["leal    {p}+0x0, %ebx", mem8(0x00, 0xF0)]),
        case("test_16_mem", ["testw   $0x0001, (%ebx)"],
             setup=["leal    {p}+0x0, %ebx", mem16(0x00, 0x0001)]),
    ]


def build_corpus():
    cases = []
    cases.extend(alu_cases())
    cases.extend(inc_dec_cases())
    cases.extend(shift_cases())
    cases.extend(muldiv_cases())
    cases.extend(move_cases())
    cases.extend(stack_cases())
    cases.extend(bit_cases())
    cases.extend(atomic_cases())
    cases.extend(condition_cases())
    cases.extend(string_cases())
    cases.extend(x87_cases())
    cases.extend(mmx_cases())
    cases.extend(sse_cases())
    # Second wave: sign-sensitive packed patterns, saturating shift counts,
    # x87 transcendentals/environment, integer/addressing/string edges.
    cases.extend(packed_pattern_cases())
    cases.extend(shift_edge_cases())
    cases.extend(mmx_transfer_cases())
    cases.extend(x87_transcendental_cases())
    cases.extend(integer_edge_cases())
    cases.extend(addressing_cases())
    cases.extend(string_edge_cases())
    cases.extend(x87_arithmetic_edge_cases())
    cases.extend(sse_edge_cases())
    cases.extend(lock_prefix_cases())
    cases.extend(stack_edge_cases())
    return cases


if __name__ == "__main__":
    for index, entry in enumerate(build_corpus()):
        print(f"{index:04d} {entry.name}")





