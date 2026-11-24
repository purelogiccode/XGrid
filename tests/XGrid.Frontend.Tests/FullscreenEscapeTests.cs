using XGrid.Frontend.Avalonia.Views;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the fullscreen Esc sequence: pause a running game on the first press,
/// close the shell on the second, and never trap a host that cannot pause.
/// </summary>
public sealed class FullscreenEscapeTests
{
    [Fact]
    public void NoRunningGame_LeavesFullscreen()
    {
        Assert.Equal(
            FullscreenEscapeAction.LeaveFullscreen,
            FullscreenEscape.Decide(running: false, paused: false, canPause: true));
    }

    [Fact]
    public void RunningGame_PausesOnTheFirstPress()
    {
        Assert.Equal(
            FullscreenEscapeAction.PauseGame,
            FullscreenEscape.Decide(running: true, paused: false, canPause: true));
    }

    [Fact]
    public void PausedGame_ClosesOnTheSecondPress()
    {
        Assert.Equal(
            FullscreenEscapeAction.CloseApplication,
            FullscreenEscape.Decide(running: true, paused: true, canPause: true));
    }

    [Fact]
    public void RunningCoreWithoutPause_LeavesFullscreenInsteadOfClosing()
    {
        Assert.Equal(
            FullscreenEscapeAction.LeaveFullscreen,
            FullscreenEscape.Decide(running: true, paused: false, canPause: false));
    }
}
