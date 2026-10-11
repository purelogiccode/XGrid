using System.Collections.Generic;
using XGrid.Bus.Usb;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class OhciUsbTests
{
    private const uint Base = 0xFED00000;

    private const uint HcControl = 0x04;
    private const uint HcCommandStatus = 0x08;
    private const uint HcInterruptStatus = 0x0C;
    private const uint HcInterruptEnable = 0x10;
    private const uint HcHcca = 0x18;
    private const uint HcFmNumber = 0x3C;
    private const uint HcRhDescriptorA = 0x48;
    private const uint HcRhStatus = 0x50;
    private const uint HcRhPortStatus1 = 0x54;

    private const uint UsbOperational = 0x80;
    private const uint UsbSuspend = 0xc0;
    private const uint StatusHcr = 0x1;
    private const uint IntrSf = 0x4;
    private const uint IntrRhsc = 0x40;
    private const uint IntrMie = 0x80000000;
    private const uint RhsLpsc = 0x00010000;
    private const uint PortCcs = 0x1;
    private const uint PortPes = 0x2;
    private const uint PortPps = 0x100;
    private const uint PortCsc = 0x10000;

    private sealed class FakeUsbMemory : IUsbMemory
    {
        public List<(uint Address, ushort Value)> Writes16 { get; } = new();

        public List<(uint Address, uint Value)> Writes32 { get; } = new();

        public void Write16(uint address, ushort value)
        {
            Writes16.Add((address, value));
        }

        public void Write32(uint address, uint value)
        {
            Writes32.Add((address, value));
        }
    }

    [Fact]
    public void RevisionAndRootHubDescriptor_MatchXemu()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        Assert.Equal(0x10u, ohci.Read(Base, 4));
        Assert.Equal(0x204u, ohci.Read(Base + HcRhDescriptorA, 4));
    }

    [Fact]
    public void EmptyPorts_ReportPowerAndNoConnect()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        uint status = ohci.Read(Base + HcRhPortStatus1, 4);

        Assert.Equal(PortPps, status & PortPps);
        Assert.Equal(0u, status & PortCcs);
    }

    [Fact]
    public void HubPowerUp_SetsPpsOnAllPorts()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        ohci.Write(Base + HcRhStatus, 4, RhsLpsc);

        Assert.Equal(PortPps, ohci.Read(Base + HcRhPortStatus1, 4) & PortPps);
        Assert.Equal(PortPps, ohci.Read(Base + HcRhPortStatus1 + 4, 4) & PortPps);
    }

    [Fact]
    public void PortEnableWrite_WithNoDevice_SetsConnectStatusChange()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());
        bool asserted = false;
        ohci.InterruptChanged += a => asserted = a;
        ohci.Write(Base + HcInterruptEnable, 4, IntrMie | IntrRhsc);

        ohci.Write(Base + HcRhPortStatus1, 4, PortPes);

        uint status = ohci.Read(Base + HcRhPortStatus1, 4);
        Assert.NotEqual(0u, status & PortCsc);
        Assert.Equal(0u, status & PortPes);
        Assert.NotEqual(0u, ohci.Read(Base + HcInterruptStatus, 4) & IntrRhsc);
        Assert.True(asserted);
    }

    [Fact]
    public void OperationalFrames_RaiseSofAndIncrementFrameNumber()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());
        bool asserted = false;
        ohci.InterruptChanged += a => asserted = a;
        ohci.Write(Base + HcInterruptEnable, 4, IntrMie | IntrSf);
        ohci.Write(Base + HcControl, 4, UsbOperational);

        ohci.Advance(733334);

        Assert.NotEqual(0u, ohci.Read(Base + HcInterruptStatus, 4) & IntrSf);
        Assert.Equal(1u, ohci.Read(Base + HcFmNumber, 4));
        Assert.True(asserted);

        ohci.Write(Base + HcInterruptStatus, 4, IntrSf);

        Assert.False(asserted);
    }

    [Fact]
    public void SuspendedController_DoesNotAdvanceFrames()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        ohci.Advance(10_000_000);

        Assert.Equal(0u, ohci.Read(Base + HcFmNumber, 4));
    }

    [Fact]
    public void FrameBoundary_WritesHccaFrameAndDone()
    {
        var memory = new FakeUsbMemory();
        var ohci = new OhciUsbController(Base, memory);
        ohci.Write(Base + HcHcca, 4, 0x10000000);
        ohci.Write(Base + HcControl, 4, UsbOperational);

        ohci.Advance(733334);

        Assert.Contains((0x10000080u, (ushort)1), memory.Writes16);
        Assert.Contains((0x10000082u, (ushort)0), memory.Writes16);
        Assert.Contains((0x10000084u, 0u), memory.Writes32);
    }

    [Fact]
    public void HostControllerReset_ClearsStatusButKeepsRootHub()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());
        ohci.Write(Base + HcInterruptEnable, 4, IntrMie | IntrSf);
        ohci.Write(Base + HcControl, 4, UsbOperational);
        ohci.Advance(733334);

        ohci.Write(Base + HcCommandStatus, 4, StatusHcr);

        Assert.Equal(0u, ohci.Read(Base + HcCommandStatus, 4));
        Assert.Equal(0u, ohci.Read(Base + HcInterruptStatus, 4));
        Assert.Equal(UsbSuspend, ohci.Read(Base + HcControl, 4) & 0xc0);
        Assert.Equal(0x204u, ohci.Read(Base + HcRhDescriptorA, 4));
    }

    [Fact]
    public void UnalignedAccess_ReturnsSentinelAndIsIgnored()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        Assert.Equal(0xFFFFFFFFu, ohci.Read(Base + 2, 4));

        ohci.Write(Base + 2, 4, 0x1234);
        Assert.Equal(0x10u, ohci.Read(Base, 4));
    }

    [Fact]
    public void PortStatusAccessor_MirrorsEmptyPorts()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        for (int i = 0; i < OhciUsbController.PortCount; i++)
        {
            Assert.Equal(0u, ohci.GetPortStatus(i));
        }

        Assert.Equal(0u, ohci.GetPortStatus(-1));
        Assert.Equal(0u, ohci.GetPortStatus(OhciUsbController.PortCount));
    }
}
