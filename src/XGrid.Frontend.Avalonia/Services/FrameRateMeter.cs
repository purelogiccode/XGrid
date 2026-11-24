using System.Threading;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Counts presented frames over a fixed wall-clock window so the shell can show
/// a stable frame rate without a second timer.
/// </summary>
internal sealed class FrameRateMeter
{
    /// <summary>The reporting window in milliseconds.</summary>
    internal const int WindowMilliseconds = 1000;

    private long _windowStart = -1;
    private int _frames;

    /// <summary>Records one presented frame.</summary>
    public void AddFrame()
    {
        Interlocked.Increment(ref _frames);
    }

    /// <summary>
    /// Returns a new reading when the window has elapsed.
    /// </summary>
    /// <param name="nowMilliseconds">Monotonic time in milliseconds.</param>
    /// <param name="framesPerSecond">Frames per second over the window.</param>
    /// <param name="frameTimeMs">Average interval between frames, in milliseconds.</param>
    /// <returns>True when the window closed and the values were updated.</returns>
    public bool TryUpdate(long nowMilliseconds, out double framesPerSecond, out double frameTimeMs)
    {
        framesPerSecond = 0;
        frameTimeMs = 0;
        if (_windowStart < 0)
        {
            _windowStart = nowMilliseconds;
            return false;
        }

        long elapsed = nowMilliseconds - _windowStart;
        if (elapsed < WindowMilliseconds)
        {
            return false;
        }

        int frames = Interlocked.Exchange(ref _frames, 0);
        framesPerSecond = frames * 1000.0 / elapsed;
        frameTimeMs = framesPerSecond > 0 ? 1000.0 / framesPerSecond : 0;
        _windowStart = nowMilliseconds;
        return true;
    }

    /// <summary>Clears the window and the pending frames.</summary>
    public void Reset()
    {
        _windowStart = -1;
        Interlocked.Exchange(ref _frames, 0);
    }
}
