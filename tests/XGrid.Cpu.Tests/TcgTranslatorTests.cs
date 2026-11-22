using System;
using XGrid.Cpu.Tcg;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// P1S3: the managed TCG IR and the i386 translator shapes are differentially
/// checked against the real interpreter (registers, EFLAGS, EIP and guest memory
/// must match exactly).
/// </summary>
public sealed class TcgTranslatorTests
{
    private static void AssertMatches(
        byte[] code,
        Action<XboxCpu>? setup = null,
        Action<XboxCpu, XboxCpu>? verify = null,
        bool mmx = false)
    {
        XboxCpu interpreter = CpuHarness.Create(code);
        XboxCpu tcg = CpuHarness.Create(code);
        setup?.Invoke(interpreter);
        setup?.Invoke(tcg);

        uint eip = tcg.Context.Eip;
        var initial = new uint[8];
        for (int i = 0; i < 8; i++)
        {
            initial[i] = tcg.Context.ReadRegister((Gpr)i);
        }

        uint initialFlags = tcg.Context.Eflags;
        var segments = new uint[6];
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = tcg.Context.GetSegmentBase((SegmentRegister)i);
        }

        DecodedInstruction decoded = Decoder.Decode(code);
        Assert.True(decoded.IsValid);

        var builder = new TcgBuilder(eip);
        bool translated = I386Translator.TryTranslate(decoded, eip, builder, out bool terminates, out string? reason);
        Assert.True(translated, reason);
        if (!terminates)
        {
            builder.SetEip(eip + (uint)decoded.Length);
            builder.Exit();
        }

        FpuState? fpuState = mmx ? tcg.Context.Fpu : null;
        TcgResult result = TcgInterpreter.Execute(
            builder.Build(),
            initial,
            initialFlags,
            segments,
            new TcgTestMemory(tcg.Bus()),
            fpuState);

        interpreter.Step();
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(interpreter.Context.ReadRegister((Gpr)i), result.Registers[i]);
        }

        Assert.Equal(interpreter.Context.Eflags, result.Eflags);
        Assert.Equal(interpreter.Context.Eip, result.Eip);
        if (mmx)
        {
            for (int i = 0; i < 8; i++)
            {
                Assert.Equal(interpreter.Context.Fpu.GetMmxRegister(i), result.Fpu!.GetMmxRegister(i));
            }

            Assert.Equal(interpreter.Context.Fpu.TagWord, result.Fpu!.TagWord);
        }

        verify?.Invoke(interpreter, tcg);
    }

    private static void Set(XboxCpu cpu, Gpr register, uint value)
    {
        cpu.Context.WriteRegister(register, value);
    }

    private static void Flags(XboxCpu cpu, uint flags)
    {
        cpu.Context.Eflags = flags;
    }

    private static void Memory32(XboxCpu cpu, uint address, uint value)
    {
        cpu.Ram().Write32(address, value);
    }

    private static void Mmx(XboxCpu cpu, int index, ulong value)
    {
        cpu.Context.WriteMmx(index, value);
    }

    private static void Memory64(XboxCpu cpu, uint address, ulong value)
    {
        cpu.Ram().Write32(address, (uint)value);
        cpu.Ram().Write32(address + 4, (uint)(value >> 32));
    }

    [Fact]
    public void MovImmediate()
    {
        AssertMatches([0xB8, 0x78, 0x56, 0x34, 0x12]);
    }

    [Fact]
    public void MovRegister()
    {
        AssertMatches([0x89, 0xC3], cpu => Set(cpu, Gpr.Eax, 0x11223344));
    }

    [Fact]
    public void AddRegister()
    {
        AssertMatches([0x01, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0xFFFFFFFE); Set(cpu, Gpr.Ebx, 3); });
    }

    [Fact]
    public void AddImmediate()
    {
        AssertMatches([0x05, 0x01, 0x00, 0x00, 0x00], cpu => Set(cpu, Gpr.Eax, 0xFFFFFFFF));
    }

    [Fact]
    public void SubRegister()
    {
        AssertMatches([0x29, 0xD8], cpu => { Set(cpu, Gpr.Eax, 3); Set(cpu, Gpr.Ebx, 5); });
    }

    [Fact]
    public void AndRegister()
    {
        AssertMatches([0x21, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x0000F0F0); Set(cpu, Gpr.Ebx, 0x00000FF0); });
    }

    [Fact]
    public void OrRegister()
    {
        AssertMatches([0x09, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x0000F0F0); Set(cpu, Gpr.Ebx, 0x00000FF0); });
    }

    [Fact]
    public void XorRegister()
    {
        AssertMatches([0x31, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x0000F0F0); Set(cpu, Gpr.Ebx, 0x00000FF0); });
    }

    [Fact]
    public void CmpRegister()
    {
        AssertMatches([0x39, 0xD8], cpu => { Set(cpu, Gpr.Eax, 5); Set(cpu, Gpr.Ebx, 5); });
    }

    [Fact]
    public void IncRegisterPreservesCarry()
    {
        AssertMatches([0x40], cpu => { Set(cpu, Gpr.Eax, 0xFFFFFFFF); Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf); });
    }

    [Fact]
    public void DecRegisterPreservesCarry()
    {
        AssertMatches([0x4B], cpu => { Set(cpu, Gpr.Ebx, 0); Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf); });
    }

    [Fact]
    public void Nop()
    {
        AssertMatches([0x90]);
    }

    [Fact]
    public void JmpRel8()
    {
        AssertMatches([0xEB, 0x05]);
    }

    [Fact]
    public void JeTaken()
    {
        AssertMatches([0x74, 0x05], cpu => Flags(cpu, FlagBits.Reserved1 | FlagBits.Zf));
    }

    [Fact]
    public void JeNotTaken()
    {
        AssertMatches([0x74, 0x05], cpu => Flags(cpu, FlagBits.Reserved1));
    }

    [Fact]
    public void JneTaken()
    {
        AssertMatches([0x75, 0x05], cpu => Flags(cpu, FlagBits.Reserved1));
    }

    [Fact]
    public void MovLoad32()
    {
        AssertMatches(
            [0x8B, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => Memory32(cpu, 0x2000, 0x11223344));
    }

    [Fact]
    public void MovStore32()
    {
        AssertMatches(
            [0x89, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => Set(cpu, Gpr.Eax, 0x55667788),
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000)));
    }

    [Fact]
    public void MovStoreImmediate32()
    {
        AssertMatches(
            [0xC7, 0x05, 0x00, 0x20, 0x00, 0x00, 0x78, 0x56, 0x34, 0x12],
            verify: (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000)));
    }

    [Fact]
    public void AddLoad32()
    {
        AssertMatches(
            [0x03, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => { Set(cpu, Gpr.Eax, 1); Memory32(cpu, 0x2000, 2); });
    }

    [Fact]
    public void AddStore32()
    {
        AssertMatches(
            [0x01, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => { Set(cpu, Gpr.Eax, 2); Memory32(cpu, 0x2000, 1); },
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000)));
    }

    [Fact]
    public void CmpMemoryDoesNotStore()
    {
        AssertMatches(
            [0x39, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => { Set(cpu, Gpr.Eax, 5); Memory32(cpu, 0x2000, 5); },
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000)));
    }

    [Fact]
    public void MovAlImmediate()
    {
        AssertMatches([0xB0, 0x12], cpu => Set(cpu, Gpr.Eax, 0xAAAAAAAA));
    }

    [Fact]
    public void MovAhImmediate()
    {
        AssertMatches([0xB4, 0x34], cpu => Set(cpu, Gpr.Eax, 0xAAAAAAAA));
    }

    [Fact]
    public void MovAxImmediate()
    {
        AssertMatches([0x66, 0xB8, 0x34, 0x12], cpu => Set(cpu, Gpr.Eax, 0xAAAAAAAA));
    }

    [Fact]
    public void AddAlRegister()
    {
        AssertMatches([0x00, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x80); Set(cpu, Gpr.Ebx, 0x80); });
    }

    [Fact]
    public void AddAxRegister()
    {
        AssertMatches([0x66, 0x01, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x8000); Set(cpu, Gpr.Ebx, 0x8000); });
    }

    [Fact]
    public void IncAl()
    {
        AssertMatches([0xFE, 0xC0], cpu => { Set(cpu, Gpr.Eax, 0xFF); Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf); });
    }

    [Fact]
    public void DecBl()
    {
        AssertMatches([0xFE, 0xCB], cpu => { Set(cpu, Gpr.Ebx, 0); Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf); });
    }

    [Fact]
    public void MovStoreByte()
    {
        AssertMatches(
            [0x88, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => Set(cpu, Gpr.Eax, 0x11223344),
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read8(0x2000), tcg.Ram().Read8(0x2000)));
    }

    [Fact]
    public void Mov16BitAddress()
    {
        AssertMatches(
            [0x67, 0x8B, 0x00],
            cpu => { Set(cpu, Gpr.Ebx, 0x10); Set(cpu, Gpr.Esi, 0x2000); Memory32(cpu, 0x2010, 0x11223344); });
    }

    [Fact]
    public void Mov16BitAddressBpRelative()
    {
        AssertMatches(
            [0x67, 0x8B, 0x46, 0x04],
            cpu => { Set(cpu, Gpr.Ebp, 0x1000); Set(cpu, Gpr.Esi, 0x2000); Memory32(cpu, 0x3004, 0x55667788); });
    }

    [Fact]
    public void UnsupportedMemoryForm_ReturnsFalse()
    {
        // INSB: a port string instruction the first slice does not translate.
        DecodedInstruction decoded = Decoder.Decode([0x6C]);
        var builder = new TcgBuilder(0x1000);
        Assert.False(I386Translator.TryTranslate(decoded, 0x1000, builder, out _, out string? reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void ShlImmediate()
    {
        AssertMatches([0xC1, 0xE0, 0x01], cpu => Set(cpu, Gpr.Eax, 0x40000001));
    }

    [Fact]
    public void ShrImmediate()
    {
        AssertMatches([0xC1, 0xE8, 0x01], cpu => Set(cpu, Gpr.Eax, 0x80000001));
    }

    [Fact]
    public void SarImmediate()
    {
        AssertMatches([0xC1, 0xF8, 0x01], cpu => Set(cpu, Gpr.Eax, 0x80000001));
    }

    [Fact]
    public void RolImmediate()
    {
        AssertMatches([0xC1, 0xC0, 0x01], cpu => Set(cpu, Gpr.Eax, 0x80000001));
    }

    [Fact]
    public void RorImmediate()
    {
        AssertMatches([0xC1, 0xC8, 0x01], cpu => Set(cpu, Gpr.Eax, 0x80000001));
    }

    [Fact]
    public void ShlByCl()
    {
        AssertMatches([0xD3, 0xE0], cpu => { Set(cpu, Gpr.Eax, 1); Set(cpu, Gpr.Ecx, 4); });
    }

    [Fact]
    public void ShldImmediate()
    {
        AssertMatches([0x0F, 0xA4, 0xC8, 0x04], cpu => { Set(cpu, Gpr.Eax, 0x12345678); Set(cpu, Gpr.Ecx, 0x9ABCDEF0); });
    }

    [Fact]
    public void ShrdImmediate()
    {
        AssertMatches([0x0F, 0xAC, 0xC8, 0x04], cpu => { Set(cpu, Gpr.Eax, 0x12345678); Set(cpu, Gpr.Ecx, 0x9ABCDEF0); });
    }

    [Fact]
    public void NegRegister()
    {
        AssertMatches([0xF7, 0xD8], cpu => Set(cpu, Gpr.Eax, 0x80000000));
    }

    [Fact]
    public void NotRegister()
    {
        AssertMatches([0xF7, 0xD0], cpu => Set(cpu, Gpr.Eax, 0x00FF00FF));
    }

    [Fact]
    public void MovzxByte()
    {
        AssertMatches([0x0F, 0xB6, 0xC3], cpu => Set(cpu, Gpr.Ebx, 0xFFFFFF80));
    }

    [Fact]
    public void MovsxByte()
    {
        AssertMatches([0x0F, 0xBE, 0xC3], cpu => Set(cpu, Gpr.Ebx, 0x00000080));
    }

    [Fact]
    public void MovzxWord()
    {
        AssertMatches([0x0F, 0xB7, 0xC3], cpu => Set(cpu, Gpr.Ebx, 0xFFFF8000));
    }

    [Fact]
    public void MovsxWord()
    {
        AssertMatches([0x0F, 0xBF, 0xC3], cpu => Set(cpu, Gpr.Ebx, 0x00008000));
    }

    [Fact]
    public void LeaStackOffset()
    {
        AssertMatches([0x8D, 0x44, 0x24, 0x08], cpu => Set(cpu, Gpr.Esp, 0x8000));
    }

    [Fact]
    public void XchgRegisters()
    {
        AssertMatches([0x87, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x11223344); Set(cpu, Gpr.Ebx, 0x55667788); });
    }

    [Fact]
    public void XchgMemory()
    {
        AssertMatches(
            [0x87, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => { Set(cpu, Gpr.Eax, 0x11223344); Memory32(cpu, 0x2000, 0x55667788); },
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000)));
    }

    [Fact]
    public void SahfLoadsFlagsFromAh()
    {
        AssertMatches([0x9E], cpu => Set(cpu, Gpr.Eax, 0x0000D500));
    }

    [Fact]
    public void LahfStoresFlagsInAh()
    {
        AssertMatches([0x9F], cpu => Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Zf));
    }

    [Fact]
    public void NegMemory()
    {
        AssertMatches(
            [0xF7, 0x1D, 0x00, 0x20, 0x00, 0x00],
            cpu => Memory32(cpu, 0x2000, 0x00000001),
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000)));
    }

    [Fact]
    public void MulRegister()
    {
        AssertMatches([0xF7, 0xE3], cpu => { Set(cpu, Gpr.Eax, 0x00010000); Set(cpu, Gpr.Ebx, 0x00010000); });
    }

    [Fact]
    public void ImulRegister()
    {
        AssertMatches([0xF7, 0xEB], cpu => { Set(cpu, Gpr.Eax, 0xFFFFFFFE); Set(cpu, Gpr.Ebx, 3); });
    }

    [Fact]
    public void ImulTwoOperands()
    {
        AssertMatches([0x0F, 0xAF, 0xC3], cpu => { Set(cpu, Gpr.Eax, 3); Set(cpu, Gpr.Ebx, 0xFFFFFFFE); });
    }

    [Fact]
    public void ImulThreeOperands()
    {
        AssertMatches([0x6B, 0xC3, 0xFD], cpu => Set(cpu, Gpr.Ebx, 2));
    }

    [Fact]
    public void DivRegister()
    {
        AssertMatches([0xF7, 0xF3], cpu => { Set(cpu, Gpr.Eax, 10); Set(cpu, Gpr.Edx, 0); Set(cpu, Gpr.Ebx, 3); });
    }

    [Fact]
    public void IdivRegister()
    {
        AssertMatches([0xF7, 0xFB], cpu => { Set(cpu, Gpr.Eax, 0xFFFFFFF6); Set(cpu, Gpr.Edx, 0xFFFFFFFF); Set(cpu, Gpr.Ebx, 3); });
    }

    [Fact]
    public void PushImmediate32()
    {
        AssertMatches(
            [0x68, 0x78, 0x56, 0x34, 0x12],
            verify: (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x7FFC), tcg.Ram().Read32(0x7FFC)));
    }

    [Fact]
    public void PushImmediate8SignExtends()
    {
        AssertMatches(
            [0x6A, 0xFF],
            verify: (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x7FFC), tcg.Ram().Read32(0x7FFC)));
    }

    [Fact]
    public void PushRegister()
    {
        AssertMatches(
            [0x50],
            cpu => Set(cpu, Gpr.Eax, 0x11223344),
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x7FFC), tcg.Ram().Read32(0x7FFC)));
    }

    [Fact]
    public void PopRegister()
    {
        AssertMatches(
            [0x58],
            cpu => { Set(cpu, Gpr.Esp, 0x7FFC); Memory32(cpu, 0x7FFC, 0x11223344); });
    }

    [Fact]
    public void PopMemory()
    {
        AssertMatches(
            [0x8F, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => { Set(cpu, Gpr.Esp, 0x7FFC); Memory32(cpu, 0x7FFC, 0x11223344); },
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000)));
    }

    [Fact]
    public void BtRegister()
    {
        AssertMatches([0x0F, 0xA3, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x10); Set(cpu, Gpr.Ebx, 4); });
    }

    [Fact]
    public void BtsRegister()
    {
        AssertMatches([0x0F, 0xAB, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0); Set(cpu, Gpr.Ebx, 4); });
    }

    [Fact]
    public void BtrRegister()
    {
        AssertMatches([0x0F, 0xB3, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x10); Set(cpu, Gpr.Ebx, 4); });
    }

    [Fact]
    public void BtcRegister()
    {
        AssertMatches([0x0F, 0xBB, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x10); Set(cpu, Gpr.Ebx, 4); });
    }

    [Fact]
    public void BtImmediate()
    {
        AssertMatches([0x0F, 0xBA, 0xE0, 0x04], cpu => Set(cpu, Gpr.Eax, 0x10));
    }

    [Fact]
    public void BtsImmediate()
    {
        AssertMatches([0x0F, 0xBA, 0xE8, 0x04], cpu => Set(cpu, Gpr.Eax, 0));
    }

    [Fact]
    public void BtMemory()
    {
        AssertMatches(
            [0x0F, 0xA3, 0x18],
            cpu => { Set(cpu, Gpr.Eax, 0x2000); Set(cpu, Gpr.Ebx, 4); Memory32(cpu, 0x2000, 0x10); });
    }

    [Fact]
    public void BtMemoryBitString()
    {
        AssertMatches(
            [0x0F, 0xA3, 0x18],
            cpu => { Set(cpu, Gpr.Eax, 0x2000); Set(cpu, Gpr.Ebx, 36); Memory32(cpu, 0x2004, 0x02); });
    }

    [Fact]
    public void CmpxchgEqual()
    {
        AssertMatches([0x0F, 0xB1, 0xCB], cpu => { Set(cpu, Gpr.Eax, 5); Set(cpu, Gpr.Ebx, 5); Set(cpu, Gpr.Ecx, 7); });
    }

    [Fact]
    public void CmpxchgByteRegisterComparesTheLowByte()
    {
        AssertMatches(
            [0x0F, 0xB0, 0xD9],
            cpu =>
            {
                Set(cpu, Gpr.Eax, 0x11111111);
                Set(cpu, Gpr.Ecx, 0x33333311);
                Set(cpu, Gpr.Ebx, 0x22222222);
            });
    }

    [Fact]
    public void CmpxchgWordRegisterComparesTheLowWord()
    {
        AssertMatches(
            [0x66, 0x0F, 0xB1, 0xD9],
            cpu =>
            {
                Set(cpu, Gpr.Eax, 0x11112222);
                Set(cpu, Gpr.Ecx, 0x33332222);
                Set(cpu, Gpr.Ebx, 0x44445555);
            });
    }

    [Fact]
    public void CmpxchgNotEqual()
    {
        AssertMatches([0x0F, 0xB1, 0xCB], cpu => { Set(cpu, Gpr.Eax, 5); Set(cpu, Gpr.Ebx, 6); Set(cpu, Gpr.Ecx, 7); });
    }

    [Fact]
    public void Cmpxchg8bEqual()
    {
        AssertMatches(
            [0x0F, 0xC7, 0x0D, 0x00, 0x20, 0x00, 0x00],
            cpu =>
            {
                Set(cpu, Gpr.Eax, 0x11223344);
                Set(cpu, Gpr.Edx, 0x55667788);
                Set(cpu, Gpr.Ebx, 0x99AABBCC);
                Set(cpu, Gpr.Ecx, 0xDDEEFF00);
                Memory32(cpu, 0x2000, 0x11223344);
                Memory32(cpu, 0x2004, 0x55667788);
            },
            (interpreter, tcg) =>
            {
                Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000));
                Assert.Equal(interpreter.Ram().Read32(0x2004), tcg.Ram().Read32(0x2004));
            });
    }

    [Fact]
    public void Cmpxchg8bNotEqual()
    {
        AssertMatches(
            [0x0F, 0xC7, 0x0D, 0x00, 0x20, 0x00, 0x00],
            cpu =>
            {
                Set(cpu, Gpr.Eax, 0x11111111);
                Set(cpu, Gpr.Edx, 0x22222222);
                Memory32(cpu, 0x2000, 0x33333333);
                Memory32(cpu, 0x2004, 0x44444444);
            });
    }

    [Fact]
    public void XaddRegister()
    {
        AssertMatches([0x0F, 0xC1, 0xD8], cpu => { Set(cpu, Gpr.Eax, 5); Set(cpu, Gpr.Ebx, 7); });
    }

    [Fact]
    public void XaddMemory()
    {
        AssertMatches(
            [0x0F, 0xC1, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => { Set(cpu, Gpr.Eax, 2); Memory32(cpu, 0x2000, 3); },
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000)));
    }

    [Fact]
    public void RepMovsb()
    {
        AssertMatches(
            [0xF3, 0xA4],
            cpu =>
            {
                Set(cpu, Gpr.Ecx, 4);
                Set(cpu, Gpr.Esi, 0x2000);
                Set(cpu, Gpr.Edi, 0x3000);
                cpu.Ram().Write8(0x2000, 0x11);
                cpu.Ram().Write8(0x2001, 0x22);
                cpu.Ram().Write8(0x2002, 0x33);
                cpu.Ram().Write8(0x2003, 0x44);
            },
            (interpreter, tcg) =>
            {
                for (uint i = 0; i < 4; i++)
                {
                    Assert.Equal(interpreter.Ram().Read8(0x3000 + i), tcg.Ram().Read8(0x3000 + i));
                }
            });
    }

    [Fact]
    public void RepStosd()
    {
        AssertMatches(
            [0xF3, 0xAB],
            cpu => { Set(cpu, Gpr.Ecx, 4); Set(cpu, Gpr.Edi, 0x3000); Set(cpu, Gpr.Eax, 0x11223344); },
            (interpreter, tcg) =>
            {
                for (uint i = 0; i < 4; i++)
                {
                    Assert.Equal(interpreter.Ram().Read32(0x3000 + (i * 4)), tcg.Ram().Read32(0x3000 + (i * 4)));
                }
            });
    }

    [Fact]
    public void RepLodsb()
    {
        AssertMatches(
            [0xF3, 0xAC],
            cpu =>
            {
                Set(cpu, Gpr.Ecx, 4);
                Set(cpu, Gpr.Esi, 0x2000);
                Set(cpu, Gpr.Eax, 0xAAAAAAAA);
                cpu.Ram().Write8(0x2000, 1);
                cpu.Ram().Write8(0x2001, 2);
                cpu.Ram().Write8(0x2002, 3);
                cpu.Ram().Write8(0x2003, 4);
            });
    }

    [Fact]
    public void RepeCmpsbEqual()
    {
        AssertMatches(
            [0xF3, 0xA6],
            cpu =>
            {
                Set(cpu, Gpr.Ecx, 4);
                Set(cpu, Gpr.Esi, 0x2000);
                Set(cpu, Gpr.Edi, 0x3000);
                for (uint i = 0; i < 4; i++)
                {
                    cpu.Ram().Write8(0x2000 + i, 0x55);
                    cpu.Ram().Write8(0x3000 + i, 0x55);
                }
            });
    }

    [Fact]
    public void RepneScasbStopsOnMatch()
    {
        AssertMatches(
            [0xF2, 0xAE],
            cpu =>
            {
                Set(cpu, Gpr.Ecx, 8);
                Set(cpu, Gpr.Edi, 0x3000);
                Set(cpu, Gpr.Eax, 0x42);
                cpu.Ram().Write8(0x3000, 1);
                cpu.Ram().Write8(0x3001, 2);
                cpu.Ram().Write8(0x3002, 3);
                cpu.Ram().Write8(0x3003, 0x42);
            });
    }

    [Fact]
    public void MovsbWithoutRep()
    {
        AssertMatches(
            [0xA4],
            cpu =>
            {
                Set(cpu, Gpr.Esi, 0x2000);
                Set(cpu, Gpr.Edi, 0x3000);
                cpu.Ram().Write8(0x2000, 0x77);
            },
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read8(0x3000), tcg.Ram().Read8(0x3000)));
    }

    [Fact]
    public void RepMovsbReverse()
    {
        AssertMatches(
            [0xF3, 0xA4],
            cpu =>
            {
                Set(cpu, Gpr.Ecx, 4);
                Set(cpu, Gpr.Esi, 0x2003);
                Set(cpu, Gpr.Edi, 0x3003);
                Flags(cpu, FlagBits.Reserved1 | FlagBits.Df);
                for (uint i = 0; i < 4; i++)
                {
                    cpu.Ram().Write8(0x2000 + i, (byte)(0x10 + i));
                }
            },
            (interpreter, tcg) =>
            {
                for (uint i = 0; i < 4; i++)
                {
                    Assert.Equal(interpreter.Ram().Read8(0x3000 + i), tcg.Ram().Read8(0x3000 + i));
                }
            });
    }

    [Fact]
    public void RepMovsb16BitAddress()
    {
        AssertMatches(
            [0x67, 0xF3, 0xA4],
            cpu =>
            {
                Set(cpu, Gpr.Ecx, 2);
                Set(cpu, Gpr.Esi, 0xABCD2000);
                Set(cpu, Gpr.Edi, 0x12343000);
                cpu.Ram().Write8(0x2000, 0x9A);
                cpu.Ram().Write8(0x2001, 0xBC);
            },
            (interpreter, tcg) =>
            {
                Assert.Equal(interpreter.Ram().Read8(0x3000), tcg.Ram().Read8(0x3000));
                Assert.Equal(interpreter.Ram().Read8(0x3001), tcg.Ram().Read8(0x3001));
            });
    }

    [Fact]
    public void PaddbWraps()
    {
        AssertMatches(
            [0x0F, 0xFC, 0xC1],
            cpu => { Mmx(cpu, 0, 0xFFFFFFFFFFFFFFFF); Mmx(cpu, 1, 0x0101010101010101); },
            mmx: true);
    }

    [Fact]
    public void PaddswSaturates()
    {
        AssertMatches(
            [0x0F, 0xED, 0xC1],
            cpu => { Mmx(cpu, 0, 0x7FFF7FFF7FFF7FFF); Mmx(cpu, 1, 0x0001000100010001); },
            mmx: true);
    }

    [Fact]
    public void PaddusbSaturates()
    {
        AssertMatches(
            [0x0F, 0xDC, 0xC1],
            cpu => { Mmx(cpu, 0, 0xF0F0F0F0F0F0F0F0); Mmx(cpu, 1, 0x2020202020202020); },
            mmx: true);
    }

    [Fact]
    public void PsubuswSaturates()
    {
        AssertMatches(
            [0x0F, 0xD9, 0xC1],
            cpu => { Mmx(cpu, 0, 0x0001000100010001); Mmx(cpu, 1, 0x0002000200020002); },
            mmx: true);
    }

    [Fact]
    public void PadddWraps()
    {
        AssertMatches(
            [0x0F, 0xFE, 0xC1],
            cpu => { Mmx(cpu, 0, 0xFFFFFFFF00000001); Mmx(cpu, 1, 0x0000000100000001); },
            mmx: true);
    }

    [Fact]
    public void Psubd()
    {
        AssertMatches(
            [0x0F, 0xFA, 0xC1],
            cpu => { Mmx(cpu, 0, 0x0000000100000005); Mmx(cpu, 1, 0x0000000200000003); },
            mmx: true);
    }

    [Fact]
    public void Pmullw()
    {
        AssertMatches(
            [0x0F, 0xD5, 0xC1],
            cpu => { Mmx(cpu, 0, 0x0002000300040005); Mmx(cpu, 1, 0x0007000B000D0011); },
            mmx: true);
    }

    [Fact]
    public void PmulhwSigned()
    {
        AssertMatches(
            [0x0F, 0xE5, 0xC1],
            cpu => { Mmx(cpu, 0, 0x80008000FFFF0002); Mmx(cpu, 1, 0x7FFF0002FFFF0003); },
            mmx: true);
    }

    [Fact]
    public void Pmaddwd()
    {
        AssertMatches(
            [0x0F, 0xF5, 0xC1],
            cpu => { Mmx(cpu, 0, 0x0002000300040005); Mmx(cpu, 1, 0x0007000B000D0011); },
            mmx: true);
    }

    [Fact]
    public void Pcmpeqb()
    {
        AssertMatches(
            [0x0F, 0x74, 0xC1],
            cpu => { Mmx(cpu, 0, 0x0011223344556677); Mmx(cpu, 1, 0x0011223344556678); },
            mmx: true);
    }

    [Fact]
    public void Pcmpgtw()
    {
        AssertMatches(
            [0x0F, 0x65, 0xC1],
            cpu => { Mmx(cpu, 0, 0x0001FFFF80000002); Mmx(cpu, 1, 0x0002000200000003); },
            mmx: true);
    }

    [Fact]
    public void Pand()
    {
        AssertMatches(
            [0x0F, 0xDB, 0xC1],
            cpu => { Mmx(cpu, 0, 0xF0F0F0F0F0F0F0F0); Mmx(cpu, 1, 0x0FF00FF00FF00FF0); },
            mmx: true);
    }

    [Fact]
    public void Por()
    {
        AssertMatches(
            [0x0F, 0xEB, 0xC1],
            cpu => { Mmx(cpu, 0, 0xF000000000000000); Mmx(cpu, 1, 0x00000000000000FF); },
            mmx: true);
    }

    [Fact]
    public void Pxor()
    {
        AssertMatches(
            [0x0F, 0xEF, 0xC1],
            cpu => { Mmx(cpu, 0, 0xFFFFFFFF00000000); Mmx(cpu, 1, 0xFFFF0000FFFF0000); },
            mmx: true);
    }

    [Fact]
    public void Punpcklbw()
    {
        AssertMatches(
            [0x0F, 0x60, 0xC1],
            cpu => { Mmx(cpu, 0, 0x8877665544332211); Mmx(cpu, 1, 0xCCBBAA9988776655); },
            mmx: true);
    }

    [Fact]
    public void Packssdw()
    {
        AssertMatches(
            [0x0F, 0x6B, 0xC1],
            cpu => { Mmx(cpu, 0, 0x0001000200008000); Mmx(cpu, 1, 0x7FFFFFFF80000000); },
            mmx: true);
    }

    [Fact]
    public void Packuswb()
    {
        AssertMatches(
            [0x0F, 0x67, 0xC1],
            cpu => { Mmx(cpu, 0, 0x00FF0001FF000002); Mmx(cpu, 1, 0x8000FFFF7FFF0000); },
            mmx: true);
    }

    [Fact]
    public void Pavgb()
    {
        AssertMatches(
            [0x0F, 0xE0, 0xC1],
            cpu => { Mmx(cpu, 0, 0x00010203FCFDFEFF); Mmx(cpu, 1, 0x0F0F0F0FF0F0F0F0); },
            mmx: true);
    }

    [Fact]
    public void Pminsw()
    {
        AssertMatches(
            [0x0F, 0xEA, 0xC1],
            cpu => { Mmx(cpu, 0, 0x0001FFFF80000002); Mmx(cpu, 1, 0x0002000200000003); },
            mmx: true);
    }

    [Fact]
    public void Psadbw()
    {
        AssertMatches(
            [0x0F, 0xF6, 0xC1],
            cpu => { Mmx(cpu, 0, 0x1020304050607080); Mmx(cpu, 1, 0x0011223344556677); },
            mmx: true);
    }

    [Fact]
    public void PsllwImmediatePastWidthClears()
    {
        AssertMatches(
            [0x0F, 0x71, 0xF1, 0x14],
            cpu => Mmx(cpu, 1, 0xFFFFFFFFFFFFFFFF),
            mmx: true);
    }

    [Fact]
    public void PsrlqImmediate()
    {
        AssertMatches(
            [0x0F, 0x73, 0xD1, 0x08],
            cpu => Mmx(cpu, 1, 0xFFEEDDCCBBAA9988),
            mmx: true);
    }

    [Fact]
    public void PsrawImmediatePastWidthSignFillsPerWord()
    {
        AssertMatches(
            [0x0F, 0x71, 0xE1, 0x14],
            cpu => Mmx(cpu, 1, 0x00000000FFFF8001),
            mmx: true);
    }

    [Fact]
    public void PsradCountFromMmx()
    {
        AssertMatches(
            [0x0F, 0xE2, 0xC1],
            cpu => { Mmx(cpu, 0, 0x80000000FFFF0000); Mmx(cpu, 1, 4); },
            mmx: true);
    }

    [Fact]
    public void MovqRegister()
    {
        AssertMatches(
            [0x0F, 0x6F, 0xC1],
            cpu => Mmx(cpu, 1, 0x1122334455667788),
            mmx: true);
    }

    [Fact]
    public void MovqLoad()
    {
        AssertMatches(
            [0x0F, 0x6F, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => Memory64(cpu, 0x2000, 0x99AABBCCDDEEFF00),
            mmx: true);
    }

    [Fact]
    public void MovqStore()
    {
        AssertMatches(
            [0x0F, 0x7F, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => Mmx(cpu, 0, 0x0123456789ABCDEF),
            (interpreter, tcg) =>
            {
                Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000));
                Assert.Equal(interpreter.Ram().Read32(0x2004), tcg.Ram().Read32(0x2004));
            },
            mmx: true);
    }

    [Fact]
    public void MovdToMmxZeroExtends()
    {
        AssertMatches(
            [0x0F, 0x6E, 0xC0],
            cpu => { Set(cpu, Gpr.Eax, 0x89ABCDEF); Mmx(cpu, 0, 0xFFFFFFFFFFFFFFFF); },
            mmx: true);
    }

    [Fact]
    public void MovdFromMmx()
    {
        AssertMatches(
            [0x0F, 0x7E, 0xC0],
            cpu => Mmx(cpu, 0, 0x1122334455667788),
            mmx: true);
    }

    [Fact]
    public void MovntqStore()
    {
        AssertMatches(
            [0x0F, 0xE7, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => Mmx(cpu, 0, 0xFEDCBA9876543210),
            (interpreter, tcg) =>
            {
                Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000));
                Assert.Equal(interpreter.Ram().Read32(0x2004), tcg.Ram().Read32(0x2004));
            },
            mmx: true);
    }

    [Fact]
    public void Pmovmskb()
    {
        AssertMatches(
            [0x0F, 0xD7, 0xC1],
            cpu => Mmx(cpu, 1, 0x807F0080FF0001FF),
            mmx: true);
    }

    [Fact]
    public void Pextrw()
    {
        AssertMatches(
            [0x0F, 0xC5, 0xC1, 0x02],
            cpu => Mmx(cpu, 1, 0x1111222233334444),
            mmx: true);
    }

    [Fact]
    public void Pinsrw()
    {
        AssertMatches(
            [0x0F, 0xC4, 0xC1, 0x01],
            cpu => { Set(cpu, Gpr.Ecx, 0x0000BEEF); Mmx(cpu, 0, 0x1111222233334444); },
            mmx: true);
    }

    [Fact]
    public void PshufwReversesWords()
    {
        AssertMatches(
            [0x0F, 0x70, 0xC1, 0x1B],
            cpu => Mmx(cpu, 1, 0x1111222233334444),
            mmx: true);
    }

    [Fact]
    public void MaskmovqWritesSelectedBytes()
    {
        AssertMatches(
            [0x0F, 0xF7, 0xC1],
            cpu =>
            {
                Set(cpu, Gpr.Edi, 0x3000);
                Mmx(cpu, 0, 0x8877665544332211);
                Mmx(cpu, 1, 0xFF00FF00FF00FF00);
            },
            (interpreter, tcg) =>
            {
                for (uint i = 0; i < 8; i++)
                {
                    Assert.Equal(interpreter.Ram().Read8(0x3000 + i), tcg.Ram().Read8(0x3000 + i));
                }
            },
            mmx: true);
    }

    [Fact]
    public void EmmsEmptiesTags()
    {
        AssertMatches(
            [0x0F, 0x77],
            cpu => { Mmx(cpu, 0, 0x1122334455667788); Mmx(cpu, 1, 0x99AABBCCDDEEFF00); },
            mmx: true);
    }

    [Fact]
    public void AdcCarriesIn()
    {
        AssertMatches(
            [0x11, 0xD8],
            cpu => { Set(cpu, Gpr.Eax, 0xFFFFFFFF); Set(cpu, Gpr.Ebx, 0); Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf); });
    }

    [Fact]
    public void AdcCarryOut()
    {
        AssertMatches(
            [0x83, 0xD0, 0x00],
            cpu => { Set(cpu, Gpr.Eax, 0xFFFFFFFE); Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf); });
    }

    [Fact]
    public void SbbBorrowsIn()
    {
        AssertMatches(
            [0x19, 0xD8],
            cpu => { Set(cpu, Gpr.Eax, 5); Set(cpu, Gpr.Ebx, 1); Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf); });
    }

    [Fact]
    public void SbbByte()
    {
        AssertMatches(
            [0x18, 0xD8],
            cpu => { Set(cpu, Gpr.Eax, 0x11223300); Set(cpu, Gpr.Ebx, 0x44556601); Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf); });
    }

    [Fact]
    public void TestRegister()
    {
        AssertMatches([0x85, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x0000F0F0); Set(cpu, Gpr.Ebx, 0x0000000F); });
    }

    [Fact]
    public void TestImmediate()
    {
        AssertMatches([0xA9, 0x00, 0x00, 0x00, 0x80], cpu => Set(cpu, Gpr.Eax, 0xFFFFFFFF));
    }

    [Fact]
    public void SeteTaken()
    {
        AssertMatches(
            [0x0F, 0x94, 0xC0],
            cpu => { Set(cpu, Gpr.Eax, 0x11223344); Flags(cpu, FlagBits.Reserved1 | FlagBits.Zf); });
    }

    [Fact]
    public void SetneMemory()
    {
        AssertMatches(
            [0x0F, 0x95, 0x05, 0x00, 0x20, 0x00, 0x00],
            cpu => { Memory32(cpu, 0x2000, 0x11223344); Flags(cpu, FlagBits.Reserved1); },
            (interpreter, tcg) => Assert.Equal(interpreter.Ram().Read32(0x2000), tcg.Ram().Read32(0x2000)));
    }

    [Fact]
    public void CmovzTaken()
    {
        AssertMatches(
            [0x0F, 0x44, 0xD8],
            cpu => { Set(cpu, Gpr.Eax, 0x11111111); Set(cpu, Gpr.Ebx, 0x22222222); Flags(cpu, FlagBits.Reserved1 | FlagBits.Zf); });
    }

    [Fact]
    public void CmovnzNotTaken()
    {
        AssertMatches(
            [0x0F, 0x45, 0xD8],
            cpu => { Set(cpu, Gpr.Eax, 0x11111111); Set(cpu, Gpr.Ebx, 0x22222222); Flags(cpu, FlagBits.Reserved1 | FlagBits.Zf); });
    }

    [Fact]
    public void CmovWord()
    {
        AssertMatches(
            [0x66, 0x0F, 0x4C, 0xD8],
            cpu => { Set(cpu, Gpr.Eax, 0x11112222); Set(cpu, Gpr.Ebx, 0x33334444); Flags(cpu, FlagBits.Reserved1); });
    }

    [Fact]
    public void BswapRegister()
    {
        AssertMatches([0x0F, 0xC8], cpu => Set(cpu, Gpr.Eax, 0x11223344));
    }

    [Fact]
    public void BsfFound()
    {
        AssertMatches([0x0F, 0xBC, 0xC3], cpu => { Set(cpu, Gpr.Eax, 0xAAAAAAAA); Set(cpu, Gpr.Ebx, 0x00000100); });
    }

    [Fact]
    public void BsfZeroKeepsDestination()
    {
        AssertMatches([0x0F, 0xBC, 0xC3], cpu => { Set(cpu, Gpr.Eax, 0xAAAAAAAA); Set(cpu, Gpr.Ebx, 0); });
    }

    [Fact]
    public void BsrFound()
    {
        AssertMatches([0x0F, 0xBD, 0xC3], cpu => { Set(cpu, Gpr.Eax, 0xAAAAAAAA); Set(cpu, Gpr.Ebx, 0x00080100); });
    }

    [Fact]
    public void BsrWordZero()
    {
        AssertMatches([0x66, 0x0F, 0xBD, 0xC3], cpu => { Set(cpu, Gpr.Eax, 0x12345678); Set(cpu, Gpr.Ebx, 0x00010000); });
    }

    [Fact]
    public void ArplAdjusts()
    {
        AssertMatches([0x63, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x0010); Set(cpu, Gpr.Ebx, 0x0003); });
    }

    [Fact]
    public void ArplNoAdjust()
    {
        AssertMatches([0x63, 0xD8], cpu => { Set(cpu, Gpr.Eax, 0x0013); Set(cpu, Gpr.Ebx, 0x0001); });
    }

    [Fact]
    public void Cwde()
    {
        AssertMatches([0x98], cpu => Set(cpu, Gpr.Eax, 0x12348000));
    }

    [Fact]
    public void Cbw()
    {
        AssertMatches([0x66, 0x98], cpu => Set(cpu, Gpr.Eax, 0x12345680));
    }

    [Fact]
    public void Cdq()
    {
        AssertMatches([0x99], cpu => Set(cpu, Gpr.Eax, 0x80000000));
    }

    [Fact]
    public void Cwd()
    {
        AssertMatches([0x66, 0x99], cpu => Set(cpu, Gpr.Eax, 0x12348000));
    }

    [Fact]
    public void Xlat()
    {
        AssertMatches(
            [0xD7],
            cpu => { Set(cpu, Gpr.Eax, 0x11223305); Set(cpu, Gpr.Ebx, 0x3000); cpu.Ram().Write8(0x3005, 0x9A); });
    }

    [Fact]
    public void CldClearsDirection()
    {
        AssertMatches([0xFC], cpu => Flags(cpu, FlagBits.Reserved1 | FlagBits.Df | FlagBits.Cf));
    }

    [Fact]
    public void StdSetsDirection()
    {
        AssertMatches([0xFD], cpu => Flags(cpu, FlagBits.Reserved1));
    }

    [Fact]
    public void CmcTogglesCarry()
    {
        AssertMatches([0xF5], cpu => Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Zf));
    }

    [Fact]
    public void StcSetsCarry()
    {
        AssertMatches([0xF9], cpu => Flags(cpu, FlagBits.Reserved1));
    }

    [Fact]
    public void PushfPopfRoundTrip()
    {
        AssertMatches(
            [0x9C, 0x9D],
            cpu => Flags(cpu, FlagBits.Reserved1 | FlagBits.Cf | FlagBits.Pf | FlagBits.Af | FlagBits.Zf | FlagBits.Sf | FlagBits.Of));
    }

    [Fact]
    public void PushaPopaRoundTrip()
    {
        AssertMatches(
            [0x60, 0x61],
            cpu =>
            {
                Set(cpu, Gpr.Eax, 0x11111111);
                Set(cpu, Gpr.Ebx, 0x22222222);
                Set(cpu, Gpr.Ecx, 0x33333333);
                Set(cpu, Gpr.Edx, 0x44444444);
                Set(cpu, Gpr.Esi, 0x55555555);
                Set(cpu, Gpr.Edi, 0x66666666);
                Set(cpu, Gpr.Ebp, 0x77777777);
            },
            (interpreter, tcg) =>
            {
                Assert.Equal(interpreter.Ram().Read32(0x7FC0), tcg.Ram().Read32(0x7FC0));
                Assert.Equal(interpreter.Ram().Read32(0x7FD8), tcg.Ram().Read32(0x7FD8));
            });
    }

    [Fact]
    public void LeaveRestoresFrame()
    {
        AssertMatches(
            [0xC9],
            cpu => { Memory32(cpu, 0x7FFC, 0x11223344); Memory32(cpu, 0x8000, 0x55667788); Set(cpu, Gpr.Ebp, 0x7FFC); Set(cpu, Gpr.Esp, 0x7000); });
    }

    [Fact]
    public void LoopTaken()
    {
        AssertMatches([0xE2, 0xFE], cpu => Set(cpu, Gpr.Ecx, 3));
    }

    [Fact]
    public void LoopFallsThroughAtZero()
    {
        AssertMatches([0xE2, 0xFE], cpu => Set(cpu, Gpr.Ecx, 0));
    }

    [Fact]
    public void LoopeNotTakenWithoutZero()
    {
        AssertMatches([0xE1, 0xFE], cpu => { Set(cpu, Gpr.Ecx, 3); Flags(cpu, FlagBits.Reserved1); });
    }

    [Fact]
    public void JecxzTaken()
    {
        AssertMatches([0x67, 0xE3, 0xFE], cpu => Set(cpu, Gpr.Ecx, 0x12340000));
    }

    [Fact]
    public void JecxzNotTaken()
    {
        AssertMatches([0xE3, 0xFE], cpu => Set(cpu, Gpr.Ecx, 1));
    }
}
