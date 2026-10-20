using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.ViewModels;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Activity log with filtered entries and auto-scroll.</summary>
public partial class LogsView : UserControl
{
    private LogsViewModel? _viewModel;

    /// <summary>Creates the activity view.</summary>
    public LogsView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Entries.CollectionChanged -= OnEntriesChanged;
        }

        _viewModel = DataContext as LogsViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Entries.CollectionChanged += OnEntriesChanged;
        }

        base.OnDataContextChanged(e);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_viewModel is not { AutoScroll: true } viewModel || viewModel.Entries.Count == 0)
        {
            return;
        }

        LogEntry? last = viewModel.Entries[^1];
        Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(last));
    }
}
