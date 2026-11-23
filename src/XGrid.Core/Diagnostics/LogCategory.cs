namespace XGrid.Core.Diagnostics;

/// <summary>Structured logging categories (plan section 33).</summary>
public enum LogCategory
{
    /// <summary>Log events from CPU execution.</summary>
    Cpu = 0,

    /// <summary>Log events from guest memory access.</summary>
    // ReSharper disable once UnusedMember.Global
    Memory = 1,

    /// <summary>Log events from memory-mapped device I/O.</summary>
    Mmio = 2,

    /// <summary>Log events from PCI bus emulation.</summary>
    // ReSharper disable once UnusedMember.Global
    Pci = 3,

    /// <summary>Log events from the MCPX chipset.</summary>
    // ReSharper disable once UnusedMember.Global
    Mcpx = 4,

    /// <summary>Log events from the NV2A GPU.</summary>
    Nv2A = 5,

    /// <summary>Log events from the audio processor.</summary>
    // ReSharper disable once UnusedMember.Global
    Apu = 6,

    /// <summary>Log events from USB emulation.</summary>
    // ReSharper disable once UnusedMember.Global
    Usb = 7,

    /// <summary>Log events from IDE disk emulation.</summary>
    // ReSharper disable once UnusedMember.Global
    Ide = 8,

    /// <summary>Log events from the FATX filesystem.</summary>
    // ReSharper disable once UnusedMember.Global
    Fatx = 9,

    /// <summary>Log events from the XDVDFS filesystem.</summary>
    // ReSharper disable once UnusedMember.Global
    Xdvdfs = 10,

    /// <summary>Log events from XBE image handling.</summary>
    // ReSharper disable once UnusedMember.Global
    Xbe = 11,

    /// <summary>Log events from kernel emulation.</summary>
    // ReSharper disable once UnusedMember.Global
    Kernel = 12,

    /// <summary>Log events from input handling.</summary>
    // ReSharper disable once UnusedMember.Global
    Input = 13,

    /// <summary>Log events from host audio output.</summary>
    // ReSharper disable once UnusedMember.Global
    Audio = 14,

    /// <summary>Log events from host video output.</summary>
    // ReSharper disable once UnusedMember.Global
    Video = 15,

    /// <summary>Log events from just-in-time compilation.</summary>
    // ReSharper disable once UnusedMember.Global
    Jit = 16,

    /// <summary>Log events from core emulator infrastructure.</summary>
    Core = 17,
}
