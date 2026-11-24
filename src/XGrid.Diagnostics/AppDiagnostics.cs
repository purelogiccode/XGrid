using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace XGrid.Diagnostics;

/// <summary>
/// Configures the process-wide Serilog logger: rolling files, the debugger,
/// an optional console and the bug-report sink for warnings and errors.
/// </summary>
public static class AppDiagnostics
{
    private static readonly Lock Gate = new();
    private static Logger? _logger;
    private static DiagnosticsOptions? _options;
    private static string? _logDirectory;
    private static bool _handlersInstalled;

    /// <summary>Gets the options used by the active logger.</summary>
    public static DiagnosticsOptions Options =>
        _options ?? throw new InvalidOperationException("AppDiagnostics has not been initialized.");

    /// <summary>Gets the directory that receives the rolling log files.</summary>
    public static string LogDirectory =>
        _logDirectory ?? throw new InvalidOperationException("AppDiagnostics has not been initialized.");

    /// <summary>Configures Serilog once for this process.</summary>
    /// <param name="options">The application identity and service options.</param>
    /// <param name="console">Whether to mirror events to the console.</param>
    public static void Initialize(DiagnosticsOptions options, bool console = false)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (Gate)
        {
            if (_logger is not null)
            {
                return;
            }

            _options = options;
            SelfLog.Enable(message => Debug.WriteLine("[xgrid-serilog] " + message));
            InstallGlobalExceptionHandlers();

            LogEventLevel minimum = ResolveMinimumLevel();
            string directory = ResolveLogDirectory(options.LogDirectory);
            _logDirectory = directory;

            LoggerConfiguration configuration = new LoggerConfiguration()
                .MinimumLevel.Is(minimum)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", options.ApplicationName)
                .WriteTo.Debug(
                    outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");

            if (!string.IsNullOrWhiteSpace(directory))
            {
                configuration = configuration.WriteTo.File(
                    Path.Combine(directory, "xgrid-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
            }

            if (console)
            {
                // Command-line output is the product surface; the console sink
                // only carries warnings and errors so normal command results
                // stay free of log prefixes.
                configuration = configuration.WriteTo.Console(
                    outputTemplate: "{Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Warning,
                    standardErrorFromLevel: LogEventLevel.Warning);
            }

            if (options.BugReportsEnabled)
            {
                configuration = configuration.WriteTo.Sink(
                    new BugReportSink(options),
                    restrictedToMinimumLevel: LogEventLevel.Warning);
            }

            _logger = configuration.CreateLogger();
            Log.Logger = _logger;
            Log.Information(
                "Starting {Application} {Version} on {OS} ({Architecture}) from {BaseDirectory}",
                options.ApplicationName,
                options.Version,
                Environment.OSVersion.VersionString,
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
                AppContext.BaseDirectory);
        }
    }

    /// <summary>Writes a startup line describing the data folder and options.</summary>
    /// <param name="dataDirectory">The per-user data folder, when known.</param>
    /// <param name="commandLine">The command line, when known.</param>
    public static void LogStartup(string? dataDirectory, string? commandLine)
    {
        Log.Information(
            "Data directory {DataDirectory}; command line {CommandLine}",
            dataDirectory ?? "(default)",
            commandLine ?? "(none)");
    }

    /// <summary>Flushes queued log events and pending bug reports.</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            if (_logger is null)
            {
                return;
            }

            Log.Information("Stopping {Application}", _options?.ApplicationName ?? "application");
            Log.CloseAndFlush();
            _logger = null;
            _options = null;
            _logDirectory = null;
        }
    }

    private static void InstallGlobalExceptionHandlers()
    {
        if (_handlersInstalled)
        {
            return;
        }

        _handlersInstalled = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Unhandled application exception");
            }
            else
            {
                Log.Fatal("Unhandled application exception: {ExceptionObject}", args.ExceptionObject);
            }

            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private static LogEventLevel ResolveMinimumLevel()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("XGRID_FRONTEND_TRACE"),
                "1",
                StringComparison.Ordinal))
        {
            return LogEventLevel.Debug;
        }

        string? configured = Environment.GetEnvironmentVariable("XGRID_LOG_LEVEL");
        if (!string.IsNullOrWhiteSpace(configured)
            && Enum.TryParse(configured, ignoreCase: true, out LogEventLevel level))
        {
            return level;
        }

#if DEBUG
        return LogEventLevel.Debug;
#else
        return LogEventLevel.Information;
#endif
    }

    private static string ResolveLogDirectory(string? configured)
    {
        string directory = string.IsNullOrWhiteSpace(configured)
            ? DiagnosticsOptions.DefaultLogDirectory
            : configured;

        try
        {
            _ = Directory.CreateDirectory(directory);
            return directory;
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException)
        {
            // A read-only installation must not prevent the application from
            // starting; the debugger sink still carries the events.
            Debug.WriteLine("[xgrid-serilog] Cannot create log directory: " + exception.Message);
            return string.Empty;
        }
    }
}
