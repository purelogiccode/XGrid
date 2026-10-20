using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace XGrid.GpuGl;

/// <summary>
/// Headless fps instrumentation for the GL backend (slice-3 milestone 2):
/// cumulative wall-clock on the emulation-thread GL submission path, split
/// into draw submission, clear submission, and flip readback. Pure data +
/// formatting only (no GL context) so the fps table in research notes §122
/// is backed by unit-tested counters rather than ad-hoc script timing.
/// All values are host wall-clock milliseconds accumulated with
/// <see cref="TimeSpan.TotalMilliseconds"/> (draws use the raw-tick overload:
/// a TimeSpan per draw is measurable on the menu's 2.76M-draw stream).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public struct GlBackendTimings
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    private double _drawMs;
    private double _clearMs;
    private double _readbackMs;
    private long _readbacks;

    /// <summary>Gets cumulative draw-submission milliseconds.</summary>
    public readonly double DrawMs => _drawMs;

    /// <summary>Gets cumulative clear-submission milliseconds.</summary>
    public readonly double ClearMs => _clearMs;

    /// <summary>Gets cumulative flip-readback (<c>ReadPixels</c>) milliseconds.</summary>
    public readonly double ReadbackMs => _readbackMs;

    /// <summary>Gets the number of readbacks performed.</summary>
    public readonly long Readbacks => _readbacks;

    /// <summary>Records one draw submission.</summary>
    public void AddDraw(TimeSpan elapsed)
    {
        _drawMs += elapsed.TotalMilliseconds;
    }

    /// <summary>Records one draw submission from a raw <see cref="Stopwatch.GetTimestamp"/> delta (no TimeSpan per draw).</summary>
    public void AddDrawTicks(long elapsedTicks)
    {
        _drawMs += elapsedTicks * TicksToMs;
    }

    /// <summary>Records one clear submission.</summary>
    public void AddClear(TimeSpan elapsed)
    {
        _clearMs += elapsed.TotalMilliseconds;
    }

    /// <summary>Records one flip readback.</summary>
    public void AddReadback(TimeSpan elapsed)
    {
        _readbackMs += elapsed.TotalMilliseconds;
        _readbacks++;
    }

    /// <summary>Clears all accumulators (mirrors backend <c>ResetState</c>).</summary>
    public void Reset()
    {
        _drawMs = 0;
        _clearMs = 0;
        _readbackMs = 0;
        _readbacks = 0;
    }

    /// <summary>
    /// Appends the <c>timed[drawMs=.. clearMs=.. readbackMs=.. readbacks=..]</c>
    /// fragment used by the backend <c>DebugSummary</c>.
    /// </summary>
    public readonly void AppendTo(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Append(CultureInfo.InvariantCulture,
            $" timed[drawMs={_drawMs:F1} clearMs={_clearMs:F1} readbackMs={_readbackMs:F1} readbacks={_readbacks}]");
    }
}
