using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using XGrid.Frontend.Avalonia.ViewModels;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Game library.</summary>
public partial class LibraryView : UserControl
{
    /// <summary>Creates the library view.</summary>
    public LibraryView()
    {
        InitializeComponent();
        Resources["LibraryCardWidth"] = 176d;
        Resources["LibraryCardImageHeight"] = Math.Round(176 * 1.36);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel)
        {
            ApplyIconSize(viewModel.IconSize);
        }
    }

    private void OnIconSizeValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        ApplyIconSize(e.NewValue);
    }

    private void ApplyIconSize(double value)
    {
        Resources["LibraryCardWidth"] = value;
        Resources["LibraryCardImageHeight"] = Math.Round(value * 1.36);
    }

    private void OnGameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel)
        {
            viewModel.LaunchSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        viewModel.LaunchSelectedCommand.Execute(null);
        e.Handled = true;
    }
}
