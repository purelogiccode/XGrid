using System;
using System.Buffers.Binary;

namespace XGrid.Memory;

/// <summary>Explicit little-endian helpers. Never depends on host byte order.</summary>
public static class Endian
{
    /// <summary>Reads a 16-bit little-endian value.</summary>
    public static ushort Read16(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(source);
    }

    /// <summary>Reads a 32-bit little-endian value.</summary>
    public static uint Read32(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(source);
    }

    /// <summary>Reads a 64-bit little-endian value.</summary>
    public static ulong Read64(ReadOnlySpan<byte> source)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(source);
    }

    /// <summary>Writes a 16-bit little-endian value.</summary>
    public static void Write16(Span<byte> destination, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
    }

    /// <summary>Writes a 32-bit little-endian value.</summary>
    public static void Write32(Span<byte> destination, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
    }

    /// <summary>Writes a 64-bit little-endian value.</summary>
    public static void Write64(Span<byte> destination, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
    }
}
