namespace XGrid.Frontend.Avalonia.Models;

/// <summary>A captured presented frame (BGRA8888, top-down).</summary>
public sealed class FramePacket
{
    /// <summary>Creates a frame packet.</summary>
    public FramePacket(uint width, uint height, byte[] bgra)
    {
        Width = width;
        Height = height;
        Bgra = bgra;
    }

    /// <summary>Gets the frame width in pixels.</summary>
    public uint Width { get; }

    /// <summary>Gets the frame height in pixels.</summary>
    public uint Height { get; }

    /// <summary>Gets the BGRA8888 pixel bytes.</summary>
    public byte[] Bgra { get; }
}
