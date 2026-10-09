namespace XGrid.Memory;

/// <summary>Xbox physical address facts (see docs/research-notes/xgrid-architecture-decisions.md).</summary>
public static class XboxAddressMap
{
    /// <summary>Main RAM base.</summary>
    public const uint RamBase = 0x00000000;

    /// <summary>NV2A tiling RAM mirror base.</summary>
    public const uint RamMirrorBase = 0x0C000000;

    /// <summary>RAM mirror window size (128 MB window over 64 MB RAM).</summary>
    public const uint RamMirrorSize = 0x08000000;

    /// <summary>Flash ROM base.</summary>
    public const uint FlashBase = 0xF0000000;

    /// <summary>Flash ROM size (1 MB).</summary>
    public const uint FlashSize = 0x00100000;

    /// <summary>Flash ROM mirror base (16 MB window).</summary>
    public const uint FlashMirrorBase = 0xFF000000;

    /// <summary>Flash ROM mirror window size.</summary>
    public const uint FlashMirrorSize = 0x01000000;

    /// <summary>MCPX boot ROM base (overlaid at the top of the BIOS image).</summary>
    public const uint McpxRomBase = 0xFFFFFE00;

    /// <summary>MCPX boot ROM size (512 bytes).</summary>
    public const uint McpxRomSize = 0x00000200;

    /// <summary>NV2A register window base.</summary>
    public const uint Nv2ABase = 0xFD000000;

    /// <summary>NV2A register window size (16 MB).</summary>
    public const uint Nv2ASize = 0x01000000;

    /// <summary>MCPX APU window base.</summary>
    public const uint ApuBase = 0xFE800000;

    /// <summary>MCPX APU window size (4 MB).</summary>
    public const uint ApuSize = 0x00400000;

    /// <summary>I/O APIC window base (devkit-era; optional).</summary>
    public const uint IoApicBase = 0xFEC00000;

    /// <summary>I/O APIC window size.</summary>
    public const uint IoApicSize = 0x00001000;

    /// <summary>USB0 OHCI window base.</summary>
    public const uint Usb0Base = 0xFED00000;

    /// <summary>USB1 OHCI window base.</summary>
    public const uint Usb1Base = 0xFED08000;

    /// <summary>OHCI window size.</summary>
    public const uint UsbSize = 0x00001000;
}
