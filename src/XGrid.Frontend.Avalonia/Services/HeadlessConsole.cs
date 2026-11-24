using System;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Console output for the command-line paths of a Windows GUI executable.
/// A WinExe starts without a console, so headless and help runs attach to the
/// console that launched them before writing. A failed attach (started from
/// Explorer, or output redirected to a file) is not an error; writes are then
/// dropped and the JSON summary remains the reliable channel.
/// </summary>
internal static partial class HeadlessConsole
{
    private const int AttachParentProcess = -1;
    private static bool _attached;

    /// <summary>Attaches to the parent console, once per process.</summary>
    internal static void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        if (OperatingSystem.IsWindows())
        {
            _ = AttachToParentConsole(AttachParentProcess);
            TryResetStandardStreams();
        }
    }

    /// <summary>Writes a line to standard output, ignoring a missing console.</summary>
    /// <param name="line">Line to write.</param>
    internal static void WriteLine(string line)
    {
        try
        {
            Console.Out.WriteLine(line);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            Log.Debug(exception, "No usable console for standard output");
        }
    }

    /// <summary>Writes a line to standard error, ignoring a missing console.</summary>
    /// <param name="line">Line to write.</param>
    internal static void WriteError(string line)
    {
        try
        {
            Console.Error.WriteLine(line);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            Log.Debug(exception, "No usable console for standard error");
        }
    }

    private static void TryResetStandardStreams()
    {
        // AttachConsole updates the process standard handles, but .NET may have
        // cached a null writer at startup. Re-open both streams explicitly.
        try
        {
            var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(output);
            var error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(error);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            Log.Debug(exception, "The console streams could not be reopened");
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "AttachConsole", SetLastError = true)]
    private static partial int AttachToParentConsole(int processId);
}
