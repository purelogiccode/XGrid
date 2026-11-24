using XGrid.Core.Xemu;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// The shell side of playback control: turns one core's pause/step ABI into
/// the paused flag the toolbar, menus and status bar show. The core applies a
/// request on its own main loop, so the controller remembers the state it
/// asked for and only changes it when the core accepts the request.
/// </summary>
public sealed class PlaybackController
{
    private readonly ICorePlayback? _playback;

    /// <summary>Creates a controller over one core's playback controls.</summary>
    /// <param name="playback">Core controls, or null when the hosting mode has none.</param>
    public PlaybackController(ICorePlayback? playback)
    {
        _playback = playback;
    }

    /// <summary>Gets a value indicating whether the core can pause and step.</summary>
    public bool IsSupported => _playback is not null;

    /// <summary>Gets a value indicating whether the shell asked the core to pause.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Pauses a running machine or resumes a paused one.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    public bool Toggle(out string problem)
    {
        return SetPaused(!IsPaused, out problem);
    }

    /// <summary>Asks the core to pause or resume.</summary>
    /// <param name="paused">True to pause, false to resume.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    public bool SetPaused(bool paused, out string problem)
    {
        if (_playback is null)
        {
            problem = NeedsInProcess;
            return false;
        }

        if (!_playback.TrySetPaused(paused, out problem))
        {
            return false;
        }

        IsPaused = paused;
        return true;
    }

    /// <summary>Queues one frame while the machine is paused.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    public bool TryStepFrame(out string problem)
    {
        if (_playback is null)
        {
            problem = NeedsInProcess;
            return false;
        }

        if (!IsPaused)
        {
            problem = "Pause the emulator before stepping.";
            return false;
        }

        return _playback.TryStepFrame(out problem);
    }

    /// <summary>Forgets the paused state when the session ends.</summary>
    public void Reset()
    {
        IsPaused = false;
    }

    private const string NeedsInProcess = "Pausing needs the in-process emulator core.";
}
