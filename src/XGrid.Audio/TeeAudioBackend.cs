using System;
using Serilog;

namespace XGrid.Audio;

/// <summary>
/// Fans one PCM stream out to several sinks (for example the live WASAPI device
/// and the WAVE capture in the same run). A failing sink does not stop the
/// others: audio is a diagnostic/product surface, never a guest-visible device.
/// </summary>
public sealed class TeeAudioBackend : IAudioBackend
{
    private readonly IAudioBackend[] _sinks;
    private readonly bool[] _reportedFailures;

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
        _reportedFailures = new bool[sinks.Length];
    }

    /// <inheritdoc/>
    public void Submit(ReadOnlySpan<byte> samples)
    {
        for (int i = 0; i < _sinks.Length; i++)
        {
            try
            {
                _sinks[i].Submit(samples);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // One sink (for example a removed audio device) must not take
                // the emulation path down with it. Report the first failure as
                // a warning; later buffers would otherwise flood the log and
                // the bug-report queue.
                if (_reportedFailures[i])
                {
                    Log.Debug(exception, "An audio sink failed again and was skipped");
                }
                else
                {
                    _reportedFailures[i] = true;
                    Log.Warning(exception, "An audio sink failed and was skipped");
                }
            }
        }
    }
}
