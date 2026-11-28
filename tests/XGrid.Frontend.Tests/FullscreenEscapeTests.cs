using XGrid.Frontend.Avalonia.Views;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the Esc sequence: pause and leave fullscreen, stop and show the
/// library, then close the shell.
/// </summary>
public sealed class FullscreenEscapeTests
{
    [Fact]
    public void NoRunningGameInFullscreen_LeavesFullscreen()
    {
        Assert.Equal(
            FullscreenEscapeAction.LeaveFullscreen,
            FullscreenEscape.Decide(
                running: false,
                paused: false,
                canPause: true,
                fullscreen: true,
                library: false));
    }

    [Fact]
    public void NoRunningGameInLibrary_ClosesTheShell()
    {
        Assert.Equal(
            FullscreenEscapeAction.CloseApplication,
            FullscreenEscape.Decide(
                running: false,
                paused: false,
                canPause: true,
                fullscreen: false,
                library: true));
    }

    [Fact]
    public void NoRunningGameWindowedOutsideTheLibrary_DoesNothing()
    {
        Assert.Equal(
            FullscreenEscapeAction.None,
            FullscreenEscape.Decide(
                running: false,
                paused: false,
                canPause: true,
                fullscreen: false,
                library: false));
    }

    [Fact]
    public void RunningGame_PausesAndLeavesFullscreen()
    {
        Assert.Equal(
            FullscreenEscapeAction.PauseAndLeaveFullscreen,
            FullscreenEscape.Decide(
                running: true,
                paused: false,
                canPause: true,
                fullscreen: true,
                library: false));
    }

    [Fact]
    public void PausedGame_StopsAndShowsTheLibrary()
    {
        Assert.Equal(
            FullscreenEscapeAction.StopAndShowLibrary,
            FullscreenEscape.Decide(
                running: true,
                paused: true,
                canPause: true,
                fullscreen: false,
                library: false));
    }

    [Fact]
    public void RunningCoreWithoutPause_LeavesFullscreenFirst()
    {
        Assert.Equal(
            FullscreenEscapeAction.LeaveFullscreen,
            FullscreenEscape.Decide(
                running: true,
                paused: false,
                canPause: false,
                fullscreen: true,
                library: false));
    }

    [Fact]
    public void RunningCoreWithoutPause_Windowed_StopsAndShowsTheLibrary()
    {
        Assert.Equal(
            FullscreenEscapeAction.StopAndShowLibrary,
            FullscreenEscape.Decide(
                running: true,
                paused: false,
                canPause: false,
                fullscreen: false,
                library: false));
    }
}
