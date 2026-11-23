using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>A selectable activity-log severity filter.</summary>
/// <remarks>Creates a filter option.</remarks>
public sealed class LogFilterOption(string label, LogLevel? level)
{

    /// <summary>Gets the display label.</summary>
    public string Label { get; } = label;

    /// <summary>Gets the level to keep, or null for all levels.</summary>
    public LogLevel? Level { get; } = level;
}

/// <summary>Activity page: filtered emulator log with copy and clear actions.</summary>
public sealed partial class LogsViewModel : PageViewModel
{
    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private LogFilterOption? _selectedFilter;

    /// <summary>Creates the activity page.</summary>
    public LogsViewModel(MainWindowViewModel shell)
        : base(shell)
    {
        Filters =
        [
            new LogFilterOption("All levels", null),
            new LogFilterOption("Info", LogLevel.Info),
            new LogFilterOption("Warnings", LogLevel.Warning),
            new LogFilterOption("Errors", LogLevel.Error),
        ];
        _selectedFilter = Filters[0];
        shell.LogEntries.CollectionChanged += OnEntriesChanged;
        Rebuild();
    }

    /// <summary>Gets the severity filters.</summary>
    public ObservableCollection<LogFilterOption> Filters { get; }

    /// <summary>Gets the filtered entries.</summary>
    public ObservableCollection<LogEntry> Entries { get; } = [];

    /// <summary>Gets a value indicating whether the log is empty.</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>Appends a newly produced entry when it matches the current filter.</summary>
    public void NotifyEntryAdded(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (Matches(entry))
        {
            Entries.Add(entry);
            while (Entries.Count > 2000)
            {
                Entries.RemoveAt(0);
            }

            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>Public command: copies the visible log to the clipboard.</summary>
    [RelayCommand]
    private async Task CopyLogAsync()
    {
        var builder = new StringBuilder();
        foreach (LogEntry entry in Entries)
        {
            builder.Append(entry.TimeText)
                .Append(' ')
                .Append(entry.LevelText)
                .Append(' ')
                .AppendLine(entry.Message);
        }

        await Shell.Dialogs.CopyTextAsync(builder.ToString());
        Shell.ShowToast("Activity log copied");
    }

    /// <summary>Public command: clears the activity log.</summary>
    [RelayCommand]
    private void Clear()
    {
        Shell.LogEntries.Clear();
        Entries.Clear();
        OnPropertyChanged(nameof(IsEmpty));
    }

    // ReSharper disable once UnusedParameterInPartialMethod
    partial void OnSelectedFilterChanged(LogFilterOption? value)
    {
        Rebuild();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            Entries.Clear();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void Rebuild()
    {
        Entries.Clear();
        var matching = new List<LogEntry>();
        foreach (LogEntry entry in Shell.LogEntries)
        {
            if (Matches(entry))
            {
                matching.Add(entry);
            }
        }

        int start = Math.Max(0, matching.Count - 2000);
        for (int index = start; index < matching.Count; index++)
        {
            Entries.Add(matching[index]);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool Matches(LogEntry entry)
    {
        return SelectedFilter?.Level is null || entry.Level == SelectedFilter.Level;
    }
}
