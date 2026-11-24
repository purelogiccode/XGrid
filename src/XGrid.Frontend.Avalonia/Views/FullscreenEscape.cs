namespace XGrid.Frontend.Avalonia.Views;

/// <summary>What Esc does while the shell is fullscreen.</summary>
public enum FullscreenEscapeAction
{
    /// <summary>No game owns the screen; return to a normal window.</summary>
    LeaveFullscreen,

    /// <summary>A running game is paused; a second press closes the shell.</summary>
    PauseGame,

    /// <summary>The game is already paused, so close the shell.</summary>
    CloseApplication,
}

/// <summary>
/// Pins the fullscreen Esc sequence: the first press pauses a running game and
/// the second closes XGrid. A console that cannot pause, or no running game,
/// falls back to leaving fullscreen so Esc never strands the user.
/// </summary>
public static class FullscreenEscape
{
    /// <summary>Chooses the action for one Esc press.</summary>
    /// <param name="running">True while the core is running.</param>
    /// <param name="paused">True while the shell has the core paused.</param>
    /// <param name="canPause">True when the running core supports pause.</param>
    /// <returns>The action to perform.</returns>
    public static FullscreenEscapeAction Decide(bool running, bool paused, bool canPause)
    {
        if (!running)
        {
            return FullscreenEscapeAction.LeaveFullscreen;
        }

        if (paused)
        {
            return FullscreenEscapeAction.CloseApplication;
        }

        return canPause
            ? FullscreenEscapeAction.PauseGame
            : FullscreenEscapeAction.LeaveFullscreen;
    }
}
