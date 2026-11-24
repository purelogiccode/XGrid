using Serilog;
using Serilog.Events;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Detailed diagnostics for the shell's native surfaces. Events go through
/// Serilog, so they appear in the rolling log file and, when tracing is
/// enabled, in the debugger.
/// </summary>
internal static class FrontendTrace
{
    /// <summary>Writes one diagnostic line at debug level.</summary>
    /// <param name="message">The message.</param>
    internal static void Write(string message)
    {
        if (Log.IsEnabled(LogEventLevel.Debug))
        {
            Log.Debug("Frontend: {Message}", message);
        }
    }
}
