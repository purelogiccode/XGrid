using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Core.Snapshots;

namespace XGrid.Bus.Net;

/// <summary>
/// Default loopback/logger backend: records transmitted frames, counts bytes, and queues
/// a copy of each frame for loopback. The RX engine drains the queue through the receive
/// filter on <c>Advance</c>; the queue is bounded and drops the oldest frames when full.
/// </summary>
public sealed class LoopbackNetworkBackend : INetworkBackend, ISnapshotable
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

    /// <summary>Records a transmitted frame: increments <see cref="SentPackets"/> and
    /// <see cref="SentBytes"/>, keeps a bounded copy in <see cref="SentLog"/>, and queues a copy
    /// for loopback, dropping the oldest queued frame past <see cref="MaxQueuedFrames"/>.</summary>
    /// <param name="frame">The Ethernet frame the guest transmitted.</param>
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
    /// Drains one queued loopback frame for the RX engine.
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

    /// <summary>Saves queued loopback frames, the sent log, and counters.</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(SentPackets);
        writer.Write(SentBytes);
        writer.Write(DroppedPackets);
        writer.Write(_loopback.Count);
        foreach (byte[] frame in _loopback)
        {
            writer.Write(frame.Length);
            writer.Write(frame);
        }

        writer.Write(_sentLog.Count);
        foreach (byte[] frame in _sentLog)
        {
            writer.Write(frame.Length);
            writer.Write(frame);
        }
    }

    /// <summary>Restores queued loopback frames, the sent log, and counters.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported loopback snapshot version {version}.");
        }

        SentPackets = reader.ReadInt64();
        SentBytes = reader.ReadInt64();
        DroppedPackets = reader.ReadInt64();
        int queued = reader.ReadInt32();
        if (queued < 0 || queued > MaxQueuedFrames)
        {
            throw new InvalidDataException("Bad loopback queue snapshot.");
        }

        _loopback.Clear();
        for (int i = 0; i < queued; i++)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > RxLimit)
            {
                throw new InvalidDataException("Bad loopback frame snapshot.");
            }

            byte[] frame = reader.ReadBytes(length);
            if (frame.Length != length)
            {
                throw new InvalidDataException("Truncated loopback frame snapshot.");
            }

            _loopback.Enqueue(frame);
        }

        int logged = reader.ReadInt32();
        if (logged < 0 || logged > MaxLoggedFrames)
        {
            throw new InvalidDataException("Bad loopback log snapshot.");
        }

        _sentLog.Clear();
        for (int i = 0; i < logged; i++)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > RxLimit)
            {
                throw new InvalidDataException("Bad loopback log frame snapshot.");
            }

            byte[] frame = reader.ReadBytes(length);
            if (frame.Length != length)
            {
                throw new InvalidDataException("Truncated loopback log snapshot.");
            }

            _sentLog.Add(frame);
        }
    }

    private const int SnapshotVersion = 1;

    private const int RxLimit = 2048;
}
