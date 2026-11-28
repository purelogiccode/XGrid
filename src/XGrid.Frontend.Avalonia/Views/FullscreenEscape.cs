namespace XGrid.Frontend.Avalonia.Views;

/// <summary>What Esc does on the main window.</summary>
public enum FullscreenEscapeAction
{
    /// <summary>Nothing in this state.</summary>
    None,

    /// <summary>No game owns the screen; return to a normal window.</summary>
    LeaveFullscreen,

    /// <summary>Pause the running game and return to a normal window.</summary>
    PauseAndLeaveFullscreen,

    /// <summary>Stop the paused game and show the library.</summary>
    StopAndShowLibrary,

    /// <summary>The game is stopped and the library is showing; close the shell.</summary>
    CloseApplication,
}

/// <summary>
/// Pins the Esc sequence: the first press pauses a running game and leaves
/// fullscreen, the second stops it and shows the library, and the third closes
/// XGrid. A console that cannot pause falls back to leaving fullscreen first.
/// </summary>
public static class FullscreenEscape
{
    /// <summary>Chooses the action for one Esc press.</summary>
    /// <param name="running">True while the core is running.</param>
    /// <param name="paused">True while the shell has the core paused.</param>
    /// <param name="canPause">True when the running core supports pause.</param>
    /// <param name="fullscreen">True while the shell window is fullscreen.</param>
    /// <param name="library">True while the library page is showing.</param>
    /// <returns>The action to perform.</returns>
    public static FullscreenEscapeAction Decide(
        bool running,
        bool paused,
        bool canPause,
        bool fullscreen,
        bool library)
    {
        if (!running)
        {
            if (library)
            {
                return FullscreenEscapeAction.CloseApplication;
            }

            return fullscreen
                ? FullscreenEscapeAction.LeaveFullscreen
                : FullscreenEscapeAction.None;
        }

        if (paused)
        {
            return FullscreenEscapeAction.StopAndShowLibrary;
        }

        if (canPause)
        {
            return FullscreenEscapeAction.PauseAndLeaveFullscreen;
        }

        // A core without pause still gets a way out: leave fullscreen first,
        // then the next press stops the console.
        return fullscreen
            ? FullscreenEscapeAction.LeaveFullscreen
            : FullscreenEscapeAction.StopAndShowLibrary;
    }
}
