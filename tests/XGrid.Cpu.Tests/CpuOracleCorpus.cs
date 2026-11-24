using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XGrid.Cpu.Tests;

/// <summary>
/// Differential CPU corpus produced by <c>tools/cpu-oracle/oracle.py</c>.
/// The expectations come from an independent x86 implementation (QEMU TCG via
/// Unicorn, or a QEMU system machine), never from XGrid itself.
/// </summary>
public sealed class CpuOracleDocument
{
    /// <summary>Gets the corpus format version.</summary>
    public int Version { get; init; }

    /// <summary>Gets the byte size of one record in the companion binary file.</summary>
    public int RecordSize { get; init; }

    /// <summary>Gets the cases.</summary>
    public List<CpuOracleCase> Cases { get; init; } = [];
}

/// <summary>One oracle case: code, initial state, and the expected final state.</summary>
public sealed class CpuOracleCase
{
    /// <summary>Gets the case name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the code bytes placed at the case's fixed address.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the initial general-purpose registers.</summary>
    public Dictionary<string, string> Registers { get; init; } = [with(System.StringComparer.Ordinal)];

    /// <summary>Gets the initial EFLAGS value.</summary>
    public string Eflags { get; init; } = "0x00000000";

    /// <summary>Gets the mask of architecturally defined EFLAGS bits for this case.</summary>
    public string FlagsMask { get; init; } = "0x000008D5";

    /// <summary>Gets the expected final state.</summary>
    public CpuOracleExpectation Expect { get; init; } = new();

    /// <summary>Gets the index of this case's record in the binary file.</summary>
    public int Record { get; set; }
}

/// <summary>Expected final architectural state.</summary>
public sealed class CpuOracleExpectation
{
    /// <summary>Gets the expected general-purpose registers.</summary>
    public Dictionary<string, string> Registers { get; init; } = [with(System.StringComparer.Ordinal)];

    /// <summary>Gets the expected EFLAGS value.</summary>
    public string Eflags { get; init; } = "0x00000000";

    /// <summary>Gets the expected final EIP (the stop marker).</summary>
    public string Eip { get; init; } = "0x00000000";
}

/// <summary>Loads oracle corpora (AOT-safe).</summary>
public static class CpuOracleLoader
{
    /// <summary>Parses a corpus document.</summary>
    public static CpuOracleDocument Load(string json)
    {
        System.ArgumentNullException.ThrowIfNull(json);
        CpuOracleDocument document = JsonSerializer.Deserialize(json, CpuOracleJsonContext.Default.CpuOracleDocument)
               ?? throw new System.FormatException("CPU oracle JSON did not contain an object.");

        // The record index is the case's position: the binary sidecar stores
        // one fixed-size record per case in the same order.
        for (int i = 0; i < document.Cases.Count; i++)
        {
            document.Cases[i].Record = i;
        }

        return document;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(CpuOracleDocument))]
internal sealed partial class CpuOracleJsonContext : JsonSerializerContext;
