using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class XboxContextTests
{
    [Fact]
    public void Reset_UsesResetVector()
    {
        var context = new XboxContext();
        context.WriteRegister(Gpr.Eax, 0x1234);
        context.Cr0 = 1;

        context.Reset();

        Assert.Equal(0xFFF0u, context.Eip);
        Assert.Equal(0xF000, context.GetSegmentSelector(SegmentRegister.Cs));
        Assert.Equal(0xFFFF0000u, context.GetSegmentBase(SegmentRegister.Cs));
        Assert.Equal(FlagBits.Reserved1, context.Eflags);
        Assert.Equal(0u, context.ReadRegister(Gpr.Eax));
        Assert.Equal(0u, context.Cr0);
    }

    [Fact]
    public void WriteRegister8_LowAndHigh()
    {
        var context = new XboxContext();

        context.WriteRegister8(0, 0x34);
        context.WriteRegister8(4, 0x12);

        Assert.Equal(0x00001234u, context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x34, context.ReadRegister8(0));
        Assert.Equal(0x12, context.ReadRegister8(4));
    }

    [Fact]
    public void WriteRegister16_PreservesHighBits()
    {
        var context = new XboxContext();
        context.WriteRegister(Gpr.Eax, 0xFFFF0000);

        context.WriteRegister16(Gpr.Eax, 0x1234);

        Assert.Equal(0xFFFF1234u, context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void WriteRegister32_OverwritesAll()
    {
        var context = new XboxContext();
        context.WriteRegister(Gpr.Eax, 0xFFFFFFFF);

        context.WriteRegister(Gpr.Eax, 0x12345678);

        Assert.Equal(0x12345678u, context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void ProtectedMode_DependsOnCr0Pe()
    {
        var context = new XboxContext();

        Assert.False(context.IsProtectedMode);
        Assert.Equal(16, context.DefaultOperandSize);
        Assert.Equal(16, context.DefaultAddressSize);

        context.Cr0 = 1;

        Assert.True(context.IsProtectedMode);
        Assert.Equal(16, context.DefaultOperandSize);

        context.CodeSegment32 = true;

        Assert.Equal(32, context.DefaultOperandSize);
        Assert.Equal(32, context.DefaultAddressSize);
    }

    [Fact]
    public void DebugRegisters_RoundTrip()
    {
        var context = new XboxContext();

        context.WriteDebugRegister(3, 0xCAFEBABE);

        Assert.Equal(0xCAFEBABEu, context.ReadDebugRegister(3));
    }
}
