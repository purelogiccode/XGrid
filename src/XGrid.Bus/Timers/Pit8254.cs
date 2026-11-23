using System;
using System.IO;
using XGrid.Bus.Io;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;

namespace XGrid.Bus.Timers;

/// <summary>8254 programmable interval timer (ports 0x40-0x43).</summary>
public sealed class Pit8254 : IXboxDevice, IIoPortDevice, ISnapshotable
{
    /// <summary>PIT input clock (Xbox 1.125 MHz; matches xemu i8254.h PIT_FREQ).</summary>
    public const uint InputFrequency = 1125001;

    private readonly Channel[] _channels = [new(), new(), new()];
    private byte _control;

    /// <summary>Creates the PIT and registers its ports.</summary>
    public Pit8254(IoSpace io)
    {
        ArgumentNullException.ThrowIfNull(io);
        io.Register(0x40, 4, this);
        Reset();
    }

    /// <summary>Gets the device name used in traces and diagnostics.</summary>
#pragma warning disable CA1822
    public string Name => "8254 PIT";
#pragma warning restore CA1822

    /// <summary>Raised when a channel output pulses (channel 0 drives IRQ0).</summary>
    public event Action<int>? Output;

    /// <summary>Reads from the PIT window at ports 0x40-0x43: offset 3 returns the last control
    /// word written, offsets 0-2 return the selected channel's counter byte (honoring a prior latch
    /// and the programmed 8/16-bit read access).</summary>
    /// <param name="offset">Byte offset from port 0x40 (0-2 counter, 3 control).</param>
    /// <param name="size">Access width in bytes (ignored; the PIT counter port returns one byte).</param>
    public uint Read(ushort offset, int size)
    {
        return offset == 3 ? _control : _channels[offset & 3].ReadCounter();
    }

    /// <summary>Writes to the PIT window at ports 0x40-0x43: offset 3 is the control word (channel
    /// select, byte access, mode; access 0 latches the counter instead), offsets 0-2 write the
    /// selected channel's reload count byte.</summary>
    /// <param name="offset">Byte offset from port 0x40 (0-2 counter, 3 control).</param>
    /// <param name="size">Access width in bytes (ignored; the PIT counter port takes one byte).</param>
    /// <param name="value">Value whose low byte is written.</param>
    public void Write(ushort offset, int size, uint value)
    {
        if (offset == 3)
        {
            Configure((byte)value);
            return;
        }

        _channels[offset & 3].WriteCounter((byte)value);
    }

    /// <summary>Gets channel 0's programmed divisor (0 means 65536).</summary>
    public ushort Channel0Divisor => _channels[0].Divisor;

    /// <summary>Gets channel 0's current mode.</summary>
    public int Channel0Mode => _channels[0].Mode;

    /// <summary>Advances the timer by the given number of PIT input ticks.</summary>
    public void Advance(ulong ticks)
    {
        for (int i = 0; i < _channels.Length; i++)
        {
            if (_channels[i].Advance(ticks))
            {
                Output?.Invoke(i);
            }
        }
    }

    /// <summary>Returns the PIT to its power-on state: clears the control word and resets all three
    /// channels (divisor 0, stopped, latches cleared).</summary>
    public void Reset()
    {
        _control = 0;
        foreach (Channel channel in _channels)
        {
            channel.Reset();
        }
    }

    /// <summary>Saves PIT control plus all three channel states (quiesce: any point; counters are exact).</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(_control);
        foreach (Channel channel in _channels)
        {
            channel.SaveState(writer);
        }
    }

    /// <summary>Restores PIT control plus all three channel states.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported PIT snapshot version {version}.");
        }

        _control = reader.ReadByte();
        foreach (Channel channel in _channels)
        {
            channel.LoadState(reader);
        }
    }

    private const int SnapshotVersion = 1;

    private void Configure(byte value)
    {
        int channel = (value >> 6) & 3;
        int access = (value >> 4) & 3;

        if (channel == 3)
        {
            return;
        }

        if (access == 0)
        {
            _channels[channel].Latch();
            return;
        }

        _channels[channel].Configure(access, (value >> 1) & 7);
    }

    private sealed class Channel
    {
        private ushort _counter;
        private int _access;
        private int _writePhase;
        private int _readPhase;
        private byte _writeLow;
        private bool _running;
        private bool _latched;
        private ushort _latchedValue;

        public void Reset()
        {
            Divisor = 0;
            _counter = 0;
            _access = 3;
            Mode = 0;
            _writePhase = 0;
            _readPhase = 0;
            _running = false;
            _latched = false;
            _latchedValue = 0;
        }

        public void Configure(int access, int mode)
        {
            _access = access;
            Mode = mode;
            _writePhase = 0;
        }

        public ushort Divisor { get; private set; }

        public int Mode { get; private set; }

        public void SaveState(BinaryWriter writer)
        {
            writer.Write(_counter);
            writer.Write(_access);
            writer.Write(_writePhase);
            writer.Write(_readPhase);
            writer.Write(_writeLow);
            writer.Write(_running);
            writer.Write(_latched);
            writer.Write(_latchedValue);
            writer.Write(Divisor);
            writer.Write(Mode);
        }

        public void LoadState(BinaryReader reader)
        {
            _counter = reader.ReadUInt16();
            _access = reader.ReadInt32();
            _writePhase = reader.ReadInt32();
            _readPhase = reader.ReadInt32();
            _writeLow = reader.ReadByte();
            _running = reader.ReadBoolean();
            _latched = reader.ReadBoolean();
            _latchedValue = reader.ReadUInt16();
            Divisor = reader.ReadUInt16();
            Mode = reader.ReadInt32();
        }

        public void WriteCounter(byte value)
        {
            switch (_access)
            {
                case 1:
                    Divisor = value;
                    break;
                case 2:
                    Divisor = (ushort)(value << 8);
                    break;
                default:
                    if (_writePhase == 0)
                    {
                        _writeLow = value;
                        _writePhase = 1;
                        return;
                    }

                    Divisor = (ushort)(_writeLow | (value << 8));
                    _writePhase = 0;
                    break;
            }

            _counter = Divisor;
            _running = Divisor != 0;
        }

        public void Latch()
        {
            _latchedValue = _counter;
            _latched = true;
        }

        public uint ReadCounter()
        {
            ushort value = _latched ? _latchedValue : _counter;

            switch (_access)
            {
                case 1:
                    _latched = false;
                    return (byte)value;
                case 2:
                    _latched = false;
                    return (byte)(value >> 8);
                default:
                    if (_readPhase == 0)
                    {
                        _readPhase = 1;
                        return (byte)value;
                    }

                    _readPhase = 0;
                    _latched = false;
                    return (byte)(value >> 8);
            }
        }

        /// <summary>Advances the channel; returns true when the output pulsed.</summary>
        public bool Advance(ulong ticks)
        {
            if (!_running)
            {
                return false;
            }

            ulong counter = _counter;
            if (ticks < counter)
            {
                _counter = (ushort)(counter - ticks);
                return false;
            }

            switch (Mode)
            {
                case 0:
                    _counter = 0;
                    _running = false;
                    return true;
                case 2:
                case 3:
                    {
                        ulong remainder = (counter == 0 ? 0 : ticks - counter) % Math.Max(1u, Divisor);
                        _counter = (ushort)(Divisor - remainder);
                        if (_counter == 0)
                        {
                            _counter = Divisor;
                        }

                        return true;
                    }
                default:
                    _counter = Divisor;
                    return true;
            }
        }
    }
}
