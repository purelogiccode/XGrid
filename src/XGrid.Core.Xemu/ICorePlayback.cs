namespace XGrid.Core.Xemu;

/// <summary>
/// Pause, resume and single-frame step as the core exposes them. Every call
/// only queues work for the core's own main loop; the shell reports the answer
/// and keeps its own paused state.
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
}
