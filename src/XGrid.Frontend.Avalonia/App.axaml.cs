using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XGrid.Frontend.Avalonia.ViewModels;
using XGrid.Frontend.Avalonia.Views;

namespace XGrid.Frontend.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            var window = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = window;
            viewModel.Attach(window);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
