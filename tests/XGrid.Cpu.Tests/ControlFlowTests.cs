using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class ControlFlowTests
{
    [Fact]
    public void Jmp_Short()
    {
        XboxCpu cpu = CpuHarness.Create(0xEB, 0x02, 0x90, 0x90, 0x90);

        cpu.Step();

        Assert.Equal(0x1004u, cpu.Context.Eip);
    }

    [Fact]
    public void Jcc_Taken()
    {
        XboxCpu cpu = CpuHarness.Create(0x74, 0x02, 0x90, 0x90, 0x90);
        cpu.Context.Eflags = FlagBits.Zf;

        cpu.Step();

        Assert.Equal(0x1004u, cpu.Context.Eip);
    }

    [Fact]
    public void Jcc_NotTaken()
    {
        XboxCpu cpu = CpuHarness.Create(0x74, 0x02, 0x90, 0x90, 0x90);
        cpu.Context.Eflags = 0;

        cpu.Step();

        Assert.Equal(0x1002u, cpu.Context.Eip);
    }

    [Fact]
    public void Call_And_Ret_RestoreStack()
    {
        XboxCpu cpu = CpuHarness.Create(0xE8, 0x02, 0x00, 0x00, 0x00, 0x90, 0xF4, 0xC3);

        cpu.Execute(10);

        Assert.True(cpu.Context.Halted);
        Assert.Equal(0x1007u, cpu.Context.Eip);
        Assert.Equal(0x8000u, cpu.Context.ReadRegister(Gpr.Esp));
    }

    [Fact]
    public void Ret_Immediate_AdjustsStack()
    {
        XboxCpu cpu = CpuHarness.Create(0xE8, 0x02, 0x00, 0x00, 0x00, 0x90, 0xF4, 0xC2, 0x04, 0x00);

        cpu.Execute(10);

        Assert.True(cpu.Context.Halted);
        Assert.Equal(0x1007u, cpu.Context.Eip);
        Assert.Equal(0x8004u, cpu.Context.ReadRegister(Gpr.Esp));
    }

    [Fact]
    public void Loop_DecrementsEcxUntilZero()
    {
        XboxCpu cpu = CpuHarness.Create(0xB9, 0x03, 0x00, 0x00, 0x00, 0xE2, 0xFE, 0xF4);

        cpu.Execute(100);

        Assert.True(cpu.Context.Halted);
        Assert.Equal(0u, cpu.Context.ReadRegister(Gpr.Ecx));
    }

    [Fact]
    public void Jecxz_TakenWhenEcxZero()
    {
        XboxCpu cpu = CpuHarness.Create(0xE3, 0x02, 0x90, 0x90, 0x90);
        cpu.Context.WriteRegister(Gpr.Ecx, 0);

        cpu.Step();

        Assert.Equal(0x1004u, cpu.Context.Eip);
    }

    [Fact]
    public void Jecxz_NotTakenWhenEcxNonZero()
    {
        XboxCpu cpu = CpuHarness.Create(0xE3, 0x02, 0x90, 0x90, 0x90);
        cpu.Context.WriteRegister(Gpr.Ecx, 1);

        cpu.Step();

        Assert.Equal(0x1002u, cpu.Context.Eip);
    }

    [Fact]
    public void Pushf_Pop_ExposesFlags()
    {
        XboxCpu cpu = CpuHarness.Create(0xF9, 0x9C, 0x58);

        cpu.Execute(3);

        Assert.True((cpu.Context.ReadRegister(Gpr.Eax) & FlagBits.Cf) != 0);
    }

    [Fact]
    public void Indirect_Jmp_ThroughRegister()
    {
        XboxCpu cpu = CpuHarness.Create(0xFF, 0xE0);
        cpu.Context.WriteRegister(Gpr.Eax, 0x2000);

        cpu.Step();

        Assert.Equal(0x2000u, cpu.Context.Eip);
    }
}
