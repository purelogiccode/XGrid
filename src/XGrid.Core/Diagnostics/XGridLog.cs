using System;

namespace XGrid.Core.Diagnostics;

/// <summary>Central logging entry point. Hot paths must check <see cref="IsEnabled"/> first.</summary>
public static class XGridLog
{
    private static ILogSink _sSink = NullLogSink.Instance;

    /// <summary>Replaces the active sink. Passing null restores the null sink.</summary>
    public static void SetSink(ILogSink? sink)
    {
        _sSink = sink ?? NullLogSink.Instance;
    }

    /// <summary>Returns whether the given category and level should be emitted.</summary>
    public static bool IsEnabled(LogCategory category, LogLevel level)
    {
        return _sSink.IsEnabled(category, level);
    }

    /// <summary>Writes a log event to the active sink.</summary>
    public static void Write(LogCategory category, LogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _sSink.Write(category, level, message);
    }

    private sealed class NullLogSink : ILogSink
    {
        public static readonly NullLogSink Instance = new();

        bool ILogSink.IsEnabled(LogCategory category, LogLevel level)
        {
            return false;
        }

        void ILogSink.Write(LogCategory category, LogLevel level, string message)
        {
        }
    }
}
