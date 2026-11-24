namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Paces the console picture. The core publishes frames as fast as the guest
/// renders them (more than a hundred a second on this host), while the shell can
/// only present at the screen's rate: polling in the hundred-millisecond UI tick
/// turned a 60 fps title into ten pictures a second.
/// </summary>
public static class FramePacing
{
    /// <summary>Gets the presentation timer's polling interval in milliseconds.</summary>
    public const int PollIntervalMilliseconds = 8;

    /// <summary>Gets the minimum gap between two presents in milliseconds.</summary>
    public const int PresentIntervalMilliseconds = 10;

    /// <summary>
    /// Returns whether a frame may be presented now. The first call after a
    /// stop (a zero timestamp) always presents.
    /// </summary>
    /// <param name="now">Current timestamp from <c>Stopwatch.GetTimestamp</c>.</param>
    /// <param name="last">Timestamp of the previous present, or 0.</param>
    /// <param name="frequency">Timestamp ticks per second.</param>
    /// <returns>True when the previous present is old enough.</returns>
    public static bool ShouldPresent(long now, long last, long frequency)
    {
        if (last == 0 || frequency <= 0)
        {
            return true;
        }

        if (now < last)
        {
            // A reset or a clock adjustment: do not stall the picture.
            return true;
        }

        long gap = frequency * PresentIntervalMilliseconds / 1000;
        return now - last >= gap;
    }
}
