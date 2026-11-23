using System;
using System.Collections.Generic;
using Avalonia;

namespace XGrid.Frontend.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var remaining = new List<string>(args.Length);
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase))
            {
                App.Autostart = true;
                continue;
            }

            remaining.Add(arg);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(remaining.ToArray());
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
