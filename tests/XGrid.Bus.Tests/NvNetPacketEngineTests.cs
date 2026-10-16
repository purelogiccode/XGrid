using XGrid.Bus.Net;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class NvNetPacketEngineTests
{
    private const uint Base = McpxNvNetDevice.BaseAddress;

    private const uint IrqStatus = 0x000;
    private const uint IrqMask = 0x004;
    private const uint TxControl = 0x084;
    private const uint RxControl = 0x094;
    private const uint TxRingBase = 0x100;
    private const uint RxRingBase = 0x104;
    private const uint RingSize = 0x108;
    private const uint TxRingCurrent = 0x11C;
    private const uint RxRingCurrent = 0x120;
    private const uint TxRingNext = 0x134;
    private const uint RxRingNext = 0x138;
    private const uint TxRxControl = 0x144;
    private const uint MdioAddr = 0x190;
    private const uint MdioData = 0x194;

    private const uint Kick = 1u << 0;
    private const uint CtrlBit1 = 1u << 1;
    private const uint CtrlBit2 = 1u << 2;
    private const uint CtrlIdle = 1u << 3;
    private const uint CtrlReset = 1u << 4;
    private const uint IrqTx = 1u << 4;

    private const uint TxValid = 1u << 15;
    private const uint TxLastPacket = 1u << 0;

    private const uint TxBaseAddr = 0x1000;
    private const uint RxBaseAddr = 0x2000;

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

        public ushort ReadDescriptorFlags(uint address)
        {
            return (ushort)(ReadPhysical(address + 6) | (ReadPhysical(address + 7) << 8));
        }

        public ushort ReadDescriptorLength(uint address)
        {
            return (ushort)(ReadPhysical(address + 4) | (ReadPhysical(address + 5) << 8));
        }
    }

    private static McpxNvNetDevice CreateTransmitter(FakeMemory memory, uint txCountMask = 0)
    {
        var net = new McpxNvNetDevice { DmaMemory = memory };
        net.Write(Base + TxRingBase, 4, TxBaseAddr);
        net.Write(Base + RxRingBase, 4, RxBaseAddr);
        net.Write(Base + RingSize, 4, txCountMask);
        net.Write(Base + TxRingNext, 4, TxBaseAddr);
        net.Write(Base + TxControl, 4, 1);
        return net;
    }

    [Fact]
    public void ResetBit_SetsCurrentNextToBase()
    {
        var net = new McpxNvNetDevice();
        net.Write(Base + TxRingBase, 4, TxBaseAddr);
        net.Write(Base + RxRingBase, 4, RxBaseAddr);
        net.Write(Base + RingSize, 4, 0x00010001);
        net.Write(Base + TxRingCurrent, 4, 0x9999);
        net.Write(Base + TxRingNext, 4, 0x9999);
        net.Write(Base + RxRingCurrent, 4, 0x9999);
        net.Write(Base + RxRingNext, 4, 0x9999);

        net.Write(Base + TxRxControl, 4, CtrlReset);

        Assert.Equal(TxBaseAddr, net.TxRingCurrentAddress);
        Assert.Equal(TxBaseAddr, net.TxRingNextAddress);
        Assert.Equal(RxBaseAddr, net.RxRingCurrentAddress);
        Assert.Equal(RxBaseAddr, net.RxRingNextAddress);
    }

    [Fact]
    public void Kick_Unprogrammed_WalksZeroDescs()
    {
        var net = new McpxNvNetDevice();
        int events = 0;
        net.InterruptChanged += _ => events++;
        net.Write(Base + TxControl, 4, 1);

        net.Write(Base + TxRxControl, 4, Kick);

        Assert.Equal(1, net.TxKicks);
        Assert.Equal(0, net.TxPackets);
        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
        Assert.NotEqual(0u, net.Read(Base + TxRxControl, 4) & CtrlIdle);
        Assert.Equal(0, events);
    }

    [Fact]
    public void RingSize_CountIsMaskPlusOne()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory, txCountMask: 1);
        memory.WriteDescriptor(TxBaseAddr, 0x3000, 1, (ushort)TxValid);
        memory.WriteDescriptor(TxBaseAddr + 8, 0x3010, 1, (ushort)(TxValid | TxLastPacket));
        memory.WritePayload(0x3000, new[] { (byte)'A', (byte)'B' });
        memory.WritePayload(0x3010, new[] { (byte)'C', (byte)'D' });

        Assert.Equal(2u, net.TxRingCount);

        net.Write(Base + TxRxControl, 4, Kick);

        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        Assert.Equal(1, backend.SentPackets);
        Assert.Equal(new[] { (byte)'A', (byte)'B', (byte)'C', (byte)'D' }, backend.SentLog[0]);
        // Both descriptors consumed; NEXT wrapped past the 2-entry ring to BASE.
        Assert.Equal(0u, memory.ReadDescriptorFlags(TxBaseAddr));
        Assert.Equal(TxLastPacket, memory.ReadDescriptorFlags(TxBaseAddr + 8));
        Assert.Equal(TxBaseAddr, net.TxRingNextAddress);
        Assert.Equal(TxBaseAddr + 8, net.TxRingCurrentAddress);
    }

    [Fact]
    public void OutOfRangeNext_ClampsToBase()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory);
        memory.WriteDescriptor(TxBaseAddr, 0x3000, 0, 0);

        net.Write(Base + TxRingNext, 4, 0xDEAD);
        net.Write(Base + TxRxControl, 4, Kick);

        Assert.Equal(TxBaseAddr, net.TxRingCurrentAddress);
        Assert.Equal(0, net.TxPackets);

        // Below-base NEXT clamps the same way, with no pointer advance on the !VALID stop.
        net.Write(Base + TxRingNext, 4, 0x10);
        net.Write(Base + TxRxControl, 4, Kick);

        Assert.Equal(TxBaseAddr, net.TxRingCurrentAddress);
        Assert.Equal(0x10u, net.TxRingNextAddress);
    }

    [Fact]
    public void Irq_MaskGated_AssertAndDeassert()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory);
        int asserts = 0;
        int deasserts = 0;
        net.InterruptChanged += a =>
        {
            if (a)
            {
                asserts++;
            }
            else
            {
                deasserts++;
            }
        };
        memory.WriteDescriptor(TxBaseAddr, 0x3000, 3, (ushort)(TxValid | TxLastPacket));
        memory.WritePayload(0x3000, new byte[] { 1, 2, 3, 4 });

        net.Write(Base + TxRxControl, 4, Kick);

        Assert.Equal(IrqTx, net.Read(Base + IrqStatus, 4));
        Assert.False(net.InterruptAsserted);
        Assert.Equal(0, asserts);

        net.Write(Base + IrqMask, 4, IrqTx);

        Assert.True(net.InterruptAsserted);
        Assert.Equal(1, asserts);
        Assert.Equal(1, net.IrqAsserts);

        net.Write(Base + IrqStatus, 4, IrqTx);

        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
        Assert.False(net.InterruptAsserted);
        Assert.Equal(1, deasserts);
        Assert.Equal(1, net.IrqDeasserts);
    }

    [Fact]
    public void Bit1Write_ClearsIrqAndDeasserts()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory);
        int deasserts = 0;
        net.InterruptChanged += a =>
        {
            if (!a)
            {
                deasserts++;
            }
        };
        net.Write(Base + IrqMask, 4, IrqTx);
        memory.WriteDescriptor(TxBaseAddr, 0x3000, 0, (ushort)(TxValid | TxLastPacket));
        memory.WritePayload(0x3000, new byte[] { 9 });
        net.Write(Base + TxRxControl, 4, Kick);
        Assert.True(net.InterruptAsserted);

        net.Write(Base + TxRxControl, 4, CtrlBit1);

        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
        Assert.False(net.InterruptAsserted);
        Assert.Equal(1, deasserts);
    }

    [Fact]
    public void Tx_SingleDesc_SendsPacketAndClearsValid()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory);
        memory.WriteDescriptor(TxBaseAddr, 0x3000, 3, (ushort)(TxValid | TxLastPacket));
        memory.WritePayload(0x3000, new[] { (byte)'P', (byte)'I', (byte)'N', (byte)'G' });

        net.Write(Base + TxRxControl, 4, Kick);

        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        Assert.Equal(1, backend.SentPackets);
        Assert.Equal(new[] { (byte)'P', (byte)'I', (byte)'N', (byte)'G' }, backend.SentLog[0]);
        Assert.Equal(1, net.TxPackets);
        Assert.Equal(4, net.TxBytes);
        // VALID cleared, LASTPACKET retained, length field preserved.
        Assert.Equal(TxLastPacket, memory.ReadDescriptorFlags(TxBaseAddr));
        Assert.Equal(3u, memory.ReadDescriptorLength(TxBaseAddr));
        Assert.Equal(IrqTx, net.Read(Base + IrqStatus, 4));
        Assert.NotEqual(0u, net.Read(Base + TxRxControl, 4) & CtrlIdle);
    }

    [Fact]
    public void Tx_MultiDesc_CoalescesToLastPacket()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory, txCountMask: 2);
        memory.WriteDescriptor(TxBaseAddr, 0x3000, 0, (ushort)TxValid);
        memory.WriteDescriptor(TxBaseAddr + 8, 0x3010, 0, (ushort)TxValid);
        memory.WriteDescriptor(TxBaseAddr + 16, 0x3020, 0, (ushort)(TxValid | TxLastPacket));
        memory.WritePayload(0x3000, new byte[] { 1 });
        memory.WritePayload(0x3010, new byte[] { 2 });
        memory.WritePayload(0x3020, new byte[] { 3 });

        net.Write(Base + TxRxControl, 4, Kick);

        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        Assert.Equal(1, backend.SentPackets);
        Assert.Equal(new byte[] { 1, 2, 3 }, backend.SentLog[0]);
        Assert.Equal(3, net.TxBytes);
        Assert.Equal(TxBaseAddr + 16, net.TxRingCurrentAddress);
    }

    [Fact]
    public void Tx_StopsAtInvalidDesc()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory, txCountMask: 1);
        memory.WriteDescriptor(TxBaseAddr, 0x3000, 1, (ushort)TxValid);
        memory.WriteDescriptor(TxBaseAddr + 8, 0x3010, 1, 0);
        memory.WritePayload(0x3000, new[] { (byte)'A', (byte)'B' });

        net.Write(Base + TxRxControl, 4, Kick);

        // First descriptor consumed, but no LASTPACKET reached: no frame, no IRQ.
        // CURRENT tracks the stop descriptor; NEXT only advances past consumed ones.
        Assert.Equal(0, net.TxPackets);
        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
        Assert.False(net.InterruptAsserted);
        Assert.Equal(0u, memory.ReadDescriptorFlags(TxBaseAddr));
        Assert.Equal(TxBaseAddr + 8, net.TxRingCurrentAddress);
        Assert.Equal(TxBaseAddr + 8, net.TxRingNextAddress);
    }

    [Fact]
    public void Tx_LengthIsLenPlusOne()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory);
        memory.WriteDescriptor(TxBaseAddr, 0x3000, 0, (ushort)(TxValid | TxLastPacket));
        memory.WritePayload(0x3000, new byte[] { 0xAB });

        net.Write(Base + TxRxControl, 4, Kick);

        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        Assert.Equal(new byte[] { 0xAB }, backend.SentLog[0]);
        Assert.Equal(1, net.TxBytes);
    }

    [Fact]
    public void Tx_Gated_WhenTxStartClear()
    {
        var memory = new FakeMemory();
        var net = new McpxNvNetDevice { DmaMemory = memory };
        net.Write(Base + TxRxControl, 4, Kick);

        Assert.Equal(1, net.CantTransmitCount);
        Assert.Equal("tx-start", net.CantTransmitReason());
        Assert.False(net.CanTransmit());
        Assert.Equal(0, net.TxPackets);
        Assert.NotEqual(0u, net.Read(Base + TxRxControl, 4) & CtrlIdle);
    }

    [Fact]
    public void Tx_Gated_WhenBit2Set()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory);
        net.Write(Base + TxRxControl, 4, CtrlBit2);

        Assert.Equal("bit2", net.CantTransmitReason());

        net.Write(Base + TxRxControl, 4, CtrlBit2 | Kick);

        Assert.Equal(1, net.CantTransmitCount);
        Assert.Equal(0, net.TxPackets);
    }

    [Fact]
    public void Tx_Gated_WhenLinkDown()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory);
        net.Write(Base + MdioData, 4, 0x0028);
        net.Write(Base + MdioAddr, 4, 0x400 | 0x1);

        Assert.Equal("link", net.CantTransmitReason());

        memory.WriteDescriptor(TxBaseAddr, 0x3000, 0, (ushort)(TxValid | TxLastPacket));
        net.Write(Base + TxRxControl, 4, Kick);

        Assert.Equal(1, net.CantTransmitCount);
        Assert.Equal(0, net.TxPackets);
    }

    [Fact]
    public void DefaultBackend_LoopsBackFrame()
    {
        var memory = new FakeMemory();
        var net = CreateTransmitter(memory);
        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        memory.WriteDescriptor(TxBaseAddr, 0x3000, 1, (ushort)(TxValid | TxLastPacket));
        memory.WritePayload(0x3000, new byte[] { 7, 8 });

        net.Write(Base + TxRxControl, 4, Kick);

        Assert.Equal(1, backend.QueuedFrames);
        Assert.True(backend.TryReceive(out byte[]? frame));
        Assert.Equal(new byte[] { 7, 8 }, frame);
        Assert.False(backend.TryReceive(out _));
    }

    [Fact]
    public void UnusedRingAndFilterRegs_Latch()
    {
        var net = new McpxNvNetDevice();
        net.Write(Base + RxControl, 4, 1);
        net.Write(Base + 0x08C, 4, 0x007F0008);
        net.Write(Base + 0x0B0, 4, 0x11223344);
        net.Write(Base + 0x0BC, 4, 0x55667788);

        Assert.Equal(1u, net.Read(Base + RxControl, 4));
        Assert.Equal(0x007F0008u, net.Read(Base + 0x08C, 4));
        Assert.Equal(0x11223344u, net.Read(Base + 0x0B0, 4));
        Assert.Equal(0x55667788u, net.Read(Base + 0x0BC, 4));
        Assert.Equal(1u, net.RxRingCount);
    }
}
