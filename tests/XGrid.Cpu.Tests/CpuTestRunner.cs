using System;
using System.Collections.Generic;
using System.Globalization;
using XGrid.Memory;

namespace XGrid.Cpu.Tests;

/// <summary>Runs declarative CPU tests in a fresh 64 KB memory space.</summary>
public static class CpuTestRunner
{
    /// <summary>Address where test code is loaded.</summary>
    public const uint CodeAddress = 0x1000;

    /// <summary>Initial stack pointer.</summary>
    public const uint StackAddress = 0x8000;

    /// <summary>Runs the test and returns the result.</summary>
    public static CpuTestResult Run(CpuTest test, int maxInstructions = 10000)
    {
        ArgumentNullException.ThrowIfNull(test);

        var memory = new PhysicalMemory(0x10000);
        var bus = new AddressSpace(memory, UnmappedAccessPolicy.Throw);
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
        cpu.Context.Eflags = FlagBits.Reserved1;
        cpu.Context.Esp = StackAddress;

        foreach (KeyValuePair<string, string> pair in test.Registers)
        {
            SetRegister(cpu.Context, pair.Key, ParseUInt32(pair.Value));
        }

        byte[] code = ParseBytes(test.Code);
        for (int i = 0; i < code.Length; i++)
        {
            memory.Write8(CodeAddress + (uint)i, code[i]);
        }

        cpu.Context.Eip = CodeAddress;

        var failures = new List<string>();
        try
        {
            cpu.Execute(maxInstructions);
        }
        catch (UnsupportedInstructionException exception)
        {
            failures.Add(exception.Message);
        }

        var actual = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in test.Expected)
        {
            uint expected = ParseUInt32(pair.Value);
            uint observed = GetRegister(cpu.Context, pair.Key);
            actual[pair.Key] = observed;

            if (observed != expected)
            {
                failures.Add($"{pair.Key}: expected 0x{expected:X8}, got 0x{observed:X8}");
            }
        }

        return new CpuTestResult(test.Name, failures.Count == 0, failures, actual);
    }

    /// <summary>Parses a hex string ("0x..." or bare hex).</summary>
    public static uint ParseUInt32(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return uint.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    /// <summary>Parses a hex byte string such as "01D8".</summary>
    public static byte[] ParseBytes(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        string text = code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (text.Length % 2 != 0)
        {
            throw new FormatException("Instruction hex string must have an even length.");
        }

        byte[] bytes = new byte[text.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(text.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return bytes;
    }

    private static void SetRegister(XboxContext context, string name, uint value)
    {
        switch (name.ToUpperInvariant())
        {
            case "EAX":
                context.WriteRegister(Gpr.Eax, value);
                break;
            case "ECX":
                context.WriteRegister(Gpr.Ecx, value);
                break;
            case "EDX":
                context.WriteRegister(Gpr.Edx, value);
                break;
            case "EBX":
                context.WriteRegister(Gpr.Ebx, value);
                break;
            case "ESP":
                context.WriteRegister(Gpr.Esp, value);
                break;
            case "EBP":
                context.WriteRegister(Gpr.Ebp, value);
                break;
            case "ESI":
                context.WriteRegister(Gpr.Esi, value);
                break;
            case "EDI":
                context.WriteRegister(Gpr.Edi, value);
                break;
            case "EIP":
                context.Eip = value;
                break;
            case "EFLAGS":
                context.Eflags = value;
                break;
            default:
                throw new FormatException($"Unknown register '{name}'.");
        }
    }

    private static uint GetRegister(XboxContext context, string name)
    {
        return name.ToUpperInvariant() switch
        {
            "EAX" => context.ReadRegister(Gpr.Eax),
            "ECX" => context.ReadRegister(Gpr.Ecx),
            "EDX" => context.ReadRegister(Gpr.Edx),
            "EBX" => context.ReadRegister(Gpr.Ebx),
            "ESP" => context.ReadRegister(Gpr.Esp),
            "EBP" => context.ReadRegister(Gpr.Ebp),
            "ESI" => context.ReadRegister(Gpr.Esi),
            "EDI" => context.ReadRegister(Gpr.Edi),
            "EIP" => context.Eip,
            "EFLAGS" => context.Eflags,
            _ => throw new FormatException($"Unknown register '{name}'."),
        };
    }
}
