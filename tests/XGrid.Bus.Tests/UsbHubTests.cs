using System;
using XGrid.Bus.Usb;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class UsbHubTests
{
    [Fact]
    public void DeviceDescriptor_IsHubClass()
    {
        var hub = new UsbHub(4);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0x0100, 0, 18), out byte[] payload)
                    == UsbTransferResult.Ack);

        Assert.Equal(18, payload.Length);
        Assert.Equal(0x09, payload[4]);
        Assert.Equal(0x0409, payload[8] | (payload[9] << 8));
        Assert.Equal(0x55AA, payload[10] | (payload[11] << 8));
    }

    [Fact]
    public void ConfigDescriptor_HasHubInterfaceAndInterruptEndpoint()
    {
        var hub = new UsbHub(4);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x80, 0x06, 0x0200, 0, 25), out byte[] payload)
                    == UsbTransferResult.Ack);

        Assert.Equal(25, payload.Length);
        Assert.Equal(0x19, payload[2] | (payload[3] << 8));
        Assert.Equal(0xE0, payload[7]);
        Assert.Equal(0x09, payload[14]);
        Assert.Equal(0x81, payload[20]);
        Assert.Equal(0x03, payload[21]);
        Assert.Equal(2, payload[22]);
        Assert.Equal(0xFF, payload[24]);
    }

    [Fact]
    public void HubDescriptor_ReportsPortsAndPowerMask()
    {
        var three = new UsbHub(3);
        Assert.True(three.HandleSetup(new UsbSetupPacket(0xA0, 0x06, 0x2900, 0, 9), out byte[] threeDesc)
                    == UsbTransferResult.Ack);
        Assert.Equal(new byte[] { 0x09, 0x29, 0x03, 0x0A, 0x00, 0x01, 0x00, 0x00, 0xFF }, threeDesc);

        var four = new UsbHub(4);
        Assert.True(four.HandleSetup(new UsbSetupPacket(0xA0, 0x06, 0x2900, 0, 9), out byte[] fourDesc)
                    == UsbTransferResult.Ack);
        Assert.Equal(new byte[] { 0x09, 0x29, 0x04, 0x0A, 0x00, 0x01, 0x00, 0x00, 0xFF }, fourDesc);
    }

    [Fact]
    public void Ports_StartPoweredWithoutConnection()
    {
        var hub = new UsbHub(3);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0xA3, 0x00, 0, 1, 4), out byte[] payload)
                    == UsbTransferResult.Ack);

        Assert.Equal(0x0100, payload[0] | (payload[1] << 8));
        Assert.Equal(0, payload[2] | (payload[3] << 8));
    }

    [Fact]
    public void AttachDevice_ReportsConnectChange()
    {
        var hub = new UsbHub(3);
        hub.Attach(0, new XidGamepad());

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0xA3, 0x00, 0, 1, 4), out byte[] payload)
                    == UsbTransferResult.Ack);
        Assert.Equal(0x0101, payload[0] | (payload[1] << 8));
        Assert.Equal(0x0001, payload[2] | (payload[3] << 8));

        Assert.True(hub.HandleIn(1, 1, out byte[] report) == UsbTransferResult.Ack);
        Assert.Equal(new byte[] { 0x02 }, report);
    }

    [Fact]
    public void InterruptReport_NaksWithNoChanges()
    {
        var hub = new UsbHub(3);

        Assert.True(hub.HandleIn(1, 1, out _) == UsbTransferResult.Nak);

        hub.Attach(0, new XidGamepad());
        Assert.True(hub.HandleIn(1, 1, out _) == UsbTransferResult.Ack);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x01, 16, 1, 0), out _) == UsbTransferResult.Ack);
        Assert.True(hub.HandleIn(1, 1, out _) == UsbTransferResult.Nak);
    }

    [Fact]
    public void PortReset_ResetsDownstreamDeviceAndEnablesPort()
    {
        var hub = new UsbHub(3);
        var pad = new XidGamepad();
        hub.Attach(0, pad);
        Assert.True(pad.HandleSetup(new UsbSetupPacket(0x00, 0x05, 0x0007, 0, 0), out _)
                    == UsbTransferResult.Ack);
        pad.CompleteStatusStage();
        Assert.Equal(7, pad.Address);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x03, 4, 1, 0), out _)
                    == UsbTransferResult.Ack);

        Assert.Equal(0, pad.Address);
        Assert.Equal(0x2, hub.GetPortStatus(0) & 0x2);
        Assert.Equal(0x13, hub.GetPortChange(0));
        Assert.Same(pad, hub.FindDevice(0));
        Assert.Null(hub.FindDevice(7));
    }

    [Fact]
    public void FindDevice_RequiresEnabledPort()
    {
        var hub = new UsbHub(3);
        var pad = new XidGamepad();
        hub.Attach(0, pad);

        Assert.Null(hub.FindDevice(0));

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x03, 4, 1, 0), out _)
                    == UsbTransferResult.Ack);

        Assert.Same(pad, hub.FindDevice(0));
    }

    [Fact]
    public void FindDevice_RoutesThroughNestedHubs()
    {
        var pad = new XidGamepad();
        var inner = new UsbHub(3);
        inner.Attach(0, pad);
        var outer = new UsbHub(4);
        outer.Attach(2, inner);

        Assert.Null(outer.FindDevice(0));

        Assert.True(outer.HandleSetup(new UsbSetupPacket(0x23, 0x03, 4, 3, 0), out _)
                    == UsbTransferResult.Ack);
        Assert.True(inner.HandleSetup(new UsbSetupPacket(0x00, 0x05, 0x0001, 0, 0), out _)
                    == UsbTransferResult.Ack);
        inner.CompleteStatusStage();
        Assert.True(inner.HandleSetup(new UsbSetupPacket(0x23, 0x03, 4, 1, 0), out _)
                    == UsbTransferResult.Ack);

        Assert.Same(inner, outer.FindDevice(1));
        Assert.Same(pad, outer.FindDevice(0));
    }

    [Fact]
    public void SetAddress_AppliesOnStatusStage()
    {
        var hub = new UsbHub(3);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x00, 0x05, 0x0005, 0, 0), out _)
                    == UsbTransferResult.Ack);
        Assert.Equal(0, hub.Address);

        hub.CompleteStatusStage();

        Assert.Equal(5, hub.Address);
    }

    [Fact]
    public void SetConfiguration_AppliesOnStatusStage()
    {
        var hub = new UsbHub(3);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x00, 0x09, 1, 0, 0), out _)
                    == UsbTransferResult.Ack);
        hub.CompleteStatusStage();
        Assert.True(hub.Configured);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x80, 0x08, 0, 0, 1), out byte[] config)
                    == UsbTransferResult.Ack);
        Assert.Equal(new byte[] { 1 }, config);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x00, 0x09, 2, 0, 0), out _)
                    == UsbTransferResult.Stall);
    }

    [Fact]
    public void HubStatusAndFeatures_FollowXemu()
    {
        var hub = new UsbHub(3);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0xA0, 0x00, 0, 0, 4), out byte[] status)
                    == UsbTransferResult.Ack);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, status);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x20, 0x03, 1, 0, 0), out _)
                    == UsbTransferResult.Ack);
        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x20, 0x03, 2, 0, 0), out _)
                    == UsbTransferResult.Stall);
        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x20, 0x01, 0, 0, 0), out _)
                    == UsbTransferResult.Ack);
    }

    [Fact]
    public void SetPortFeature_RejectsUnsupportedFeatures()
    {
        var hub = new UsbHub(3);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x03, 2, 1, 0), out _)
                    == UsbTransferResult.Ack);
        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x03, 8, 1, 0), out _)
                    == UsbTransferResult.Ack);
        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x03, 1, 1, 0), out _)
                    == UsbTransferResult.Stall);
        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x03, 4, 4, 0), out _)
                    == UsbTransferResult.Stall);
    }

    [Fact]
    public void ClearPortFeature_ClearsChangeBits()
    {
        var hub = new UsbHub(3);
        hub.Attach(0, new XidGamepad());

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x01, 16, 1, 0), out _)
                    == UsbTransferResult.Ack);
        Assert.Equal(0, hub.GetPortChange(0));

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x01, 1, 1, 0), out _)
                    == UsbTransferResult.Ack);
        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x01, 3, 1, 0), out _)
                    == UsbTransferResult.Stall);
    }

    [Fact]
    public void Reset_ClearsPortsAndKeepsConnection()
    {
        var hub = new UsbHub(3);
        hub.Attach(0, new XidGamepad());
        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x23, 0x03, 4, 1, 0), out _)
                    == UsbTransferResult.Ack);

        hub.Reset();

        Assert.Equal(0x0101, hub.GetPortStatus(0));
        Assert.Equal(0x0001, hub.GetPortChange(0));
    }

    [Fact]
    public void EndpointHaltClear_IsAccepted()
    {
        var hub = new UsbHub(3);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x02, 0x01, 0, 0x81, 0), out _)
                    == UsbTransferResult.Ack);
        Assert.True(hub.HandleSetup(new UsbSetupPacket(0x02, 0x01, 0, 0x82, 0), out _)
                    == UsbTransferResult.Stall);
    }

    [Fact]
    public void UnknownRequests_Stall()
    {
        var hub = new UsbHub(3);

        Assert.True(hub.HandleSetup(new UsbSetupPacket(0xC1, 0x06, 0x4200, 0, 16), out _)
                    == UsbTransferResult.Stall);
        Assert.True(hub.HandleOut(1, Array.Empty<byte>()) == UsbTransferResult.Stall);
        Assert.True(hub.CompleteControlOut(new byte[] { 1 }) == UsbTransferResult.Stall);
    }
}
