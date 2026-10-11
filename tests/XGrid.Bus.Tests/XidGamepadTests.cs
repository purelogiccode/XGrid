using System;
using XGrid.Bus.Usb;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class XidGamepadTests
{
    [Fact]
    public void Reset_IsNeutralWithZeroAddress()
    {
        var pad = new XidGamepad();

        Assert.Equal(0, pad.Address);
        Assert.False(pad.Configured);
        Assert.True(pad.HandleIn(2, 20, out byte[] report) == UsbTransferResult.Ack);
        Assert.Equal(0, report[0]);
        Assert.Equal(20, report[1]);
        Assert.Equal(new byte[] { 0, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, report);
    }

    [Fact]
    public void GetDeviceDescriptor_ReturnsMicrosoftPad()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0x0100, 0, 18), out byte[] payload)
            == UsbTransferResult.Ack);

        Assert.Equal(18, payload.Length);
        Assert.Equal(0x12, payload[0]);
        Assert.Equal(0x01, payload[1]);
        Assert.Equal(0x5E, payload[8]);
        Assert.Equal(0x04, payload[9]);
        Assert.Equal(0x02, payload[10]);
        Assert.Equal(0x02, payload[11]);
    }

    [Fact]
    public void GetConfigDescriptor_TruncatesToRequestedLength()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0x0200, 0, 9), out byte[] payload)
            == UsbTransferResult.Ack);

        Assert.Equal(new byte[] { 0x09, 0x02, 0x20, 0x00, 0x01, 0x01, 0x00, 0x80, 0x32 }, payload);
    }

    [Fact]
    public void GetStrings_ReturnLanguageAndProduct()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0x0300, 0, 4), out byte[] lang)
            == UsbTransferResult.Ack);
        Assert.Equal(new byte[] { 0x04, 0x03, 0x09, 0x04 }, lang);

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0x0302, 0, 64), out byte[] product)
            == UsbTransferResult.Ack);
        Assert.Equal(0x03, product[1]);
        Assert.Equal("Xbox Controller".Length * 2 + 2, product.Length);

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0x0309, 0, 64), out _)
            == UsbTransferResult.Stall);
    }

    [Fact]
    public void GetXidDescriptor_ReturnsSixteenBytes()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0xC1, 0x06, 0x4200, 0, 16), out byte[] payload)
            == UsbTransferResult.Ack);

        Assert.Equal(new byte[]
        {
            0x10, 0x42, 0x00, 0x01, 0x01, 0x01, 0x14, 0x06,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        }, payload);
    }

    [Fact]
    public void GetCapabilities_ReturnsSizedReports()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0xC1, 0x01, 0x0100, 0, 20), out byte[] inCaps)
            == UsbTransferResult.Ack);
        Assert.Equal(20, inCaps.Length);
        Assert.Equal(0, inCaps[0]);
        Assert.Equal(20, inCaps[1]);
        Assert.Equal(0xFF, inCaps[2]);

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0xC1, 0x01, 0x0200, 0, 6), out byte[] outCaps)
            == UsbTransferResult.Ack);
        Assert.Equal(new byte[] { 0, 6, 0xFF, 0xFF, 0xFF, 0xFF }, outCaps);

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0xC1, 0x01, 0x0300, 0, 20), out _)
            == UsbTransferResult.Stall);
    }

    [Fact]
    public void GetReport_RejectsOversizedBuffer()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0xA1, 0x01, 0x0100, 0, 64), out _)
            == UsbTransferResult.Stall);
        Assert.True(pad.HandleSetup(new UsbSetupPacket(0xA1, 0x01, 0x0100, 0, 20), out byte[] report)
            == UsbTransferResult.Ack);
        Assert.Equal(20, report.Length);
    }

    [Fact]
    public void SetReport_RequiresExactLength()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x21, 0x09, 0x0200, 0, 5), out _)
            == UsbTransferResult.Stall);
        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x21, 0x09, 0x0200, 0, 6), out _)
            == UsbTransferResult.Ack);
        Assert.True(pad.CompleteControlOut(new byte[] { 0, 6, 1, 2, 3, 4 }) == UsbTransferResult.Ack);
        Assert.True(pad.CompleteControlOut(new byte[] { 0, 6, 1 }) == UsbTransferResult.Stall);
    }

    [Fact]
    public void SetAddress_AppliesOnStatusStage()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x00, 0x05, 0x0002, 0, 0), out _)
            == UsbTransferResult.Ack);
        Assert.Equal(0, pad.Address);

        pad.CompleteStatusStage();

        Assert.Equal(2, pad.Address);
    }

    [Fact]
    public void StandardRequests_AreAcknowledged()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x00, 0x09, 0x0001, 0, 0), out _)
            == UsbTransferResult.Ack);
        pad.CompleteStatusStage();
        Assert.True(pad.Configured);

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x80, 0x08, 0, 0, 1), out byte[] config)
            == UsbTransferResult.Ack);
        Assert.Equal(new byte[] { 1 }, config);

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x82, 0x00, 0, 2, 2), out byte[] status)
            == UsbTransferResult.Ack);
        Assert.Equal(new byte[] { 0, 0 }, status);

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x02, 0x01, 0, 0x82, 0), out _)
            == UsbTransferResult.Ack);

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0xFF00, 0, 8), out _)
            == UsbTransferResult.Stall);
    }

    [Fact]
    public void SetInput_IsVisibleOnInterruptEndpoint()
    {
        var pad = new XidGamepad();
        pad.SetInput(0x0001, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, new short[] { 100, -200, 300, -400 });

        Assert.True(pad.HandleIn(2, 20, out byte[] report) == UsbTransferResult.Ack);

        Assert.Equal(0, report[0]);
        Assert.Equal(20, report[1]);
        Assert.Equal(0x01, report[2]);
        Assert.Equal(0x00, report[3]);
        Assert.Equal(8, report[11]);
        Assert.Equal(100, BitConverter.ToInt16(report, 12));
        Assert.Equal(-200, BitConverter.ToInt16(report, 14));

        Assert.True(pad.HandleIn(3, 20, out _) == UsbTransferResult.Stall);
        Assert.True(pad.HandleOut(2, new byte[] { 0, 6, 0, 0, 0, 0 }) == UsbTransferResult.Ack);
        Assert.True(pad.HandleOut(1, Array.Empty<byte>()) == UsbTransferResult.Stall);
    }

    [Fact]
    public void SetInput_SurvivesReset()
    {
        var pad = new XidGamepad();
        pad.SetInput(0x0010, new byte[] { 0, 0, 0xFF, 0, 0, 0, 0, 0 }, new short[] { 0, 0, 0, 0 });

        pad.Reset();

        Assert.True(pad.HandleIn(2, 20, out byte[] report) == UsbTransferResult.Ack);
        Assert.Equal(0x10, report[2] | (report[3] << 8));
        Assert.Equal(0xFF, report[6]);
    }

    [Fact]
    public void RequestLog_RecordsSetupOutcomes()
    {
        var pad = new XidGamepad();

        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0x0100, 0, 18), out _)
            == UsbTransferResult.Ack);
        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0xFF00, 0, 8), out _)
            == UsbTransferResult.Stall);

        Assert.Equal(2, pad.RequestLog.Count);
    }
}
