using Avalonia.Controls;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Build and project information.</summary>
public partial class AboutView : UserControl
{
    /// <summary>Creates the about view.</summary>
    public AboutView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => PageScroll.ScrollToHome();
    }
}
