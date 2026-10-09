using XGrid.Bus.Io;
using XGrid.Bus.Pci;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class PciBusTests
{
    [Fact]
    public void HostBridgeIdsAreReadable()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        pci.Register(new PciHostBridge());

        uint id = pci.ReadConfig(PciBus.MakeConfigAddress(0, 0, 0x00), 4);

        Assert.Equal(0x02A510DEu, id);
    }

    [Fact]
    public void UnknownSlotReturnsAllOnes()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);

        Assert.Equal(0xFFFFFFFFu, pci.ReadConfig(PciBus.MakeConfigAddress(7, 0, 0x00), 4));
    }

    [Fact]
    public void DisabledAddressReadsZero()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        pci.Register(new PciHostBridge());

        Assert.Equal(0u, pci.ReadConfig(0x00000000, 4));
    }

    [Fact]
    public void ConfigPortsRoundTrip()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        pci.Register(new PciHostBridge());

        io.Write32(PciBus.ConfigAddressPort, PciBus.MakeConfigAddress(0, 0, 0x00));

        Assert.Equal(0x02A510DEu, io.Read32(PciBus.ConfigDataPort));
    }

    [Fact]
    public void BarProbeReturnsSizeMask()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x10), 4, 0xFFFFFFFF);
        uint probed = pci.ReadConfig(PciBus.MakeConfigAddress(1, 0, 0x10), 4);

        Assert.Equal(0xFFFFFF01u, probed);

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x10), 4, 0x8000);
        uint assigned = pci.ReadConfig(PciBus.MakeConfigAddress(1, 0, 0x10), 4);

        Assert.Equal(0x8001u, assigned);
        Assert.Equal(0x8000u, lpc.GetBarBase(0));
    }

    [Fact]
    public void BarAssignmentRegistersIoWindow()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);
        lpc.AdvancePmTimer(1234);

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x10), 4, 0x8000);

        Assert.Equal(1234u, io.Read32(0x8008));
    }

    [Fact]
    public void CommandRegisterEnablesSpaces()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);

        Assert.False(lpc.MemorySpaceEnabled);
        Assert.False(lpc.IoSpaceEnabled);

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x04), 2, 0x0003);

        Assert.True(lpc.MemorySpaceEnabled);
        Assert.True(lpc.IoSpaceEnabled);
    }

    [Fact]
    public void InterruptLineIsProgrammable()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var lpc = new LpcController(io, pci);

        pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x3C), 1, 14);

        Assert.Equal(14, lpc.InterruptLine);
        Assert.Equal(1, lpc.InterruptPin);
    }
}
