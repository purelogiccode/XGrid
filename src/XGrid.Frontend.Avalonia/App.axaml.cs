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
    /// Gets or sets the switches the shell was started with. The command line
    /// is a per-session override: it is applied to each launch and never saved
    /// back into <c>settings.json</c>.
    /// </summary>
    public static LaunchArguments LaunchOptions { get; set; } = new();

    /// <summary>
    /// Gets a value indicating whether the console should power on as soon as
    /// the shell is ready (<c>--autostart</c> or a disc on the command line).
    /// </summary>
    public static bool Autostart => LaunchOptions.Autostart;

    /// <summary>
    /// Gets a value indicating whether the shell should enter fullscreen after
    /// it opens (<c>-fullscreen</c> or <c>-f</c>).
    /// </summary>
    public static bool Fullscreen => LaunchOptions.Fullscreen;

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
                    viewModel.StartFromCommandLine,
                    DispatcherPriority.Background);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
