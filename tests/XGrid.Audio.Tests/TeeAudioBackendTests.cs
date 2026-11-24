using System;
using System.Collections.Generic;
using Xunit;

namespace XGrid.Audio.Tests;

/// <summary>
/// The tee sink fans one APU PCM stream out to a live device and a capture at
/// the same time; one failing sink must never take the emulation path down.
/// </summary>
public sealed class TeeAudioBackendTests
{
    [Fact]
    public void Tee_FansOutEveryBlockToEverySink()
    {
        var first = new RecordingBackend();
        var second = new RecordingBackend();
        var tee = new TeeAudioBackend(first, second);

        tee.Submit([1, 2, 3, 4]);
        tee.Submit([5, 6]);

        Assert.Equal([1, 2, 3, 4, 5, 6], first.Bytes);
        Assert.Equal([1, 2, 3, 4, 5, 6], second.Bytes);
    }

    [Fact]
    public void Tee_OneFailingSinkDoesNotStopTheOthers()
    {
        var good = new RecordingBackend();
        var tee = new TeeAudioBackend(new ThrowingBackend(), good);

        tee.Submit([7, 8, 9, 10]);

        Assert.Equal([7, 8, 9, 10], good.Bytes);
    }

    [Fact]
    public void Tee_RequiresAtLeastOneSink()
    {
        Assert.Throws<ArgumentException>(() => new TeeAudioBackend());
    }

    [Fact]
    public void Tee_RetriesAFailedSinkOnTheNextBlock()
    {
        var flaky = new FailOnceBackend();
        var tee = new TeeAudioBackend(flaky);

        tee.Submit([1, 2]);
        tee.Submit([3, 4]);

        Assert.Equal([3, 4], flaky.Bytes);
    }

    private sealed class FailOnceBackend : IAudioBackend
    {
        private readonly List<byte> _bytes = [];
        private bool _failed;

        public byte[] Bytes => _bytes.ToArray();

        public void Submit(ReadOnlySpan<byte> samples)
        {
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("device not ready");
            }

            foreach (byte value in samples)
            {
                _bytes.Add(value);
            }
        }
    }

    private sealed class RecordingBackend : IAudioBackend
    {
        private readonly List<byte> _bytes = [];

        public byte[] Bytes => _bytes.ToArray();

        public void Submit(ReadOnlySpan<byte> samples)
        {
            foreach (byte value in samples)
            {
                _bytes.Add(value);
            }
        }
    }

    private sealed class ThrowingBackend : IAudioBackend
    {
        public void Submit(ReadOnlySpan<byte> samples)
        {
            throw new InvalidOperationException("device removed");
        }
    }
}
