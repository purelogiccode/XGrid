using System;
using System.Collections.Generic;

namespace XGrid.Core.Xemu;

/// <summary>
/// Everything the xemu core needs to boot one console: the firmware images,
/// the storage media, the RAM size and the display surface geometry.
/// </summary>
/// <remarks>
/// The launcher writes these values into a generated <c>xemu.toml</c> so the
/// core never reads a user-edited configuration file, and so two launches can
/// never inherit each other's state. Paths must be absolute: the core resolves
/// relative paths against its own working directory, which the launcher owns.
/// </remarks>
public sealed record XemuLaunchSettings
{
    /// <summary>Gets the path to the xemu executable that provides the core.</summary>
    public string CorePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the hosting mode: <c>in-process</c> loads <c>xgrid_core.dll</c>
    /// beside the executable, <c>process</c> starts the executable.
    /// </summary>
    public string HostMode { get; init; } = "in-process";

    /// <summary>Gets the MCPX boot ROM image (512 bytes, <c>mcpx_1.0.bin</c>).</summary>
    public string BootRomPath { get; init; } = string.Empty;

    /// <summary>Gets the flash ROM image (256 KB, <c>Complex_4627.bin</c>).</summary>
    public string FlashRomPath { get; init; } = string.Empty;

    /// <summary>Gets the persistent EEPROM image (256 bytes).</summary>
    public string EepromPath { get; init; } = string.Empty;

    /// <summary>Gets the hard-disk image (raw or qcow2).</summary>
    public string HddPath { get; init; } = string.Empty;

    /// <summary>Gets the disc image inserted at power-on (ISO/XISO or CHD).</summary>
    public string DvdPath { get; init; } = string.Empty;

    /// <summary>Gets the machine: <c>xbox</c> or <c>chihiro</c>.</summary>
    public string Machine { get; init; } = MachineXbox;

    /// <summary>The Xbox machine key.</summary>
    public const string MachineXbox = "xbox";

    /// <summary>The Sega Chihiro machine key.</summary>
    public const string MachineChihiro = "chihiro";

    /// <summary>Gets a value indicating whether this launch boots a Chihiro.</summary>
    public bool IsChihiro => string.Equals(Machine, MachineChihiro, StringComparison.Ordinal);

    /// <summary>Gets the Chihiro flash ROM image (<c>chihiro_xbox_bios.bin</c>).</summary>
    public string ChihiroBiosPath { get; init; } = string.Empty;

    /// <summary>Gets the Chihiro media-board flash image.</summary>
    public string ChihiroMediaboardPath { get; init; } = string.Empty;

    /// <summary>Gets the Chihiro QC EEPROM (<c>ic10_g24lc64.bin</c>).</summary>
    public string ChihiroIc10Path { get; init; } = string.Empty;

    /// <summary>Gets the Chihiro baseboard EEPROM (<c>ic11_24lc024.bin</c>).</summary>
    public string ChihiroIc11Path { get; init; } = string.Empty;

    /// <summary>Gets the Chihiro SC EEPROM (<c>pc20_g24lc64.bin</c>).</summary>
    public string ChihiroPc20Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional Chihiro network-board firmware (<c>ver1305.bin</c>).
    /// Network titles such as MJ2 and MJ3 check its version.
    /// </summary>
    public string ChihiroNetFirmwarePath { get; init; } = string.Empty;

    /// <summary>Gets the qcow2 file that holds the Chihiro snapshots.</summary>
    public string ChihiroSnapshotStorePath { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the cabinet is on free play.</summary>
    public bool ChihiroFreePlay { get; init; }

    /// <summary>Gets the cabinet region (<c>auto</c>, <c>jp</c>, <c>us</c>, <c>ex</c>).</summary>
    public string ChihiroRegion { get; init; } = "ex";

    /// <summary>Gets the DIMM size (<c>auto</c>, <c>128</c>, <c>256</c>, <c>512</c>, <c>1024</c>).</summary>
    public string ChihiroDimmSize { get; init; } = "auto";

    /// <summary>Gets the media-board type (<c>auto</c>, <c>type1</c>, <c>type3</c>).</summary>
    public string ChihiroBoardType { get; init; } = "auto";

    /// <summary>Gets a value indicating whether drive-board force feedback is sent to a gamepad.</summary>
    public bool ChihiroForceFeedback { get; init; }

    /// <summary>Gets the force-feedback strength, 0 to 200 percent.</summary>
    public int ChihiroFfbStrength { get; init; } = 100;

    /// <summary>Gets a value indicating whether the wheel's force direction is inverted.</summary>
    public bool ChihiroFfbInvert { get; init; }

    /// <summary>Gets the wheel's damping share, 0 to 100 percent.</summary>
    public int ChihiroWheelWeight { get; init; } = 40;

    /// <summary>Gets a value indicating whether the wheel centers itself.</summary>
    public bool ChihiroWheelAutocenter { get; init; } = true;

    /// <summary>Gets the self-centering strength, 0 to 100 percent.</summary>
    public int ChihiroWheelAutocenterStrength { get; init; } = 50;

    /// <summary>Gets a value indicating whether pointer devices aim separately.</summary>
    public bool ChihiroPointerDevices { get; init; }

    /// <summary>Gets the Chihiro cabinet key and mouse bindings.</summary>
    public ChihiroJvsSettings ChihiroJvs { get; init; } = new();

    /// <summary>Gets the player-2 cabinet key, mouse and aim bindings.</summary>
    public ChihiroJvsP2Settings ChihiroJvsP2 { get; init; } = new();

    /// <summary>Gets the card-reader files and the reader enable switch.</summary>
    public ChihiroCardReaderSettings ChihiroCardReader { get; init; } = new();

    /// <summary>Gets a value indicating whether the linked-cabinet relay runs.</summary>
    public bool ChihiroLinkEnabled { get; init; }

    /// <summary>Gets the number of linked cabinets (2 to 4).</summary>
    public int ChihiroLinkCabinets { get; init; } = 2;

    /// <summary>Gets this cabinet's number (1 to <see cref="ChihiroLinkCabinets"/>).</summary>
    public int ChihiroLinkCabinet { get; init; } = 1;

    /// <summary>Gets the host cabinet's address; empty on cabinet 1.</summary>
    public string ChihiroLinkHost { get; init; } = string.Empty;

    /// <summary>Gets the relay's base TCP port.</summary>
    public int ChihiroLinkPort { get; init; } = 9100;

    /// <summary>Gets the guest RAM size in megabytes (64 retail, 128 debug kit).</summary>
    public int RamMb { get; init; } = 64;

    /// <summary>Gets the initial window width in physical pixels.</summary>
    public int WindowWidth { get; init; } = 640;

    /// <summary>Gets the initial window height in physical pixels.</summary>
    public int WindowHeight { get; init; } = 480;

    /// <summary>
    /// Gets a value indicating whether the core waits for vertical sync.
    /// Disabled by default: the shell owns presentation pacing and a blocked
    /// swap is lost guest time.
    /// </summary>
    public bool VSync { get; init; }

    /// <summary>Gets the core renderer: <c>OPENGL</c>, <c>VULKAN</c> or <c>NULL</c>.</summary>
    public string Renderer { get; init; } = "OPENGL";

    /// <summary>Gets the internal surface scale (1-8).</summary>
    public int SurfaceScale { get; init; } = 1;

    /// <summary>Gets the core texture filtering: <c>linear</c> or <c>nearest</c>.</summary>
    public string Filtering { get; init; } = "linear";

    /// <summary>Gets the core window startup-size key.</summary>
    public string StartupSize { get; init; } = "1280x960";

    /// <summary>Gets a value indicating whether the core starts fullscreen.</summary>
    public bool FullscreenOnStartup { get; init; }

    /// <summary>Gets a value indicating whether fullscreen is exclusive.</summary>
    public bool FullscreenExclusive { get; init; }

    /// <summary>Gets the aspect-ratio mode: <c>native</c>, <c>auto</c>, <c>4x3</c> or <c>16x9</c>.</summary>
    public string AspectRatio { get; init; } = "auto";

    /// <summary>Gets the fit mode: <c>center</c>, <c>scale</c> or <c>stretch</c>.</summary>
    public string Fit { get; init; } = "scale";

    /// <summary>Gets a value indicating whether the APU DSP (guest audio microcode) runs.</summary>
    public bool AudioDsp { get; init; } = true;

    /// <summary>Gets a value indicating whether the DSP JIT is used.</summary>
    public bool AudioDspJit { get; init; }

    /// <summary>Gets a value indicating whether HRTF filtering is applied.</summary>
    public bool Hrtf { get; init; } = true;

    /// <summary>Gets the output volume limit (0-1).</summary>
    public double VolumeLimit { get; init; } = 1.0;

    /// <summary>Gets the voice-processor worker count (0 = auto).</summary>
    public int VoiceWorkers { get; init; }

    /// <summary>Gets the host audio backend key the core registered.</summary>
    public string AudioBackend { get; init; } = AudioBackends.Auto;

    /// <summary>
    /// Gets the backend-specific output device. DirectSound uses a device GUID;
    /// PulseAudio, PipeWire, ALSA, OSS, sndio and JACK use their own device or
    /// server name. Empty follows the system default.
    /// </summary>
    public string AudioDevice { get; init; } = string.Empty;

    /// <summary>Gets the WAV capture path when <see cref="AudioBackend"/> is <c>wav</c>.</summary>
    public string AudioWavPath { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the boot animation is skipped.</summary>
    public bool SkipBootAnimation { get; init; }

    /// <summary>
    /// Gets the AV pack the core reports: <c>hdtv</c>, <c>scart</c>, <c>vga</c>,
    /// <c>rfu</c>, <c>svideo</c>, <c>composite</c> or <c>none</c>.
    /// </summary>
    public string AvPack { get; init; } = "hdtv";

    /// <summary>
    /// Gets a value indicating whether the core's own menu bar is visible.
    /// Hidden by default: the shell is the menu, and an embedded core must not
    /// draw chrome inside the display rectangle.
    /// </summary>
    public bool ShowMenuBar { get; init; }

    /// <summary>
    /// Gets the shared-section name the core should publish frames into, or an
    /// empty string to let the core show its own window.
    /// </summary>
    public string DisplaySinkName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the shared-section name the core should read port 1 from, or an
    /// empty string to use SDL input.
    /// </summary>
    public string InputSinkName { get; init; } = string.Empty;


    /// <summary>Gets the directory that holds the generated config and the core log.</summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Gets the directory the core writes in-process screenshots to. Empty
    /// leaves the core's own default (its working directory).
    /// </summary>
    public string ScreenshotDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Gets the directory the D3D11 backend keeps compiled shader blobs in.
    /// Empty leaves the core's own default (its working directory).
    /// </summary>
    public string ShaderCacheDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Gets the presentation filter the core applies in its final blit. The
    /// shell selects it; it never processes picture pixels.
    /// </summary>
    public string PresentationFilter { get; init; } = XemuPresentationFilter.Nearest;

    /// <summary>Gets a value indicating whether the core draws the frame-rate overlay.</summary>
    public bool FpsOverlay { get; init; }

    private static readonly HashSet<string> StartupSizes = new(StringComparer.Ordinal)
    {
        "last_used",
        "640x480",
        "720x480",
        "1280x720",
        "1280x800",
        "1280x960",
        "1920x1080",
        "2560x1440",
        "2560x1600",
        "2560x1920",
        "3840x2160",
    };

    /// <summary>
    /// Returns the configuration problems that would stop the core from
    /// booting, as human-readable messages. Empty means valid.
    /// </summary>
    /// <returns>Validation messages, one per problem.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        RequireFile(errors, "core executable", CorePath);
        if (IsChihiro)
        {
            RequireFile(errors, "Chihiro BIOS", ChihiroBiosPath);
            RequireFile(errors, "media-board flash", ChihiroMediaboardPath);
            RequireFile(errors, "QC EEPROM (ic10)", ChihiroIc10Path);
            RequireFile(errors, "baseboard EEPROM (ic11)", ChihiroIc11Path);
            RequireFile(errors, "SC EEPROM (pc20)", ChihiroPc20Path);
            if (string.IsNullOrWhiteSpace(DvdPath))
            {
                errors.Add("The Chihiro netboot image (.bin) is not set.");
            }
            else if (!System.IO.File.Exists(DvdPath))
            {
                errors.Add($"The Chihiro netboot image does not exist: {DvdPath}");
            }

            if (ChihiroLinkEnabled)
            {
                if (ChihiroLinkCabinets is < 2 or > 4)
                {
                    errors.Add($"The cabinet link needs 2 to 4 cabinets, not {ChihiroLinkCabinets}.");
                }

                if (ChihiroLinkCabinet < 1 || ChihiroLinkCabinet > ChihiroLinkCabinets)
                {
                    errors.Add($"Cabinet {ChihiroLinkCabinet} is outside 1..{ChihiroLinkCabinets}.");
                }

                if (ChihiroLinkPort is < 1 or > 65531)
                {
                    errors.Add($"The cabinet link port must be 1 to 65531, not {ChihiroLinkPort}.");
                }

                if (ChihiroLinkCabinet > 1 && string.IsNullOrWhiteSpace(ChihiroLinkHost))
                {
                    errors.Add("The cabinet link needs the host cabinet's address.");
                }
            }

            if (ChihiroRegion is not ("auto" or "jp" or "us" or "ex"))
            {
                errors.Add($"Unknown Chihiro region '{ChihiroRegion}'.");
            }

            if (ChihiroDimmSize is not ("auto" or "128" or "256" or "512" or "1024"))
            {
                errors.Add($"Unknown Chihiro DIMM size '{ChihiroDimmSize}'.");
            }

            if (ChihiroBoardType is not ("auto" or "type1" or "type3"))
            {
                errors.Add($"Unknown Chihiro media-board type '{ChihiroBoardType}'.");
            }

            if (ChihiroFfbStrength is < 0 or > 200)
            {
                errors.Add($"Chihiro force feedback must be 0 to 200%, not {ChihiroFfbStrength}%.");
            }

            if (ChihiroWheelWeight is < 0 or > 100)
            {
                errors.Add($"Chihiro wheel weight must be 0 to 100%, not {ChihiroWheelWeight}%.");
            }

            if (ChihiroWheelAutocenterStrength is < 0 or > 100)
            {
                errors.Add($"Chihiro wheel autocenter must be 0 to 100%, not {ChihiroWheelAutocenterStrength}%.");
            }

            if (ChihiroJvsP2.AimSpeed is < 0 or > 400)
            {
                errors.Add($"Player 2's aim speed must be 0 to 400%, not {ChihiroJvsP2.AimSpeed}%.");
            }
        }
        else
        {
            RequireFile(errors, "MCPX boot ROM", BootRomPath);
            RequireFile(errors, "flash ROM", FlashRomPath);
            RequireFile(errors, "EEPROM image", EepromPath);
            RequireFile(errors, "hard-disk image", HddPath);

            if (!string.IsNullOrWhiteSpace(DvdPath) && !System.IO.File.Exists(DvdPath))
            {
                errors.Add($"The disc image does not exist: {DvdPath}");
            }
        }

        if (RamMb is not (64 or 128))
        {
            errors.Add($"Guest RAM must be 64 or 128 MB, not {RamMb}.");
        }

        if (WindowWidth < 64 || WindowHeight < 64)
        {
            errors.Add($"The window must be at least 64x64 pixels (got {WindowWidth}x{WindowHeight}).");
        }

        if (!string.Equals(Renderer, "OPENGL", StringComparison.Ordinal)
            && !string.Equals(Renderer, "VULKAN", StringComparison.Ordinal)
            && !string.Equals(Renderer, "D3D11", StringComparison.Ordinal)
            && !string.Equals(Renderer, "NULL", StringComparison.Ordinal))
        {
            errors.Add($"Unknown renderer '{Renderer}'.");
        }

        if (SurfaceScale is < 1 or > 8)
        {
            errors.Add($"Surface scale must be between 1 and 8, not {SurfaceScale}.");
        }

        if (!string.Equals(Filtering, "linear", StringComparison.Ordinal)
            && !string.Equals(Filtering, "nearest", StringComparison.Ordinal))
        {
            errors.Add($"Unknown texture filtering '{Filtering}'.");
        }

        if (!StartupSizes.Contains(StartupSize))
        {
            errors.Add($"Unknown startup size '{StartupSize}'.");
        }

        if (!string.Equals(AspectRatio, "native", StringComparison.Ordinal)
            && !string.Equals(AspectRatio, "auto", StringComparison.Ordinal)
            && !string.Equals(AspectRatio, "4x3", StringComparison.Ordinal)
            && !string.Equals(AspectRatio, "16x9", StringComparison.Ordinal))
        {
            errors.Add($"Unknown aspect ratio '{AspectRatio}'.");
        }

        if (!string.Equals(Fit, "center", StringComparison.Ordinal)
            && !string.Equals(Fit, "scale", StringComparison.Ordinal)
            && !string.Equals(Fit, "stretch", StringComparison.Ordinal))
        {
            errors.Add($"Unknown fit mode '{Fit}'.");
        }

        if (VolumeLimit is < 0.0 or > 1.0)
        {
            errors.Add($"Audio volume must be between 0 and 1, not {VolumeLimit}.");
        }

        if (VoiceWorkers is < 0 or > 16)
        {
            errors.Add($"Audio worker count must be between 0 and 16, not {VoiceWorkers}.");
        }

        if (!AudioBackends.IsKnown(AudioBackend))
        {
            errors.Add($"Unknown audio backend '{AudioBackend}'.");
        }
        else if (!AudioBackends.IsAvailableOnThisHost(AudioBackend))
        {
            errors.Add($"The {AudioBackend} audio backend is not available on this platform.");
        }

        if (!string.IsNullOrEmpty(AudioDevice)
            && AudioDevice.AsSpan().IndexOfAny(",\r\n") >= 0)
        {
            errors.Add("The audio device name cannot contain a comma or a line break.");
        }

        if (!string.Equals(PresentationFilter, XemuPresentationFilter.Nearest, StringComparison.Ordinal)
            && !string.Equals(PresentationFilter, XemuPresentationFilter.Bilinear, StringComparison.Ordinal)
            && !string.Equals(PresentationFilter, XemuPresentationFilter.Scanline, StringComparison.Ordinal)
            && !string.Equals(PresentationFilter, XemuPresentationFilter.Crt, StringComparison.Ordinal))
        {
            errors.Add($"Unknown presentation filter '{PresentationFilter}'.");
        }

        if (string.Equals(AudioBackend, AudioBackends.Wav, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(AudioWavPath))
        {
            errors.Add("The WAV audio backend needs a capture path.");
        }

        if (!string.Equals(AvPack, "hdtv", StringComparison.Ordinal)
            && !string.Equals(AvPack, "scart", StringComparison.Ordinal)
            && !string.Equals(AvPack, "vga", StringComparison.Ordinal)
            && !string.Equals(AvPack, "rfu", StringComparison.Ordinal)
            && !string.Equals(AvPack, "svideo", StringComparison.Ordinal)
            && !string.Equals(AvPack, "composite", StringComparison.Ordinal)
            && !string.Equals(AvPack, "none", StringComparison.Ordinal))
        {
            errors.Add($"Unknown AV pack '{AvPack}'.");
        }

        if (string.IsNullOrWhiteSpace(WorkingDirectory))
        {
            errors.Add("The core working directory is not set.");
        }

        return errors;
    }

    /// <summary>Validates these settings and throws when the core could not boot.</summary>
    /// <exception cref="XemuConfigurationException">One or more settings are unusable.</exception>
    public void EnsureValid()
    {
        IReadOnlyList<string> errors = Validate();
        if (errors.Count > 0)
        {
            throw new XemuConfigurationException(errors);
        }
    }

    private static void RequireFile(List<string> errors, string name, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"The {name} path is not set.");
        }
        else if (!System.IO.File.Exists(path))
        {
            errors.Add($"The {name} was not found: {path}");
        }
    }
}
