namespace XGrid.Memory;

/// <summary>Xbox physical address facts (see docs/research-notes/xgrid-architecture-decisions.md).</summary>
public static class XboxAddressMap
{
    /// <summary>NV2A tiling RAM mirror base.</summary>
    public const uint RamMirrorBase = 0x0C000000;

    /// <summary>RAM mirror window size (128 MB window over 64 MB RAM).</summary>
    public const uint RamMirrorSize = 0x08000000;

    /// <summary>
    /// Tiled / write-combined RAM view base (Cxbx AddressRanges TILED_MEMORY_BASE /
    /// XBOX_WRITE_COMBINED_BASE; XboxDevWiki "Shadow RAM"). Mirrors RAM; overlaps the
    /// flash window below, which keeps precedence there by registration order.
    /// </summary>
    public const uint WriteCombinedBase = 0xF0000000;

    /// <summary>Tiled / write-combined RAM view size, retail 64 MB (matches RAM).</summary>
    public const uint WriteCombinedSize = 0x04000000;

    /// <summary>Flash ROM base.</summary>
    public const uint FlashBase = 0xF0000000;

    /// <summary>Flash ROM size (1 MB).</summary>
    public const uint FlashSize = 0x00100000;

    /// <summary>Flash ROM mirror base (16 MB window).</summary>
    public const uint FlashMirrorBase = 0xFF000000;

    /// <summary>Flash ROM mirror window size.</summary>
    public const uint FlashMirrorSize = 0x01000000;

    /// <summary>NV2A register window base.</summary>
    public const uint Nv2ABase = 0xFD000000;

    /// <summary>NV2A register window size (16 MB).</summary>
    public const uint Nv2ASize = 0x01000000;

    /// <summary>MCPX APU window base.</summary>
    public const uint ApuBase = 0xFE800000;

    /// <summary>MCPX APU window size (512 KB, XboxDevWiki Memory).</summary>
    public const uint ApuSize = 0x00080000;

    /// <summary>MCPX ACI (AC'97 audio) register window base (PCI 0:6.0 BAR2, XboxDevWiki Memory/PCI).</summary>
    public const uint AciBase = 0xFEC00000;

    /// <summary>MCPX ACI register window size (4 KB).</summary>
    public const uint AciSize = 0x00001000;

    /// <summary>USB0 OHCI window base.</summary>
    public const uint Usb0Base = 0xFED00000;

    /// <summary>USB1 OHCI window base.</summary>
    public const uint Usb1Base = 0xFED08000;

    /// <summary>OHCI window size.</summary>
    public const uint UsbSize = 0x00001000;

    /// <summary>MCPX nvnet (nForce Ethernet) window base (PCI 0:4.0 BAR0, XboxDevWiki Memory).</summary>
    public const uint NvNetBase = 0xFEF00000;

    /// <summary>MCPX nvnet window size (PCI BAR0 size).</summary>
    public const uint NvNetSize = 0x00000400;
}
