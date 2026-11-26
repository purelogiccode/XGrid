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
        bool chihiro = settings.Chihiro.Enabled || IsChihiroImage(dvd);
        string chihiroDirectory = settings.Chihiro.ResolveDirectory(settings.BiosPath);
        if (string.IsNullOrWhiteSpace(chihiroDirectory) && firmwareDirectory is not null)
        {
            chihiroDirectory = firmwareDirectory;
        }

        // The core owns the picture. The shell only selects the filter and the
        // overlay; it never receives or presents frames. The pad section is a
        // Windows integration used in both presentation modes.
        bool hostInput = XemuInputSink.IsSupported;

        return new XemuLaunchSettings
        {
            CorePath = core ?? string.Empty,
            HostMode = settings.CoreHosting,
            Machine = chihiro ? XemuLaunchSettings.MachineChihiro : XemuLaunchSettings.MachineXbox,
            BootRomPath = Resolve(settings.McpxRomPath, firmwareDirectory, "mcpx_1.0.bin"),
            FlashRomPath = chihiro
                ? ResolveChihiro(chihiroDirectory, ChihiroSettings.BiosFileName, firmwareDirectory)
                : settings.BiosPath,
            EepromPath = Resolve(settings.EepromPath, firmwareDirectory, "eeprom.bin"),
            HddPath = chihiro
                ? string.Empty
                : Resolve(settings.HddPath, firmwareDirectory, "xbox_hdd.qcow2"),
            DvdPath = dvd,
            ChihiroBiosPath = chihiro
                ? ResolveChihiro(chihiroDirectory, ChihiroSettings.BiosFileName, firmwareDirectory)
                : string.Empty,
            ChihiroMediaboardPath = chihiro
                ? ResolveChihiro(chihiroDirectory, ChihiroSettings.MediaboardFileName, firmwareDirectory)
                : string.Empty,
            ChihiroIc10Path = chihiro
                ? ResolveChihiro(chihiroDirectory, ChihiroSettings.Ic10FileName, firmwareDirectory)
                : string.Empty,
            ChihiroIc11Path = chihiro
                ? ResolveChihiro(chihiroDirectory, ChihiroSettings.Ic11FileName, firmwareDirectory)
                : string.Empty,
            ChihiroPc20Path = chihiro
                ? ResolveChihiro(chihiroDirectory, ChihiroSettings.Pc20FileName, firmwareDirectory)
                : string.Empty,
            RamMb = chihiro ? 128 : settings.RamMb,
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
            AudioDevice = settings.Audio.Device,
            AudioWavPath = string.Equals(settings.Audio.Backend, AudioSettings.BackendWav, StringComparison.Ordinal)
                ? Path.Combine(workingDirectory, "audio.wav")
                : string.Empty,
            DisplaySinkName = string.Empty,
            InputSinkName = hostInput ? XemuInputSink.CreateName() : string.Empty,
            PresentationFilter = XemuPresentationFilter.Normalize(settings.Filter),
            FpsOverlay = settings.ShowFpsOverlay,
            WorkingDirectory = workingDirectory,
            // The core writes in-process captures; the shell only lists them.
            ScreenshotDirectory = Path.Combine(
                Path.GetDirectoryName(workingDirectory) ?? workingDirectory,
                ScreenshotNaming.FolderName),
            ShaderCacheDirectory = Path.Combine(workingDirectory, "d3d11-shader-cache"),
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

        bool chihiro = settings.Chihiro.Enabled || IsChihiroImage(discPath ?? settings.DvdPath);
        if (!chihiro && !settings.IsBiosReady)
        {
            return "Choose a BIOS image first.";
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
        if (capabilities is null || capabilities.Has(renderer))
        {
            return renderer;
        }

        // A renderer the core did not compile cannot start. Vulkan and
        // Direct3D 11 both fall back to OpenGL rather than to the null device.
        if (string.Equals(renderer, GraphicsSettings.RendererVulkan, StringComparison.OrdinalIgnoreCase)
            || string.Equals(renderer, GraphicsSettings.RendererDirect3D11, StringComparison.OrdinalIgnoreCase))
        {
            return GraphicsSettings.RendererOpenGl;
        }

        return renderer;
    }

    /// <summary>
    /// Returns true when a path is a Chihiro netboot image. These are raw FATX
    /// images, not Xbox XISOs, and the extension is the only cheap signal.
    /// </summary>
    /// <param name="path">Image path, or null.</param>
    /// <returns>True for a <c>.bin</c> image.</returns>
    public static bool IsChihiroImage(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && Path.GetExtension(path).Equals(".bin", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveChihiro(string directory, string fileName, string? firmwareDirectory)
    {
        string resolved = string.IsNullOrWhiteSpace(directory) ? firmwareDirectory ?? string.Empty : directory;
        return string.IsNullOrWhiteSpace(resolved) ? string.Empty : Path.Combine(resolved, fileName);
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
