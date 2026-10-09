namespace XGrid.Memory;

/// <summary>Determines how accesses outside every mapped region behave.</summary>
public enum UnmappedAccessPolicy
{
    /// <summary>Unmapped reads return all-ones; writes are ignored. Matches reference emulator behavior.</summary>
    Sentinel = 0,

    /// <summary>Unmapped accesses throw <see cref="MemoryAccessException"/>. Intended for tests.</summary>
    Throw = 1,
}
