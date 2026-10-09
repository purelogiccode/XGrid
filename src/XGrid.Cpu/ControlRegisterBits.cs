namespace XGrid.Cpu;

/// <summary>CR0/CR4 bit definitions used by the MMU.</summary>
public static class ControlRegisterBits
{
    /// <summary>CR0.PE — protected mode enable.</summary>
    public const uint Cr0ProtectedMode = 1u << 0;

    /// <summary>CR0.WP — write protect (supervisor writes to read-only pages fault).</summary>
    public const uint Cr0WriteProtect = 1u << 16;

    /// <summary>CR0.PG — paging enable.</summary>
    public const uint Cr0Paging = 1u << 31;

    /// <summary>CR4.PSE — page size extensions (4 MB pages).</summary>
    public const uint Cr4PageSizeExtensions = 1u << 4;
}
