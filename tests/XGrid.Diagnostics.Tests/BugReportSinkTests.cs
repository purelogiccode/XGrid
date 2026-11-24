using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace XGrid.Diagnostics.Tests;

/// <summary>Pins which Serilog events become bug reports and how they are labelled.</summary>
public sealed class BugReportSinkTests
{
    [Fact]
    public void Emit_IgnoresInformationAndDebugEvents()
    {
        var delivered = new List<BugReportRequest>();
        using var sink = CreateSink(delivered);

        sink.Emit(CreateEvent(LogEventLevel.Information));
        sink.Emit(CreateEvent(LogEventLevel.Debug));

        Assert.Empty(delivered);
    }

    [Fact]
    public async Task Emit_ForwardsWarningsWithTheSourceContextPrefix()
    {
        var delivered = new List<BugReportRequest>();
        var gate = new SemaphoreSlim(0);
        using var sink = CreateSink(delivered, gate);

        sink.Emit(CreateEvent(
            LogEventLevel.Warning,
            properties: [new LogEventProperty("SourceContext", new ScalarValue("XGrid.Frontend"))]));

        Assert.True(await gate.WaitAsync(TimeSpan.FromSeconds(5)));
        BugReportRequest report = Assert.Single(delivered);
        Assert.Contains("XGrid.Frontend: boom", report.Message, StringComparison.Ordinal);
        Assert.Equal("XGrid", report.ApplicationName);
        Assert.Equal("1.2.3", report.Version);
    }

    [Fact]
    public async Task Emit_ForwardsErrorsWithTheExceptionText()
    {
        var delivered = new List<BugReportRequest>();
        var gate = new SemaphoreSlim(0);
        using var sink = CreateSink(delivered, gate);
        var exception = new InvalidOperationException("the wheel fell off");

        sink.Emit(CreateEvent(LogEventLevel.Error, exception));

        Assert.True(await gate.WaitAsync(TimeSpan.FromSeconds(5)));
        BugReportRequest report = Assert.Single(delivered);
        Assert.Contains("the wheel fell off", report.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), report.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emit_RendersStructuredValuesWithoutAContext()
    {
        var delivered = new List<BugReportRequest>();
        var gate = new SemaphoreSlim(0);
        using var sink = CreateSink(delivered, gate);

        sink.Emit(CreateEvent(
            LogEventLevel.Warning,
            properties: [new LogEventProperty("Count", new ScalarValue(7))]));

        Assert.True(await gate.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("boom 7", Assert.Single(delivered).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Emit_RejectsANullEvent()
    {
        using var sink = CreateSink([]);

        Assert.Throws<ArgumentNullException>(() => sink.Emit(null!));
    }

    private static BugReportSink CreateSink(
        List<BugReportRequest> delivered,
        SemaphoreSlim? gate = null)
    {
        DiagnosticsOptions options = CreateOptions();
        var queue = new BugReportQueue(
            (request, _) =>
            {
                lock (delivered)
                {
                    delivered.Add(request);
                }

                gate?.Release();
                return Task.CompletedTask;
            },
            TimeSpan.Zero);
        return new BugReportSink(options, queue);
    }

    private static DiagnosticsOptions CreateOptions()
    {
        return new DiagnosticsOptions
        {
            ApplicationName = "XGrid",
            Version = "1.2.3",
            BugReportEndpoint = new Uri("https://example.test/bugreport", UriKind.Absolute),
        };
    }

    private static LogEvent CreateEvent(
        LogEventLevel level,
        Exception? exception = null,
        params LogEventProperty[] properties)
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            exception,
            new MessageTemplateParser().Parse("boom {Count}"),
            properties);
    }
}
