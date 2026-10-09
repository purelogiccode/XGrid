using System;
using XGrid.Core.Emulation;
using Xunit;

namespace XGrid.Core.Tests;

public sealed class EmulatorStateTests
{
    [Fact]
    public void EmulatorState_DefinesExpectedLifecycle()
    {
        Assert.Equal(0, (int)EmulatorState.Created);
        Assert.Equal(1, (int)EmulatorState.Initialized);
        Assert.Equal(2, (int)EmulatorState.Running);
        Assert.Equal(3, (int)EmulatorState.Paused);
        Assert.Equal(4, (int)EmulatorState.Stopped);
        Assert.Equal(5, (int)EmulatorState.Faulted);
    }

    [Fact]
    public void IEmulator_ExposesFullLifecycle()
    {
        Type type = typeof(IEmulator);

        Assert.NotNull(type.GetProperty(nameof(IEmulator.State)));
        Assert.NotNull(type.GetMethod(nameof(IEmulator.Initialize)));
        Assert.NotNull(type.GetMethod(nameof(IEmulator.Reset)));
        Assert.NotNull(type.GetMethod(nameof(IEmulator.Start)));
        Assert.NotNull(type.GetMethod(nameof(IEmulator.Pause)));
        Assert.NotNull(type.GetMethod(nameof(IEmulator.Resume)));
        Assert.NotNull(type.GetMethod(nameof(IEmulator.Stop)));
    }
}
