using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class ShiftTests
{
    [Fact]
    public void Shl_ByOne_SetsCarryAndOverflow()
    {
        XboxCpu cpu = CpuHarness.Create(0xD1, 0xE0);
        cpu.Context.WriteRegister(Gpr.Eax, 0x80000000);

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.CF) != 0);
        Assert.True((cpu.Context.Eflags & FlagBits.OF) != 0);
        Assert.True((cpu.Context.Eflags & FlagBits.ZF) != 0);
    }

    [Fact]
    public void Shl_ByCl()
    {
        XboxCpu cpu = CpuHarness.Create(0xD3, 0xE0);
        cpu.Context.WriteRegister(Gpr.Eax, 1);
        cpu.Context.WriteRegister(Gpr.Ecx, 4);

        cpu.Step();

        Assert.Equal(0x10u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Shl_ByImmediate()
    {
        XboxCpu cpu = CpuHarness.Create(0xC1, 0xE0, 0x04);
        cpu.Context.WriteRegister(Gpr.Eax, 1);

        cpu.Step();

        Assert.Equal(0x10u, cpu.Context.ReadRegister(Gpr.Eax));
    }

    [Fact]
    public void Shl_ByZero_LeavesFlags()
    {
        XboxCpu cpu = CpuHarness.Create(0xC1, 0xE0, 0x00);
        cpu.Context.WriteRegister(Gpr.Eax, 0x1234);
        cpu.Context.Eflags = FlagBits.CF;

        cpu.Step();

        Assert.Equal(0x1234u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.CF) != 0);
    }

    [Fact]
    public void Shl_Byte_TruncatesToByte()
    {
        XboxCpu cpu = CpuHarness.Create(0xD0, 0xE0);
        cpu.Context.WriteRegister(Gpr.Eax, 0x00000080);

        cpu.Step();

        Assert.Equal(0x00000000u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.CF) != 0);
    }

    [Fact]
    public void Shr_ByOne()
    {
        XboxCpu cpu = CpuHarness.Create(0xD1, 0xE8);
        cpu.Context.WriteRegister(Gpr.Eax, 2);

        cpu.Step();

        Assert.Equal(1u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.False((cpu.Context.Eflags & FlagBits.CF) != 0);
    }

    [Fact]
    public void Shr_SetsOverflowFromOriginalMsb()
    {
        XboxCpu cpu = CpuHarness.Create(0xD1, 0xE8);
        cpu.Context.WriteRegister(Gpr.Eax, 0x80000000);

        cpu.Step();

        Assert.Equal(0x40000000u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.OF) != 0);
    }

    [Fact]
    public void Sar_SignFills()
    {
        XboxCpu cpu = CpuHarness.Create(0xD1, 0xF8);
        cpu.Context.WriteRegister(Gpr.Eax, 0xFFFFFFFE);

        cpu.Step();

        Assert.Equal(0xFFFFFFFFu, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.False((cpu.Context.Eflags & FlagBits.OF) != 0);
    }

    [Fact]
    public void Rol_ByOne()
    {
        XboxCpu cpu = CpuHarness.Create(0xD1, 0xC0);
        cpu.Context.WriteRegister(Gpr.Eax, 0x80000001);

        cpu.Step();

        Assert.Equal(0x00000003u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.CF) != 0);
        Assert.True((cpu.Context.Eflags & FlagBits.OF) != 0);
    }

    [Fact]
    public void Ror_ByOne()
    {
        XboxCpu cpu = CpuHarness.Create(0xD1, 0xC8);
        cpu.Context.WriteRegister(Gpr.Eax, 0x00000001);

        cpu.Step();

        Assert.Equal(0x80000000u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.CF) != 0);
        Assert.True((cpu.Context.Eflags & FlagBits.OF) != 0);
    }

    [Fact]
    public void Rcl_ByOne_RotatesThroughCarry()
    {
        XboxCpu cpu = CpuHarness.Create(0xD1, 0xD0);
        cpu.Context.WriteRegister(Gpr.Eax, 0x80000000);
        cpu.Context.Eflags = 0;

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.CF) != 0);
        Assert.True((cpu.Context.Eflags & FlagBits.OF) != 0);
    }

    [Fact]
    public void Rcr_ByOne_RotatesThroughCarry()
    {
        XboxCpu cpu = CpuHarness.Create(0xD1, 0xD8);
        cpu.Context.WriteRegister(Gpr.Eax, 1);
        cpu.Context.Eflags = 0;

        cpu.Step();

        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.True((cpu.Context.Eflags & FlagBits.CF) != 0);
        Assert.False((cpu.Context.Eflags & FlagBits.OF) != 0);
    }
}
