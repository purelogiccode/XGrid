using System;
using Avalonia.Controls;
using XGrid.Frontend.Avalonia.ViewModels;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Console home and live display.</summary>
public partial class HomeView : UserControl
{
    /// <summary>Creates the home view and wires the native core display.</summary>
    public HomeView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            PageScroll.ScrollToHome();
            CoreDisplay.PublishGeometry();
        };
        CoreDisplay.GeometryChanged += OnCoreDisplayGeometryChanged;
        CoreDisplay.GeometryLost += OnCoreDisplayGeometryLost;
    }

    private void OnCoreDisplayGeometryChanged(object? sender, CoreDisplayGeometryEventArgs e)
    {
        // The page's DataContext is the Home view model; the shell is its parent.
        if (DataContext is HomeViewModel { Shell: { } shell })
        {
            shell.AttachCoreDisplay(e.Handle, e.Width, e.Height);
        }
    }

    private void OnCoreDisplayGeometryLost(object? sender, EventArgs e)
    {
        if (DataContext is HomeViewModel { Shell: { } shell })
        {
            shell.DetachCoreDisplay();
        }
    }
}
