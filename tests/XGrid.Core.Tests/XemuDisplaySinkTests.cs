using System;
using System.IO.MemoryMappedFiles;
using XGrid.Core.Xemu;
using Xunit;

namespace XGrid.Core.Tests;

/// <summary>
/// Pins the host transport's shared-section layout: the sink name matches the
/// platform, and a Windows section written with the documented header is read
/// back without a core.
/// </summary>
public sealed class XemuDisplaySinkTests
{
    [Fact]
    public void CreateName_UsesTheHostTransport()
    {
        string name = XemuDisplaySink.CreateName();

        if (OperatingSystem.IsWindows())
        {
            Assert.StartsWith("Local\\XGridDisplay-", name, StringComparison.Ordinal);
        }
        else
        {
            Assert.StartsWith("/xgrid-disp-", name, StringComparison.Ordinal);
            // POSIX names are limited to 31 characters on macOS.
            Assert.True(name.Length <= 31, $"sink name is {name.Length} characters: {name}");
        }
    }

    [Fact]
    public void TryRead_ReadsAWindowsSection()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const uint magic = 0x53444758;
        const uint width = 2;
        const uint height = 2;
        const uint stride = width * 4;
        byte[] pixels =
        [
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
        ];

        string name = "Local\\XGridDisplayTest-" + Guid.NewGuid().ToString("N");
        using var mapping = MemoryMappedFile.CreateNew(name, 32 + pixels.Length);
        using (MemoryMappedViewAccessor view = mapping.CreateViewAccessor())
        {
            view.Write(0, magic);
            view.Write(4, width);
            view.Write(8, height);
            view.Write(12, stride);
            view.Write(16, 7UL);
            view.WriteArray(32, pixels, 0, pixels.Length);
        }

        using var sink = new XemuDisplaySink(name);
        Assert.True(sink.TryReadCounter(out ulong counter));
        Assert.Equal(7UL, counter);

        Assert.True(sink.TryRead(out XemuDisplayFrame frame));
        Assert.Equal((int)width, frame.Width);
        Assert.Equal((int)height, frame.Height);
        Assert.Equal(7UL, frame.Counter);
        Assert.Equal(pixels, frame.Pixels.AsSpan(0, pixels.Length).ToArray());
    }
}
