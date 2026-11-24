using System.Collections.Generic;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the shell side of a disc change: one request at a time, a completion
/// report with the operation and image, and a timeout so the UI cannot stay
/// busy forever. The core changes the block backend on its own loop.
/// </summary>
public sealed class DiscChangeRunnerTests
{
    [Fact]
    public void TryLoad_QueuesTheImageAndReportsTheCompletion()
    {
        var discs = new FakeDiscs();
        var runner = new DiscChangeRunner(discs, clock: () => 1000);

        Assert.True(runner.TryLoad(@"C:\games\disc2.iso", out string problem));
        Assert.Empty(problem);
        Assert.True(runner.IsBusy);
        Assert.Equal(DiscChangeOperation.Load, runner.Operation);
        Assert.Equal([@"load:C:\games\disc2.iso"], discs.Calls);
        Assert.Null(runner.Poll());

        discs.Status = XemuDiscStatus.Completed;
        DiscChangeResult? result = runner.Poll();
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.Equal(DiscChangeOperation.Load, result.Operation);
        Assert.Equal(@"C:\games\disc2.iso", result.Path);
        Assert.Equal("Inserted disc2.iso.", result.Message);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public void TryEject_ReportsTheEject()
    {
        var discs = new FakeDiscs();
        var runner = new DiscChangeRunner(discs, clock: () => 1000);

        Assert.True(runner.TryEject(out _));
        discs.Status = XemuDiscStatus.Completed;
        DiscChangeResult? result = runner.Poll();

        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.Equal(DiscChangeOperation.Eject, result.Operation);
        Assert.Equal("Ejected the disc.", result.Message);
        Assert.Equal(["eject"], discs.Calls);
    }

    [Fact]
    public void RefusedRequest_DoesNotBecomeBusy()
    {
        var discs = new FakeDiscs { Accept = false, Refusal = "A disc change is still in progress." };
        var runner = new DiscChangeRunner(discs, clock: () => 1000);

        Assert.False(runner.TryLoad(@"C:\games\disc2.iso", out string problem));
        Assert.Equal("A disc change is still in progress.", problem);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public void ASingleOutstandingRequestIsEnforced()
    {
        var discs = new FakeDiscs();
        var runner = new DiscChangeRunner(discs, clock: () => 1000);

        Assert.True(runner.TryLoad(@"C:\games\a.iso", out _));
        Assert.False(runner.TryEject(out string problem));
        Assert.Equal("A disc change is still in progress.", problem);
    }

    [Fact]
    public void Poll_ReportsTheCoreErrorText()
    {
        var discs = new FakeDiscs();
        var runner = new DiscChangeRunner(discs, clock: () => 1000);

        Assert.True(runner.TryLoad(@"C:\games\bad.iso", out _));
        discs.Status = XemuDiscStatus.Failed;
        discs.Error = "xgridzar: no .iso entry in this archive";
        DiscChangeResult? result = runner.Poll();

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal("xgridzar: no .iso entry in this archive", result.Message);
    }

    [Fact]
    public void Poll_AbandonsARequestThatNeverAnswers()
    {
        var discs = new FakeDiscs();
        long now = 1000;
        var runner = new DiscChangeRunner(discs, timeoutMs: 30_000, clock: () => now);

        Assert.True(runner.TryLoad(@"C:\games\a.iso", out _));
        now += 30_001;
        DiscChangeResult? result = runner.Poll();

        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Equal("The emulator did not answer the disc change.", result.Message);
    }

    [Fact]
    public void WithoutACore_ReportsTheHostingRequirement()
    {
        var runner = new DiscChangeRunner(null);

        Assert.False(runner.IsSupported);
        Assert.False(runner.TryLoad(@"C:\games\a.iso", out string problem));
        Assert.Equal("Changing discs needs the in-process emulator core.", problem);
    }

    private sealed class FakeDiscs : ICoreDiscs
    {
        public bool Accept { get; set; } = true;

        public string Refusal { get; set; } = "refused";

        public XemuDiscStatus Status { get; set; } = XemuDiscStatus.Pending;

        public string Error { get; set; } = string.Empty;

        public List<string> Calls { get; } = [];

        public bool TryLoad(string path, out string problem)
        {
            Calls.Add("load:" + path);
            problem = Accept ? string.Empty : Refusal;
            return Accept;
        }

        public bool TryEject(out string problem)
        {
            Calls.Add("eject");
            problem = Accept ? string.Empty : Refusal;
            return Accept;
        }
    }
}
