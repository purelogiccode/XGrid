using System;
using XGrid.Bus.Io;
using XGrid.Core.Devices;

namespace XGrid.Bus.Timers;

/// <summary>8254 programmable interval timer (ports 0x40-0x43).</summary>
public sealed class Pit8254 : IXboxDevice, IIoPortDevice
{
    /// <summary>PIT input clock (1.193182 MHz).</summary>
    public const uint InputFrequency = 1193182;

    private readonly Channel[] _channels = { new(), new(), new() };
    private byte _control;

    /// <summary>Creates the PIT and registers its ports.</summary>
    public Pit8254(IoSpace io)
    {
        ArgumentNullException.ThrowIfNull(io);
        io.Register(0x40, 4, this);
        Reset();
    }

    /// <summary>Gets the device name used in traces and diagnostics.</summary>
    public string Name => "8254 PIT";

    /// <summary>Raised when a channel output pulses (channel 0 drives IRQ0).</summary>
    public event Action<int>? Output;

    /// <inheritdoc />
    public uint Read(ushort offset, int size)
    {
        if (offset == 3)
        {
            return _control;
        }

        return _channels[offset & 3].ReadCounter();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Reset()
    {
        _control = 0;
        foreach (Channel channel in _channels)
        {
            channel.Reset();
        }
    }

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
