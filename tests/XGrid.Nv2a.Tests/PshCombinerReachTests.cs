using System;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// <see cref="PshCombiner.DescribeReach"/> - the `--gpu-time` combiner
/// histogram's read-set summary (decisions §228). It exists because two states
/// with identical word dumps can run different programs, and the corpus needs
/// to say which one a draw used.
/// </summary>
public sealed class PshCombinerReachTests
{
    /// <summary>
    /// The two 8-stage records the xgu bump_cubemap_reflect reflection phase
    /// programs (measured at 4.4B: `c=00011108 tm=0332` and
    /// `c=00011107 tm=1332`, 19.9M and 12.4M pixels). Both name no texture
    /// input beyond stage 0; their dot stages consume register constants.
    /// DescribeReach must say exactly that: the slots the stage modes sample,
    /// the sockets the input words name, and the lerp mode.
    /// </summary>
    [Theory]
    // 0x02200500: A = T2 (0xA), B = ZERO. 0x0000000C: A = ZERO, B = R0.
    [InlineData(0x00011108u, 0x0332u, 0x02200500u)]
    [InlineData(0x00011107u, 0x1332u, 0x0000000Cu)]
    public void DescribeReach_ReportsTheSocketsAProgramReads(uint control, uint stageProgram, uint stage0Inputs)
    {
        var combiner = new PshCombiner
        {
            Control = control,
            StageProgram = stageProgram,
            Final0 = 0x0C0B000Fu,
            Final1 = 0x0C011180u,
        };
        combiner.RgbInputs[0] = stage0Inputs;
        combiner.RgbOutputs[0] = 0x000C30CDu | 0x40000000u;
        combiner.RgbInputs[1] = 0x0D0D0000u;
        combiner.RgbOutputs[1] = 0x000005C0u;
        combiner.RgbInputs[2] = 0xCDCD0000u;
        combiner.RgbOutputs[2] = 0xC0000000u;
        for (int stage = 3; stage < PshCombiner.MaxStages; stage++)
        {
            combiner.RgbInputs[stage] = 0xCDCD0000u;
            combiner.RgbOutputs[stage] = 0u;
        }

        string reach = combiner.DescribeReach();

        // Stage 0's projector samples TM0; stage 0's T2 input adds TM2.
        bool viaT2 = (stage0Inputs >> 18) == 0x8;
        Assert.Contains(viaT2 ? "reach[tx=02]" : "reach[tx=]", reach, StringComparison.Ordinal);
        // Two stages write a register (R0, R1); the rest write none.
        Assert.Contains("stg=2/", reach, StringComparison.Ordinal);
        // The read sockets: stage 1 reads and writes R1 and the final block A
        // reads R0, which is what stage 2 (and stage 0's CD word) write.
        Assert.Contains("r1", reach, StringComparison.Ordinal);
        Assert.Contains("r0", reach, StringComparison.Ordinal);
        // The final record's lerp mode is reported alongside.
        Assert.Contains("lerp=0", reach, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stage the union of output flags comes from. The B3 row's 8-stage
    /// record carries BLUE_TO_ALPHA on several stages but only the last one
    /// (the stage that writes R0) reaches the final block; a row that reported
    /// only the union would claim six stages exercise the bit. Encoded as runs
    /// of consecutive writing stages sharing a flag word:
    /// <c>flags@=&lt;first&gt;[-&lt;last&gt;]:&lt;flags&gt;,…</c>.
    /// </summary>
    [Fact]
    public void DescribeReach_ReportsWhichStageCarriesEachFlagWord()
    {
        // Four R0-writing stages (MUX id 0xC in bits 8-11): 0 and 2 carry no
        // flag, 1 carries blue-to-alpha (0x40), 3 carries blue-to-alpha plus
        // the MUX bit (0x04) on its MUX colour write - only stage 3's MUX bit
        // reaches the final block, so a union alone would read as if two
        // stages exercised it. Flag bits live in bits 12-15. (The alpha word
        // stays 0 throughout: FlagBits ORs the RGB and alpha words, and an
        // alpha MUX write would add its own unmasked 0x40.)
        Variant named = new("named", s =>
        {
            s.RgbOutputs[0] = 0x0050000Cu;
            s.RgbOutputs[1] = 0x0054000Cu;
            s.RgbOutputs[2] = 0x0050000Cu;
            s.RgbOutputs[3] = 0x005C000Cu | (0x04u << 12) | (0x04u << 15);
        }, "flags=E0 flags@=0:00,1:40,2:00,3:E0 ");

        // Consecutive stages that share a flag word collapse into one run.
        Variant run = new("run", s =>
        {
            s.RgbOutputs[0] = 0x0054000Cu;
            s.RgbOutputs[1] = 0x0054000Cu;
        }, "flags=40 flags@=0-1:40 ");

        // No writer at all: an empty column instead of a misleading 00.
        Variant none = new("none", _ => { }, "flags=00 flags@=- ");

        foreach (Variant variant in new[] { named, run, none })
        {
            var combiner = new PshCombiner { Control = 0x00011108u, StageProgram = 0x0332u };
            combiner.RgbInputs[0] = 0x0D0D0000u;
            combiner.RgbInputs[1] = 0x0D0D0000u;
            variant.Set(combiner);
            string reach = combiner.DescribeReach();
            Assert.True(
                reach.Contains(variant.Expected, StringComparison.Ordinal),
                $"{variant.Name}: expected '{variant.Expected}' in '{reach}'");
        }
    }

    /// <summary>One stage-flag configuration and the <c>flags@=</c> text it must produce.</summary>
    /// <param name="Name">Label for the assertion message.</param>
    /// <param name="Set">Writes the stage output words.</param>
    /// <param name="Expected">The <c>flags=… flags@=… </c> substring expected.</param>
    private sealed record Variant(string Name, Action<PshCombiner> Set, string Expected);

    /// <summary>A program with no texture socket and one writer reports an empty slot column.</summary>
    [Fact]
    public void DescribeReach_EmptyWhenNoStageNamesATexture()
    {
        var combiner = new PshCombiner { Control = 1u, StageProgram = 0u };
        combiner.RgbInputs[0] = 0x04040000u;
        combiner.RgbOutputs[0] = 0x000000C0u;

        string reach = combiner.DescribeReach();
        Assert.StartsWith("reach[tx=] stg=1/1", reach, StringComparison.Ordinal);
        Assert.Contains("src=0,v0 ", reach, StringComparison.Ordinal);
    }
}
