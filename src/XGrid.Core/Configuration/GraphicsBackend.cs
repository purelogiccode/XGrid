namespace XGrid.Core.Configuration;

/// <summary>Graphics backend selection.</summary>
public enum GraphicsBackend
{
    /// <summary>Vulkan backend (primary target).</summary>
    Vulkan = 0,

    /// <summary>OpenGL backend (optional).</summary>
    OpenGl = 1,

    /// <summary>No rendering; used by headless tools and tests.</summary>
    Null = 2,
}
