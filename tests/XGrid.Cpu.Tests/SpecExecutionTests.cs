using System;
using System.IO;
using System.Text;
using XGrid.Core.Devices;
using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// Slice-1/slice-2 specialization tests (§139, §134 style, extended by
/// slices 3-9): every covered
/// shape at 8/16/32 against the interpreter, segment overrides, mem-shape
/// fault precision, #UD termination, IRQ delivery at every member including an
/// IF flip, Step/budget transparency, idle NoteAccess replay, the before-
/// execute verify tripwire and cap discipline with a live cursor.
/// </summary>
public sealed class SpecExecutionTests
{
    private static readonly byte[] Loop = [0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xEB, 0xF8];

    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;

    [Fact]
    public void Mov_Shapes_AtAllSizes_MatchInterpreter()
    {
        RunShapes(
            ("mov r32,r32", [0x89, 0xD8]),
            ("mov r8,r8", [0x88, 0xC8]),
            ("mov r16,r16", [0x66, 0x89, 0xD8]),
            ("mov r32,imm32", [0xB8, 0x78, 0x56, 0x34, 0x12]),
            ("mov r8,imm8", [0xB0, 0x5A]),
            ("mov r16,imm16", [0x66, 0xB8, 0x34, 0x12]),
            ("mov r32,m32", [0x8B, 0x03]),
            ("mov r8,m8", [0x8A, 0x03]),
            ("mov r16,m16", [0x66, 0x8B, 0x03]),
            ("mov m32,r32", [0x89, 0x03]),
            ("mov m8,r8", [0x88, 0x03]),
            ("mov m16,r16", [0x66, 0x89, 0x03]));
    }

    [Fact]
    public void Alu_Shapes_AtAllSizes_MatchInterpreter()
    {
        RunShapes(
            ("add r32,r32", [0x01, 0xD8]),
            ("add r8,r8", [0x00, 0xD8]),
            ("add r16,r16", [0x66, 0x01, 0xD8]),
            ("add r32,imm8", [0x83, 0xC0, 0x05]),
            ("add r8,imm8", [0x04, 0x05]),
            ("add r16,imm8", [0x66, 0x83, 0xC0, 0x05]),
            ("add r32,imm32", [0x05, 0x78, 0x56, 0x34, 0x12]),
            ("sub r32,r32", [0x29, 0xD8]),
            ("sub r8,r8", [0x28, 0xD8]),
            ("sub r16,r16", [0x66, 0x29, 0xD8]),
            ("sub r32,imm8", [0x83, 0xE8, 0x05]),
            ("sub r8,imm8", [0x2C, 0x05]),
            ("sub r16,imm8", [0x66, 0x83, 0xE8, 0x05]),
            ("sub r32,imm32", [0x2D, 0x78, 0x56, 0x34, 0x12]),
            ("and r32,r32", [0x21, 0xD8]),
            ("and r8,r8", [0x20, 0xD8]),
            ("and r16,r16", [0x66, 0x21, 0xD8]),
            ("and r32,imm8", [0x83, 0xE0, 0x05]),
            ("and r8,imm8", [0x24, 0x05]),
            ("and r16,imm8", [0x66, 0x83, 0xE0, 0x05]),
            ("and r32,imm32", [0x25, 0x78, 0x56, 0x34, 0x12]),
            ("or r32,r32", [0x09, 0xD8]),
            ("or r8,r8", [0x08, 0xD8]),
            ("or r16,r16", [0x66, 0x09, 0xD8]),
            ("or r32,imm8", [0x83, 0xC8, 0x05]),
            ("or r8,imm8", [0x0C, 0x05]),
            ("or r16,imm8", [0x66, 0x83, 0xC8, 0x05]),
            ("or r32,imm32", [0x0D, 0x78, 0x56, 0x34, 0x12]),
            ("xor r32,r32", [0x31, 0xD8]),
            ("xor r8,r8", [0x30, 0xD8]),
            ("xor r16,r16", [0x66, 0x31, 0xD8]),
            ("xor r32,imm8", [0x83, 0xF0, 0x05]),
            ("xor r8,imm8", [0x34, 0x05]),
            ("xor r16,imm8", [0x66, 0x83, 0xF0, 0x05]),
            ("xor r32,imm32", [0x35, 0x78, 0x56, 0x34, 0x12]));
    }

    [Fact]
    public void CmpTest_Shapes_AtAllSizes_MatchInterpreter()
    {
        RunShapes(
            ("cmp r32,r32", [0x39, 0xD8]),
            ("cmp r8,r8", [0x38, 0xC8]),
            ("cmp r16,r16", [0x66, 0x39, 0xD8]),
            ("cmp r32,imm8", [0x83, 0xF8, 0x05]),
            ("cmp r8,imm8", [0x3C, 0x05]),
            ("cmp r16,imm8", [0x66, 0x83, 0xF8, 0x05]),
            ("cmp r32,imm32", [0x3D, 0x78, 0x56, 0x34, 0x12]),
            ("test r32,r32", [0x85, 0xC0]),
            ("test r8,r8", [0x84, 0xC8]),
            ("test r16,r16", [0x66, 0x85, 0xC0]),
            ("test r32,imm32", [0xF7, 0xC0, 0x78, 0x56, 0x34, 0x12]),
            ("test r8,imm8", [0xA8, 0x05]),
            ("test r16,imm16", [0x66, 0xF7, 0xC0, 0x34, 0x12]));
    }

    [Fact]
    public void IncDecLeaPushPopNop_Shapes_MatchInterpreter()
    {
        RunShapes(
            ("inc r32", [0x40]),
            ("inc r8", [0xFE, 0xC0]),
            ("inc r16", [0x66, 0x40]),
            ("dec r32", [0x48]),
            ("dec r8", [0xFE, 0xC8]),
            ("dec r16", [0x66, 0x48]),
            ("lea r32,m32", [0x8D, 0x03]),
            ("lea r16,m16", [0x66, 0x8D, 0x03]),
            ("push r32; pop r32", [0x53, 0x58]),
            ("push r16; pop r16", [0x66, 0x53, 0x66, 0x58]),
            ("push imm32; pop r32", [0x68, 0x78, 0x56, 0x34, 0x12, 0x58]),
            ("push imm8; pop r32", [0x6A, 0x05, 0x58]),
            ("nop; nop", [0x90, 0x90]));
    }

    [Fact]
    public void FlagsMisc_Shapes_MatchInterpreter()
    {
        // Slice-20 family (§158): carry-rotates, sign-extension, flag
        // push/pop, conditional set/move and Rdtsc. All are verbatim
        // mirrors of single-helper or inline interpreter cases; the
        // popf shape pushes first so both sides restore identical flags
        // (a bare popf would ingest whatever the stack holds — still
        // deterministic, but pushfd+popf exercises both handlers).
        RunShapes(
            ("rcl r32,1", [0xD1, 0xD0]),
            ("rcl r32,cl", [0xB1, 0x03, 0xD3, 0xD0]),
            ("rcr m32,1", [0xD1, 0x1B]),
            ("rcr m32,cl", [0xB1, 0x03, 0xD3, 0x1B]),
            ("cwde", [0x98]),
            ("cbw", [0x66, 0x98]),
            ("cdq", [0x99]),
            ("pushf", [0x9C]),
            ("pushfd; popf", [0x9C, 0x9D]),
            ("setne al", [0x0F, 0x95, 0xC0]),
            ("setbe m8", [0x0F, 0x96, 0x03]),
            ("cmovne ecx,eax", [0x0F, 0x45, 0xC8]),
            ("cmovbe eax,m32", [0x0F, 0x46, 0x03]),
            ("rdtsc", [0x0F, 0x31]));
    }

    [Fact]
    public void CliSti_Shapes_MatchInterpreter()
    {
        // Slice 6 (§168): IF clear/set plus both orders — verbatim
        // interpreter mirrors, EIP-precise, IRQ effects at the existing
        // per-instruction poll boundaries on both sides.
        RunShapes(
            ("cli", [0xFA]),
            ("sti", [0xFB]),
            ("cli; sti", [0xFA, 0xFB]),
            ("sti; cli", [0xFB, 0xFA]));
    }

    [Fact]
    public void AdcSbb_Shapes_MatchInterpreter()
    {
        // Slice 7 (§170): add-with-carry / subtract-with-borrow in every
        // form and size. The preset sets CF, so plain shapes prove the
        // carry=1 path; clc-prefixed 32-bit twins prove carry=0.
        RunShapes(
            ("adc r8,r8", [0x10, 0xD8]),
            ("adc r16,r16", [0x66, 0x11, 0xD8]),
            ("adc r32,r32", [0x11, 0xD8]),
            ("adc r8,imm8", [0x14, 0x05]),
            ("adc r16,imm16", [0x66, 0x15, 0x34, 0x12]),
            ("adc r32,imm32", [0x15, 0x78, 0x56, 0x34, 0x12]),
            ("adc r32,imm8", [0x83, 0xD0, 0x05]),
            ("adc r8,m8", [0x12, 0x03]),
            ("adc r16,m16", [0x66, 0x13, 0x03]),
            ("adc r32,m32", [0x13, 0x03]),
            ("adc m8,r8", [0x10, 0x03]),
            ("adc m16,r16", [0x66, 0x11, 0x03]),
            ("adc m32,r32", [0x11, 0x03]),
            ("adc m8,imm8", [0x80, 0x13, 0x05]),
            ("adc m16,imm16", [0x66, 0x81, 0x13, 0x34, 0x12]),
            ("adc m32,imm32", [0x81, 0x13, 0x78, 0x56, 0x34, 0x12]),
            ("sbb r8,r8", [0x18, 0xD8]),
            ("sbb r16,r16", [0x66, 0x1B, 0xD8]),
            ("sbb r32,r32", [0x1B, 0xD8]),
            ("sbb r8,imm8", [0x1C, 0x05]),
            ("sbb r16,imm16", [0x66, 0x1D, 0x34, 0x12]),
            ("sbb r32,imm32", [0x1D, 0x78, 0x56, 0x34, 0x12]),
            ("sbb r32,imm8", [0x83, 0xD8, 0x05]),
            ("sbb r8,m8", [0x1A, 0x03]),
            ("sbb r16,m16", [0x66, 0x1B, 0x03]),
            ("sbb r32,m32", [0x1B, 0x03]),
            ("sbb m8,r8", [0x18, 0x03]),
            ("sbb m16,r16", [0x66, 0x19, 0x03]),
            ("sbb m32,r32", [0x19, 0x03]),
            ("sbb m8,imm8", [0x80, 0x1B, 0x05]),
            ("sbb m16,imm16", [0x66, 0x81, 0x1B, 0x34, 0x12]),
            ("sbb m32,imm32", [0x81, 0x1B, 0x78, 0x56, 0x34, 0x12]),
            ("clc; adc r32,r32", [0xF8, 0x11, 0xD8]),
            ("clc; adc r32,imm32", [0xF8, 0x15, 0x78, 0x56, 0x34, 0x12]),
            ("clc; adc r32,m32", [0xF8, 0x13, 0x03]),
            ("clc; adc m32,r32", [0xF8, 0x11, 0x03]),
            ("clc; sbb r32,r32", [0xF8, 0x1B, 0xD8]),
            ("clc; sbb r32,imm32", [0xF8, 0x1D, 0x78, 0x56, 0x34, 0x12]),
            ("clc; sbb r32,m32", [0xF8, 0x1B, 0x03]),
            ("clc; sbb m32,r32", [0xF8, 0x19, 0x03]),
            ("fldcw m16", [0xD9, 0x2D, 0x00, 0x20, 0x00, 0x00]),
            ("fnstcw m16", [0xD9, 0x3D, 0x00, 0x20, 0x00, 0x00]),
            ("fwait", [0x9B]),
            ("emms", [0x0F, 0x77]));
    }

    [Fact]
    public void SseCompute_Shapes_MatchInterpreter()
    {
        // Slice-19 family (§157): SSE sqrt/reciprocal, packed arithmetic,
        // packed logicals and compares through the shared interpreter
        // helpers. All run on shared zero XMM state with [EBX] as the
        // memory side; the F3 scalar forms (sqrtss/rsqrtss/rcpss/cmpss)
        // prove the extended mandatory-prefix gate.
        RunShapes(
            ("sqrtps xmm,xmm", [0x0F, 0x51, 0xC8]),
            ("sqrtss xmm,xmm", [0xF3, 0x0F, 0x51, 0xC8]),
            ("rsqrtps xmm,xmm", [0x0F, 0x52, 0xC8]),
            ("rsqrtss xmm,xmm", [0xF3, 0x0F, 0x52, 0xC8]),
            ("rsqrtss xmm,m32", [0xF3, 0x0F, 0x52, 0x03]),
            ("rcpss xmm,xmm", [0xF3, 0x0F, 0x53, 0xC8]),
            ("addps xmm,xmm", [0x0F, 0x58, 0xC8]),
            ("addps xmm,m128", [0x0F, 0x58, 0x03]),
            ("mulps xmm,xmm", [0x0F, 0x59, 0xC8]),
            ("maxps xmm,xmm", [0x0F, 0x5F, 0xC8]),
            ("andps xmm,xmm", [0x0F, 0x54, 0xC8]),
            ("xorps xmm,xmm", [0x0F, 0x57, 0xC8]),
            ("cmpps xmm,xmm,0", [0x0F, 0xC2, 0xC8, 0x00]),
            ("cmpss xmm,xmm,0", [0xF3, 0x0F, 0xC2, 0xC8, 0x00]),
            ("comiss xmm,xmm", [0x0F, 0x2F, 0xC8]),
            ("ucomiss xmm,xmm", [0x0F, 0x2E, 0xC8]));
    }

    [Fact]
    public void SseMove_Shapes_MatchInterpreter()
    {
        // Slice-18 family (§156): packed 128-bit moves, scalar Movss,
        // low/high 64-bit moves, HL shuffles, non-temporal store and
        // Movmskps. All run on shared zero XMM state (both sides) with
        // [EBX] (Preset 0x2000 content) as the memory side; every body
        // reads before writing, so fault/step ordering matches exactly.
        // Movss carries the F3 mandatory prefix (Rep-stamped) like the
        // slice-12 scalars — the differential proves the gate holds.
        RunShapes(
            ("movups xmm,xmm", [0x0F, 0x10, 0xC8]),
            ("movups xmm,m128", [0x0F, 0x10, 0x03]),
            ("movups m128,xmm", [0x0F, 0x11, 0x03]),
            ("movaps xmm,xmm", [0x0F, 0x28, 0xC8]),
            ("movaps xmm,m128", [0x0F, 0x28, 0x03]),
            ("movaps m128,xmm", [0x0F, 0x29, 0x03]),
            ("movss xmm,xmm", [0xF3, 0x0F, 0x10, 0xC8]),
            ("movss xmm,m32", [0xF3, 0x0F, 0x10, 0x03]),
            ("movss m32,xmm", [0xF3, 0x0F, 0x11, 0x03]),
            ("movlps xmm,m64", [0x0F, 0x12, 0x03]),
            ("movlps m64,xmm", [0x0F, 0x13, 0x03]),
            ("movhps xmm,m64", [0x0F, 0x16, 0x03]),
            ("movhps m64,xmm", [0x0F, 0x17, 0x03]),
            ("movhlps xmm,xmm", [0x0F, 0x12, 0xC8]),
            ("movlhps xmm,xmm", [0x0F, 0x16, 0xC8]),
            ("movntps m128,xmm", [0x0F, 0x2B, 0x03]),
            ("movmskps r32,xmm", [0x0F, 0x50, 0xC8]));
    }

    [Fact]
    public void FpuCompare_Shapes_MatchInterpreter()
    {
        // Slice-17 family (§155): x87 compares, ST(0) sign/test ops and
        // tag/TOP/exception-control ops. Compares run behind `fld1`
        // prefixes against real stack values (integer compares read the
        // Preset 0xAABBCCDD int at [EBX]); all are read-before-mutate
        // verbatim mirrors, so fault/step ordering matches exactly.
        RunShapes(
            ("fld1; fld1; fcom st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xD1]),
            ("fld1; fld1; fcomp st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xD9]),
            ("fld1; fld1; fucom st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xDD, 0xE1]),
            ("fld1; fld1; fucomp st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xDD, 0xE9]),
            ("fld1; fcom m32", [0xD9, 0xE8, 0xD8, 0x13]),
            ("fld1; fcomp m64", [0xD9, 0xE8, 0xDC, 0x1B]),
            ("fld1; ficom m32", [0xD9, 0xE8, 0xDA, 0x13]),
            ("fld1; ficomp m16", [0xD9, 0xE8, 0xDE, 0x0B]),
            ("fld1; fchs", [0xD9, 0xE8, 0xD9, 0xE0]),
            ("fld1; fabs", [0xD9, 0xE8, 0xD9, 0xE1]),
            ("fld1; ftst", [0xD9, 0xE8, 0xD9, 0xE4]),
            ("fld1; fld1; ffree st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xDD, 0xC1]),
            ("fdecstp", [0xD9, 0xF6]),
            ("fincstp", [0xD9, 0xF7]),
            ("fnclex", [0xDB, 0xE2]),
            ("fninit", [0xDB, 0xE3]));
    }

    [Fact]
    public void IntRemainder_Shapes_MatchInterpreter()
    {
        // Slice-16 family (§154): Neg/Not, Div/Idiv single, Bswap, Xchg,
        // Leave, x87 integer arithmetic and Fnstsw. Div shapes use small
        // dividends (`mov eax,8; mov edx,0` — Preset EAX/EDX would overflow
        // the quotient and raise #DE on both sides); div-mem relies on
        // Preset values (quotient fits u32); idiv-mem stores 7 first.
        // Fiadd shapes run behind `fld1` against a real ST0.
        RunShapes(
            ("neg r32", [0xF7, 0xD8]),
            ("neg m32", [0xF7, 0x1B]),
            ("not r32", [0xF7, 0xD0]),
            ("not m32", [0xF7, 0x13]),
            ("div r32", [0xB8, 0x08, 0x00, 0x00, 0x00, 0xBA, 0x00, 0x00, 0x00, 0x00, 0xF7, 0xF1]),
            ("div m32", [0xF7, 0x33]),
            ("idiv r32", [0xB8, 0x08, 0x00, 0x00, 0x00, 0xBA, 0x00, 0x00, 0x00, 0x00, 0xF7, 0xF9]),
            ("idiv m32", [0xB8, 0x08, 0x00, 0x00, 0x00, 0xBA, 0x00, 0x00, 0x00, 0x00, 0xC7, 0x03, 0x07, 0x00, 0x00, 0x00, 0xF7, 0x3B]),
            ("bswap eax", [0x0F, 0xC8]),
            ("bswap ecx", [0x0F, 0xC9]),
            ("xchg r32,r32", [0x87, 0xC1]),
            ("xchg r32,m32", [0x87, 0x0B]),
            ("leave", [0xC9]),
            ("fld1; fiadd m32", [0xD9, 0xE8, 0xDA, 0x03]),
            ("fld1; fimul m32", [0xD9, 0xE8, 0xDA, 0x0B]),
            ("fld1; fisub m32", [0xD9, 0xE8, 0xDA, 0x23]),
            ("fld1; fidiv m32", [0xD9, 0xE8, 0xDA, 0x33]),
            ("fnstsw ax", [0xDF, 0xE0]),
            ("fld1; fnstsw m16", [0xD9, 0xE8, 0xDD, 0x3B]));
    }

    [Fact]
    public void ShiftCountCl_Mul_Shapes_MatchInterpreter()
    {
        // Slice-15 family (§153): shifts/rotates counted by CL (the
        // decoder stamps D2/D3 counts as Register index 1, so the
        // resolver gate is exact), push-mem and Mul/Imul. Shift shapes
        // prefix `mov cl,3` so both sides shift by a real nonzero count
        // (Preset leaves ECX alone); Mul/Imul run on Preset register
        // values. Bodies call the same `ExecuteShift`/`Push`/
        // `ExecuteMulDiv`/`ExecuteImul` helpers the interpreter uses.
        RunShapes(
            ("shl r32,cl", [0xB1, 0x03, 0xD3, 0xE0]),
            ("shr r32,cl", [0xB1, 0x03, 0xD3, 0xE8]),
            ("sar r32,cl", [0xB1, 0x03, 0xD3, 0xF8]),
            ("rol r32,cl", [0xB1, 0x03, 0xD3, 0xC0]),
            ("ror r32,cl", [0xB1, 0x03, 0xD3, 0xC8]),
            ("shl m32,cl", [0xB1, 0x03, 0xD3, 0x23]),
            ("shr m32,cl", [0xB1, 0x03, 0xD3, 0x2B]),
            ("push m32", [0xFF, 0x33]),
            ("mul r32", [0xF7, 0xE0]),
            ("mul m32", [0xF7, 0x23]),
            ("imul r32", [0xF7, 0xE8]),
            ("imul r32,r32", [0x0F, 0xAF, 0xC8]),
            ("imul r32,m32", [0x0F, 0xAF, 0x0B]),
            ("imul r32,m32,imm8", [0x6B, 0x0B, 0x05]));
    }

    [Fact]
    public void FpuReg_Shapes_MatchInterpreter()
    {
        // Slice-14 family (§152): x87 register forms — Fld ST(i), Fxch,
        // constant loads, pop-arithmetic and the Fsub/Fdiv families.
        // All run behind `fld1` prefixes so they execute against real
        // stack values; all are pure register ops (no fault possible)
        // except the reg,mem arithmetic which reads [EBX] into a temp
        // before any FPU mutation, exactly like the interpreter.
        RunShapes(
            ("fld1; fld st1", [0xD9, 0xE8, 0xD9, 0xC1]),
            ("fld1; fld1; fxch st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xD9, 0xC9]),
            ("fld1", [0xD9, 0xE8]),
            ("fldz", [0xD9, 0xEE]),
            ("fldpi", [0xD9, 0xEB]),
            ("fld1; fld1; faddp st1,st0", [0xD9, 0xE8, 0xD9, 0xE8, 0xDE, 0xC1]),
            ("fld1; fld1; fmulp st1,st0", [0xD9, 0xE8, 0xD9, 0xE8, 0xDE, 0xC9]),
            ("fld1; fld1; fsubp st1,st0", [0xD9, 0xE8, 0xD9, 0xE8, 0xDE, 0xE9]),
            ("fld1; fld1; fsubrp st1,st0", [0xD9, 0xE8, 0xD9, 0xE8, 0xDE, 0xE1]),
            ("fld1; fld1; fdivp st1,st0", [0xD9, 0xE8, 0xD9, 0xE8, 0xDE, 0xF9]),
            ("fld1; fld1; fdivrp st1,st0", [0xD9, 0xE8, 0xD9, 0xE8, 0xDE, 0xF1]),
            ("fld1; fld1; fsub st0,st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xE1]),
            ("fld1; fld1; fsubr st0,st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xE9]),
            ("fld1; fld1; fdiv st0,st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xF1]),
            ("fld1; fld1; fdivr st0,st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xF9]),
            ("fld1; fsub m32", [0xD9, 0xE8, 0xD8, 0x23]),
            ("fld1; fdiv m32", [0xD9, 0xE8, 0xD8, 0x33]),
            ("fld1; fst st1", [0xD9, 0xE8, 0xDD, 0xD1]));
    }

    [Fact]
    public void FpuMem_Shapes_MatchInterpreter()
    {
        // Slice-13 family (§151): x87 loads/stores/integer moves plus
        // ST(0) add/multiply. Stack ops run behind an `fld1` prefix so
        // they execute against a real ST0 (both sides share the empty
        // start, so the differential is exact either way); modrm 0x03
        // addresses [EBX] (Preset maps 0x2000). Fstp/Fst never touch
        // EFLAGS; Fistp's RoundingMode/IE live in the shared helper.
        RunShapes(
            ("fld m32", [0xD9, 0x03]),
            ("fld m64", [0xDD, 0x03]),
            ("fld m80", [0xDB, 0x2B]),
            ("fld1; fst m32", [0xD9, 0xE8, 0xD9, 0x13]),
            ("fld1; fst m64", [0xD9, 0xE8, 0xDD, 0x13]),
            ("fld1; fstp m32", [0xD9, 0xE8, 0xD9, 0x1B]),
            ("fld1; fstp m64", [0xD9, 0xE8, 0xDD, 0x1B]),
            ("fld1; fstp m80", [0xD9, 0xE8, 0xDB, 0x3B]),
            ("fld1; fstp st0", [0xD9, 0xE8, 0xDD, 0xD8]),
            ("fild m16", [0xDF, 0x03]),
            ("fild m32", [0xDB, 0x03]),
            ("fild m64", [0xDF, 0x2B]),
            ("fld1; fistp m16", [0xD9, 0xE8, 0xDF, 0x1B]),
            ("fld1; fistp m32", [0xD9, 0xE8, 0xDB, 0x1B]),
            ("fld1; fistp m64", [0xD9, 0xE8, 0xDF, 0x3B]),
            ("fld1; fadd m32", [0xD9, 0xE8, 0xD8, 0x03]),
            ("fld1; fadd m64", [0xD9, 0xE8, 0xDC, 0x03]),
            ("fld1; fld1; fadd st0,st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xC1]),
            ("fld1; fmul m32", [0xD9, 0xE8, 0xD8, 0x0B]),
            ("fld1; fmul m64", [0xD9, 0xE8, 0xDC, 0x0B]),
            ("fld1; fld1; fmul st0,st1", [0xD9, 0xE8, 0xD9, 0xE8, 0xD8, 0xC9]));
    }

    [Fact]
    public void SseScalar_Shapes_MatchInterpreter()
    {
        // Slice-12 family (§149): scalar SSE arithmetic (one verbatim
        // `ExecuteSseArithmetic` call) and scalar float-to-int converts
        // (same `ConvertFloatToInt32` helper, truncate baked). modrm 0x03
        // reads [EBX]; 0xC8 is the reg,reg form. A faulting memory source
        // throws before any write, exactly like the interpreter.
        RunShapes(
            ("addss xmm,xmm", [0xF3, 0x0F, 0x58, 0xC8]),
            ("addss xmm,m32", [0xF3, 0x0F, 0x58, 0x03]),
            ("subss xmm,xmm", [0xF3, 0x0F, 0x5C, 0xC8]),
            ("subss xmm,m32", [0xF3, 0x0F, 0x5C, 0x03]),
            ("mulss xmm,xmm", [0xF3, 0x0F, 0x59, 0xC8]),
            ("mulss xmm,m32", [0xF3, 0x0F, 0x59, 0x03]),
            ("divss xmm,xmm", [0xF3, 0x0F, 0x5E, 0xC8]),
            ("divss xmm,m32", [0xF3, 0x0F, 0x5E, 0x03]),
            ("minss xmm,xmm", [0xF3, 0x0F, 0x5D, 0xC8]),
            ("minss xmm,m32", [0xF3, 0x0F, 0x5D, 0x03]),
            ("maxss xmm,xmm", [0xF3, 0x0F, 0x5F, 0xC8]),
            ("maxss xmm,m32", [0xF3, 0x0F, 0x5F, 0x03]),
            ("cvtss2si r32,xmm", [0xF3, 0x0F, 0x2D, 0xC8]),
            ("cvtss2si r32,m32", [0xF3, 0x0F, 0x2D, 0x03]),
            ("cvttss2si r32,xmm", [0xF3, 0x0F, 0x2C, 0xC8]),
            ("cvttss2si r32,m32", [0xF3, 0x0F, 0x2C, 0x03]));
    }

    [Fact]
    public void Hint_Shapes_MatchInterpreter()
    {
        // Slice-11 family (§148): Prefetch + ordering fences are interpreter
        // nops. Prefetch decodes with a memory operand it never touches
        // (0F 18 /1 with a disp32), so even an unmapped address must not
        // fault — the differential proves it (both sides advance EIP).
        RunShapes(
            ("prefetch m8", [0x0F, 0x18, 0x0D, 0x00, 0x20, 0x00, 0x00]),
            ("lfence", [0x0F, 0xAE, 0xE8]),
            ("mfence", [0x0F, 0xAE, 0xF0]),
            ("sfence", [0x0F, 0xAE, 0xF8]));
    }

    [Fact]
    public void MmxMov_Shapes_MatchInterpreter()
    {
        // Slice-10 family (§147): MMX 64-bit moves. modrm 0x03 addresses
        // [EBX] (Preset maps 0x2000 with known content, 8-byte aligned);
        // 0xC8 is the reg,reg form. No flags are touched (Preset's CF/ZF
        // pin that); EIP advances only after completion, so a faulting
        // memory shape keeps precise EIP exactly like the interpreter.
        RunShapes(
            ("movq mm,m64", [0x0F, 0x6F, 0x03]),
            ("movq m64,mm", [0x0F, 0x7F, 0x03]),
            ("movq mm,mm", [0x0F, 0x6F, 0xC8]),
            ("movntq m64,mm", [0x0F, 0xE7, 0x03]));
    }

    [Fact]
    public void IncDecMem_Shapes_MatchInterpreter()
    {
        // Slice-9 family (§146): Inc/Dec with a memory destination through
        // ComputeAddress + ReadMemory/WriteMemory. modrm 0x03 addresses
        // [EBX] (Preset maps 0x2000); Preset sets CF so a CF-clobbering
        // body fails the EFLAGS comparison. EIP advances only after the
        // write completes, so a faulting shape keeps precise EIP exactly
        // like the interpreter (covered by MemFault_PreciseEipVectorCr2).
        RunShapes(
            ("inc m8", [0xFE, 0x03]),
            ("inc m16", [0x66, 0xFF, 0x03]),
            ("inc m32", [0xFF, 0x03]),
            ("dec m8", [0xFE, 0x0B]),
            ("dec m16", [0x66, 0xFF, 0x0B]),
            ("dec m32", [0xFF, 0x0B]));
    }

    [Fact]
    public void AluMem_Shapes_AtAllSizes_MatchInterpreter()
    {
        // Slice-3 family (§142): ALU with a memory operand. modrm 0x03 reads
        // [EBX] (Preset maps 0x2000 with known content); 0x18 swaps reg/mem.
        RunShapes(
            ("mov m8,imm8", [0xC6, 0x03, 0x05]),
            ("mov m16,imm16", [0x66, 0xC7, 0x03, 0x34, 0x12]),
            ("mov m32,imm32", [0xC7, 0x03, 0x78, 0x56, 0x34, 0x12]),
            ("add r32,m32", [0x03, 0x03]),
            ("add r8,m8", [0x02, 0x03]),
            ("add r16,m16", [0x66, 0x03, 0x03]),
            ("add m32,r32", [0x01, 0x03]),
            ("add m8,r8", [0x00, 0x03]),
            ("add m16,r16", [0x66, 0x01, 0x03]),
            ("add m32,imm8", [0x83, 0x03, 0x05]),
            ("add m8,imm8", [0x80, 0x03, 0x05]),
            ("add m32,imm32", [0x81, 0x03, 0x78, 0x56, 0x34, 0x12]),
            ("sub r32,m32", [0x2B, 0x03]),
            ("sub r8,m8", [0x2A, 0x03]),
            ("sub r16,m16", [0x66, 0x2B, 0x03]),
            ("sub m32,r32", [0x29, 0x03]),
            ("sub m8,r8", [0x28, 0x03]),
            ("sub m16,r16", [0x66, 0x29, 0x03]),
            ("sub m32,imm8", [0x83, 0x2B, 0x05]),
            ("sub m8,imm8", [0x80, 0x2B, 0x05]),
            ("sub m32,imm32", [0x81, 0x2B, 0x78, 0x56, 0x34, 0x12]),
            ("and r32,m32", [0x23, 0x03]),
            ("and r8,m8", [0x22, 0x03]),
            ("and r16,m16", [0x66, 0x23, 0x03]),
            ("and m32,r32", [0x21, 0x03]),
            ("and m8,r8", [0x20, 0x03]),
            ("and m16,r16", [0x66, 0x21, 0x03]),
            ("and m32,imm8", [0x83, 0x23, 0x05]),
            ("and m8,imm8", [0x80, 0x23, 0x05]),
            ("and m32,imm32", [0x81, 0x23, 0x78, 0x56, 0x34, 0x12]),
            ("or r32,m32", [0x0B, 0x03]),
            ("or r8,m8", [0x0A, 0x03]),
            ("or r16,m16", [0x66, 0x0B, 0x03]),
            ("or m32,r32", [0x09, 0x03]),
            ("or m8,r8", [0x08, 0x03]),
            ("or m16,r16", [0x66, 0x09, 0x03]),
            ("or m32,imm8", [0x83, 0x0B, 0x05]),
            ("or m8,imm8", [0x80, 0x0B, 0x05]),
            ("or m32,imm32", [0x81, 0x0B, 0x78, 0x56, 0x34, 0x12]),
            ("xor r32,m32", [0x33, 0x03]),
            ("xor r8,m8", [0x32, 0x03]),
            ("xor r16,m16", [0x66, 0x33, 0x03]),
            ("xor m32,r32", [0x31, 0x03]),
            ("xor m8,r8", [0x30, 0x03]),
            ("xor m16,r16", [0x66, 0x31, 0x03]),
            ("xor m32,imm8", [0x83, 0x33, 0x05]),
            ("xor m8,imm8", [0x80, 0x33, 0x05]),
            ("xor m32,imm32", [0x81, 0x33, 0x78, 0x56, 0x34, 0x12]),
            ("cmp r32,m32", [0x3B, 0x03]),
            ("cmp r8,m8", [0x3A, 0x03]),
            ("cmp r16,m16", [0x66, 0x3B, 0x03]),
            ("cmp m32,r32", [0x39, 0x03]),
            ("cmp m8,r8", [0x38, 0x03]),
            ("cmp m16,r16", [0x66, 0x39, 0x03]),
            ("cmp m32,imm8", [0x83, 0x3B, 0x05]),
            ("cmp m8,imm8", [0x80, 0x3B, 0x05]),
            ("cmp m32,imm32", [0x81, 0x3B, 0x78, 0x56, 0x34, 0x12]),
            ("test r32,m32", [0x85, 0x03]),
            ("test r8,m8", [0x84, 0x03]),
            ("test r16,m16", [0x66, 0x85, 0x03]),
            ("test m32,r32", [0x85, 0x1B]),
            ("test m8,r8", [0x84, 0x1B]),
            ("test m16,r16", [0x66, 0x85, 0x1B]),
            ("test m8,imm8", [0xF6, 0x03, 0x05]),
            ("test m16,imm16", [0x66, 0xF7, 0x03, 0x34, 0x12]),
            ("test m32,imm32", [0xF7, 0x03, 0x78, 0x56, 0x34, 0x12]));
    }

    [Fact]
    public void Jcc_AllConditions_TakenAndFallthrough_MatchInterpreter()
    {
        // jcc +1 over inc eax: taken skips the inc, so a wrong direction
        // shows up in EAX. Two flag setups split every condition across
        // taken and fallthrough (Pf covered by setup 2).
        uint[] flagSetups =
        [
            FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Zf,
            FlagBits.Reserved1 | FlagBits.Sf | FlagBits.Of | FlagBits.Pf,
        ];
        for (int condition = 0; condition < 16; condition++)
        {
            RunJcc([(byte)(0x70 + condition), 0x01, 0x40, 0xF4], $"jcc rel8 cond {condition}", flagSetups);
            RunJcc(
                [0x0F, (byte)(0x80 + condition), 0x01, 0x00, 0x00, 0x00, 0x40, 0xF4],
                $"jcc rel32 cond {condition}",
                flagSetups);
        }

        RunJcc(
            [0x66, 0x0F, 0x84, 0x01, 0x00, 0x40, 0xF4],
            "jcc rel16 (size-insensitive)",
            flagSetups);
        return;

        static void RunJcc(byte[] code, string name, uint[] setups)
        {
            foreach (uint flags in setups)
            {
                XboxCpu plain = Create(false, code);
                XboxCpu specCpu = Create(true, code);
                Preset(plain);
                Preset(specCpu);
                plain.Context.Eflags = flags;
                specCpu.Context.Eflags = flags;
                try
                {
                    Assert.Equal(plain.Execute(16), specCpu.Execute(16));
                    AssertSameState(plain, specCpu);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"Shape '{name}' diverged (flags 0x{flags:X})", exception);
                }

                Assert.True(plain.Context.Halted && specCpu.Context.Halted);
            }
        }
    }

    [Fact]
    public void Jcc_Resolver_CoversAllConditions()
    {
        for (int condition = 0; condition < 16; condition++)
        {
            Assert.NotNull(XboxCpu.ResolveSpec(DecodeRel8(condition)));
            Assert.NotNull(XboxCpu.ResolveSpec(DecodeRel32(condition)));
        }

        return;

        static DecodedInstruction DecodeRel8(int condition)
        {
            return Decode((byte)(0x70 + condition), 0x01);
        }

        static DecodedInstruction DecodeRel32(int condition)
        {
            return Decode(0x0F, (byte)(0x80 + condition), 0x01, 0x00, 0x00, 0x00);
        }

        static DecodedInstruction Decode(params byte[] code)
        {
            Span<byte> window = stackalloc byte[Decoder.MaxInstructionLength];
            window.Clear();
            code.CopyTo(window);
            return Decoder.Decode(window);
        }
    }

    [Fact]
    public void ShiftExtend_Shapes_AtAllSizes_MatchInterpreter()
    {
        // Slice-8 family (§145): C1-group shifts by immediate and 0F B6/B7/BE/BF
        // extension. modrm 0xE0/0xC0 addresses EAX, 0x23/0x03 address [EBX].
        RunShapes(
            ("shl r32,imm8", [0xC1, 0xE0, 0x05]),
            ("shl r8,imm8", [0xC0, 0xE0, 0x05]),
            ("shl r16,imm8", [0x66, 0xC1, 0xE0, 0x05]),
            ("shl m32,imm8", [0xC1, 0x23, 0x05]),
            ("shl m8,imm8", [0xC0, 0x23, 0x05]),
            ("shl m16,imm8", [0x66, 0xC1, 0x23, 0x05]),
            ("shr r32,imm8", [0xC1, 0xE8, 0x05]),
            ("shr r8,imm8", [0xC0, 0xE8, 0x05]),
            ("shr r16,imm8", [0x66, 0xC1, 0xE8, 0x05]),
            ("shr m32,imm8", [0xC1, 0x2B, 0x05]),
            ("shr m8,imm8", [0xC0, 0x2B, 0x05]),
            ("shr m16,imm8", [0x66, 0xC1, 0x2B, 0x05]),
            ("sar r32,imm8", [0xC1, 0xF8, 0x05]),
            ("sar r8,imm8", [0xC0, 0xF8, 0x05]),
            ("sar r16,imm8", [0x66, 0xC1, 0xF8, 0x05]),
            ("sar m32,imm8", [0xC1, 0x3B, 0x05]),
            ("sar m8,imm8", [0xC0, 0x3B, 0x05]),
            ("sar m16,imm8", [0x66, 0xC1, 0x3B, 0x05]),
            ("rol r32,imm8", [0xC1, 0xC0, 0x05]),
            ("rol r8,imm8", [0xC0, 0xC0, 0x05]),
            ("rol r16,imm8", [0x66, 0xC1, 0xC0, 0x05]),
            ("rol m32,imm8", [0xC1, 0x03, 0x05]),
            ("rol m8,imm8", [0xC0, 0x03, 0x05]),
            ("rol m16,imm8", [0x66, 0xC1, 0x03, 0x05]),
            ("ror r32,imm8", [0xC1, 0xC8, 0x05]),
            ("ror r8,imm8", [0xC0, 0xC8, 0x05]),
            ("ror r16,imm8", [0x66, 0xC1, 0xC8, 0x05]),
            ("ror m32,imm8", [0xC1, 0x0B, 0x05]),
            ("ror m8,imm8", [0xC0, 0x0B, 0x05]),
            ("ror m16,imm8", [0x66, 0xC1, 0x0B, 0x05]),
            ("movzx r32,r8", [0x0F, 0xB6, 0xC0]),
            ("movzx r32,m8", [0x0F, 0xB6, 0x03]),
            ("movzx r16,r8", [0x66, 0x0F, 0xB6, 0xC0]),
            ("movzx r16,m8", [0x66, 0x0F, 0xB6, 0x03]),
            ("movzx r32,r16", [0x0F, 0xB7, 0xC0]),
            ("movzx r32,m16", [0x0F, 0xB7, 0x03]),
            ("movzx r16,r16", [0x66, 0x0F, 0xB7, 0xC0]),
            ("movzx r16,m16", [0x66, 0x0F, 0xB7, 0x03]),
            ("movsx r32,r8", [0x0F, 0xBE, 0xC0]),
            ("movsx r32,m8", [0x0F, 0xBE, 0x03]),
            ("movsx r16,r8", [0x66, 0x0F, 0xBE, 0xC0]),
            ("movsx r16,m8", [0x66, 0x0F, 0xBE, 0x03]),
            ("movsx r32,r16", [0x0F, 0xBF, 0xC0]),
            ("movsx r32,m16", [0x0F, 0xBF, 0x03]),
            ("movsx r16,r16", [0x66, 0x0F, 0xBF, 0xC0]),
            ("movsx r16,m16", [0x66, 0x0F, 0xBF, 0x03]));
    }

    [Fact]
    public void NearControl_Shapes_MatchInterpreter()
    {
        // Jumps skip an inc, calls return onto a hlt: a wrong target shows
        // up in EAX, ESP or the halt itself. All end at a hlt.
        RunControl("jmp rel8", [0xEB, 0x01, 0x40, 0xF4], static _ => { });
        RunControl("jmp rel32", [0xE9, 0x01, 0x00, 0x00, 0x00, 0x40, 0xF4], static _ => { });
        RunControl(
            "jmp eax",
            [0xB8, 0x08, 0x10, 0x00, 0x00, 0xFF, 0xE0, 0x40, 0xF4],
            static _ => { });
        RunControl(
            "jmp [ebx]",
            [0xFF, 0x23, 0x40, 0xF4],
            static cpu => cpu.Ram().Write32(0x2000, 0x1003));
        RunControl(
            "call rel32; ret",
            [0xE8, 0x02, 0x00, 0x00, 0x00, 0xF4, 0x40, 0xC3],
            static _ => { });
        RunControl(
            "call eax; ret",
            [0xB8, 0x09, 0x10, 0x00, 0x00, 0xFF, 0xD0, 0xF4, 0x40, 0xC3],
            static _ => { });
        RunControl(
            "call [ebx]; ret",
            [0xFF, 0x13, 0xF4, 0x40, 0xC3],
            static cpu => cpu.Ram().Write32(0x2000, 0x1004));
        RunControl(
            "call; ret imm16",
            [0xE8, 0x03, 0x00, 0x00, 0x00, 0xF4, 0x90, 0x90, 0xC2, 0x04, 0x00],
            static _ => { });
        return;

        static void RunControl(string name, byte[] code, Action<XboxCpu> setup)
        {
            XboxCpu plain = Create(false, code);
            XboxCpu specCpu = Create(true, code);
            Preset(plain);
            Preset(specCpu);
            setup(plain);
            setup(specCpu);
            try
            {
                Assert.Equal(plain.Execute(32), specCpu.Execute(32));
                AssertSameState(plain, specCpu);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Shape '{name}' diverged", exception);
            }

            Assert.True(plain.Context.Halted && specCpu.Context.Halted);
        }
    }

    [Fact]
    public void SegmentOverride_MemShapes_MatchInterpreter()
    {
        // FS base differs from DS base: the override must select the FS byte.
        RunShapes(
            ("mov r32,FS:m32", [0x64, 0x8B, 0x03]),
            ("mov FS:m32,r32", [0x64, 0x89, 0x03]),
            ("lea r32,FS:m32", [0x64, 0x8D, 0x03]));
    }

    [Fact]
    public void Resolver_CoversOnlyTheInitialShapes()
    {
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x90)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x89, 0xD8)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x8B, 0x03)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x01, 0xD8)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x83, 0xC0, 0x05)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x39, 0xD8)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x85, 0xC0)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x40)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x48)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x8D, 0x03)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x50)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x58)));

        // Uncovered control (far/int), privileged, string/REP, FPU, invalid
        // and uncovered shapes keep the interpreter.
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xEA, 0x00, 0x00, 0x08, 0x00))); // jmp far
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xEB, 0xFE))); // jmp rel8
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xE9, 0x00, 0x00, 0x00, 0x00))); // jmp rel32
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xE8, 0x00, 0x00, 0x00, 0x00))); // call rel32
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xC3))); // ret
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xC2, 0x04, 0x00))); // ret imm16
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xF4)));
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xCD, 0x21)));
        Assert.Null(XboxCpu.ResolveSpec(Decode(0x0F, 0x0B)));
        Assert.Null(XboxCpu.ResolveSpec(Decode(0x62, 0x00, 0x00)));
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xFF, 0x30))); // push m32 (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xFF, 0x03))); // inc m32 (slice 9)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xFF, 0x0B))); // dec m32 (slice 9)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xFE, 0x03))); // inc m8 (slice 9)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xFE, 0x0B))); // dec m8 (slice 9)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x6F, 0x03))); // movq mm,m64 (slice 10)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x7F, 0x03))); // movq m64,mm (slice 10)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x6F, 0xC8))); // movq mm,mm (slice 10)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0xE7, 0x03))); // movntq m64,mm (slice 10)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x18, 0x0D, 0x00, 0x20, 0x00, 0x00))); // prefetch m8 (slice 11)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0xAE, 0xE8))); // lfence (slice 11)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0xAE, 0xF0))); // mfence (slice 11)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0xAE, 0xF8))); // sfence (slice 11)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x59, 0xC8))); // mulss xmm,xmm (slice 12)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x59, 0x03))); // mulss xmm,m32 (slice 12)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x58, 0x03))); // addss xmm,m32 (slice 12)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x5C, 0x03))); // subss xmm,m32 (slice 12)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x5E, 0x03))); // divss xmm,m32 (slice 12)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x5D, 0x03))); // minss xmm,m32 (slice 12)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x5F, 0x03))); // maxss xmm,m32 (slice 12)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x2D, 0x03))); // cvtss2si r32,m32 (slice 12)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x2C, 0x03))); // cvttss2si r32,m32 (slice 12)
        Assert.Null(XboxCpu.ResolveSpec(Decode(0xF3, 0xA4))); // rep movsb: real repeat keeps the interpreter
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0x03))); // fld m32 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDD, 0x03))); // fld m64 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDB, 0x2B))); // fld m80 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0x13))); // fst m32 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0x1B))); // fstp m32 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDD, 0xD8))); // fstp st0 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDB, 0x03))); // fild m32 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDF, 0x2B))); // fild m64 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDB, 0x1B))); // fistp m32 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD8, 0x03))); // fadd m32 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD8, 0x0B))); // fmul m32 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD8, 0xC1))); // fadd st0,st1 (slice 13)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0xC1))); // fld st1 (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0xC9))); // fxch st1 (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0xE8))); // fld1 (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0xEE))); // fldz (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDE, 0xC1))); // faddp (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDE, 0xC9))); // fmulp (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDE, 0xE9))); // fsubp (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDE, 0xF9))); // fdivp (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD8, 0xE1))); // fsub st0,st1 (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD8, 0xF1))); // fdiv st0,st1 (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD8, 0x23))); // fsub m32 (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDD, 0xD1))); // fst st1 (slice 14)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD3, 0xE0))); // shl r32,cl (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD3, 0xE8))); // shr r32,cl (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD3, 0xF8))); // sar r32,cl (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD3, 0x23))); // shl m32,cl (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF7, 0xE0))); // mul r32 (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF7, 0x23))); // mul m32 (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF7, 0xE8))); // imul r32 (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0xAF, 0xC8))); // imul r32,r32 (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0xAF, 0x0B))); // imul r32,m32 (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x6B, 0x0B, 0x05))); // imul r32,m32,imm8 (slice 15)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD3, 0xD0))); // rcl r32,cl (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF7, 0xD8))); // neg r32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF7, 0x1B))); // neg m32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF7, 0xD0))); // not r32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF7, 0xF0))); // div r32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF7, 0x33))); // div m32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF7, 0xF8))); // idiv r32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0xC8))); // bswap eax (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x87, 0xC1))); // xchg r32,r32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x87, 0x0B))); // xchg r32,m32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xC9))); // leave (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDA, 0x03))); // fiadd m32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDA, 0x0B))); // fimul m32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDA, 0x2B))); // fisubr m32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDA, 0x33))); // fidiv m32 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDF, 0xE0))); // fnstsw ax (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDD, 0x3B))); // fnstsw m16 (slice 16)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD8, 0xD1))); // fcom st1 (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD8, 0xD9))); // fcomp st1 (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDD, 0xE1))); // fucom st1 (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDD, 0xE9))); // fucomp st1 (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD8, 0x13))); // fcom m32 (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDA, 0x13))); // ficom m32 (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDE, 0x0B))); // ficomp m16 (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0xE0))); // fchs (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0xE1))); // fabs (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0xE4))); // ftst (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDD, 0xC1))); // ffree st1 (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0xF6))); // fdecstp (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xDB, 0xE2))); // fnclex (slice 17)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x10, 0xC8))); // movups xmm,xmm (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x10, 0x03))); // movups xmm,m128 (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x11, 0x03))); // movups m128,xmm (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x28, 0xC8))); // movaps xmm,xmm (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x29, 0x03))); // movaps m128,xmm (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x10, 0xC8))); // movss xmm,xmm (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x10, 0x03))); // movss xmm,m32 (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x11, 0x03))); // movss m32,xmm (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x12, 0x03))); // movlps xmm,m64 (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x13, 0x03))); // movlps m64,xmm (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x12, 0xC8))); // movhlps (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x2B, 0x03))); // movntps m128,xmm (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x50, 0xC8))); // movmskps (slice 18)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x51, 0xC8))); // sqrtps (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x51, 0xC8))); // sqrtss (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x52, 0xC8))); // rsqrtss (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x52, 0x03))); // rsqrtss m32 (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0x53, 0xC8))); // rcpss (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x58, 0xC8))); // addps (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x59, 0x03))); // mulps m128 (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x54, 0xC8))); // andps (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x57, 0xC8))); // xorps (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0xC2, 0xC8, 0x00))); // cmpps (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF3, 0x0F, 0xC2, 0xC8, 0x00))); // cmpss (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x2F, 0xC8))); // comiss (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x2E, 0xC8))); // ucomiss (slice 19)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD1, 0xD0))); // rcl r32,1 (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD1, 0x1B))); // rcr m32,1 (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x98))); // cwde (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x98))); // cbw (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x99))); // cdq (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x9C))); // pushf (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x9D))); // popf (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x95, 0xC0))); // setne al (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x96, 0x03))); // setbe m8 (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x45, 0xC8))); // cmovne (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x31))); // rdtsc (slice 20)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD1, 0xE0))); // shl r,1: implicit imm
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xF6, 0xC0, 0x01))); // test r8,imm8 via F6 grp
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xFA))); // cli (slice 6/§168)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xFB))); // sti (slice 6/§168)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x10, 0xD8))); // adc r8,r8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x11, 0xD8))); // adc r16,r16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x11, 0xD8))); // adc r32,r32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x14, 0x05))); // adc r8,imm8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x15, 0x34, 0x12))); // adc r16,imm16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x15, 0x78, 0x56, 0x34, 0x12))); // adc r32,imm32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x83, 0xD0, 0x05))); // adc r32,imm8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x12, 0x03))); // adc r8,m8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x13, 0x03))); // adc r16,m16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x13, 0x03))); // adc r32,m32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x10, 0x03))); // adc m8,r8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x11, 0x03))); // adc m16,r16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x11, 0x03))); // adc m32,r32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x80, 0x13, 0x05))); // adc m8,imm8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x81, 0x13, 0x34, 0x12))); // adc m16,imm16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x81, 0x13, 0x78, 0x56, 0x34, 0x12))); // adc m32,imm32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x18, 0xD8))); // sbb r8,r8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x1B, 0xD8))); // sbb r16,r16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x1B, 0xD8))); // sbb r32,r32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x1C, 0x05))); // sbb r8,imm8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x1D, 0x34, 0x12))); // sbb r16,imm16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x1D, 0x78, 0x56, 0x34, 0x12))); // sbb r32,imm32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x83, 0xD8, 0x05))); // sbb r32,imm8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x1A, 0x03))); // sbb r8,m8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x1B, 0x03))); // sbb r16,m16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x1B, 0x03))); // sbb r32,m32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x18, 0x03))); // sbb m8,r8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x19, 0x03))); // sbb m16,r16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x19, 0x03))); // sbb m32,r32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x80, 0x1B, 0x05))); // sbb m8,imm8 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x66, 0x81, 0x1B, 0x34, 0x12))); // sbb m16,imm16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x81, 0x1B, 0x78, 0x56, 0x34, 0x12))); // sbb m32,imm32 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0x2D, 0x00, 0x20, 0x00, 0x00))); // fldcw m16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0xD9, 0x3D, 0x00, 0x20, 0x00, 0x00))); // fnstcw m16 (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x9B))); // fwait (slice 7)
        Assert.NotNull(XboxCpu.ResolveSpec(Decode(0x0F, 0x77))); // emms (slice 7)
        return;

        static DecodedInstruction Decode(params byte[] code)
        {
            Span<byte> window = stackalloc byte[Decoder.MaxInstructionLength];
            window.Clear();
            code.CopyTo(window);
            return Decoder.Decode(window);
        }
    }

    [Fact]
    public void ControlMix_Loop_MatchesInterpreter()
    {
        // inc/xor-imm/dec/nop/jmp all specialize; the loop runs fully
        // through handlers with no interpreter fallback.
        XboxCpu plain = Create(false, Loop);
        XboxCpu spec = Create(true, Loop);
        Assert.Equal(plain.Execute(64), spec.Execute(64));
        AssertSameState(plain, spec);
        Assert.True(spec.SpecStatistics!.ShapeHits > 0);
        Assert.Equal(0UL, spec.SpecStatistics.ShapeFallbacks);
        Assert.True(spec.SpecStatistics.SpecRan > 0);
    }

    [Fact]
    public void MemFault_PreciseEipVectorCr2_MatchesInterpreter()
    {
        // mov eax,1 (commits) then mov ebx,[0x2000] faults: VPN2 unmapped.
        // The vector-14 handler is entered once per fault; the EIP is rewound
        // into the warm blocks so the second fault runs the faulting member
        // through its pre-specialized handler. (A bare IRET would pop the
        // pushed #PF error code as EIP, so no round-trip is attempted.)
        byte[] code = [0xB8, 0x01, 0x00, 0x00, 0x00, 0x8B, 0x1D, 0x00, 0x20, 0x00, 0x00, 0x90, 0xF4];

        XboxCpu plain = CreatePaged(false, code);
        XboxCpu specPaged = CreatePaged(true, code);

        plain.Step();
        specPaged.Step();
        AssertSameState(plain, specPaged);
        Assert.Equal(0x1005u, specPaged.Context.Eip);

        // First fault (miss path decodes fresh on both sides).
        plain.Step();
        specPaged.Step();
        AssertSameState(plain, specPaged);
        Assert.Equal(0x1500u, specPaged.Context.Eip);

        // Rewind into the warm blocks: the faulting member is now served from
        // a formed block (via its handler on the spec side).
        plain.Context.Eip = 0x1000;
        specPaged.Context.Eip = 0x1000;
        plain.Step();
        specPaged.Step();
        AssertSameState(plain, specPaged);
        Assert.Equal(0x1005u, specPaged.Context.Eip);

        plain.Step();
        specPaged.Step();
        AssertSameState(plain, specPaged);
        Assert.Equal(14, specPaged.LastFaultVector);
        Assert.Equal(0x1005u, specPaged.LastFaultEip);
        Assert.Equal(0x2000u, specPaged.Context.Cr2);
        Assert.Equal(1u, specPaged.Context.Eax);
        Assert.Equal(0x1500u, specPaged.Context.Eip);
        return;

        static XboxCpu CreatePaged(bool spec, byte[] bytes)
        {
            XboxCpu cpu = Create(spec, bytes);
            MapIdentity(cpu.Bus(), 1, 1);
            MapIdentity(cpu.Bus(), 4, 1);
            MapIdentity(cpu.Bus(), 7, 2); // IDT/stacked frame pages (frame grows below 0x8000)
            EnablePaging(cpu);
            cpu.Ram().Write8(0x1500, 0x90); // handler: nop (never returned through)
            InstallIdt(cpu, 14, 0x1500);
            return cpu;
        }
    }

    [Fact]
    public void UdTerminator_ExactVectorAndEip_MatchesInterpreter()
    {
        byte[] code = [0x90, 0x40, 0x62, 0x00, 0x00, 0xF4];

        XboxCpu plain = CreateHandled(false, code);
        XboxCpu specCpu = CreateHandled(true, code);
        for (int i = 0; i < 4; i++)
        {
            plain.Step();
            specCpu.Step();
            AssertSameState(plain, specCpu);
        }

        Assert.Equal(6, specCpu.LastFaultVector);
        Assert.Equal(0x1002u, specCpu.LastFaultEip);
        return;

        static XboxCpu CreateHandled(bool spec, byte[] bytes)
        {
            XboxCpu cpu = Create(spec, bytes);
            cpu.Ram().Write8(0x3000, 0xCF);
            InstallIdt(cpu, 6, 0x3000);
            return cpu;
        }
    }

    [Fact]
    public void Irq_AtEveryMember_MatchesInterpreter()
    {
        // Six NOPs + HLT; the source fires on the 3rd acknowledge (before the
        // 3rd member). The handler IRETs back into the stream.
        byte[] code = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xF4];

        XboxCpu plain = CreateIrq(false, code, new CountedInterruptSource(0x21, fireAt: 3));
        XboxCpu specCpu = CreateIrq(true, code, new CountedInterruptSource(0x21, fireAt: 3));

        // Two quiet members, then delivery lands before the third member.
        plain.Step();
        specCpu.Step();
        AssertSameState(plain, specCpu);
        plain.Step();
        specCpu.Step();
        AssertSameState(plain, specCpu);
        plain.Step();
        specCpu.Step();
        Assert.Equal(0x3000u, plain.Context.Eip);
        Assert.Equal(0x3000u, specCpu.Context.Eip);
        Assert.Equal(1, plain.InterruptCount);
        Assert.Equal(1, specCpu.InterruptCount);

        Assert.Equal(plain.Execute(16), specCpu.Execute(16));
        AssertSameState(plain, specCpu);
        Assert.Equal(1, specCpu.InterruptCount);
        return;

        static XboxCpu CreateIrq(bool spec, byte[] bytes, CountedInterruptSource source)
        {
            XboxCpu cpu = Create(spec, bytes);
            cpu.Ram().Write8(0x3000, 0xCF);
            InstallIdt(cpu, 0x21, 0x3000);
            cpu.Context.Eflags |= FlagBits.If;
            cpu.InterruptSource = source;
            return cpu;
        }
    }

    [Fact]
    public void Irq_IfFlipMidBlock_MatchesInterpreter()
    {
        // IF starts clear with an always-firing source: delivery waits for the
        // STI (a fallback terminator), then lands before the following member.
        byte[] code = [0x90, 0x90, 0xFB, 0x90, 0x90, 0xF4];

        XboxCpu plain = CreateSti(false, code);
        XboxCpu specCpu = CreateSti(true, code);

        // Two NOPs with IF clear (no poll observes the source), STI sets IF,
        // then delivery lands before the following member.
        for (int i = 0; i < 3; i++)
        {
            plain.Step();
            specCpu.Step();
            AssertSameState(plain, specCpu);
        }

        Assert.Equal(0x1003u, specCpu.Context.Eip);
        plain.Step();
        specCpu.Step();
        Assert.Equal(0x3000u, plain.Context.Eip);
        Assert.Equal(0x3000u, specCpu.Context.Eip);

        Assert.Equal(plain.Execute(16), specCpu.Execute(16));
        AssertSameState(plain, specCpu);
        Assert.Equal(1, specCpu.InterruptCount);
        return;

        static XboxCpu CreateSti(bool spec, byte[] bytes)
        {
            XboxCpu cpu = Create(spec, bytes);
            cpu.Ram().Write8(0x3000, 0xCF);
            InstallIdt(cpu, 0x21, 0x3000);
            cpu.Context.Eflags &= ~FlagBits.If;
            cpu.InterruptSource = new CountedInterruptSource(0x21, fireAt: 1);
            return cpu;
        }
    }

    [Fact]
    public void EveryStepAndBudget_MatchesCache_StateAndInstructionCounts()
    {
        XboxCpu plain = Create(false, Loop);
        XboxCpu specCpu = Create(true, Loop);
        var plainProfile = new HotspotProfiler();
        var specProfile = new HotspotProfiler();
        plain.HotspotObserver = plainProfile;
        specCpu.HotspotObserver = specProfile;
        for (int i = 0; i < 200; i++)
        {
            if ((i & 1) == 0)
            {
                plain.Step();
                specCpu.Step();
            }
            else
            {
                Assert.Equal(plain.Execute(i), specCpu.Execute(i));
            }

            AssertSameState(plain, specCpu);
            Assert.Equal(plain.DecodeCacheHits, specCpu.DecodeCacheHits);
            Assert.Equal(plain.DecodeCount, specCpu.DecodeCount);
            Assert.Equal(plainProfile.TotalInstructions, specProfile.TotalInstructions);
        }

        Assert.True(specCpu.SpecStatistics!.ShapeHits > 1000);
        Assert.True(specCpu.SpecStatistics.SpecRan > 1000);
    }

    [Fact]
    public void Verify_RejectsStaleMemberBeforeExecution()
    {
        XboxCpu cpu = Create(true, Loop);
        cpu.BlockVerify = true;
        cpu.Execute(5);
        cpu.Step();
        cpu.Ram().Write8(0x1001, 0x90);
        cpu.Step();
        Assert.Equal(0x1002u, cpu.Context.Eip);
        Assert.Equal(1UL, cpu.SpecStatistics!.VerifyFailures);
        Assert.Equal(1UL, cpu.DecodeCacheVerifyFailures);
    }

    [Fact]
    public void Cap_BoundsArraysAndInvalidatesCursor_WithSpec()
    {
        var blocks = new DecodedBlockCache { SpecEnabled = true };
        var decodes = new DecodeCache { Blocks = blocks };
        for (uint i = 0; i <= DecodedBlockCache.MaxBlocks; i++)
        {
            uint address = (i + 1) * 32;
            Fill(decodes, address, 0x90);
            Fill(decodes, address + 1, 0xC3);
            Assert.True(blocks.TryNext(decodes, address, 32, 32, out _));
        }

        Assert.Equal(DecodedBlockCache.MaxBlocks, blocks.PeakCount);
        Assert.Equal(1UL, blocks.CapFlushes);
        Assert.Equal(1, blocks.Count);
        Assert.True(blocks.TryNext(decodes, 0x1000, 32, 32, out _));
        Assert.NotNull(blocks.CurrentHandler);
        Assert.True(blocks.ShapeHits > 0);
    }

    [Fact]
    public void ModeChange_RedirectsCursor_MatchesInterpreter()
    {
        XboxCpu plain = Create(false, Loop);
        XboxCpu specCpu = Create(true, Loop);
        Assert.Equal(plain.Execute(32), specCpu.Execute(32));
        plain.Context.CodeSegment32 = false;
        specCpu.Context.CodeSegment32 = false;
        for (int i = 0; i < 8; i++)
        {
            plain.Step();
            specCpu.Step();
            AssertSameState(plain, specCpu);
        }

        Assert.True(specCpu.SpecStatistics!.Redirects > 0);
    }

    [Fact]
    public void FlagOff_LeavesSpecDisabled()
    {
        XboxCpu cpu = CpuHarness.Create(Loop);
        Assert.False(cpu.SpecExecutionEnabled);
        Assert.Null(cpu.SpecStatistics);
        Assert.Null(cpu.BlockStatistics);
        cpu.Execute(16);
        Assert.False(cpu.SpecExecutionEnabled);
    }

    [Fact]
    public void FallbackHistogram_TalliesServeTimeShapes()
    {
        // Slice 15: shl r,cl was the last uncovered shape in this loop —
        // now every member specializes (inc/xor-imm/dec/nop/shl-cl/jmp),
        // so the loop runs with zero fallbacks. The histogram still
        // tallies serve-time shapes: with nothing uncovered the total and
        // the map must be empty (and this doubles as a regression pin
        // that shl-cl stays covered — un-resolving it fails the zeros).
        byte[] code = [0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xD3, 0xE0, 0xEB, 0xF7];
        XboxCpu cpu = Create(true, code);
        cpu.Execute(64);
        DecodedBlockCache stats = cpu.SpecStatistics!;
        Assert.Equal(0UL, stats.ShapeFallbacks);
        Assert.Empty(stats.FallbackShapes);
        Assert.Empty(stats.TopFallbackShapes(8));
    }

    [Fact]
    public void FallbackHistogram_EmptyWhenSpecDisabled()
    {
        var blocks = new DecodedBlockCache();
        var decodes = new DecodeCache { Blocks = blocks };
        Fill(decodes, 0x1000, 0xEB);
        Fill(decodes, 0x1002, 0x90);
        Assert.True(blocks.TryNext(decodes, 0x1000, 32, 32, out _));
        Assert.True(blocks.TryNext(decodes, 0x1002, 32, 32, out _));
        Assert.Equal(0UL, blocks.ShapeFallbacks);
        Assert.Empty(blocks.FallbackShapes);
        Assert.Empty(blocks.TopFallbackShapes(8));
    }

    [Fact]
    public void ServedShapeTally_UnitLevel_CountsServedMembersOnlyWhenEnabled()
    {
        // inc eax; nop — both specialized; the tally must count both serves
        // under the flag and stay empty without it (block formation itself
        // must not tally).
        var off = new DecodedBlockCache { SpecEnabled = true };
        var offDecodes = new DecodeCache { Blocks = off };
        Fill(offDecodes, 0x1000, 0x40);
        Fill(offDecodes, 0x1001, 0x90);
        Assert.True(off.TryNext(offDecodes, 0x1000, 32, 32, out _));
        Assert.True(off.TryNext(offDecodes, 0x1001, 32, 32, out _));
        Assert.Equal(2UL, off.ShapeHits);
        Assert.Empty(off.ServedShapes);
        Assert.Empty(off.TopServedShapes(8));

        var on = new DecodedBlockCache { SpecEnabled = true, ShapeTallyEnabled = true };
        var onDecodes = new DecodeCache { Blocks = on };
        Fill(onDecodes, 0x1000, 0x40);
        Fill(onDecodes, 0x1001, 0x90);
        Assert.True(on.TryNext(onDecodes, 0x1000, 32, 32, out _));
        Assert.True(on.TryNext(onDecodes, 0x1001, 32, 32, out _));
        Assert.Equal(2UL, on.ShapeHits);
        Assert.Equal(2, on.ServedShapes.Count);
        var top = on.TopServedShapes(8);
        Assert.Equal(2, top.Length);
        Assert.Equal(1UL, top[0].Count);
        Assert.Equal(1UL, top[1].Count);
    }

    [Fact]
    public void ServedShapeTally_DefaultOff_LeavesHistogramEmpty()
    {
        // The slice-15 loop specializes every member; without the flag the
        // serve path must not tally anything (and the run stays exact).
        byte[] code = [0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xD3, 0xE0, 0xEB, 0xF7];
        XboxCpu cpu = Create(true, code);
        cpu.Execute(64);
        DecodedBlockCache stats = cpu.SpecStatistics!;
        Assert.True(stats.ShapeHits > 0);
        Assert.Empty(stats.ServedShapes);
        Assert.Empty(stats.TopServedShapes(8));
    }

    [Fact]
    public void ServedShapeTally_TotalsEqualShapeHits_AndStateStaysIdentical()
    {
        byte[] code = [0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xD3, 0xE0, 0xEB, 0xF7];

        XboxCpu off = Create(true, code);
        off.Execute(64);

        XboxCpu on = Create(true, code);
        on.SpecStatistics!.ShapeTallyEnabled = true;
        on.Execute(64);

        DecodedBlockCache stats = on.SpecStatistics!;
        ulong total = 0;
        foreach (var entry in stats.ServedShapes)
        {
            total += entry.Value;
        }

        Assert.True(stats.ShapeHits > 0);
        Assert.Equal(stats.ShapeHits, total);
        Assert.NotEmpty(stats.TopServedShapes(8));
        Assert.Equal(0UL, stats.ShapeFallbacks);

        // Diagnostic only: enabling the tally changes no guest state.
        AssertSameState(off, on);
    }

    private static void RunShapes(params (string Name, byte[] Code)[] shapes)
    {
        foreach ((string name, byte[] code) in shapes)
        {
            byte[] withHalt = new byte[code.Length + 1];
            code.CopyTo(withHalt, 0);
            withHalt[^1] = 0xF4;

            XboxCpu plain = Create(false, withHalt);
            XboxCpu specCpu = Create(true, withHalt);
            Preset(plain);
            Preset(specCpu);
            try
            {
                Assert.Equal(plain.Execute(16), specCpu.Execute(16));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Shape '{name}' diverged in run", exception);
            }

            try
            {
                AssertSameState(plain, specCpu);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Shape '{name}' diverged in state", exception);
            }

            Assert.True(plain.Context.Halted && specCpu.Context.Halted);
        }
    }

    private static XboxCpu Create(bool spec, byte[] code)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        cpu.DecodeCacheEnabled = true;
        if (spec)
        {
            cpu.SpecExecutionEnabled = true;
        }

        return cpu;
    }

    private static void Preset(XboxCpu cpu)
    {
        cpu.Context.Eax = 0x11111111;
        cpu.Context.Ebx = 0x00002000;
        cpu.Context.Ecx = 0x33333333;
        cpu.Context.Edx = 0x44444444;
        cpu.Context.Esi = 0x55555555;
        cpu.Context.Edi = 0x66666666;
        cpu.Context.Ebp = 0x00007000;
        cpu.Context.Eflags = FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Zf;
        cpu.Ram().Write32(0x2000, 0xAABBCCDD);
        cpu.Ram().Write32(0x5000, 0x11223344);
        cpu.Context.SetSegment(SegmentRegister.Fs, 0x20, 0x3000);
    }

    private static void AssertSameState(XboxCpu expected, XboxCpu actual)
    {
        Assert.Equal(expected.Context.Eip, actual.Context.Eip);
        Assert.Equal(expected.Context.Eflags, actual.Context.Eflags);
        Assert.Equal(expected.Context.Eax, actual.Context.Eax);
        Assert.Equal(expected.Context.Ebx, actual.Context.Ebx);
        Assert.Equal(expected.Context.Ecx, actual.Context.Ecx);
        Assert.Equal(expected.Context.Edx, actual.Context.Edx);
        Assert.Equal(expected.Context.Esi, actual.Context.Esi);
        Assert.Equal(expected.Context.Edi, actual.Context.Edi);
        Assert.Equal(expected.Context.Ebp, actual.Context.Ebp);
        Assert.Equal(expected.Context.Esp, actual.Context.Esp);
        Assert.Equal(expected.Context.Tsc, actual.Context.Tsc);
        Assert.Equal(expected.Context.Halted, actual.Context.Halted);
        Assert.Equal(expected.Context.Cr2, actual.Context.Cr2);
        Assert.Equal(expected.InterruptCount, actual.InterruptCount);
        Assert.Equal(expected.LastFaultVector, actual.LastFaultVector);
        Assert.Equal(expected.LastFaultEip, actual.LastFaultEip);
        Assert.Equal(expected.LastFaultErrorCode, actual.LastFaultErrorCode);
        Assert.Equal(StateBytes(expected), StateBytes(actual));

        PhysicalMemory plainRam = expected.Ram();
        PhysicalMemory specRam = actual.Ram();
        for (uint address = 0; address < 0x10000; address += 4)
        {
            Assert.Equal(plainRam.Read32(address), specRam.Read32(address));
        }
    }

    private static byte[] StateBytes(XboxCpu cpu)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            cpu.SaveState(writer);
        }

        return stream.ToArray();
    }

    private static void Fill(DecodeCache cache, uint linear, byte opcode)
    {
        Span<byte> bytes = stackalloc byte[15];
        bytes.Clear();
        bytes[0] = opcode;
        cache.Insert(linear, Decoder.Decode(bytes), linear, 0, 0, bytes, 32, 32);
    }

    private static void MapIdentity(AddressSpace bus, uint firstVpn, uint count)
    {
        bus.Write32(PageDirectory, PageTable | 3u);
        for (uint vpn = firstVpn; vpn < firstVpn + count; vpn++)
        {
            bus.Write32(PageTable + vpn * 4, (vpn << 12) | 3u);
        }
    }

    private static void EnablePaging(XboxCpu cpu)
    {
        cpu.Context.Cr3 = PageDirectory;
        cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;
        cpu.Mmu.Flush();
    }

    private static void InstallIdt(XboxCpu cpu, int vector, uint handler)
    {
        const uint idt = 0x4000;
        uint gate = idt + (uint)vector * 8;
        cpu.Bus().Write16(gate, (ushort)(handler & 0xFFFF));
        cpu.Bus().Write16(gate + 2, 0x08);
        cpu.Bus().Write16(gate + 4, 0x8E00);
        cpu.Bus().Write16(gate + 6, (ushort)(handler >> 16));
        cpu.Context.Idtr.Base = idt;
        cpu.Context.Idtr.Limit = 0x7FF;
    }

    /// <summary>Fires one vector at the Nth acknowledge.</summary>
    private sealed class CountedInterruptSource(int vector, int fireAt) : IInterruptSource
    {
        private int _acks;

        public int Acknowledge()
        {
            _acks++;
            return _acks == fireAt ? vector : -1;
        }
    }
}
