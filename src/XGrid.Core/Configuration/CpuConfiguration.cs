namespace XGrid.Core.Configuration;

/// <summary>CPU configuration.</summary>
public sealed class CpuConfiguration
{
    /// <summary>Gets the selected CPU backend.</summary>
    public CpuBackend Backend { get; init; } = CpuBackend.Interpreter;

    /// <summary>Gets whether the JIT is enabled when the backend supports it.</summary>
    public bool JitEnabled { get; init; }
}
