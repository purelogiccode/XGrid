using System;
using System.Diagnostics;
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
    private readonly Func<BugReportRequest, CancellationToken, Task> _send;
    private readonly TimeSpan _minimumInterval;
    private readonly Task _worker;
    private int _disposed;

    /// <summary>Initializes the queue and its background worker.</summary>
    /// <param name="options">The endpoint and API key.</param>
    internal BugReportQueue(DiagnosticsOptions options)
        : this(CreateSender(options), MinimumInterval)
    {
    }

    /// <summary>Initializes a queue with an injected sender (tests and tools).</summary>
    /// <param name="send">Sender invoked for every queued report.</param>
    /// <param name="minimumInterval">Delay enforced between reports.</param>
    internal BugReportQueue(
        Func<BugReportRequest, CancellationToken, Task> send,
        TimeSpan minimumInterval)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumInterval, TimeSpan.Zero);

        _send = send;
        _minimumInterval = minimumInterval;
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

    private static Func<BugReportRequest, CancellationToken, Task> CreateSender(
        DiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (request, token) => BugReportClient.SendAsync(request, options, token);
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
            if (sent && clock.Elapsed < _minimumInterval)
            {
                TimeSpan remaining = _minimumInterval - clock.Elapsed;
                await Task.Delay(remaining).ConfigureAwait(false);
            }

            clock.Restart();
            try
            {
                await _send(request, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The report is best-effort: a network outage or a malformed
                // report must not affect the application or stop the worker.
                SelfLog.WriteLine("Bug report could not be sent: " + exception.Message);
            }

            sent = true;
        }
    }
}
