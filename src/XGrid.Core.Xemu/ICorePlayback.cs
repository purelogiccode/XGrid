namespace XGrid.Core.Xemu;

/// <summary>
/// Pause, resume, single-frame step and speed as the core exposes them. Every
/// call only queues work for the core's own main loop or updates an atomic the
/// loop reads; the shell reports the answer and keeps its own state.
/// </summary>
public interface ICorePlayback
{
    /// <summary>Queues a pause or resume.</summary>
    /// <param name="paused">True to pause, false to resume.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    bool TrySetPaused(bool paused, out string problem);

    /// <summary>Queues a single-frame step while the machine is paused.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    bool TryStepFrame(out string problem);

    /// <summary>Sets the presentation pace (1 is real time, up to 8).</summary>
    /// <param name="multiplier">Speed multiplier.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    bool TrySetSpeed(int multiplier, out string problem);
}
