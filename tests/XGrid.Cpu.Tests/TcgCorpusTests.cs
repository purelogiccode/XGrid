using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Cpu.Tcg;
using XGrid.Memory;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.Cpu.Tests;

/// <summary>
/// P1S3 gate: the same independent-instruction corpus that pins the interpreter
/// runs through the managed TCG engine. Every instruction is decoded and offered
/// to the translator; a translated instruction executes on the IR interpreter,
/// anything else falls back to the exact interpreter and is counted. The final
/// architectural state (EIP, GPRs, masked EFLAGS, the FXSAVE image and the case's
/// data page) must still match the independent oracle byte for byte.
/// </summary>
public sealed class TcgCorpusTests(ITestOutputHelper output)
{
    [Fact]
    public void InstructionCorpus_RunsThroughTheTcgEngine()
    {
        string root = CpuOracleTests.FindCorpusDirectory();
        CpuOracleDocument document = CpuOracleLoader.Load(File.ReadAllText(Path.Combine(root, "int.json")));
        byte[] records = File.ReadAllBytes(Path.Combine(root, "int.dat"));

        var memory = new PhysicalMemory(CpuOracleTests.MemorySizeFor(document.Cases.Count));
        var bus = new AddressSpace(memory, UnmappedAccessPolicy.Throw);
        memory.Write32(0x001F1000, 0x00001F80);

        var failures = new List<string>();
        var fallbackReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int translated = 0;
        int fallbacks = 0;

        foreach (CpuOracleCase testCase in document.Cases)
        {
            string? failure = RunCase(memory, bus, testCase, records, document.RecordSize, out int caseTranslated, out int caseFallbacks, fallbackReasons);
            if (failure is not null)
            {
                failures.Add($"{testCase.Name}: {failure}");
            }

            translated += caseTranslated;
            fallbacks += caseFallbacks;
        }

        output.WriteLine($"translated={translated} fallback={fallbacks}");
        foreach (KeyValuePair<string, int> reason in fallbackReasons)
        {
            output.WriteLine($"fallback {reason.Value,4} {reason.Key}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(translated > 0, "no corpus instruction was translated");
    }

    private static string? RunCase(
        PhysicalMemory memory,
        AddressSpace bus,
        CpuOracleCase testCase,
        byte[] records,
        int recordSize,
        out int translated,
        out int fallbacks,
        SortedDictionary<string, int> fallbackReasons)
    {
        translated = 0;
        fallbacks = 0;
        byte[] code = CpuTestRunner.ParseBytes(testCase.Code);
        uint codeAddress = CpuOracleTests.CodeBase + ((uint)testCase.Record * CpuOracleTests.CaseStride);
        memory.WriteSpan(codeAddress, code);
        memory.WriteSpan(CpuOracleTests.FxsaveAddress, CpuOracleTests.FxsaveSentinel);

        var cpu = new XboxCpu(bus);
        cpu.Reset();
        cpu.Context.Cr0 = 1;
        cpu.Context.CodeSegment32 = true;
        cpu.Context.SetSegment(SegmentRegister.Cs, 0x08, 0);
        cpu.Context.SetSegment(SegmentRegister.Ds, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Ss, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Es, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Fs, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Gs, 0x10, 0);
        cpu.Context.Eflags = CpuTestRunner.ParseUInt32(testCase.Eflags) | FlagBits.Reserved1;

        foreach (KeyValuePair<string, string> register in testCase.Registers)
        {
            CpuOracleTests.SetRegister(cpu.Context, register.Key, CpuTestRunner.ParseUInt32(register.Value));
        }

        cpu.Context.Eip = codeAddress;

        Span<byte> window = stackalloc byte[Decoder.MaxInstructionLength];
        for (int step = 0; step < 256; step++)
        {
            uint eip = cpu.Context.Eip;
            for (int i = 0; i < window.Length; i++)
            {
                window[i] = bus.Read8(eip + (uint)i);
            }

            DecodedInstruction decoded = Decoder.Decode(
                window, cpu.Context.DefaultOperandSize, cpu.Context.DefaultAddressSize);
            if (decoded.IsValid && decoded.Mnemonic == Mnemonic.Interrupt && decoded.Operand1.Immediate == 0x30)
            {
                break;
            }

            var builder = new TcgBuilder(eip);
            bool terminates = false;
            bool isTranslated = decoded.IsValid
                && I386Translator.TryTranslate(decoded, eip, builder, out terminates, out string? reason);
            if (!isTranslated)
            {
                fallbacks++;
                string key = decoded.IsValid ? decoded.Mnemonic.ToString() : "invalid";
                fallbackReasons[key] = fallbackReasons.TryGetValue(key, out int count) ? count + 1 : 1;
                cpu.Step();
                continue;
            }

            if (!terminates)
            {
                builder.SetEip(eip + (uint)decoded.Length);
                builder.Exit();
            }

            var gprs = new uint[8];
            for (int i = 0; i < 8; i++)
            {
                gprs[i] = cpu.Context.ReadRegister((Gpr)i);
            }

            var segments = new uint[6];
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = cpu.Context.GetSegmentBase((SegmentRegister)i);
            }

            TcgResult result = TcgInterpreter.Execute(
                builder.Build(),
                gprs,
                cpu.Context.Eflags,
                segments,
                new TcgTestMemory(bus),
                cpu.Context.Fpu);
            for (int i = 0; i < 8; i++)
            {
                cpu.Context.WriteRegister((Gpr)i, result.Registers[i]);
            }

            cpu.Context.Eflags = result.Eflags;
            cpu.Context.Eip = result.Eip;
            translated++;
        }

        if (cpu.Context.Eip != CpuTestRunner.ParseUInt32(testCase.Expect.Eip))
        {
            return $"EIP expected 0x{CpuTestRunner.ParseUInt32(testCase.Expect.Eip):X8}, got 0x{cpu.Context.Eip:X8}";
        }

        foreach (KeyValuePair<string, string> register in testCase.Expect.Registers)
        {
            uint expected = CpuTestRunner.ParseUInt32(register.Value);
            uint actual = CpuOracleTests.GetRegister(cpu.Context, register.Key);
            if (actual != expected)
            {
                return $"{register.Key.ToUpperInvariant()} expected 0x{expected:X8}, got 0x{actual:X8}";
            }
        }

        uint expectedFlags = CpuTestRunner.ParseUInt32(testCase.Expect.Eflags);
        uint flagsMask = CpuTestRunner.ParseUInt32(testCase.FlagsMask);
        if ((cpu.Context.Eflags & flagsMask) != (expectedFlags & flagsMask))
        {
            return $"EFLAGS expected 0x{expectedFlags & flagsMask:X8}, got 0x{cpu.Context.Eflags & flagsMask:X8}";
        }

        ReadOnlySpan<byte> expectedRecord = new(records, testCase.Record * recordSize, recordSize);
        Span<byte> actualRecord = stackalloc byte[CpuOracleTests.FxsaveSize + CpuOracleTests.PageDumpSize];
        memory.GetSpan(CpuOracleTests.FxsaveAddress, CpuOracleTests.FxsaveSize).CopyTo(actualRecord[..CpuOracleTests.FxsaveSize]);
        memory.GetSpan(
                CpuOracleTests.PageBase + ((uint)testCase.Record * CpuOracleTests.PageStride),
                CpuOracleTests.PageDumpSize)
            .CopyTo(actualRecord[CpuOracleTests.FxsaveSize..]);

        for (int i = 0; i < CpuOracleTests.FxsaveSize; i++)
        {
            if (!CpuOracleTests.IsComparedFxsaveByte(i))
            {
                continue;
            }

            if (actualRecord[i] != expectedRecord[i])
            {
                return $"FXSAVE +0x{i:X2} expected 0x{expectedRecord[i]:X2}, got 0x{actualRecord[i]:X2}";
            }
        }

        for (int i = CpuOracleTests.FxsaveSize; i < actualRecord.Length; i++)
        {
            if (actualRecord[i] != expectedRecord[i])
            {
                return $"memory +0x{i - CpuOracleTests.FxsaveSize:X2} expected 0x{expectedRecord[i]:X2}, got 0x{actualRecord[i]:X2}";
            }
        }

        return null;
    }
}
