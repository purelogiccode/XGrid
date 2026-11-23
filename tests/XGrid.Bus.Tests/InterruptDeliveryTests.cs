using XGrid.Cpu;
using XGrid.Memory;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class InterruptDeliveryTests
{
    [Fact]
    public void BusAdvanceTotal_IsIndependentOfBatching()
    {
        var single = new XboxBus();
        single.Advance(1000000);

        var batched = new XboxBus();
        for (int i = 0; i < 1000; i++)
        {
            batched.Advance(1000);
        }

        Assert.Equal(single.Lpc.PmTimerCounter, batched.Lpc.PmTimerCounter);
    }

    [Fact]
    public void PicInterruptTransfersToHandler()
    {
        var memory = new PhysicalMemory(0x10000);
        var space = new AddressSpace(memory, UnmappedAccessPolicy.Throw);
        var bus = new XboxBus();
        var cpu = new XboxCpu(space, bus.Io);

        cpu.Reset();
        cpu.Context.Cr0 = 1;
        cpu.Context.CodeSegment32 = true;
        cpu.Context.SetSegment(SegmentRegister.Cs, 0x08, 0);
        cpu.Context.SetSegment(SegmentRegister.Ds, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Ss, 0x10, 0);
        cpu.Context.Eflags = FlagBits.Reserved1 | FlagBits.If;
        cpu.Context.Esp = 0x8000;

        memory.Write8(0x1000, 0x90);
        memory.Write8(0x1001, 0x90);
        cpu.Context.Eip = 0x1000;

        cpu.Context.Idtr.Base = 0x6000;
        cpu.Context.Idtr.Limit = 0x7FF;
        uint gate = 0x6000 + (0x20 * 8);
        memory.Write16(gate, 0x3000);
        memory.Write16(gate + 2, 0x08);
        memory.Write16(gate + 4, 0x8E00);
        memory.Write16(gate + 6, 0x0000);

        InitializePic(bus);
        bus.Io.Write8(0x21, 0xFE);
        cpu.InterruptSource = bus.Pic;

        bus.Pic.SetIrq(0, true);
        cpu.Step();

        Assert.Equal(0x3000u, cpu.Context.Eip);
        Assert.Equal(0x01, bus.Pic.MasterInService);
        Assert.Equal(0x8000u - 12u, cpu.Context.Esp);
    }

    [Fact]
    public void MaskedInterruptDoesNotTransfer()
    {
        var memory = new PhysicalMemory(0x10000);
        var space = new AddressSpace(memory, UnmappedAccessPolicy.Throw);
        var bus = new XboxBus();
        var cpu = new XboxCpu(space, bus.Io);

        cpu.Reset();
        cpu.Context.Cr0 = 1;
        cpu.Context.SetSegment(SegmentRegister.Cs, 0x08, 0);
        cpu.Context.SetSegment(SegmentRegister.Ss, 0x10, 0);
        cpu.Context.Eflags = FlagBits.Reserved1 | FlagBits.If;
        cpu.Context.Esp = 0x8000;
        memory.Write8(0x1000, 0x90);
        cpu.Context.Eip = 0x1000;
        cpu.Context.Idtr.Base = 0x6000;
        cpu.Context.Idtr.Limit = 0x7FF;

        InitializePic(bus);
        bus.Io.Write8(0x21, 0xFF);
        cpu.InterruptSource = bus.Pic;

        bus.Pic.SetIrq(0, true);
        cpu.Step();

        Assert.Equal(0x1001u, cpu.Context.Eip);
    }

    private static void InitializePic(XboxBus bus)
    {
        bus.Io.Write8(0x20, 0x11);
        bus.Io.Write8(0x21, 0x20);
        bus.Io.Write8(0x21, 0x04);
        bus.Io.Write8(0x21, 0x01);
        bus.Io.Write8(0xA0, 0x11);
        bus.Io.Write8(0xA1, 0x28);
        bus.Io.Write8(0xA1, 0x02);
        bus.Io.Write8(0xA1, 0x01);
    }
}
