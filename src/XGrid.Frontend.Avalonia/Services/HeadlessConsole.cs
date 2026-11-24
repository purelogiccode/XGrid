using System;
using System.IO;
using System.Runtime.InteropServices;

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
    public static void Attach()
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
    public static void WriteLine(string line)
    {
        try
        {
            Console.Out.WriteLine(line);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // No usable console: the summary file is the caller's fallback.
        }
    }

    /// <summary>Writes a line to standard error, ignoring a missing console.</summary>
    /// <param name="line">Line to write.</param>
    public static void WriteError(string line)
    {
        try
        {
            Console.Error.WriteLine(line);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // No usable console.
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
            // Output stays unavailable; the caller still has the summary file.
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "AttachConsole", SetLastError = true)]
    private static partial int AttachToParentConsole(int processId);
}
