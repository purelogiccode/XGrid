using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Checks the core-log parsing behind the pointer pinning drop-downs.</summary>
public sealed class ChihiroPointerLogTests : IDisposable
{
    private readonly string _root;

    public ChihiroPointerLogTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "xgrid-pointers-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Read_ParsesLinuxAbsoluteAndRelativePointers()
    {
        string path = WriteLog(
            "[0000030][chihiro] linux pointer 0: Microsoft Mouse (/dev/input/event4) relative",
            "[0000031][chihiro] linux pointer 2: XGrid Test Light Gun 1 (/dev/input/event16) absolute");

        IReadOnlyList<ChihiroPointerDevice> devices = ChihiroPointerLog.Read(path);

        Assert.Collection(
            devices,
            first =>
            {
                Assert.Equal(0, first.Index);
                Assert.Equal("Microsoft Mouse", first.Name);
                Assert.Equal("relative", first.Kind);
            },
            second =>
            {
                Assert.Equal(2, second.Index);
                Assert.Equal("XGrid Test Light Gun 1", second.Name);
                Assert.Equal("absolute", second.Kind);
            });
    }

    [Fact]
    public void Read_ParsesMacAndWindowsPointers()
    {
        string mac = WriteLog(
            "[0000030][chihiro] mac pointer 0: Virtual USB Digitizer (absolute)");
        Assert.Equal(
            "Virtual USB Digitizer",
            Assert.Single(ChihiroPointerLog.Read(mac)).Name);

        string windows = WriteLog(
            @"[0000030][chihiro] raw pointer 1: \\?\HID#VID_1234&PID_5678#8&1&0#0000");
        ChihiroPointerDevice device = Assert.Single(ChihiroPointerLog.Read(windows));
        Assert.Equal(1, device.Index);
        Assert.Equal(
            @"\\?\HID#VID_1234&PID_5678#8&1&0#0000",
            device.Name);
        Assert.Equal(string.Empty, device.Kind);
    }

    [Fact]
    public void Read_KeepsTheLastLineForADuplicatedSlot()
    {
        string path = WriteLog(
            "[0000030][chihiro] linux pointer 0: Old Mouse (/dev/input/event4) relative",
            "[0000040][chihiro] linux pointer 0: New Gun (/dev/input/event5) absolute");

        ChihiroPointerDevice device = Assert.Single(ChihiroPointerLog.Read(path));
        Assert.Equal("New Gun", device.Name);
        Assert.Equal("absolute", device.Kind);
    }

    [Fact]
    public void Read_IgnoresAssignmentLinesAndMissingFiles()
    {
        string path = WriteLog(
            "[0000030][chihiro] linux pointer P1: XGrid Test Light Gun 1 (absolute)",
            "not a pointer line");

        Assert.Empty(ChihiroPointerLog.Read(path));
        Assert.Empty(ChihiroPointerLog.Read(Path.Combine(_root, "missing.log")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteLog(params string[] lines)
    {
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".log");
        File.WriteAllLines(path, lines);
        return path;
    }
}
