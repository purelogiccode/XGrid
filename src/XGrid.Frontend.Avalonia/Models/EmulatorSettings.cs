using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>User-facing emulator configuration, persisted as JSON.</summary>
public sealed class EmulatorSettings
{
    /// <summary>Gets or sets the MCPX BIOS image path (required to power on).</summary>
    public string BiosPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional 512-byte MCPX boot ROM path.</summary>
    public string McpxRomPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional 256-byte EEPROM image path.</summary>
    public string EepromPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional hard-disk image path.</summary>
    public string HddPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the disc image inserted at power-on (ISO/XISO or CHD).</summary>
    public string DvdPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the guest RAM size in megabytes (64 retail, 128 debug kit).</summary>
    public int RamMb { get; set; } = 64;

    /// <summary>Gets or sets a value indicating whether the fast-mode JIT engine is enabled.</summary>
    public bool FastMode { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the raw x86-64 block emitter is used.</summary>
    public bool RawBlocks { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether idle-spin fast-forwarding is enabled.</summary>
    public bool SpinSkip { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the decode cache is enabled.</summary>
    public bool DecodeCache { get; set; } = true;

    /// <summary>Gets or sets the JIT build threshold (visits before a block is compiled).</summary>
    public int JitThreshold { get; set; } = 8;

    /// <summary>Gets or sets a value indicating whether the NV2A fixed-function vertex stage is used (<c>--ffp</c>).</summary>
    public bool FixedFunctionVertex { get; set; }

    /// <summary>Gets or sets the NV2A texture filter mode ("legacy" or "registers", <c>--tex-filter</c>).</summary>
    public string TextureFilter { get; set; } = "legacy";

    /// <summary>Gets or sets the graphics renderer ("software" or "gl", <c>--gpu</c>).</summary>
    /// <remarks>
    /// The software rasterizer is the exact renderer (gate currency). "gl" asks
    /// for the host OpenGL backend in replacement mode, which renders the
    /// presented image on the GPU and is several times faster on the dashboard's
    /// menu stream; it falls back to software when no GL context can be created.
    /// </remarks>
    public string GpuRenderer { get; set; } = SoftwareRenderer;

    /// <summary>Renderer key for the exact software rasterizer (the default).</summary>
    public const string SoftwareRenderer = "software";

    /// <summary>Renderer key for the host OpenGL backend (replacement mode).</summary>
    public const string OpenGlRenderer = "gl";

    /// <summary>Gets or sets a value indicating whether the 007 end-of-stream quirk is enabled.</summary>
    public bool EosQuirk { get; set; } = true;

    /// <summary>Gets or sets the NVNET backend ("loopback" or "socket").</summary>
    public string NetworkBackend { get; set; } = "loopback";

    /// <summary>Gets or sets a value indicating whether the emulated network link is up.</summary>
    public bool NetworkLinkUp { get; set; } = true;

    /// <summary>Gets or sets the accent palette key.</summary>
    public string Accent { get; set; } = "Emerald";

    /// <summary>Gets or sets the folders scanned by the game library.</summary>
    public List<string> LibraryFolders { get; set; } = new();

    /// <summary>Gets or sets recently launched disc images (newest first).</summary>
    public List<string> RecentGames { get; set; } = new();

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
        return key is not null && key.StartsWith(TitleIdKeyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parses an override key written by <see cref="TitleIdKey"/>.</summary>
    /// <param name="key">Override dictionary key.</param>
    /// <param name="titleId">Parsed title id.</param>
    public static bool TryParseTitleIdKey(string key, out uint titleId)
    {
        titleId = 0;
        if (!IsTitleIdKey(key))
        {
            return false;
        }

        return uint.TryParse(
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

        if (titleId is uint id && TitleOverrides.TryGetValue(TitleIdKey(id), out TitleOverride? byTitle))
        {
            return byTitle;
        }

        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return null;
        }

        return TitleOverrides.TryGetValue(NormalizeTitleKey(gamePath), out TitleOverride? value) ? value : null;
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
        if (title is null)
        {
            return effective;
        }

        if (title.FixedFunctionVertex is bool fixedFunction)
        {
            effective.FixedFunctionVertex = fixedFunction;
        }

        if (!string.IsNullOrEmpty(title.TextureFilter))
        {
            effective.TextureFilter = string.Equals(title.TextureFilter, "registers", StringComparison.Ordinal)
                ? "registers"
                : "legacy";
        }

        if (!string.IsNullOrEmpty(title.GpuRenderer))
        {
            effective.GpuRenderer = string.Equals(title.GpuRenderer, OpenGlRenderer, StringComparison.Ordinal)
                ? OpenGlRenderer
                : SoftwareRenderer;
        }

        if (title.RamMb is int ram)
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
            FastMode = FastMode,
            RawBlocks = RawBlocks,
            SpinSkip = SpinSkip,
            DecodeCache = DecodeCache,
            JitThreshold = JitThreshold,
            FixedFunctionVertex = FixedFunctionVertex,
            TextureFilter = TextureFilter,
            GpuRenderer = GpuRenderer,
            EosQuirk = EosQuirk,
            NetworkBackend = NetworkBackend,
            NetworkLinkUp = NetworkLinkUp,
            Accent = Accent,
            LibraryFolders = new List<string>(LibraryFolders),
            RecentGames = new List<string>(RecentGames),
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
        JitThreshold = Math.Clamp(JitThreshold, 1, 4096);
        TextureFilter = string.Equals(TextureFilter, "registers", StringComparison.Ordinal)
            ? "registers"
            : "legacy";
        GpuRenderer = string.Equals(GpuRenderer, OpenGlRenderer, StringComparison.Ordinal)
            ? OpenGlRenderer
            : SoftwareRenderer;
        NetworkBackend = string.Equals(NetworkBackend, "socket", StringComparison.Ordinal)
            ? "socket"
            : "loopback";
        if (string.IsNullOrWhiteSpace(Accent))
        {
            Accent = "Emerald";
        }

        LibraryFolders ??= new List<string>();
        RecentGames ??= new List<string>();
        LibraryFolders.RemoveAll(string.IsNullOrWhiteSpace);
        RecentGames.RemoveAll(string.IsNullOrWhiteSpace);

        var overrides = new Dictionary<string, TitleOverride>(StringComparer.OrdinalIgnoreCase);
        if (TitleOverrides is not null)
        {
            foreach (KeyValuePair<string, TitleOverride> pair in TitleOverrides)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null || pair.Value.IsEmpty)
                {
                    continue;
                }

                TitleOverride value = pair.Value;
                value.TextureFilter = string.Equals(value.TextureFilter, "registers", StringComparison.Ordinal)
                    ? "registers"
                    : string.Equals(value.TextureFilter, "legacy", StringComparison.Ordinal)
                        ? "legacy"
                        : null;
                value.GpuRenderer = string.Equals(value.GpuRenderer, OpenGlRenderer, StringComparison.Ordinal)
                    ? OpenGlRenderer
                    : string.Equals(value.GpuRenderer, SoftwareRenderer, StringComparison.Ordinal)
                        ? SoftwareRenderer
                        : null;
                if (value.RamMb is int ram && ram is not (64 or 128))
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
        }

        TitleOverrides = overrides;
    }

    /// <summary>Gets a value indicating whether a host GPU backend is requested.</summary>
    [JsonIgnore]
    public bool UsesGpuBackend => string.Equals(GpuRenderer, OpenGlRenderer, StringComparison.Ordinal);

    /// <summary>Returns true when the required firmware image is present on disk.</summary>
    [JsonIgnore]
    public bool IsBiosReady => !string.IsNullOrWhiteSpace(BiosPath) && File.Exists(BiosPath);

    /// <summary>Returns the number of configured library folders that still exist.</summary>
    [JsonIgnore]
    public int ExistingLibraryFolderCount
    {
        get
        {
            int count = 0;
            foreach (string folder in LibraryFolders)
            {
                if (Directory.Exists(folder))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
