using System;
using System.IO;
using XGrid.Bus.Io;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;

namespace XGrid.Bus.Interrupts;

/// <summary>Dual 8259 programmable interrupt controller (master 0x20/0x21, slave 0xA0/0xA1).</summary>
public sealed class Pic8259 : IXboxDevice, IInterruptSource, ISnapshotable
{
    private readonly Pic _master = new();
    private readonly Pic _slave = new();

    /// <summary>Creates the PIC pair and registers its command/data ports.</summary>
    public Pic8259(IoSpace io)
    {
        ArgumentNullException.ThrowIfNull(io);
        io.Register(0x20, 2, new PortWindow(_master));
        io.Register(0xA0, 2, new PortWindow(_slave));
        Reset();
    }

    /// <summary>Gets the device name used in traces and diagnostics.</summary>
#pragma warning disable CA1822
    public string Name => "8259 PIC";
#pragma warning restore CA1822

    /// <summary>Gets the master interrupt mask register.</summary>
    public byte MasterMask => _master.Mask;

    /// <summary>Gets the slave interrupt mask register.</summary>
    public byte SlaveMask => _slave.Mask;

    /// <summary>Gets the master in-service register.</summary>
    public byte MasterInService => _master.InServiceRegister;

    /// <summary>Gets the slave in-service register.</summary>
    public byte SlaveInService => _slave.InServiceRegister;

    /// <summary>Gets the master interrupt request register.</summary>
    public byte MasterRequest => _master.RequestRegister;

    /// <summary>Gets the slave interrupt request register.</summary>
    public byte SlaveRequest => _slave.RequestRegister;

    /// <summary>Gets the master vector base programmed via ICW2.</summary>
    public byte MasterBase => _master.VectorBase;

    /// <summary>Sets an interrupt request line (0-15) to the given level.</summary>
    public void SetIrq(int line, bool level)
    {
        if (line < 8)
        {
            _master.SetIrq(line, level);
        }
        else
        {
            _slave.SetIrq(line - 8, level);
            _master.SetIrq(2, _slave.HighestPending() >= 0);
        }
    }

    /// <summary>Acknowledges the highest-priority pending interrupt, or returns -1.</summary>
    public int Acknowledge()
    {
        int masterLine = _master.HighestPending();
        if (masterLine < 0)
        {
            return -1;
        }

        if (masterLine == 2)
        {
            int slaveLine = _slave.HighestPending();
            if (slaveLine >= 0)
            {
                int vector = _slave.AcknowledgeLine(slaveLine);
                _master.AcknowledgeLine(2);
                return vector;
            }

            _master.AcknowledgeLine(2);
            return -1;
        }

        return _master.AcknowledgeLine(masterLine);
    }

    /// <summary>Returns both PICs to their power-on state: masks fully enabled (0xFF), request and
    /// in-service latches cleared, init sequencing off, with master vector base 0x20 and slave
    /// vector base 0x28.</summary>
    public void Reset()
    {
        _master.Reset(0x20);
        _slave.Reset(0x28);
    }

    /// <summary>Saves masks, request/in-service latches, vector bases, and init state (quiesce: any point).</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        _master.SaveState(writer);
        _slave.SaveState(writer);
    }

    /// <summary>Restores masks, latches, vector bases, and init state.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported PIC snapshot version {version}.");
        }

        _master.LoadState(reader);
        _slave.LoadState(reader);
    }

    private const int SnapshotVersion = 1;

    private sealed class PortWindow : IIoPortDevice
    {
        private readonly Pic _pic;

        public PortWindow(Pic pic)
        {
            _pic = pic;
        }

        public uint Read(ushort offset, int size)
        {
            return offset == 0 ? _pic.ReadCommand() : _pic.ReadData();
        }

        public void Write(ushort offset, int size, uint value)
        {
            if (offset == 0)
            {
                _pic.WriteCommand((byte)value);
            }
            else
            {
                _pic.WriteData((byte)value);
            }
        }
    }

    private sealed class Pic
    {
        private const byte InitFlag = 0x10;

        private int _initStep;
        private bool _autoEoi;
        private bool _readInService;
        private bool _pollPending;

        public byte Mask { get; private set; } = 0xFF;

        public byte VectorBase { get; private set; }

        public byte RequestRegister { get; private set; }

        public byte InServiceRegister { get; private set; }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(Mask);
            writer.Write(VectorBase);
            writer.Write(RequestRegister);
            writer.Write(InServiceRegister);
            writer.Write(_initStep);
            writer.Write(_autoEoi);
            writer.Write(_readInService);
            writer.Write(_pollPending);
        }

        public void LoadState(BinaryReader reader)
        {
            Mask = reader.ReadByte();
            VectorBase = reader.ReadByte();
            RequestRegister = reader.ReadByte();
            InServiceRegister = reader.ReadByte();
            _initStep = reader.ReadInt32();
            _autoEoi = reader.ReadBoolean();
            _readInService = reader.ReadBoolean();
            _pollPending = reader.ReadBoolean();
        }

        public void Reset(byte vectorBase)
        {
            VectorBase = vectorBase;
            Mask = 0xFF;
            RequestRegister = 0;
            InServiceRegister = 0;
            _initStep = 0;
            _autoEoi = false;
            _readInService = false;
            _pollPending = false;
        }

        public void SetIrq(int line, bool level)
        {
            if (level)
            {
                RequestRegister |= (byte)(1 << line);
            }
            else
            {
                RequestRegister &= (byte)~(1 << line);
            }
        }

        public int HighestPending()
        {
            byte pending = (byte)(RequestRegister & ~Mask);
            if (pending == 0)
            {
                return -1;
            }

            int inService = -1;
            for (int i = 0; i < 8; i++)
            {
                if ((InServiceRegister & (1 << i)) != 0)
                {
                    inService = i;
                    break;
                }
            }

            for (int i = 0; i < 8; i++)
            {
                if ((pending & (1 << i)) != 0 && (inService < 0 || i < inService))
                {
                    return i;
                }
            }

            return -1;
        }

        public int AcknowledgeLine(int line)
        {
            RequestRegister &= (byte)~(1 << line);
            if (!_autoEoi)
            {
                InServiceRegister |= (byte)(1 << line);
            }

            return VectorBase | line;
        }

        public void WriteCommand(byte value)
        {
            if ((value & InitFlag) != 0)
            {
                _initStep = 1;
                _autoEoi = false;
                _readInService = false;
                _pollPending = false;
                Mask = 0;
                return;
            }

            if ((value & 0x08) != 0)
            {
                _readInService = (value & 0x03) == 0x01;
                _pollPending = (value & 0x04) != 0;
                return;
            }

            if ((value & 0x20) != 0)
            {
                if ((value & 0x80) != 0 && (value & 0x40) != 0)
                {
                    EoiSpecific((int)(value & 7));
                }
                else if ((value & 0x80) != 0 && (value & 0x40) == 0)
                {
                    RotatePriority((int)(value & 7));
                }
                else
                {
                    Eoi();
                }
            }
        }

        public void WriteData(byte value)
        {
            switch (_initStep)
            {
                case 1:
                    VectorBase = (byte)(value & 0xF8);
                    _initStep = 2;
                    break;
                case 2:
                    _initStep = 3;
                    break;
                case 3:
                    _autoEoi = (value & 0x02) != 0;
                    _initStep = 0;
                    break;
                default:
                    Mask = value;
                    break;
            }
        }

        public byte ReadCommand()
        {
            if (_pollPending)
            {
                _pollPending = false;
                int line = HighestPending();
                if (line < 0)
                {
                    return 0x80;
                }

                RequestRegister &= (byte)~(1 << line);
                if (!_autoEoi)
                {
                    InServiceRegister |= (byte)(1 << line);
                }

                return (byte)(0x80 | line);
            }

            return _readInService ? InServiceRegister : RequestRegister;
        }

        public byte ReadData()
        {
            return Mask;
        }

        private void Eoi()
        {
            for (int i = 0; i < 8; i++)
            {
                if ((InServiceRegister & (1 << i)) != 0)
                {
                    InServiceRegister &= (byte)~(1 << i);
                    return;
                }
            }
        }

        private void EoiSpecific(int line)
        {
            InServiceRegister &= (byte)~(1 << line);
        }

        private static void RotatePriority(int line)
        {
            _ = line;
        }
    }
}
