using XGrid.Bus.Net;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class NvNetLinkTests
{
    private const uint Base = McpxNvNetDevice.BaseAddress;

    private const uint IrqStatus = 0x000;
    private const uint IrqMask = 0x004;
    private const uint TxControl = 0x084;
    private const uint RxControl = 0x094;
    private const uint TxRxControl = 0x144;
    private const uint MiiStatus = 0x180;
    private const uint AdapterControl = 0x188;
    private const uint MdioAddr = 0x190;
    private const uint MdioData = 0x194;

    private const uint IrqMiiEvent = 1u << 6;
    private const uint MiiLinkChange = 1u << 3;

    [Fact]
    public void LinkDown_BlocksTxRx_RaisesMiiEvent()
    {
        var net = new McpxNvNetDevice();
        net.Write(Base + TxControl, 4, 1);
        net.Write(Base + RxControl, 4, 1);
        net.Write(Base + IrqMask, 4, IrqMiiEvent);
        int asserts = 0;
        net.InterruptChanged += asserted =>
        {
            if (asserted)
            {
                asserts++;
            }
        };

        net.SetLinkUp(false);

        Assert.False(net.LinkUp);
        Assert.False(net.CanTransmit());
        Assert.Equal("link", net.CantTransmitReason());
        Assert.False(net.CanReceive());
        Assert.Equal("link", net.CanReceiveReason());
        Assert.Equal(1, net.LinkChanges);
        Assert.Equal(MiiLinkChange, net.Read(Base + MiiStatus, 4));
        Assert.Equal(IrqMiiEvent, net.Read(Base + IrqStatus, 4));
        Assert.True(net.InterruptAsserted);
        Assert.Equal(1, asserts);
        Assert.Equal(0u, net.Read(Base + AdapterControl, 4) & (1u << 2));
    }

    [Fact]
    public void MiiEvent_W1C_Clears()
    {
        var net = new McpxNvNetDevice();
        net.Write(Base + IrqMask, 4, IrqMiiEvent);
        net.SetLinkUp(false);
        Assert.True(net.InterruptAsserted);

        net.Write(Base + IrqStatus, 4, IrqMiiEvent);

        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
        Assert.False(net.InterruptAsserted);

        net.Write(Base + MiiStatus, 4, MiiLinkChange);

        Assert.Equal(0u, net.Read(Base + MiiStatus, 4));
    }

    [Fact]
    public void LinkUp_Autoneg_Takes250ms()
    {
        var net = new McpxNvNetDevice();
        net.Write(Base + TxControl, 4, 1);
        net.Write(Base + IrqMask, 4, IrqMiiEvent);
        net.SetLinkUp(false);
        net.Write(Base + IrqStatus, 4, IrqMiiEvent);
        long changes = net.LinkChanges;

        net.SetLinkUp(true);

        // Autoneg enabled and incomplete: link stays down until the timer fires.
        Assert.True(net.AutoNegPending);
        Assert.False(net.LinkUp);
        Assert.Equal(changes, net.LinkChanges);

        net.Advance(XboxBus.CpuClockHz / 10);

        Assert.True(net.AutoNegPending);
        Assert.False(net.LinkUp);

        net.Advance(XboxBus.CpuClockHz);

        Assert.False(net.AutoNegPending);
        Assert.True(net.LinkUp);
        Assert.True(net.CanTransmit());
        Assert.Equal(changes + 1, net.LinkChanges);
        Assert.Equal(IrqMiiEvent, net.Read(Base + IrqStatus, 4));
    }

    [Fact]
    public void LinkUp_Instant_WhenAutoNegDisabled()
    {
        var net = new McpxNvNetDevice();
        // Clear BMCR AUTOEN via MDIO; autoneg can never complete on its own.
        net.Write(Base + MdioData, 4, 0x0100);
        net.Write(Base + MdioAddr, 4, 0x400 | 0x0);
        net.SetLinkUp(false);

        net.SetLinkUp(true);

        Assert.False(net.AutoNegPending);
        Assert.True(net.LinkUp);
        // The instant path sets LINK_ST only; AN_COMP stays clear (xemu parity).
        net.Write(Base + MdioAddr, 4, 0x1);
        uint bmsr = net.Read(Base + MdioData, 4);
        Assert.NotEqual(0u, bmsr & 0x04u);
        Assert.Equal(0u, bmsr & 0x20u);
    }

    [Fact]
    public void SetLinkUp_IsIdempotent()
    {
        var net = new McpxNvNetDevice();

        net.SetLinkUp(true);

        Assert.Equal(0, net.LinkChanges);
        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));

        net.SetLinkUp(false);
        net.SetLinkUp(false);

        Assert.Equal(1, net.LinkChanges);
    }

    [Fact]
    public void LinkDown_CancelsPendingAutoneg()
    {
        var net = new McpxNvNetDevice();
        net.SetLinkUp(false);
        net.SetLinkUp(true);
        Assert.True(net.AutoNegPending);

        net.SetLinkUp(false);

        Assert.False(net.AutoNegPending);
        net.Advance(XboxBus.CpuClockHz * 2);
        Assert.False(net.LinkUp);
    }

    [Fact]
    public void Advance_WhenIdle_IsSideEffectFree()
    {
        // Guest init-only steadiness: with no backend traffic and no autoneg
        // pending, Advance must not touch registers, counters, or the IRQ line.
        var net = new McpxNvNetDevice();
        int events = 0;
        net.InterruptChanged += _ => events++;

        net.Advance(10_000_000_000);

        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
        Assert.Equal(0u, net.Read(Base + MiiStatus, 4));
        Assert.Equal(0, net.TxPackets);
        Assert.Equal(0, net.RxPackets);
        Assert.Equal(0, net.RxDropFilter);
        Assert.Equal(0, net.RxDropNoBuf);
        Assert.Equal(0, net.RxOversize);
        Assert.Equal(0, net.LinkChanges);
        Assert.Equal(0, events);
        Assert.True(net.LinkUp);
        Assert.False(net.InterruptAsserted);
    }

    [Fact]
    public void Reset_RestoresLinkUp_ClearsRxState()
    {
        var net = new McpxNvNetDevice();
        net.Write(Base + RxControl, 4, 1);
        net.SetLinkUp(false);
        net.Write(Base + TxRxControl, 4, 0);

        net.Reset();

        Assert.True(net.LinkUp);
        Assert.False(net.AutoNegPending);
        Assert.Equal(0, net.RxPackets);
        Assert.Equal(0, net.RxBytes);
        Assert.Equal(0, net.RxDropFilter);
        Assert.Equal(0, net.RxDropNoBuf);
        Assert.Equal(0, net.RxOversize);
        Assert.Equal(0, net.CantReceiveCount);
        Assert.Equal(0, net.LinkChanges);
        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
    }
}
