using XGrid.Bus.Net;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class NvNetReceiveTests
{
    private const uint Base = McpxNvNetDevice.BaseAddress;

    private const uint IrqStatus = 0x000;
    private const uint IrqMask = 0x004;
    private const uint PacketFilter = 0x08C;
    private const uint RxControl = 0x094;
    private const uint MacAddrA = 0x0A8;
    private const uint MacAddrB = 0x0AC;
    private const uint MulticastAddrA = 0x0B0;
    private const uint MulticastAddrB = 0x0B4;
    private const uint MulticastMaskA = 0x0B8;
    private const uint MulticastMaskB = 0x0BC;
    private const uint TxRingBase = 0x100;
    private const uint RxRingBase = 0x104;
    private const uint RingSize = 0x108;
    private const uint TxRingNext = 0x134;
    private const uint RxRingNext = 0x138;
    private const uint TxRxControl = 0x144;
    private const uint MiiStatus = 0x180;
    private const uint MdioAddr = 0x190;
    private const uint MdioData = 0x194;

    private const uint CtrlBit2 = 1u << 2;
    private const uint IrqRx = 1u << 1;
    private const uint IrqRxNoBuf = 1u << 2;
    private const uint IrqMiiEvent = 1u << 6;
    private const uint MiiLinkChange = 1u << 3;

    private const uint RxAvail = 1u << 15;
    private const uint RxValid = 1u << 0;
    private const uint RxBit4 = 1u << 4;

    private const uint TxBaseAddr = 0x1000;
    private const uint RxBaseAddr = 0x2000;

    private static readonly byte[] BroadcastMac = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    private static readonly byte[] GuestMac = new byte[] { 0x52, 0x54, 0x00, 0x12, 0x34, 0x56 };
    private static readonly byte[] OtherMac = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };

    private sealed class FakeMemory : INvNetMemory
    {
        private readonly byte[] _ram = new byte[64 * 1024];

        public byte ReadPhysical(uint address)
        {
            return _ram[address];
        }

        public void WritePhysical(uint address, byte value)
        {
            _ram[address] = value;
        }

        public void WriteDescriptor(uint address, uint buf, ushort len, ushort flags)
        {
            WritePhysical(address, (byte)buf);
            WritePhysical(address + 1, (byte)(buf >> 8));
            WritePhysical(address + 2, (byte)(buf >> 16));
            WritePhysical(address + 3, (byte)(buf >> 24));
            WritePhysical(address + 4, (byte)len);
            WritePhysical(address + 5, (byte)(len >> 8));
            WritePhysical(address + 6, (byte)flags);
            WritePhysical(address + 7, (byte)(flags >> 8));
        }

        public void WritePayload(uint address, byte[] payload)
        {
            for (int i = 0; i < payload.Length; i++)
            {
                WritePhysical(address + (uint)i, payload[i]);
            }
        }

        public byte[] ReadPayload(uint address, int length)
        {
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = ReadPhysical(address + (uint)i);
            }

            return payload;
        }

        public ushort ReadDescriptorFlags(uint address)
        {
            return (ushort)(ReadPhysical(address + 6) | (ReadPhysical(address + 7) << 8));
        }

        public ushort ReadDescriptorLength(uint address)
        {
            return (ushort)(ReadPhysical(address + 4) | (ReadPhysical(address + 5) << 8));
        }
    }

    private static McpxNvNetDevice CreateReceiver(FakeMemory memory, uint rxCountMask = 0)
    {
        var net = new McpxNvNetDevice { DmaMemory = memory };
        net.Write(Base + TxRingBase, 4, TxBaseAddr);
        net.Write(Base + RxRingBase, 4, RxBaseAddr);
        net.Write(Base + RingSize, 4, rxCountMask << 16);
        net.Write(Base + RxRingNext, 4, RxBaseAddr);
        net.Write(Base + RxControl, 4, 1);
        return net;
    }

    private static void WriteMac(McpxNvNetDevice net, byte[] mac)
    {
        uint regA = (uint)(mac[0] | (mac[1] << 8) | (mac[2] << 16) | (mac[3] << 24));
        uint regB = (uint)(mac[4] | (mac[5] << 8));
        net.Write(Base + MacAddrA, 4, regA);
        net.Write(Base + MacAddrB, 4, regB);
    }

    private static void WriteMulticast(McpxNvNetDevice net, byte[] addr, byte[] mask)
    {
        net.Write(Base + MulticastAddrA, 4, (uint)(addr[0] | (addr[1] << 8) | (addr[2] << 16) | (addr[3] << 24)));
        net.Write(Base + MulticastAddrB, 4, (uint)(addr[4] | (addr[5] << 8)));
        net.Write(Base + MulticastMaskA, 4, (uint)(mask[0] | (mask[1] << 8) | (mask[2] << 16) | (mask[3] << 24)));
        net.Write(Base + MulticastMaskB, 4, (uint)(mask[4] | (mask[5] << 8)));
    }

    private static byte[] BuildFrame(byte[] dstMac, byte[] payload)
    {
        byte[] frame = new byte[14 + payload.Length];
        dstMac.CopyTo(frame, 0);
        GuestMac.CopyTo(frame, 6);
        frame[12] = 0x08;
        frame[13] = 0x00;
        payload.CopyTo(frame, 14);
        return frame;
    }

    private static void PostRxBuffer(FakeMemory memory, uint descAddr, uint bufAddr, ushort capacityMinusOne)
    {
        memory.WriteDescriptor(descAddr, bufAddr, capacityMinusOne, (ushort)RxAvail);
    }

    [Fact]
    public void Rx_DeliversFrame_ConsumesAvail()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        PostRxBuffer(memory, RxBaseAddr, 0x3000, 63);
        byte[] frame = BuildFrame(BroadcastMac, new byte[] { 1, 2, 3, 4, 5, 6 });
        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        backend.SendPacket(frame);

        int delivered = net.PollReceive();

        Assert.Equal(1, delivered);
        Assert.Equal(frame, memory.ReadPayload(0x3000, frame.Length));
        // Length writeback is the raw size; AVAIL consumed, VALID+BIT4 set.
        Assert.Equal((ushort)frame.Length, memory.ReadDescriptorLength(RxBaseAddr));
        Assert.Equal(RxValid | RxBit4, memory.ReadDescriptorFlags(RxBaseAddr));
        Assert.Equal(1, net.RxPackets);
        Assert.Equal(frame.Length, net.RxBytes);
        Assert.Equal(IrqRx, net.Read(Base + IrqStatus, 4));
        // Single-entry ring: NEXT wraps to BASE, CURRENT latched the consumed desc.
        Assert.Equal(RxBaseAddr, net.RxRingCurrentAddress);
        Assert.Equal(RxBaseAddr, net.RxRingNextAddress);
        Assert.Equal(0, backend.QueuedFrames);
    }

    [Fact]
    public void Rx_TwoDescRing_WrapsNext()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory, rxCountMask: 1);
        Assert.Equal(2u, net.RxRingCount);
        PostRxBuffer(memory, RxBaseAddr, 0x3000, 63);
        PostRxBuffer(memory, RxBaseAddr + 8, 0x3100, 63);
        byte[] first = BuildFrame(BroadcastMac, new byte[] { 0xAA });
        byte[] second = BuildFrame(BroadcastMac, new byte[] { 0xBB, 0xCC });
        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        backend.SendPacket(first);
        backend.SendPacket(second);

        Assert.Equal(2, net.PollReceive());

        Assert.Equal(first, memory.ReadPayload(0x3000, first.Length));
        Assert.Equal(second, memory.ReadPayload(0x3100, second.Length));
        Assert.Equal(RxBaseAddr + 8, net.RxRingCurrentAddress);
        Assert.Equal(RxBaseAddr, net.RxRingNextAddress);
        Assert.Equal(2, net.RxPackets);
    }

    [Fact]
    public void Filter_BroadcastAlwaysPasses_WithMyAddrSet()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        net.Write(Base + PacketFilter, 4, 0x20);
        WriteMac(net, OtherMac);
        PostRxBuffer(memory, RxBaseAddr, 0x3000, 63);

        Assert.True(net.InjectFrame(BuildFrame(BroadcastMac, new byte[] { 9 })));
        Assert.Equal(1, net.RxPackets);
    }

    [Fact]
    public void Filter_UnicastMatchPasses_MismatchDrops()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        net.Write(Base + PacketFilter, 4, 0x20);
        WriteMac(net, GuestMac);
        // Broadcast multicast address disables the multicast path (xemu-literal).
        WriteMulticast(net, BroadcastMac, new byte[] { 0, 0, 0, 0, 0, 0 });
        PostRxBuffer(memory, RxBaseAddr, 0x3000, 63);

        Assert.True(net.PassesFilter(BuildFrame(GuestMac, new byte[] { 1 })));
        Assert.False(net.PassesFilter(BuildFrame(OtherMac, new byte[] { 1 })));

        Assert.True(net.InjectFrame(BuildFrame(GuestMac, new byte[] { 1 })));
        // Re-post: the drop below must leave the fresh AVAIL descriptor untouched.
        PostRxBuffer(memory, RxBaseAddr, 0x3000, 63);
        Assert.False(net.InjectFrame(BuildFrame(OtherMac, new byte[] { 2 })));
        Assert.Equal(1, net.RxPackets);
        Assert.Equal(1, net.RxDropFilter);
        // The dropped frame leaves the AVAIL descriptor untouched and raises no IRQ.
        Assert.Equal(RxAvail, memory.ReadDescriptorFlags(RxBaseAddr));
        Assert.Equal(IrqRx, net.Read(Base + IrqStatus, 4));
    }

    [Fact]
    public void Filter_MyAddrClear_AcceptsAll()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        net.Write(Base + PacketFilter, 4, 0);
        WriteMac(net, OtherMac);

        Assert.True(net.PassesFilter(BuildFrame(OtherMac, new byte[] { 1 })));
    }

    [Fact]
    public void Filter_MulticastMaskedCompare()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        net.Write(Base + PacketFilter, 4, 0x20);
        WriteMac(net, GuestMac);
        // The programmed address is compared against the masked destination,
        // so don't-care bytes must already be zero (forcedeth programs it so).
        WriteMulticast(
            net,
            new byte[] { 0x01, 0x00, 0x5E, 0x00, 0x00, 0x00 },
            new byte[] { 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00 });

        Assert.True(net.PassesFilter(BuildFrame(new byte[] { 0x01, 0x00, 0x5E, 0xAA, 0xBB, 0xCC }, new byte[] { 1 })));
        Assert.False(net.PassesFilter(BuildFrame(new byte[] { 0x33, 0x33, 0x00, 0x00, 0x00, 0x01 }, new byte[] { 1 })));
    }

    [Fact]
    public void Filter_ShortFrame_Drops()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);

        Assert.False(net.PassesFilter(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.False(net.InjectFrame(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Equal(1, net.RxDropFilter);
    }

    [Fact]
    public void Rx_NoBuf_RaisesNoBufIrq()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        memory.WriteDescriptor(RxBaseAddr, 0x3000, 63, 0);
        net.Write(Base + IrqMask, 4, IrqRxNoBuf);

        Assert.False(net.InjectFrame(BuildFrame(BroadcastMac, new byte[] { 7 })));

        Assert.Equal(1, net.RxDropNoBuf);
        Assert.Equal(IrqRxNoBuf, net.Read(Base + IrqStatus, 4));
        Assert.True(net.InterruptAsserted);
        // No descriptor advance on the miss.
        Assert.Equal(RxBaseAddr, net.RxRingNextAddress);
    }

    [Fact]
    public void Rx_ShortDescriptor_TreatedAsNoBuf()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        PostRxBuffer(memory, RxBaseAddr, 0x3000, 3);

        Assert.False(net.InjectFrame(BuildFrame(BroadcastMac, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })));

        Assert.Equal(1, net.RxDropNoBuf);
        Assert.Equal(0, net.RxPackets);
    }

    [Fact]
    public void Rx_Oversize_Dropped()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        PostRxBuffer(memory, RxBaseAddr, 0x3000, 63);
        byte[] frame = BuildFrame(BroadcastMac, new byte[McpxNvNetDevice.RxBufferSize]);

        Assert.False(net.InjectFrame(frame));
        Assert.Equal(1, net.RxOversize);
        Assert.Equal(0, net.RxPackets);
        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
        Assert.Equal(RxAvail, memory.ReadDescriptorFlags(RxBaseAddr));
    }

    [Fact]
    public void Gates_ReportReasons_AndBlockInject()
    {
        var memory = new FakeMemory();
        var net = new McpxNvNetDevice { DmaMemory = memory };
        net.Write(Base + RxRingBase, 4, RxBaseAddr);
        net.Write(Base + RxRingNext, 4, RxBaseAddr);

        Assert.Equal("rx-start", net.CanReceiveReason());
        Assert.False(net.CanReceive());

        net.Write(Base + RxControl, 4, 1);
        net.Write(Base + TxRxControl, 4, CtrlBit2);
        Assert.Equal("bit2", net.CanReceiveReason());

        net.Write(Base + TxRxControl, 4, 0);
        net.SetLinkUp(false);
        Assert.Equal("link", net.CanReceiveReason());

        net.SetLinkUp(true);
        Assert.True(net.AutoNegPending);
        net.Advance(XboxBus.CpuClockHz);
        Assert.True(net.LinkUp);
        PostRxBuffer(memory, RxBaseAddr, 0x3000, 63);
        Assert.Null(net.CanReceiveReason());
        Assert.True(net.CanReceive());

        memory.WriteDescriptor(RxBaseAddr, 0x3000, 63, 0);
        Assert.Equal("no-buf", net.CanReceiveReason());

        Assert.False(net.InjectFrame(BuildFrame(BroadcastMac, new byte[] { 1 })));
        Assert.Equal(1, net.RxDropNoBuf);

        // A gate-blocked inject (RX_START clear) counts cant_rx instead.
        var gated = new McpxNvNetDevice { DmaMemory = memory };
        gated.Write(Base + RxRingBase, 4, RxBaseAddr);
        gated.Write(Base + RxRingNext, 4, RxBaseAddr);
        Assert.False(gated.InjectFrame(BuildFrame(BroadcastMac, new byte[] { 1 })));
        Assert.Equal(1, gated.CantReceiveCount);
        Assert.Equal(0, gated.RxDropNoBuf);
    }

    [Fact]
    public void LoopbackRoundTrip_TxToRx()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        net.Write(Base + TxRingBase, 4, TxBaseAddr);
        net.Write(Base + TxRingNext, 4, TxBaseAddr);
        net.Write(Base + 0x084, 4, 1);
        PostRxBuffer(memory, RxBaseAddr, 0x3100, 127);
        byte[] payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        byte[] frame = BuildFrame(BroadcastMac, payload);
        memory.WriteDescriptor(TxBaseAddr, 0x3000, (ushort)(frame.Length - 1), 0x8001);
        memory.WritePayload(0x3000, frame);

        net.Write(Base + TxRxControl, 4, 1);

        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        Assert.Equal(1, net.TxPackets);
        Assert.Equal(1, backend.QueuedFrames);

        Assert.Equal(1, net.PollReceive());

        Assert.Equal(1, net.RxPackets);
        Assert.Equal(frame, memory.ReadPayload(0x3100, frame.Length));
        Assert.Equal(0, backend.QueuedFrames);
    }

    [Fact]
    public void PendingFrame_RedeliveredAfterBuffersPosted()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        memory.WriteDescriptor(RxBaseAddr, 0x3000, 63, 0);
        byte[] frame = BuildFrame(BroadcastMac, new byte[] { 0x42 });

        Assert.False(net.InjectFrame(frame));
        Assert.Equal(1, net.RxDropNoBuf);

        PostRxBuffer(memory, RxBaseAddr, 0x3000, 63);

        Assert.Equal(1, net.PollReceive());
        Assert.Equal(frame, memory.ReadPayload(0x3000, frame.Length));
        Assert.Equal(0, net.PollReceive());
    }

    [Fact]
    public void Advance_DrainsBackend()
    {
        var memory = new FakeMemory();
        var net = CreateReceiver(memory);
        PostRxBuffer(memory, RxBaseAddr, 0x3000, 63);
        byte[] frame = BuildFrame(BroadcastMac, new byte[] { 5 });
        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        backend.SendPacket(frame);

        net.Advance(0);

        Assert.Equal(1, net.RxPackets);
        Assert.Equal(frame, memory.ReadPayload(0x3000, frame.Length));
    }
}
