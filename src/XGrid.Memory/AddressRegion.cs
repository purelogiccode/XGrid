using System;

namespace XGrid.Memory;

/// <summary>A mapped window backed by an <see cref="IMmioDevice"/>.</summary>
public sealed class AddressRegion
{
    /// <summary>Creates a region.</summary>
    public AddressRegion(string name, uint baseAddress, uint size, IMmioDevice device, int priority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(device);

        if (size == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Region size must be greater than zero.");
        }

        Name = name;
        BaseAddress = baseAddress;
        Size = size;
        Device = device;
        Priority = priority;
    }

    /// <summary>Gets the diagnostic name.</summary>
    public string Name { get; }

    /// <summary>Gets the first address of the window.</summary>
    private uint BaseAddress { get; }

    /// <summary>Gets the window size in bytes.</summary>
    private uint Size { get; }

    /// <summary>Gets the backing device.</summary>
    public IMmioDevice Device { get; }

    /// <summary>Gets the priority; higher values win when regions overlap.</summary>
    public int Priority { get; }

    /// <summary>Returns whether the address falls inside this window.</summary>
    public bool Contains(uint address)
    {
        return (ulong)address >= BaseAddress && (ulong)address - BaseAddress < Size;
    }
}
