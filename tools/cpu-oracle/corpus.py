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
    return cases


if __name__ == "__main__":
    for index, entry in enumerate(build_corpus()):
        print(f"{index:04d} {entry.name}")





