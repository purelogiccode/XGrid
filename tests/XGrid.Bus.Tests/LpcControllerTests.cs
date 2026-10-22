using XGrid.Bus.Io;
using XGrid.Bus.Pci;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class LpcControllerTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 9)]
    [InlineData(2, 4)]
    [InlineData(3, 5)]
    [InlineData(4, 6)]
    [InlineData(5, 0)]
    [InlineData(6, 14)]
    public void InternalRoutingDefaultsResolve(int index, int expectedIrq)
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);

        Assert.Equal(expectedIrq, lpc.ResolveIrq(index));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 9)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(9, 14)]
    [InlineData(30, 3)]
    public void SlotRoutingResolvesCanonicalIrqs(int slot, int expectedIrq)
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);

        Assert.Equal(expectedIrq, lpc.ResolveSlotIrq(slot));
    }

    [Fact]
    public void AcpiRoutingDefaultsToZeroAndReadsProgrammedBytes()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);

        Assert.Equal(0, lpc.ResolveAcpiIrq(1));

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x64), 4, 0x00000900);

        Assert.Equal(0x09, lpc.ResolveAcpiIrq(1));
        Assert.Equal(0, lpc.ResolveAcpiIrq(4));
    }

    [Fact]
    public void RomOverlayDisablesOnBitOne()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);
        bool raised = false;
        lpc.RomOverlayDisabled += () => raised = true;

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x80), 1, 0x01);
        Assert.True(lpc.RomOverlayEnabled);
        Assert.False(raised);

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x80), 1, 0x02);
        Assert.False(lpc.RomOverlayEnabled);
        Assert.True(raised);
    }

    [Fact]
    public void PmTimerAdvancesAndWraps()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);

        lpc.AdvancePmTimer(0xFFFFFE);
        Assert.Equal(0xFFFFFEu, lpc.PmTimerCounter);

        lpc.AdvancePmTimer(4);
        Assert.Equal(2u, lpc.PmTimerCounter);
    }

    [Fact]
    public void Pm1StatusIsWriteOneToClear()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci)
        {
            Pm1Status = 0x0003
        };

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x10), 4, 0x8000);
        io.Write16(0x8000, 0x0001);

        Assert.Equal(0x0002, lpc.Pm1Status);
    }

    [Fact]
    public void PmEnableIsReadableThroughWindow()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x10), 4, 0x8000);
        io.Write16(0x8002, 0x0001);

        Assert.Equal(0x0001, io.Read16(0x8002));
        Assert.Equal(0x0001, lpc.Pm1Enable);
    }
}
