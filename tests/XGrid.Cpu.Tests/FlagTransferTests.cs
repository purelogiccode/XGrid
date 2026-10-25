using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// SAHF / LAHF (one-byte 0x9E / 0x9F). The x87 FPREM loop every retail title
/// runs (`fprem; fnstsw ax; sahf; jp`) needs SAHF, and the kernel's exception
/// state needs LAHF; both were missing from the decoder, so the first SAHF
/// raised #UD (TM3: 3 of the 7 oracle titles). The transfer rules follow the
/// reference `gen_SAHF`/`gen_LAHF` (xemu 0.8.139 target/i386/tcg/emit.c.inc):
/// the five condition flags move, OF and every non-condition bit stay.
/// </summary>
public sealed class FlagTransferTests
{
    private const uint ConditionMask = FlagBits.Cf | FlagBits.Pf | FlagBits.Af | FlagBits.Zf | FlagBits.Sf;

    private static DecodedInstruction Decode(params byte[] code)
    {
        return Decoder.Decode(code);
    }

    [Fact]
    public void Sahf_IsDecoded()
    {
        DecodedInstruction instruction = Decode(0x9E, 0x90);

        Assert.True(instruction.IsValid);
        Assert.Equal(Mnemonic.Sahf, instruction.Mnemonic);
        Assert.Equal(1, instruction.Length);
        Assert.Equal(OperandKind.None, instruction.Operand1.Kind);
        Assert.Equal(OperandKind.None, instruction.Operand2.Kind);
    }

    [Fact]
    public void Lahf_IsDecoded()
    {
        DecodedInstruction instruction = Decode(0x9F, 0x90);

        Assert.True(instruction.IsValid);
        Assert.Equal(Mnemonic.Lahf, instruction.Mnemonic);
        Assert.Equal(1, instruction.Length);
    }

    [Theory]
    [InlineData(0xD5u)] // every condition flag set
    [InlineData(0x00u)] // every condition flag clear
    [InlineData(0x55u)] // SF clear; ZF/AF/PF/CF set
    [InlineData(0x80u)] // SF only
    [InlineData(0x04u)] // PF only (the FPREM loop's jp input)
    public void Sahf_LoadsTheConditionFlagsFromAh(uint ah)
    {
        XboxCpu cpu = CpuHarness.Create(0x9E, 0x90);
        cpu.Context.Eax = ah << 8;
        cpu.Context.Eflags = FlagBits.Reserved1 | FlagBits.Of | FlagBits.If | FlagBits.Df;

        cpu.Step();

        Assert.Equal(ah & ConditionMask, cpu.Context.Eflags & ConditionMask);
        Assert.NotEqual(0u, cpu.Context.Eflags & FlagBits.Of);
        Assert.NotEqual(0u, cpu.Context.Eflags & FlagBits.If);
        Assert.NotEqual(0u, cpu.Context.Eflags & FlagBits.Df);
        Assert.Equal(ah, (cpu.Context.Eax >> 8) & 0xFF);
    }

    [Theory]
    [InlineData(0x202u, 0x02u)]
    [InlineData(0x8D7u, 0xD7u)]
    [InlineData(0x8D5u, 0xD7u)]
    [InlineData(0x8D0u, 0xD2u)] // OF set alone must not reach AH
    public void Lahf_StoresTheConditionFlagsAndReservedBit(uint eflags, uint expectedAh)
    {
        XboxCpu cpu = CpuHarness.Create(0x9F, 0x90);
        cpu.Context.Eax = 0x12345678;
        cpu.Context.Eflags = eflags;

        cpu.Step();

        Assert.Equal(expectedAh, (cpu.Context.Eax >> 8) & 0xFF);
        Assert.Equal(0x78u, cpu.Context.Eax & 0xFF);
        Assert.Equal(0x1234u, cpu.Context.Eax >> 16);
        Assert.Equal(eflags, cpu.Context.Eflags);
    }

    [Fact]
    public void Sahf_DrivesTheFpremLoopParityBranch()
    {
        // The retail FPREM tail: sahf; jp back. Parity comes from AH bit 2.
        byte[] taken = [0x9E, 0x7A, 0x02, 0x90, 0x90, 0x90];
        XboxCpu jump = CpuHarness.Create(taken);
        jump.Context.Eax = 0x00000400;
        jump.Step();
        jump.Step();
        Assert.Equal(0x1005u, jump.Context.Eip);

        XboxCpu fall = CpuHarness.Create(taken);
        fall.Context.Eax = 0x00000000;
        fall.Step();
        fall.Step();
        Assert.Equal(0x1003u, fall.Context.Eip);
    }

    [Fact]
    public void LahfSahf_RoundTripsTheConditionFlags()
    {
        // LAHF stores 0x02 for an all-clear condition state, so AH starts there
        // and the pair must be a no-op on both the flags and the register.
        XboxCpu cpu = CpuHarness.Create(0x9F, 0x9E, 0x90);
        cpu.Context.Eax = 0xDEAD0200;
        cpu.Context.Eflags = FlagBits.Reserved1 | FlagBits.Of | FlagBits.If | FlagBits.Df;

        cpu.Step();
        cpu.Step();

        Assert.Equal(FlagBits.Reserved1 | FlagBits.Of | FlagBits.If | FlagBits.Df, cpu.Context.Eflags);
        Assert.Equal(0xDEAD0200u, cpu.Context.Eax);
    }
}
