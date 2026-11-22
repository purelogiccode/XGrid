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
    public string BiosPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the optional 512-byte MCPX boot ROM path.</summary>
    public string McpxRomPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the optional 256-byte EEPROM image path.</summary>
    public string EepromPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the optional hard-disk image path.</summary>
    public string HddPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the disc image inserted at power-on (ISO/XISO or CHD).</summary>
    public string DvdPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the guest RAM size in megabytes (64 retail, 128 debug kit).</summary>
    public int RamMb { get; set; } = 64;

    /// <summary>Gets or sets a value indicating whether the fast-mode JIT engine is enabled.</summary>
    public bool FastMode { get; init; } = true;

    /// <summary>Gets or sets a value indicating whether the raw x86-64 block emitter is used.</summary>
    public bool RawBlocks { get; init; } = true;

    /// <summary>Gets or sets a value indicating whether idle-spin fast-forwarding is enabled.</summary>
    public bool SpinSkip { get; init; } = true;

    /// <summary>Gets or sets a value indicating whether the decode cache is enabled.</summary>
    public bool DecodeCache { get; init; } = true;

    /// <summary>Gets or sets the JIT build threshold (visits before a block is compiled).</summary>
    public int JitThreshold { get; set; } = 8;

    /// <summary>
    /// Gets or sets a value indicating whether the APU DSP and voice processor
    /// run (the guest microcode and mixer that produce PCM).
    /// </summary>
    public bool ApuDsp { get; init; } = true;

    /// <summary>
    /// Gets or sets the audio output: <see cref="AudioOutputLive"/> (the Windows
    /// WASAPI shared-mode sink) or <see cref="AudioOutputOff"/>. A live sink that
    /// cannot open falls back to silence for the session, never to a fault.
    /// </summary>
    public string AudioOutput { get; set; } = AudioOutputLive;

    /// <summary>Audio output key: no host sink.</summary>
    public const string AudioOutputOff = "off";

    /// <summary>Audio output key: live WASAPI shared-mode sink (Windows).</summary>
    public const string AudioOutputLive = "live";

    /// <summary>Gets or sets a value indicating whether the NV2A fixed-function vertex stage is used (<c>--ffp</c>).</summary>
    public bool FixedFunctionVertex { get; set; }

    /// <summary>Gets or sets the NV2A texture filter mode ("legacy" or "registers", <c>--tex-filter</c>).</summary>
    public string TextureFilter { get; set; } = "legacy";

    /// <summary>
    /// Gets or sets the renderer: <c>software</c> (the portable reference
    /// rasterizer, always available) or <c>opengl</c> (the host GPU backend in
    /// replacement mode, Windows/WGL only - <c>--gpu gl --gpu-replace</c>).
    /// An unavailable GPU renderer falls back to software for the session.
    /// </summary>
    public string Renderer { get; set; } = RendererSoftware;

    /// <summary>Renderer key for the software rasterizer.</summary>
    public const string RendererSoftware = "software";

    /// <summary>Renderer key for the OpenGL host GPU backend.</summary>
    public const string RendererOpenGl = "opengl";

    /// <summary>Returns true when <paramref name="renderer"/> names the GPU backend.</summary>
    /// <param name="renderer">Renderer key.</param>
    public static bool IsGpuRenderer(string? renderer)
    {
        return string.Equals(renderer, RendererOpenGl, StringComparison.Ordinal);
    }

    /// <summary>Gets or sets a value indicating whether the 007 end-of-stream quirk is enabled.</summary>
    public bool EosQuirk { get; init; } = true;

    /// <summary>Gets or sets the NVNET backend ("loopback" or "socket").</summary>
    public string NetworkBackend { get; set; } = "loopback";

    /// <summary>Gets or sets a value indicating whether the emulated network link is up.</summary>
    public bool NetworkLinkUp { get; init; } = true;

    /// <summary>Gets or sets the accent palette key.</summary>
    public string Accent { get; set; } = "Emerald";

    /// <summary>Gets or sets the folders scanned by the game library.</summary>
    public List<string> LibraryFolders { get; init; } = [];

    /// <summary>Gets or sets recently launched disc images (newest first).</summary>
    public List<string> RecentGames { get; init; } = [];

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
        if (title is null)
        {
            return effective;
        }

        if (title.FixedFunctionVertex is { } fixedFunction)
        {
            effective.FixedFunctionVertex = fixedFunction;
        }

        if (!string.IsNullOrEmpty(title.TextureFilter))
        {
            effective.TextureFilter = string.Equals(title.TextureFilter, "registers", StringComparison.Ordinal)
                ? "registers"
                : "legacy";
        }

        if (!string.IsNullOrEmpty(title.Renderer))
        {
            effective.Renderer = string.Equals(title.Renderer, RendererOpenGl, StringComparison.Ordinal)
                ? RendererOpenGl
                : RendererSoftware;
        }

        if (title.RamMb is { } ram)
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
            Renderer = Renderer,
            EosQuirk = EosQuirk,
            NetworkBackend = NetworkBackend,
            NetworkLinkUp = NetworkLinkUp,
            Accent = Accent,
            LibraryFolders = [.. LibraryFolders],
            RecentGames = [.. RecentGames],
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
        Renderer = IsGpuRenderer(Renderer) ? RendererOpenGl : RendererSoftware;
        NetworkBackend = string.Equals(NetworkBackend, "socket", StringComparison.Ordinal)
            ? "socket"
            : "loopback";
        if (string.IsNullOrWhiteSpace(Accent))
        {
            Accent = "Emerald";
        }

        LibraryFolders.RemoveAll(string.IsNullOrWhiteSpace);
        RecentGames.RemoveAll(string.IsNullOrWhiteSpace);

        var overrides = new Dictionary<string, TitleOverride>(StringComparer.OrdinalIgnoreCase);
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
            value.Renderer = string.Equals(value.Renderer, RendererOpenGl, StringComparison.Ordinal)
                ? RendererOpenGl
                : string.Equals(value.Renderer, RendererSoftware, StringComparison.Ordinal)
                    ? RendererSoftware
                    : null;
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

    /// <summary>Gets a value indicating whether a host GPU backend is requested.</summary>
    [JsonIgnore]
    public bool UsesGpuBackend => IsGpuRenderer(Renderer);

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
