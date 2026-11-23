using XGrid.Bus.Interrupts;
using XGrid.Bus.Io;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class Pic8259Tests
{
    private static Pic8259 CreateInitialized(IoSpace io)
    {
        var pic = new Pic8259(io);
        io.Write8(0x20, 0x11);
        io.Write8(0x21, 0x20);
        io.Write8(0x21, 0x04);
        io.Write8(0x21, 0x01);
        io.Write8(0xA0, 0x11);
        io.Write8(0xA1, 0x28);
        io.Write8(0xA1, 0x02);
        io.Write8(0xA1, 0x01);
        return pic;
    }

    [Fact]
    public void InitializationClearsMask()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);

        Assert.Equal(0, pic.MasterMask);
        Assert.Equal(0, pic.SlaveMask);
    }

    [Fact]
    public void AcknowledgeReturnsVectorAndSetsInService()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x21, 0xFE);

        pic.SetIrq(0, true);

        Assert.Equal(0x20, pic.Acknowledge());
        Assert.Equal(0x01, pic.MasterInService);
        Assert.Equal(0x00, pic.MasterRequest);
    }

    [Fact]
    public void EndOfInterruptClearsInService()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x21, 0xFE);
        pic.SetIrq(0, true);
        pic.Acknowledge();

        io.Write8(0x20, 0x20);

        Assert.Equal(0x00, pic.MasterInService);
    }

    [Fact]
    public void MaskedRequestIsNotAcknowledged()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x21, 0xFF);

        pic.SetIrq(0, true);

        Assert.Equal(-1, pic.Acknowledge());
        Assert.Equal(0x01, pic.MasterRequest);
    }

    [Fact]
    public void LowerPriorityWaitsForEoi()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x21, 0xFC);

        pic.SetIrq(0, true);
        Assert.Equal(0x20, pic.Acknowledge());

        pic.SetIrq(1, true);
        Assert.Equal(-1, pic.Acknowledge());

        io.Write8(0x20, 0x20);
        Assert.Equal(0x21, pic.Acknowledge());
    }

    [Fact]
    public void CascadeAcknowledgesSlaveVector()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x21, 0xFB);
        io.Write8(0xA1, 0xFD);

        pic.SetIrq(9, true);

        Assert.Equal(0x29, pic.Acknowledge());
        Assert.Equal(0x04, pic.MasterInService);
        Assert.Equal(0x02, pic.SlaveInService);
    }

    [Fact]
    public void ReadIrrViaOcw3()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        pic.SetIrq(3, true);

        io.Write8(0x20, 0x0A);

        Assert.Equal(0x08, io.Read8(0x20));
    }

    [Fact]
    public void PollCommandReturnsHighestRequest()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x21, 0xDF);
        pic.SetIrq(5, true);

        io.Write8(0x20, 0x0C);

        Assert.Equal(0x85, io.Read8(0x20));
        Assert.Equal(0x20, pic.MasterInService);
    }

    [Fact]
    public void SpecificEoiClearsRequestedLine()
    {
        var io = new IoSpace();
        Pic8259 pic = CreateInitialized(io);
        io.Write8(0x21, 0xFC);
        pic.SetIrq(0, true);
        pic.Acknowledge();

        io.Write8(0x20, 0x63);

        Assert.Equal(0x00, pic.MasterInService);
    }
}
