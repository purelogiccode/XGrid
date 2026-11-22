using System;
using XGrid.Cpu.Tcg;
using XGrid.Memory;
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
        Action<XboxCpu, XboxCpu>? verify = null)
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

        TcgResult result = TcgInterpreter.Execute(
            builder.Build(),
            initial,
            initialFlags,
            segments,
            new TestMemory(tcg.Bus()));

        interpreter.Step();
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(interpreter.Context.ReadRegister((Gpr)i), result.Registers[i]);
        }

        Assert.Equal(interpreter.Context.Eflags, result.Eflags);
        Assert.Equal(interpreter.Context.Eip, result.Eip);
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
    public void Unsupported16BitAddress_ReturnsFalse()
    {
        // 8B 00 = mov eax, [bx+si] with a 0x67 address-size override in 32-bit mode:
        // the decoder marks Is16Bit; the translator declines it.
        DecodedInstruction decoded = Decoder.Decode([0x67, 0x8B, 0x00]);
        var builder = new TcgBuilder(0x1000);
        Assert.False(I386Translator.TryTranslate(decoded, 0x1000, builder, out _, out string? reason));
        Assert.NotNull(reason);
    }

    [Fact]
    public void UnsupportedMemoryForm_ReturnsFalse()
    {
        // MOVSB: a string instruction the first slice does not translate.
        DecodedInstruction decoded = Decoder.Decode([0xA4]);
        var builder = new TcgBuilder(0x1000);
        Assert.False(I386Translator.TryTranslate(decoded, 0x1000, builder, out _, out string? reason));
        Assert.NotNull(reason);
    }

    private sealed class TestMemory(AddressSpace bus) : ITcgMemory
    {
        public byte Read8(uint address)
        {
            return bus.Read8(address);
        }

        public ushort Read16(uint address)
        {
            return (ushort)(bus.Read8(address) | (bus.Read8(address + 1) << 8));
        }

        public uint Read32(uint address)
        {
            return (uint)(bus.Read8(address)
                | (bus.Read8(address + 1) << 8)
                | (bus.Read8(address + 2) << 16)
                | (bus.Read8(address + 3) << 24));
        }

        public void Write8(uint address, byte value)
        {
            bus.Write8(address, value);
        }

        public void Write16(uint address, ushort value)
        {
            bus.Write8(address, (byte)value);
            bus.Write8(address + 1, (byte)(value >> 8));
        }

        public void Write32(uint address, uint value)
        {
            bus.Write8(address, (byte)value);
            bus.Write8(address + 1, (byte)(value >> 8));
            bus.Write8(address + 2, (byte)(value >> 16));
            bus.Write8(address + 3, (byte)(value >> 24));
        }
    }
}
