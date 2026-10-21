using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Tools.FrontendSmoke;

/// <summary>
/// Headless exercise of the frontend's emulation session: boots the configured
/// disc (or the dashboard) through <see cref="EmulatorSession"/> with a chosen
/// NV2A renderer, then reports the picture, the throughput, and a save/load
/// round trip.
/// </summary>
/// <remarks>
/// <para>
/// This is the only way to reach the renderer switch without a desktop session:
/// the frontend's UI is not part of any gate, but the session code is (the GL
/// context must be created, used and disposed on the emulation thread, and a
/// snapshot request must be served there). It prints the console log lines the
/// app would show on the Activity page, so a renderer fallback (non-Windows, or
/// a failed WGL probe) is visible in the output.
/// </para>
/// <para>
/// Usage (from the repository root, after a Release build):
/// <c>dotnet run --project tools/frontend-smoke -c Release -- &lt;software|gl&gt; [seconds] [steps|--disc path]</c>.
/// Exit code 0 means at least one frame reached the UI seam.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (!TryParseArguments(args, out string renderer, out int seconds, out string? discPath, out string error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (!TryFindRepositoryRoot(out string root))
        {
            Console.Error.WriteLine("Repository root not found (no XGrid.sln above this executable).");
            return 2;
        }

        var settings = new EmulatorSettings
        {
            BiosPath = Path.Combine(root, @"data\xemu-0.8.136-windows-x86_64\Complex_4627.bin"),
            McpxRomPath = Path.Combine(root, @"data\xemu-0.8.136-windows-x86_64\mcpx_1.0.bin"),
            EepromPath = Path.Combine(root, @"data\xemu-0.8.136-windows-x86_64\eeprom.bin"),
            HddPath = Path.Combine(root, @"wt-data\chain-199l\hdd.qcow2"),
            Renderer = renderer,
            FastMode = true,
            RawBlocks = true,
            SpinSkip = true,
            DecodeCache = true,
            JitThreshold = 16,
        };
        settings.Normalize();

        if (!File.Exists(settings.BiosPath))
        {
            Console.Error.WriteLine("Gate firmware missing: " + settings.BiosPath + " (run scripts/host-doctor.ps1).");
            return 2;
        }

        using var session = new EmulatorSession();
        int frames = 0;
        byte[] last = Array.Empty<byte>();
        session.Log += (_, message) => Console.WriteLine("LOG  " + message);
        session.FrameReady += (_, packet) =>
        {
            frames++;
            last = packet.Bgra;
        };
        session.Stopped += (_, e) =>
            Console.WriteLine(
                $"STOP faulted={e.Faulted} reason={e.Message} steps={e.Steps}");

        long started = Stopwatch.GetTimestamp();
        session.Start(settings, discPath);
        while (Stopwatch.GetElapsedTime(started).TotalSeconds < seconds)
        {
            Thread.Sleep(100);
        }

        int nonBlack = 0;
        for (int index = 0; index < last.Length; index += 4)
        {
            if ((last[index] | last[index + 1] | last[index + 2]) != 0)
            {
                nonBlack++;
            }
        }

        double wall = Stopwatch.GetElapsedTime(started).TotalSeconds;
        Console.WriteLine(
            $"RESULT renderer={renderer} frames={frames} steps={session.Steps} presented={session.Frames} " +
            $"bgra={last.Length} nonBlack={nonBlack} msps={session.Steps / wall / 1_000_000.0:F1} wall={wall:F1}s");

        if (session.IsRunning)
        {
            string stateDirectory = Path.Combine(root, "wt-data", "frontend-smoke");
            Directory.CreateDirectory(stateDirectory);
            string state = Path.Combine(stateDirectory, "state-" + renderer + ".xgs");
            session.SaveState(state);
            Console.WriteLine(
                $"SAVED {state} ({new FileInfo(state).Length:N0} bytes)");
            session.LoadState(state);
            Console.WriteLine("LOADED " + state);
        }

        session.Stop();
        Thread.Sleep(500);
        Console.WriteLine($"DONE running={session.IsRunning} steps={session.Steps}");
        return frames > 0 ? 0 : 1;
    }

    private static bool TryParseArguments(
        string[] args, out string renderer, out int seconds, out string? discPath, out string error)
    {
        renderer = RendererPolicy.Software;
        seconds = 45;
        discPath = null;
        error = string.Empty;
        if (args.Length > 0)
        {
            if (!RendererPolicy.IsOpenGl(args[0]) && !string.Equals(args[0], RendererPolicy.Software, StringComparison.Ordinal))
            {
                error = "renderer must be 'software' or 'gl'";
                return false;
            }

            renderer = args[0];
        }

        if (args.Length > 1 && !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
        {
            error = "seconds must be an integer";
            return false;
        }

        if (args.Length > 2)
        {
            discPath = args[2];
        }

        return true;
    }

    private static bool TryFindRepositoryRoot(out string root)
    {
        root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "XGrid.sln")))
        {
            string? parent = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
            if (parent is null || parent.Length >= root.Length)
            {
                return false;
            }

            root = parent;
        }

        return true;
    }
}
