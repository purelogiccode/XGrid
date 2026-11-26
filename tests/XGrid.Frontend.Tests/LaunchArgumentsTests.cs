using System;
using System.IO;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia;
using XGrid.Frontend.Avalonia.Models;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the shell's own command-line switches: autostart, the fullscreen
/// spellings, the per-session settings overrides and the headless limits. An
/// unknown argument still passes through to Avalonia; a recognized switch with
/// a bad value must fail instead of silently booting something else.
/// </summary>
public sealed class LaunchArgumentsTests
{
    [Theory]
    [InlineData("-fullscreen")]
    [InlineData("--fullscreen")]
    [InlineData("-f")]
    [InlineData("-F")]
    public void Parse_RecognizesTheFullscreenSpellings(string argument)
    {
        LaunchArguments options = LaunchArguments.Parse([argument]);

        Assert.True(options.Fullscreen);
        Assert.False(options.Autostart);
        Assert.Empty(options.Remaining);
        Assert.False(options.HasErrors);
    }

    [Fact]
    public void Parse_CombinesAutostartAndFullscreen()
    {
        LaunchArguments options = LaunchArguments.Parse(["-f", "--autostart"]);

        Assert.True(options.Autostart);
        Assert.True(options.Fullscreen);
        Assert.Empty(options.Remaining);
    }

    [Fact]
    public void Parse_PassesUnknownArgumentsThrough()
    {
        LaunchArguments options = LaunchArguments.Parse(["one", "--theme", "dark", "-f"]);

        Assert.True(options.Fullscreen);
        Assert.Equal(["one", "--theme", "dark"], options.Remaining);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        LaunchArguments options = LaunchArguments.Parse(["--AUTOSTART", "--FullScreen"]);

        Assert.True(options.Autostart);
        Assert.True(options.Fullscreen);
    }

    [Fact]
    public void Parse_TreatsADiscPathAsTheLaunchRequest()
    {
        string path = Path.Combine("media", "game.iso");

        LaunchArguments options = LaunchArguments.Parse([path]);

        Assert.Equal(Path.GetFullPath(path), options.DiscPath);
        Assert.True(options.Autostart);
        Assert.Empty(options.Remaining);
        Assert.False(options.HasErrors);
    }

    [Fact]
    public void Parse_ReadsTheSessionOverrides()
    {
        LaunchArguments options = LaunchArguments.Parse(
        [
            "--renderer", "vulkan",
            "--scale=3",
            "--filtering", "NEAREST",
            "--filter", "scanline",
            "--aspect", "16:9",
            "--fit", "stretch",
            "--ram", "128",
            "--audio", "none",
            "--core", "core/xemu.exe",
            "--bios", "fw/Complex_4627.bin",
            "--mcpx", "fw/mcpx_1.0.bin",
            "--eeprom", "fw/eeprom.bin",
            "--hdd", "fw/hdd.qcow2",
        ]);

        Assert.False(options.HasErrors);
        Assert.Equal(GraphicsSettings.RendererVulkan, options.Renderer);
        Assert.Equal(3, options.SurfaceScale);
        Assert.Equal(GraphicsSettings.FilterNearest, options.Filtering);
        Assert.Equal(XemuPresentationFilter.Scanline, options.PresentationFilter);
        Assert.Equal("16x9", options.AspectRatio);
        Assert.Equal("stretch", options.Fit);
        Assert.Equal(128, options.RamMb);
        Assert.Equal(AudioSettings.BackendNone, options.AudioBackend);
        Assert.Equal(Path.GetFullPath("core/xemu.exe"), options.CorePath);
        Assert.Equal(Path.GetFullPath("fw/Complex_4627.bin"), options.BiosPath);
        Assert.Equal(Path.GetFullPath("fw/mcpx_1.0.bin"), options.McpxRomPath);
        Assert.Equal(Path.GetFullPath("fw/eeprom.bin"), options.EepromPath);
        Assert.Equal(Path.GetFullPath("fw/hdd.qcow2"), options.HddPath);
        Assert.True(options.HasOverrides);
    }

    [Theory]
    [InlineData("d3d11")]
    [InlineData("direct3d11")]
    [InlineData("directx")]
    public void Parse_MapsTheDirect3D11Aliases(string value)
    {
        LaunchArguments options = LaunchArguments.Parse(["--renderer", value]);

        Assert.False(options.HasErrors);
        Assert.Equal(GraphicsSettings.RendererDirect3D11, options.Renderer);
    }

    [Fact]
    public void Parse_ReadsTheHeadlessLimits()
    {
        LaunchArguments options = LaunchArguments.Parse(
            ["--headless", "--seconds", "45", "--frames=1200", "--summary", "run.json"]);

        Assert.True(options.Headless);
        Assert.Equal(45, options.Seconds);
        Assert.Equal(1200, options.Frames);
        Assert.Equal(Path.GetFullPath("run.json"), options.SummaryPath);
        Assert.False(options.HasErrors);
    }

    [Fact]
    public void Parse_BenchmarkImpliesHeadlessAndDefaultsToAFixedFrameCount()
    {
        LaunchArguments options = LaunchArguments.Parse(["--benchmark"]);

        Assert.True(options.Benchmark);
        Assert.True(options.Headless);
        Assert.Equal(LaunchArguments.DefaultBenchmarkFrames, options.Frames);
        Assert.Null(options.Seconds);
    }

    [Fact]
    public void Parse_BenchmarkHonorsAnExplicitLimit()
    {
        LaunchArguments seconds = LaunchArguments.Parse(["--benchmark", "--seconds", "30"]);
        LaunchArguments frames = LaunchArguments.Parse(["--benchmark", "--frames", "120"]);

        Assert.Equal(30, seconds.Seconds);
        Assert.Null(seconds.Frames);
        Assert.Equal(120, frames.Frames);
        Assert.Null(frames.Seconds);
    }

    [Fact]
    public void Usage_DocumentsTheBenchmarkSwitch()
    {
        Assert.Contains("--benchmark", LaunchArguments.Usage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_StopsAtTheEndOfOptionsMarker()
    {
        LaunchArguments options = LaunchArguments.Parse(["--headless", "--", "--not-a-disc.iso", "-f"]);

        Assert.True(options.Headless);
        Assert.Null(options.DiscPath);
        Assert.False(options.Fullscreen);
        Assert.Equal(["--not-a-disc.iso", "-f"], options.Remaining);
    }

    [Theory]
    [InlineData("--renderer", "metal")]
    [InlineData("--scale", "0")]
    [InlineData("--scale", "9")]
    [InlineData("--filtering", "anisotropic")]
    [InlineData("--filter", "sharpen")]
    [InlineData("--aspect", "21x9")]
    [InlineData("--fit", "crop")]
    [InlineData("--ram", "256")]
    [InlineData("--audio", "pulse")]
    [InlineData("--seconds", "0")]
    [InlineData("--frames", "0")]
    public void Parse_RejectsABadValue(string name, string value)
    {
        LaunchArguments options = LaunchArguments.Parse([name, value]);

        Assert.True(options.HasErrors);
        Assert.NotEmpty(options.Errors);
    }

    [Fact]
    public void Parse_RejectsAMissingValue()
    {
        LaunchArguments options = LaunchArguments.Parse(["--renderer"]);

        Assert.True(options.HasErrors);
        Assert.Contains("--renderer", options.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_DoesNotConsumeTheNextSwitchAsAMissingValue()
    {
        LaunchArguments options = LaunchArguments.Parse(["--renderer", "--autostart"]);

        Assert.True(options.HasErrors);
        Assert.Contains("--renderer", options.Errors[0], StringComparison.Ordinal);
        Assert.True(options.Autostart);
    }

    [Fact]
    public void Parse_ReadsTheAudioDevice()
    {
        LaunchArguments options = LaunchArguments.Parse(
            ["--audio-device", "{c847f520-6c79-4ae1-ab1e-b9cc32469df8}"]);

        Assert.False(options.HasErrors);
        Assert.Equal("{c847f520-6c79-4ae1-ab1e-b9cc32469df8}", options.AudioDevice);
        Assert.True(options.HasOverrides);
    }

    [Fact]
    public void Parse_RejectsAnAudioDeviceWithAComma()
    {
        LaunchArguments options = LaunchArguments.Parse(["--audio-device", "sink,other"]);

        Assert.True(options.HasErrors);
        Assert.Contains("comma", options.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_AcceptsInlineValues()
    {
        LaunchArguments options = LaunchArguments.Parse(
            ["--renderer=null", "--scale=2", "--filter=crt", "--ram=128", "--audio=wav"]);

        Assert.False(options.HasErrors);
        Assert.Equal(GraphicsSettings.RendererNull, options.Renderer);
        Assert.Equal(2, options.SurfaceScale);
        Assert.Equal(XemuPresentationFilter.Crt, options.PresentationFilter);
        Assert.Equal(128, options.RamMb);
        Assert.Equal(AudioSettings.BackendWav, options.AudioBackend);
    }

    [Fact]
    public void Parse_RejectsAnEmptyInlineValue()
    {
        LaunchArguments options = LaunchArguments.Parse(["--renderer="]);

        Assert.True(options.HasErrors);
        Assert.Contains("--renderer", options.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsOutOfRangeLimits()
    {
        Assert.True(LaunchArguments.Parse(["--seconds", "86401"]).HasErrors);
        Assert.True(LaunchArguments.Parse(["--frames", "100000001"]).HasErrors);
    }

    [Fact]
    public void Parse_ReadsTheDataDirectory()
    {
        LaunchArguments options = LaunchArguments.Parse(["--data-dir", "portable-data"]);

        Assert.Equal(Path.GetFullPath("portable-data"), options.DataDirectory);
        Assert.False(options.HasErrors);
    }

    [Fact]
    public void Parse_RejectsTwoDiscs()
    {
        LaunchArguments options = LaunchArguments.Parse(["one.iso", "--disc", "two.chd"]);

        Assert.True(options.HasErrors);
        Assert.Contains("Only one disc", options.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsAnUnsupportedDiscExtension()
    {
        LaunchArguments options = LaunchArguments.Parse(["game.bin"]);

        Assert.False(options.HasErrors);
        Assert.Null(options.DiscPath);
        Assert.Equal(["game.bin"], options.Remaining);

        LaunchArguments switched = LaunchArguments.Parse(["--disc", "game.bin"]);

        Assert.True(switched.HasErrors);
    }

    [Fact]
    public void Parse_RejectsALooseXbe()
    {
        LaunchArguments options = LaunchArguments.Parse(["--disc", "default.xbe"]);

        Assert.True(options.HasErrors);
        Assert.Contains("not bootable", options.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyTo_WritesOnlyTheGivenOverrides()
    {
        var settings = new EmulatorSettings
        {
            BiosPath = "old-bios.bin",
            RamMb = 64,
            Filter = XemuPresentationFilter.Nearest,
        };
        settings.Graphics.Renderer = GraphicsSettings.RendererOpenGl;
        settings.Graphics.SurfaceScale = 1;

        LaunchArguments options = LaunchArguments.Parse(
            ["--renderer", "null", "--scale", "4", "--ram", "128", "--filter", "crt"]);
        options.ApplyTo(settings);

        Assert.Equal(GraphicsSettings.RendererNull, settings.Graphics.Renderer);
        Assert.Equal(4, settings.Graphics.SurfaceScale);
        Assert.Equal(128, settings.RamMb);
        Assert.Equal(XemuPresentationFilter.Crt, settings.Filter);
        Assert.Equal("old-bios.bin", settings.BiosPath);
    }

    [Fact]
    public void Parse_HeadlessControlsSelectHeadlessMode()
    {
        Assert.True(LaunchArguments.Parse(["--seconds", "10"]).Headless);
        Assert.True(LaunchArguments.Parse(["--frames", "1"]).Headless);
        Assert.True(LaunchArguments.Parse(["--summary", "run.json"]).Headless);
    }

    [Fact]
    public void Parse_RecognizesHelpAndVersion()
    {
        Assert.True(LaunchArguments.Parse(["--help"]).Help);
        Assert.True(LaunchArguments.Parse(["-h"]).Help);
        Assert.True(LaunchArguments.Parse(["-?"]).Help);
        Assert.True(LaunchArguments.Parse(["--version"]).Version);
        Assert.Contains("--headless", LaunchArguments.Usage, StringComparison.Ordinal);
    }
}
