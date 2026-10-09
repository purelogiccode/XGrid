using System.Collections.Generic;
using XGrid.Core.Configuration;
using XGrid.Core.Emulation;
using Xunit;

namespace XGrid.Core.Tests;

public sealed class DeterminismTests
{
    private static List<string> Run(int steps, ulong cyclesPerStep)
    {
        var log = new List<string>();
        var device = new RecordingDevice("dev", log);
        var emulator = new Emulator(
            new EmulatorConfiguration(),
            new EmulatorOptions { CyclesPerStep = cyclesPerStep });
        emulator.Scheduler.Register(device);
        emulator.Initialize();
        emulator.Start();

        for (int i = 0; i < steps; i++)
        {
            emulator.AdvanceStep();
        }

        return log;
    }

    [Fact]
    public void TwoEmulators_SameSteps_ProduceIdenticalState()
    {
        List<string> first = Run(10, 3);
        List<string> second = Run(10, 3);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Reset_RestartsDeterministicState()
    {
        var log = new List<string>();
        var device = new RecordingDevice("dev", log);
        var emulator = new Emulator(new EmulatorConfiguration());
        emulator.Scheduler.Register(device);
        emulator.Initialize();
        emulator.Start();

        emulator.Advance(100);
        emulator.Reset();
        emulator.Start();
        emulator.Advance(100);

        Assert.Equal(1, device.ResetCount);
        Assert.Equal(2, device.TickCount);
        Assert.Equal(100UL, emulator.Clock.Cycles);
        Assert.Equal(3, log.Count);
        Assert.Equal("dev:tick:100", log[0]);
        Assert.Equal("dev:reset", log[1]);
        Assert.Equal("dev:tick:100", log[2]);
    }
}
