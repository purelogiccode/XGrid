namespace XGrid.Core.Diagnostics;

/// <summary>Receives structured log events. Implementations must be thread-safe.</summary>
public interface ILogSink
{
    /// <summary>Returns whether the given category and level should be emitted.</summary>
    bool IsEnabled(LogCategory category, LogLevel level);

    /// <summary>Writes a single log event.</summary>
    void Write(LogCategory category, LogLevel level, string message);
}
