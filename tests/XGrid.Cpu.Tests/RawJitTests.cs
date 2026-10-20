using System;
using XGrid.Memory;
using Xunit;
namespace XGrid.Cpu.Tests;

/// <summary>
/// C3 raw-emitter differentials: the same programs the IL block JIT is held to
/// (interpreter parity over registers, flags, memory, stack and fault paths),
/// run with <see cref="XboxCpu.JitRawBlocks"/> enabled so the native runner is
/// the one under test.
/// </summary>
public sealed class RawJitTests
{
    private static XboxCpu CreateRaw(byte[] code)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        cpu.DecodeCacheEnabled = true;
        cpu.SpecExecutionEnabled = true;
        cpu.JitSpecSlice = int.MaxValue;
        cpu.JitBuildThreshold = Environment.GetEnvironmentVariable("XGRID_RAW_THRESHOLD") is { } rawThreshold
            ? int.Parse(rawThreshold, System.Globalization.CultureInfo.InvariantCulture)
            : 1;
        cpu.JitEnabled = true;
        cpu.JitRawBlocks = true;
        return cpu;
    }

    private static XboxCpu CreatePlain(byte[] code) => CpuHarness.Create(code);

    private static void Preset(XboxCpu cpu)
    {
        cpu.Context.Eax = 0x11111111;
        cpu.Context.Ebx = 0x00002000;
        cpu.Context.Ecx = 0x33333333;
        cpu.Context.Edx = 0x44444444;
        cpu.Context.Esi = 0x55555555;
        cpu.Context.Edi = 0x66666666;
        cpu.Context.Ebp = 0x00007000;
        cpu.Context.Esp = CpuHarness.StackAddress;
        cpu.Context.Eflags = FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Zf;
    }

    private static void AssertSame(XboxCpu expected, XboxCpu actual)
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
    }

    [Fact]
    public void AluLoop_MatchesInterpreter()
    {
        // mov ecx,5; mov eax,1; mov ebx,0x10
        // loop: add eax,ebx; xor edx,edx; sub eax,2; nop; dec ecx; jne loop; hlt
        byte[] code =
        [
            0xB9, 0x05, 0x00, 0x00, 0x00,
            0xB8, 0x01, 0x00, 0x00, 0x00,
            0xBB, 0x10, 0x00, 0x00, 0x00,
            0x01, 0xD8,
            0x31, 0xD2,
            0x2D, 0x02, 0x00, 0x00, 0x00,
            0x90,
            0x49,
            0x75, 0xF3,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        Preset(plain);
        Preset(raw);
        plain.Execute(100_000);
        raw.Execute(100_000);
        AssertSame(plain, raw);
        Assert.True(raw.Context.Halted);
        Assert.True(raw.JitRawBlocksBuilt > 0, "no raw blocks were built");
        Assert.True(raw.JitRawInvocations > 0, "no raw block ran");
    }

    [Fact]
    public void MemoryForms_MatchesInterpreter()
    {
        // mov eax,[0x2000]; add eax,[0x2004]; mov [0x2008],eax; inc dword [0x200C];
        // mov ecx,[0x2008]; cmp ecx,[0x2000]; sete al; hlt
        byte[] code =
        [
            0xA1, 0x00, 0x20, 0x00, 0x00,
            0x03, 0x05, 0x04, 0x20, 0x00, 0x00,
            0xA3, 0x08, 0x20, 0x00, 0x00,
            0xFF, 0x05, 0x0C, 0x20, 0x00, 0x00,
            0x8B, 0x0D, 0x08, 0x20, 0x00, 0x00,
            0x3B, 0x0D, 0x00, 0x20, 0x00, 0x00,
            0x0F, 0x94, 0xC0,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x2000, 0x00010000);
            cpu.Ram().Write32(0x2004, 0x00000004);
            cpu.Ram().Write32(0x200C, 7);
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(10_000);
        raw.Execute(10_000);
        AssertSame(plain, raw);
        Assert.Equal(plain.Ram().Read32(0x2008), raw.Ram().Read32(0x2008));
        Assert.Equal(plain.Ram().Read32(0x200C), raw.Ram().Read32(0x200C));
    }

    [Fact]
    public void StackPushPop_MatchesInterpreter()
    {
        // push eax; push ebx; pop ecx; pop edx; hlt
        byte[] code =
        [
            0x50,
            0x53,
            0x59,
            0x5A,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
    }

    [Fact]
    public void CallRetPushImmPop_MatchesInterpreter()
    {
        // mov esi,2
        // loop: mov eax,0x1234; push 0x11223344; call func; nop; dec esi; jne loop
        // hlt; nop
        // func: mov eax,[esp+4]; push eax; pop edx; ret 4
        byte[] code =
        [
            0xBE, 0x02, 0x00, 0x00, 0x00,
            0xB8, 0x34, 0x12, 0x00, 0x00,
            0x68, 0x44, 0x33, 0x22, 0x11,
            0xE8, 0x06, 0x00, 0x00, 0x00,
            0x90,
            0x4E,
            0x75, 0xED,
            0xF4,
            0x90,
            0x8B, 0x44, 0x24, 0x04,
            0x50,
            0x5A,
            0xC2, 0x04, 0x00,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        Preset(plain);
        Preset(raw);
        plain.Execute(10_000);
        raw.Execute(10_000);
        AssertSame(plain, raw);
        Assert.Equal(0x11223344u, raw.Context.Eax);
        Assert.Equal(0x11223344u, raw.Context.Edx);
        Assert.Equal(CpuHarness.StackAddress, raw.Context.Esp);
        Assert.True(raw.JitRawInvocations > 0, "no raw block ran");
    }

    [Fact]
    public void RetNear_MatchesInterpreter()
    {
        // push 0x100B; ret; nop x5; hlt
        byte[] code =
        [
            0x68, 0x0B, 0x10, 0x00, 0x00,
            0xC3,
            0x90, 0x90, 0x90, 0x90, 0x90,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.True(raw.Context.Halted);
        Assert.Equal(CpuHarness.StackAddress, raw.Context.Esp);
    }

    [Fact]
    public void MovzxMovsxMemory_MatchesInterpreter()
    {
        // movzx eax,byte [0x2000]; movsx ebx,byte [0x2001];
        // movzx ecx,word [0x2002]; movsx edx,word [0x2004]; hlt
        byte[] code =
        [
            0x0F, 0xB6, 0x05, 0x00, 0x20, 0x00, 0x00,
            0x0F, 0xBE, 0x1D, 0x01, 0x20, 0x00, 0x00,
            0x0F, 0xB7, 0x0D, 0x02, 0x20, 0x00, 0x00,
            0x0F, 0xBF, 0x15, 0x04, 0x20, 0x00, 0x00,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write8(0x2000, 0xFE);
            cpu.Ram().Write8(0x2001, 0x80);
            cpu.Ram().Write16(0x2002, 0x8001);
            cpu.Ram().Write16(0x2004, 0xFFF0);
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.Equal(0xFEu, raw.Context.Eax);
        Assert.Equal(0xFFFFFF80u, raw.Context.Ebx);
        Assert.Equal(0x8001u, raw.Context.Ecx);
        Assert.Equal(0xFFFFFFF0u, raw.Context.Edx);
    }

    [Fact]
    public void Prefetch_IsANoop_MatchesInterpreter()
    {
        // prefetchnta [0x2000]; prefetch [0x2004]; hlt
        byte[] code =
        [
            0x0F, 0x18, 0x05, 0x00, 0x20, 0x00, 0x00,
            0x0F, 0x18, 0x0D, 0x04, 0x20, 0x00, 0x00,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.True(raw.Context.Halted);
    }

    [Fact]
    public void FpuMemoryArithmetic_MatchesInterpreter()
    {
        // loop 3x: fld [0x2000]; fadd [0x2004]; fmul [0x2008]; fsub [0x200C];
        // fdiv [0x2010]; fstp [0x2014]; dec ecx; jne loop; hlt
        byte[] code =
        [
            0xB9, 0x03, 0x00, 0x00, 0x00,
            0xD9, 0x05, 0x00, 0x20, 0x00, 0x00,
            0xD8, 0x05, 0x04, 0x20, 0x00, 0x00,
            0xD8, 0x0D, 0x08, 0x20, 0x00, 0x00,
            0xD8, 0x25, 0x0C, 0x20, 0x00, 0x00,
            0xD8, 0x35, 0x10, 0x20, 0x00, 0x00,
            0xD9, 0x1D, 0x14, 0x20, 0x00, 0x00,
            0x49,
            0x75, 0xD9,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x2000, BitConverter.SingleToUInt32Bits(2.0f));
            cpu.Ram().Write32(0x2004, BitConverter.SingleToUInt32Bits(3.0f));
            cpu.Ram().Write32(0x2008, BitConverter.SingleToUInt32Bits(4.0f));
            cpu.Ram().Write32(0x200C, BitConverter.SingleToUInt32Bits(1.0f));
            cpu.Ram().Write32(0x2010, BitConverter.SingleToUInt32Bits(2.0f));
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.Equal(plain.Ram().Read32(0x2014), raw.Ram().Read32(0x2014));
        Assert.Equal(BitConverter.SingleToUInt32Bits(9.5f), raw.Ram().Read32(0x2014));
    }

    [Fact]
    public void FpuRegisterArithmetic_MatchesInterpreter()
    {
        // fld [0x2000] (2.0); fld [0x2004] (3.0); fadd st0,st1; fmul st0,st1;
        // faddp st1,st0; fstp [0x2008]; hlt
        byte[] code =
        [
            0xBE, 0x02, 0x00, 0x00, 0x00,
            0xD9, 0x05, 0x00, 0x20, 0x00, 0x00,
            0xD9, 0x05, 0x04, 0x20, 0x00, 0x00,
            0xD8, 0xC1,
            0xD8, 0xC9,
            0xDE, 0xC1,
            0xD9, 0x1D, 0x08, 0x20, 0x00, 0x00,
            0x4E,
            0x75, 0xE5,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x2000, BitConverter.SingleToUInt32Bits(2.0f));
            cpu.Ram().Write32(0x2004, BitConverter.SingleToUInt32Bits(3.0f));
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.Equal(BitConverter.SingleToUInt32Bits(12.0f), raw.Ram().Read32(0x2008));
        Assert.Equal(plain.Ram().Read32(0x2008), raw.Ram().Read32(0x2008));
        Assert.True(raw.JitRawInvocations > 0, "no raw block ran");
    }

    [Fact]
    public void FpuIntegerLoadStore_MatchesInterpreter()
    {
        // fild dword [0x2000]; fistp dword [0x2004]; fild dword [0x2008];
        // fistp dword [0x200C]; hlt
        byte[] code =
        [
            0xBE, 0x02, 0x00, 0x00, 0x00,
            0xDB, 0x05, 0x00, 0x20, 0x00, 0x00,
            0xDB, 0x1D, 0x04, 0x20, 0x00, 0x00,
            0xDB, 0x05, 0x08, 0x20, 0x00, 0x00,
            0xDB, 0x1D, 0x0C, 0x20, 0x00, 0x00,
            0x4E,
            0x75, 0xE5,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x2000, 0x80000001);
            cpu.Ram().Write32(0x2008, 0x7FFFFFFF);
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.Equal(plain.Ram().Read32(0x2004), raw.Ram().Read32(0x2004));
        Assert.Equal(plain.Ram().Read32(0x200C), raw.Ram().Read32(0x200C));
        Assert.True(raw.JitRawInvocations > 0, "no raw block ran");
    }

    [Fact]
    public void ShiftsAndRotates_MatchesInterpreter()
    {
        // mov esi,2; mov ecx,5;
        // loop: rol eax,1; ror eax,2; rol eax,5; shl ebx,cl; shr ebx,cl;
        // sar ebx,cl; rol edx,cl; ror edx,cl; dec esi; jne loop; hlt
        byte[] code =
        [
            0xBE, 0x02, 0x00, 0x00, 0x00,
            0xB9, 0x05, 0x00, 0x00, 0x00,
            0xB8, 0x01, 0x00, 0x00, 0x80,
            0xC1, 0xC0, 0x01,
            0xC1, 0xC8, 0x02,
            0xC1, 0xC0, 0x05,
            0xBB, 0x78, 0x56, 0x34, 0x12,
            0xD3, 0xE3,
            0xD3, 0xEB,
            0xD3, 0xFB,
            0xBA, 0xF0, 0xDE, 0xBC, 0x9A,
            0xD3, 0xC2,
            0xD3, 0xCA,
            0x4E,
            0x75, 0xDB,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.True(raw.Context.Halted);
        Assert.True(raw.JitRawInvocations > 0, "no raw block ran");
    }

    [Fact]
    public void ShiftByCl_AndRotate_DoNotUseHelperMembers()
    {
        // mov esi,2; mov ecx,3; mov eax,0x80000001; rol eax,1; ror eax,2;
        // shl eax,cl; shr eax,cl; sar eax,cl; dec esi; jne; hlt
        byte[] code =
        [
            0xBE, 0x02, 0x00, 0x00, 0x00,
            0xB9, 0x03, 0x00, 0x00, 0x00,
            0xB8, 0x01, 0x00, 0x00, 0x80,
            0xC1, 0xC0, 0x01,
            0xC1, 0xC8, 0x02,
            0xD3, 0xE0,
            0xD3, 0xE8,
            0xD3, 0xF8,
            0x4E,
            0x75, 0xEC,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        raw.RawShapeTally = true;
        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Rol]);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Ror]);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Shl]);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Shr]);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Sar]);
    }

    [Fact]
    public void SseScalarForms_DoNotUseHelperMembers()
    {
        // mov esi,2; movss xmm0,[0x2000]; mulss xmm0,xmm1; addss xmm0,[0x2004];
        // subss xmm0,xmm2; movss [0x2008],xmm0; cvttss2si eax,xmm0;
        // cvttss2si ebx,[0x2000]; dec esi; jne; hlt
        byte[] code =
        [
            0xBE, 0x02, 0x00, 0x00, 0x00,
            0xF3, 0x0F, 0x10, 0x05, 0x00, 0x20, 0x00, 0x00,
            0xF3, 0x0F, 0x59, 0xC1,
            0xF3, 0x0F, 0x58, 0x05, 0x04, 0x20, 0x00, 0x00,
            0xF3, 0x0F, 0x5C, 0xC2,
            0xF3, 0x0F, 0x11, 0x05, 0x08, 0x20, 0x00, 0x00,
            0xF3, 0x0F, 0x2C, 0xC0,
            0xF3, 0x0F, 0x2C, 0x1D, 0x00, 0x20, 0x00, 0x00,
            0x4E,
            0x75, 0xD1,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        raw.RawShapeTally = true;
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x2000, BitConverter.SingleToUInt32Bits(3.5f));
            cpu.Ram().Write32(0x2004, BitConverter.SingleToUInt32Bits(1.5f));
            cpu.Context.WriteXmmElement(1, 0, BitConverter.SingleToUInt32Bits(2.0f));
            cpu.Context.WriteXmmElement(2, 0, BitConverter.SingleToUInt32Bits(0.5f));
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.Equal(plain.Ram().Read32(0x2008), raw.Ram().Read32(0x2008));
        Assert.Equal(plain.Context.Eax, raw.Context.Eax);
        Assert.Equal(plain.Context.Ebx, raw.Context.Ebx);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Mulss]);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Addss]);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Subss]);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Movss]);
        Assert.Equal(0, raw.RawHelperShapeCounts[(int)Mnemonic.Cvttss2Si]);
    }

    [Fact]
    public void MmxMovqMovntq_MatchesInterpreter()
    {
        // mov esi,2; movq mm0,[0x2000]; movq [0x2008],mm0;
        // movntq [0x2010],mm0; movq mm1,[0x2004]; movq [0x2020],mm1;
        // dec esi; jne; hlt
        byte[] code =
        [
            0xBE, 0x02, 0x00, 0x00, 0x00,
            0x0F, 0x6F, 0x05, 0x00, 0x20, 0x00, 0x00,
            0x0F, 0x7F, 0x05, 0x08, 0x20, 0x00, 0x00,
            0x0F, 0xE7, 0x05, 0x10, 0x20, 0x00, 0x00,
            0x0F, 0x6F, 0x0D, 0x04, 0x20, 0x00, 0x00,
            0x0F, 0x7F, 0x0D, 0x20, 0x20, 0x00, 0x00,
            0x4E,
            0x75, 0xDA,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x2000, 0x11223344);
            cpu.Ram().Write32(0x2004, 0x55667788);
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        foreach (uint address in new uint[] { 0x2008, 0x2010, 0x2020 })
        {
            Assert.Equal(plain.Ram().Read32(address), raw.Ram().Read32(address));
        }

        Assert.True(raw.JitRawInvocations > 0, "no raw block ran");
    }

    [Fact]
    public void FlagsAcrossConditionals_MatchesInterpreter()
    {
        // mov eax,0x80000000; mov ebx,1; add eax,ebx; jno skip; mov ecx,1;
        // skip: sub eax,ebx; jc done; mov edx,2; done: hlt
        byte[] code =
        [
            0xB8, 0x00, 0x00, 0x00, 0x80,
            0xBB, 0x01, 0x00, 0x00, 0x00,
            0x01, 0xD8,
            0x71, 0x05,
            0xB9, 0x01, 0x00, 0x00, 0x00,
            0x29, 0xD8,
            0x72, 0x05,
            0xBA, 0x02, 0x00, 0x00, 0x00,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        Preset(plain);
        Preset(raw);
        plain.Execute(10_000);
        raw.Execute(10_000);
        AssertSame(plain, raw);
    }

    [Fact]
    public void MemoryImmediateForms_MatchesInterpreter()
    {
        // add dword [0x2010],3; and byte [0x2014],0x0F; dec byte [0x2015];
        // test dword [0x2018],1; hlt
        byte[] code =
        [
            0x81, 0x05, 0x10, 0x20, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00,
            0x80, 0x25, 0x14, 0x20, 0x00, 0x00, 0x0F,
            0xFE, 0x0D, 0x15, 0x20, 0x00, 0x00,
            0xF7, 0x05, 0x18, 0x20, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x2010, 0x7FFFFFFF);
            cpu.Ram().Write16(0x2014, 0xAAFF);
            cpu.Ram().Write32(0x2018, 0x00000003);
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(1000);
        raw.Execute(1000);
        AssertSame(plain, raw);
        Assert.Equal(plain.Ram().Read32(0x2010), raw.Ram().Read32(0x2010));
        Assert.Equal(plain.Ram().Read16(0x2014), raw.Ram().Read16(0x2014));
        Assert.Equal(plain.Ram().Read32(0x2018), raw.Ram().Read32(0x2018));
    }

    [Fact]
    public void CmpMemoryImmediate_MatchesInterpreter()
    {
        // mov ecx,4; cmp dword [0x200C],5; jne +1; nop; dec ecx; jne loop; hlt
        byte[] code =
        [
            0xB9, 0x04, 0x00, 0x00, 0x00,
            0x83, 0x3D, 0x0C, 0x20, 0x00, 0x00, 0x05,
            0x75, 0x01,
            0x90,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x200C, 0x00000005);
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(10_000);
        raw.Execute(10_000);
        AssertSame(plain, raw);
    }
    [Fact]
    public void BlockEndingInMemoryRmw_AdvancesEipOnce()
    {
        // A full 32-member block whose last member is a memory read-modify-write.
        // The RMW's slow path runs the spec handler (which advances EIP); the
        // fast path must advance EIP itself. If it does not, the block exits on
        // the RMW's first byte and the member path re-executes it: the inc
        // doubles and the step/TSC count drifts by one per block. The loop
        // makes the head reach the build threshold so the raw block exists, and
        // the head sits exactly on the xor so the 32-member cap lands on the RMW.
        byte[] code = new byte[5 + 2 + 30 + 6 + 3 + 1];
        code[0] = 0xB9; // mov ecx,2
        code[1] = 0x02;
        code[2] = 0x00;
        code[3] = 0x00;
        code[4] = 0x00;
        code[5] = 0x31; // start: xor eax,eax
        code[6] = 0xC0;
        for (int i = 0; i < 30; i++)
        {
            code[7 + i] = 0x40; // inc eax
        }

        int at = 37;
        code[at++] = 0xFF; // inc dword [0x2000]
        code[at++] = 0x05;
        code[at++] = 0x00;
        code[at++] = 0x20;
        code[at++] = 0x00;
        code[at++] = 0x00;
        code[at++] = 0x49; // dec ecx
        code[at++] = 0x75; // jne start
        code[at++] = 0xD7;
        code[at] = 0xF4; // hlt

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            // Paging on (the fast probe's data translate cache only arms on
            // the paged path): identity-map the first 16 pages.
            cpu.Bus().Write32(0x4000, 0x5000u | 3u);
            for (uint vpn = 0; vpn < 16; vpn++)
            {
                cpu.Bus().Write32(0x5000 + vpn * 4, (vpn << 12) | 3u);
            }

            cpu.Context.Cr3 = 0x4000;
            cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;
            cpu.Mmu.Flush();
            cpu.Ram().Write32(0x2000, 5);
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(10_000);
        raw.Execute(10_000);
        AssertSame(plain, raw);
        Assert.Equal(7u, raw.Ram().Read32(0x2000));
        Assert.True(
            raw.JitRawBlocksBuilt > 0,
            $"no raw blocks were built (jitBlocks={raw.JitBlocksBuilt} calls={raw.JitCallCount} " +
            $"inv={raw.JitInvocations} rejected={raw.JitRejectedHeads} transient={raw.JitTransientBails} " +
            $"served={raw.JitServedTotal})");
        Assert.True(raw.JitRawInvocations > 0, "no raw block ran");
    }

    // The post-helper TSC rebase was removed in S199f (the frame slot is
    // authoritative; helpers never tick TSC), so this loop is a full
    // differential including Context.Tsc.
    [Fact]
    public void CmpMemoryImmediateLoop_MatchesInterpreter()
    {
        // mov ecx,4; loop: cmp dword [0x200C],5; jne +1; nop; dec ecx; jne loop; hlt
        byte[] code =
        [
            0xB9, 0x04, 0x00, 0x00, 0x00,
            0x83, 0x3D, 0x0C, 0x20, 0x00, 0x00, 0x05,
            0x75, 0x01,
            0x90,
            0x49,
            0x75, 0xF3,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x200C, 0x00000005);
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(10_000);
        raw.Execute(10_000);
        AssertSame(plain, raw);
    }
    [Fact]
    public void HelperClobberedCallerSavedRegisters_AreReloaded()
    {
        // mov esi,2
        // loop: mov ecx,5; mov edx,7; movq mm0,[0x2000]  (helper call: the
        // Win64 ABI clobbers host RAX/RCX/RDX, which hold guest eax/ecx/edx);
        // add edx,ecx; mov [0x2004],edx; dec esi; jne loop; hlt
        byte[] code =
        [
            0xBE, 0x02, 0x00, 0x00, 0x00,
            0xB9, 0x05, 0x00, 0x00, 0x00,
            0xBA, 0x07, 0x00, 0x00, 0x00,
            0x0F, 0x6F, 0x05, 0x00, 0x20, 0x00, 0x00,
            0x01, 0xCA,
            0x89, 0x15, 0x04, 0x20, 0x00, 0x00,
            0x4E,
            0x75, 0xE4,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x2000, 0x11223344);
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(10_000);
        raw.Execute(10_000);
        AssertSame(plain, raw);
        Assert.Equal(plain.Ram().Read32(0x2004), raw.Ram().Read32(0x2004));
        Assert.Equal(12u, raw.Ram().Read32(0x2004));
        Assert.True(raw.JitRawInvocations > 0, "no raw block ran");
    }

    [Fact]
    public void ChainedBlocks_MatchInterpreter_AndCountChains()
    {
        // 0x1000: jmp 0x1003 ; 0x1003: inc eax ; jmp 0x1000.
        // Both blocks are store-free, so once formed they chain into each
        // other (the chain edge validates generation/budget/pending IRQ).
        byte[] code =
        [
            0xEB, 0x01,
            0x90,
            0x40,
            0xEB, 0xFA,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        Preset(plain);
        Preset(raw);
        plain.Execute(5000);
        raw.Execute(5000);
        AssertSame(plain, raw);
        Assert.True(raw.JitRawChainedBlocks > 0, "no block chains fired");
        Assert.True(
            raw.JitRawInvocations < 100,
            $"too many runner invocations: chained={raw.JitRawChainedBlocks} inv={raw.JitRawInvocations} " +
            $"served={raw.JitRawServedMembers} built={raw.JitRawBlocksBuilt}");
    }

    private static void RunLoop(byte[] code)
    {
        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        Preset(plain);
        Preset(raw);
        plain.Execute(5000);
        raw.Execute(5000);
        AssertSame(plain, raw);
    }

    [Fact]
    public void Loop_LoadMember_MatchesInterpreter()
    {
        // 0x1000: jmp 0x1005 ; 0x1005: mov eax,[0x200C] ; 0x100A: jmp 0x1000
        byte[] code =
        [
            0xEB, 0x03,
            0x90, 0x90, 0x90,
            0xA1, 0x0C, 0x20, 0x00, 0x00,
            0xEB, 0xF4,
        ];
        RunLoop(code);
    }

    [Fact]
    public void Loop_CmpMember_MatchesInterpreter()
    {
        // 0x1000: jmp 0x1005 ; 0x1005: cmp dword [0x200C],5 ; 0x100C: jmp 0x1000
        byte[] code =
        [
            0xEB, 0x03,
            0x90, 0x90, 0x90,
            0x83, 0x3D, 0x0C, 0x20, 0x00, 0x00, 0x05,
            0xEB, 0xF2,
        ];
        RunLoop(code);
    }

    [Fact]
    public void Loop_StoreMember_MatchesInterpreter()
    {
        // 0x1000: jmp 0x1005 ; 0x1005: mov [0x200C],eax ; 0x100A: jmp 0x1000
        byte[] code =
        [
            0xEB, 0x03,
            0x90, 0x90, 0x90,
            0xA3, 0x0C, 0x20, 0x00, 0x00,
            0xEB, 0xF4,
        ];
        RunLoop(code);
    }
    [Fact]
    public void FallbackMembers_StillExact()
    {
        // mov ecx,2; fld dword [0x2000]; fmul dword [0x2004]; fstp dword [0x2008];
        // rdtsc; hlt - x87/SSE members run through the raw helper trampoline.
        byte[] code =
        [
            0xB9, 0x02, 0x00, 0x00, 0x00,
            0xD9, 0x05, 0x00, 0x20, 0x00, 0x00,
            0xD8, 0x0D, 0x04, 0x20, 0x00, 0x00,
            0xD9, 0x1D, 0x08, 0x20, 0x00, 0x00,
            0x0F, 0x31,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            cpu.Ram().Write32(0x2000, BitConverter.SingleToUInt32Bits(3.5f));
            cpu.Ram().Write32(0x2004, BitConverter.SingleToUInt32Bits(2.0f));
        }

        Preset(plain);
        Preset(raw);
        plain.Execute(10_000);
        raw.Execute(10_000);
        Assert.Equal(plain.Ram().Read32(0x2008), raw.Ram().Read32(0x2008));
        Assert.Equal(plain.Context.Eip, raw.Context.Eip);
    }

    [Fact]
    public void FaultingMember_DeliveredFromRawHelper_MatchesInterpreter()
    {
        // Regression for the §199l crash: a managed exception must never unwind
        // out of a raw helper through the hand-emitted frame (CoreCLR cannot
        // walk it - no unwind data - so delivery depended on stack layout luck
        // and aborted the process with 0x80131506). The helpers catch, record
        // and report; RunJitBlock rethrows in managed code.
        //
        // mov esi,0x2000; mov ecx,6; loop: mov eax,[esi]; add esi,0x1000;
        // dec ecx; jne loop; hlt. Four pages (0x2000-0x5FFF) are mapped, so the
        // load faults on the fifth iteration - by then the raw block has
        // chained into itself several times, so the faulting invocation is a
        // chain target (the shape that used to abort the test host).
        byte[] code =
        [
            0xBE, 0x00, 0x20, 0x00, 0x00,
            0xB9, 0x06, 0x00, 0x00, 0x00,
            0x8B, 0x06,
            0x81, 0xC6, 0x00, 0x10, 0x00, 0x00,
            0x49,
            0x75, 0xF5,
            0xF4,
        ];

        XboxCpu plain = CreatePlain(code);
        XboxCpu raw = CreateRaw(code);
        foreach (XboxCpu cpu in new[] { plain, raw })
        {
            MapIdentity(cpu.Bus(), 1, 5);
            EnablePaging(cpu);
            cpu.Ram().Write32(0x2000, 0xAABBCCDD);
            cpu.Ram().Write8(0x1500, 0xF4);
            InstallIdt(cpu, 14, 0x1500);
        }

        Preset(plain);
        Preset(raw);
        plain.Context.Esp = 0x2F00;
        raw.Context.Esp = 0x2F00;
        plain.Execute(10_000);
        raw.Execute(10_000);

        AssertSame(plain, raw);
        Assert.Equal(14, raw.LastFaultVector);
        Assert.Equal(0x100Au, raw.LastFaultEip);
        Assert.Equal(0x6000u, raw.Context.Cr2);
        Assert.Equal(1ul, raw.JitFaults);
        Assert.True(
            raw.JitRawFaults == 1,
            $"rawFaults={raw.JitRawFaults} rawInv={raw.JitRawInvocations} built={raw.JitRawBlocksBuilt} " +
            $"chained={raw.JitRawChainedBlocks} served={raw.JitRawServedMembers} faults={raw.JitFaults}");
        Assert.True(raw.JitRawBlocksBuilt > 0, "no raw block was built");
        Assert.True(raw.JitRawChainedBlocks > 0, "the loop did not chain before the fault");
    }

    private const uint PageDirectory = 0x4000;
    private const uint PageTable = 0x5000;

    private static void MapIdentity(AddressSpace bus, uint firstVpn, uint count)
    {
        bus.Write32(PageDirectory, PageTable | 3u);
        for (uint vpn = firstVpn; vpn < firstVpn + count; vpn++)
        {
            bus.Write32(PageTable + (vpn * 4), (vpn << 12) | 3u);
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
        uint gate = idt + ((uint)vector * 8);
        cpu.Bus().Write16(gate, (ushort)(handler & 0xFFFF));
        cpu.Bus().Write16(gate + 2, 0x08);
        cpu.Bus().Write16(gate + 4, 0x8E00);
        cpu.Bus().Write16(gate + 6, (ushort)(handler >> 16));
        cpu.Context.Idtr.Base = idt;
        cpu.Context.Idtr.Limit = 0x7FF;
    }
}
