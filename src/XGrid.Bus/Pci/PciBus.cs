using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Bus.Io;
using XGrid.Core;
using XGrid.Core.Devices;

namespace XGrid.Bus.Pci;

/// <summary>
/// PCI configuration mechanism #1 (0xCF8/0xCFC). Bus 0 is the root and owns the ports. Secondary buses
/// created behind a bridge answer only when a configuration address names the bridge's secondary bus
/// number (register 0x19), so a probe of an unpopulated bus never aliases onto bus-0 devices.
/// </summary>
public sealed class PciBus : IXboxDevice, IIoPortDevice, ISnapshotable
{
    /// <summary>Configuration address port.</summary>
    public const ushort ConfigAddressPort = 0xCF8;

    /// <summary>Configuration data port.</summary>
    public const ushort ConfigDataPort = 0xCFC;

    /// <summary>PCI-to-PCI bridge register holding the secondary bus number.</summary>
    private const int SecondaryBusRegister = 0x19;

    private readonly PciDevice?[] _devices = new PciDevice[256];
    private readonly List<(PciDevice Bridge, PciBus Secondary)> _secondaries = new();
    private readonly Dictionary<ushort, int> _configAccesses = new();

    /// <summary>Creates the root PCI bus and registers its configuration ports in the I/O space.</summary>
    public PciBus(IoSpace io)
    {
        ArgumentNullException.ThrowIfNull(io);
        io.Register(ConfigAddressPort, 8, this);
    }

    private PciBus()
    {
    }

    /// <summary>Gets the device name used in traces and diagnostics.</summary>
    public string Name => "PCI bus";

    /// <summary>Raised when a device asserts its interrupt line.</summary>
    public event Action<PciDevice>? InterruptRaised;

    /// <summary>Raised when a device deasserts its interrupt line.</summary>
    public event Action<PciDevice>? InterruptLowered;

    /// <summary>Gets the configuration address register value.</summary>
    public uint ConfigAddress { get; private set; }

    /// <summary>Gets configuration access counts keyed by (bus &lt;&lt; 8) | devfn. Diagnostics.</summary>
    public IReadOnlyDictionary<ushort, int> ConfigAccessCounts => _configAccesses;

    /// <summary>Attaches a device to the bus.</summary>
    public void Register(PciDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.Bus = this;
        _devices[device.DevFn] = device;
    }

    /// <summary>
    /// Creates a secondary bus behind a bridge. Its devices are reached when a configuration address
    /// names the bridge's programmed secondary bus number.
    /// </summary>
    public PciBus CreateSecondaryBus(PciDevice bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        var secondary = new PciBus();
        secondary.InterruptRaised += RaiseInterrupt;
        secondary.InterruptLowered += LowerInterrupt;
        _secondaries.Add((bridge, secondary));
        return secondary;
    }

    /// <summary>Finds a device by device/function number, or null.</summary>
    public PciDevice? Find(byte deviceNumber, byte functionNumber)
    {
        return _devices[(deviceNumber << 3) | functionNumber];
    }

    private void CountConfig(uint address)
    {
        ushort key = (ushort)((((address >> 16) & 0xFFu) << 8) | ((address >> 8) & 0xFFu));
        _configAccesses.TryGetValue(key, out int count);
        _configAccesses[key] = count + 1;
    }

    /// <summary>Reads a configuration dword from a raw configuration address.</summary>
    public uint ReadConfig(uint address, int size)
    {
        if ((address & 0x80000000u) == 0)
        {
            return 0;
        }

        CountConfig(address);
        PciBus? target = Route(address);
        return target?.ReadLocal(address, size) ?? Mask(size);
    }

    /// <summary>Writes a configuration dword to a raw configuration address.</summary>
    public void WriteConfig(uint address, int size, uint value)
    {
        if ((address & 0x80000000u) == 0)
        {
            return;
        }

        CountConfig(address);
        PciBus? target = Route(address);
        target?.WriteLocal(address, size, value);
    }

    /// <inheritdoc />
    public uint Read(ushort offset, int size)
    {
        int bytes = size / 8;
        if (offset < 4)
        {
            return ReadAddressRegister(offset, bytes);
        }

        return ReadDataRegister(offset - 4, bytes);
    }

    /// <inheritdoc />
    public void Write(ushort offset, int size, uint value)
    {
        int bytes = size / 8;
        if (offset < 4)
        {
            WriteAddressRegister(offset, bytes, value);
            return;
        }

        WriteDataRegister(offset - 4, bytes, value);
    }

    /// <inheritdoc />
    public void Reset()
    {
        ConfigAddress = 0;
        foreach (PciDevice? device in _devices)
        {
            device?.Reset();
        }

        foreach (var (_, secondary) in _secondaries)
        {
            secondary.Reset();
        }
    }

    /// <summary>Saves the config address, access histograms, and every attached device (quiesce: BARs stable).</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(ConfigAddress);
        writer.Write(_configAccesses.Count);
        foreach (var entry in _configAccesses)
        {
            writer.Write(entry.Key);
            writer.Write(entry.Value);
        }

        for (int i = 0; i < _devices.Length; i++)
        {
            writer.Write(_devices[i] is not null);
            _devices[i]?.SaveState(writer);
        }

        writer.Write(_secondaries.Count);
        foreach (var (_, secondary) in _secondaries)
        {
            secondary.SaveState(writer);
        }
    }

    /// <summary>Restores the config address, histograms, and every attached device.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported PCI bus snapshot version {version}.");
        }

        ConfigAddress = reader.ReadUInt32();
        int accessCount = reader.ReadInt32();
        if (accessCount < 0)
        {
            throw new InvalidDataException("Negative PCI access histogram snapshot.");
        }

        _configAccesses.Clear();
        for (int i = 0; i < accessCount; i++)
        {
            ushort key = reader.ReadUInt16();
            int value = reader.ReadInt32();
            _configAccesses[key] = value;
        }

        for (int i = 0; i < _devices.Length; i++)
        {
            bool present = reader.ReadBoolean();
            PciDevice? device = _devices[i];
            if (present)
            {
                if (device is null)
                {
                    throw new InvalidDataException($"PCI device snapshot has no live slot {i}.");
                }

                device.LoadState(reader);
            }
            else if (device is not null)
            {
                device.Reset();
            }
        }

        int secondaries = reader.ReadInt32();
        if (secondaries != _secondaries.Count)
        {
            throw new InvalidDataException("PCI secondary bus count mismatch on snapshot load.");
        }

        foreach (var (_, secondary) in _secondaries)
        {
            secondary.LoadState(reader);
        }
    }

    private const int SnapshotVersion = 1;

    /// <summary>Notifies listeners that a device asserted its interrupt line.</summary>
    public void RaiseInterrupt(PciDevice device)
    {
        InterruptRaised?.Invoke(device);
    }

    /// <summary>Notifies listeners that a device deasserted its interrupt line.</summary>
    public void LowerInterrupt(PciDevice device)
    {
        InterruptLowered?.Invoke(device);
    }

    /// <summary>Builds a configuration address for the given slot and register.</summary>
    public static uint MakeConfigAddress(byte deviceNumber, byte functionNumber, byte register)
    {
        return 0x80000000u
               | (register & 0xFCu)
               | ((uint)(deviceNumber & 0x1F) << 11)
               | ((uint)(functionNumber & 7) << 8);
    }

    private PciBus? Route(uint address)
    {
        uint bus = (address >> 16) & 0xFFu;
        if (bus == 0)
        {
            return this;
        }

        foreach (var (bridge, secondary) in _secondaries)
        {
            if (bridge.ReadConfig(SecondaryBusRegister, 1) == bus)
            {
                return secondary;
            }
        }

        return null;
    }

    private uint ReadLocal(uint address, int size)
    {
        PciDevice? device = DeviceFromAddress(address);
        if (device is null)
        {
            return Mask(size);
        }

        int offset = (int)(address & 0xFC) + (int)(address & 3);
        return device.ReadConfig(offset, size);
    }

    private void WriteLocal(uint address, int size, uint value)
    {
        PciDevice? device = DeviceFromAddress(address);
        if (device is null)
        {
            return;
        }

        int offset = (int)(address & 0xFC) + (int)(address & 3);
        device.WriteConfig(offset, size, value);
    }

    private PciDevice? DeviceFromAddress(uint address)
    {
        int devfn = (int)((address >> 8) & 0xFF);
        return _devices[devfn];
    }

    private uint ReadAddressRegister(ushort offset, int size)
    {
        uint value = ConfigAddress;
        return (value >> (offset * 8)) & Mask(size);
    }

    private void WriteAddressRegister(ushort offset, int size, uint value)
    {
        for (int i = 0; i < size; i++)
        {
            int shift = (offset + i) * 8;
            ConfigAddress = (ConfigAddress & ~(0xFFu << shift)) | (((value >> (8 * i)) & 0xFFu) << shift);
        }

        ConfigAddress &= 0xFFFFFFFCu;
    }

    private uint ReadDataRegister(int byteOffset, int size)
    {
        uint address = ConfigAddress | (uint)(byteOffset & 3);
        return ReadConfig(address, size);
    }

    private void WriteDataRegister(int byteOffset, int size, uint value)
    {
        uint address = ConfigAddress | (uint)(byteOffset & 3);
        WriteConfig(address, size, value);
    }

    private static uint Mask(int size)
    {
        return size switch
        {
            1 => 0xFFu,
            2 => 0xFFFFu,
            _ => 0xFFFFFFFFu,
        };
    }
}
