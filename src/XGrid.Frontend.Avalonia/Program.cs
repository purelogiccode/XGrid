using System;
using System.IO;
using Avalonia;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        LaunchArguments options = LaunchArguments.Parse(args);

        bool writesToConsole = options.Help || options.Version || options.Headless || options.HasErrors;
        if (writesToConsole)
        {
            HeadlessConsole.Attach();
        }

        if (options.Help)
        {
            HeadlessConsole.WriteLine(LaunchArguments.Usage);
            return 0;
        }

        if (options.Version)
        {
            Version version = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
            HeadlessConsole.WriteLine("XGrid v" + version.ToString(3));
            return 0;
        }

        if (options.HasErrors)
        {
            foreach (string error in options.Errors)
            {
                HeadlessConsole.WriteError("xgrid: " + error);
            }

            HeadlessConsole.WriteError("Run with --help for the command-line reference.");
            return HeadlessRunner.ConfigurationError;
        }

        if (options.DataDirectory is { Length: > 0 } dataDirectory)
        {
            Environment.SetEnvironmentVariable("XGRID_FRONTEND_DATA_DIR", Path.GetFullPath(dataDirectory));
        }

        // An explicit core path must not silently fall back to another build.
        if (options.CorePath is { Length: > 0 } corePath && !File.Exists(corePath))
        {
            HeadlessConsole.WriteError("xgrid: the emulator binary was not found: " + corePath);
            return HeadlessRunner.ConfigurationError;
        }

        if (options.Headless)
        {
            return HeadlessRunner.Run(options);
        }

        App.LaunchOptions = options;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime([.. options.Remaining]);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
