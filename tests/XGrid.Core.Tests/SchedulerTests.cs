using System;
using System.Collections.Generic;
using XGrid.Core.Emulation;
using Xunit;

namespace XGrid.Core.Tests;

public sealed class SchedulerTests
{
    [Fact]
    public void Tick_CallsDevicesInRegistrationOrder()
    {
        var log = new List<string>();
        var scheduler = new Scheduler();
        scheduler.Register(new RecordingDevice("a", log));
        scheduler.Register(new RecordingDevice("b", log));
        scheduler.Register(new RecordingDevice("c", log));

        scheduler.Tick(5);

        Assert.Equal(3, log.Count);
        Assert.Equal("a:tick:5", log[0]);
        Assert.Equal("b:tick:5", log[1]);
        Assert.Equal("c:tick:5", log[2]);
    }

    [Fact]
    public void Reset_CallsDevicesInRegistrationOrder()
    {
        var log = new List<string>();
        var scheduler = new Scheduler();
        scheduler.Register(new RecordingDevice("a", log));
        scheduler.Register(new RecordingDevice("b", log));

        scheduler.Reset();

        Assert.Equal(2, log.Count);
        Assert.Equal("a:reset", log[0]);
        Assert.Equal("b:reset", log[1]);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var scheduler = new Scheduler();

        Assert.Throws<ArgumentNullException>(() => scheduler.Register(null!));
    }

    [Fact]
    public void Clear_RemovesDevices()
    {
        var scheduler = new Scheduler();
        scheduler.Register(new RecordingDevice("a", []));

        scheduler.Clear();

        Assert.Equal(0, scheduler.DeviceCount);
    }

    [Fact]
    public void Devices_ExposesRegistrationOrder()
    {
        var scheduler = new Scheduler();
        var first = new RecordingDevice("a", []);
        var second = new RecordingDevice("b", []);
        scheduler.Register(first);
        scheduler.Register(second);

        Assert.Equal(2, scheduler.DeviceCount);
        Assert.Same(first, scheduler.Devices[0]);
        Assert.Same(second, scheduler.Devices[1]);
    }
}
