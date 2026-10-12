using System;
using System.IO;
using XGrid.Bus.Io;

namespace XGrid.Bus.Pci;

/// <summary>MCPX LPC/ISA bridge (1:0.0) with interrupt routing, ROM overlay control, and ACPI PM registers.</summary>
public sealed class LpcController : PciDevice, IIoPortDevice
{
    /// <summary>ACPI/SCI routing register (two bytes).</summary>
    public const int AcpiRoutingRegister = 0x64;

    /// <summary>PIRQ routing register (one byte per PIRQ, A-D).</summary>
    public const int PirqRoutingRegister = 0x68;

    /// <summary>Internal IRQ routing register (eight 4-bit nibbles).</summary>
    public const int IntRoutingRegister = 0x6C;

    /// <summary>MCPX boot ROM overlay control register.</summary>
    public const int RomOverlayRegister = 0x80;

    /// <summary>Hardware default of the internal IRQ routing register.</summary>
    public const uint DefaultIntRouting = 0x0E065491;

    /// <summary>Hardware default of the PIRQ routing register.</summary>
    public const uint DefaultPirqRouting = 0x00031000;

    private readonly IoSpace _io;
    private IDisposable? _ioWindow;

    /// <summary>Creates the LPC bridge and registers it on the bus.</summary>
    public LpcController(IoSpace io, PciBus bus)
        : base("MCPX LPC", 1, 0, 0x10DE, 0x01B2, 0xB2, 0x06, 0x01, 0x00)
    {
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(bus);
        _io = io;
        DefineBar(0, 256, isIo: true);
        HeaderType = 0x80;
        bus.Register(this);
        WriteConfig(IntRoutingRegister, 4, DefaultIntRouting);
        WriteConfig(PirqRoutingRegister, 4, DefaultPirqRouting);
    }

    /// <summary>Raised when firmware disables the MCPX boot ROM overlay.</summary>
    public event Action? RomOverlayDisabled;

    /// <summary>Gets whether the MCPX boot ROM overlay is still enabled.</summary>
    public bool RomOverlayEnabled { get; private set; } = true;

    /// <summary>Gets or sets the ACPI PM1 status register (write-1-to-clear).</summary>
    public ushort Pm1Status { get; set; }

    /// <summary>Gets or sets the ACPI PM1 enable register.</summary>
    public ushort Pm1Enable { get; set; }

    /// <summary>Gets the free-running ACPI PM timer counter (24 bits).</summary>
    public uint PmTimerCounter { get; private set; }

    /// <summary>Advances the ACPI PM timer by the given number of 3.579545 MHz ticks.</summary>
    public void AdvancePmTimer(uint ticks)
    {
        PmTimerCounter = (PmTimerCounter + ticks) & 0xFFFFFFu;
    }

    /// <summary>Returns the PIC IRQ for a routing index (0-7 internal, 8-11 PIRQ A-D), or 0.</summary>
    public int ResolveIrq(int routingIndex)
    {
        if (routingIndex is < 0 or > 11)
        {
            return 0;
        }

        if (routingIndex < 8)
        {
            uint routing = ReadConfig(IntRoutingRegister, 4);
            return (int)((routing >> (routingIndex * 4)) & 0xF);
        }

        return (int)ReadConfig(PirqRoutingRegister + (routingIndex - 8), 1);
    }

    /// <summary>Returns the PIC IRQ from an ACPI routing byte (0-3), or 0 when unrouted.</summary>
    public int ResolveAcpiIrq(int index)
    {
        if (index is < 0 or > 3)
        {
            return 0;
        }

        return (int)ReadConfig(AcpiRoutingRegister + index, 1);
    }

    /// <summary>Returns the routing index for a PCI slot, or -1 when the slot has no IRQ.</summary>
    public static int RoutingIndexForSlot(int slot)
    {
        return slot switch
        {
            0 => 5,
            1 => 7,
            2 => 0,
            3 => 1,
            4 => 2,
            5 => 3,
            6 => 4,
            9 => 6,
            30 => 10,
            _ => -1,
        };
    }

    /// <summary>Resolves the PIC IRQ for a PCI slot, or 0 when unmapped.</summary>
    public int ResolveSlotIrq(int slot)
    {
        int index = RoutingIndexForSlot(slot);
        return index < 0 ? 0 : ResolveIrq(index);
    }

    /// <inheritdoc />
    public override void Reset()
    {
        base.Reset();
        WriteConfig(IntRoutingRegister, 4, DefaultIntRouting);
        WriteConfig(PirqRoutingRegister, 4, DefaultPirqRouting);
        RomOverlayEnabled = true;
        Pm1Status = 0;
        Pm1Enable = 0;
        PmTimerCounter = 0;
    }

    /// <summary>Saves PCI config/BARs plus overlay, PM1, and PM-timer state (quiesce: any point).</summary>
    public override void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        base.SaveState(writer);
        writer.Write(LpcSnapshotVersion);
        writer.Write(RomOverlayEnabled);
        writer.Write(Pm1Status);
        writer.Write(Pm1Enable);
        writer.Write(PmTimerCounter);
    }

    /// <summary>Restores PCI config/BARs plus overlay, PM1, and PM-timer state.</summary>
    public override void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        base.LoadState(reader);
        int version = reader.ReadInt32();
        if (version != LpcSnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported LPC snapshot version {version}.");
        }

        RomOverlayEnabled = reader.ReadBoolean();
        Pm1Status = reader.ReadUInt16();
        Pm1Enable = reader.ReadUInt16();
        PmTimerCounter = reader.ReadUInt32();
    }

    private const int LpcSnapshotVersion = 1;

    /// <inheritdoc />
    public override void WriteConfig(int offset, int size, uint value)
    {
        base.WriteConfig(offset, size, value);

        if (offset == RomOverlayRegister && (value & 2) != 0 && RomOverlayEnabled)
        {
            RomOverlayEnabled = false;
            RomOverlayDisabled?.Invoke();
        }
    }

    /// <inheritdoc />
    protected override void OnBarWritten(int index, uint baseAddress)
    {
        if (index != 0)
        {
            return;
        }

        _ioWindow?.Dispose();
        _ioWindow = baseAddress == 0 ? null : _io.Register((ushort)baseAddress, 256, this);
    }

    /// <inheritdoc />
    public uint Read(ushort offset, int size)
    {
        uint value = offset switch
        {
            0x00 => Pm1Status,
            0x02 => Pm1Enable,
            0x08 => PmTimerCounter,
            _ => 0,
        };

        return value & Mask(size);
    }

    /// <inheritdoc />
    public void Write(ushort offset, int size, uint value)
    {
        switch (offset)
        {
            case 0x00:
                Pm1Status = (ushort)(Pm1Status & ~value);
                break;
            case 0x02:
                Pm1Enable = (ushort)value;
                break;
            default:
                break;
        }
    }

    private static uint Mask(int size)
    {
        return size switch
        {
            8 => 0xFFu,
            16 => 0xFFFFu,
            _ => 0xFFFFFFFFu,
        };
    }
}
