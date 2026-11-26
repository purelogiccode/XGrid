using System;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Pure decisions for the shell's power and session lifecycle. Keeping them out
/// of the window view model makes the edge cases testable: command-line
/// autostart wins over the saved setting, a first run asks for firmware before
/// powering on, and a fault stays visible instead of closing the window.
/// </summary>
public static class SessionPolicy
{
    /// <summary>
    /// Returns true when the saved setting should power the console on after
    /// the window is ready. A command-line autostart already did the work, and
    /// the first-run wizard must finish first.
    /// </summary>
    public static bool ShouldPowerOnAtStartup(
        EmulatorSettings settings,
        bool commandLineAutostart,
        bool firstRunPending,
        bool isRunning)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.PowerOnAtStartup
               && !commandLineAutostart
               && !firstRunPending
               && !isRunning;
    }

    /// <summary>Returns true when stopping should save the session snapshot first.</summary>
    public static bool ShouldSaveBeforeStop(
        EmulatorSettings settings,
        bool saveStatesSupported,
        bool isRunning)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.SaveStateOnExit && saveStatesSupported && isRunning;
    }

    /// <summary>
    /// Returns true when the shell should close after a requested stop or a
    /// normal guest shutdown. A faulted core stays open so the error is visible.
    /// </summary>
    public static bool ShouldCloseAfterStop(EmulatorSettings settings, bool faulted)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !settings.StayOpenAfterPowerOff && !faulted;
    }

    /// <summary>
    /// Returns true when power-on should restore the session snapshot. Launching
    /// a specific game starts it fresh; only the Power button/dashboard resumes.
    /// </summary>
    public static bool ShouldResumeAfterStart(
        EmulatorSettings settings,
        bool hasGame,
        bool saveStatesSupported)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ResumeStateOnStart && saveStatesSupported && !hasGame;
    }

    /// <summary>Returns true when the console page should show the powered-off panel.</summary>
    public static bool ShouldShowPoweredOffScreen(EmulatorSettings settings, bool isRunning)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ShowPoweredOffScreen && !isRunning;
    }
}
