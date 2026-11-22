using System;
using System.Collections.Generic;

namespace XGrid.Cpu.Tcg;

/// <summary>
/// The opt-in translator-coverage probe (P1S3 acceptance). While disabled it is
/// one volatile read per decoded instruction; while enabled (<c>XGRID_TCG_COVERAGE=1</c>)
/// it asks the i386 translator whether the instruction would translate and
/// tallies the answer. It never changes CPU state.
/// </summary>
public static class TcgCoverage
{
    private static readonly Dictionary<string, long> FallbackReasons = new(StringComparer.Ordinal);
    private static readonly System.Threading.Lock Gate = new();

    /// <summary>Gets whether the probe is enabled (the environment variable is read once).</summary>
    public static bool Enabled { get; } = string.Equals(
        Environment.GetEnvironmentVariable("XGRID_TCG_COVERAGE"),
        "1",
        StringComparison.Ordinal);

    private static long _seen;

    /// <summary>Gets the deterministic sampling stride (XGRID_TCG_COVERAGE_SAMPLE, default 1).</summary>
    public static int SampleRate { get; } = ParseRate();

    /// <summary>Gets the translated instruction count.</summary>
    public static long Translated { get; private set; }

    /// <summary>Gets the fallback instruction count.</summary>
    public static long Fallback { get; private set; }

    /// <summary>Records one decoded instruction.</summary>
    /// <param name="instruction">The decoded instruction.</param>
    /// <param name="eip">Its guest EIP.</param>
    public static void Observe(in DecodedInstruction instruction, uint eip)
    {
        if (!Enabled || (_seen++ & (SampleRate - 1)) != 0)
        {
            return;
        }

        var builder = new TcgBuilder(eip);
        bool translated = I386Translator.TryTranslate(instruction, eip, builder, out _, out string? reason);
        lock (Gate)
        {
            if (translated)
            {
                Translated++;
                return;
            }

            Fallback++;
            string key = reason ?? "unknown";
            FallbackReasons[key] = FallbackReasons.TryGetValue(key, out long count) ? count + 1 : 1;
        }
    }

    /// <summary>Renders the probe summary (one line plus the top fallback reasons).</summary>
    /// <returns>The summary text.</returns>
    public static string Describe()
    {
        if (!Enabled)
        {
            return "tcg-coverage: disabled";
        }

        var reasons = new List<KeyValuePair<string, long>>(FallbackReasons);
        reasons.Sort(static (a, b) => b.Value.CompareTo(a.Value));
        var text = new System.Text.StringBuilder();
        long total = Translated + Fallback;
        double share = total == 0 ? 0 : (double)Translated / total;
        text.Append("tcg-coverage: translated=").Append(Translated)
            .Append(" fallback=").Append(Fallback)
            .Append(" share=").Append(share.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture));
        for (int i = 0; i < reasons.Count && i < 12; i++)
        {
            text.Append(" | ").Append(reasons[i].Key).Append('=').Append(reasons[i].Value);
        }

        return text.ToString();
    }

    private static int ParseRate()
    {
        string? value = Environment.GetEnvironmentVariable("XGRID_TCG_COVERAGE_SAMPLE");
        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int rate) || rate < 1)
        {
            return 1;
        }

        int power = 1;
        while (power < rate && power < 1 << 20)
        {
            power <<= 1;
        }

        return power;
    }
}
