using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace XGrid.Bus.Net;

/// <summary>
/// Socket-based user-mode IP backend (xemu SLIRP-equivalent role, reimplemented):
/// answers ARP for a virtual gateway, answers ICMP echo to the gateway, and relays
/// guest UDP through host sockets (unicast and broadcast) with NAT-style port
/// mapping. TCP and non-gateway IP protocols are dropped and counted. All socket
/// polling is non-blocking; responses queue for the RX engine via
/// <see cref="TryReceive"/>. TAP/bridge operation is explicitly out of scope.
/// </summary>
public sealed class SocketNetworkBackend : INetworkBackend, IDisposable
{
    /// <summary>Virtual gateway IPv4 address (192.168.0.1) in network byte order.</summary>
    public const uint GatewayIpAddress = 0xC0A80001u;

    /// <summary>Default guest IPv4 address (192.168.0.2) until learned from outbound frames.</summary>
    public const uint DefaultGuestIpAddress = 0xC0A80002u;

    /// <summary>Maximum queued response frames before the oldest is dropped.</summary>
    public const int MaxQueuedFrames = 64;

    /// <summary>Maximum transmitted-frame copies kept in the log.</summary>
    public const int MaxLoggedFrames = 16;

    /// <summary>Maximum concurrent UDP NAT mappings.</summary>
    public const int MaxUdpMappings = 16;

    /// <summary>UDP mapping idle lifetime in milliseconds.</summary>
    public const long UdpMappingLifetimeMs = 60_000;

    /// <summary>Virtual gateway MAC (locally administered).</summary>
    public static byte[] GatewayMacAddress { get; } = new byte[] { 0x52, 0x55, 0x0A, 0x00, 0x02, 0x01 };

    private const ushort EtherTypeArp = 0x0806;
    private const ushort EtherTypeIpv4 = 0x0800;
    private const byte IpProtoIcmp = 1;
    private const byte IpProtoUdp = 17;
    private const byte IcmpEchoRequest = 8;
    private const byte IcmpEchoReply = 0;

    private readonly Queue<byte[]> _responses = new();
    private readonly List<byte[]> _sentLog = new();
    private readonly List<UdpMapping> _udpMappings = new();
    private readonly Dictionary<uint, byte[]> _ipToMac = new();
    private readonly byte[] _guestMac = new byte[6];
    private bool _haveGuestMac;
    private uint _guestIp = DefaultGuestIpAddress;
    private ushort _ipIdent;
    private bool _disposed;

    /// <summary>Gets the number of frames handed to the backend.</summary>
    public long SentPackets { get; private set; }

    /// <summary>Gets the total transmitted frame bytes.</summary>
    public long SentBytes { get; private set; }

    /// <summary>Gets the number of frames dropped (runt, unsupported, queue/mapping overflow).</summary>
    public long DroppedPackets { get; private set; }

    /// <summary>Gets the number of ARP replies generated.</summary>
    public long ArpReplies { get; private set; }

    /// <summary>Gets the number of ICMP echo replies generated.</summary>
    public long IcmpReplies { get; private set; }

    /// <summary>Gets the number of UDP datagrams relayed to the host.</summary>
    public long UdpRelayed { get; private set; }

    /// <summary>Gets the number of UDP datagrams received from the host.</summary>
    public long UdpReceived { get; private set; }

    /// <summary>Gets recent transmitted-frame copies (newest last), for diagnostics.</summary>
    public IReadOnlyList<byte[]> SentLog => _sentLog;

    /// <summary>Gets the number of response frames currently queued for the guest.</summary>
    public int QueuedFrames => _responses.Count;

    /// <summary>Gets the guest IPv4 address learned from outbound frames (network order).</summary>
    public uint GuestIp => _guestIp;

    /// <inheritdoc />
    public void SendPacket(ReadOnlySpan<byte> frame)
    {
        if (_disposed)
        {
            DroppedPackets++;
            return;
        }

        if (frame.Length < 14)
        {
            DroppedPackets++;
            return;
        }

        byte[] copy = frame.ToArray();
        SentPackets++;
        SentBytes += copy.Length;
        _sentLog.Add(copy);
        if (_sentLog.Count > MaxLoggedFrames)
        {
            _sentLog.RemoveAt(0);
        }

        copy.AsSpan(6, 6).CopyTo(_guestMac);
        _haveGuestMac = true;

        ushort etherType = ReadU16BE(frame, 12);
        if (etherType == EtherTypeArp)
        {
            HandleArp(frame);
        }
        else if (etherType == EtherTypeIpv4)
        {
            HandleIpv4(frame);
        }
        else
        {
            DroppedPackets++;
        }
    }

    /// <inheritdoc />
    public bool TryReceive(out byte[]? frame)
    {
        if (!_disposed)
        {
            if (_responses.Count != 0)
            {
                frame = _responses.Dequeue();
                return true;
            }

            PollUdp();
            SweepUdp();
            if (_responses.Count != 0)
            {
                frame = _responses.Dequeue();
                return true;
            }
        }

        frame = null;
        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (UdpMapping mapping in _udpMappings)
        {
            mapping.Socket.Dispose();
        }

        _udpMappings.Clear();
        GC.SuppressFinalize(this);
    }

    private void HandleArp(ReadOnlySpan<byte> frame)
    {
        // Ethernet(14) + ARP(28): reply to requests targeting the gateway.
        if (frame.Length < 14 + 28)
        {
            DroppedPackets++;
            return;
        }

        ReadOnlySpan<byte> arp = frame.Slice(14);
        if (ReadU16BE(arp, 0) != 1 || ReadU16BE(arp, 2) != EtherTypeIpv4 ||
            arp[4] != 6 || arp[5] != 4 || ReadU16BE(arp, 6) != 1)
        {
            DroppedPackets++;
            return;
        }

        uint targetIp = ReadU32BE(arp, 24);
        if (targetIp != GatewayIpAddress)
        {
            DroppedPackets++;
            return;
        }

        byte[] senderMac = arp.Slice(8, 6).ToArray();
        uint senderIp = ReadU32BE(arp, 14);
        LearnHost(senderIp, senderMac);

        byte[] reply = new byte[14 + 28];
        senderMac.CopyTo(reply, 0);
        GatewayMacAddress.CopyTo(reply, 6);
        WriteU16BE(reply, 12, EtherTypeArp);
        Span<byte> body = reply.AsSpan(14);
        WriteU16BE(body, 0, 1);
        WriteU16BE(body, 2, EtherTypeIpv4);
        body[4] = 6;
        body[5] = 4;
        WriteU16BE(body, 6, 2);
        GatewayMacAddress.AsSpan().CopyTo(body.Slice(8));
        WriteU32BE(body, 14, GatewayIpAddress);
        senderMac.AsSpan().CopyTo(body.Slice(18));
        WriteU32BE(body, 24, senderIp);
        EnqueueResponse(reply);
        ArpReplies++;
    }

    private void HandleIpv4(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 14 + 20)
        {
            DroppedPackets++;
            return;
        }

        ReadOnlySpan<byte> ip = frame.Slice(14);
        if ((ip[0] >> 4) != 4)
        {
            DroppedPackets++;
            return;
        }

        int headerLength = (ip[0] & 0x0F) * 4;
        if (headerLength < 20 || frame.Length < 14 + headerLength)
        {
            DroppedPackets++;
            return;
        }

        uint srcIp = ReadU32BE(ip, 12);
        uint dstIp = ReadU32BE(ip, 16);
        byte[] srcMac = frame.Slice(6, 6).ToArray();
        LearnHost(srcIp, srcMac);

        if (dstIp == GatewayIpAddress)
        {
            if (ip[9] == IpProtoIcmp)
            {
                HandleIcmp(frame, headerLength);
            }
            else
            {
                DroppedPackets++;
            }

            return;
        }

        if (ip[9] == IpProtoUdp)
        {
            RelayUdp(frame, headerLength, srcIp, dstIp);
        }
        else
        {
            DroppedPackets++;
        }
    }

    private void HandleIcmp(ReadOnlySpan<byte> frame, int headerLength)
    {
        int icmpOffset = 14 + headerLength;
        if (frame.Length < icmpOffset + 8)
        {
            DroppedPackets++;
            return;
        }

        if (frame[icmpOffset] != IcmpEchoRequest || frame[icmpOffset + 1] != 0)
        {
            DroppedPackets++;
            return;
        }

        // Echo reply: swap MACs/IPs, type 0, recomputed checksum.
        byte[] reply = frame.ToArray();
        byte[] requestorMac = frame.Slice(6, 6).ToArray();
        uint requestorIp = ReadU32BE(frame, 14 + 12);
        requestorMac.CopyTo(reply, 0);
        GatewayMacAddress.CopyTo(reply, 6);
        WriteU32BE(reply.AsSpan(), 14 + 12, GatewayIpAddress);
        WriteU32BE(reply.AsSpan(), 14 + 16, requestorIp);
        reply[icmpOffset] = IcmpEchoReply;
        WriteU16BE(reply, icmpOffset + 2, 0);
        WriteU16BE(reply, icmpOffset + 2, Checksum(reply.AsSpan(icmpOffset)));
        WriteU16BE(reply, 14 + 10, 0);
        WriteU16BE(reply, 14 + 10, Checksum(reply.AsSpan(14, (reply[14] & 0x0F) * 4)));
        EnqueueResponse(reply);
        IcmpReplies++;
    }

    private void RelayUdp(ReadOnlySpan<byte> frame, int headerLength, uint srcIp, uint dstIp)
    {
        int udpOffset = 14 + headerLength;
        if (frame.Length < udpOffset + 8)
        {
            DroppedPackets++;
            return;
        }

        ushort srcPort = ReadU16BE(frame, udpOffset);
        ushort dstPort = ReadU16BE(frame, udpOffset + 2);
        int udpLength = ReadU16BE(frame, udpOffset + 4);
        if (udpLength < 8 || frame.Length < udpOffset + udpLength)
        {
            DroppedPackets++;
            return;
        }

        ReadOnlySpan<byte> payload = frame.Slice(udpOffset + 8, udpLength - 8);
        UdpMapping? mapping = FindUdpMapping(srcIp, srcPort, dstIp, dstPort);
        if (mapping is null)
        {
            if (_udpMappings.Count >= MaxUdpMappings)
            {
                DroppedPackets++;
                return;
            }

            Socket socket;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.EnableBroadcast = true;
            }
            catch (SocketException)
            {
                DroppedPackets++;
                return;
            }

            mapping = new UdpMapping(socket, srcIp, srcPort, dstIp, dstPort);
            _udpMappings.Add(mapping);
        }

        try
        {
            IPAddress target = IsBroadcastIp(dstIp)
                ? IPAddress.Broadcast
                : new IPAddress(ToBigEndianBytes(dstIp));
            mapping.Socket.SendTo(payload.ToArray(), new IPEndPoint(target, dstPort));
            mapping.LastActivity = Environment.TickCount64;
            UdpRelayed++;
        }
        catch (SocketException)
        {
            DroppedPackets++;
        }
    }

    private void PollUdp()
    {
        foreach (UdpMapping mapping in _udpMappings)
        {
            Socket socket = mapping.Socket;
            try
            {
                if (socket.Available <= 0)
                {
                    continue;
                }

                byte[] buffer = new byte[2048];
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int received = socket.ReceiveFrom(buffer, ref remote);
                if (remote is not IPEndPoint sender || received <= 0)
                {
                    continue;
                }

                byte[]? inbound = BuildUdpInbound(mapping, sender, buffer.AsSpan(0, received));
                if (inbound is not null)
                {
                    EnqueueResponse(inbound);
                    mapping.LastActivity = Environment.TickCount64;
                    UdpReceived++;
                    return;
                }
            }
            catch (SocketException)
            {
                mapping.Failed = true;
            }
            catch (ObjectDisposedException)
            {
                mapping.Failed = true;
            }
        }

        _udpMappings.RemoveAll(static mapping => mapping.Failed);
    }

    private byte[]? BuildUdpInbound(UdpMapping mapping, IPEndPoint sender, ReadOnlySpan<byte> payload)
    {
        if (!_haveGuestMac)
        {
            DroppedPackets++;
            return null;
        }

        if (!_ipToMac.TryGetValue(mapping.GuestIp, out byte[]? guestMac))
        {
            guestMac = (byte[])_guestMac.Clone();
        }

        uint senderIp = FromBigEndianBytes(sender.Address.GetAddressBytes());
        byte[] frame = new byte[14 + 20 + 8 + payload.Length];
        guestMac.CopyTo(frame, 0);
        GatewayMacAddress.CopyTo(frame, 6);
        WriteU16BE(frame, 12, EtherTypeIpv4);

        Span<byte> ip = frame.AsSpan(14, 20);
        ip[0] = 0x45;
        ip[1] = 0;
        WriteU16BE(ip, 2, (ushort)(20 + 8 + payload.Length));
        WriteU16BE(ip, 4, _ipIdent++);
        WriteU16BE(ip, 6, 0);
        ip[8] = 64;
        ip[9] = IpProtoUdp;
        WriteU16BE(ip, 10, 0);
        WriteU32BE(ip, 12, senderIp);
        WriteU32BE(ip, 16, mapping.GuestIp);
        WriteU16BE(ip, 10, Checksum(ip));

        Span<byte> udp = frame.AsSpan(34, 8 + payload.Length);
        WriteU16BE(udp, 0, (ushort)sender.Port);
        WriteU16BE(udp, 2, mapping.GuestPort);
        WriteU16BE(udp, 4, (ushort)(8 + payload.Length));
        WriteU16BE(udp, 6, 0);
        payload.CopyTo(udp.Slice(8));
        WriteU16BE(udp, 6, UdpChecksum(senderIp, mapping.GuestIp, IpProtoUdp, udp));
        return frame;
    }

    private void SweepUdp()
    {
        long now = Environment.TickCount64;
        for (int i = _udpMappings.Count - 1; i >= 0; i--)
        {
            UdpMapping mapping = _udpMappings[i];
            if (mapping.Failed || now - mapping.LastActivity > UdpMappingLifetimeMs)
            {
                mapping.Socket.Dispose();
                _udpMappings.RemoveAt(i);
            }
        }
    }

    private UdpMapping? FindUdpMapping(uint srcIp, ushort srcPort, uint dstIp, ushort dstPort)
    {
        foreach (UdpMapping mapping in _udpMappings)
        {
            if (!mapping.Failed && mapping.GuestIp == srcIp && mapping.GuestPort == srcPort &&
                mapping.DstIp == dstIp && mapping.DstPort == dstPort)
            {
                return mapping;
            }
        }

        return null;
    }

    private void LearnHost(uint ip, byte[] mac)
    {
        _guestIp = ip;
        _ipToMac[ip] = (byte[])mac.Clone();
    }

    private void EnqueueResponse(byte[] frame)
    {
        _responses.Enqueue(frame);
        while (_responses.Count > MaxQueuedFrames)
        {
            _responses.Dequeue();
            DroppedPackets++;
        }
    }

    private static bool IsBroadcastIp(uint ip)
    {
        // Limited broadcast or the virtual subnet's directed broadcast.
        return ip == 0xFFFFFFFFu || ip == (GatewayIpAddress | 0xFFu);
    }

    private static ushort ReadU16BE(ReadOnlySpan<byte> span, int offset)
    {
        return (ushort)((span[offset] << 8) | span[offset + 1]);
    }

    private static uint ReadU32BE(ReadOnlySpan<byte> span, int offset)
    {
        return ((uint)span[offset] << 24) | ((uint)span[offset + 1] << 16) |
            ((uint)span[offset + 2] << 8) | span[offset + 3];
    }

    private static void WriteU16BE(Span<byte> span, int offset, ushort value)
    {
        span[offset] = (byte)(value >> 8);
        span[offset + 1] = (byte)value;
    }

    private static void WriteU16BE(byte[] array, int offset, ushort value)
    {
        array[offset] = (byte)(value >> 8);
        array[offset + 1] = (byte)value;
    }

    private static void WriteU32BE(Span<byte> span, int offset, uint value)
    {
        span[offset] = (byte)(value >> 24);
        span[offset + 1] = (byte)(value >> 16);
        span[offset + 2] = (byte)(value >> 8);
        span[offset + 3] = (byte)value;
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

    private static ushort UdpChecksum(uint srcIp, uint dstIp, byte proto, ReadOnlySpan<byte> udp)
    {
        uint sum = ((srcIp >> 16) & 0xFFFF) + (srcIp & 0xFFFF) +
            ((dstIp >> 16) & 0xFFFF) + (dstIp & 0xFFFF) +
            proto + (uint)udp.Length;
        int i = 0;
        while (i + 1 < udp.Length)
        {
            sum += (uint)((udp[i] << 8) | udp[i + 1]);
            i += 2;
        }

        if (i < udp.Length)
        {
            sum += (uint)(udp[i] << 8);
        }

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        ushort result = (ushort)~sum;
        return result == 0 ? (ushort)0xFFFF : result;
    }

    private static byte[] ToBigEndianBytes(uint ip)
    {
        return new byte[] { (byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip };
    }

    private static uint FromBigEndianBytes(byte[] bytes)
    {
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private sealed class UdpMapping
    {
        public UdpMapping(Socket socket, uint guestIp, ushort guestPort, uint dstIp, ushort dstPort)
        {
            Socket = socket;
            GuestIp = guestIp;
            GuestPort = guestPort;
            DstIp = dstIp;
            DstPort = dstPort;
            LastActivity = Environment.TickCount64;
        }

        public Socket Socket { get; }

        public uint GuestIp { get; }

        public ushort GuestPort { get; }

        public uint DstIp { get; }

        public ushort DstPort { get; }

        public long LastActivity { get; set; }

        public bool Failed { get; set; }
    }
}
