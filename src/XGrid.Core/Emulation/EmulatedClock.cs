namespace XGrid.Core.Emulation;

/// <summary>Monotonic count of emulated CPU cycles. The single source of emulated time.</summary>
/// <remarks>Creates a clock for the given time mode.</remarks>
public sealed class EmulatedClock(TimeMode mode)
{

    /// <summary>Gets how time advances.</summary>
    public TimeMode Mode { get; } = mode;

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
