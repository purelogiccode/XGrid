namespace XGrid.Core.Diagnostics;

/// <summary>Structured logging categories (plan section 33).</summary>
public enum LogCategory
{
    Cpu = 0,
    Memory = 1,
    Mmio = 2,
    Pci = 3,
    Mcpx = 4,
    Nv2A = 5,
    Apu = 6,
    Usb = 7,
    Ide = 8,
    Fatx = 9,
    Xdvdfs = 10,
    Xbe = 11,
    Kernel = 12,
    Input = 13,
    Audio = 14,
    Video = 15,
    Jit = 16,
    Core = 17,
}
