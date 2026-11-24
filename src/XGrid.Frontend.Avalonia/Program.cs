using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Serilog;
using XGrid.Diagnostics;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string version = ProductVersion();
        DiagnosticsOptions diagnostics = DiagnosticsOptions.CreateDefault(version);
        try
        {
            AppDiagnostics.Initialize(diagnostics);
        }
        catch (Exception exception)
        {
            // Logging must never prevent the shell from starting.
            Debug.WriteLine("XGrid diagnostics could not be initialized: " + exception);
        }

        try
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
                HeadlessConsole.WriteLine("XGrid v" + version);
                return 0;
            }

            if (options.HasErrors)
            {
                foreach (string error in options.Errors)
                {
                    HeadlessConsole.WriteError("xgrid: " + error);
                    Log.Error("Command-line error: {Error}", error);
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
                Log.Error("The emulator binary was not found: {CorePath}", corePath);
                return HeadlessRunner.ConfigurationError;
            }

            AppDiagnostics.LogStartup(
                options.DataDirectory ?? SettingsStore.ResolveDirectory(),
                string.Join(' ', args));

            if (options.Headless)
            {
                Task statistics = ApplicationStatsClient.RecordLaunchAsync(diagnostics);
                int result = HeadlessRunner.Run(options);
                WaitBriefly(statistics);
                return result;
            }

            App.LaunchOptions = options;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([.. options.Remaining]);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "The frontend stopped unexpectedly");
            HeadlessConsole.WriteError("xgrid: fatal error: " + exception.Message);
            return 1;
        }
        finally
        {
            AppDiagnostics.Shutdown();
        }
    }

    private static string ProductVersion()
    {
        Version version = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return version.ToString(3);
    }

    private static void WaitBriefly(Task task)
    {
        try
        {
            _ = task.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception)
        {
            Log.Debug(exception, "A startup task failed");
        }
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
