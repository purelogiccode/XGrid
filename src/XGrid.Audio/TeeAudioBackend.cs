using System;

namespace XGrid.Audio;

/// <summary>
/// Fans one PCM stream out to several sinks (for example the live WASAPI device
/// and the WAVE capture in the same run). A failing sink does not stop the
/// others: audio is a diagnostic/product surface, never a guest-visible device.
/// </summary>
public sealed class TeeAudioBackend : IAudioBackend
{
    private readonly IAudioBackend[] _sinks;

    /// <summary>Initializes a new instance of the <see cref="TeeAudioBackend"/> class.</summary>
    /// <param name="sinks">The sinks that receive every submitted block.</param>
    public TeeAudioBackend(params IAudioBackend[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        if (sinks.Length == 0)
        {
            throw new ArgumentException("at least one sink is required", nameof(sinks));
        }

        _sinks = sinks;
    }

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<byte> samples)
    {
        foreach (IAudioBackend sink in _sinks)
        {
            try
            {
                sink.Submit(samples);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // One sink (for example a removed audio device) must not take
                // the emulation path down with it.
            }
        }
    }
}
