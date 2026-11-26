using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>
/// One library card or row. Holds the scanned entry, lazily decoded cover art,
/// favorite/play state and optional online-cover metadata.
/// </summary>
public sealed class GameListItem : ObservableObject
{
    private readonly Action<GameEntry> _launch;
    private readonly Action<GameListItem> _toggleFavorite;
    private readonly CoverArtCache _covers;

    private Bitmap? _cover;
    private string? _coverPath;
    private bool _coverRequested;
    private bool _isFavorite;
    private long _playtimeSeconds;
    private DateTime? _lastPlayedUtc;
    private int _launchCount;
    private string _regions = string.Empty;
    private string _year = string.Empty;
    private string _publisher = string.Empty;
    private string _developer = string.Empty;
    private string _genre = string.Empty;
    private string _description = string.Empty;
    private string _coverSource = string.Empty;
    private string _matchedCoverName = string.Empty;
    private double _coverScore;
    private int _players;

    /// <summary>Creates an item for a scanned game.</summary>
    /// <param name="game">Scanned entry.</param>
    /// <param name="launch">Launch callback.</param>
    /// <param name="toggleFavorite">Favorite callback.</param>
    /// <param name="covers">Shared cover decoder.</param>
    public GameListItem(
        GameEntry game,
        Action<GameEntry> launch,
        Action<GameListItem> toggleFavorite,
        CoverArtCache covers)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(toggleFavorite);
        ArgumentNullException.ThrowIfNull(covers);
        Game = game;
        _launch = launch;
        _toggleFavorite = toggleFavorite;
        _covers = covers;
        LaunchCommand = new RelayCommand(() => _launch(Game));
        ToggleFavoriteCommand = new RelayCommand(() => _toggleFavorite(this));

        CoverAnnotations annotations = CoverMatcher.ParseAnnotations(
            System.IO.Path.GetFileNameWithoutExtension(game.Path));
        _regions = string.Join(", ", annotations.Regions);
        _year = annotations.Year;
        _coverPath = game.CoverPath;
        if (!string.IsNullOrWhiteSpace(_coverPath))
        {
            StartCoverLoad();
        }
    }

    /// <summary>Gets the scanned entry.</summary>
    public GameEntry Game { get; }

    /// <summary>Gets the launch command.</summary>
    public IRelayCommand LaunchCommand { get; }

    /// <summary>Gets the favorite toggle command.</summary>
    public IRelayCommand ToggleFavoriteCommand { get; }

    /// <summary>Gets the full image path.</summary>
    public string Path => Game.Path;

    /// <summary>Gets the display title with region/language annotations removed.</summary>
    public string Title => CoverMatcher.StripAnnotations(Game.Title);

    /// <summary>Gets the container format label.</summary>
    public string FormatText => Game.FormatText;

    /// <summary>Gets the formatted image size.</summary>
    public string SizeText => Game.SizeText;

    /// <summary>Gets the certificate title id label, or an empty string.</summary>
    public string TitleIdText => Game.TitleIdText;

    /// <summary>Gets the short eight-character title id, or an empty string.</summary>
    public string ShortTitleIdText => Game.HasDistinctTitleId
        ? Game.TitleId.ToString("X8", CultureInfo.InvariantCulture)
        : string.Empty;

    /// <summary>Gets a value indicating whether the game has a distinct title id.</summary>
    public bool HasDistinctTitleId => Game.HasDistinctTitleId;

    /// <summary>Gets the containing folder.</summary>
    public string Folder => Game.Folder;

    /// <summary>Gets a value indicating whether the image can be inserted as a disc.</summary>
    public bool IsLaunchable => Game.IsLaunchable;

    /// <summary>Gets a value indicating whether the entry is a loose XBE.</summary>
    public bool IsExecutable => Game.Format == GameFormat.Xbe;

    /// <summary>Gets the stable library-state key.</summary>
    public string LibraryKey => Game.LibraryKey;

    /// <summary>Gets the decoded cover, or null while it is loading or absent.</summary>
    public Bitmap? Cover => _cover;

    /// <summary>Gets a value indicating whether a cover bitmap is ready.</summary>
    public bool HasCover => _cover is not null;

    /// <summary>Gets the current cover path, or null.</summary>
    public string? CoverPath => _coverPath;

    /// <summary>Gets a value indicating whether the title is a favorite.</summary>
    public bool IsFavorite => _isFavorite;

    /// <summary>Gets the favorite glyph shown on the card.</summary>
    public string FavoriteGlyph => _isFavorite ? "★" : "☆";

    /// <summary>Gets the accumulated play time in seconds.</summary>
    public long PlaytimeSeconds => _playtimeSeconds;

    /// <summary>Gets the compact play-time label, or an empty string.</summary>
    public string PlaytimeText => FormatPlaytime(_playtimeSeconds);

    /// <summary>Gets the last-played timestamp label, or an empty string.</summary>
    public string LastPlayedText => _lastPlayedUtc is null
        ? string.Empty
        : _lastPlayedUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Gets the last-played UTC timestamp, or null.</summary>
    public DateTime? LastPlayedUtc => _lastPlayedUtc;

    /// <summary>Gets the launch count.</summary>
    public int LaunchCount => _launchCount;

    /// <summary>Gets the region codes from the game name or cover catalog.</summary>
    public string Regions => _regions;

    /// <summary>Gets the release year from the game name or cover catalog.</summary>
    public string Year => _year;

    /// <summary>Gets the publisher metadata, or an empty string.</summary>
    public string Publisher => _publisher;

    /// <summary>Gets the developer metadata, or an empty string.</summary>
    public string Developer => _developer;

    /// <summary>Gets the genre metadata, or an empty string.</summary>
    public string Genre => _genre;

    /// <summary>Gets the description metadata, or an empty string.</summary>
    public string Description => _description;

    /// <summary>Gets the player-count metadata, or zero when unknown.</summary>
    public int Players => _players;

    /// <summary>Gets the compact metadata line.</summary>
    public string MetadataText
    {
        get
        {
            var parts = new List<string>(5);
            Add(parts, _year);
            Add(parts, _publisher);
            Add(parts, _developer);
            Add(parts, _genre);
            Add(parts, _regions);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Gets the detailed metadata line used by tooltips.</summary>
    public string DetailText
    {
        get
        {
            var parts = new List<string>(6);
            Add(parts, MetadataText);
            if (_players > 0)
            {
                Add(parts, _players.ToString(CultureInfo.InvariantCulture) + " players");
            }

            Add(parts, PlaytimeText.Length == 0 ? string.Empty : "Played " + PlaytimeText);
            Add(parts, _launchCount > 0 ? "Launched " + _launchCount + " times" : string.Empty);
            Add(parts, _description);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Gets the hover tooltip with path, image details and cover provenance.</summary>
    public string Tip
    {
        get
        {
            string tip = Path + Environment.NewLine
                         + FormatText + " · " + SizeText;
            if (HasDistinctTitleId)
            {
                tip += Environment.NewLine + TitleIdText;
            }

            if (MetadataText.Length > 0)
            {
                tip += Environment.NewLine + MetadataText;
            }

            if (PlaytimeText.Length > 0)
            {
                tip += Environment.NewLine + "Played " + PlaytimeText;
            }

            if (LastPlayedText.Length > 0)
            {
                tip += Environment.NewLine + "Last played " + LastPlayedText;
            }

            if (_coverSource.Length > 0 && !string.Equals(_coverSource, "none", StringComparison.Ordinal))
            {
                tip += Environment.NewLine + "Cover: " + _coverSource;
                if (_matchedCoverName.Length > 0)
                {
                    tip += " (" + _matchedCoverName + ", " + _coverScore.ToString("P0", CultureInfo.CurrentCulture) + ")";
                }
            }

            return tip;
        }
    }

    /// <summary>Replaces the local cover path and starts a background decode.</summary>
    /// <param name="path">Local cover path, or null to clear it.</param>
    public void SetCoverPath(string? path)
    {
        if (string.Equals(_coverPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _coverPath = path;
        _cover = null;
        _coverRequested = false;
        OnPropertyChanged(nameof(CoverPath));
        OnPropertyChanged(nameof(Cover));
        OnPropertyChanged(nameof(HasCover));
        OnPropertyChanged(nameof(Tip));
        if (!string.IsNullOrWhiteSpace(path))
        {
            StartCoverLoad();
        }
    }

    /// <summary>Updates favorite state and notifies the card.</summary>
    /// <param name="favorite">True when the title is a favorite.</param>
    public void SetFavorite(bool favorite)
    {
        if (_isFavorite == favorite)
        {
            return;
        }

        _isFavorite = favorite;
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteGlyph));
        OnPropertyChanged(nameof(Tip));
    }

    /// <summary>Updates accumulated play state and notifies the card.</summary>
    /// <param name="record">Play record, or null for a new title.</param>
    public void SetPlayState(LibraryPlayRecord? record)
    {
        _playtimeSeconds = record?.Seconds ?? 0;
        _lastPlayedUtc = record?.LastPlayedUtc;
        _launchCount = record?.LaunchCount ?? 0;
        OnPropertyChanged(nameof(PlaytimeSeconds));
        OnPropertyChanged(nameof(PlaytimeText));
        OnPropertyChanged(nameof(LastPlayedText));
        OnPropertyChanged(nameof(LaunchCount));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(Tip));
    }

    /// <summary>Applies a cover resolution and its optional metadata.</summary>
    /// <param name="resolution">Resolved cover.</param>
    public void ApplyCoverResolution(CoverResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        _coverSource = resolution.CoverSource;
        _matchedCoverName = resolution.MatchedName;
        _coverScore = resolution.Score;
        if (!string.IsNullOrWhiteSpace(resolution.Regions))
        {
            _regions = resolution.Regions;
        }

        if (!string.IsNullOrWhiteSpace(resolution.Year))
        {
            _year = resolution.Year;
        }

        if (!string.IsNullOrWhiteSpace(resolution.Publisher))
        {
            _publisher = resolution.Publisher;
        }

        if (!string.IsNullOrWhiteSpace(resolution.Developer))
        {
            _developer = resolution.Developer;
        }

        if (!string.IsNullOrWhiteSpace(resolution.Genre))
        {
            _genre = resolution.Genre;
        }

        if (!string.IsNullOrWhiteSpace(resolution.Description))
        {
            _description = resolution.Description;
        }

        if (resolution.Players > 0)
        {
            _players = resolution.Players;
        }

        if (!resolution.NeedsDownload)
        {
            SetCoverPath(resolution.CoverPath);
        }

        OnPropertyChanged(nameof(MetadataText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(Tip));
    }

    /// <summary>Notifies bindings after the underlying title id was resolved.</summary>
    public void NotifyIdentityResolved()
    {
        OnPropertyChanged(nameof(TitleIdText));
        OnPropertyChanged(nameof(ShortTitleIdText));
        OnPropertyChanged(nameof(HasDistinctTitleId));
        OnPropertyChanged(nameof(LibraryKey));
        OnPropertyChanged(nameof(Tip));
    }

    private void StartCoverLoad()
    {
        if (_coverRequested || string.IsNullOrWhiteSpace(_coverPath))
        {
            return;
        }

        _coverRequested = true;
        _ = LoadCoverAsync(_coverPath);
    }

    private async Task LoadCoverAsync(string path)
    {
        Bitmap? cover = await _covers.LoadAsync(path).ConfigureAwait(true);
        if (!string.Equals(_coverPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _cover = cover;
        OnPropertyChanged(nameof(Cover));
        OnPropertyChanged(nameof(HasCover));
    }

    private static string FormatPlaytime(long seconds)
    {
        if (seconds <= 0)
        {
            return string.Empty;
        }

        if (seconds >= 3600)
        {
            return (seconds / 3600).ToString(CultureInfo.InvariantCulture)
                   + "h " + (seconds % 3600 / 60).ToString("00", CultureInfo.InvariantCulture) + "m";
        }

        return seconds >= 60
            ? (seconds / 60).ToString(CultureInfo.InvariantCulture) + "m"
            : seconds.ToString(CultureInfo.InvariantCulture) + "s";
    }

    private static void Add(List<string> parts, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value.Trim());
        }
    }
}
