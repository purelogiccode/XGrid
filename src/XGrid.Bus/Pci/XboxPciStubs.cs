namespace XGrid.Bus.Pci;

/// <summary>
/// nForce OHCI USB controller (MCPX USB at 0:2.0 and 0:3.0, 10DE:01C2, class 0C03 prog-if 10).
/// Configuration space and BAR0 sizing only; the OHCI register block lives in
/// <c>XGrid.Bus.Usb.OhciUsbController</c> at 0xFED00000/0xFED08000.
/// </summary>
public sealed class NForceUsbController : PciDevice
{
    /// <summary>Creates an OHCI controller at the given device number (2 or 3).</summary>
    public NForceUsbController(string name, byte deviceNumber)
        : base(name, deviceNumber, 0, 0x10DE, 0x01C2, 0xB1, 0x0C, 0x03, 0x10)
    {
        DefineBar(0, 0x100, isIo: false);
    }
}

/// <summary>
/// nForce Ethernet controller (MCPX nvnet at 0:4.0, 10DE:01C3). Configuration space only;
/// the MMIO register block lives in <c>XGrid.Bus.Net.McpxNvNetDevice</c> at 0xFEF00000.
/// </summary>
public sealed class NvNetController : PciDevice
{
    /// <summary>Creates the nvnet controller.</summary>
    public NvNetController()
        : base("nForce Ethernet (nvnet)", 4, 0, 0x10DE, 0x01C3, 0xB1, 0x02, 0x00, 0x00)
    {
        DefineBar(0, 0x400, isIo: false);
        DefineBar(1, 0x8, isIo: true);
    }
}

/// <summary>MCPX audio processing unit (0:5.0, 10DE:01B0). Configuration space only.</summary>
public sealed class McpxApu : PciDevice
{
    /// <summary>Creates the APU.</summary>
    public McpxApu()
        : base("MCPX APU", 5, 0, 0x10DE, 0x01B0, 0xB1, 0x04, 0x01, 0x00)
    {
        DefineBar(0, 0x80000, isIo: false);
    }
}

/// <summary>MCPX audio codec interface (0:6.0, 10DE:01B1). Exposes BAR2 only, as in xemu.</summary>
public sealed class McpxAci : PciDevice
{
    /// <summary>Creates the ACI.</summary>
    public McpxAci()
        : base("MCPX ACI", 6, 0, 0x10DE, 0x01B1, 0xB1, 0x04, 0x01, 0x00)
    {
        DefineBar(2, 0x1000, isIo: false);
    }
}

/// <summary>NV2A GPU on the AGP secondary bus (bus N, device 0, 10DE:02A0). Configuration space only.</summary>
public sealed class Nv2AGpu : PciDevice
{
    /// <summary>Creates the NV2A.</summary>
    public Nv2AGpu()
        : base("NV2A", 0, 0, 0x10DE, 0x02A0, 0xA1, 0x03, 0x00, 0x00)
    {
        DefineBar(0, 0x1000000, isIo: false);
        DefineBar(1, 0x4000000, isIo: false, prefetchable: true);
    }
}
