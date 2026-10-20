using Avalonia.Controls;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Emulator configuration.</summary>
public partial class SettingsView : UserControl
{
    /// <summary>Creates the settings view.</summary>
    public SettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => PageScroll.ScrollToHome();
    }
}
