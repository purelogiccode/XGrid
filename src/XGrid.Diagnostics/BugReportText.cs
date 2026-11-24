using System;
using System.Globalization;
using System.Text;

namespace XGrid.Diagnostics;

/// <summary>Builds the fixed bug-report text layout.</summary>
public static class BugReportText
{
    /// <summary>The maximum message length accepted by the service.</summary>
    public const int MaxMessageLength = 4000;

    /// <summary>The maximum stack-trace length accepted by the service.</summary>
    public const int MaxStackTraceLength = 8000;

    /// <summary>Composes a report with environment, error and exception sections.</summary>
    /// <param name="errorMessage">The warning or error message.</param>
    /// <param name="exception">The exception, when available.</param>
    /// <param name="options">The application options.</param>
    /// <param name="snapshot">An optional environment snapshot.</param>
    /// <returns>A request ready for the bug-report endpoint.</returns>
    public static BugReportRequest CreateRequest(
        string errorMessage,
        Exception? exception,
        DiagnosticsOptions options,
        EnvironmentSnapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        string message = string.IsNullOrWhiteSpace(errorMessage) ? "(no message)" : errorMessage.Trim();

        // Reserve room for the environment and exception sections: a huge error
        // string must not push the exception details out of the report.
        message = Truncate(message, MaxMessageLength / 3);
        EnvironmentSnapshot environment = snapshot
                                          ?? EnvironmentSnapshot.Capture(options.ApplicationName, options.Version);

        var builder = new StringBuilder(capacity: 1024);
        _ = builder.AppendLine(environment.ToText());
        _ = builder.AppendLine();
        _ = builder.AppendLine("=== Error Details ===");
        _ = builder.Append("Error message: ").AppendLine(message);
        _ = builder.AppendLine();
        _ = builder.AppendLine("=== Exception Details ===");
        _ = builder.Append("Type: ").AppendLine(exception?.GetType().FullName ?? "(none)");
        _ = builder.Append("Message: ").AppendLine(exception?.Message ?? "(none)");
        _ = builder.Append("Source: ").AppendLine(exception?.Source ?? "(none)");
        _ = builder.AppendLine("StackTrace:");
        _ = builder.AppendLine(exception?.ToString() ?? "(none)");

        string report = Truncate(builder.ToString(), MaxMessageLength);
        string? stackTrace = exception is null
            ? null
            : Truncate(exception.ToString(), MaxStackTraceLength);

        return new BugReportRequest
        {
            Message = report,
            ApplicationName = options.ApplicationName,
            Version = options.Version,
            Environment = FormattableString.Invariant(
                $"{environment.PlatformVersion}; {environment.Architecture}; {environment.Bitness}"),
            StackTrace = stackTrace,
        };
    }

    private static string Truncate(string text, int maximum)
    {
        return text.Length <= maximum ? text : text[..(maximum - 3)] + "...";
    }

    /// <summary>Formats an exception with its type, message, source and stack trace.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>A diagnostic string.</returns>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}: {1}{2}{3}",
            exception.GetType().FullName,
            exception.Message,
            Environment.NewLine,
            exception.StackTrace ?? "(no stack trace)");
    }
}
