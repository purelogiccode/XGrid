using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>Library page: scans folders, matches cover art, and launches games.</summary>
public sealed partial class LibraryViewModel(MainWindowViewModel shell) : PageViewModel(shell), IDisposable
{
    private readonly List<GameListItem> _all = [];
    private readonly CoverArtCache _covers = new();

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _iconSaveCts;
    private CancellationTokenSource? _searchCts;
    private CoverService? _coverService;
    private int _scanGeneration;
    private string _playSessionKey = string.Empty;
    private DateTime _playSessionStartedUtc;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _filterIndex;

    [ObservableProperty]
    private int _sortIndex;

    [ObservableProperty]
    private double _iconSize = Math.Clamp(shell.Config.LibraryIconSize, 128, 384);

    [ObservableProperty]
    private bool _isGridView = shell.Config.LibraryGridView;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isResolvingCovers;

    [ObservableProperty]
    private string _statusText = "No folders configured yet.";

    [ObservableProperty]
    private string _coverStatusText = string.Empty;

    [ObservableProperty]
    private GameListItem? _selectedGame;

    /// <summary>Gets the currently visible games.</summary>
    public ObservableCollection<GameListItem> Games { get; } = [];

    /// <summary>Gets a value indicating whether the detailed row list is shown.</summary>
    public bool IsListView => !IsGridView;

    /// <summary>Gets a value indicating whether the thumbnail tiles are on screen.</summary>
    public bool ShowGameTiles => IsGridView && !ShowEmptyState;

    /// <summary>Gets a value indicating whether the detailed rows are on screen.</summary>
    public bool ShowGameRows => IsListView && !ShowEmptyState;

    /// <summary>Gets a value indicating whether the empty state should be shown.</summary>
    public bool ShowEmptyState => !IsScanning && Games.Count == 0;

    /// <summary>Gets a value indicating whether the library's configured folders are empty.</summary>
    public bool HasNoFolders => Shell.Config.LibraryFolders.Count == 0;

    /// <summary>Gets the empty-state headline.</summary>
    public string EmptyTitle => _all.Count == 0 ? "No games found" : "No matches";

    /// <summary>Gets the empty-state supporting text.</summary>
    public string EmptyDetail => _all.Count == 0
        ? "Add a folder containing Xbox ISO, CHD, or XBE files. XGrid scans subfolders too."
        : "Try another search, change the filter, or pick a different sort order.";

    /// <summary>Gets the total library count label.</summary>
    public string CountText => _all.Count == 1 ? "1 game" : _all.Count + " games";

    /// <summary>Gets the tile width used by the grid.</summary>
    public double CardWidth => IconSize;

    /// <summary>Gets the cover-frame height used by the grid.</summary>
    public double CardImageHeight => Math.Round(IconSize * 1.36);

    /// <summary>Gets a value indicating whether the status strip should be shown.</summary>
    public bool ShowStatus => IsScanning || IsResolvingCovers || CoverStatusText.Length > 0;

    /// <summary>Rescans every configured folder and refreshes cover art.</summary>
    public async Task RefreshAsync()
    {
        string[] folders = [.. Shell.Config.LibraryFolders];
        OnPropertyChanged(nameof(HasNoFolders));

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        int generation = ++_scanGeneration;

        _coverService?.Dispose();
        _coverService = null;
        IsResolvingCovers = false;
        CoverStatusText = string.Empty;
        ClearItems();

        if (folders.Length == 0)
        {
            IsScanning = false;
            StatusText = "No folders configured yet.";
            CoverStatusText = string.Empty;
            NotifyViewState();
            return;
        }

        IsScanning = true;
        StatusText = "Scanning " + folders.Length + " folder" + (folders.Length == 1 ? "…" : "s…");
        NotifyViewState();
        try
        {
            string coverDirectory = ResolveCoverDirectory();
            var matchOptions = new CoverMatchOptions
            {
                Fuzzy = Shell.Config.Covers.FuzzyMatching,
                StripAnnotations = Shell.Config.Covers.StripAnnotations,
                Threshold = Shell.Config.Covers.FuzzyThreshold,
            };
            List<GameEntry> scanned = await Task.Run(
                    () => GameLibraryService.Scan(
                        folders,
                        coverDirectory,
                        resolveTitleIds: false,
                        matchOptions),
                    cts.Token)
                .ConfigureAwait(true);
            if (cts.IsCancellationRequested || generation != _scanGeneration)
            {
                return;
            }

            ReplaceItems(scanned);
            StatusText = scanned.Count == 0
                ? "No games found. Add another folder or check the image formats."
                : scanned.Count + " game" + (scanned.Count == 1 ? string.Empty : "s")
                  + " in " + folders.Length + " folder" + (folders.Length == 1 ? string.Empty : "s");
            Shell.AddLog(LogLevel.Info, "Library scan: " + StatusText);
            ApplyView();
            _ = ResolveOnlineCoversAsync(scanned, generation, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
                                              or UnauthorizedAccessException or ArgumentException
                                              or NotSupportedException)
        {
            StatusText = "Scan failed: " + exception.Message;
            Shell.AddLog(LogLevel.Error, StatusText);
        }
        finally
        {
            if (generation == _scanGeneration)
            {
                IsScanning = false;
                NotifyViewState();
            }
        }
    }

    /// <summary>Public command: rescans the library.</summary>
    [RelayCommand]
    private Task RescanAsync()
    {
        return RefreshAsync();
    }

    /// <summary>Public command: adds a folder and rescans.</summary>
    [RelayCommand]
    private async Task AddFolderAsync()
    {
        string? folder = await Shell.Dialogs.PickFolderAsync("Choose a folder containing Xbox games");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Shell.AddLibraryFolder(folder);
        await RefreshAsync();
    }

    /// <summary>Public command: opens the configured covers folder.</summary>
    [RelayCommand]
    private void OpenCoversFolder()
    {
        Shell.OpenCoversFolderCommand.Execute(null);
    }

    /// <summary>Public command: shows the library as thumbnail tiles.</summary>
    [RelayCommand]
    private void ShowGrid()
    {
        IsGridView = true;
    }

    /// <summary>Public command: shows the detailed row list.</summary>
    [RelayCommand]
    private void ShowList()
    {
        IsGridView = false;
    }

    /// <summary>Public command: launches the selected game.</summary>
    [RelayCommand]
    private void LaunchSelected()
    {
        SelectedGame?.LaunchCommand.Execute(null);
    }

    /// <summary>Records a successful launch and starts a play-time session.</summary>
    /// <param name="game">Launched game.</param>
    public void RecordLaunch(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        CompletePlaySession();

        game.ResolveTitleId();
        string key = game.LibraryKey;
        string pathKey = EmulatorSettings.NormalizeTitleKey(game.Path);
        if (game.HasDistinctTitleId)
        {
            Shell.Config.LibraryTitleIds[pathKey] = game.TitleId;
        }

        MigrateState(pathKey, key);

        LibraryPlayRecord record = GetOrCreatePlayRecord(key);
        record.LaunchCount++;
        record.LastPlayedUtc = DateTime.UtcNow;
        _playSessionKey = key;
        _playSessionStartedUtc = DateTime.UtcNow;

        GameListItem? item = FindItem(game.Path);
        if (item is not null && game.HasDistinctTitleId)
        {
            item.Game.SetTitleId(game.TitleId);
        }

        item?.NotifyIdentityResolved();
        item?.SetFavorite(IsFavorite(key));
        item?.SetPlayState(record);
        Shell.SaveLibraryState();
        ApplyViewIfNeeded();
    }

    /// <summary>Adds the elapsed session time to the current title's play time.</summary>
    public void CompletePlaySession()
    {
        if (_playSessionKey.Length == 0)
        {
            return;
        }

        string key = _playSessionKey;
        _playSessionKey = string.Empty;
        if (!Shell.Config.LibraryPlayState.TryGetValue(key, out LibraryPlayRecord? record))
        {
            return;
        }

        long seconds = (long)Math.Max(0, (DateTime.UtcNow - _playSessionStartedUtc).TotalSeconds);
        record.Seconds += seconds;
        GameListItem? item = _all.FirstOrDefault(candidate =>
            string.Equals(candidate.LibraryKey, key, StringComparison.OrdinalIgnoreCase));
        item?.SetPlayState(record);
        Shell.SaveLibraryState();
        ApplyViewIfNeeded();
    }

    /// <summary>Releases the scan, cover service and decoded bitmaps.</summary>
    public void Dispose()
    {
        _scanGeneration++;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        _iconSaveCts?.Cancel();
        _iconSaveCts?.Dispose();
        _iconSaveCts = null;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
        _coverService?.Dispose();
        _coverService = null;
        _covers.Dispose();
        ClearItems();
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = ApplySearchAsync(cts.Token);
    }

    private async Task ApplySearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            ApplyView();
        }
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnFilterIndexChanged(int value)
    {
        if (value is < 0 or > 3)
        {
            FilterIndex = 0;
            return;
        }

        ApplyView();
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnSortIndexChanged(int value)
    {
        if (value is < 0 or > 4)
        {
            SortIndex = 0;
            return;
        }

        ApplyView();
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIconSizeChanged(double value)
    {
        double clamped = Math.Clamp(value, 128, 384);
        if (Math.Abs(clamped - value) > 0.01)
        {
            IconSize = clamped;
            return;
        }

        OnPropertyChanged(nameof(CardWidth));
        OnPropertyChanged(nameof(CardImageHeight));
        _iconSaveCts?.Cancel();
        _iconSaveCts?.Dispose();
        var cts = new CancellationTokenSource();
        _iconSaveCts = cts;
        _ = PersistIconSizeAsync(clamped, cts.Token);
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsGridViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListView));
        NotifyViewState();
        Shell.SetLibraryView(value);
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsScanningChanged(bool value)
    {
        NotifyViewState();
        OnPropertyChanged(nameof(ShowStatus));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsResolvingCoversChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStatus));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnCoverStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowStatus));
    }

    private async Task PersistIconSizeAsync(double value, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            // The delay keeps a slider drag from writing settings.json on every tick.
            Shell.Config.LibraryIconSize = value;
            Shell.SaveLibraryState();
        }
    }

    private async Task ResolveOnlineCoversAsync(
        IReadOnlyList<GameEntry> entries,
        int generation,
        CancellationToken cancellationToken)
    {
        if (!Shell.Config.Covers.UseOnline)
        {
            return;
        }

        IsResolvingCovers = true;
        CoverStatusText = "Refreshing cover catalog…";
        try
        {
            _coverService = new CoverService(CreateCoverOptions());
            CoverCatalogRefreshResult refresh = await _coverService
                .RefreshCatalogAsync(force: false, cancellationToken)
                .ConfigureAwait(true);
            if (generation != _scanGeneration)
            {
                return;
            }

            if (!refresh.Success)
            {
                CoverStatusText = "Online covers unavailable; using local covers.";
                return;
            }

            CoverStatusText = "Cover catalog " + refresh.PackVersion
                              + " (" + refresh.CoverCount + " covers)";
            IReadOnlyList<CoverResolution> resolutions = await _coverService
                .ResolveAllAsync(entries, cancellationToken)
                .ConfigureAwait(true);
            if (generation != _scanGeneration)
            {
                return;
            }

            ApplyResolutions(resolutions);
            List<CoverResolution> missing = resolutions
                .Where(resolution => resolution.NeedsDownload)
                .ToList();
            if (missing.Count == 0 || !Shell.Config.Covers.AutoDownload)
            {
                CoverStatusText = "Covers ready.";
                return;
            }

            var progress = new Progress<CoverDownloadProgress>(value =>
            {
                if (generation == _scanGeneration)
                {
                    CoverStatusText = "Downloading covers… " + value.Completed + "/" + value.Total;
                }
            });
            CoverDownloadResult download = await _coverService
                .DownloadMissingAsync(missing, progress, cancellationToken)
                .ConfigureAwait(true);
            if (generation != _scanGeneration)
            {
                return;
            }

            ApplyDownloadedPaths(download.CoverPaths);
            CoverStatusText = download.Failed == 0
                ? "Covers: " + download.Downloaded + " downloaded, " + download.Skipped + " cached."
                : "Covers: " + download.Downloaded + " downloaded, " + download.Failed + " failed.";
            if (download.Failures.Count > 0)
            {
                Shell.AddLog(
                    LogLevel.Warning,
                    "Cover download failed for " + download.Failures[0].CoverId + ": "
                    + download.Failures[0].Message);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or InvalidDataException
                                              or ArgumentException
                                              or NotSupportedException
                                              or ObjectDisposedException)
        {
            Shell.AddLog(LogLevel.Warning, "Cover resolution failed; local covers remain available: " + exception.Message);
            CoverStatusText = "Online covers unavailable; using local covers.";
        }
        finally
        {
            if (generation == _scanGeneration)
            {
                IsResolvingCovers = false;
            }
        }
    }

    private CoverServiceOptions CreateCoverOptions()
    {
        return new CoverServiceOptions
        {
            CatalogUri = new Uri(CoverDefaults.NormalizeCatalogUrl(Shell.Config.Covers.CatalogUrl)),
            UseOnlineCovers = Shell.Config.Covers.UseOnline,
            AutoDownloadCovers = Shell.Config.Covers.AutoDownload,
            FuzzyMatching = Shell.Config.Covers.FuzzyMatching,
            AnnotationStripping = Shell.Config.Covers.StripAnnotations,
            FuzzyThreshold = Shell.Config.Covers.FuzzyThreshold,
            CoverDirectory = ResolveCoverDirectory(),
            CacheDirectory = CoverDefaults.CacheDirectory,
        };
    }

    private string ResolveCoverDirectory()
    {
        return string.IsNullOrWhiteSpace(Shell.Config.Covers.Directory)
            ? CoverDefaults.LocalDirectory
            : Shell.Config.Covers.Directory;
    }

    private void ReplaceItems(IReadOnlyList<GameEntry> entries)
    {
        _all.Clear();
        Games.Clear();
        var favorites = new HashSet<string>(
            Shell.Config.LibraryFavorites,
            StringComparer.OrdinalIgnoreCase);
        foreach (GameEntry entry in entries)
        {
            if (entry.TitleId == 0
                && Shell.Config.LibraryTitleIds.TryGetValue(
                    EmulatorSettings.NormalizeTitleKey(entry.Path),
                    out uint knownTitleId))
            {
                entry.SetTitleId(knownTitleId);
            }

            var item = new GameListItem(entry, LaunchGame, ToggleFavorite, _covers);
            item.SetFavorite(favorites.Contains(item.LibraryKey));
            if (Shell.Config.LibraryPlayState.TryGetValue(item.LibraryKey, out LibraryPlayRecord? record))
            {
                item.SetPlayState(record);
            }

            _all.Add(item);
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
        NotifyViewState();
    }

    private void ApplyResolutions(IReadOnlyList<CoverResolution> resolutions)
    {
        var byPath = new Dictionary<string, CoverResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (CoverResolution resolution in resolutions)
        {
            byPath[resolution.EntryPath] = resolution;
        }

        foreach (GameListItem item in _all)
        {
            if (byPath.TryGetValue(item.Path, out CoverResolution? resolution))
            {
                item.ApplyCoverResolution(resolution);
            }
        }
    }

    private void ApplyDownloadedPaths(IReadOnlyDictionary<string, string> paths)
    {
        foreach (GameListItem item in _all)
        {
            if (paths.TryGetValue(item.Path, out string? path))
            {
                item.SetCoverPath(path);
            }
        }
    }

    private void LaunchGame(GameEntry game)
    {
        Shell.StartGameCommand.Execute(game);
    }

    private void ToggleFavorite(GameListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        string key = item.LibraryKey;
        bool wasFavorite = IsFavorite(key);
        if (wasFavorite)
        {
            Shell.Config.LibraryFavorites.RemoveAll(candidate =>
                string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Shell.Config.LibraryFavorites.Add(key);
        }

        item.SetFavorite(!wasFavorite);
        Shell.SaveLibraryState();
        if (FilterIndex == (int)LibraryFilter.Favorites)
        {
            ApplyView();
        }
    }

    private bool IsFavorite(string key)
    {
        return Shell.Config.LibraryFavorites.Any(candidate =>
            string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));
    }

    private LibraryPlayRecord GetOrCreatePlayRecord(string key)
    {
        if (!Shell.Config.LibraryPlayState.TryGetValue(key, out LibraryPlayRecord? record))
        {
            record = new LibraryPlayRecord();
            Shell.Config.LibraryPlayState[key] = record;
        }

        return record;
    }

    private void MigrateState(string fromKey, string toKey)
    {
        if (string.IsNullOrWhiteSpace(fromKey)
            || string.Equals(fromKey, toKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Shell.Config.LibraryFavorites.RemoveAll(candidate =>
                string.Equals(candidate, fromKey, StringComparison.OrdinalIgnoreCase)) > 0
            && !IsFavorite(toKey))
        {
            Shell.Config.LibraryFavorites.Add(toKey);
        }

        if (Shell.Config.LibraryPlayState.TryGetValue(fromKey, out LibraryPlayRecord? record))
        {
            Shell.Config.LibraryPlayState.Remove(fromKey);
            if (!Shell.Config.LibraryPlayState.ContainsKey(toKey))
            {
                Shell.Config.LibraryPlayState[toKey] = record;
            }
        }
    }

    private GameListItem? FindItem(string path)
    {
        return _all.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyView()
    {
        GameListItem? selected = SelectedGame;
        List<GameListItem> visible = LibraryQuery.Apply(
            _all,
            SearchText,
            (LibraryFilter)FilterIndex,
            (LibrarySort)SortIndex);
        Games.Clear();
        foreach (GameListItem item in visible)
        {
            Games.Add(item);
        }

        if (selected is not null && !Games.Contains(selected))
        {
            SelectedGame = null;
        }

        NotifyViewState();
    }

    private void ApplyViewIfNeeded()
    {
        if (FilterIndex is (int)LibraryFilter.Favorites
                or (int)LibraryFilter.Played
                or (int)LibraryFilter.Unplayed
            || SortIndex is (int)LibrarySort.RecentlyPlayed or (int)LibrarySort.MostPlayed)
        {
            ApplyView();
        }
    }

    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(ShowGameTiles));
        OnPropertyChanged(nameof(ShowGameRows));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
        OnPropertyChanged(nameof(CountText));
    }

    private void ClearItems()
    {
        SelectedGame = null;
        Games.Clear();
        _all.Clear();
        OnPropertyChanged(nameof(CountText));
        NotifyViewState();
    }
}
