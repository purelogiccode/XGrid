using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins the power/session decisions independently of the window.</summary>
public sealed class SessionPolicyTests
{
    [Fact]
    public void PowerOnAtStartupHonorsTheSettingOnlyWithoutAutostartOrFirstRun()
    {
        var settings = new EmulatorSettings { PowerOnAtStartup = true };

        Assert.True(SessionPolicy.ShouldPowerOnAtStartup(settings, false, false, false));
        Assert.False(SessionPolicy.ShouldPowerOnAtStartup(settings, true, false, false));
        Assert.False(SessionPolicy.ShouldPowerOnAtStartup(settings, false, true, false));
        Assert.False(SessionPolicy.ShouldPowerOnAtStartup(settings, false, false, true));
        Assert.False(SessionPolicy.ShouldPowerOnAtStartup(
            new EmulatorSettings { PowerOnAtStartup = false }, false, false, false));
    }

    [Fact]
    public void SaveBeforeStopNeedsTheSettingSupportAndARunningConsole()
    {
        var settings = new EmulatorSettings { SaveStateOnExit = true };

        Assert.True(SessionPolicy.ShouldSaveBeforeStop(settings, true, true));
        Assert.False(SessionPolicy.ShouldSaveBeforeStop(settings, false, true));
        Assert.False(SessionPolicy.ShouldSaveBeforeStop(settings, true, false));
        Assert.False(SessionPolicy.ShouldSaveBeforeStop(
            new EmulatorSettings { SaveStateOnExit = false }, true, true));
    }

    [Fact]
    public void CloseAfterStopKeepsAFaultVisible()
    {
        Assert.True(SessionPolicy.ShouldCloseAfterStop(
            new EmulatorSettings { StayOpenAfterPowerOff = false }, faulted: false));
        Assert.False(SessionPolicy.ShouldCloseAfterStop(
            new EmulatorSettings { StayOpenAfterPowerOff = false }, faulted: true));
        Assert.False(SessionPolicy.ShouldCloseAfterStop(new EmulatorSettings(), faulted: false));
    }

    [Fact]
    public void ResumeAfterStartSkipsALibraryLaunchAndNeedsSaveStates()
    {
        var settings = new EmulatorSettings { ResumeStateOnStart = true };

        Assert.True(SessionPolicy.ShouldResumeAfterStart(settings, hasGame: false, saveStatesSupported: true));
        Assert.False(SessionPolicy.ShouldResumeAfterStart(settings, hasGame: true, saveStatesSupported: true));
        Assert.False(SessionPolicy.ShouldResumeAfterStart(settings, hasGame: false, saveStatesSupported: false));
        Assert.False(SessionPolicy.ShouldResumeAfterStart(
            new EmulatorSettings { ResumeStateOnStart = false }, false, true));
    }

    [Fact]
    public void PoweredOffScreenShowsOnlyWhileStoppedAndEnabled()
    {
        Assert.True(SessionPolicy.ShouldShowPoweredOffScreen(new EmulatorSettings(), isRunning: false));
        Assert.False(SessionPolicy.ShouldShowPoweredOffScreen(new EmulatorSettings(), isRunning: true));
        Assert.False(SessionPolicy.ShouldShowPoweredOffScreen(
            new EmulatorSettings { ShowPoweredOffScreen = false }, isRunning: false));
    }
}
