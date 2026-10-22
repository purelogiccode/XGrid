using System;

namespace XGrid.Memory;

/// <summary>
/// Marks a device whose window serves a host-side byte image that guest
/// stores can never change (they are ignored or never reach it): a flash/BIOS
/// window, its mirror, or the MCPX ROM overlay.
/// </summary>
/// <remarks>
/// The decode cache (§131) may cache an instruction fetch from such a window
/// without a store-invalidation path, because the only way those bytes move is
/// an explicit host action — the overlay being disabled, a snapshot load, or a
/// new image being mapped — and every one of those flushes the cache
/// explicitly. Keep this interface off any device that a guest store can
/// modify, and off any window whose reads have side effects (both would make
/// cached decode bytes stale).
/// </remarks>
public interface IImmutableImageDevice
{
}

/// <summary>Read-only memory window backed by a byte image (flash/ROM).</summary>
public sealed class RomDevice : IMmioDevice, IImmutableImageDevice
{
    private readonly byte[] _data;
    private readonly uint _baseAddress;

    /// <summary>Creates a read-only window at the given base address.</summary>
    public RomDevice(uint baseAddress, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _baseAddress = baseAddress;
        _data = data;
    }

    /// <summary>Gets the image size in bytes.</summary>
    // ReSharper disable once UnusedMember.Global
    public int Size => _data.Length;

    /// <summary>Reads <paramref name="size"/> bytes little-endian from the image at the offset of <paramref name="address"/> from the window base.</summary>
    public uint Read(uint address, int size)
    {
        int offset = (int)(address - _baseAddress);
        uint value = 0;
        for (int i = 0; i < size; i++)
        {
            value |= (uint)_data[offset + i] << (8 * i);
        }

        return value;
    }

    /// <summary>Ignored: the window is read-only (a flash command set is planned for a later phase).</summary>
    public void Write(uint address, int size, uint value)
    {
        // Read-only; writes are ignored (flash command set arrives in Phase 6).
    }
}

/// <summary>Read-only window that mirrors a power-of-two image across a larger address window.</summary>
public sealed class RomAliasDevice : IMmioDevice, IImmutableImageDevice
{
    private readonly byte[] _data;
    private readonly uint _baseAddress;
    private readonly uint _mask;

    /// <summary>Creates an alias window over the image.</summary>
    public RomAliasDevice(uint baseAddress, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0 || (data.Length & (data.Length - 1)) != 0)
        {
            throw new ArgumentException("ROM image size must be a power of two for mirroring.", nameof(data));
        }

        _baseAddress = baseAddress;
        _data = data;
        _mask = (uint)(data.Length - 1);
    }

    /// <summary>Gets the image size in bytes.</summary>
    // ReSharper disable once UnusedMember.Global
    public int Size => _data.Length;

    /// <summary>Reads <paramref name="size"/> bytes little-endian from the image with the address offset masked to the power-of-two image size, so the image mirrors across the window.</summary>
    public uint Read(uint address, int size)
    {
        int offset = (int)((address - _baseAddress) & _mask);
        uint value = 0;
        for (int i = 0; i < size; i++)
        {
            value |= (uint)_data[offset + i] << (8 * i);
        }

        return value;
    }

    /// <summary>Ignored: the window is read-only.</summary>
    public void Write(uint address, int size, uint value)
    {
        // Read-only.
    }
}

/// <summary>Overlay window that reveals an underlying device when disabled (MCPX boot ROM).</summary>
public sealed class RomOverlayDevice : IMmioDevice, IImmutableImageDevice
{
    private readonly byte[] _overlay;
    private readonly uint _baseAddress;
    private readonly IMmioDevice _underlying;

    /// <summary>Creates an overlay over the given underlying window.</summary>
    public RomOverlayDevice(uint baseAddress, byte[] overlay, IMmioDevice underlying)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(underlying);
        _baseAddress = baseAddress;
        _overlay = overlay;
        _underlying = underlying;
    }

    /// <summary>Gets or sets whether the overlay is active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Reads <paramref name="size"/> bytes little-endian from the overlay while <see cref="Enabled"/> is true; when disabled, forwards the read to the underlying device.</summary>
    public uint Read(uint address, int size)
    {
        if (!Enabled)
        {
            return _underlying.Read(address, size);
        }

        int offset = (int)(address - _baseAddress);
        uint value = 0;
        for (int i = 0; i < size; i++)
        {
            value |= (uint)_overlay[offset + i] << (8 * i);
        }

        return value;
    }

    /// <summary>Forwards the write to the underlying device only while <see cref="Enabled"/> is false; writes with the overlay active are ignored.</summary>
    public void Write(uint address, int size, uint value)
    {
        if (!Enabled)
        {
            _underlying.Write(address, size, value);
        }
    }
}
