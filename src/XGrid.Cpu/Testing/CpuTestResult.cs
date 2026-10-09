using System.Collections.Generic;

namespace XGrid.Cpu.Testing;

/// <summary>Result of running a <see cref="CpuTest"/>.</summary>
public sealed class CpuTestResult
{
    /// <summary>Creates a result.</summary>
    public CpuTestResult(string name, bool passed, IReadOnlyList<string> failures,
        IReadOnlyDictionary<string, uint> actual)
    {
        Name = name;
        Passed = passed;
        Failures = failures;
        Actual = actual;
    }

    /// <summary>Gets the test name.</summary>
    public string Name { get; }

    /// <summary>Gets whether the test passed.</summary>
    public bool Passed { get; }

    /// <summary>Gets the failure messages.</summary>
    public IReadOnlyList<string> Failures { get; }

    /// <summary>Gets the observed register values.</summary>
    public IReadOnlyDictionary<string, uint> Actual { get; }
}
