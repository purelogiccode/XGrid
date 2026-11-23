namespace XGrid.Frontend.Avalonia.Models;

/// <summary>A captured presented frame (BGRA8888, top-down).</summary>
/// <remarks>Creates a frame packet.</remarks>
public sealed class FramePacket(uint width, uint height, byte[] bgra)
{

    /// <summary>Gets the frame width in pixels.</summary>
    public uint Width { get; } = width;

    /// <summary>Gets the frame height in pixels.</summary>
    public uint Height { get; } = height;

    /// <summary>Gets the BGRA8888 pixel bytes.</summary>
    public byte[] Bgra { get; } = bgra;
}
