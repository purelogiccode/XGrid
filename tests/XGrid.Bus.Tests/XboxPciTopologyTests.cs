using XGrid.Bus.Pci;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class XboxPciTopologyTests
{
    private const uint Bus1Enable = 0x80000000u | (1u << 16);

    [Theory]
    [InlineData(2, 0x01C210DEu, 0x0C0310B1u)]
    [InlineData(3, 0x01C210DEu, 0x0C0310B1u)]
    [InlineData(4, 0x01C310DEu, 0x020000B1u)]
    [InlineData(5, 0x01B010DEu, 0x040100B1u)]
    [InlineData(6, 0x01B110DEu, 0x040100B1u)]
    public void McpxDevicesExposeXemuIdsAndClass(int device, uint expectedId, uint expectedClassDword)
    {
        var bus = new XboxBus();
        byte slot = (byte)device;

        Assert.Equal(expectedId, bus.Pci.ReadConfig(PciBus.MakeConfigAddress(slot, 0, 0x00), 4));
        Assert.Equal(expectedClassDword, bus.Pci.ReadConfig(PciBus.MakeConfigAddress(slot, 0, 0x08), 4));
    }

    [Fact]
    public void ConfigAccesses_AreCountedPerBusAndSlot()
    {
        var bus = new XboxBus();

        bus.Pci.ReadConfig(PciBus.MakeConfigAddress(0, 0, 0x00), 4);
        bus.Pci.ReadConfig(PciBus.MakeConfigAddress(0, 0, 0x00) | (1u << 16), 4);

        Assert.Equal(1, bus.Pci.ConfigAccessCounts[0x0000]);
        Assert.Equal(1, bus.Pci.ConfigAccessCounts[0x0100]);
    }

    [Fact]
    public void HostBridgeIsMultiFunctionAndKeepsHeaderAcrossReset()
    {
        var bus = new XboxBus();
        uint header = bus.Pci.ReadConfig(PciBus.MakeConfigAddress(0, 0, 0x0C), 4);
        Assert.Equal(0x80u, (header >> 16) & 0xFFu);

        bus.Reset();

        header = bus.Pci.ReadConfig(PciBus.MakeConfigAddress(0, 0, 0x0C), 4);
        Assert.Equal(0x80u, (header >> 16) & 0xFFu);
    }

    [Fact]
    public void UsbBar0ProbeReturnsOhciSize()
    {
        var bus = new XboxBus();
        uint address = PciBus.MakeConfigAddress(2, 0, 0x10);

        bus.Pci.WriteConfig(address, 4, 0xFFFFFFFFu);

        Assert.Equal(0xFFFFFF00u, bus.Pci.ReadConfig(address, 4));
    }

    [Fact]
    public void Nv2aIsReachableOnAgpSecondaryBusOnceFirmwareProgramsIt()
    {
        var bus = new XboxBus();

        bus.Pci.WriteConfig(PciBus.MakeConfigAddress(30, 0, 0x18), 4, 0x00010100u);

        Assert.Equal(0x02A010DEu, bus.Pci.ReadConfig(Bus1Enable, 4));
    }

    [Fact]
    public void UnprogrammedSecondaryBusDoesNotAliasBusZero()
    {
        var bus = new XboxBus();

        Assert.Equal(0xFFFFFFFFu, bus.Pci.ReadConfig(Bus1Enable, 4));
    }

    [Fact]
    public void SmbusCompletionIrq_ReachesPicThroughAcpiRouting()
    {
        var bus = new XboxBus();
        bus.Pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x64), 4, 0x00000B00);
        bus.Pci.WriteConfig(PciBus.MakeConfigAddress(1, 1, 0x14), 4, 0xC000);

        bus.Io.Write8(0xC004, 0x21);
        bus.Io.Write8(0xC008, 0x01);
        bus.Io.Write8(0xC002, 0x1A);

        Assert.NotEqual(0, bus.Pic.SlaveRequest & 0x08);

        bus.Io.Write8(0xC000, 0x10);

        Assert.Equal(0, bus.Pic.SlaveRequest & 0x08);
    }

    [Fact]
    public void Nv2aBar1IsSixtyFourMegabytePrefetchableAperture()
    {
        var bus = new XboxBus();
        bus.Pci.WriteConfig(PciBus.MakeConfigAddress(30, 0, 0x18), 4, 0x00010100u);
        uint address = Bus1Enable | 0x14u;

        bus.Pci.WriteConfig(address, 4, 0xFFFFFFFFu);

        Assert.Equal(0xFC000008u, bus.Pci.ReadConfig(address, 4));
    }
}
