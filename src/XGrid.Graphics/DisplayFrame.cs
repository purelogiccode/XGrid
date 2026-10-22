using System;
using System.IO;

namespace XGrid.Graphics;

/// <summary>Latest presented frame buffer with BMP export.</summary>
public sealed class DisplayFrame
{
    private const uint MaxDimension = 4096u;
    private const int BmpHeaderSize = 14;
    private const int BmpInfoSize = 40;

    private byte[] _pixels = Array.Empty<byte>();

    /// <summary>Gets the frame width in pixels.</summary>
    public uint Width { get; private set; }

    /// <summary>Gets the frame height in pixels.</summary>
    public uint Height { get; private set; }

    /// <summary>Gets whether a frame has been presented.</summary>
    public bool HasFrame => _pixels.Length != 0;

    /// <summary>Stores a presented frame in 32-bit BGRA byte order.</summary>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="bgra">Pixel data, four bytes (blue, green, red, alpha) per pixel.</param>
    public void Show(uint width, uint height, byte[] bgra)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width == 0 || width > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height == 0 || height > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if ((ulong)bgra.Length != (ulong)width * height * 4)
        {
            throw new ArgumentException("Pixel data does not match dimensions.", nameof(bgra));
        }

        Width = width;
        Height = height;
        _pixels = (byte[])bgra.Clone();
    }

    /// <summary>Counts pixels with at least one nonzero channel.</summary>
    public uint CountNonZeroPixels()
    {
        uint count = 0;
        for (int i = 0; i + 4 <= _pixels.Length; i += 4)
        {
            if (_pixels[i] != 0 || _pixels[i + 1] != 0 || _pixels[i + 2] != 0 || _pixels[i + 3] != 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Saves the presented frame as a 32-bit BMP file.</summary>
    /// <param name="path">Destination file path.</param>
    public void SaveBmp(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!HasFrame)
        {
            throw new InvalidOperationException("No frame has been presented.");
        }

        uint rowBytes = Width * 4;
        uint imageBytes = rowBytes * Height;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write((BmpHeaderSize + BmpInfoSize + imageBytes));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((uint)(BmpHeaderSize + BmpInfoSize));
        writer.Write((uint)BmpInfoSize);
        writer.Write((int)Width);
        writer.Write((int)Height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0u);
        writer.Write(imageBytes);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0u);
        writer.Write(0u);
        for (uint y = 0; y < Height; y++)
        {
            int row = (int)((Height - 1 - y) * rowBytes);
            writer.Write(_pixels, row, (int)rowBytes);
        }
    }
}
