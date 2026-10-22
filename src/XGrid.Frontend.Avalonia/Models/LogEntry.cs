using System;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>Severity of a frontend or emulator log message.</summary>
public enum LogLevel
{
    /// <summary>Informational progress.</summary>
    Info,

    /// <summary>A recoverable condition worth attention.</summary>
    Warning,

    /// <summary>A failure.</summary>
    Error,

    /// <summary>Detailed diagnostic text.</summary>
    Debug,
}

/// <summary>One timestamped line in the activity log.</summary>
/// <remarks>Creates a log entry.</remarks>
public sealed class LogEntry(DateTimeOffset timestamp, LogLevel level, string message)
{

    /// <summary>Gets the creation timestamp.</summary>
    private DateTimeOffset Timestamp { get; } = timestamp;

    /// <summary>Gets the severity.</summary>
    public LogLevel Level { get; } = level;

    /// <summary>Gets the message text.</summary>
    public string Message { get; } = message;

    /// <summary>Gets the local-time clock label.</summary>
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gets the severity label.</summary>
    public string LevelText => Level switch
    {
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Debug => "DEBUG",
        _ => "INFO",
    };
}
