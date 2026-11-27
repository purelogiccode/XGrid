using System;

namespace XGrid.Core.Xemu;

/// <summary>
/// One running core, hosted in the shell's process or in its own. The session
/// drives a runtime and never branches on the hosting mode itself.
/// </summary>
public interface IXemuCoreRuntime : IDisposable
{
    /// <summary>Gets a value indicating whether the machine is alive.</summary>
    bool IsRunning { get; }

    /// <summary>Gets the exit code once the machine has stopped.</summary>
    int ExitCode { get; }

    /// <summary>Gets how long the machine has been running.</summary>
    TimeSpan Uptime { get; }

    /// <summary>Gets the operating-system process id that hosts the machine.</summary>
    int ProcessId { get; }

    /// <summary>Gets a value indicating whether the machine shares the shell's process.</summary>
    bool IsInProcess { get; }

    /// <summary>
    /// Gets the core's save-state controls, or null when this hosting mode
    /// cannot reach them.
    /// </summary>
    ISaveStateService? SaveStates { get; }

    /// <summary>
    /// Gets the core's pause/step controls, or null when this hosting mode (or
    /// an older core build) cannot reach them.
    /// </summary>
    ICorePlayback? Playback { get; }

    /// <summary>
    /// Gets the core's disc-change controls, or null when this hosting mode
    /// (or an older core build) cannot reach them.
    /// </summary>
    ICoreDiscs? Discs { get; }

    /// <summary>Starts the machine.</summary>
    void Start();

    /// <summary>
    /// Sets the presentation pace (1 is real time, up to 8). Returns false when
    /// this hosting mode cannot reach the running core's speed control.
    /// </summary>
    /// <param name="multiplier">Speed multiplier.</param>
    bool TrySetSpeed(int multiplier);

    /// <summary>
    /// Selects the core-side presentation filter. Returns false when this
    /// hosting mode cannot change it while the machine runs.
    /// </summary>
    /// <param name="filter">Filter key understood by the core.</param>
    bool TrySetPresentationFilter(string filter);

    /// <summary>
    /// Enables or disables the core-drawn frame-rate overlay. Returns false
    /// when this hosting mode cannot change it while the machine runs.
    /// </summary>
    /// <param name="enabled">True to draw the overlay.</param>
    bool TrySetFpsOverlay(bool enabled);

    /// <summary>
    /// Caps presented frames per second (0 is unlimited). Returns false when
    /// this hosting mode cannot change the limit while the machine runs.
    /// </summary>
    /// <param name="fps">Frame-rate limit.</param>
    bool TrySetFrameRateLimit(int fps);

    /// <summary>
    /// Enables or disables dropping missed presentation deadlines. Returns
    /// false when this hosting mode cannot change it while the machine runs.
    /// </summary>
    /// <param name="enabled">True to skip missed frames.</param>
    bool TrySetFrameSkip(bool enabled);

    /// <summary>
    /// Asks the core to write a screenshot of the current picture. Returns
    /// false when this hosting mode cannot capture the core's picture.
    /// </summary>
    bool TryRequestScreenshot();

    /// <summary>Asks the guest to power off through its own shutdown path.</summary>
    void RequestStop();

    /// <summary>Terminates a machine that ignored <see cref="RequestStop"/>.</summary>
    void Kill();

    /// <summary>Reads the tail of the core's own log.</summary>
    /// <param name="maxChars">Maximum number of characters to return.</param>
    string ReadLogTail(int maxChars);
}
