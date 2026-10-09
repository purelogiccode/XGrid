namespace XGrid.Core.Configuration;

/// <summary>Firmware and storage paths supplied by the user. Never bundled with XGrid.</summary>
public sealed class SystemConfiguration
{
    /// <summary>Gets the Xbox BIOS image path.</summary>
    public string? Bios { get; init; }

    /// <summary>Gets the 512-byte MCPX boot ROM path.</summary>
    public string? Mcpx { get; init; }

    /// <summary>Gets the hard disk image path.</summary>
    public string? HardDisk { get; init; }
}
