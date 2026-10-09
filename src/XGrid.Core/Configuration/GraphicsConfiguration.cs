namespace XGrid.Core.Configuration;

/// <summary>Graphics configuration.</summary>
public sealed class GraphicsConfiguration
{
    /// <summary>Gets the selected backend.</summary>
    public GraphicsBackend Backend { get; init; } = GraphicsBackend.Vulkan;

    /// <summary>Gets the internal resolution scale (1 = native).</summary>
    public int ResolutionScale { get; init; } = 1;

    /// <summary>Gets whether vertical sync is enabled.</summary>
    public bool Vsync { get; init; } = true;
}
