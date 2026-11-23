using System.IO;
using XGrid.Bus.Interrupts;
using XGrid.Bus.Io;
using Xunit;

namespace XGrid.Bus.Tests;

/// <summary>
/// Level-triggered delivery. The Xbox's PCI lines (NV2A IRQ3, APU IRQ5, ACI IRQ6,
/// USB IRQ11/12) are held high until the device's own status is acknowledged, and
/// the guest programs the edge/level control register for exactly those lines
/// (measured master 0x68, slave 0x18). The 8259 must keep the request latched
/// across an acknowledge and re-present it after the EOI (xemu 0.8.139
/// <c>hw/intc/i8259.c</c>: <c>pic_set_irq</c> keeps <c>irr</c> for
/// <c>ltim</c>/ELCR lines and <c>pic_intack</c> only clears it for an edge line).
/// </summary>
public sealed class Pic8259LevelTests
{
    private static Pic8259 CreateInitialized(IoSpace io, byte icw1Master = 0x11, byte icw1Slave = 0x11)
    {
        var pic = new Pic8259(io);
        io.Write8(0x20, icw1Master);
        io.Write8(0x21, 0x30);
        io.Write8(0x21, 0x04);
        io.Write8(0x21, 0x01);
        io.Write8(0xA0, icw1Slave);
        io.Write8(0xA1, 0x38);
        io.Write8(0xA1, 0x02);
        io.Write8(0xA1, 0x01);
        return pic;
    }

    [Fact]
    public void ElcrWrite_KeepsTheRequestAcrossAnAcknowledge_AndTheEoiRedeliversIt()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x4D0, 0x68);

        Assert.Equal(0x68, pic.MasterElcr);

        // The device raises the line and never touches it again (a held level).
        pic.SetIrq(5, true);
        Assert.Equal(0x35, pic.Acknowledge());
        Assert.Equal(0x20, pic.MasterInService);
        Assert.Equal(0x20, pic.MasterRequest);

        // The guest's ISR clears the device status, but this fixture keeps the line
        // high on purpose: the 8259 must re-present it after the EOI.
        io.Write8(0x20, 0x20);

        Assert.Equal(0x00, pic.MasterInService);
        Assert.Equal(0x20, pic.MasterRequest);
        Assert.Equal(0x35, pic.Acknowledge());
    }

    [Fact]
    public void LevelLine_IsNotDeliveredAgain_AfterTheDeviceLowersIt()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x4D0, 0x20);
        pic.SetIrq(5, true);
        Assert.Equal(0x35, pic.Acknowledge());

        pic.SetIrq(5, false);
        io.Write8(0x20, 0x20);

        Assert.Equal(-1, pic.Acknowledge());
        Assert.Equal(0x00, pic.MasterRequest);
    }

    [Fact]
    public void LevelLine_IsNotLost_WhenItIsAcknowledgedWhileMaskedThenUnmasked()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x4D0, 0x20);
        io.Write8(0x21, 0x20);

        pic.SetIrq(5, true);
        Assert.Equal(-1, pic.Acknowledge());

        io.Write8(0x21, 0x00);

        Assert.Equal(0x35, pic.Acknowledge());
    }

    [Fact]
    public void EdgeLine_IsNotDeliveredAgainAfterEoi_WhileTheLineStaysHigh()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);

        pic.SetIrq(5, true);
        Assert.Equal(0x35, pic.Acknowledge());
        io.Write8(0x20, 0x20);

        // Edge mode: one request, one interrupt (the pre-fix behaviour, pinned).
        Assert.Equal(-1, pic.Acknowledge());
        Assert.Equal(0x00, pic.MasterRequest);
    }

    [Fact]
    public void LtimMarksEveryLineLevelTriggered()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io, icw1Master: 0x19);

        pic.SetIrq(0, true);
        Assert.Equal(0x30, pic.Acknowledge());
        io.Write8(0x20, 0x20);

        Assert.Equal(0x30, pic.Acknowledge());
    }

    [Fact]
    public void LevelSlaveLine_IsDeliveredAgainAfterTheSlaveEoi()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x4D1, 0x18);
        io.Write8(0xA1, 0xF7);

        pic.SetIrq(11, true);
        Assert.Equal(0x3B, pic.Acknowledge());
        Assert.Equal(0x04, pic.MasterInService);
        Assert.Equal(0x08, pic.SlaveInService);

        io.Write8(0xA0, 0x20);
        io.Write8(0x20, 0x20);

        Assert.Equal(0x00, pic.SlaveInService);
        Assert.Equal(0x00, pic.MasterInService);
        Assert.Equal(0x04, pic.MasterRequest);
        Assert.Equal(0x3B, pic.Acknowledge());
    }

    [Fact]
    public void ElcrMasksTheReservedBits()
    {
        var io = new IoSpace();
        _ = CreateInitialized(io);

        io.Write8(0x4D0, 0xFF);
        io.Write8(0x4D1, 0xFF);

        Assert.Equal(0xF8, io.Read8(0x4D0));
        Assert.Equal(0xDE, io.Read8(0x4D1));
    }

    [Fact]
    public void LevelState_SurvivesASnapshotRoundTrip()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x4D0, 0x68);
        pic.SetIrq(5, true);
        pic.Acknowledge();

        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            pic.SaveState(writer);
        }

        var io2 = new IoSpace();
        var restored = new Pic8259(io2);
        buffer.Position = 0;
        using (var reader = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            restored.LoadState(reader);
        }

        Assert.Equal(0x68, restored.MasterElcr);
        io2.Write8(0x20, 0x20);

        Assert.Equal(0x20, restored.MasterRequest);
        Assert.Equal(0x35, restored.Acknowledge());
    }
}
