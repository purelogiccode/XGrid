using System;
using System.Net;
using System.Net.Sockets;
using XGrid.Bus.Net;
using Xunit;

// The spin-wait lambda runs synchronously before the using scope disposes the backend.
// ReSharper disable AccessToDisposedClosure

namespace XGrid.Bus.Tests;

public sealed class SocketNetworkBackendTests
{
    private static readonly byte[] GuestMac = new byte[] { 0x52, 0x54, 0x00, 0x12, 0x34, 0x56 };
    private const uint GuestIp = 0xC0A80002u;
    private const uint GatewayIp = SocketNetworkBackend.GatewayIpAddress;

    private static byte[] BuildArpRequest(byte[] senderMac, uint senderIp, uint targetIp)
    {
        byte[] frame = new byte[14 + 28];
        new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }.CopyTo(frame, 0);
        senderMac.CopyTo(frame, 6);
        frame[12] = 0x08;
        frame[13] = 0x06;
        Span<byte> arp = frame.AsSpan(14);
        arp[0] = 0;
        arp[1] = 1;
        arp[2] = 0x08;
        arp[3] = 0x00;
        arp[4] = 6;
        arp[5] = 4;
        arp[6] = 0;
        arp[7] = 1;
        senderMac.CopyTo(arp.Slice(8));
        arp[14] = (byte)(senderIp >> 24);
        arp[15] = (byte)(senderIp >> 16);
        arp[16] = (byte)(senderIp >> 8);
        arp[17] = (byte)senderIp;
        arp[18] = 0;
        arp[19] = 0;
        arp[20] = 0;
        arp[21] = 0;
        arp[22] = 0;
        arp[23] = 0;
        arp[24] = (byte)(targetIp >> 24);
        arp[25] = (byte)(targetIp >> 16);
        arp[26] = (byte)(targetIp >> 8);
        arp[27] = (byte)targetIp;
        return frame;
    }

    private static byte[] BuildIcmpEcho(byte[] srcMac, uint srcIp, uint dstIp, ushort ident, ushort seq, byte[] data)
    {
        byte[] frame = new byte[14 + 20 + 8 + data.Length];
        SocketNetworkBackend.GatewayMacAddress.CopyTo(frame, 0);
        srcMac.CopyTo(frame, 6);
        frame[12] = 0x08;
        frame[13] = 0x00;
        Span<byte> ip = frame.AsSpan(14, 20);
        ip[0] = 0x45;
        ip[1] = 0;
        ushort total = (ushort)(20 + 8 + data.Length);
        ip[2] = (byte)(total >> 8);
        ip[3] = (byte)total;
        ip[4] = 0;
        ip[5] = 1;
        ip[6] = 0;
        ip[7] = 0;
        ip[8] = 64;
        ip[9] = 1;
        ip[10] = 0;
        ip[11] = 0;
        ip[12] = (byte)(srcIp >> 24);
        ip[13] = (byte)(srcIp >> 16);
        ip[14] = (byte)(srcIp >> 8);
        ip[15] = (byte)srcIp;
        ip[16] = (byte)(dstIp >> 24);
        ip[17] = (byte)(dstIp >> 16);
        ip[18] = (byte)(dstIp >> 8);
        ip[19] = (byte)dstIp;
        ushort sum = IpChecksum(ip);
        ip[10] = (byte)(sum >> 8);
        ip[11] = (byte)sum;
        Span<byte> icmp = frame.AsSpan(34);
        icmp[0] = 8;
        icmp[1] = 0;
        icmp[2] = 0;
        icmp[3] = 0;
        icmp[4] = (byte)(ident >> 8);
        icmp[5] = (byte)ident;
        icmp[6] = (byte)(seq >> 8);
        icmp[7] = (byte)seq;
        data.CopyTo(icmp.Slice(8));
        sum = IpChecksum(icmp);
        icmp[2] = (byte)(sum >> 8);
        icmp[3] = (byte)sum;
        return frame;
    }

    private static ushort IpChecksum(ReadOnlySpan<byte> data)
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

    private static byte[] BuildUdpFrame(byte[] srcMac, uint srcIp, ushort srcPort, uint dstIp, ushort dstPort,
        byte[] payload)
    {
        byte[] frame = new byte[14 + 20 + 8 + payload.Length];
        SocketNetworkBackend.GatewayMacAddress.CopyTo(frame, 0);
        srcMac.CopyTo(frame, 6);
        frame[12] = 0x08;
        frame[13] = 0x00;
        Span<byte> ip = frame.AsSpan(14, 20);
        ip[0] = 0x45;
        ip[1] = 0;
        ushort total = (ushort)(20 + 8 + payload.Length);
        ip[2] = (byte)(total >> 8);
        ip[3] = (byte)total;
        ip[4] = 0;
        ip[5] = 2;
        ip[6] = 0;
        ip[7] = 0;
        ip[8] = 64;
        ip[9] = 17;
        ip[10] = 0;
        ip[11] = 0;
        ip[12] = (byte)(srcIp >> 24);
        ip[13] = (byte)(srcIp >> 16);
        ip[14] = (byte)(srcIp >> 8);
        ip[15] = (byte)srcIp;
        ip[16] = (byte)(dstIp >> 24);
        ip[17] = (byte)(dstIp >> 16);
        ip[18] = (byte)(dstIp >> 8);
        ip[19] = (byte)dstIp;
        ushort sum = IpChecksum(ip);
        ip[10] = (byte)(sum >> 8);
        ip[11] = (byte)sum;
        Span<byte> udp = frame.AsSpan(34);
        udp[0] = (byte)(srcPort >> 8);
        udp[1] = (byte)srcPort;
        udp[2] = (byte)(dstPort >> 8);
        udp[3] = (byte)dstPort;
        ushort udpLen = (ushort)(8 + payload.Length);
        udp[4] = (byte)(udpLen >> 8);
        udp[5] = (byte)udpLen;
        udp[6] = 0;
        udp[7] = 0;
        payload.CopyTo(udp.Slice(8));
        return frame;
    }

    [Fact]
    public void ArpRequest_ForGateway_GetsReply()
    {
        using var backend = new SocketNetworkBackend();
        backend.SendPacket(BuildArpRequest(GuestMac, GuestIp, GatewayIp));

        Assert.True(backend.TryReceive(out byte[]? reply));
        Assert.NotNull(reply);
        Assert.Equal(14 + 28, reply.Length);
        // Ethernet: to requestor, from gateway.
        Assert.Equal(GuestMac, reply[0..6]);
        Assert.Equal(SocketNetworkBackend.GatewayMacAddress, reply[6..12]);
        // ARP reply: opcode 2, sender gateway, target requestor.
        Assert.Equal(2, (reply[20] << 8) | reply[21]);
        Assert.Equal(SocketNetworkBackend.GatewayMacAddress, reply[22..28]);
        Assert.Equal(new byte[] { 0xC0, 0xA8, 0x00, 0x01 }, reply[28..32]);
        Assert.Equal(GuestMac, reply[32..38]);
        Assert.Equal(new byte[] { 0xC0, 0xA8, 0x00, 0x02 }, reply[38..42]);
        Assert.Equal(1, backend.ArpReplies);
        Assert.Equal(GuestIp, backend.GuestIp);
        Assert.False(backend.TryReceive(out _));
    }

    [Fact]
    public void ArpRequest_ForOtherHost_Ignored()
    {
        using var backend = new SocketNetworkBackend();
        backend.SendPacket(BuildArpRequest(GuestMac, GuestIp, 0xC0A80063u));

        Assert.False(backend.TryReceive(out _));
        Assert.Equal(1, backend.DroppedPackets);
        Assert.Equal(0, backend.ArpReplies);
    }

    [Fact]
    public void IcmpEcho_ToGateway_GetsReply()
    {
        using var backend = new SocketNetworkBackend();
        byte[] data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        backend.SendPacket(BuildIcmpEcho(GuestMac, GuestIp, GatewayIp, 0x1234, 0x0007, data));

        Assert.True(backend.TryReceive(out byte[]? reply));
        Assert.NotNull(reply);
        // MACs/IPs swapped, ICMP type 0, ident/seq/data preserved.
        Assert.Equal(GuestMac, reply[0..6]);
        Assert.Equal(SocketNetworkBackend.GatewayMacAddress, reply[6..12]);
        Assert.Equal(new byte[] { 0xC0, 0xA8, 0x00, 0x01 }, reply[26..30]);
        Assert.Equal(new byte[] { 0xC0, 0xA8, 0x00, 0x02 }, reply[30..34]);
        Assert.Equal(0, reply[34]);
        Assert.Equal(0, reply[35]);
        Assert.Equal(0x1234, (reply[38] << 8) | reply[39]);
        Assert.Equal(0x0007, (reply[40] << 8) | reply[41]);
        Assert.Equal(data, reply[42..46]);
        // Both checksums valid.
        Assert.Equal(0, IpChecksum(reply.AsSpan(14, 20)));
        Assert.Equal(0, IpChecksum(reply.AsSpan(34)));
        Assert.Equal(1, backend.IcmpReplies);
    }

    [Fact]
    public void UdpRelay_LoopbackRoundTrip()
    {
        using var backend = new SocketNetworkBackend();
        using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Client.ReceiveTimeout = 5000;
        int port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
        uint loopback = 0x7F000001u;
        byte[] payload = new byte[] { 0x68, 0x65, 0x6C, 0x6C, 0x6F };

        backend.SendPacket(BuildUdpFrame(GuestMac, GuestIp, 40000, loopback, (ushort)port, payload));

        Assert.Equal(1, backend.UdpRelayed);
        IPEndPoint remote = new(IPAddress.Any, 0);
        byte[] received = listener.Receive(ref remote);
        Assert.Equal(payload, received);

        byte[] answer = new byte[] { 0x6F, 0x6B };
        listener.Send(answer, answer.Length, remote);

        byte[]? inbound = null;
        Assert.True(
            System.Threading.SpinWait.SpinUntil(
                () => backend.TryReceive(out inbound),
                TimeSpan.FromSeconds(5)),
            "udp reply was not delivered back to the backend");
        Assert.NotNull(inbound);
        // Re-encapsulated: gateway MAC -> guest MAC, sender -> guest.
        Assert.Equal(GuestMac, inbound![0..6]);
        Assert.Equal(SocketNetworkBackend.GatewayMacAddress, inbound[6..12]);
        Assert.Equal(new byte[] { 0x7F, 0x00, 0x00, 0x01 }, inbound[26..30]);
        Assert.Equal(new byte[] { 0xC0, 0xA8, 0x00, 0x02 }, inbound[30..34]);
        Assert.Equal(port, (inbound[34] << 8) | inbound[35]);
        Assert.Equal(40000, (inbound[36] << 8) | inbound[37]);
        Assert.Equal(answer, inbound[42..44]);
        Assert.Equal(0, IpChecksum(inbound.AsSpan(14, 20)));
        Assert.Equal(1, backend.UdpReceived);
    }

    [Fact]
    public void MalformedFrames_Dropped()
    {
        using var backend = new SocketNetworkBackend();
        backend.SendPacket(new byte[] { 1, 2, 3 });
        backend.SendPacket(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0x86, 0xDD });

        Assert.False(backend.TryReceive(out _));
        Assert.Equal(2, backend.DroppedPackets);
    }

    [Fact]
    public void TcpFrame_DroppedAndCounted()
    {
        using var backend = new SocketNetworkBackend();
        byte[] frame = BuildUdpFrame(GuestMac, GuestIp, 40000, 0x08080808u, 80, new byte[] { 1 });
        frame[14 + 9] = 6;

        backend.SendPacket(frame);

        Assert.False(backend.TryReceive(out _));
        Assert.Equal(1, backend.DroppedPackets);
        Assert.Equal(0, backend.UdpRelayed);
    }

    [Fact]
    public void DisposedBackend_DropsAndNeverReceives()
    {
        var backend = new SocketNetworkBackend();
        backend.Dispose();

        backend.SendPacket(BuildArpRequest(GuestMac, GuestIp, GatewayIp));

        Assert.False(backend.TryReceive(out _));
        Assert.Equal(1, backend.DroppedPackets);
    }
}
