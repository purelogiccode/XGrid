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

        // The shell-owned frame and pad sections are a Windows integration. On
        // other hosts the core keeps its SDL window and reads SDL input, so a
        // configured sink must not be advertised.
        bool hostSinks = settings.DisplaySink && XemuDisplaySink.IsSupported;

        return new XemuLaunchSettings
        {
            CorePath = core ?? string.Empty,
            HostMode = settings.CoreHosting,
            BootRomPath = Resolve(settings.McpxRomPath, firmwareDirectory, "mcpx_1.0.bin"),
            FlashRomPath = settings.BiosPath,
            EepromPath = Resolve(settings.EepromPath, firmwareDirectory, "eeprom.bin"),
            HddPath = Resolve(settings.HddPath, firmwareDirectory, "xbox_hdd.qcow2"),
            DvdPath = dvd,
            RamMb = settings.RamMb,
            // The display control publishes 1x1 before its first layout pass.
            WindowWidth = Math.Max(64, width),
            WindowHeight = Math.Max(64, height),
            Renderer = ResolveRenderer(settings.Graphics.Renderer, CoreCapabilities.Read(core)),
            SurfaceScale = settings.Graphics.SurfaceScale,
            Filtering = settings.Graphics.Filtering,
            StartupSize = settings.Graphics.StartupSize,
            VSync = settings.Graphics.VSync,
            FullscreenOnStartup = settings.Graphics.FullscreenOnStartup,
            FullscreenExclusive = settings.Graphics.FullscreenExclusive,
            AspectRatio = settings.Graphics.AspectRatio,
            Fit = settings.Graphics.Fit,
            AvPack = settings.Graphics.AvPack,
            SkipBootAnimation = false,
            ShowMenuBar = false,
            AudioDsp = settings.Audio.UseDsp,
            AudioDspJit = settings.Audio.UseDspJit,
            Hrtf = settings.Audio.Hrtf,
            VolumeLimit = settings.Audio.VolumeLimit,
            VoiceWorkers = settings.Audio.VoiceWorkers,
            AudioBackend = settings.Audio.Backend,
            AudioWavPath = string.Equals(settings.Audio.Backend, AudioSettings.BackendWav, StringComparison.Ordinal)
                ? Path.Combine(workingDirectory, "audio.wav")
                : string.Empty,
            DisplaySinkName = hostSinks ? XemuDisplaySink.CreateName() : string.Empty,
            InputSinkName = hostSinks ? XemuInputSink.CreateName() : string.Empty,
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
            return "The emulator binary was not found. Set its path in Settings.";
        }

        if (!settings.IsBiosReady)
        {
            return "Choose a BIOS image first.";
        }

        // A .zar that stores the game as a file tree cannot be mounted: the
        // guest needs an XDVDFS volume and the synthesizer is not built yet.
        string disc = discPath ?? settings.DvdPath;
        if (!string.IsNullOrWhiteSpace(disc)
            && disc.EndsWith(".zar", StringComparison.OrdinalIgnoreCase)
            && ZarArchive.TryOpen(disc, out ZarArchive? archive)
            && archive is not null
            && !archive.HasDiscImage)
        {
            return "This .zar stores the game as files rather than one ISO entry, "
                   + "so it needs the disc synthesizer (not built yet). Repack it "
                   + "with a folder that contains the .iso.";
        }

        XemuLaunchSettings launch = Create(settings, discPath, 640, 480, workingDirectory);
        System.Collections.Generic.IReadOnlyList<string> errors = launch.Validate();
        return errors.Count == 0 ? null : errors[0];
    }

    /// <summary>
    /// Returns the renderer the core will actually start. A build that reports
    /// no Vulkan cannot start one, so a configured Vulkan renderer becomes
    /// OpenGL here rather than in the core's log after the window is up.
    /// </summary>
    /// <param name="renderer">Configured renderer key.</param>
    /// <param name="capabilities">Core capabilities, or null when unknown.</param>
    /// <returns>The renderer key to write into the core configuration.</returns>
    public static string ResolveRenderer(string renderer, CoreCapabilities? capabilities)
    {
        if (string.Equals(renderer, GraphicsSettings.RendererVulkan, StringComparison.OrdinalIgnoreCase)
            && capabilities is { SupportsVulkan: false })
        {
            return GraphicsSettings.RendererOpenGl;
        }

        return renderer;
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
