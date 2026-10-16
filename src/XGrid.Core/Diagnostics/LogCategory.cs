namespace XGrid.Core.Diagnostics;

/// <summary>Structured logging categories (plan section 33).</summary>
public enum LogCategory
{
    /// <summary>Log events from CPU execution.</summary>
    Cpu = 0,
    /// <summary>Log events from guest memory access.</summary>
    Memory = 1,
    /// <summary>Log events from memory-mapped device I/O.</summary>
    Mmio = 2,
    /// <summary>Log events from PCI bus emulation.</summary>
    Pci = 3,
    /// <summary>Log events from the MCPX chipset.</summary>
    Mcpx = 4,
    /// <summary>Log events from the NV2A GPU.</summary>
    Nv2A = 5,
    /// <summary>Log events from the audio processor.</summary>
    Apu = 6,
    /// <summary>Log events from USB emulation.</summary>
    Usb = 7,
    /// <summary>Log events from IDE disk emulation.</summary>
    Ide = 8,
    /// <summary>Log events from the FATX filesystem.</summary>
    Fatx = 9,
    /// <summary>Log events from the XDVDFS filesystem.</summary>
    Xdvdfs = 10,
    /// <summary>Log events from XBE image handling.</summary>
    Xbe = 11,
    /// <summary>Log events from kernel emulation.</summary>
    Kernel = 12,
    /// <summary>Log events from input handling.</summary>
    Input = 13,
    /// <summary>Log events from host audio output.</summary>
    Audio = 14,
    /// <summary>Log events from host video output.</summary>
    Video = 15,
    /// <summary>Log events from just-in-time compilation.</summary>
    Jit = 16,
    /// <summary>Log events from core emulator infrastructure.</summary>
    Core = 17,
}
