using System;
using Avalonia;

namespace XGrid.Frontend.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        LaunchArguments options = LaunchArguments.Parse(args);
        App.Autostart = options.Autostart;
        App.Fullscreen = options.Fullscreen;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime([.. options.Remaining]);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
