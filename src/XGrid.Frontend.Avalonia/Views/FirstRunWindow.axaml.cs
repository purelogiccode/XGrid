using Avalonia.Controls;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Modal first-run wizard; the view model owns the choices and the save.</summary>
public partial class FirstRunWindow : Window
{
    /// <summary>Creates the window.</summary>
    public FirstRunWindow()
    {
        InitializeComponent();
    }
}
