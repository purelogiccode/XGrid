using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace XGrid.Diagnostics.Tests;

/// <summary>Pins the background queue's delivery and failure isolation.</summary>
public sealed class BugReportQueueTests
{
    [Fact]
    public void Constructor_RejectsNegativeIntervals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BugReportQueue(static (_, _) => Task.CompletedTask, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Constructor_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BugReportQueue(null!));
        Assert.Throws<ArgumentNullException>(
            () => new BugReportQueue(null!, TimeSpan.Zero));
    }

    [Fact]
    public async Task Enqueue_DeliversEveryReportOnTheWorker()
    {
        var delivered = new List<BugReportRequest>();
        var gate = new SemaphoreSlim(0);
        using var queue = new BugReportQueue(
            (request, _) =>
            {
                lock (delivered)
                {
                    delivered.Add(request);
                }

                gate.Release();
                return Task.CompletedTask;
            },
            TimeSpan.Zero);

        queue.Enqueue(new BugReportRequest { Message = "first" });
        queue.Enqueue(new BugReportRequest { Message = "second" });

        Assert.True(await gate.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await gate.WaitAsync(TimeSpan.FromSeconds(5)));

        lock (delivered)
        {
            Assert.Equal(["first", "second"], delivered.ConvertAll(request => request.Message));
        }
    }

    [Fact]
    public async Task Worker_ContinuesAfterASenderFailure()
    {
        int calls = 0;
        var delivered = new List<string>();
        var gate = new SemaphoreSlim(0);
        using var queue = new BugReportQueue(
            (request, _) =>
            {
                Interlocked.Increment(ref calls);
                if (calls == 1)
                {
                    throw new InvalidDataException("malformed report");
                }

                lock (delivered)
                {
                    delivered.Add(request.Message);
                }

                gate.Release();
                return Task.CompletedTask;
            },
            TimeSpan.Zero);

        queue.Enqueue(new BugReportRequest { Message = "first" });
        queue.Enqueue(new BugReportRequest { Message = "second" });

        Assert.True(await gate.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(["second"], delivered);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var queue = new BugReportQueue(
            static (_, _) => Task.CompletedTask,
            TimeSpan.Zero);

        queue.Dispose();
        queue.Dispose();
    }

    [Fact]
    public void Enqueue_RejectsANullReport()
    {
        using var queue = new BugReportQueue(
            static (_, _) => Task.CompletedTask,
            TimeSpan.Zero);

        Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!));
    }
}
