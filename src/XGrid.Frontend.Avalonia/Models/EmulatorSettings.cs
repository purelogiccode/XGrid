using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using Serilog;
using XGrid.Core.Xemu;

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
    public string BiosPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional 512-byte MCPX boot ROM path.</summary>
    public string McpxRomPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional 256-byte EEPROM image path.</summary>
    public string EepromPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional hard-disk image path.</summary>
    public string HddPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the disc image inserted at power-on (ISO/XISO, CHD or ZAR).</summary>
    public string DvdPath { get; init; } = string.Empty;

    /// <summary>Gets or sets the guest RAM size in megabytes (64 retail, 128 debug kit).</summary>
    public int RamMb { get; set; } = 64;

    /// <summary>
    /// Gets or sets the presentation filter. The core applies it in its final
    /// blit; the shell never processes picture pixels.
    /// </summary>
    public string Filter { get; set; } = XemuPresentationFilter.Nearest;

    /// <summary>
    /// Gets or sets a value indicating whether the core draws the frame-rate
    /// overlay over the console picture.
    /// </summary>
    public bool ShowFpsOverlay { get; set; }

    /// <summary>
    /// Gets or sets the core hosting mode: <c>in-process</c> (experimental;
    /// the shell loads the core library) or <c>process</c> (the core runs
    /// beside the shell, the stable default).
    /// </summary>
    public string CoreHosting { get; set; } = CoreHostingInProcess;

    /// <summary>Hosting mode that loads the core into the shell's process.</summary>
    public const string CoreHostingInProcess = "in-process";

    /// <summary>Hosting mode that runs the core as a separate process.</summary>
    public const string CoreHostingProcess = "process";

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
    public string XemuCorePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the folders scanned by the game library.</summary>
    public List<string> LibraryFolders { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the library shows thumbnail
    /// tiles (the default) or the detailed row list.
    /// </summary>
    public bool LibraryGridView { get; set; } = true;

    /// <summary>Gets or sets the tile width used by the library grid.</summary>
    public double LibraryIconSize { get; set; } = 176;

    /// <summary>Gets or sets local and online cover-art options.</summary>
    public CoverSettings Covers { get; set; } = new();

    /// <summary>Gets or sets favorite library keys (title id or image path).</summary>
    public List<string> LibraryFavorites { get; set; } = [];

    /// <summary>Gets or sets accumulated play state keyed by title id or image path.</summary>
    public Dictionary<string, LibraryPlayRecord> LibraryPlayState { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the XBE title id last resolved for each image path. The
    /// library scan is metadata-only, so this cache lets favorites and play
    /// time follow the title id without opening every disc again.
    /// </summary>
    public Dictionary<string, uint> LibraryTitleIds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the most recently launched games, newest first. Entries are
    /// informational; a missing file is ignored by the Library page.
    /// </summary>
    public List<RecentGame> RecentGames { get; set; } = [];

    /// <summary>
    /// Gets or sets the email address the user donated with. It is stored only
    /// on this computer and is sent only when checking donation status.
    /// </summary>
    public string DonationEmail { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a donation was confirmed for
    /// <see cref="DonationEmail"/>. The donation banner is hidden while true.
    /// </summary>
    public bool HasDonated { get; set; }

    /// <summary>Gets or sets the UTC time of the last donation status check.</summary>
    public DateTime? DonationLastCheckedUtc { get; set; }

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
        Dictionary<string, TitleOverride> overrides = NullSafe.Value(
            TitleOverrides,
            new Dictionary<string, TitleOverride>(StringComparer.OrdinalIgnoreCase));
        if (overrides.Count == 0)
        {
            return null;
        }

        if (titleId is { } id && overrides.TryGetValue(TitleIdKey(id), out TitleOverride? byTitle))
        {
            return byTitle;
        }

        return string.IsNullOrWhiteSpace(gamePath)
            ? null
            : overrides.TryGetValue(NormalizeTitleKey(gamePath), out TitleOverride? value) ? value : null;
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
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Log.Debug(exception, "The configured path is not valid: {Path}", trimmed);
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

        if (title.RamMb is { } ram)
        {
            effective.RamMb = ram is 128 ? 128 : 64;
        }

        if (title.Renderer is { } renderer)
        {
            effective.Graphics.Renderer = renderer;
        }

        if (title.Filtering is { } filtering)
        {
            effective.Graphics.Filtering = filtering;
        }

        if (title.Filter is { } filter)
        {
            effective.Filter = filter;
        }

        // The override came from disk or the settings page, so invalid values
        // fall back to the global ones instead of failing the launch.
        effective.Normalize();
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
            ShowFpsOverlay = ShowFpsOverlay,
            CoreHosting = CoreHosting,
            Filter = Filter,
            Input = NullSafe.Value(Input, new InputSettings()).Clone(),
            Graphics = NullSafe.Value(Graphics, new GraphicsSettings()).Clone(),
            Audio = NullSafe.Value(Audio, new AudioSettings()).Clone(),
            LibraryFolders = [.. NullSafe.Value(LibraryFolders, new List<string>())],
            LibraryGridView = LibraryGridView,
            LibraryIconSize = LibraryIconSize,
            Covers = NullSafe.Value(Covers, new CoverSettings()).Clone(),
            LibraryFavorites = [.. NullSafe.Value(LibraryFavorites, new List<string>())],
            RecentGames = CloneRecentGames(),
            DonationEmail = NullSafe.Text(DonationEmail),
            HasDonated = HasDonated,
            DonationLastCheckedUtc = DonationLastCheckedUtc,
        };
        foreach (KeyValuePair<string, LibraryPlayRecord> pair in NullSafe.Value(
                     LibraryPlayState,
                     new Dictionary<string, LibraryPlayRecord>(StringComparer.OrdinalIgnoreCase)))
        {
            if (pair.Value is not null)
            {
                result.LibraryPlayState[pair.Key] = pair.Value.Clone();
            }
        }

        foreach (KeyValuePair<string, uint> pair in NullSafe.Value(
                     LibraryTitleIds,
                     new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)))
        {
            if (pair.Value != 0)
            {
                result.LibraryTitleIds[pair.Key] = pair.Value;
            }
        }
        foreach (KeyValuePair<string, TitleOverride> pair in NullSafe.Value(
                     TitleOverrides,
                     new Dictionary<string, TitleOverride>(StringComparer.OrdinalIgnoreCase)))
        {
            if (pair.Value is not null)
            {
                result.TitleOverrides[pair.Key] = pair.Value.Clone();
            }
        }

        return result;
    }

    private List<RecentGame> CloneRecentGames()
    {
        var result = new List<RecentGame>();
        foreach (RecentGame? entry in NullSafe.Value(RecentGames, new List<RecentGame>()))
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract -- settings JSON may contain null rows.
            if (entry is not null)
            {
                result.Add(entry.Clone());
            }
        }

        return result;
    }

    /// <summary>Clamps and repairs values loaded from disk.</summary>
    public void Normalize()
    {
        RamMb = RamMb is 128 ? 128 : 64;
        CoreHosting = string.Equals(CoreHosting, CoreHostingProcess, StringComparison.OrdinalIgnoreCase)
            ? CoreHostingProcess
            : CoreHostingInProcess;
        Filter = XemuPresentationFilter.Normalize(Filter);
        Input = NullSafe.Value(Input, new InputSettings());
        Graphics = NullSafe.Value(Graphics, new GraphicsSettings());
        Audio = NullSafe.Value(Audio, new AudioSettings());
        Input.Normalize();
        Graphics.Normalize();
        Audio.Normalize();
        if (string.IsNullOrWhiteSpace(Accent))
        {
            Accent = "Blue";
        }

        LibraryFolders = NullSafe.Value(LibraryFolders, []);
        LibraryFolders.RemoveAll(string.IsNullOrWhiteSpace);
        LibraryIconSize = double.IsFinite(LibraryIconSize) && LibraryIconSize > 0
            ? Math.Clamp(LibraryIconSize, 128, 384)
            : 176;
        Covers = NullSafe.Value(Covers, new CoverSettings());
        Covers.Normalize();

        var favorites = new List<string>();
        var favoriteKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? favorite in NullSafe.Value(LibraryFavorites, new List<string>()))
        {
            if (string.IsNullOrWhiteSpace(favorite))
            {
                continue;
            }

            string key = CanonicalTitleKey(favorite);
            if (key.Length > 0 && favoriteKeys.Add(key))
            {
                favorites.Add(key);
            }
        }

        LibraryFavorites = favorites;

        var playState = new Dictionary<string, LibraryPlayRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, LibraryPlayRecord> pair in NullSafe.Value(
                     LibraryPlayState,
                     new Dictionary<string, LibraryPlayRecord>(StringComparer.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            string key = CanonicalTitleKey(pair.Key);
            if (key.Length > 0)
            {
                playState[key] = pair.Value;
            }
        }

        LibraryPlayState = playState;

        var titleIds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, uint> pair in NullSafe.Value(
                     LibraryTitleIds,
                     new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)))
        {
            if (pair.Value == 0 || string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            string key = NormalizeTitleKey(pair.Key);
            if (key.Length > 0)
            {
                titleIds[key] = pair.Value;
            }
        }

        LibraryTitleIds = titleIds;
        DonationEmail = NullSafe.Text(DonationEmail).Trim();

        var recent = new List<RecentGame>(NullSafe.Value(RecentGames, []).Count);
        foreach (RecentGame? entry in NullSafe.Value(RecentGames, []))
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract -- settings JSON may contain null rows.
            if (entry is null || string.IsNullOrWhiteSpace(entry.Path))
            {
                continue;
            }

            entry.Path = entry.Path.Trim();
            entry.Title = string.IsNullOrWhiteSpace(entry.Title)
                ? Path.GetFileNameWithoutExtension(entry.Path)
                : entry.Title.Trim();
            recent.Add(entry);
        }

        recent.Sort(static (left, right) => right.LastPlayedUtc.CompareTo(left.LastPlayedUtc));
        var recentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trimmedRecent = new List<RecentGame>(Math.Min(recent.Count, RecentGame.MaxEntries));
        foreach (RecentGame entry in recent)
        {
            if (!recentPaths.Add(entry.Path))
            {
                continue;
            }

            trimmedRecent.Add(entry);
            if (trimmedRecent.Count >= RecentGame.MaxEntries)
            {
                break;
            }
        }

        RecentGames = trimmedRecent;

        var overrides = new Dictionary<string, TitleOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, TitleOverride> pair in NullSafe.Value(
                     TitleOverrides,
                     new Dictionary<string, TitleOverride>(StringComparer.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            TitleOverride value = pair.Value;
            value.Normalize();
            if (value.IsEmpty)
            {
                continue;
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
