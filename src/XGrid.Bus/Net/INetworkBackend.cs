using System;

namespace XGrid.Bus.Net;

/// <summary>
/// Pluggable network backend for nvnet frames (P3 seam). P1 only needs the transmit
/// direction; receive injection arrives with the P2 RX engine.
/// </summary>
public interface INetworkBackend
{
    /// <summary>Transmits one coalesced Ethernet frame from the guest.</summary>
    void SendPacket(ReadOnlySpan<byte> frame);
}
