namespace XGrid.Bus.Pci;

/// <summary>Xbox PCI host bridge (0:0.0, NVIDIA XBOX_PCHB 10DE:02A5). Multi-function, as in xemu.</summary>
public sealed class PciHostBridge : PciDevice
{
    /// <summary>Creates the host bridge.</summary>
    public PciHostBridge()
        : base("Xbox PCI host bridge", 0, 0, 0x10DE, 0x02A5, 0xA1, 0x06, 0x00, 0x00)
    {
        HeaderType = 0x80;
    }
}

/// <summary>AGP bridge (0:30.0, nForce AGP 10DE:01B7). Its secondary bus carries the NV2A.</summary>
public sealed class PciAgpBridge : PciDevice
{
    /// <summary>Creates the AGP bridge.</summary>
    public PciAgpBridge()
        : base("AGP bridge", 30, 0, 0x10DE, 0x01B7, 0xA1, 0x06, 0x04, 0x00)
    {
        HeaderType = 0x01;
    }
}
