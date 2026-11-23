namespace XGrid.Core.Configuration;

/// <summary>CPU backend selection.</summary>
public enum CpuBackend
{
    /// <summary>Reference interpreter.</summary>
    Interpreter = 0,

    /// <summary>JIT backend (added in a later phase).</summary>
    Jit = 1,
}
