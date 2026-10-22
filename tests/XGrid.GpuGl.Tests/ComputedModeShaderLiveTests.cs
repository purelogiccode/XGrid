using System;
using System.Collections.Generic;
using System.Globalization;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// Live compile gate for the computed-mode shaders (B3 slice 2). Every
/// DOT_*/DOT_RFLCT_*/DOT_STR_* combination the combiner table can produce is
/// built and compiled+linked on the real driver through the generated source.
/// A string golden cannot catch a redeclaration or an undeclared identifier,
/// and that is exactly how every two-stage DOT_PRODUCT program failed to
/// compile before this gate existed: the second stage redeclared
/// <c>dotVal</c>/<c>dotOkA</c> and the texel line read an undeclared
/// <c>dotOk</c>, so the driver refused the program. Each case also compiles
/// the down-sized block layout a small fragment budget forces
/// (PshGlslGenerator.MaxTriangleCapacity), which is the other way this file
/// failed once. Returns early (pass) when no GL context exists.
/// </summary>
public sealed class ComputedModeShaderLiveTests(ITestOutputHelper output)
{
    private sealed class ArrayMemory : IGpuMemory
    {
        private readonly byte[] _data = new byte[0x10000];

        public void WritePhysical(uint address, uint value)
        {
            _data[address] = (byte)value;
            _data[address + 1] = (byte)(value >> 8);
            _data[address + 2] = (byte)(value >> 16);
            _data[address + 3] = (byte)(value >> 24);
        }

        public uint ReadPhysical(uint address)
        {
            return (uint)(_data[address] | (_data[address + 1] << 8) | (_data[address + 2] << 16) |
                          (_data[address + 3] << 24));
        }
    }

    private const string VertexSource =
        "#version 330 core\n" +
        "flat out int vTri;\n" +
        "uniform vec4 uTri[%BUFFER%];\n" +
        "void main() { vTri = gl_InstanceID; gl_Position = vec4(uTri[0].xy, 0.0, 1.0); }\n";

    [Fact]
    public void EveryComputedStageCombination_CompilesAndLinks()
    {
        // A backend instance is how the test reaches a live GL context
        // (GlProbe is internal to the assembly); its probe has already
        // compiled a real program on the same context.
        var memory = new ArrayMemory();
        bool created = GlBackendFactory.TryCreate(
            memory, windowEnabled: false, replaceSoftware: true, out IPgraphBackend? backend, out string why);
        if (!created || backend is not GlPgraphBackend live)
        {
            output.WriteLine("no GL context (" + why + "); live compile row not exercised.");
            return;
        }

        try
        {
            var failures = new List<string>();
            int rows = 0;
            foreach ((string name, uint stageProgram) in Combinations())
            {
                PshCombiner state = Combiner(stageProgram);
                // The full batch block, and the block the smallest min-spec
                // fragment budget (256 vec4) leaves once the guest-word pool
                // and the per-program reserve are taken out.
                foreach (int capacity in new[] { 1, 8, 32 })
                {
                    string source = PshGlslGenerator.BuildFragmentSource(legacy: false, state, capacity);
                    string vertex = VertexSource.Replace(
                        "%BUFFER%",
                        (capacity * 25).ToString(CultureInfo.InvariantCulture),
                        StringComparison.Ordinal);
                    string? error = live.BuildProgramForTest(vertex, source);
                    if (error is not null)
                    {
                        failures.Add($"{name} (block capacity {capacity}): {error}");
                    }

                    rows++;
                }
            }

            Assert.True(rows > 0, "no rows exercised");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
            output.WriteLine($"{rows} computed-mode programs compiled and linked");
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Every mode the computed families use in the stages the reference table
    /// allows, the two-dot-stage pairs that collided, and the cubemap row's
    /// own program (PROJECT2D, DOT_PRODUCT x2, DOT_REFLECT_SPECULAR).
    /// </summary>
    private static IEnumerable<(string Name, uint StageProgram)> Combinations()
    {
        yield return ("cubemap-reflect", 0x01u | (0x11u << 5) | (0x11u << 10) | (0x0Cu << 15));

        for (int mode = 0x10; mode <= 0x1Eu; mode++)
        {
            for (int slot = 0; slot < PshCombiner.TextureSlots; slot++)
            {
                if (!PshCombiner.IsModeValidForStage(mode, slot))
                {
                    continue;
                }

                yield return ($"mode-{mode:X2}-slot-{slot}", (uint)mode << (5 * slot));
            }
        }

        // Two dot stages in one program (the pair whose variables collided).
        for (int slot = 0; slot < 3; slot++)
        {
            yield return ($"dotpair-{slot}", (0x11u << (5 * slot)) | (0x11u << (5 * (slot + 1))));
        }

        yield return ("dotst-2", (0x11u << 5) | (0x13u << 10));
        yield return ("dotrflct-3", (0x11u << 5) | (0x11u << 10) | (0x0Cu << 15));
        yield return ("dotrflctdiff-3", (0x11u << 5) | (0x11u << 10) | (0x0Bu << 15));
        yield return ("dotstrcube-3", (0x11u << 5) | (0x11u << 10) | (0x12u << 15));
        yield return ("dotstr3d-3", (0x11u << 5) | (0x11u << 10) | (0x14u << 15));
        yield return ("cubemap-3", (0x11u << 5) | (0x11u << 10) | (0x02u << 15));
    }

    /// <summary>A fully populated combiner: every stage enabled with distinct inputs, so every body has real expressions.</summary>
    private static PshCombiner Combiner(uint stageProgram)
    {
        var state = new PshCombiner { Control = 2u, StageProgram = stageProgram, Final0 = 0x00000000u, Final1 = 0x00000001u };
        for (int i = 0; i < PshCombiner.TextureSlots; i++)
        {
            state.RgbInputs[i] = 0x1B1B0000u + (uint)i;
            state.RgbOutputs[i] = 0x00000000u;
            state.AlphaInputs[i] = 0x1B1B0000u + (uint)i;
            state.AlphaOutputs[i] = 0x00000000u;
        }

        return state;
    }
}
