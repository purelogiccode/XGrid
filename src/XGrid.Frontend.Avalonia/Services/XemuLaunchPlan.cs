using System;
using System.IO;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Maps shell settings to a core launch. This is the only place that knows both
/// vocabularies (the shell's settings model and the core's launch settings), so
/// it stays pure and testable: no process, no window, no file writes.
/// </summary>
public static class XemuLaunchPlan
{
    /// <summary>
    /// Builds the launch settings for a title. Firmware the user did not
    /// configure falls back to the firmware directory beside the core (the
    /// development layout ships the MCPX ROM, flash ROM, EEPROM and HDD there).
    /// </summary>
    /// <param name="settings">Shell settings.</param>
    /// <param name="discPath">Disc to insert, or null to use the configured disc.</param>
    /// <param name="width">Display width in physical pixels.</param>
    /// <param name="height">Display height in physical pixels.</param>
    /// <param name="workingDirectory">Directory for the generated config and the core log.</param>
    public static XemuLaunchSettings Create(
        EmulatorSettings settings,
        string? discPath,
        int width,
        int height,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string? core = XemuPaths.FindCore(settings.XemuCorePath);
        string? firmwareDirectory = XemuPaths.FindFirmwareDirectory(core);

        string dvd = discPath ?? settings.DvdPath;

        return new XemuLaunchSettings
        {
            CorePath = core ?? string.Empty,
            BootRomPath = Resolve(settings.McpxRomPath, firmwareDirectory, "mcpx_1.0.bin"),
            FlashRomPath = settings.BiosPath,
            EepromPath = Resolve(settings.EepromPath, firmwareDirectory, "eeprom.bin"),
            HddPath = Resolve(settings.HddPath, firmwareDirectory, "xbox_hdd.qcow2"),
            DvdPath = dvd ?? string.Empty,
            RamMb = settings.RamMb,
            // The display control publishes 1x1 before its first layout pass.
            WindowWidth = Math.Max(64, width),
            WindowHeight = Math.Max(64, height),
            DisplaySinkName = settings.DisplaySink ? XemuDisplaySink.CreateName() : string.Empty,
            InputSinkName = settings.DisplaySink ? XemuInputSink.CreateName() : string.Empty,
            WorkingDirectory = workingDirectory,
        };
    }

    /// <summary>
    /// Returns a human-readable reason when the core cannot be launched for
    /// these settings, or null when the launch is expected to work.
    /// </summary>
    /// <param name="settings">Shell settings.</param>
    /// <param name="discPath">Disc to insert, or null.</param>
    /// <param name="workingDirectory">Core working directory.</param>
    public static string? DescribeProblem(EmulatorSettings settings, string? discPath, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (XemuPaths.FindCore(settings.XemuCorePath) is null)
        {
            return "The xemu core executable was not found. Set the core path in Settings.";
        }

        if (!settings.IsBiosReady)
        {
            return "Choose a BIOS image first.";
        }

        XemuLaunchSettings launch = Create(settings, discPath, 640, 480, workingDirectory);
        System.Collections.Generic.IReadOnlyList<string> errors = launch.Validate();
        return errors.Count == 0 ? null : errors[0];
    }

    private static string Resolve(string configured, string? firmwareDirectory, string fileName)
    {
        // An empty setting means "use what ships beside the core"; a setting the
        // user typed is authoritative, so a missing file is reported rather than
        // silently replaced by a different image.
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (firmwareDirectory is null)
        {
            return configured;
        }

        string candidate = Path.Combine(firmwareDirectory, fileName);
        return File.Exists(candidate) ? candidate : configured;
    }
}
