using System;
using System.Threading;
using NAudio.Wave;

namespace XGrid.Audio;

/// <summary>
/// Live host audio sink: a shared-mode WASAPI output fed from the APU's EP
/// output FIFO (48 kHz, 16-bit, stereo). Windows-only; <see cref="WaveAudioBackend"/>
/// remains the portable sink and the byte-for-byte capture path.
/// </summary>
/// <remarks>
/// The APU submits one 32-sample reference frame at a time on the emulation
/// thread. The samples go into a bounded <see cref="BufferedWaveProvider"/>; the
/// WASAPI thread drains it independently, so the emulation thread never blocks
/// on the audio device. Overflow discards the oldest audio, which is the right
/// failure for a real-time sink.
/// </remarks>
public sealed class WasapiAudioBackend : IAudioBackend, IDisposable
{
    private readonly BufferedWaveProvider _buffer;
    private readonly WasapiPlayer _output;
    private long _bytesSubmitted;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="WasapiAudioBackend"/> class.</summary>
    /// <param name="latencyMilliseconds">Requested WASAPI shared-mode latency.</param>
    public WasapiAudioBackend(int latencyMilliseconds = 100)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WASAPI live audio is Windows-only; use --audio-out on this host.");
        }

        _buffer = new BufferedWaveProvider(
            new WaveFormat(WaveAudioBackend.SampleRate, WaveAudioBackend.BitsPerSample, WaveAudioBackend.Channels),
            TimeSpan.FromMilliseconds(500))
        {
            DiscardOnBufferOverflow = true,
        };

        _output = new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithLatency(latencyMilliseconds)
            .Build();
        _output.Init(_buffer);
        _output.Play();
    }

    /// <summary>Gets the number of PCM bytes submitted to the device.</summary>
    public long BytesSubmitted => Interlocked.Read(ref _bytesSubmitted);

    /// <summary>Gets the number of stereo frames submitted to the device.</summary>
    public long Frames => BytesSubmitted / (WaveAudioBackend.Channels * (WaveAudioBackend.BitsPerSample / 8));

    /// <summary>Gets the current device playback state (Stopped off Windows).</summary>
    public PlaybackState PlaybackState => OperatingSystem.IsWindows() ? _output.PlaybackState : PlaybackState.Stopped;

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<byte> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (samples.IsEmpty)
        {
            return;
        }

        _buffer.AddSamples(samples);
        Interlocked.Add(ref _bytesSubmitted, samples.Length);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (OperatingSystem.IsWindows())
        {
            _output.Stop();
            _output.Dispose();
        }
    }
}
