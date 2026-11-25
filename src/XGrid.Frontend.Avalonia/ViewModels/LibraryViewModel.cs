using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>Library page: scans configured folders and launches disc images.</summary>
/// <remarks>Creates the library page.</remarks>
public sealed partial class LibraryViewModel(MainWindowViewModel shell) : PageViewModel(shell), IDisposable
{
    private readonly List<GameEntry> _all = [];
    private readonly CoverArtCache _covers = new();
    private readonly Dictionary<string, ScreenshotItem> _screenshotCache =
        new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "No folders configured yet.";

    [ObservableProperty]
    private bool _hasScreenshots;

    [ObservableProperty]
    private bool _isGridView = shell.Config.LibraryGridView;

    /// <summary>Gets a value indicating whether the detailed row list is shown.</summary>
    public bool IsListView => !IsGridView;

    /// <summary>Gets a value indicating whether the thumbnail tiles are on screen.</summary>
    public bool ShowGameTiles => IsGridView && !ShowEmptyState;

    /// <summary>Gets a value indicating whether the detailed rows are on screen.</summary>
    public bool ShowGameRows => IsListView && !ShowEmptyState;

    /// <summary>Gets the currently visible games.</summary>
    public ObservableCollection<GameListItem> Games { get; } = [];

    /// <summary>Gets the recent captures shown under the game list.</summary>
    public ObservableCollection<ScreenshotItem> Screenshots { get; } = [];

    /// <summary>Gets the recently launched games, newest first.</summary>
    public ObservableCollection<RecentGameItem> RecentGames { get; } = [];

    /// <summary>Gets a value indicating whether the recent section has rows.</summary>
    public bool HasRecentGames => RecentGames.Count > 0;

    /// <summary>Gets a value indicating whether the library's configured folders are empty.</summary>
    public bool HasNoFolders => Shell.Config.LibraryFolders.Count == 0;

    /// <summary>Gets a value indicating whether the visible list is empty.</summary>
    public bool IsEmpty => !IsScanning && Games.Count == 0;

    /// <summary>Gets a value indicating whether the empty state should be shown.</summary>
    public bool ShowEmptyState => IsEmpty;

    /// <summary>Gets the empty-state headline.</summary>
    public string EmptyTitle => _all.Count == 0 ? "Your library is empty" : "No matches";

    /// <summary>Gets the empty-state supporting text.</summary>
    public string EmptyDetail => _all.Count == 0
        ? "Add a folder containing Xbox ISO, CHD, or XBE files. XGrid scans subfolders too."
        : "Try a different title or format in the search box.";

    /// <summary>Rescans every configured folder.</summary>
    public async Task RefreshAsync()
    {
        string[] folders = [.. Shell.Config.LibraryFolders];
        OnPropertyChanged(nameof(HasNoFolders));
        if (folders.Length == 0)
        {
            _all.Clear();
            DisposeGames();
            _covers.Clear();
            ApplyFilter();
            IsScanning = false;
            StatusText = "No folders configured yet.";
            return;
        }

        IsScanning = true;
        StatusText = "Scanning " + folders.Length + " folder" + (folders.Length == 1 ? "…" : "s…");
        try
        {
            string coversDirectory = CoverArtIndex.DefaultDirectory();
            List<GameEntry> scanned = await Task.Run(() => GameLibraryService.Scan(folders, coversDirectory, resolveTitleIds: false));
            DisposeGames();
            _covers.Clear();
            _all.Clear();
            _all.AddRange(scanned);
            ApplyFilter();
            StatusText = _all.Count == 0
                ? "No games found. Add another folder or check the image formats."
                : $"{_all.Count} game{(_all.Count == 1 ? string.Empty : "s")} in {folders.Length} folder{(folders.Length == 1 ? string.Empty : "s")}";
            Shell.AddLog(LogLevel.Info, "Library scan: " + StatusText);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
                                              or UnauthorizedAccessException)
        {
            StatusText = "Scan failed: " + exception.Message;
            Shell.AddLog(LogLevel.Error, StatusText);
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowEmptyState));
            NotifyGameViews();
        }
    }

    /// <summary>Public command: rescans the library.</summary>
    [RelayCommand]
    private Task RescanAsync()
    {
        return RefreshAsync();
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

    /// <summary>Rebuilds the recently played list from the persisted settings.</summary>
    public void RefreshRecentGames()
    {
        DateTime now = DateTime.UtcNow;
        RecentGames.Clear();
        foreach (RecentGame game in Shell.Config.RecentGames)
        {
            if (!File.Exists(game.Path))
            {
                continue;
            }

            RecentGames.Add(new RecentGameItem(game, LaunchRecentGame, now));
        }

        OnPropertyChanged(nameof(HasRecentGames));
    }

    /// <summary>Public command: removes every recently played entry.</summary>
    [RelayCommand]
    private void ClearRecentGames()
    {
        Shell.ClearRecentGames();
    }

    private void LaunchRecentGame(RecentGame game)
    {
        if (!File.Exists(game.Path))
        {
            Shell.ForgetRecentGame(game.Path);
            Shell.ShowToast("That game is no longer available; the recent entry was removed.");
            return;
        }

        GameEntry? entry = GameEntry.FromFile(game.Path, resolveTitleId: false);
        if (entry is null)
        {
            Shell.ForgetRecentGame(game.Path);
            Shell.ShowToast("That file could not be read as a disc image.");
            return;
        }

        Shell.StartGameCommand.Execute(entry);
    }

    /// <summary>Public command: re-reads the screenshots folder.</summary>
    [RelayCommand]
    public void RefreshScreenshots()
    {
        string directory = ScreenshotNaming.Directory(SettingsStore.ResolveDirectory());
        IReadOnlyList<ScreenshotFile> files;
        try
        {
            files = ScreenshotLibrary.List(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Shell.AddLog(LogLevel.Warning, "Could not read the screenshots folder: " + exception.Message);
            return;
        }

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var next = new List<ScreenshotItem>(files.Count);
        foreach (ScreenshotFile file in files)
        {
            keep.Add(file.Path);
            if (_screenshotCache.TryGetValue(file.Path, out ScreenshotItem? existing)
                && existing.CapturedAt == file.CapturedAt)
            {
                next.Add(existing);
                continue;
            }

            existing?.Dispose();
            var item = new ScreenshotItem(file, Shell);
            _screenshotCache[file.Path] = item;
            next.Add(item);
        }

        foreach (KeyValuePair<string, ScreenshotItem> pair in _screenshotCache.ToList())
        {
            if (!keep.Contains(pair.Key))
            {
                pair.Value.Dispose();
                _screenshotCache.Remove(pair.Key);
            }
        }

        if (!next.SequenceEqual(Screenshots))
        {
            Screenshots.Clear();
            foreach (ScreenshotItem item in next)
            {
                Screenshots.Add(item);
            }
        }

        HasScreenshots = next.Count > 0;
    }

    /// <summary>Releases the decoded covers and thumbnails.</summary>
    public void Dispose()
    {
        DisposeGames();
        _covers.Dispose();
        foreach (ScreenshotItem item in _screenshotCache.Values)
        {
            item.Dispose();
        }

        _screenshotCache.Clear();
        Screenshots.Clear();
    }

    /// <summary>Public command: adds a folder to the library.</summary>
    [RelayCommand]
    private async Task AddFolderAsync()
    {
        string? folder = await Shell.Dialogs.PickFolderAsync("Choose a folder containing Xbox games");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Shell.AddLibraryFolder(folder);
        OnPropertyChanged(nameof(HasNoFolders));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsGridViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListView));
        NotifyGameViews();
        Shell.SetLibraryView(value);
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
        NotifyGameViews();
    }

    private void ApplyFilter()
    {
        string query = SearchText.Trim();
        DisposeGames();
        foreach (GameEntry game in _all)
        {
            if (query.Length == 0
                || game.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || game.FormatText.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Games.Add(new GameListItem(
                    game,
                    entry => Shell.StartGameCommand.Execute(entry),
                    _covers.Get(game.CoverPath)));
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
        NotifyGameViews();
    }

    private void NotifyGameViews()
    {
        OnPropertyChanged(nameof(ShowGameTiles));
        OnPropertyChanged(nameof(ShowGameRows));
    }

    private void DisposeGames()
    {
        Games.Clear();
    }
}
