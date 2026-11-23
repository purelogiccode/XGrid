using Avalonia.Controls;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Console home and live display.</summary>
public partial class HomeView : UserControl
{
    /// <summary>Creates the home view.</summary>
    public HomeView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => PageScroll.ScrollToHome();
    }
}
