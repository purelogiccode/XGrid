using System;
using System.Collections.Generic;

namespace XGrid.Bus.Net;

/// <summary>
/// Default loopback/logger backend: records transmitted frames, counts bytes, and queues
/// a copy of each frame for loopback. The queue is the P2 RX seam (the RX engine will
/// drain it through the receive filter); nothing drains it yet, so it is bounded and
/// drops the oldest frames when full.
/// </summary>
public sealed class LoopbackNetworkBackend : INetworkBackend
{
    /// <summary>Maximum queued loopback frames before the oldest is dropped.</summary>
    public const int MaxQueuedFrames = 64;

    /// <summary>Maximum transmitted-frame copies kept in the log.</summary>
    public const int MaxLoggedFrames = 16;

    private readonly Queue<byte[]> _loopback = new();
    private readonly List<byte[]> _sentLog = new();

    /// <summary>Gets the number of frames handed to the backend.</summary>
    public long SentPackets { get; private set; }

    /// <summary>Gets the total transmitted frame bytes.</summary>
    public long SentBytes { get; private set; }

    /// <summary>Gets the number of loopback frames dropped because the queue was full.</summary>
    public long DroppedPackets { get; private set; }

    /// <summary>Gets recent transmitted-frame copies (newest last), for diagnostics.</summary>
    public IReadOnlyList<byte[]> SentLog => _sentLog;

    /// <summary>Gets the number of frames currently queued for loopback.</summary>
    public int QueuedFrames => _loopback.Count;

    /// <inheritdoc />
    public void SendPacket(ReadOnlySpan<byte> frame)
    {
        byte[] copy = frame.ToArray();
        SentPackets++;
        SentBytes += copy.Length;

        _sentLog.Add(copy);
        if (_sentLog.Count > MaxLoggedFrames)
        {
            _sentLog.RemoveAt(0);
        }

        _loopback.Enqueue(copy);
        while (_loopback.Count > MaxQueuedFrames)
        {
            _loopback.Dequeue();
            DroppedPackets++;
        }
    }

    /// <summary>
    /// Drains one queued loopback frame. P2 RX seam; currently only used by tests.
    /// </summary>
    public bool TryReceive(out byte[]? frame)
    {
        if (_loopback.Count == 0)
        {
            frame = null;
            return false;
        }

        frame = _loopback.Dequeue();
        return true;
    }
}
