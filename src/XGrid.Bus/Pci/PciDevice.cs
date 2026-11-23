using System;
using System.IO;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;

namespace XGrid.Bus.Pci;

/// <summary>A PCI device with a standard 256-byte configuration header and up to six BARs.</summary>
public class PciDevice : IXboxDevice, ISnapshotable
{
    private readonly byte[] _config = new byte[256];
    private readonly PciBar?[] _bars = new PciBar?[6];

    /// <summary>Creates a device at the given device/function slot on bus 0.</summary>
    public PciDevice(
        string name,
        byte deviceNumber,
        byte functionNumber,
        ushort vendorId,
        ushort deviceId,
        byte revisionId,
        byte classCode,
        byte subclass,
        byte interfaceCode)
    {
        Name = name;
        DeviceNumber = deviceNumber;
        FunctionNumber = functionNumber;
        VendorId = vendorId;
        DeviceId = deviceId;
        RevisionId = revisionId;
        ClassCode = classCode;
        Subclass = subclass;
        InterfaceCode = interfaceCode;

        WriteConfigWord(0x00, vendorId);
        WriteConfigWord(0x02, deviceId);
        _config[0x08] = revisionId;
        _config[0x09] = interfaceCode;
        _config[0x0A] = subclass;
        _config[0x0B] = classCode;
        _config[0x0E] = 0x00;
    }

    /// <summary>Gets the device name used in traces and diagnostics.</summary>
    public string Name { get; }

    /// <summary>Gets the device number on bus 0.</summary>
    public byte DeviceNumber { get; }

    /// <summary>Gets the function number.</summary>
    private byte FunctionNumber { get; }

    /// <summary>Gets the PCI vendor ID.</summary>
    private ushort VendorId { get; }

    /// <summary>Gets the PCI device ID.</summary>
    private ushort DeviceId { get; }

    /// <summary>Gets the revision ID.</summary>
    private byte RevisionId { get; }

    /// <summary>Gets the base class code.</summary>
    private byte ClassCode { get; }

    /// <summary>Gets the subclass code.</summary>
    private byte Subclass { get; }

    /// <summary>Gets the programming interface code.</summary>
    private byte InterfaceCode { get; }

    /// <summary>Gets the packed device/function number.</summary>
    public int DevFn => (DeviceNumber << 3) | FunctionNumber;

    /// <summary>Gets or sets the interrupt pin (1 = INTA#).</summary>
    public byte InterruptPin { get; } = 1;

    private byte _headerType;

    /// <summary>Gets or sets the header-type byte at 0x0E (bit 7 = multi-function device).</summary>
    protected byte HeaderType
    {
        // ReSharper disable once UnusedMember.Global
        get => _headerType;
        set
        {
            _headerType = value;
            _config[0x0E] = value;
        }
    }

    /// <summary>Gets or sets the interrupt line (programmed by firmware).</summary>
    public byte InterruptLine { get; private set; }

    /// <summary>Gets the bus this device is attached to.</summary>
    public PciBus? Bus { get; internal set; }

    /// <summary>Gets whether memory space decoding is enabled (command bit 1).</summary>
    public bool MemorySpaceEnabled => (ReadConfig(0x04, 2) & 0x0002u) != 0;

    /// <summary>Gets whether I/O space decoding is enabled (command bit 0).</summary>
    public bool IoSpaceEnabled => (ReadConfig(0x04, 2) & 0x0001u) != 0;

    /// <summary>Returns the device to its power-on state: rebuilds the configuration header from
    /// the constructor identities (vendor/device IDs, revision, class codes, header type) and
    /// zeroes every BAR base and probe flag.</summary>
    public virtual void Reset()
    {
        Array.Clear(_config);
        WriteConfigWord(0x00, VendorId);
        WriteConfigWord(0x02, DeviceId);
        _config[0x08] = RevisionId;
        _config[0x09] = InterfaceCode;
        _config[0x0A] = Subclass;
        _config[0x0B] = ClassCode;
        _config[0x0E] = _headerType;

        foreach (PciBar? bar in _bars)
        {
            if (bar is not null)
            {
                bar.Base = 0;
                bar.Probing = false;
            }
        }
    }

    /// <summary>Saves config space, BAR bases/probe flags, and the interrupt line (quiesce: BARs stable).</summary>
    public virtual void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(_config);
        writer.Write(InterruptLine);
        for (int i = 0; i < _bars.Length; i++)
        {
            PciBar? bar = _bars[i];
            writer.Write(bar is not null);
            if (bar is not null)
            {
                writer.Write(bar.Base);
                writer.Write(bar.Probing);
            }
        }
    }

    /// <summary>Restores config space, BAR bases/probe flags, and the interrupt line.</summary>
    public virtual void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported PCI device snapshot version {version}.");
        }

        byte[] config = reader.ReadBytes(256);
        if (config.Length != 256)
        {
            throw new InvalidDataException("Truncated PCI config snapshot.");
        }

        Array.Copy(config, _config, 256);
        InterruptLine = reader.ReadByte();
        for (int i = 0; i < _bars.Length; i++)
        {
            bool present = reader.ReadBoolean();
            if (!present)
            {
                continue;
            }

            uint restBase = reader.ReadUInt32();
            bool restProbing = reader.ReadBoolean();
            if (_bars[i] is not null)
            {
                _bars[i]!.Base = restBase;
                _bars[i]!.Probing = restProbing;
                OnBarWritten(i, restBase);
            }
        }

        _headerType = _config[0x0E];
    }

    private const int SnapshotVersion = 1;

    /// <summary>Reads configuration space (1, 2, or 4 bytes, little-endian).</summary>
    public uint ReadConfig(int offset, int size)
    {
        for (int i = 0; i < _bars.Length; i++)
        {
            PciBar? bar = _bars[i];
            int barOffset = 0x10 + (i * 4);
            if (bar is not null && offset >= barOffset && offset + size <= barOffset + 4)
            {
                uint value = ReadBar(i);
                return (value >> ((offset - barOffset) * 8)) & Mask(size);
            }
        }

        if (offset == 0x3C && size == 2)
        {
            return (uint)(InterruptLine | (InterruptPin << 8));
        }

        uint result = 0;
        for (int i = 0; i < size; i++)
        {
            result |= (uint)_config[offset + i] << (8 * i);
        }

        return result;
    }

    /// <summary>Writes configuration space.</summary>
    public virtual void WriteConfig(int offset, int size, uint value)
    {
        if (size == 4 && offset >= 0x10 && offset <= 0x24 && (offset & 3) == 0)
        {
            int index = (offset - 0x10) / 4;
            if (_bars[index] is not null)
            {
                WriteBar(index, value);
                return;
            }
        }

        if (offset == 0x3C && size == 1)
        {
            InterruptLine = (byte)value;
            _config[0x3C] = (byte)value;
            return;
        }

        for (int i = 0; i < size; i++)
        {
            _config[offset + i] = (byte)(value >> (8 * i));
        }
    }

    /// <summary>Returns the current base address of a BAR (0 when unassigned).</summary>
    public uint GetBarBase(int index)
    {
        return _bars[index]?.Base ?? 0;
    }

    /// <summary>Raises the device interrupt line through the bus.</summary>
    public void RaiseInterrupt()
    {
        Bus?.RaiseInterrupt(this);
    }

    /// <summary>Lowers the device interrupt line through the bus.</summary>
    public void LowerInterrupt()
    {
        Bus?.LowerInterrupt(this);
    }

    /// <summary>Defines a BAR with the given size and type.</summary>
    protected void DefineBar(int index, uint size, bool isIo, bool prefetchable = false)
    {
        if (size == 0 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("BAR size must be a power of two.", nameof(size));
        }

        _bars[index] = new PciBar(size, isIo, prefetchable);
    }

    /// <summary>Called after firmware assigns a new BAR base.</summary>
    protected virtual void OnBarWritten(int index, uint baseAddress)
    {
    }

    private void WriteConfigWord(int offset, ushort value)
    {
        _config[offset] = (byte)value;
        _config[offset + 1] = (byte)(value >> 8);
    }

    private uint ReadBar(int index)
    {
        PciBar bar = _bars[index]!;
        uint typeBits = bar.IsIo ? 0x1u : bar.Prefetchable ? 0x8u : 0x0u;
        uint mask = ~(bar.Size - 1);
        return bar.Probing ? mask | typeBits : bar.Base | typeBits;
    }

    private void WriteBar(int index, uint value)
    {
        PciBar bar = _bars[index]!;
        uint mask = ~(bar.Size - 1);
        if ((value & mask) == mask)
        {
            bar.Base = 0;
            bar.Probing = true;
            return;
        }

        bar.Probing = false;
        bar.Base = value & mask;
        OnBarWritten(index, bar.Base);
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

    private sealed class PciBar(uint size, bool isIo, bool prefetchable)
    {
        public uint Size { get; } = size;

        public bool IsIo { get; } = isIo;

        public bool Prefetchable { get; } = prefetchable;

        public uint Base { get; set; }

        public bool Probing { get; set; }
    }
}
