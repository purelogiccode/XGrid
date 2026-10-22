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
