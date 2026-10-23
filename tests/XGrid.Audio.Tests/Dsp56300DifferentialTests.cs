using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using XGrid.Audio.Dsp;
using Xunit;

namespace XGrid.Audio.Tests;

/// <summary>
/// Differential tests for the ported DSP56300 interpreter.  The vectors in
/// <c>TestData/dsp-vectors.txt</c> were produced by the xemu 0.8.139 reference
/// interpreter built standalone (see <c>tools/dsp-oracle/README.md</c>): the
/// same seed, program words and step count are replayed here and the complete
/// resulting state must match.
/// </summary>
public sealed class Dsp56300DifferentialTests
{
    private const uint Mask32 = 0xFFFFFFFF;
    private const int RegSp = 0x3B;
    private const int RegSr = 0x39;
    private const int RegLa = 0x3E;
    private const int RegLc = 0x3F;
    private const int RegSsh = 0x3C;
    private const int RegSsl = 0x3D;
    private const int RegR0 = 0x10;
    private const int RegN0 = 0x18;
    private const int RegM0 = 0x20;

    private static readonly uint[] MValues = [0xFFFF, 0x0F, 0x1F, 0x3F];

    /// <summary>One differential vector.</summary>
    private sealed record Vector(
        uint[] Words,
        uint Seed,
        int Steps,
        string State,
        IReadOnlyDictionary<int, uint> Registers,
        int Interrupts)
    {
        /// <summary>Gets a value indicating whether the program is a parallel-move word.</summary>
        public bool IsParallelMove => Words.Length == 1 && Words[0] >= 0x100000;

        /// <summary>Gets a value indicating whether the program is one instruction.</summary>
        public bool IsSingleInstruction => Words.Length == 1;
    }

    [Fact]
    public void DecodeTableVectors_MatchTheReferenceInterpreter()
    {
        List<Vector> vectors = LoadVectors().Where(v => v.IsSingleInstruction && !v.IsParallelMove).ToList();

        // Every decode-table entry with a handler contributes up to three vectors.
        Assert.True(vectors.Count >= 200, $"only {vectors.Count} decode vectors");
        AssertAllMatch(vectors);
    }

    [Fact]
    public void AluTableVectors_MatchTheReferenceInterpreter()
    {
        List<Vector> vectors = LoadVectors().Where(v => v.IsParallelMove).ToList();

        // Two vectors per defined ALU opcode; the reference marks four entries
        // (0x04, 0x08, 0x0c, 0x15) undefined.
        HashSet<uint> covered = [.. vectors.Select(v => v.Words[0] & 0xFF)];
        Assert.Equal(252, covered.Count);
        foreach (uint undefined in new uint[] { 0x04, 0x08, 0x0C, 0x15 })
        {
            Assert.DoesNotContain(undefined, covered);
        }

        AssertAllMatch(vectors);
    }

    [Fact]
    public void ProgramVectors_MatchTheReferenceInterpreter()
    {
        List<Vector> vectors = LoadVectors().Where(v => v.Words.Length > 1 && v.Interrupts == 0).ToList();

        Assert.True(vectors.Count >= 120, $"only {vectors.Count} program vectors");
        AssertAllMatch(vectors);
    }

    [Fact]
    public void InterruptVectors_MatchTheReferenceInterpreter()
    {
        List<Vector> vectors = LoadVectors().Where(v => v.Interrupts != 0).ToList();

        Assert.Equal(24, vectors.Count);
        AssertAllMatch(vectors);
    }

    [Fact]
    public void AllVectors_MatchTheReferenceInterpreter()
    {
        List<Vector> vectors = LoadVectors();
        Assert.True(vectors.Count >= 800, $"only {vectors.Count} vectors");
        AssertAllMatch(vectors);
    }

    [Fact]
    public void Rep_RepeatsTheNextInstruction()
    {
        // rep #3 ; inc a
        var core = new Dsp56300Core(isGp: true);
        core.State.ExceptionDebugging = false;
        core.State.Pram[0] = 0x0603A0;
        core.State.Pram[1] = 0x000008;

        for (int i = 0; i < 6; i++)
        {
            core.ExecuteInstruction();
        }

        Assert.Equal(3u, core.State.Registers[Dsp56300State.RegA0]);
    }

    [Fact]
    public void Do_RunsTheLoopBodyUntilTheCounterIsZero()
    {
        // do #3, P:3 ; P:3 ; inc a ; nop
        var core = new Dsp56300Core(isGp: true);
        core.State.ExceptionDebugging = false;
        core.State.Pram[0] = 0x060380; // do #3, P:3
        core.State.Pram[1] = 0x000003; // loop end address
        core.State.Pram[2] = 0x000008; // inc a
        core.State.Pram[3] = 0x000000; // nop (last instruction of the loop)

        for (int i = 0; i < 16; i++)
        {
            core.ExecuteInstruction();
        }

        Assert.Equal(3u, core.State.Registers[Dsp56300State.RegA0]);
        Assert.Equal(0u, core.State.Registers[Dsp56300State.RegSr] & (1u << Dsp56300State.SrLf));
    }

    [Fact]
    public void Jsr_Rts_PushesAndPopsTheReturnAddress()
    {
        // jsr P:2 ; inc a ; rts
        var core = new Dsp56300Core(isGp: true);
        core.State.ExceptionDebugging = false;
        core.State.Pram[0] = 0x0D0002; // jsr P:2
        core.State.Pram[1] = 0x000008; // inc a
        core.State.Pram[2] = 0x00000C; // rts

        core.ExecuteInstruction();
        Assert.Equal(2u, core.State.Pc);
        Assert.Equal(1u, core.State.Registers[Dsp56300State.RegSp] & 0xF);

        core.ExecuteInstruction();
        Assert.Equal(1u, core.State.Pc);

        core.ExecuteInstruction();
        Assert.Equal(2u, core.State.Pc);
        Assert.Equal(1u, core.State.Registers[Dsp56300State.RegA0]);
    }

    [Fact]
    public void UndefinedInstruction_CountsAndDoesNotAdvance()
    {
        var core = new Dsp56300Core(isGp: true);
        core.State.ExceptionDebugging = false;
        core.State.Pram[0] = 0x000181; // matches no decode-table entry

        core.ExecuteInstruction();

        Assert.Equal(1, core.UndefinedInstructions);
        Assert.Equal(0u, core.State.Pc);
        Assert.Equal(102, core.State.InstrCycle);
    }

    private static void AssertAllMatch(List<Vector> vectors)
    {
        foreach (Vector vector in vectors)
        {
            (string state, uint[] registers) = Replay(vector);
            string expected = vector.State;
            if (!string.Equals(expected, state, StringComparison.Ordinal))
            {
                Assert.Fail($"state mismatch for seed 0x{vector.Seed:X} words {Words(vector)}\nexpected: {expected}\nactual:   {state}");
            }

            uint[] initial = SetupState(vector.Seed).Registers;
            for (int i = 0; i < registers.Length; i++)
            {
                uint want = vector.Registers.TryGetValue(i, out uint changed) ? changed : initial[i];
                if (want != registers[i])
                {
                    Assert.Fail($"register 0x{i:X2} mismatch for seed 0x{vector.Seed:X} words {Words(vector)}: expected 0x{want:X6}, actual 0x{registers[i]:X6}");
                }
            }
        }
    }

    private static string Words(Vector vector) =>
        string.Join(',', vector.Words.Select(w => w.ToString("x6", CultureInfo.InvariantCulture)));

    private static (string State, uint[] Registers) Replay(Vector vector)
    {
        var core = new Dsp56300Core(isGp: true);
        Dsp56300State state = core.State;
        state.ExceptionDebugging = false;
        state.ReadPeripheral = address => (address * 2654435761u) & 0xFFFFFF;

        (uint[] registers, (uint Address, uint Value)[] xw, (uint Address, uint Value)[] yw,
            (uint Address, uint Value)[] mw, uint[] pfill) = SetupState(vector.Seed);

        Array.Copy(registers, state.Registers, registers.Length);
        foreach ((uint address, uint value) in xw)
        {
            state.Xram[address] = value;
        }

        foreach ((uint address, uint value) in yw)
        {
            state.Yram[address] = value;
        }

        foreach ((uint address, uint value) in mw)
        {
            state.MixBuffer[address] = value;
        }

        for (int i = 0; i < pfill.Length; i++)
        {
            state.Pram[i] = pfill[i];
        }

        for (int i = 0; i < vector.Words.Length; i++)
        {
            state.Pram[i] = vector.Words[i];
        }

        state.Pc = 0;
        for (int i = 0; i < 4; i++)
        {
            if ((vector.Interrupts & (1 << i)) != 0)
            {
                core.AddInterrupt(i);
            }
        }

        for (int i = 0; i < vector.Steps; i++)
        {
            core.ExecuteInstruction();
            state.CycleCount += state.InstrCycle;
        }

        uint hash = 2166136261u;
        hash = HashWords(hash, state.Xram);
        hash = HashWords(hash, state.Yram);
        hash = HashWords(hash, state.Pram);
        hash = HashWords(hash, state.MixBuffer);
        hash = HashWords(hash, state.Stack[0]);
        hash = HashWords(hash, state.Stack[1]);

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"pc={state.Pc:x8} sr={state.Registers[Dsp56300State.RegSr]:x4} ic={state.InstrCycle} len={state.CurrentInstructionLength} cyc={state.CycleCount} h={hash:x8}");
        return (line, state.Registers.ToArray());
    }

    private static uint HashWords(uint hash, uint[] data)
    {
        foreach (uint value in data)
        {
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (value >> shift) & 0xFF;
                hash *= 16777619u;
            }
        }

        return hash;
    }

    private static uint Next(ref uint state)
    {
        state ^= (state << 13) & Mask32;
        state ^= state >> 17;
        state ^= (state << 5) & Mask32;
        return state & Mask32;
    }

    /// <summary>Mirrors the generator's deterministic state setup.</summary>
    private static (uint[] Registers, (uint Address, uint Value)[] X, (uint Address, uint Value)[] Y,
        (uint Address, uint Value)[] M, uint[] PFill) SetupState(uint seed)
    {
        uint rng = seed;
        uint[] registers = new uint[64];
        for (int i = 0; i < registers.Length; i++)
        {
            registers[i] = Next(ref rng);
        }

        for (int i = 0; i < 8; i++)
        {
            registers[RegR0 + i] = Next(ref rng) % 1024;
        }

        for (int i = 0; i < 8; i++)
        {
            registers[RegN0 + i] = Next(ref rng) % 32;
        }

        for (int i = 0; i < 8; i++)
        {
            registers[RegM0 + i] = MValues[Next(ref rng) & 3];
        }

        registers[RegSp] = Next(ref rng) % 9;
        registers[RegSsh] = 0;
        registers[RegSsl] = 0;
        registers[RegLa] = 0;
        registers[RegLc] = 0;
        registers[RegSr] &= 0x7FFF;

        var xw = new (uint, uint)[64];
        for (int i = 0; i < xw.Length; i++)
        {
            uint address = Next(ref rng) & 0xFFF;
            xw[i] = (address, Next(ref rng) & 0xFFFFFF);
        }

        var yw = new (uint, uint)[64];
        for (int i = 0; i < yw.Length; i++)
        {
            uint address = Next(ref rng) & 0x7FF;
            yw[i] = (address, Next(ref rng) & 0xFFFFFF);
        }

        var mw = new (uint, uint)[32];
        for (int i = 0; i < mw.Length; i++)
        {
            uint address = Next(ref rng) & 0x3FF;
            mw[i] = (address, Next(ref rng) & 0xFFFFFF);
        }

        uint[] pf = new uint[32];
        for (int i = 0; i < pf.Length; i++)
        {
            pf[i] = Next(ref rng) & 0xFFFFFF;
        }

        return (registers, xw, yw, mw, pf);
    }

    private static List<Vector> LoadVectors()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "dsp-vectors.txt");
        var vectors = new List<Vector>();
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split('|');
            uint[] words = [.. parts[0].Split(',').Select(w => uint.Parse(w, NumberStyles.HexNumber, CultureInfo.InvariantCulture))];
            uint seed = uint.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int steps = int.Parse(parts[2], CultureInfo.InvariantCulture);

            var registers = new Dictionary<int, uint>();
            if (parts[4].Length > 0)
            {
                foreach (string pair in parts[4].Split(','))
                {
                    string[] kv = pair.Split(':');
                    registers.Add(
                        int.Parse(kv[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        uint.Parse(kv[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                }
            }

            int interrupts = parts.Length > 5 ? int.Parse(parts[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 0;
            vectors.Add(new Vector(words, seed, steps, parts[3], registers, interrupts));
        }

        return vectors;
    }
}
