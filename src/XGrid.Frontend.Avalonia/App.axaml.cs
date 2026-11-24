using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using XGrid.Frontend.Avalonia.ViewModels;
using XGrid.Frontend.Avalonia.Views;

namespace XGrid.Frontend.Avalonia;

public class App : Application
{
    /// <summary>
    /// Gets or sets a value indicating whether the console should power on as
    /// soon as the shell is ready (<c>--autostart</c> on the command line).
    /// </summary>
    public static bool Autostart { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the shell should enter
    /// fullscreen after it opens (<c>-fullscreen</c> or <c>-f</c>).
    /// </summary>
    public static bool Fullscreen { get; set; }

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

            if (Fullscreen)
            {
                // The window state is only reliable once the platform window
                // exists, so enter fullscreen after the first open.
                window.Opened += (_, _) => window.EnterFullscreen();
            }

            if (Autostart)
            {
                Dispatcher.UIThread.Post(
                    () => viewModel.TogglePowerCommand.Execute(null),
                    DispatcherPriority.Background);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
