using System;
using System.IO;
using System.Runtime.InteropServices;
using XGrid.Bus.Io;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;

namespace XGrid.Bus.Interrupts;

/// <summary>Dual 8259 programmable interrupt controller (master 0x20/0x21, slave 0xA0/0xA1).</summary>
/// <remarks>
/// The PCI lines are held levels: the device keeps the line high until its own
/// status register is acknowledged. The guest programs the edge/level control
/// register (0x4D0/0x4D1) for exactly those lines (measured: master 0x68 =
/// IRQ3/IRQ5/IRQ6, slave 0x18 = IRQ11/IRQ12), and the controller then follows
/// xemu 0.8.139 <c>hw/intc/i8259.c</c>: a level line's request is not cleared by
/// the acknowledge, so the EOI re-presents it. Clearing it on every acknowledge
/// loses the interrupt - the device's de-duplicated assert never produces a
/// second edge - which is what parked 007 at 30B steps and the Redump rows in
/// the kernel idle spin.
/// </remarks>
public sealed class Pic8259 : IXboxDevice, IInterruptSource, ISnapshotable
{
    private static readonly bool TraceWrites = IsTraceEnabled();

    private readonly Pic _master = new(elcrMask: 0xF8);
    private readonly Pic _slave = new(elcrMask: 0xDE);
    private readonly uint[] _pendingFlag = new uint[1];

    /// <summary>Creates the PIC pair and registers its command/data ports.</summary>
    public Pic8259(IoSpace io)
    {
        ArgumentNullException.ThrowIfNull(io);
        GCHandle pendingPin = GCHandle.Alloc(
            _pendingFlag,
            GCHandleType.Pinned);
        PendingFlagAddress = pendingPin.AddrOfPinnedObject();
        io.Register(0x20, 2, new PortWindow(_master, 0x20, UpdatePendingFlag));
        io.Register(0xA0, 2, new PortWindow(_slave, 0xA0, UpdatePendingFlag));
        io.Register(0x4D0, 1, new ElcrWindow(_master));
        io.Register(0x4D1, 1, new ElcrWindow(_slave));
        Reset();
    }

    /// <summary>Gets the address of the 0/1 pending word emitted code polls (see <see cref="IInterruptSource.PendingFlagAddress"/>).</summary>
    public nint PendingFlagAddress { get; }

    private static bool IsTraceEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("XGRID_PIC_TRACE");
        return !string.IsNullOrEmpty(value) && !string.Equals(value, "0", StringComparison.Ordinal);
    }

    private void UpdatePendingFlag()
    {
        // The slave's INT output is the master's IR2 input, a level that follows
        // the slave's highest pending request. Re-drive it on every state change
        // (acknowledge, EOI, mask write) so a held slave line is not stranded.
        _master.SetIrq(2, _slave.HighestPending() >= 0);
        _pendingFlag[0] = _master.HighestPending() >= 0 ? 1u : 0u;
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

    /// <summary>Gets the master edge/level control register (0x4D0).</summary>
    public byte MasterElcr => _master.Elcr;

    /// <summary>Gets the slave edge/level control register (0x4D1).</summary>
    public byte SlaveElcr => _slave.Elcr;

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
        }

        UpdatePendingFlag();
    }

    /// <summary>Acknowledges the highest-priority pending interrupt, or returns -1.</summary>
    public int Acknowledge()
    {
        int vector = AcknowledgeCore();
        UpdatePendingFlag();
        return vector;
    }

    private int AcknowledgeCore()
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
    /// in-service latches cleared, edge/level control cleared, init sequencing off, with master
    /// vector base 0x20 and slave vector base 0x28.</summary>
    public void Reset()
    {
        _master.Reset(0x20);
        _slave.Reset(0x28);
        UpdatePendingFlag();
    }

    /// <summary>Saves masks, request/in-service latches, vector bases, edge/level control, and init state (quiesce: any point).</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        _master.SaveState(writer);
        _slave.SaveState(writer);
    }

    /// <summary>Restores masks, latches, vector bases, edge/level control, and init state.</summary>
    /// <remarks>A version 1 snapshot predates the edge/level model, so it restores
    /// every line as edge-triggered. Regenerate such a state if it parked on a
    /// held PCI line (007's 30B stall, the Redump dashboard spin).</remarks>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version is < 1 or > SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported PIC snapshot version {version}.");
        }

        _master.LoadState(reader, version);
        _slave.LoadState(reader, version);
        UpdatePendingFlag();
    }

    private const int SnapshotVersion = 2;

    private sealed class PortWindow(Pic pic, int basePort, Action changed) : IIoPortDevice
    {

        public uint Read(ushort offset, int size)
        {
            return offset == 0 ? pic.ReadCommand() : pic.ReadData();
        }

        public void Write(ushort offset, int size, uint value)
        {
            if (offset == 0)
            {
                pic.WriteCommand((byte)value);
            }
            else
            {
                pic.WriteData((byte)value);
            }

            if (TraceWrites)
            {
                Console.Error.WriteLine($"PIC {basePort + offset:X2}<={value:X2} irr={pic.RequestRegister:X2} isr={pic.InServiceRegister:X2} imr={pic.Mask:X2} elcr={pic.Elcr:X2}");
            }

            changed();
        }
    }

    private sealed class ElcrWindow(Pic pic) : IIoPortDevice
    {
        public uint Read(ushort offset, int size)
        {
            return pic.Elcr;
        }

        public void Write(ushort offset, int size, uint value)
        {
            pic.WriteElcr((byte)value);
            if (TraceWrites)
            {
                Console.Error.WriteLine($"PIC ELCR<={(byte)value:X2} elcr={pic.Elcr:X2}");
            }
        }
    }

    private sealed class Pic(byte elcrMask)
    {
        private const byte InitFlag = 0x10;

        private readonly byte _elcrMask = elcrMask;

        private int _initStep;
        private bool _autoEoi;
        private bool _readInService;
        private bool _pollPending;
        private bool _levelMode;

        public byte Mask { get; private set; } = 0xFF;

        public byte VectorBase { get; private set; }

        public byte RequestRegister { get; private set; }

        public byte InServiceRegister { get; private set; }

        /// <summary>Gets the edge/level control register (a set bit selects level-triggered).</summary>
        public byte Elcr { get; private set; }

        /// <summary>Gets whether ICW1 bit 3 (LTIM) selected level-triggered lines.</summary>
        public bool LevelMode => _levelMode;

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
            writer.Write(_levelMode);
            writer.Write(Elcr);
        }

        public void LoadState(BinaryReader reader, int version)
        {
            Mask = reader.ReadByte();
            VectorBase = reader.ReadByte();
            RequestRegister = reader.ReadByte();
            InServiceRegister = reader.ReadByte();
            _initStep = reader.ReadInt32();
            _autoEoi = reader.ReadBoolean();
            _readInService = reader.ReadBoolean();
            _pollPending = reader.ReadBoolean();
            _levelMode = version >= 2 && reader.ReadBoolean();
            Elcr = version >= 2 ? (byte)(reader.ReadByte() & _elcrMask) : (byte)0;
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
            _levelMode = false;
            Elcr = 0;
        }

        public void WriteElcr(byte value)
        {
            Elcr = (byte)(value & _elcrMask);
        }

        /// <summary>Gets whether a line is level-triggered (LTIM or its ELCR bit).</summary>
        public bool IsLevel(int line)
        {
            return _levelMode || (Elcr & (1 << line)) != 0;
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
            // A level line stays latched until the device lowers it (xemu
            // pic_intack: "We don't clear a level sensitive interrupt here").
            if (!IsLevel(line))
            {
                RequestRegister &= (byte)~(1 << line);
            }

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
                // ICW1: an init sequence clears the latches but keeps the level
                // lines' requests (xemu pic_reset_common: irr &= elcr, isr = 0,
                // imr = 0; ELCR and LTIM survive).
                _initStep = 1;
                _autoEoi = false;
                _readInService = false;
                _pollPending = false;
                _levelMode = (value & 0x08) != 0;
                Mask = 0;
                RequestRegister &= Elcr;
                InServiceRegister = 0;
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
                    EoiSpecific((value & 7));
                }
                else if ((value & 0x80) != 0 && (value & 0x40) == 0)
                {
                    RotatePriority((value & 7));
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

                AcknowledgeLine(line);
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
