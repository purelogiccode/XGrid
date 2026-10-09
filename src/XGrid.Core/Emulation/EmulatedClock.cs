namespace XGrid.Core.Emulation;

/// <summary>Monotonic count of emulated CPU cycles. The single source of emulated time.</summary>
public sealed class EmulatedClock
{
    /// <summary>Creates a clock for the given time mode.</summary>
    public EmulatedClock(TimeMode mode)
    {
        Mode = mode;
    }

    /// <summary>Gets how time advances.</summary>
    public TimeMode Mode { get; }

    /// <summary>Gets the number of cycles elapsed since the last reset.</summary>
    public ulong Cycles { get; private set; }

    /// <summary>Advances the clock by the given number of cycles.</summary>
    public void Advance(ulong cycles)
    {
        checked
        {
            Cycles += cycles;
        }
    }

    /// <summary>Returns the clock to zero.</summary>
    public void Reset()
    {
        Cycles = 0;
    }
}
