using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class StringInstructionTests
{
    [Fact]
    public void Movsb_CopiesByte()
    {
        XboxCpu cpu = CpuHarness.Create(0xA4);
        cpu.Context.WriteRegister(Gpr.Esi, 0x2000);
        cpu.Context.WriteRegister(Gpr.Edi, 0x3000);
        cpu.Bus().Write8(0x2000, 0x5A);

        cpu.Step();

        Assert.Equal(0x5A, cpu.Bus().Read8(0x3000));
        Assert.Equal(0x2001u, cpu.Context.ReadRegister(Gpr.Esi));
        Assert.Equal(0x3001u, cpu.Context.ReadRegister(Gpr.Edi));
    }

    [Fact]
    public void Movsd_CopiesDword()
    {
        XboxCpu cpu = CpuHarness.Create(0xA5);
        cpu.Context.WriteRegister(Gpr.Esi, 0x2000);
        cpu.Context.WriteRegister(Gpr.Edi, 0x3000);
        cpu.Bus().Write32(0x2000, 0x11223344);

        cpu.Step();

        Assert.Equal(0x11223344u, cpu.Bus().Read32(0x3000));
        Assert.Equal(0x2004u, cpu.Context.ReadRegister(Gpr.Esi));
        Assert.Equal(0x3004u, cpu.Context.ReadRegister(Gpr.Edi));
    }

    [Fact]
    public void RepMovsb_CopiesCount()
    {
        XboxCpu cpu = CpuHarness.Create(0xF3, 0xA4);
        cpu.Context.WriteRegister(Gpr.Ecx, 4);
        cpu.Context.WriteRegister(Gpr.Esi, 0x2000);
        cpu.Context.WriteRegister(Gpr.Edi, 0x3000);
        cpu.Bus().Write32(0x2000, 0x04030201);

        cpu.Step();

        Assert.Equal(0x04030201u, cpu.Bus().Read32(0x3000));
        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Ecx));
        Assert.Equal(0x2004u, cpu.Context.ReadRegister(Gpr.Esi));
        Assert.Equal(0x3004u, cpu.Context.ReadRegister(Gpr.Edi));
    }

    [Fact]
    public void DirectionFlag_DecrementsIndexes()
    {
        XboxCpu cpu = CpuHarness.Create(0xA4);
        cpu.Context.WriteRegister(Gpr.Esi, 0x2000);
        cpu.Context.WriteRegister(Gpr.Edi, 0x3000);
        cpu.Context.Eflags = FlagBits.Df;

        cpu.Step();

        Assert.Equal(0x1FFFu, cpu.Context.ReadRegister(Gpr.Esi));
        Assert.Equal(0x2FFFu, cpu.Context.ReadRegister(Gpr.Edi));
    }

    [Fact]
    public void Stosb_WritesAccumulator()
    {
        XboxCpu cpu = CpuHarness.Create(0xAA);
        cpu.Context.WriteRegister(Gpr.Edi, 0x3000);
        cpu.Context.WriteRegister(Gpr.Eax, 0x42);

        cpu.Step();

        Assert.Equal(0x42, cpu.Bus().Read8(0x3000));
        Assert.Equal(0x3001u, cpu.Context.ReadRegister(Gpr.Edi));
    }

    [Fact]
    public void Lodsd_LoadsAccumulator()
    {
        XboxCpu cpu = CpuHarness.Create(0xAD);
        cpu.Context.WriteRegister(Gpr.Esi, 0x2000);
        cpu.Bus().Write32(0x2000, 0xDEADBEEF);

        cpu.Step();

        Assert.Equal(0xDEADBEEFu, cpu.Context.ReadRegister(Gpr.Eax));
        Assert.Equal(0x2004u, cpu.Context.ReadRegister(Gpr.Esi));
    }

    [Fact]
    public void Scasb_ComparesAccumulator()
    {
        XboxCpu cpu = CpuHarness.Create(0xAE);
        cpu.Context.WriteRegister(Gpr.Edi, 0x3000);
        cpu.Context.WriteRegister(Gpr.Eax, 0x10);
        cpu.Bus().Write8(0x3000, 0x10);

        cpu.Step();

        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
        Assert.Equal(0x3001u, cpu.Context.ReadRegister(Gpr.Edi));
    }

    [Fact]
    public void Cmpsb_ComparesOperands()
    {
        XboxCpu cpu = CpuHarness.Create(0xA6);
        cpu.Context.WriteRegister(Gpr.Esi, 0x2000);
        cpu.Context.WriteRegister(Gpr.Edi, 0x3000);
        cpu.Bus().Write8(0x2000, 0x20);
        cpu.Bus().Write8(0x3000, 0x20);

        cpu.Step();

        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void RepneScasb_FindsByte()
    {
        XboxCpu cpu = CpuHarness.Create(0xF2, 0xAE);
        cpu.Context.WriteRegister(Gpr.Ecx, 4);
        cpu.Context.WriteRegister(Gpr.Edi, 0x3000);
        cpu.Context.WriteRegister(Gpr.Eax, 3);
        cpu.Bus().Write8(0x3000, 1);
        cpu.Bus().Write8(0x3001, 2);
        cpu.Bus().Write8(0x3002, 3);

        cpu.Step();

        Assert.Equal(1u, cpu.Context.ReadRegister(Gpr.Ecx));
        Assert.Equal(0x3003u, cpu.Context.ReadRegister(Gpr.Edi));
        Assert.True((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void RepeCmpsb_StopsOnDifference()
    {
        XboxCpu cpu = CpuHarness.Create(0xF3, 0xA6);
        cpu.Context.WriteRegister(Gpr.Ecx, 4);
        cpu.Context.WriteRegister(Gpr.Esi, 0x2000);
        cpu.Context.WriteRegister(Gpr.Edi, 0x3000);
        cpu.Bus().Write8(0x2000, 1);
        cpu.Bus().Write8(0x2001, 2);
        cpu.Bus().Write8(0x3000, 1);
        cpu.Bus().Write8(0x3001, 9);

        cpu.Step();

        Assert.Equal(2u, cpu.Context.ReadRegister(Gpr.Ecx));
        Assert.False((cpu.Context.Eflags & FlagBits.Zf) != 0);
    }
}
