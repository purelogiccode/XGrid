using System;
using XGrid.Bus.Interrupts;
using XGrid.Bus.Io;
using XGrid.Bus.Pci;
using XGrid.Bus.Smbus;
using XGrid.Bus.Timers;
using XGrid.Core.Devices;

namespace XGrid.Bus;

/// <summary>Wires the Xbox bus: I/O space, PCI, LPC, PIC, and timers.</summary>
public sealed class XboxBus : IXboxDevice
{
    /// <summary>Pentium III core clock used to convert CPU cycles to device ticks.</summary>
    public const ulong CpuClockHz = 733333333;

    /// <summary>ACPI PM timer frequency (3.579545 MHz).</summary>
    public const uint PmTimerFrequency = 3579545;

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
        Pci.Register(new NForceUsbController("nForce USB (OHCI) 0", 2));
        Pci.Register(new NForceUsbController("nForce USB (OHCI) 1", 3));
        Pci.Register(new NvNetController());
        Pci.Register(new McpxApu());
        Pci.Register(new McpxAci());
        Pci.CreateSecondaryBus(agp).Register(Nv2aPci = new Nv2aGpu());
        Lpc = new LpcController(Io, Pci);
        Smbus = new Amd756Smbus(Io, Pci);
        Pic = new Pic8259(Io);
        Pit = new Pit8254(Io);
        Rtc = new Rtc146818(Io);

        Smc = new SmcDevice();
        Eeprom = new Eeprom24c02();
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
    public Nv2aGpu Nv2aPci { get; }

    /// <summary>Gets the MCPX LPC bridge.</summary>
    public LpcController Lpc { get; }

    /// <summary>Gets the AMD756 SMBus controller.</summary>
    public Amd756Smbus Smbus { get; }

    /// <summary>Gets the system management controller.</summary>
    public SmcDevice Smc { get; }

    /// <summary>Gets the SMBus EEPROM.</summary>
    public Eeprom24c02 Eeprom { get; private set; }

    /// <summary>Gets the video encoder stub.</summary>
    public ConexantCx25871 Encoder { get; }

    /// <summary>Replaces the EEPROM image (e.g. with a user-supplied eeprom.bin).</summary>
    public void AttachEeprom(ReadOnlySpan<byte> image)
    {
        Smbus.Detach(Eeprom24c02.DeviceAddress);
        Eeprom = new Eeprom24c02(image);
        Smbus.Attach(Eeprom);
    }

    /// <summary>Gets the interrupt controller.</summary>
    public Pic8259 Pic { get; }

    /// <summary>Gets the interval timer.</summary>
    public Pit8254 Pit { get; }

    /// <summary>Gets the real-time clock.</summary>
    public Rtc146818 Rtc { get; }

    /// <summary>Gets the device name used in traces and diagnostics.</summary>
    public string Name => "Xbox bus";

    /// <summary>Gets whether the MCPX boot ROM overlay is enabled.</summary>
    public bool RomOverlayEnabled => Lpc.RomOverlayEnabled;

    /// <inheritdoc />
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
        if (device is Amd756Smbus)
        {
            return Lpc.ResolveAcpiIrq(1);
        }

        if (device is Nv2aGpu)
        {
            return Lpc.ResolveSlotIrq(30);
        }

        return Lpc.ResolveSlotIrq(device.DeviceNumber);
    }

    private void OnPitOutput(int channel)
    {
        if (channel == 0)
        {
            Pic.SetIrq(0, true);
        }
    }
}
