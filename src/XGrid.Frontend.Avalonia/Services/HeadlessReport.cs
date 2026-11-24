using System;
using System.Globalization;
using System.Text.Json;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// One headless run's result: what booted, how long it ran, how many frames were
/// presented and why it stopped. The text form goes to the console; the JSON
/// form is written when <c>--summary</c> is given, so automation does not have
/// to parse log lines.
/// </summary>
public sealed record HeadlessReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Gets the emulator binary that was launched.</summary>
    public string Core { get; init; } = string.Empty;

    /// <summary>Gets the inserted disc image, or null for the dashboard.</summary>
    public string? Disc { get; init; }

    /// <summary>Gets the renderer key the core started.</summary>
    public string Renderer { get; init; } = string.Empty;

    /// <summary>Gets the internal surface scale.</summary>
    public int SurfaceScale { get; init; } = 1;

    /// <summary>Gets the core texture filtering key.</summary>
    public string Filtering { get; init; } = string.Empty;

    /// <summary>Gets the presentation filter key.</summary>
    public string PresentationFilter { get; init; } = string.Empty;

    /// <summary>Gets the core window width in physical pixels.</summary>
    public int Width { get; init; }

    /// <summary>Gets the core window height in physical pixels.</summary>
    public int Height { get; init; }

    /// <summary>Gets the audio backend used for the run.</summary>
    public string Audio { get; init; } = string.Empty;

    /// <summary>Gets the number of frames the core published.</summary>
    public long Frames { get; init; }

    /// <summary>Gets the wall-clock run length in seconds.</summary>
    public double Seconds { get; init; }

    /// <summary>Gets the time from process start to the first published frame, in seconds.</summary>
    public double FirstFrameSeconds { get; init; }

    /// <summary>Gets the average presented frames per second between first and last frame.</summary>
    public double Fps { get; init; }

    /// <summary>Gets the average interval between presented frames, in milliseconds.</summary>
    public double FrameTimeMs => Fps > 0 ? 1000.0 / Fps : 0;

    /// <summary>Gets why the run stopped: <c>duration</c>, <c>frames</c>, <c>core</c> or <c>cancel</c>.</summary>
    public string Stop { get; init; } = string.Empty;

    /// <summary>Gets the core's exit code.</summary>
    public int ExitCode { get; init; }

    /// <summary>Gets the run's own exit code (0 success, 1 core failure, 2 configuration, 3 no frames).</summary>
    public int Result { get; init; }

    /// <summary>Renders the report as the console lines a headless run prints.</summary>
    /// <returns>One line per field, newline-terminated.</returns>
    public string ToText()
    {
        string size = Width.ToString(CultureInfo.InvariantCulture)
                      + "x"
                      + Height.ToString(CultureInfo.InvariantCulture);
        return string.Join(
                   Environment.NewLine,
                   "xgrid: core=" + Core,
                   "xgrid: disc=" + (string.IsNullOrEmpty(Disc) ? "(dashboard)" : Disc),
                   "xgrid: renderer=" + Renderer
                   + " scale=" + SurfaceScale.ToString(CultureInfo.InvariantCulture)
                   + " filtering=" + Filtering
                   + " filter=" + PresentationFilter
                   + " size=" + size,
                   "xgrid: audio=" + Audio,
                   "xgrid: frames=" + Frames.ToString(CultureInfo.InvariantCulture)
                   + " seconds=" + Seconds.ToString("0.00", CultureInfo.InvariantCulture)
                   + " firstFrame=" + FirstFrameSeconds.ToString("0.00", CultureInfo.InvariantCulture)
                   + " fps=" + Fps.ToString("0.00", CultureInfo.InvariantCulture)
                   + " frameTime=" + FrameTimeMs.ToString("0.00", CultureInfo.InvariantCulture) + "ms"
                   + " stop=" + Stop
                   + " exit=" + ExitCode.ToString(CultureInfo.InvariantCulture)) + Environment.NewLine;
    }

    /// <summary>Serializes the report as indented JSON.</summary>
    /// <returns>The JSON document, newline-terminated.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine;
    }
}
