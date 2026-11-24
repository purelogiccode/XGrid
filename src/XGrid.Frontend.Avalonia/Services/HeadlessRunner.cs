using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Serilog;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Models;
using XGrid.XboxFs;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Runs the console without the Avalonia shell, for automation and benchmark
/// jobs. The core still renders off-screen: the host display sink publishes
/// frames into a shared section, and this runner counts them without
/// presenting. The run ends on a wall-clock limit, a frame limit, a guest
/// power-off or Ctrl+C.
/// </summary>
public static class HeadlessRunner
{
    /// <summary>The run completed and the core shut down cleanly.</summary>
    public const int Success = 0;

    /// <summary>The core failed to start or ended with an error.</summary>
    public const int CoreFailure = 1;

    /// <summary>The settings, firmware or media could not be used.</summary>
    public const int ConfigurationError = 2;

    /// <summary>The core ran but published no frames (HEADLESS without a picture).</summary>
    public const int NoFrames = 3;

    /// <summary>Runs the console headlessly and prints one report line per field.</summary>
    /// <param name="options">Parsed command-line switches, without errors.</param>
    /// <returns>A process exit code; see the constants on this class.</returns>
    public static int Run(LaunchArguments options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.HasErrors)
        {
            foreach (string error in options.Errors)
            {
                HeadlessConsole.WriteError("xgrid: " + error);
            }

            return ConfigurationError;
        }

        EmulatorSettings stored = new SettingsStore().Load();
        if (options.CorePath is { Length: > 0 } corePath && !File.Exists(corePath))
        {
            return Fail("The emulator binary was not found: " + corePath);
        }

        string? disc = options.DiscPath
                       ?? (string.IsNullOrWhiteSpace(stored.DvdPath) ? null : stored.DvdPath);
        if (disc is not null && !File.Exists(disc))
        {
            return Fail("The disc image does not exist: " + disc);
        }

        if (disc is not null
            && Path.GetExtension(disc).Equals(".xbe", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("A loose .xbe is not bootable; wrap it in an ISO or CHD image.");
        }

        EmulatorSettings settings = BuildEffectiveSettings(stored, disc, options);
        if (options.AudioBackend is null)
        {
            // A headless run has no device to play into; tests and benchmarks
            // should not depend on the host audio device.
            settings.Audio.Backend = AudioSettings.BackendNone;
        }

        string workingDirectory = Path.Combine(SettingsStore.ResolveDirectory(), "xemu");
        string? problem = XemuLaunchPlan.DescribeProblem(settings, disc, workingDirectory);
        if (problem is not null)
        {
            return Fail(problem);
        }

        (int width, int height) = ResolveWindowSize(settings.Graphics.StartupSize);
        XemuLaunchSettings launch = XemuLaunchPlan.Create(settings, disc, width, height, workingDirectory)
            with
        {
            // Headless always needs the frame sink, including on hosts where
            // the shell's normal presentation path is not available.
            DisplaySinkName = XemuDisplaySink.CreateName(),
            InputSinkName = string.Empty,
            VSync = false,
            FullscreenOnStartup = false,
            FullscreenExclusive = false,
        };

        return Execute(options, settings, launch, width, height);
    }

    private static EmulatorSettings BuildEffectiveSettings(
        EmulatorSettings stored,
        string? disc,
        LaunchArguments options)
    {
        EmulatorSettings settings;
        if (disc is not null && DiscTitleId.TryRead(disc, out uint titleId, out _) && titleId != 0)
        {
            settings = stored.WithTitleOverride(disc, titleId);
        }
        else if (disc is not null)
        {
            settings = stored.WithTitleOverride(disc);
        }
        else
        {
            settings = stored.Clone();
        }

        // The command line has the last word, including over a title override.
        options.ApplyTo(settings);
        return settings;
    }

    private static int Execute(
        LaunchArguments options,
        EmulatorSettings settings,
        XemuLaunchSettings launch,
        int width,
        int height)
    {
        TimeSpan? duration = options.Seconds is { } secondsLimit
            ? TimeSpan.FromSeconds(secondsLimit)
            : options.Frames is null
                ? TimeSpan.FromSeconds(LaunchArguments.DefaultHeadlessSeconds)
                : null;
        long frameLimit = options.Frames ?? 0;

        using var session = new XemuCoreSession(readFrames: false);
        using var sink = new XemuDisplaySink(launch.DisplaySinkName);
        using var exited = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();
        XemuCoreStoppedEventArgs? stopped = null;
        long frames = 0;
        ulong lastCounter = 0;
        double firstFrameSeconds = 0;
        double lastFrameSeconds = 0;
        bool cancel = false;

        void CountFrames()
        {
            if (!sink.TryReadCounter(out ulong counter))
            {
                return;
            }

            // The core resets the counter when it recreates the section for a
            // new geometry. Treat that as starting from zero, not as a wrap.
            frames += counter >= lastCounter
                ? (long)(counter - lastCounter)
                : (long)counter;
            lastCounter = counter;

            if (frames > 0)
            {
                double now = clock.Elapsed.TotalSeconds;
                if (firstFrameSeconds == 0)
                {
                    firstFrameSeconds = now;
                }

                lastFrameSeconds = now;
            }
        }

        session.Stopped += (_, args) =>
        {
            stopped = args;
            exited.Set();
        };
        session.Log += (_, message) => HeadlessConsole.WriteLine("xgrid: " + message);

        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            cancel = true;
        };

        try
        {
            Console.CancelKeyPress += cancelHandler;
        }
        catch (Exception exception) when (exception is IOException
                                              or InvalidOperationException
                                              or PlatformNotSupportedException)
        {
            Log.Debug(exception, "The headless run cannot receive Ctrl+C");
        }

        try
        {
            try
            {
                session.Start(launch);
            }
            catch (XemuConfigurationException exception)
            {
                Log.Error(exception, "The headless core configuration is invalid");
                return Fail(exception.Message);
            }
            catch (Exception exception) when (exception is IOException
                                                  or UnauthorizedAccessException
                                                  or InvalidOperationException)
            {
                Log.Error(exception, "The headless emulator could not start");
                HeadlessConsole.WriteError("xgrid: the emulator could not start: " + exception.Message);
                return CoreFailure;
            }

            string stop = "core";
            while (!exited.IsSet)
            {
                CountFrames();
                if (cancel)
                {
                    stop = "cancel";
                    break;
                }

                if (frameLimit > 0 && frames >= frameLimit)
                {
                    stop = "frames";
                    break;
                }

                if (duration is { } limit && clock.Elapsed >= limit)
                {
                    stop = "duration";
                    break;
                }

                if (!session.IsRunning)
                {
                    break;
                }

                // Poll faster near a frame limit so a short run does not
                // overshoot by several frames at high presentation rates.
                int delay = frameLimit > 0 && frameLimit - frames <= 3 ? 1 : 10;
                Thread.Sleep(delay);
            }

            session.Stop();
            if (!exited.Wait(TimeSpan.FromSeconds(12)))
            {
                HeadlessConsole.WriteError("xgrid: the emulator did not shut down in time.");
                return CoreFailure;
            }

            CountFrames();
            double seconds = clock.Elapsed.TotalSeconds;
            long presented = frames;
            double frameWindow = lastFrameSeconds - firstFrameSeconds;
            double fps = presented > 1 && frameWindow > 0
                ? (presented - 1) / frameWindow
                : seconds > 0
                    ? presented / seconds
                    : 0;
            int exitCode = stopped?.ExitCode ?? -1;
            int result = Success;
            if (stopped is null || stopped.Faulted || exitCode != 0)
            {
                result = CoreFailure;
            }
            else if (presented == 0
                     && !string.Equals(launch.Renderer, GraphicsSettings.RendererNull, StringComparison.Ordinal)
                     && !string.Equals(stop, "cancel", StringComparison.Ordinal))
            {
                result = NoFrames;
            }

            var report = new HeadlessReport
            {
                Core = launch.CorePath,
                Disc = string.IsNullOrEmpty(launch.DvdPath) ? null : launch.DvdPath,
                Renderer = launch.Renderer,
                SurfaceScale = launch.SurfaceScale,
                Filtering = launch.Filtering,
                PresentationFilter = settings.Filter,
                Width = width,
                Height = height,
                Audio = launch.AudioBackend,
                Frames = presented,
                Seconds = seconds,
                FirstFrameSeconds = firstFrameSeconds,
                Fps = fps,
                Stop = stop,
                ExitCode = exitCode,
                Result = result,
            };

            HeadlessConsole.WriteLine(report.ToText().TrimEnd());
            if (options.SummaryPath is { } summaryPath
                && !TryWriteSummary(summaryPath, report.ToJson()))
            {
                return CoreFailure;
            }

            if (result == NoFrames)
            {
                HeadlessConsole.WriteError(
                    "xgrid: the core published no frames; check the renderer and the core build.");
            }

            return result;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static bool TryWriteSummary(string path, string json)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                _ = Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json);
            HeadlessConsole.WriteLine("xgrid: summary=" + path);
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException)
        {
            Log.Error(exception, "Could not write the headless summary to {Path}", path);
            HeadlessConsole.WriteError("xgrid: could not write the summary: " + exception.Message);
            return false;
        }
    }

    private static (int Width, int Height) ResolveWindowSize(string startupSize)
    {
        int separator = startupSize.IndexOf('x', StringComparison.Ordinal);
        if (separator > 0
            && int.TryParse(
                startupSize.AsSpan(0, separator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int width)
            && int.TryParse(
                startupSize.AsSpan(separator + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int height)
            && width >= 64
            && height >= 64)
        {
            return (width, height);
        }

        // "last_used" has no prior session in a fresh automation folder.
        return (1280, 960);
    }

    private static int Fail(string message)
    {
        HeadlessConsole.WriteError("xgrid: " + message);
        return ConfigurationError;
    }
}
