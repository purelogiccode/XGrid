using System;

namespace XGrid.Core.Diagnostics;

/// <summary>Central logging entry point. Hot paths must check <see cref="IsEnabled"/> first.</summary>
public static class XGridLog
{
    private static ILogSink s_sink = NullLogSink.Instance;

    /// <summary>Replaces the active sink. Passing null restores the null sink.</summary>
    public static void SetSink(ILogSink? sink)
    {
        s_sink = sink ?? NullLogSink.Instance;
    }

    /// <summary>Returns whether the given category and level should be emitted.</summary>
    public static bool IsEnabled(LogCategory category, LogLevel level)
    {
        return s_sink.IsEnabled(category, level);
    }

    /// <summary>Writes a log event to the active sink.</summary>
    public static void Write(LogCategory category, LogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        s_sink.Write(category, level, message);
    }

    private sealed class NullLogSink : ILogSink
    {
        public static readonly NullLogSink Instance = new();

        public bool IsEnabled(LogCategory category, LogLevel level) => false;

        public void Write(LogCategory category, LogLevel level, string message)
        {
        }
    }
}
