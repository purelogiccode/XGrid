using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    /// <summary>Gets the currently visible games.</summary>
    public ObservableCollection<GameListItem> Games { get; } = [];

    /// <summary>Gets the recent captures shown under the game list.</summary>
    public ObservableCollection<ScreenshotItem> Screenshots { get; } = [];

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
            ApplyFilter();
            IsScanning = false;
            StatusText = "No folders configured yet.";
            return;
        }

        IsScanning = true;
        StatusText = "Scanning " + folders.Length + " folder" + (folders.Length == 1 ? "…" : "s…");
        try
        {
            List<GameEntry> scanned = await Task.Run(() => GameLibraryService.Scan(folders));
            _all.Clear();
            _all.AddRange(scanned);
            ApplyFilter();
            StatusText = _all.Count == 0
                ? "No games found. Add another folder or check the image formats."
                : $"{_all.Count} game{(_all.Count == 1 ? string.Empty : "s")} in {folders.Length} folder{(folders.Length == 1 ? string.Empty : "s")}";
            Shell.AddLog(LogLevel.Info, "Library scan: " + StatusText);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.IO.IOException
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
        }
    }

    /// <summary>Public command: rescans the library.</summary>
    [RelayCommand]
    private Task RescanAsync()
    {
        return RefreshAsync();
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
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
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

    /// <summary>Releases the decoded thumbnails.</summary>
    public void Dispose()
    {
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
    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
    }

    private void ApplyFilter()
    {
        string query = SearchText.Trim();
        Games.Clear();
        foreach (GameEntry game in _all)
        {
            if (query.Length == 0
                || game.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || game.FormatText.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Games.Add(new GameListItem(game, entry => Shell.StartGameCommand.Execute(entry)));
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDetail));
    }
}
