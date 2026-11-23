using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>User-facing emulator configuration, persisted as JSON.</summary>
/// <remarks>
/// The shell drives the xemu core; settings that only ever applied to the
/// managed C# oracle (CPU/JIT switches, NV2A renderer, network backend and the
/// audio sink) are not part of this model. The oracle keeps its own command
/// line in <c>XGrid.Cli</c>.
/// </remarks>
public sealed class EmulatorSettings
{
    /// <summary>Gets or sets the MCPX BIOS image path (required to power on).</summary>
    public string BiosPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the optional 512-byte MCPX boot ROM path.</summary>
    public string McpxRomPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the optional 256-byte EEPROM image path.</summary>
    public string EepromPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the optional hard-disk image path.</summary>
    public string HddPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the disc image inserted at power-on (ISO/XISO, CHD or ZAR).</summary>
    public string DvdPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the guest RAM size in megabytes (64 retail, 128 debug kit).</summary>
    public int RamMb { get; set; } = 64;

    /// <summary>
    /// Gets or sets a value indicating whether the core publishes frames to the
    /// shell (shared section, shell presents) instead of showing its own window.
    /// </summary>
    public bool DisplaySink { get; init; }

    /// <summary>
    /// Gets or sets the core hosting mode: <c>in-process</c> (experimental;
    /// the shell loads the core library) or <c>process</c> (the core runs
    /// beside the shell, the stable default).
    /// </summary>
    public string CoreHosting { get; set; } = CoreHostingProcess;

    /// <summary>Hosting mode that loads the core into the shell's process.</summary>
    public const string CoreHostingInProcess = "in-process";

    /// <summary>Hosting mode that runs the core as a separate process.</summary>
    public const string CoreHostingProcess = "process";

    /// <summary>
    /// Gets or sets the presentation filter (nearest, bilinear, scanline, crt).
    /// </summary>
    public string Filter { get; set; } = Services.FrameFilters.Nearest;

    /// <summary>Gets or sets the player-1 keyboard and gamepad configuration.</summary>
    public InputSettings Input { get; set; } = new();

    /// <summary>Gets or sets the graphics configuration.</summary>
    public GraphicsSettings Graphics { get; set; } = new();

    /// <summary>Gets or sets the audio configuration.</summary>
    public AudioSettings Audio { get; set; } = new();

    /// <summary>Gets or sets the accent palette key.</summary>
    public string Accent { get; set; } = "Blue";

    /// <summary>
    /// Gets or sets an explicit xemu executable path. Empty resolves the
    /// packaged <c>core/xemu/xemu.exe</c> and then the repository's
    /// <c>data/xemu/xemu.exe</c>.
    /// </summary>
    public string XemuCorePath { get; init; } = string.Empty;

    /// <summary>Gets or sets the folders scanned by the game library.</summary>
    public List<string> LibraryFolders { get; init; } = [];

    /// <summary>
    /// Gets or sets per-game overrides. A key is either a launched image path
    /// (V1) or a title id written as <c>title:4D530002</c> (V2), which follows
    /// the title across paths and containers.
    /// </summary>
    public Dictionary<string, TitleOverride> TitleOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prefix that marks an override key as an XBE title id.</summary>
    public const string TitleIdKeyPrefix = "title:";

    /// <summary>Builds the override dictionary key for an XBE title id.</summary>
    /// <param name="titleId">XBE certificate title id.</param>
    public static string TitleIdKey(uint titleId)
    {
        return TitleIdKeyPrefix + titleId.ToString("X8", CultureInfo.InvariantCulture);
    }

    /// <summary>Returns true when an override key names a title id (not a path).</summary>
    /// <param name="key">Override dictionary key.</param>
    public static bool IsTitleIdKey(string key)
    {
        return key.StartsWith(TitleIdKeyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parses an override key written by <see cref="TitleIdKey"/>.</summary>
    /// <param name="key">Override dictionary key.</param>
    /// <param name="titleId">Parsed title id.</param>
    public static bool TryParseTitleIdKey(string key, out uint titleId)
    {
        titleId = 0;
        return IsTitleIdKey(key)
            && uint.TryParse(
            key.AsSpan(TitleIdKeyPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out titleId);
    }

    /// <summary>
    /// Returns the override for an image, preferring a title-id key (V2) and
    /// falling back to the image path (V1). A null title id means the caller
    /// could not read one (e.g. a CHD image), so only the path can match.
    /// </summary>
    /// <param name="gamePath">Launched ISO/CHD/XBE path.</param>
    /// <param name="titleId">XBE title id of the launched disc, when known.</param>
    public TitleOverride? FindTitleOverride(string? gamePath, uint? titleId = null)
    {
        if (TitleOverrides.Count == 0)
        {
            return null;
        }

        if (titleId is { } id && TitleOverrides.TryGetValue(TitleIdKey(id), out TitleOverride? byTitle))
        {
            return byTitle;
        }

        return string.IsNullOrWhiteSpace(gamePath)
            ? null
            : TitleOverrides.TryGetValue(NormalizeTitleKey(gamePath), out TitleOverride? value) ? value : null;
    }

    /// <summary>
    /// Canonicalizes an override key: title-id keys are rebuilt from their
    /// parsed value, path keys are made absolute.
    /// </summary>
    /// <param name="key">Raw override key.</param>
    public static string CanonicalTitleKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return TryParseTitleIdKey(key, out uint id) ? TitleIdKey(id) : NormalizeTitleKey(key);
    }

    /// <summary>Normalizes an image path into the override dictionary key.</summary>
    /// <param name="gamePath">Launched ISO/CHD/XBE path.</param>
    public static string NormalizeTitleKey(string gamePath)
    {
        ArgumentNullException.ThrowIfNull(gamePath);
        string trimmed = gamePath.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }

    /// <summary>
    /// Returns an independent copy with the per-title override for
    /// <paramref name="gamePath"/> (or its title id, when known) applied, or a
    /// plain copy when there is none.
    /// </summary>
    /// <param name="gamePath">Launched ISO/CHD/XBE path (null for the dashboard).</param>
    /// <param name="titleId">XBE title id of the launched disc, when known.</param>
    public EmulatorSettings WithTitleOverride(string? gamePath, uint? titleId = null)
    {
        EmulatorSettings effective = Clone();
        TitleOverride? title = FindTitleOverride(gamePath, titleId);
        if (title?.RamMb is { } ram)
        {
            effective.RamMb = ram is 128 ? 128 : 64;
        }

        return effective;
    }

    /// <summary>Creates an independent copy of these settings.</summary>
    public EmulatorSettings Clone()
    {
        var result = new EmulatorSettings
        {
            BiosPath = BiosPath,
            McpxRomPath = McpxRomPath,
            EepromPath = EepromPath,
            HddPath = HddPath,
            DvdPath = DvdPath,
            RamMb = RamMb,
            Accent = Accent,
            XemuCorePath = XemuCorePath,
            DisplaySink = DisplaySink,
            CoreHosting = CoreHosting,
            Filter = Filter,
            Input = Input?.Clone() ?? new InputSettings(),
            Graphics = Graphics?.Clone() ?? new GraphicsSettings(),
            Audio = Audio?.Clone() ?? new AudioSettings(),
            LibraryFolders = [.. LibraryFolders],
        };
        foreach (KeyValuePair<string, TitleOverride> pair in TitleOverrides)
        {
            result.TitleOverrides[pair.Key] = pair.Value.Clone();
        }

        return result;
    }

    /// <summary>Clamps and repairs values loaded from disk.</summary>
    public void Normalize()
    {
        RamMb = RamMb is 128 ? 128 : 64;
        CoreHosting = string.Equals(CoreHosting, CoreHostingInProcess, StringComparison.OrdinalIgnoreCase)
            ? CoreHostingInProcess
            : CoreHostingProcess;
        Filter = Services.FrameFilters.Normalize(Filter);
        Input ??= new InputSettings();
        Graphics ??= new GraphicsSettings();
        Audio ??= new AudioSettings();
        Input.Normalize();
        Graphics.Normalize();
        Audio.Normalize();
        if (string.IsNullOrWhiteSpace(Accent))
        {
            Accent = "Blue";
        }

        LibraryFolders.RemoveAll(string.IsNullOrWhiteSpace);

        var overrides = new Dictionary<string, TitleOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, TitleOverride> pair in TitleOverrides)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || pair.Value.IsEmpty)
            {
                continue;
            }

            TitleOverride value = pair.Value;
            if (value.RamMb is { } ram && ram is not (64 or 128))
            {
                value.RamMb = null;
            }

            // A title key that does not parse can never match a launch;
            // dropping it is better than expanding it into a bogus path.
            if (IsTitleIdKey(pair.Key) && !TryParseTitleIdKey(pair.Key, out _))
            {
                continue;
            }

            overrides[CanonicalTitleKey(pair.Key)] = value;
        }

        TitleOverrides = overrides;
    }

    /// <summary>Returns true when the required firmware image is present on disk.</summary>
    [JsonIgnore]
    public bool IsBiosReady => !string.IsNullOrWhiteSpace(BiosPath) && File.Exists(BiosPath);

}
