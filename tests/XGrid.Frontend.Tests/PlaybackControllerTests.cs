using System.Collections.Generic;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the shell side of pause and frame step: the controller only reports a
/// state the core accepted, refuses a step while running, and needs the
/// in-process ABI. The core applies the request on its own loop, so these
/// tests make that contract explicit without a running core.
/// </summary>
public sealed class PlaybackControllerTests
{
    [Fact]
    public void Toggle_PausesThenResumes()
    {
        var playback = new FakePlayback();
        var controller = new PlaybackController(playback);

        Assert.True(controller.IsSupported);
        Assert.False(controller.IsPaused);

        Assert.True(controller.Toggle(out string problem));
        Assert.Empty(problem);
        Assert.True(controller.IsPaused);
        Assert.Equal([true], playback.PauseRequests);

        Assert.True(controller.Toggle(out problem));
        Assert.Empty(problem);
        Assert.False(controller.IsPaused);
        Assert.Equal([true, false], playback.PauseRequests);
    }

    [Fact]
    public void Toggle_KeepsThePreviousStateWhenTheCoreRefuses()
    {
        var playback = new FakePlayback { Accept = false, Refusal = "The emulator is not running." };
        var controller = new PlaybackController(playback);

        Assert.False(controller.Toggle(out string problem));
        Assert.Equal("The emulator is not running.", problem);
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public void Toggle_WithoutACoreReportsTheHostingRequirement()
    {
        var controller = new PlaybackController(null);

        Assert.False(controller.IsSupported);
        Assert.False(controller.Toggle(out string problem));
        Assert.Equal("Pausing needs the in-process emulator core.", problem);
        Assert.False(controller.IsPaused);
    }

    [Fact]
    public void StepFrame_RequiresAPausedMachine()
    {
        var playback = new FakePlayback();
        var controller = new PlaybackController(playback);

        Assert.False(controller.TryStepFrame(out string problem));
        Assert.Equal("Pause the emulator before stepping.", problem);
        Assert.Equal(0, playback.Steps);

        Assert.True(controller.SetPaused(true, out _));
        Assert.True(controller.TryStepFrame(out problem));
        Assert.Empty(problem);
        Assert.Equal(1, playback.Steps);
    }

    [Fact]
    public void Reset_ForgetsThePausedState()
    {
        var playback = new FakePlayback();
        var controller = new PlaybackController(playback);

        Assert.True(controller.SetPaused(true, out _));
        controller.Reset();

        Assert.False(controller.IsPaused);
    }

    private sealed class FakePlayback : ICorePlayback
    {
        public bool Accept { get; set; } = true;

        public string Refusal { get; set; } = "refused";

        public List<bool> PauseRequests { get; } = [];

        public int Steps { get; private set; }

        public bool TrySetPaused(bool paused, out string problem)
        {
            PauseRequests.Add(paused);
            problem = Accept ? string.Empty : Refusal;
            return Accept;
        }

        public bool TryStepFrame(out string problem)
        {
            Steps++;
            problem = Accept ? string.Empty : Refusal;
            return Accept;
        }
    }
}
