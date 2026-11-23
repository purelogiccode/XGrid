using System;
using System.Collections.Generic;
using XGrid.Core.Configuration;
using XGrid.Core.Emulation;
using Xunit;

namespace XGrid.Core.Tests;

public sealed class EmulatorLifecycleTests
{
    private static Emulator CreateEmulator()
    {
        return new Emulator(new EmulatorConfiguration());
    }

    [Fact]
    public void FullLifecycle_TransitionsInOrder()
    {
        Emulator emulator = CreateEmulator();

        Assert.Equal(EmulatorState.Created, emulator.State);

        emulator.Initialize();
        Assert.Equal(EmulatorState.Initialized, emulator.State);

        emulator.Start();
        Assert.Equal(EmulatorState.Running, emulator.State);

        emulator.Pause();
        Assert.Equal(EmulatorState.Paused, emulator.State);

        emulator.Resume();
        Assert.Equal(EmulatorState.Running, emulator.State);

        emulator.Stop();
        Assert.Equal(EmulatorState.Stopped, emulator.State);

        emulator.Start();
        Assert.Equal(EmulatorState.Running, emulator.State);

        emulator.Reset();
        Assert.Equal(EmulatorState.Initialized, emulator.State);
    }

    [Fact]
    public void Initialize_Twice_Throws()
    {
        Emulator emulator = CreateEmulator();
        emulator.Initialize();

        Assert.Throws<InvalidOperationException>(emulator.Initialize);
    }

    [Fact]
    public void Start_FromCreated_Throws()
    {
        Emulator emulator = CreateEmulator();

        Assert.Throws<InvalidOperationException>(emulator.Start);
    }

    [Fact]
    public void Pause_FromInitialized_Throws()
    {
        Emulator emulator = CreateEmulator();
        emulator.Initialize();

        Assert.Throws<InvalidOperationException>(emulator.Pause);
    }

    [Fact]
    public void Resume_FromRunning_Throws()
    {
        Emulator emulator = CreateEmulator();
        emulator.Initialize();
        emulator.Start();

        Assert.Throws<InvalidOperationException>(emulator.Resume);
    }

    [Fact]
    public void Stop_FromCreated_Throws()
    {
        Emulator emulator = CreateEmulator();

        Assert.Throws<InvalidOperationException>(emulator.Stop);
    }

    [Fact]
    public void Reset_FromCreated_Throws()
    {
        Emulator emulator = CreateEmulator();

        Assert.Throws<InvalidOperationException>(emulator.Reset);
    }

    [Fact]
    public void Advance_WhenNotRunning_Throws()
    {
        Emulator emulator = CreateEmulator();
        emulator.Initialize();

        Assert.Throws<InvalidOperationException>(() => emulator.Advance(1));
    }

    [Fact]
    public void Advance_TicksDevicesAndClock()
    {
        var log = new List<string>();
        var device = new RecordingDevice("cpu", log);
        Emulator emulator = CreateEmulator();
        emulator.Scheduler.Register(device);
        emulator.Initialize();
        emulator.Start();

        ulong cycles = emulator.Advance(100);

        Assert.Equal(100UL, cycles);
        Assert.Equal(100UL, emulator.Clock.Cycles);
        Assert.Equal(1, device.TickCount);
        Assert.Equal(100UL, device.TotalCycles);
    }

    [Fact]
    public void AdvanceStep_UsesConfiguredCyclesPerStep()
    {
        var device = new RecordingDevice("cpu", []);
        Emulator emulator = new(
            new EmulatorConfiguration(),
            new EmulatorOptions { CyclesPerStep = 7 });
        emulator.Scheduler.Register(device);
        emulator.Initialize();
        emulator.Start();

        ulong cycles = emulator.AdvanceStep();

        Assert.Equal(7UL, cycles);
        Assert.Equal(7UL, device.TotalCycles);
    }

    [Fact]
    public void Reset_ResetsClockAndDevices()
    {
        var device = new RecordingDevice("cpu", []);
        Emulator emulator = CreateEmulator();
        emulator.Scheduler.Register(device);
        emulator.Initialize();
        emulator.Start();
        emulator.Advance(50);

        emulator.Reset();

        Assert.Equal(0UL, emulator.Clock.Cycles);
        Assert.Equal(1, device.ResetCount);
    }
}
