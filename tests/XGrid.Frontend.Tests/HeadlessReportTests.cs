using System.Text.Json;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>
/// Pins the headless run report: the console form carries the numbers an
/// operator reads, and the JSON form keeps the same fields for automation.
/// </summary>
public sealed class HeadlessReportTests
{
    [Fact]
    public void ToText_RendersTheRunSummary()
    {
        HeadlessReport report = Sample();

        string text = report.ToText();

        Assert.Contains("core=D:\\core\\xemu.exe", text, System.StringComparison.Ordinal);
        Assert.Contains("disc=(dashboard)", text, System.StringComparison.Ordinal);
        Assert.Contains("renderer=OPENGL scale=2 filtering=nearest", text, System.StringComparison.Ordinal);
        Assert.Contains("size=1280x960", text, System.StringComparison.Ordinal);
        Assert.Contains("audio=none", text, System.StringComparison.Ordinal);
        Assert.Contains("frames=1800 seconds=30.00 firstFrame=1.50 fps=60.00 stop=duration exit=0", text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_WritesCamelCaseFieldsForAutomation()
    {
        HeadlessReport report = Sample();

        using JsonDocument document = JsonDocument.Parse(report.ToJson());
        JsonElement root = document.RootElement;

        Assert.Equal("OPENGL", root.GetProperty("renderer").GetString());
        Assert.Equal(1800, root.GetProperty("frames").GetInt64());
        Assert.Equal(30.0, root.GetProperty("seconds").GetDouble(), 3);
        Assert.Equal(1.5, root.GetProperty("firstFrameSeconds").GetDouble(), 3);
        Assert.Equal("duration", root.GetProperty("stop").GetString());
        Assert.Equal(0, root.GetProperty("result").GetInt32());
        Assert.True(root.TryGetProperty("presentationFilter", out _));
    }

    private static HeadlessReport Sample()
    {
        return new HeadlessReport
        {
            Core = "D:\\core\\xemu.exe",
            Renderer = "OPENGL",
            SurfaceScale = 2,
            Filtering = "nearest",
            PresentationFilter = "scanline",
            Width = 1280,
            Height = 960,
            Audio = "none",
            Frames = 1800,
            Seconds = 30,
            FirstFrameSeconds = 1.5,
            Fps = 60,
            Stop = "duration",
            ExitCode = 0,
            Result = 0,
        };
    }
}
