using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class InterruptTests
{
    private const uint IdtAddress = 0x4000;
    private const uint HandlerAddress = 0x3000;

    private static void InstallIdt(XboxCpu cpu, int vector, uint handler, ushort selector = 0x08,
        ushort attributes = 0x8E00)
    {
        uint gate = IdtAddress + (uint)vector * 8;
        cpu.Bus().Write16(gate, (ushort)(handler & 0xFFFF));
        cpu.Bus().Write16(gate + 2, selector);
        cpu.Bus().Write16(gate + 4, attributes);
        cpu.Bus().Write16(gate + 6, (ushort)(handler >> 16));
        cpu.Context.Idtr.Base = IdtAddress;
        cpu.Context.Idtr.Limit = 0x7FF;
    }

    [Fact]
    public void Lidt_LoadsInterruptTable()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x01, 0x1D, 0x00, 0x40, 0x00, 0x00);
        cpu.Bus().Write16(IdtAddress, 0x7FF);
        cpu.Bus().Write32(IdtAddress + 2, IdtAddress);

        cpu.Step();

        Assert.Equal(IdtAddress, cpu.Context.Idtr.Base);
        Assert.Equal(0x7FF, cpu.Context.Idtr.Limit);
    }

    [Fact]
    public void Sidt_StoresInterruptTable()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x01, 0x0D, 0x00, 0x50, 0x00, 0x00);
        cpu.Context.Idtr.Base = IdtAddress;
        cpu.Context.Idtr.Limit = 0x7FF;

        cpu.Step();

        Assert.Equal(0x7FF, cpu.Bus().Read16(0x5000));
        Assert.Equal(IdtAddress, cpu.Bus().Read32(0x5002));
    }

    [Fact]
    public void Int_TransfersToHandlerAndPushesFrame()
    {
        XboxCpu cpu = CpuHarness.Create(0xCD, 0x21, 0xF4);
        InstallIdt(cpu, 0x21, HandlerAddress);

        cpu.Step();

        Assert.Equal(HandlerAddress, cpu.Context.Eip);
        Assert.Equal(0x7FF4u, cpu.Context.ReadRegister(Gpr.Esp));
        Assert.Equal(0x1002u, cpu.Bus().Read32(0x7FF4));
        Assert.Equal(0x08u, cpu.Bus().Read32(0x7FF8));
        Assert.True((cpu.Bus().Read32(0x7FFC) & FlagBits.Reserved1) != 0);
    }

    [Fact]
    public void Iret_ReturnsFromInterrupt()
    {
        XboxCpu cpu = CpuHarness.Create(0xCD, 0x21, 0xF4);
        InstallIdt(cpu, 0x21, HandlerAddress);
        cpu.Bus().Write8(HandlerAddress, 0xCF);

        cpu.Step();
        cpu.Step();

        Assert.Equal(0x1002u, cpu.Context.Eip);
        Assert.Equal(0x8000u, cpu.Context.ReadRegister(Gpr.Esp));

        cpu.Step();

        Assert.True(cpu.Context.Halted);
    }

    [Fact]
    public void Int3_UsesVector3()
    {
        XboxCpu cpu = CpuHarness.Create(0xCC, 0xF4);
        InstallIdt(cpu, 3, HandlerAddress);

        cpu.Step();

        Assert.Equal(HandlerAddress, cpu.Context.Eip);
        Assert.Equal(0x1001u, cpu.Bus().Read32(0x7FF4));
    }

    [Fact]
    public void Into_TakenWhenOverflow()
    {
        XboxCpu cpu = CpuHarness.Create(0xCE, 0xF4);
        InstallIdt(cpu, 4, HandlerAddress);
        cpu.Context.Eflags = FlagBits.Of;

        cpu.Step();

        Assert.Equal(HandlerAddress, cpu.Context.Eip);
    }

    [Fact]
    public void Into_NotTakenWithoutOverflow()
    {
        XboxCpu cpu = CpuHarness.Create(0xCE, 0xF4);
        InstallIdt(cpu, 4, HandlerAddress);
        cpu.Context.Eflags = 0;

        cpu.Step();

        Assert.Equal(0x1001u, cpu.Context.Eip);
    }

    [Fact]
    public void DivideError_DeliversToVector0()
    {
        XboxCpu cpu = CpuHarness.Create(0xF7, 0xF3);
        InstallIdt(cpu, 0, HandlerAddress);
        cpu.Context.WriteRegister(Gpr.Ebx, 0);

        cpu.Step();

        Assert.Equal(HandlerAddress, cpu.Context.Eip);
        Assert.Equal(0x1000u, cpu.Bus().Read32(0x7FF4));
    }

    [Fact]
    public void UnsupportedOpcode_DeliversInvalidOpcode()
    {
        XboxCpu cpu = CpuHarness.Create(0x0F, 0x0B);
        InstallIdt(cpu, 6, HandlerAddress);

        cpu.Step();

        Assert.Equal(HandlerAddress, cpu.Context.Eip);
        Assert.Equal(0x1000u, cpu.Bus().Read32(0x7FF4));
    }

    [Fact]
    public void Int_ClearsInterruptAndTrapFlags()
    {
        XboxCpu cpu = CpuHarness.Create(0xCD, 0x21, 0xF4);
        InstallIdt(cpu, 0x21, HandlerAddress);
        cpu.Context.Eflags = FlagBits.If | FlagBits.Tf;

        cpu.Step();

        Assert.False((cpu.Context.Eflags & FlagBits.If) != 0);
        Assert.False((cpu.Context.Eflags & FlagBits.Tf) != 0);
    }

    [Fact]
    public void InvalidOpcode_RecordsLastFault()
    {
        XboxCpu cpu = CpuHarness.Create(0xD6);
        InstallIdt(cpu, 6, HandlerAddress);

        cpu.Step();

        Assert.Equal(6, cpu.LastFaultVector);
        Assert.Equal(CpuHarness.CodeAddress, cpu.LastFaultEip);
        Assert.Equal(HandlerAddress, cpu.Context.Eip);
    }
}
