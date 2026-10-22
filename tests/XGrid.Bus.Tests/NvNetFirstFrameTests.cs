using System;
using XGrid.Bus.Net;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class NvNetFirstFrameTests
{
    private const uint Base = McpxNvNetDevice.BaseAddress;

    private const uint IrqStatus = 0x000;
    private const uint IrqMask = 0x004;
    private const uint TxControl = 0x084;
    private const uint RxControl = 0x094;
    private const uint TxRingBase = 0x100;
    private const uint RxRingBase = 0x104;
    private const uint TxRingNext = 0x134;
    private const uint RxRingNext = 0x138;
    private const uint TxRxControl = 0x144;

    private const uint TxValid = 1u << 15;
    private const uint TxLastPacket = 1u << 0;
    private const uint RxAvail = 1u << 15;
    private const uint RxValid = 1u << 0;
    private const uint RxBit4 = 1u << 4;
    private const uint IrqRx = 1u << 1;
    private const uint IrqTx = 1u << 4;

    private const uint TxDescAddr = 0x1000;
    private const uint RxDescAddr = 0x2000;
    private const uint TxBufAddr = 0x3000;
    private const uint RxBufAddr = 0x4000;

    private const string GoldenDhcpDiscoverHex =
        "FFFFFFFFFFFF0050F2A4F5A8080045000148961C00004011E38900000000FFFF" +
        "FFFF004400430134CBA001010600F5EAFE080000000000000000000000000000" +
        "0000000000000050F2A4F5A80000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "0000000000000000000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000638253633501013D0701" +
        "0050F2A4F5A83C0858424F5820312E303703010306FF00000000000000000000" +
        "00000000000000000000000000000000000000000000";

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
            byte[] data = new byte[length];
            for (int i = 0; i < length; i++)
            {
                data[i] = ReadPhysical(address + (uint)i);
            }

            return data;
        }

        public ushort ReadDescriptorFlags(uint address)
        {
            return (ushort)(ReadPhysical(address + 6) | (ReadPhysical(address + 7) << 8));
        }

        public ushort ReadDescriptorLength(uint address)
        {
            return (ushort)(ReadPhysical(address + 4) | (ReadPhysical(address + 5) << 8));
        }

        public uint ReadDescriptorBuffer(uint address)
        {
            return ReadPhysical(address)
                   | ((uint)ReadPhysical(address + 1) << 8)
                   | ((uint)ReadPhysical(address + 2) << 16)
                   | ((uint)ReadPhysical(address + 3) << 24);
        }
    }

    private static byte[] GoldenFrame => Convert.FromHexString(GoldenDhcpDiscoverHex);

    private static ushort ReadBe16(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static ushort Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        int i = 0;
        while (i + 1 < data.Length)
        {
            sum += (uint)((data[i] << 8) | data[i + 1]);
            i += 2;
        }

        if (i < data.Length)
        {
            sum += (uint)(data[i] << 8);
        }

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }

    private static ushort UdpChecksumWithPseudoHeader(ReadOnlySpan<byte> frame)
    {
        int udpLength = ReadBe16(frame, 38);
        byte[] data = new byte[12 + udpLength];
        frame.Slice(26, 8).CopyTo(data.AsSpan(0));
        data[9] = 17;
        data[10] = (byte)(udpLength >> 8);
        data[11] = (byte)udpLength;
        frame.Slice(34, udpLength).CopyTo(data.AsSpan(12));
        return Checksum(data);
    }

    private static McpxNvNetDevice CreateDevice(FakeMemory memory, byte[] frame)
    {
        var net = new McpxNvNetDevice { DmaMemory = memory };
        net.Write(Base + TxRingBase, 4, TxDescAddr);
        net.Write(Base + RxRingBase, 4, RxDescAddr);
        net.Write(Base + 0x108, 4, 0x00010001);
        net.Write(Base + TxRingNext, 4, TxDescAddr);
        net.Write(Base + RxRingNext, 4, RxDescAddr);
        net.Write(Base + TxControl, 4, 1);
        net.Write(Base + RxControl, 4, 1);
        net.Write(Base + IrqMask, 4, IrqTx | IrqRx);

        memory.WriteDescriptor(
            TxDescAddr, TxBufAddr, (ushort)(frame.Length - 1), (ushort)(TxValid | TxLastPacket));
        memory.WritePayload(TxBufAddr, frame);
        memory.WriteDescriptor(RxDescAddr, RxBufAddr, 0x7FD, (ushort)RxAvail);
        return net;
    }

    [Fact]
    public void GoldenFrame_TxKickToRxPoll_RoundTripsThroughDeviceModel()
    {
        byte[] frame = GoldenFrame;
        var memory = new FakeMemory();
        McpxNvNetDevice net = CreateDevice(memory, frame);

        net.Write(Base + TxRxControl, 4, 1);

        var backend = Assert.IsType<LoopbackNetworkBackend>(net.Backend);
        Assert.Equal(1, net.TxPackets);
        Assert.Equal(frame.Length, net.TxBytes);
        Assert.Equal(frame, backend.SentLog[0]);

        Assert.Equal(TxBufAddr, memory.ReadDescriptorBuffer(TxDescAddr));
        Assert.Equal((ushort)(frame.Length - 1), memory.ReadDescriptorLength(TxDescAddr));
        Assert.Equal((ushort)TxLastPacket, memory.ReadDescriptorFlags(TxDescAddr));
        Assert.Equal(TxDescAddr, net.TxRingCurrentAddress);
        Assert.Equal(TxDescAddr + 8, net.TxRingNextAddress);
        Assert.Equal(IrqTx, net.Read(Base + IrqStatus, 4));
        Assert.True(net.InterruptAsserted);

        Assert.Equal(1, net.PollReceive());
        Assert.Equal(1, net.RxPackets);
        Assert.Equal(frame.Length, net.RxBytes);
        Assert.Equal(frame, memory.ReadPayload(RxBufAddr, frame.Length));
        Assert.Equal(RxBufAddr, memory.ReadDescriptorBuffer(RxDescAddr));
        Assert.Equal((ushort)frame.Length, memory.ReadDescriptorLength(RxDescAddr));
        Assert.Equal((ushort)(RxValid | RxBit4), memory.ReadDescriptorFlags(RxDescAddr));
        Assert.Equal(RxDescAddr, net.RxRingCurrentAddress);
        Assert.Equal(RxDescAddr + 8, net.RxRingNextAddress);
        Assert.Equal(IrqTx | IrqRx, net.Read(Base + IrqStatus, 4));

        net.Write(Base + IrqStatus, 4, IrqTx | IrqRx);
        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
        Assert.False(net.InterruptAsserted);
    }

    [Fact]
    public void GoldenFrame_MatchesCapturedLiveDhcpDiscoverFields()
    {
        byte[] frame = GoldenFrame;

        Assert.Equal(342, frame.Length);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, frame[..6]);
        Assert.Equal(new byte[] { 0x00, 0x50, 0xF2, 0xA4, 0xF5, 0xA8 }, frame[6..12]);
        Assert.Equal(0x0800, ReadBe16(frame, 12));

        Assert.Equal(0x45, frame[14]);
        Assert.Equal(frame.Length - 14, ReadBe16(frame, 16));
        Assert.Equal(64, frame[22]);
        Assert.Equal(17, frame[23]);
        Assert.Equal(0, Checksum(frame.AsSpan(14, 20)));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, frame[26..30]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, frame[30..34]);

        Assert.Equal(68, ReadBe16(frame, 34));
        Assert.Equal(67, ReadBe16(frame, 36));
        Assert.Equal(frame.Length - 34, ReadBe16(frame, 38));
        Assert.Equal(0, UdpChecksumWithPseudoHeader(frame));

        Assert.Equal(1, frame[42]);
        Assert.Equal(1, frame[43]);
        Assert.Equal(6, frame[44]);
        Assert.Equal(frame[6..12], frame[70..76]);
        Assert.Equal(new byte[] { 0x63, 0x82, 0x53, 0x63 }, frame[278..282]);

        Assert.Equal(53, frame[282]);
        Assert.Equal(1, frame[283]);
        Assert.Equal(1, frame[284]);
        Assert.Equal(61, frame[285]);
        Assert.Equal(7, frame[286]);
        Assert.Equal(frame[6..12], frame[288..294]);
        Assert.Equal(60, frame[294]);
        Assert.Equal(8, frame[295]);
        Assert.Equal("XBOX 1.0", System.Text.Encoding.ASCII.GetString(frame, 296, 8));
        Assert.Equal(55, frame[304]);
        Assert.Equal(3, frame[305]);
        Assert.Equal(255, frame[309]);
    }
}
