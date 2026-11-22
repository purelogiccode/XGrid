using System.Collections.Generic;

namespace XGrid.Cpu.Tests;

/// <summary>Declarative CPU regression test (plan section 8.7).</summary>
public sealed class CpuTest
{
    /// <summary>Gets the test name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the initial register values (name to hex string).</summary>
    public Dictionary<string, string> Registers { get; init; } = [with(global::System.StringComparer.Ordinal)];

    /// <summary>Gets the instruction bytes as a hex string.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Gets the expected register values after execution (name to hex string).</summary>
    public Dictionary<string, string> Expected { get; init; } = [with(global::System.StringComparer.Ordinal)];
}
