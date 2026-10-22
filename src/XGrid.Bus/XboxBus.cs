using System;
using System.IO;
using XGrid.Bus.Interrupts;
using XGrid.Bus.Io;
using XGrid.Bus.Pci;
using XGrid.Bus.Smbus;
using XGrid.Bus.Timers;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;

namespace XGrid.Bus;

/// <summary>Wires the Xbox bus: I/O space, PCI, LPC, PIC, and timers.</summary>
public sealed class XboxBus : IXboxDevice, ISnapshotable
{
    /// <summary>Pentium III core clock used to convert CPU cycles to device ticks.</summary>
    public const ulong CpuClockHz = 733333333;

    /// <summary>ACPI PM timer frequency (3.579545 MHz).</summary>
    private const uint PmTimerFrequency = 3579545;

    private ulong _pitRemainder;
    private ulong _pmRemainder;
    private ulong _rtcRemainder;

    /// <summary>Creates the bus and registers the core Xbox devices.</summary>
    public XboxBus()
    {
        Pci = new PciBus(Io);
        Pci.Register(new PciHostBridge());
        var agp = new PciAgpBridge();
        Pci.Register(agp);
        Pci.Register(Usb0Pci = new NForceUsbController("nForce USB (OHCI) 0", 2));
        Pci.Register(Usb1Pci = new NForceUsbController("nForce USB (OHCI) 1", 3));
        Pci.Register(NvNetPci = new NvNetController());
        Pci.Register(ApuPci = new McpxApu());
        Pci.Register(new McpxAci());
        Pci.CreateSecondaryBus(agp).Register(Nv2APci = new Nv2AGpu());
        Lpc = new LpcController(Io, Pci);
        Smbus = new Amd756Smbus(Io, Pci);
        Pic = new Pic8259(Io);
        Pit = new Pit8254(Io);
        Rtc = new Rtc146818(Io);

        Smc = new SmcDevice();
        Eeprom = new Eeprom24C02();
        Encoder = new ConexantCx25871();
        Smbus.Attach(Smc);
        Smbus.Attach(Eeprom);
        Smbus.Attach(Encoder);

        Pci.InterruptRaised += OnPciInterrupt;
        Pci.InterruptLowered += OnPciInterruptLowered;
        Pit.Output += OnPitOutput;
        Rtc.Interrupt += () => Pic.SetIrq(8, true);
        Rtc.InterruptAcknowledged += () => Pic.SetIrq(8, false);
    }

    /// <summary>Gets the I/O port space.</summary>
    public IoSpace Io { get; } = new();

    /// <summary>Gets the PCI bus.</summary>
    public PciBus Pci { get; }

    /// <summary>Gets the NV2A GPU PCI function on the AGP secondary bus.</summary>
    public Nv2AGpu Nv2APci { get; }

    /// <summary>Gets the MCPX APU PCI function (0:5.0).</summary>
    public McpxApu ApuPci { get; }

    /// <summary>Gets the nvnet Ethernet PCI function (0:4.0).</summary>
    public NvNetController NvNetPci { get; }

    /// <summary>Gets the OHCI USB0 PCI function (0:2.0).</summary>
    public NForceUsbController Usb0Pci { get; }

    /// <summary>Gets the OHCI USB1 PCI function (0:3.0).</summary>
    public NForceUsbController Usb1Pci { get; }

    /// <summary>Gets the MCPX LPC bridge.</summary>
    public LpcController Lpc { get; }

    /// <summary>Gets the AMD756 SMBus controller.</summary>
    public Amd756Smbus Smbus { get; }

    /// <summary>Gets the system management controller.</summary>
    public SmcDevice Smc { get; }

    /// <summary>Gets the SMBus EEPROM.</summary>
    public Eeprom24C02 Eeprom { get; private set; }

    /// <summary>Gets the video encoder stub.</summary>
    private ConexantCx25871 Encoder { get; }

    /// <summary>Replaces the EEPROM image (e.g. with a user-supplied eeprom.bin).</summary>
    public void AttachEeprom(ReadOnlySpan<byte> image)
    {
        Smbus.Detach(Eeprom24C02.DeviceAddress);
        Eeprom = new Eeprom24C02(image);
        Smbus.Attach(Eeprom);
    }

    /// <summary>Gets the interrupt controller.</summary>
    public Pic8259 Pic { get; }

    /// <summary>Gets the interval timer.</summary>
    public Pit8254 Pit { get; }

    /// <summary>Gets the real-time clock.</summary>
    public Rtc146818 Rtc { get; }

    /// <summary>Gets the device name used in traces and diagnostics.</summary>
#pragma warning disable CA1822
    public string Name => "Xbox bus";
#pragma warning restore CA1822

    /// <summary>Gets whether the MCPX boot ROM overlay is enabled.</summary>
    public bool RomOverlayEnabled => Lpc.RomOverlayEnabled;

    /// <summary>Returns the whole bus to its power-on state: resets the PCI bus (and with it every
    /// PCI device and secondary bus), the PIC, the PIT, and the RTC, and clears the accumulated
    /// cycle-to-tick conversion remainders.</summary>
    public void Reset()
    {
        Pci.Reset();
        Pic.Reset();
        Pit.Reset();
        Rtc.Reset();
        _pitRemainder = 0;
        _pmRemainder = 0;
        _rtcRemainder = 0;
    }

    /// <summary>Saves timer remainders plus PIC/PIT/RTC/PCI/SMBus-slave state (quiesce: any point; SMBus idle).</summary>
    /// <remarks>Pci cascades to every PCI device (LPC, SMBus controller, stubs). The
    /// SMBus slaves (SMC/EEPROM/encoder) are saved explicitly. IoSpace port routing is
    /// construction-time and is rebuilt via BAR restore; it is not serialized.</remarks>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(_pitRemainder);
        writer.Write(_pmRemainder);
        writer.Write(_rtcRemainder);
        Pic.SaveState(writer);
        Pit.SaveState(writer);
        Rtc.SaveState(writer);
        Pci.SaveState(writer);
        Smc.SaveState(writer);
        Eeprom.SaveState(writer);
        Encoder.SaveState(writer);
    }

    /// <summary>Restores timer remainders plus PIC/PIT/RTC/PCI/SMBus-slave state.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported bus snapshot version {version}.");
        }

        _pitRemainder = reader.ReadUInt64();
        _pmRemainder = reader.ReadUInt64();
        _rtcRemainder = reader.ReadUInt64();
        Pic.LoadState(reader);
        Pit.LoadState(reader);
        Rtc.LoadState(reader);
        Pci.LoadState(reader);
        Smc.LoadState(reader);
        Eeprom.LoadState(reader);
        Encoder.LoadState(reader);
    }

    private const int SnapshotVersion = 1;

    /// <summary>
    /// Advances bus timers by the given number of CPU cycles. Fractional ticks accumulate
    /// across calls so the total is independent of batch sizes.
    /// </summary>
    public void Advance(ulong cycles)
    {
        _pitRemainder += cycles * Pit8254.InputFrequency;
        ulong pitTicks = _pitRemainder / CpuClockHz;
        _pitRemainder %= CpuClockHz;
        if (pitTicks != 0)
        {
            Pit.Advance(pitTicks);
        }

        _pmRemainder += cycles * PmTimerFrequency;
        ulong pmTicks = _pmRemainder / CpuClockHz;
        _pmRemainder %= CpuClockHz;
        if (pmTicks != 0)
        {
            Lpc.AdvancePmTimer((uint)pmTicks);
        }

        _rtcRemainder += cycles * Rtc146818.InputFrequency;
        ulong rtcTicks = _rtcRemainder / CpuClockHz;
        _rtcRemainder %= CpuClockHz;
        if (rtcTicks != 0)
        {
            Rtc.Advance(rtcTicks);
        }
    }

    private void OnPciInterrupt(PciDevice device)
    {
        int irq = ResolveDeviceIrq(device);
        if (irq is > 0 and < 16)
        {
            Pic.SetIrq(irq, true);
        }
    }

    private void OnPciInterruptLowered(PciDevice device)
    {
        int irq = ResolveDeviceIrq(device);
        if (irq is > 0 and < 16)
        {
            Pic.SetIrq(irq, false);
        }
    }

    private int ResolveDeviceIrq(PciDevice device)
    {
        return device is Amd756Smbus
            ? Lpc.ResolveAcpiIrq(1)
            : device is Nv2AGpu ? Lpc.ResolveSlotIrq(30) : Lpc.ResolveSlotIrq(device.DeviceNumber);
    }

    private void OnPitOutput(int channel)
    {
        if (channel == 0)
        {
            Pic.SetIrq(0, true);
        }
    }
}
