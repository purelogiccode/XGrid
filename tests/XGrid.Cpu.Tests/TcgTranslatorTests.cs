using System;
using XGrid.Cpu.Tcg;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// P1S3: the managed TCG IR and the first i386 translator shapes are
/// differentially checked against the real interpreter (registers, EFLAGS and
/// EIP must match exactly).
/// </summary>
public sealed class TcgTranslatorTests
{
    private static void AssertMatches(byte[] code, Action<XboxCpu>? setup = null)
    {
        XboxCpu cpu = CpuHarness.Create(code);
        setup?.Invoke(cpu);
        uint eip = cpu.Context.Eip;
        var initial = new uint[8];
        for (int i = 0; i < 8; i++)
        {
            initial[i] = cpu.Context.ReadRegister((Gpr)i);
        }

        uint initialFlags = cpu.Context.Eflags;
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

        TcgResult result = TcgInterpreter.Execute(builder.Build(), initial, initialFlags);

        cpu.Step();
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(cpu.Context.ReadRegister((Gpr)i), result.Registers[i]);
        }

        Assert.Equal(cpu.Context.Eflags, result.Eflags);
        Assert.Equal(cpu.Context.Eip, result.Eip);
    }

    private static void Set(XboxCpu cpu, Gpr register, uint value)
    {
        cpu.Context.WriteRegister(register, value);
    }

    private static void Flags(XboxCpu cpu, uint flags)
    {
        cpu.Context.Eflags = flags;
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
    public void UnsupportedMemoryForm_ReturnsFalse()
    {
        DecodedInstruction decoded = Decoder.Decode([0x8B, 0x00]);
        var builder = new TcgBuilder(0x1000);
        Assert.False(I386Translator.TryTranslate(decoded, 0x1000, builder, out _, out string? reason));
        Assert.NotNull(reason);
    }
}
