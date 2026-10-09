using System;
using System.Collections.Generic;
using XGrid.Bus.Io;
using XGrid.Bus.Pci;

namespace XGrid.Bus.Smbus;

/// <summary>AMD756 SMBus host controller (PCI 1:1.0) with the Xbox register map.</summary>
public sealed class Amd756Smbus : PciDevice, IIoPortDevice
{
    /// <summary>Global status register.</summary>
    public const byte GlobalStatus = 0x0;

    /// <summary>Global enable register.</summary>
    public const byte GlobalEnable = 0x2;

    /// <summary>Host address register.</summary>
    public const byte HostAddress = 0x4;

    /// <summary>Host data register (low byte).</summary>
    public const byte HostData = 0x6;

    /// <summary>Host data register (high byte).</summary>
    public const byte HostDataHigh = 0x7;

    /// <summary>Host command register.</summary>
    public const byte HostCommand = 0x8;

    /// <summary>Host block data register.</summary>
    public const byte HostBlockData = 0x9;

    /// <summary>Transaction completed (cycle) status.</summary>
    public const byte HcycStatus = 0x10;

    /// <summary>Protocol error status.</summary>
    public const byte PrerrStatus = 0x04;

    /// <summary>Abort status.</summary>
    public const byte AbrtStatus = 0x01;

    /// <summary>Status bits cleared by writing them back.</summary>
    public const byte ClearStatusMask = 0x37;

    /// <summary>Global-enable bit that starts a transaction.</summary>
    public const byte TransactionGo = 0x08;

    /// <summary>Global-enable bit that arms the completion interrupt.</summary>
    public const byte InterruptEnable = 0x10;

    /// <summary>Global-enable bit that aborts the current transaction.</summary>
    public const byte AbortBit = 0x20;

    private readonly IoSpace _io;
    private readonly Dictionary<byte, ISmbusDevice> _devices = new();
    private readonly byte[] _blockData = new byte[32];
    private IDisposable? _window;
    private byte _address;
    private byte _data0;
    private byte _data1;
    private byte _command;
    private int _blockIndex;
    private readonly List<Transaction> _recent = new();

    /// <summary>Creates the controller and registers it on the PCI bus.</summary>
    public Amd756Smbus(IoSpace io, PciBus bus)
        : base("MCPX SMBus", 1, 1, 0x10DE, 0x01B4, 0xB1, 0x0C, 0x05, 0x00)
    {
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(bus);
        _io = io;
        DefineBar(1, 16, isIo: true);
        bus.Register(this);
    }

    /// <summary>Gets the global status register value.</summary>
    public byte Status { get; private set; }

    /// <summary>Gets the global enable register value.</summary>
    public byte Control { get; private set; }

    /// <summary>Gets the number of executed transactions. Diagnostics.</summary>
    public int TransactionCount { get; private set; }

    /// <summary>Gets the slave address of the most recent transaction. Diagnostics.</summary>
    public byte LastSlaveAddress { get; private set; }

    /// <summary>Gets the protocol of the most recent transaction. Diagnostics.</summary>
    public int LastProtocol { get; private set; }

    /// <summary>Gets the command byte of the most recent transaction. Diagnostics.</summary>
    public byte LastCommand { get; private set; }

    /// <summary>Gets whether the most recent transaction was a read. Diagnostics.</summary>
    public bool LastRead { get; private set; }

    /// <summary>Gets whether the most recent transaction succeeded. Diagnostics.</summary>
    public bool LastOk { get; private set; }

    /// <summary>Gets the recent transaction history (oldest first, at most 192 entries). Diagnostics.</summary>
    public IReadOnlyList<Transaction> RecentTransactions => _recent;

    /// <summary>Attaches a slave device.</summary>
    public void Attach(ISmbusDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _devices[device.Address] = device;
    }

    /// <summary>Removes a slave device by address.</summary>
    public void Detach(byte address)
    {
        _devices.Remove(address);
    }

    /// <inheritdoc />
    public override void Reset()
    {
        base.Reset();
        Status = 0;
        Control = 0;
        _address = 0;
        _data0 = 0;
        _data1 = 0;
        _command = 0;
        _blockIndex = 0;
        _recent.Clear();
        TransactionCount = 0;
        LastSlaveAddress = 0;
        LastProtocol = 0;
        LastCommand = 0;
        LastRead = false;
        LastOk = false;
        Array.Clear(_blockData);
    }

    /// <inheritdoc />
    protected override void OnBarWritten(int index, uint baseAddress)
    {
        if (index != 1)
        {
            return;
        }

        _window?.Dispose();
        _window = baseAddress == 0 ? null : _io.Register((ushort)baseAddress, 16, this);
    }

    /// <inheritdoc />
    public uint Read(ushort offset, int size)
    {
        byte register = (byte)(offset & 0x3F);
        uint value = 0;
        for (int i = 0; i < size / 8; i++)
        {
            value |= (uint)ReadByte((byte)((register + i) & 0x3F)) << (8 * i);
        }

        return value & Mask(size);
    }

    /// <inheritdoc />
    public void Write(ushort offset, int size, uint value)
    {
        byte register = (byte)(offset & 0x3F);
        for (int i = 0; i < size / 8; i++)
        {
            WriteByte((byte)((register + i) & 0x3F), (byte)(value >> (8 * i)));
        }
    }

    private byte ReadByte(byte register)
    {
        return register switch
        {
            GlobalStatus => Status,
            GlobalEnable => (byte)(Control & 0x1F),
            HostAddress => _address,
            HostData => _data0,
            HostDataHigh => _data1,
            HostCommand => _command,
            HostBlockData => _blockData[_blockIndex++ & 31],
            _ => 0,
        };
    }

    private void WriteByte(byte register, byte value)
    {
        switch (register)
        {
            case GlobalStatus:
                if ((Control & InterruptEnable) != 0
                    && ((value & ClearStatusMask) & ~(Status & ClearStatusMask)) != 0)
                {
                    RaiseInterrupt();
                }
                else
                {
                    LowerInterrupt();
                }

                if ((value & ClearStatusMask) != 0)
                {
                    Status = 0;
                }
                else
                {
                    Status = HcycStatus;
                }

                _blockIndex = 0;
                break;

            case GlobalEnable:
                Control = value;
                if ((value & AbortBit) != 0)
                {
                    Status |= AbrtStatus;
                }

                if ((value & TransactionGo) != 0)
                {
                    ExecuteTransaction();
                    if ((value & InterruptEnable) != 0 && (Status & ClearStatusMask) != 0)
                    {
                        RaiseInterrupt();
                    }
                }

                break;

            case HostAddress:
                _address = value;
                break;

            case HostData:
                _data0 = value;
                break;

            case HostDataHigh:
                _data1 = value;
                break;

            case HostCommand:
                _command = value;
                break;

            case HostBlockData:
                _blockData[_blockIndex++ & 31] = value;
                break;

            default:
                break;
        }
    }

    private void ExecuteTransaction()
    {
        int protocol = Control & 7;
        bool read = (_address & 1) != 0;
        byte address = (byte)((_address >> 1) & 0x7F);

        TransactionCount++;
        LastSlaveAddress = address;
        LastProtocol = protocol;
        LastCommand = _command;
        LastRead = read;

        if (!_devices.TryGetValue(address, out ISmbusDevice? device))
        {
            Status |= PrerrStatus;
            RecordTransaction(address, protocol, read, false, 0, 0);
            return;
        }

        bool ok;
        switch (protocol)
        {
            case 0:
                ok = device.Quick(read);
                break;

            case 1:
                if (read)
                {
                    ok = device.TryReceiveByte(out _data0);
                }
                else
                {
                    ok = device.SendByte(_command);
                }

                break;

            case 2:
                if (read)
                {
                    ok = device.TryReadByte(_command, out _data0);
                }
                else
                {
                    ok = device.WriteByte(_command, _data0);
                }

                break;

            case 3:
                if (read)
                {
                    ok = device.TryReadWord(_command, out ushort word);
                    _data0 = (byte)word;
                    _data1 = (byte)(word >> 8);
                }
                else
                {
                    ok = device.WriteWord(_command, (ushort)((_data1 << 8) | _data0));
                }

                break;

            case 5:
                if (read)
                {
                    int count = device.ReadBlock(_command, _blockData);
                    ok = count >= 0;
                    if (ok)
                    {
                        _data0 = (byte)count;
                    }
                }
                else
                {
                    int count = Math.Min(_data0, _blockData.Length);
                    ok = device.WriteBlock(_command, _blockData.AsSpan(0, count));
                }

                break;

            default:
                Status |= PrerrStatus;
                RecordTransaction(address, protocol, read, false, 0, 0);
                return;
        }

        RecordTransaction(address, protocol, read, ok, _data0, _data1);
        Status |= ok ? HcycStatus : PrerrStatus;
    }

    private void RecordTransaction(byte address, int protocol, bool read, bool ok, byte data0, byte data1)
    {
        LastOk = ok;
        _recent.Add(new Transaction(address, protocol, _command, read, ok, data0, data1));
        if (_recent.Count > 192)
        {
            _recent.RemoveAt(0);
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

    /// <summary>One executed SMBus transaction. Diagnostics.</summary>
    public readonly struct Transaction
    {
        /// <summary>Creates a transaction record.</summary>
        public Transaction(byte slaveAddress, int protocol, byte command, bool read, bool ok, byte data0, byte data1)
        {
            SlaveAddress = slaveAddress;
            Protocol = protocol;
            Command = command;
            Read = read;
            Ok = ok;
            Data0 = data0;
            Data1 = data1;
        }

        /// <summary>Gets the 7-bit slave address.</summary>
        public byte SlaveAddress { get; }

        /// <summary>Gets the protocol number (0-5).</summary>
        public int Protocol { get; }

        /// <summary>Gets the command byte.</summary>
        public byte Command { get; }

        /// <summary>Gets whether it was a read.</summary>
        public bool Read { get; }

        /// <summary>Gets whether it succeeded.</summary>
        public bool Ok { get; }

        /// <summary>Gets the data byte 0 delivered or received.</summary>
        public byte Data0 { get; }

        /// <summary>Gets the data byte 1 delivered or received.</summary>
        public byte Data1 { get; }
    }
}
