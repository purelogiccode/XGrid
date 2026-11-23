using System;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Opt-in diagnostics for the shell's native surfaces. The activity log is the
/// product surface; this exists because an embedding failure happens before a
/// window can be inspected, and <c>XGRID_FRONTEND_TRACE=1</c> makes it visible
/// on a redirected standard error stream.
/// </summary>
internal static class FrontendTrace
{
    private static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("XGRID_FRONTEND_TRACE"), "1", StringComparison.Ordinal);

    /// <summary>Writes one diagnostic line when tracing is enabled.</summary>
    /// <param name="message">The message.</param>
    internal static void Write(string message)
    {
        if (Enabled)
        {
            Console.Error.WriteLine("[xgrid] " + message);
            Console.Error.Flush();
        }
    }
}
