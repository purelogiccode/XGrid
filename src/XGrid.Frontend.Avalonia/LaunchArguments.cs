using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Serilog;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia;

/// <summary>
/// The command-line switches the shell handles itself. Recognized switches are
/// removed from the arguments Avalonia sees; everything else is passed through
/// unchanged.
/// </summary>
/// <remarks>
/// The parser is deliberately forgiving about unknown arguments, because the
/// platform and Avalonia still receive their own switches. A recognized switch
/// with a bad value is an error instead: silently ignoring a mistyped renderer
/// would make an automated run look successful while booting something else.
/// </remarks>
public sealed record LaunchArguments
{
    /// <summary>The default headless run length, in seconds.</summary>
    public const int DefaultHeadlessSeconds = 60;

    /// <summary>The frame limit used by <c>--benchmark</c> when no limit is given.</summary>
    public const long DefaultBenchmarkFrames = 600;

    /// <summary>Gets a value indicating whether the console powers on at startup.</summary>
    public bool Autostart { get; init; }

    /// <summary>Gets a value indicating whether the shell opens fullscreen.</summary>
    public bool Fullscreen { get; init; }

    /// <summary>Gets a value indicating whether the run is headless (no shell window).</summary>
    public bool Headless { get; init; }

    /// <summary>
    /// Gets a value indicating whether the headless run is a benchmark. Benchmark
    /// runs default to <see cref="DefaultBenchmarkFrames"/> presented frames when
    /// neither <c>--seconds</c> nor <c>--frames</c> is given.
    /// </summary>
    public bool Benchmark { get; init; }

    /// <summary>Gets a value indicating whether the run should print the command-line help.</summary>
    public bool Help { get; init; }

    /// <summary>Gets a value indicating whether the run should print the application version.</summary>
    public bool Version { get; init; }

    /// <summary>Gets the disc image to insert, or null for the configured disc.</summary>
    public string? DiscPath { get; init; }

    /// <summary>Gets the headless run length in seconds, or null when no wall-clock limit is set.</summary>
    public int? Seconds { get; init; }

    /// <summary>Gets the number of presented frames after which a headless run stops, or null.</summary>
    public long? Frames { get; init; }

    /// <summary>Gets the path of the JSON run report to write, or null.</summary>
    public string? SummaryPath { get; init; }

    /// <summary>Gets the shell data folder to use for this run, or null for the default.</summary>
    public string? DataDirectory { get; init; }

    /// <summary>Gets the emulator binary to use for this run, or null for the configured one.</summary>
    public string? CorePath { get; init; }

    /// <summary>Gets the flash ROM image to use for this run, or null for the configured one.</summary>
    public string? BiosPath { get; init; }

    /// <summary>Gets the 512-byte MCPX boot ROM to use for this run, or null for the configured one.</summary>
    public string? McpxRomPath { get; init; }

    /// <summary>Gets the EEPROM image to use for this run, or null for the configured one.</summary>
    public string? EepromPath { get; init; }

    /// <summary>Gets the hard-disk image to use for this run, or null for the configured one.</summary>
    public string? HddPath { get; init; }

    /// <summary>Gets the core renderer key (<c>OPENGL</c>, <c>VULKAN</c> or <c>NULL</c>), or null.</summary>
    public string? Renderer { get; init; }

    /// <summary>Gets the internal surface scale (1-8), or null for the configured one.</summary>
    public int? SurfaceScale { get; init; }

    /// <summary>Gets the core texture filtering (<c>linear</c> or <c>nearest</c>), or null.</summary>
    public string? Filtering { get; init; }

    /// <summary>Gets the core presentation filter, or null for the configured one.</summary>
    public string? PresentationFilter { get; init; }

    /// <summary>Gets the aspect-ratio mode, or null for the configured one.</summary>
    public string? AspectRatio { get; init; }

    /// <summary>Gets the fit mode, or null for the configured one.</summary>
    public string? Fit { get; init; }

    /// <summary>Gets the guest RAM size in megabytes (64 or 128), or null.</summary>
    public int? RamMb { get; init; }

    /// <summary>Gets the host audio backend, or null for the configured one.</summary>
    public string? AudioBackend { get; init; }

    /// <summary>Gets the arguments Avalonia and the platform should still see.</summary>
    public IReadOnlyList<string> Remaining { get; init; } = [];

    /// <summary>Gets the recognized switches that could not be parsed.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Gets a value indicating whether at least one switch was rejected.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Gets a value indicating whether a session override is present.</summary>
    public bool HasOverrides =>
        BiosPath is not null
        || McpxRomPath is not null
        || EepromPath is not null
        || HddPath is not null
        || CorePath is not null
        || Renderer is not null
        || SurfaceScale is not null
        || Filtering is not null
        || PresentationFilter is not null
        || AspectRatio is not null
        || Fit is not null
        || RamMb is not null
        || AudioBackend is not null;

    /// <summary>Gets the command-line help text.</summary>
    public static string Usage { get; } = """
        XGrid - Xbox emulator shell

        Usage:
          XGrid.Frontend.Avalonia [options] [disc-image]

        Session:
          --autostart                 Power on as soon as the shell is ready.
          -f, -fullscreen, --fullscreen  Open the shell fullscreen.
          --disc <path>               Insert this ISO/XISO, CHD or ZAR and power on.
          --core <path>               Use this emulator binary for this run.
          --data-dir <path>           Use this shell data folder for this run.
          --bios <path>               Flash ROM image for this run.
          --mcpx <path>               512-byte MCPX boot ROM for this run.
          --eeprom <path>             256-byte EEPROM image for this run.
          --hdd <path>                Hard-disk image for this run.

        Graphics and audio:
          --renderer <name>           OPENGL, VULKAN or NULL.
          --scale <1-8>               Internal surface scale.
          --filtering <mode>          Core texture filtering: linear or nearest.
          --filter <mode>             Presentation filter: nearest, bilinear, scanline or crt.
          --aspect <mode>             native, auto, 4x3 or 16x9.
          --fit <mode>                center, scale or stretch.
          --ram <64|128>              Guest RAM in megabytes.
          --audio <backend>           auto, dsound, none or wav.

        Headless:
          --headless                  Run without a shell window.
          --benchmark                 Benchmark run; 600 frames when no limit is given.
          --seconds <n>               Stop after n seconds (60 when no frame limit).
          --frames <n>                Stop after n presented frames.
          --summary <path>            Write a JSON run report.

        Other:
          --                          Pass the remaining arguments through.
          A positional .iso/.xiso/.chd/.zar path is --disc plus --autostart.

        This build:
          -h, --help                  Show this help.
          --version                   Show the application version.
        """;

    /// <summary>Parses the process arguments, accepting the switches in any case.</summary>
    /// <param name="arguments">The command line, not including the executable.</param>
    /// <returns>The recognized switches, the remaining arguments and any errors.</returns>
    public static LaunchArguments Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string[] args = [.. arguments];
        var builder = new Builder();
        var remaining = new List<string>();
        bool endOfOptions = false;
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (endOfOptions)
            {
                remaining.Add(argument);
                continue;
            }

            if (string.Equals(argument, "--", StringComparison.Ordinal))
            {
                endOfOptions = true;
                continue;
            }

            if (!builder.TryApply(args, ref index))
            {
                remaining.Add(argument);
            }
        }

        return builder.Build(remaining);
    }

    /// <summary>
    /// Applies the session overrides to a settings copy. Values that were not
    /// given on the command line are left untouched.
    /// </summary>
    /// <param name="settings">Settings to update in place.</param>
    public void ApplyTo(EmulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (BiosPath is not null)
        {
            settings.BiosPath = BiosPath;
        }

        if (McpxRomPath is not null)
        {
            settings.McpxRomPath = McpxRomPath;
        }

        if (EepromPath is not null)
        {
            settings.EepromPath = EepromPath;
        }

        if (HddPath is not null)
        {
            settings.HddPath = HddPath;
        }

        if (CorePath is not null)
        {
            settings.XemuCorePath = CorePath;
        }

        if (Renderer is not null)
        {
            settings.Graphics.Renderer = Renderer;
        }

        if (SurfaceScale is { } scale)
        {
            settings.Graphics.SurfaceScale = scale;
        }

        if (Filtering is not null)
        {
            settings.Graphics.Filtering = Filtering;
        }

        if (PresentationFilter is not null)
        {
            settings.Filter = PresentationFilter;
        }

        if (AspectRatio is not null)
        {
            settings.Graphics.AspectRatio = AspectRatio;
        }

        if (Fit is not null)
        {
            settings.Graphics.Fit = Fit;
        }

        if (RamMb is { } ram)
        {
            settings.RamMb = ram;
        }

        if (AudioBackend is not null)
        {
            settings.Audio.Backend = AudioBackend;
        }

        settings.Normalize();
    }

    private sealed class Builder
    {
        private bool _autostart;
        private bool _fullscreen;
        private bool _headless;
        private bool _benchmark;
        private bool _help;
        private bool _version;
        private string? _discPath;
        private int? _seconds;
        private long? _frames;
        private string? _summaryPath;
        private string? _dataDirectory;
        private string? _corePath;
        private string? _biosPath;
        private string? _mcpxRomPath;
        private string? _eepromPath;
        private string? _hddPath;
        private string? _renderer;
        private int? _surfaceScale;
        private string? _filtering;
        private string? _presentationFilter;
        private string? _aspectRatio;
        private string? _fit;
        private int? _ramMb;
        private string? _audioBackend;
        private List<string>? _errors;

        private List<string> Errors => _errors ??= [];

        public bool TryApply(string[] args, ref int index)
        {
            string argument = args[index];
            switch (argument.ToLowerInvariant())
            {
                case "--autostart":
                    _autostart = true;
                    return true;
                case "-f":
                case "-fullscreen":
                case "--fullscreen":
                    _fullscreen = true;
                    return true;
                case "--headless":
                    _headless = true;
                    return true;
                case "--benchmark":
                    _benchmark = true;
                    return true;
                case "-h":
                case "-?":
                case "--help":
                    _help = true;
                    return true;
                case "--version":
                    _version = true;
                    return true;
            }

            string name = argument;
            string? inline = null;
            int equals = argument.IndexOf('=', StringComparison.Ordinal);
            if (equals > 2 && argument.StartsWith("--", StringComparison.Ordinal))
            {
                name = argument[..equals];
                inline = argument[(equals + 1)..];
            }

            switch (name.ToLowerInvariant())
            {
                case "--disc":
                    return ReadDisc(args, ref index, inline);
                case "--seconds":
                    return ReadSeconds(args, ref index, inline);
                case "--frames":
                    return ReadFrames(args, ref index, inline);
                case "--summary":
                    return ReadPath(args, ref index, inline, "--summary", path => _summaryPath = path);
                case "--data-dir":
                    return ReadPath(args, ref index, inline, "--data-dir", path => _dataDirectory = path);
                case "--core":
                    return ReadPath(args, ref index, inline, "--core", path => _corePath = path);
                case "--bios":
                    return ReadPath(args, ref index, inline, "--bios", path => _biosPath = path);
                case "--mcpx":
                    return ReadPath(args, ref index, inline, "--mcpx", path => _mcpxRomPath = path);
                case "--eeprom":
                    return ReadPath(args, ref index, inline, "--eeprom", path => _eepromPath = path);
                case "--hdd":
                    return ReadPath(args, ref index, inline, "--hdd", path => _hddPath = path);
                case "--renderer":
                    return ReadValue(args, ref index, inline, "--renderer", SetRenderer);
                case "--scale":
                    return ReadValue(args, ref index, inline, "--scale", SetScale);
                case "--filtering":
                    return ReadValue(args, ref index, inline, "--filtering", SetFiltering);
                case "--filter":
                    return ReadValue(args, ref index, inline, "--filter", SetPresentationFilter);
                case "--aspect":
                    return ReadValue(args, ref index, inline, "--aspect", SetAspectRatio);
                case "--fit":
                    return ReadValue(args, ref index, inline, "--fit", SetFit);
                case "--ram":
                    return ReadValue(args, ref index, inline, "--ram", SetRam);
                case "--audio":
                    return ReadValue(args, ref index, inline, "--audio", SetAudioBackend);
            }

            if (!argument.StartsWith('-') && GameLibraryService.IsGameCandidate(argument))
            {
                SetDisc(argument);
                return true;
            }

            return false;
        }

        public LaunchArguments Build(List<string> remaining)
        {
            // A headless limit or report has no meaning in the windowed shell,
            // so it selects the headless run instead of being silently ignored.
            bool headless = _headless
                            || _benchmark
                            || _seconds is not null
                            || _frames is not null
                            || _summaryPath is not null;
            long? frames = _frames;
            if (_benchmark && frames is null && _seconds is null)
            {
                // A fixed frame count keeps repeated benchmark runs comparable.
                frames = DefaultBenchmarkFrames;
            }

            return new LaunchArguments
            {
                // A disc on the command line is a request to launch it; the
                // explicit switch is only needed when the path could be
                // mistaken for a platform argument.
                Autostart = _autostart || _discPath is not null,
                Fullscreen = _fullscreen,
                Headless = headless,
                Benchmark = _benchmark,
                Help = _help,
                Version = _version,
                DiscPath = _discPath,
                Seconds = _seconds,
                Frames = frames,
                SummaryPath = _summaryPath,
                DataDirectory = _dataDirectory,
                CorePath = _corePath,
                BiosPath = _biosPath,
                McpxRomPath = _mcpxRomPath,
                EepromPath = _eepromPath,
                HddPath = _hddPath,
                Renderer = _renderer,
                SurfaceScale = _surfaceScale,
                Filtering = _filtering,
                PresentationFilter = _presentationFilter,
                AspectRatio = _aspectRatio,
                Fit = _fit,
                RamMb = _ramMb,
                AudioBackend = _audioBackend,
                Remaining = remaining,
                Errors = _errors ?? [],
            };
        }

        private bool ReadDisc(string[] args, ref int index, string? inline)
        {
            if (!TakeValue(args, ref index, inline, "--disc", out string value))
            {
                return true;
            }

            SetDisc(value);
            return true;
        }

        private bool ReadSeconds(string[] args, ref int index, string? inline)
        {
            if (!TakeValue(args, ref index, inline, "--seconds", out string value))
            {
                return true;
            }

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
                || seconds is < 1 or > 86400)
            {
                Errors.Add($"--seconds needs a whole number from 1 to 86400, not '{value}'.");
                return true;
            }

            _seconds = seconds;
            return true;
        }

        private bool ReadFrames(string[] args, ref int index, string? inline)
        {
            if (!TakeValue(args, ref index, inline, "--frames", out string value))
            {
                return true;
            }

            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long frames)
                || frames is < 1 or > 100_000_000)
            {
                Errors.Add($"--frames needs a whole number from 1 to 100000000, not '{value}'.");
                return true;
            }

            _frames = frames;
            return true;
        }

        private bool ReadPath(
            string[] args,
            ref int index,
            string? inline,
            string name,
            Action<string> apply)
        {
            if (!TakeValue(args, ref index, inline, name, out string value))
            {
                return true;
            }

            string? path = ResolvePath(value, name);
            if (path is not null)
            {
                apply(path);
            }

            return true;
        }

        private bool ReadValue(
            string[] args,
            ref int index,
            string? inline,
            string name,
            Action<string> apply)
        {
            if (TakeValue(args, ref index, inline, name, out string value))
            {
                apply(value);
            }

            return true;
        }

        private bool TakeValue(
            string[] args,
            ref int index,
            string? inline,
            string name,
            out string value)
        {
            if (inline is not null)
            {
                value = inline;
                if (value.Length == 0)
                {
                    Errors.Add($"{name} needs a value.");
                    return false;
                }

                return true;
            }

            if (index + 1 >= args.Length)
            {
                value = string.Empty;
                Errors.Add($"{name} needs a value.");
                return false;
            }

            // Do not swallow the next switch as this switch's value. A
            // recognized option with a missing value is an error, and the
            // following option still has to be applied.
            string next = args[index + 1];
            if (next.StartsWith("--", StringComparison.Ordinal)
                || (next.Length == 2
                    && next[0] == '-'
                    && char.IsLetter(next[1])))
            {
                value = string.Empty;
                Errors.Add($"{name} needs a value.");
                return false;
            }

            value = args[++index];
            return true;
        }

        private string? ResolvePath(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Errors.Add($"{name} needs a non-empty path.");
                return null;
            }

            try
            {
                return Path.GetFullPath(value);
            }
            catch (Exception exception) when (exception is ArgumentException
                                                  or NotSupportedException
                                                  or PathTooLongException)
            {
                Log.Warning(exception, "The command-line path for {Option} is not valid: {Value}", name, value);
                Errors.Add($"{name} is not a valid path: {value}");
                return null;
            }
        }

        private void SetDisc(string path)
        {
            if (_discPath is not null)
            {
                Errors.Add("Only one disc image can be given.");
                return;
            }

            if (!GameLibraryService.IsGameCandidate(path))
            {
                Errors.Add($"'{path}' is not a supported disc image. Use .iso, .xiso, .chd or .zar.");
                return;
            }

            if (Path.GetExtension(path).Equals(".xbe", StringComparison.OrdinalIgnoreCase))
            {
                Errors.Add("A loose .xbe is not bootable; wrap it in an ISO or CHD image.");
                return;
            }

            string? resolved = ResolvePath(path, "--disc");
            if (resolved is not null)
            {
                _discPath = resolved;
            }
        }

        private void SetRenderer(string value)
        {
            switch (value.ToUpperInvariant())
            {
                case "OPENGL":
                case "GL":
                    _renderer = GraphicsSettings.RendererOpenGl;
                    break;
                case "VULKAN":
                case "VK":
                    _renderer = GraphicsSettings.RendererVulkan;
                    break;
                case "NULL":
                    _renderer = GraphicsSettings.RendererNull;
                    break;
                default:
                    Errors.Add($"Unknown renderer '{value}'. Use OPENGL, VULKAN or NULL.");
                    break;
            }
        }

        private void SetScale(string value)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int scale)
                || scale is < 1 or > 8)
            {
                Errors.Add($"--scale needs a whole number from 1 to 8, not '{value}'.");
                return;
            }

            _surfaceScale = scale;
        }

        private void SetFiltering(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case GraphicsSettings.FilterLinear:
                    _filtering = GraphicsSettings.FilterLinear;
                    break;
                case GraphicsSettings.FilterNearest:
                    _filtering = GraphicsSettings.FilterNearest;
                    break;
                default:
                    Errors.Add($"Unknown texture filtering '{value}'. Use linear or nearest.");
                    break;
            }
        }

        private void SetPresentationFilter(string value)
        {
            foreach ((string key, _) in XemuPresentationFilter.All)
            {
                if (string.Equals(key, value, StringComparison.OrdinalIgnoreCase))
                {
                    _presentationFilter = key;
                    return;
                }
            }

            Errors.Add($"Unknown presentation filter '{value}'. Use nearest, bilinear, scanline or crt.");
        }

        private void SetAspectRatio(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "native":
                    _aspectRatio = "native";
                    break;
                case "auto":
                    _aspectRatio = "auto";
                    break;
                case "4x3":
                case "4:3":
                    _aspectRatio = "4x3";
                    break;
                case "16x9":
                case "16:9":
                    _aspectRatio = "16x9";
                    break;
                default:
                    Errors.Add($"Unknown aspect ratio '{value}'. Use native, auto, 4x3 or 16x9.");
                    break;
            }
        }

        private void SetFit(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "center":
                    _fit = "center";
                    break;
                case "scale":
                    _fit = "scale";
                    break;
                case "stretch":
                    _fit = "stretch";
                    break;
                default:
                    Errors.Add($"Unknown fit mode '{value}'. Use center, scale or stretch.");
                    break;
            }
        }

        private void SetRam(string value)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int ram)
                || ram is not (64 or 128))
            {
                Errors.Add($"--ram must be 64 or 128, not '{value}'.");
                return;
            }

            _ramMb = ram;
        }

        private void SetAudioBackend(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case AudioSettings.BackendAuto:
                    _audioBackend = AudioSettings.BackendAuto;
                    break;
                case AudioSettings.BackendDirectSound:
                    if (OperatingSystem.IsWindows())
                    {
                        _audioBackend = AudioSettings.BackendDirectSound;
                    }
                    else
                    {
                        Errors.Add("The dsound audio backend is only available on Windows.");
                    }

                    break;
                case AudioSettings.BackendNone:
                    _audioBackend = AudioSettings.BackendNone;
                    break;
                case AudioSettings.BackendWav:
                    _audioBackend = AudioSettings.BackendWav;
                    break;
                default:
                    Errors.Add($"Unknown audio backend '{value}'. Use auto, dsound, none or wav.");
                    break;
            }
        }
    }
}
