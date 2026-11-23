using System;

namespace XGrid.Bus.Net;

/// <summary>
/// Pluggable network backend for nvnet frames (P3 seam). The device polls
/// <see cref="TryReceive"/> from <c>Advance</c> and delivers queued frames
/// through the P2 RX engine (can_receive gates + receive filter + RX DMA).
/// </summary>
public interface INetworkBackend
{
    /// <summary>Transmits one coalesced Ethernet frame from the guest.</summary>
    void SendPacket(ReadOnlySpan<byte> frame);

    /// <summary>
    /// Drains one frame queued for the guest. Returns false when no frame is
    /// available; never blocks (socket backends poll non-blocking).
    /// </summary>
    bool TryReceive(out byte[]? frame);
}
