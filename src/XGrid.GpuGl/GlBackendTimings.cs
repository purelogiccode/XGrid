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

    /// <summary>Gets cumulative draw-submission milliseconds.</summary>
    public double DrawMs { get; private set; }

    /// <summary>Gets the part of <see cref="DrawMs"/> spent submitting batched draw commands (one instanced draw per batch).</summary>
    public double FlushMs { get; private set; }

    /// <summary>Gets cumulative clear-submission milliseconds.</summary>
    public double ClearMs { get; private set; }

    /// <summary>Gets cumulative flip-readback (<c>ReadPixels</c>) milliseconds.</summary>
    public double ReadbackMs { get; private set; }

    /// <summary>Gets the number of readbacks performed.</summary>
    public long Readbacks { get; private set; }

    /// <summary>Records one draw submission.</summary>
    public void AddDraw(TimeSpan elapsed)
    {
        DrawMs += elapsed.TotalMilliseconds;
    }

    /// <summary>Records one draw submission from a raw <see cref="Stopwatch.GetTimestamp"/> delta (no TimeSpan per draw).</summary>
    public void AddDrawTicks(long elapsedTicks)
    {
        DrawMs += elapsedTicks * TicksToMs;
    }

    /// <summary>
    /// Records one batched draw-command submission from a raw tick delta. The
    /// flush is part of draw submission (it is where a batched draw actually
    /// reaches GL), so it is added to <see cref="DrawMs"/> as well as to the
    /// <see cref="FlushMs"/> subset — <c>drawMs</c> stays comparable across
    /// batched and unbatched runs.
    /// </summary>
    public void AddBatchFlushTicks(long elapsedTicks)
    {
        double ms = elapsedTicks * TicksToMs;
        FlushMs += ms;
        DrawMs += ms;
    }

    /// <summary>Records one clear submission.</summary>
    public void AddClear(TimeSpan elapsed)
    {
        ClearMs += elapsed.TotalMilliseconds;
    }

    /// <summary>Records one flip readback.</summary>
    public void AddReadback(TimeSpan elapsed)
    {
        ReadbackMs += elapsed.TotalMilliseconds;
        Readbacks++;
    }

    /// <summary>Clears all accumulators (mirrors backend <c>ResetState</c>).</summary>
    public void Reset()
    {
        DrawMs = 0;
        ClearMs = 0;
        ReadbackMs = 0;
        FlushMs = 0;
        Readbacks = 0;
    }

    /// <summary>
    /// Appends the <c>timed[drawMs=.. clearMs=.. readbackMs=.. readbacks=..]</c>
    /// fragment used by the backend <c>DebugSummary</c>.
    /// </summary>
    public readonly void AppendTo(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Append(CultureInfo.InvariantCulture,
            $" timed[drawMs={DrawMs:F1} flushMs={FlushMs:F1} clearMs={ClearMs:F1} readbackMs={ReadbackMs:F1} readbacks={Readbacks}]");
    }
}
