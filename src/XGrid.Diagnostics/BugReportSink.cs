using System;
using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace XGrid.Diagnostics;

/// <summary>
/// Serilog sink that forwards warning and error events to the centralized
/// bug-report service. The network work happens on a background queue.
/// </summary>
public sealed class BugReportSink : ILogEventSink, IDisposable
{
    private readonly DiagnosticsOptions _options;
    private readonly BugReportQueue _queue;

    /// <summary>Creates the sink.</summary>
    /// <param name="options">The bug-report endpoint and application identity.</param>
    public BugReportSink(DiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _queue = new BugReportQueue(options);
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (logEvent.Level < LogEventLevel.Warning)
        {
            return;
        }

        string message = logEvent.RenderMessage(CultureInfo.InvariantCulture);
        if (logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? source)
            && source is ScalarValue { Value: string context }
            && !string.IsNullOrWhiteSpace(context))
        {
            message = context + ": " + message;
        }

        BugReportRequest request = BugReportText.CreateRequest(message, logEvent.Exception, _options);
        _queue.Enqueue(request);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _queue.Dispose();
    }
}
