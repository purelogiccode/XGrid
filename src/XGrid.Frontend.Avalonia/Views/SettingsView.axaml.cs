using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XGrid.Frontend.Avalonia.ViewModels;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Emulator configuration.</summary>
public partial class SettingsView : UserControl
{
    /// <summary>Creates the settings view.</summary>
    public SettingsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => ScrollSelectedSectionToTop();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && IsVisible)
            {
                ScrollSelectedSectionToTop();
            }
        };
        SettingsTabs.SelectionChanged += (_, _) => ScrollSelectedSectionToTop();
    }

    private void ScrollSelectedSectionToTop()
    {
        if (SettingsTabs.SelectedItem is TabItem { Content: ScrollViewer scroll })
        {
            scroll.ScrollToHome();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && viewModel.CaptureKey(e.Key))
        {
            e.Handled = true;
        }
    }
}
