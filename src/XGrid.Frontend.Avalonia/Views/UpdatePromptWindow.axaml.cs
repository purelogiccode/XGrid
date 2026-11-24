using Avalonia.Controls;
using Avalonia.Interactivity;
using XGrid.Frontend.Avalonia.ViewModels;

namespace XGrid.Frontend.Avalonia.Views;

/// <summary>Asks whether to open the release page for a newer build.</summary>
public partial class UpdatePromptWindow : Window
{
    /// <summary>Creates the window. Required by the XAML loader.</summary>
    public UpdatePromptWindow()
    {
        InitializeComponent();
    }

    /// <summary>Creates the window with the release text.</summary>
    /// <param name="viewModel">The update description.</param>
    public UpdatePromptWindow(UpdatePromptViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnOpen(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnLater(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
