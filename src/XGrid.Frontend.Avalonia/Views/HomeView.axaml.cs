using System;
using Avalonia.Controls;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.ViewModels;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Console view: the game picture, the transport strip and the core display.</summary>
public partial class HomeView : UserControl
{
    private readonly XemuDisplayHost? _coreDisplay;

    /// <summary>Creates the view and wires the native core display.</summary>
    public HomeView()
    {
        InitializeComponent();

        // Only Windows has a native child-window embedding implementation. On
        // other hosts the core opens its own SDL window and the page explains
        // where the picture is; it must never construct a Win32 surface.
        if (XemuWindowEmbedder.IsSupported)
        {
            _coreDisplay = new XemuDisplayHost();
            _coreDisplay.GeometryChanged += OnCoreDisplayGeometryChanged;
            _coreDisplay.GeometryLost += OnCoreDisplayGeometryLost;
            CoreDisplayContainer.Children.Add(_coreDisplay);
        }

        AttachedToVisualTree += (_, _) => _coreDisplay?.PublishGeometry();
        DataContextChanged += (_, _) => _coreDisplay?.PublishGeometry();
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
