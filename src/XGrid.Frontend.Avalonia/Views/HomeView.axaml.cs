using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using XGrid.Frontend.Avalonia.ViewModels;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Console view: the game picture, the transport strip and the core display.</summary>
public partial class HomeView : UserControl
{
    /// <summary>Creates the view and wires the native core display.</summary>
    public HomeView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => CoreDisplay.PublishGeometry();
        DataContextChanged += OnDataContextChanged;
        CoreDisplay.GeometryChanged += OnCoreDisplayGeometryChanged;
        CoreDisplay.GeometryLost += OnCoreDisplayGeometryLost;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is HomeViewModel { Shell: { } shell })
        {
            shell.PropertyChanged += OnShellPropertyChanged;
            ApplySampling(shell);
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SmoothFrame)
            && DataContext is HomeViewModel { Shell: { } shell })
        {
            ApplySampling(shell);
        }
    }

    private void ApplySampling(MainWindowViewModel shell)
    {
        RenderOptions.SetBitmapInterpolationMode(
            FrameImage,
            shell.SmoothFrame ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.None);
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
