using XGrid.Bus.Io;
using XGrid.Bus.Pci;
using XGrid.Bus.Smbus;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class SmbusTests
{
    private const ushort Base = 0xC000;

    [Fact]
    public void SmcCommandAccesses_AreCounted()
    {
        var smc = new SmcDevice();

        smc.TryReadByte(0x03, out _);
        smc.TryReadByte(0x03, out _);
        smc.WriteByte(0x1B, 0x04);

        Assert.Equal(2, smc.ReadCounts[0x03]);
        Assert.Equal(1, smc.WriteCounts[0x1B]);
    }

    [Fact]
    public void SmcScratch_SelectsShortAnimationByDefault()
    {
        var smc = new SmcDevice();

        Assert.True(smc.TryReadByte(0x1B, out byte value));
        Assert.Equal(SmcDevice.ScratchShortAnimation, value);
    }

    [Fact]
    public void SmcInterruptStatus_ReportsTrayClosedThenClears()
    {
        var smc = new SmcDevice();

        Assert.True(smc.TryReadByte(0x11, out byte first));
        Assert.Equal(SmcDevice.TrayClosedStatus, first);
        Assert.True(smc.TryReadByte(0x11, out byte second));
        Assert.Equal(0, second);
    }

    [Fact]
    public void EepromByteDataReadReturnsData()
    {
        (Amd756Smbus smbus, IoSpace io, _) = Create();
        byte[] image = new byte[256];
        image[0x10] = 0xAB;
        smbus.Attach(new Eeprom24c02(image));

        io.Write8(Base + Amd756Smbus.HostAddress, 0xA9);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x10);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);

        Assert.Equal(Amd756Smbus.HcycStatus, io.Read8(Base + Amd756Smbus.GlobalStatus));
        Assert.Equal(0xAB, io.Read8(Base + Amd756Smbus.HostData));
    }

    [Fact]
    public void EepromWordDataReadReturnsWord()
    {
        (Amd756Smbus smbus, IoSpace io, _) = Create();
        byte[] image = new byte[256];
        image[0x10] = 0x34;
        image[0x11] = 0x12;
        smbus.Attach(new Eeprom24c02(image));

        io.Write8(Base + Amd756Smbus.HostAddress, 0xA9);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x10);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0B);

        Assert.Equal(0x34, io.Read8(Base + Amd756Smbus.HostData));
        Assert.Equal(0x12, io.Read8(Base + Amd756Smbus.HostDataHigh));
    }

    [Fact]
    public void EepromWriteReadRoundTrips()
    {
        (Amd756Smbus smbus, IoSpace io, _) = Create();
        smbus.Attach(new Eeprom24c02());

        io.Write8(Base + Amd756Smbus.HostAddress, 0xA8);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x20);
        io.Write8(Base + Amd756Smbus.HostData, 0x77);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);
        Assert.Equal(Amd756Smbus.HcycStatus, io.Read8(Base + Amd756Smbus.GlobalStatus));

        io.Write8(Base + Amd756Smbus.HostAddress, 0xA9);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x20);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);

        Assert.Equal(0x77, io.Read8(Base + Amd756Smbus.HostData));
    }

    [Fact]
    public void UnknownAddressSetsProtocolError()
    {
        (_, IoSpace io, _) = Create();

        io.Write8(Base + Amd756Smbus.HostAddress, 0xAA);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x00);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);

        Assert.Equal(Amd756Smbus.PrerrStatus, io.Read8(Base + Amd756Smbus.GlobalStatus));
    }

    [Fact]
    public void StatusWriteClears()
    {
        (_, IoSpace io, _) = Create();

        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x20);
        Assert.Equal(Amd756Smbus.AbrtStatus, io.Read8(Base + Amd756Smbus.GlobalStatus));

        io.Write8(Base + Amd756Smbus.GlobalStatus, Amd756Smbus.AbrtStatus);
        Assert.Equal(0, io.Read8(Base + Amd756Smbus.GlobalStatus));
    }

    [Fact]
    public void SmcVersionReadsSequentially()
    {
        (Amd756Smbus smbus, IoSpace io, _) = Create();
        smbus.Attach(new SmcDevice());

        io.Write8(Base + Amd756Smbus.HostAddress, 0x21);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x01);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);
        Assert.Equal((byte)'P', io.Read8(Base + Amd756Smbus.HostData));

        io.Write8(Base + Amd756Smbus.HostCommand, 0x01);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);
        Assert.Equal((byte)'0', io.Read8(Base + Amd756Smbus.HostData));
    }

    [Fact]
    public void SmcChallengeValuesAreReadable()
    {
        (Amd756Smbus smbus, IoSpace io, _) = Create();
        smbus.Attach(new SmcDevice());

        io.Write8(Base + Amd756Smbus.HostAddress, 0x21);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x1C);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);

        Assert.Equal(0x52, io.Read8(Base + Amd756Smbus.HostData));
    }

    [Fact]
    public void SmcScratchRoundTrips()
    {
        (Amd756Smbus smbus, IoSpace io, _) = Create();
        smbus.Attach(new SmcDevice());

        io.Write8(Base + Amd756Smbus.HostAddress, 0x20);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x1B);
        io.Write8(Base + Amd756Smbus.HostData, 0x55);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);
        Assert.Equal(Amd756Smbus.HcycStatus, io.Read8(Base + Amd756Smbus.GlobalStatus));

        io.Write8(Base + Amd756Smbus.HostAddress, 0x21);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x1B);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);

        Assert.Equal(0x55, io.Read8(Base + Amd756Smbus.HostData));
    }

    [Fact]
    public void CompletedTransaction_WithInterruptEnabled_RaisesAndStatusWriteLowers()
    {
        (Amd756Smbus smbus, IoSpace io, PciBus pci) = Create();
        smbus.Attach(new Eeprom24c02());
        PciDevice? raised = null;
        PciDevice? lowered = null;
        pci.InterruptRaised += device => raised = device;
        pci.InterruptLowered += device => lowered = device;

        io.Write8(Base + Amd756Smbus.HostAddress, 0xA9);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x10);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x1A);

        Assert.Same(smbus, raised);
        Assert.Null(lowered);

        io.Write8(Base + Amd756Smbus.GlobalStatus, Amd756Smbus.HcycStatus);

        Assert.Same(smbus, lowered);
    }

    [Fact]
    public void CompletedTransaction_WithoutInterruptEnabled_DoesNotRaise()
    {
        (_, IoSpace io, PciBus pci) = Create();
        bool raised = false;
        pci.InterruptRaised += _ => raised = true;

        io.Write8(Base + Amd756Smbus.HostAddress, 0xAA);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x00);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);

        Assert.False(raised);
    }

    [Fact]
    public void TransactionDiagnostics_RecordLastTransaction()
    {
        (Amd756Smbus smbus, IoSpace io, _) = Create();
        smbus.Attach(new Eeprom24c02());

        Assert.Equal(0, smbus.TransactionCount);

        io.Write8(Base + Amd756Smbus.HostAddress, 0xA9);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x10);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);

        Assert.Equal(1, smbus.TransactionCount);
        Assert.Equal(0x54, smbus.LastSlaveAddress);
        Assert.Equal(2, smbus.LastProtocol);
        Assert.Equal(0x10, smbus.LastCommand);
        Assert.True(smbus.LastRead);
        Assert.True(smbus.LastOk);
    }

    [Fact]
    public void TransactionLog_RecordsHistoryOldestFirst()
    {
        (Amd756Smbus smbus, IoSpace io, _) = Create();
        smbus.Attach(new Eeprom24c02());

        io.Write8(Base + Amd756Smbus.HostAddress, 0xA9);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x10);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);

        io.Write8(Base + Amd756Smbus.HostAddress, 0xAA);
        io.Write8(Base + Amd756Smbus.HostCommand, 0x00);
        io.Write8(Base + Amd756Smbus.GlobalEnable, 0x0A);

        Assert.Equal(2, smbus.RecentTransactions.Count);
        Assert.Equal(0x54, smbus.RecentTransactions[0].SlaveAddress);
        Assert.True(smbus.RecentTransactions[0].Ok);
        Assert.Equal(0x55, smbus.RecentTransactions[1].SlaveAddress);
        Assert.False(smbus.RecentTransactions[1].Ok);
    }

    private static (Amd756Smbus Smbus, IoSpace Io, PciBus Pci) Create()
    {
        var io = new IoSpace();
        var pci = new PciBus(io);
        var smbus = new Amd756Smbus(io, pci);
        pci.WriteConfig(PciBus.MakeConfigAddress(1, 1, 0x14), 4, Base);
        return (smbus, io, pci);
    }
}
