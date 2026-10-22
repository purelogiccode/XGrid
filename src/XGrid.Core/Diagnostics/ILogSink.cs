namespace XGrid.Core.Diagnostics;

/// <summary>Receives structured log events. Implementations must be thread-safe.</summary>
public interface ILogSink
{
    /// <summary>Returns whether the given category and level should be emitted.</summary>
    // The level parameter is part of the contract even when an implementation
    // filters on category alone.
    // ReSharper disable UnusedParameter.Global
    bool IsEnabled(LogCategory category, LogLevel level);
    // ReSharper restore UnusedParameter.Global

    /// <summary>Writes a single log event.</summary>
    void Write(LogCategory category, LogLevel level, string message);
}
