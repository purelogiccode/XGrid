using System.Collections.Generic;

namespace XGrid.Cpu.Tests;

/// <summary>Result of running a <see cref="CpuTest"/>.</summary>
/// <remarks>Creates a result.</remarks>
public sealed class CpuTestResult(string name, bool passed, IReadOnlyList<string> failures,
    IReadOnlyDictionary<string, uint> actual)
{

    /// <summary>Gets the test name.</summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    // ReSharper disable once UnusedMember.Global
    public string Name { get; } = name;

    /// <summary>Gets whether the test passed.</summary>
    public bool Passed { get; } = passed;

    /// <summary>Gets the failure messages.</summary>
    public IReadOnlyList<string> Failures { get; } = failures;

    /// <summary>Gets the observed register values.</summary>
    public IReadOnlyDictionary<string, uint> Actual { get; } = actual;
}
