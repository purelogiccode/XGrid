using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog.Debugging;

namespace XGrid.Diagnostics;

/// <summary>
/// Queues warning and error reports so logging never waits on the network.
/// The queue respects the service's limit of ten reports per minute.
/// </summary>
internal sealed class BugReportQueue : IDisposable
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(6);

    private readonly Channel<BugReportRequest> _channel;
    private readonly DiagnosticsOptions _options;
    private readonly Task _worker;
    private int _disposed;

    /// <summary>Initializes the queue and its background worker.</summary>
    /// <param name="options">The endpoint and API key.</param>
    internal BugReportQueue(DiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _channel = Channel.CreateBounded<BugReportRequest>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        _worker = Task.Run(ProcessAsync);
    }

    /// <summary>Adds a report without blocking the caller.</summary>
    /// <param name="request">The report to queue.</param>
    internal void Enqueue(BugReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = _channel.Writer.TryWrite(request);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _ = _channel.Writer.TryComplete();
        try
        {
            _ = _worker.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException exception)
        {
            SelfLog.WriteLine("Bug report worker failed: " + exception.GetBaseException());
        }
    }

    private async Task ProcessAsync()
    {
        var clock = new Stopwatch();
        bool sent = false;
        await foreach (BugReportRequest request in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (sent && clock.Elapsed < MinimumInterval)
            {
                TimeSpan remaining = MinimumInterval - clock.Elapsed;
                await Task.Delay(remaining).ConfigureAwait(false);
            }

            clock.Restart();
            try
            {
                _ = await BugReportClient
                    .SendAsync(request, _options)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException
                                                  or TaskCanceledException
                                                  or InvalidOperationException)
            {
                // The report is best-effort: a network outage must not affect
                // the application or recurse back into the logging pipeline.
                SelfLog.WriteLine("Bug report could not be sent: " + exception.Message);
            }

            sent = true;
        }
    }
}
