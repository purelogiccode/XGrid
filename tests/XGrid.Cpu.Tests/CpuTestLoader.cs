using System;
using System.Text.Json;

namespace XGrid.Cpu.Tests;

/// <summary>Loads and saves CPU tests as JSON (AOT-safe).</summary>
public static class CpuTestLoader
{
    /// <summary>Parses a CPU test from JSON.</summary>
    public static CpuTest Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, CpuTestJsonContext.Default.CpuTest)
               ?? throw new FormatException("CPU test JSON did not contain an object.");
    }

    /// <summary>Serializes a CPU test to JSON.</summary>
    public static string Save(CpuTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return JsonSerializer.Serialize(test, CpuTestJsonContext.Default.CpuTest);
    }
}
