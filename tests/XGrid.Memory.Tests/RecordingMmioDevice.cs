using System.Collections.Generic;

namespace XGrid.Memory.Tests;

internal sealed class RecordingMmioDevice : IMmioDevice
{
    private readonly List<MmioAccess> _accesses = [];

    public IReadOnlyList<MmioAccess> Accesses => _accesses;

    public uint ReadValue { get; set; } = 0xDEADBEEF;

    public uint Read(uint address, int size)
    {
        _accesses.Add(new MmioAccess(true, address, size, 0));
        return ReadValue;
    }

    public void Write(uint address, int size, uint value)
    {
        _accesses.Add(new MmioAccess(false, address, size, value));
    }
}

internal readonly record struct MmioAccess(bool IsRead, uint Address, int Size, uint Value);
