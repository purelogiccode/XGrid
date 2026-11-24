using System;
using System.Collections.Generic;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the shell side of save states: one request at a time, named slots, and
/// one completion message per request. The core writes the machine's state as
/// an internal snapshot of the hard disk image, so the shell only queues and
/// polls - these tests make that contract explicit without a running core.
/// </summary>
public sealed class SaveStateRunnerTests
{
    [Fact]
    public void TrySave_QueuesTheSlotNameAndWaits()
    {
        var service = new FakeSaveStateService();
        var runner = new SaveStateRunner(service, clock: () => 1000);

        Assert.True(runner.TrySave(2, out string problem));
        Assert.Empty(problem);
        Assert.True(runner.IsBusy);
        Assert.Equal(XemuSaveStateOperation.Save, runner.Operation);
        Assert.Equal(2, runner.Slot);
        Assert.Equal(["save:xgrid-slot-2"], service.Calls);
        Assert.Null(runner.Poll());
    }

    [Fact]
    public void TrySaveThenTryLoad_RoundTripsTheSameSlot()
    {
        var service = new FakeSaveStateService();
        var runner = new SaveStateRunner(service, clock: () => 1000);

        Assert.True(runner.TrySave(3, out _));
        service.Status = XemuSaveStateStatus.Completed;
        SaveStateResult? saved = runner.Poll();
        Assert.NotNull(saved);
        Assert.True(saved!.Succeeded);
        Assert.Equal("Saved state to slot 3.", saved.Message);

        Assert.True(runner.TryLoad(3, out _));
        service.Status = XemuSaveStateStatus.Completed;
        SaveStateResult? loaded = runner.Poll();
        Assert.NotNull(loaded);
        Assert.True(loaded!.Succeeded);
        Assert.Equal("Loaded state from slot 3.", loaded.Message);

        Assert.Equal(["save:xgrid-slot-3", "load:xgrid-slot-3"], service.Calls);
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public void Poll_ReportsOneResultAndThenStaysQuiet()
    {
        var service = new FakeSaveStateService();
        var runner = new SaveStateRunner(service, clock: () => 1000);
        Assert.True(runner.TryDelete(1, out _));
        service.Status = XemuSaveStateStatus.Completed;

        Assert.NotNull(runner.Poll());
        Assert.Null(runner.Poll());
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public void Poll_ReportsTheCoreError()
    {
        var service = new FakeSaveStateService();
        var runner = new SaveStateRunner(service, clock: () => 1000);
        Assert.True(runner.TrySave(1, out _));
        service.Status = XemuSaveStateStatus.Failed;
        service.Error = "Could not create snapshot: no space";

        SaveStateResult? result = runner.Poll();

        Assert.NotNull(result);
        Assert.False(result!.Succeeded);
        Assert.Equal("Could not create snapshot: no space", result.Message);
    }

    [Fact]
    public void TrySave_RefusesASecondRequestWhileOneIsRunning()
    {
        var service = new FakeSaveStateService();
        var runner = new SaveStateRunner(service, clock: () => 1000);
        Assert.True(runner.TrySave(1, out _));

        Assert.False(runner.TryLoad(1, out string problem));

        Assert.Contains("previous save state", problem, StringComparison.Ordinal);
        Assert.Equal(["save:xgrid-slot-1"], service.Calls);
    }

    [Fact]
    public void TrySave_RefusesWhenTheHostingModeHasNoService()
    {
        var runner = new SaveStateRunner(service: null);

        Assert.False(runner.IsSupported);
        Assert.False(runner.TrySave(1, out string problem));
        Assert.Contains("in-process", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySave_RefusesASlotOutsideTheMenu()
    {
        var service = new FakeSaveStateService();
        var runner = new SaveStateRunner(service, clock: () => 1000);

        Assert.False(runner.TrySave(0, out _));
        Assert.False(runner.TrySave(5, out _));
        Assert.Empty(service.Calls);
    }

    [Fact]
    public void Poll_AbandonsARequestTheCoreNeverAnswers()
    {
        long now = 1000;
        var service = new FakeSaveStateService();
        var runner = new SaveStateRunner(service, timeoutMs: 500, clock: () => now);
        Assert.True(runner.TrySave(1, out _));

        now += 400;
        Assert.Null(runner.Poll());

        now += 200;
        SaveStateResult? result = runner.Poll();

        Assert.NotNull(result);
        Assert.False(result!.Succeeded);
        Assert.Contains("did not answer", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refresh_ReportsTheNumberOfSnapshots()
    {
        var service = new FakeSaveStateService();
        service.Names = ["xgrid-slot-1", "xgrid-slot-4"];
        var runner = new SaveStateRunner(service, clock: () => 1000);
        Assert.True(runner.TryRefresh(out _));
        service.Status = XemuSaveStateStatus.Completed;

        SaveStateResult? result = runner.Poll();

        Assert.NotNull(result);
        Assert.True(result!.Succeeded);
        Assert.Equal("Save states: 2 found.", result.Message);
        Assert.Equal(["refresh:"], service.Calls);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(0, false)]
    [InlineData(5, false)]
    public void IsValidSlot_AcceptsOnlyTheMenuSlots(int slot, bool expected)
    {
        Assert.Equal(expected, SaveStateRunner.IsValidSlot(slot));
    }

    [Fact]
    public void SlotName_IsStableForTheDiskImage()
    {
        Assert.Equal("xgrid-slot-1", SaveStateRunner.SlotName(1));
        Assert.Equal("xgrid-slot-4", SaveStateRunner.SlotName(4));
    }

    private sealed class FakeSaveStateService : ISaveStateService
    {
        public List<string> Calls { get; } = [];

        public XemuSaveStateStatus Status { get; set; }

        public string Error { get; set; } = string.Empty;

        public IReadOnlyList<string> Names { get; set; } = Array.Empty<string>();

        public bool TrySave(string name, out string problem)
        {
            return Queue("save", name, out problem);
        }

        public bool TryLoad(string name, out string problem)
        {
            return Queue("load", name, out problem);
        }

        public bool TryDelete(string name, out string problem)
        {
            return Queue("delete", name, out problem);
        }

        public bool TryRefresh(out string problem)
        {
            return Queue("refresh", string.Empty, out problem);
        }

        private bool Queue(string operation, string name, out string problem)
        {
            problem = string.Empty;
            Calls.Add(operation + ":" + name);
            Status = XemuSaveStateStatus.Pending;
            return true;
        }
    }
}
