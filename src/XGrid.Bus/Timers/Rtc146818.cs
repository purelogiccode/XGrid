using System;
using System.IO;
using XGrid.Bus.Io;
using XGrid.Core.Snapshots;

namespace XGrid.Bus.Timers;

/// <summary>MC146818-compatible real-time clock (ports 0x70/0x71, IRQ8).</summary>
public sealed class Rtc146818 : IIoPortDevice, ISnapshotable
{
    /// <summary>RTC crystal frequency.</summary>
    public const uint InputFrequency = 32768;

    private static readonly DateTime BaseTime = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly byte[] _cmos = new byte[64];
    private byte _index;
    private byte _statusA = 0x26;
    private byte _statusB = 0x02;
    private byte _statusC;
    private ulong _subTicks;
    private ulong _seconds;
    private ulong _periodAccum;

    /// <summary>Creates the RTC and registers its ports.</summary>
    public Rtc146818(IoSpace io)
    {
        ArgumentNullException.ThrowIfNull(io);
        io.Register(0x70, 2, this);
        _cmos[0x0D] = 0x80;
    }

    /// <summary>Gets the number of port accesses seen (diagnostics).</summary>
    public int AccessCount { get; private set; }

    /// <summary>Gets whether the periodic interrupt is enabled (status B bit 6).</summary>
    public bool PeriodicEnabled => (_statusB & 0x40) != 0;

    /// <summary>Raised when the periodic flag asserts (IRQ8).</summary>
    public event Action? Interrupt;

    /// <summary>Raised when status register C is read (clears the request).</summary>
    public event Action? InterruptAcknowledged;

    /// <summary>Advances the RTC by the given number of 32.768 kHz ticks.</summary>
    public void Advance(ulong ticks)
    {
        _subTicks += ticks;
        while (_subTicks >= InputFrequency)
        {
            _subTicks -= InputFrequency;
            _seconds++;
        }

        _periodAccum += ticks;
        ulong period = 1UL << ((_statusA & 0x0F) + 1);
        if (_periodAccum >= period)
        {
            _periodAccum %= period;
            _statusC |= 0x40;
            if ((_statusB & 0x40) != 0)
            {
                Interrupt?.Invoke();
            }
        }
    }

    /// <summary>Resets the RTC state (time is kept relative to the fixed base).</summary>
    public void Reset()
    {
        _subTicks = 0;
        _seconds = 0;
        _periodAccum = 0;
        _statusA = 0x26;
        _statusB = 0x02;
        _statusC = 0;
    }

    /// <summary>Saves CMOS, index, status registers, time base, and access count (quiesce: any point).</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(_cmos);
        writer.Write(_index);
        writer.Write(_statusA);
        writer.Write(_statusB);
        writer.Write(_statusC);
        writer.Write(_subTicks);
        writer.Write(_seconds);
        writer.Write(_periodAccum);
        writer.Write(AccessCount);
    }

    /// <summary>Restores CMOS, index, status registers, time base, and access count.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported RTC snapshot version {version}.");
        }

        byte[] cmos = reader.ReadBytes(64);
        if (cmos.Length != 64)
        {
            throw new InvalidDataException("Truncated RTC CMOS snapshot.");
        }

        Array.Copy(cmos, _cmos, 64);
        _index = reader.ReadByte();
        _statusA = reader.ReadByte();
        _statusB = reader.ReadByte();
        _statusC = reader.ReadByte();
        _subTicks = reader.ReadUInt64();
        _seconds = reader.ReadUInt64();
        _periodAccum = reader.ReadUInt64();
        AccessCount = reader.ReadInt32();
    }

    private const int SnapshotVersion = 1;

    /// <summary>Reads from the RTC window at ports 0x70/0x71: offset 0 returns the current index
    /// register, offset 1 reads the indexed register — time registers as BCD from the fixed 2000-01-01
    /// base, status A/B, status C (clearing it and raising <see cref="InterruptAcknowledged"/>), or
    /// plain CMOS — counting the access in <see cref="AccessCount"/>.</summary>
    /// <param name="offset">0 for the index port 0x70, 1 for the data port 0x71.</param>
    /// <param name="size">Access width in bytes (ignored; the RTC returns one byte).</param>
    public uint Read(ushort offset, int size)
    {
        AccessCount++;
        return offset == 0 ? _index : ReadData(_index);
    }

    /// <summary>Writes from the RTC window at ports 0x70/0x71: offset 0 sets the index register
    /// (masked to 6 bits), offset 1 writes the indexed register — status A (update-inhibit bit
    /// cleared) and status B stored directly, all other indices written to CMOS — counting the
    /// access in <see cref="AccessCount"/>.</summary>
    /// <param name="offset">0 for the index port 0x70, 1 for the data port 0x71.</param>
    /// <param name="size">Access width in bytes (ignored; the RTC takes one byte).</param>
    /// <param name="value">Value whose low byte is written.</param>
    public void Write(ushort offset, int size, uint value)
    {
        AccessCount++;
        if (offset == 0)
        {
            _index = (byte)(value & 0x3F);
            return;
        }

        WriteData(_index, (byte)value);
    }

    private byte ReadData(byte index)
    {
        if (index == 0x0C)
        {
            byte value = _statusC;
            _statusC = 0;
            InterruptAcknowledged?.Invoke();
            return value;
        }

        if (index <= 0x09)
        {
            return TimeRegister(index);
        }

        return index switch
        {
            0x0A => _statusA,
            0x0B => _statusB,
            _ => _cmos[index],
        };
    }

    private void WriteData(byte index, byte value)
    {
        switch (index)
        {
            case 0x0A:
                _statusA = (byte)(value & 0x7F);
                break;
            case 0x0B:
                _statusB = value;
                break;
            default:
                _cmos[index] = value;
                break;
        }
    }

    private byte TimeRegister(byte index)
    {
        DateTime time = BaseTime.AddSeconds(_seconds);
        return index switch
        {
            0x00 => ToBcd(time.Second),
            0x02 => ToBcd(time.Minute),
            0x04 => ToBcd(time.Hour),
            0x06 => ToBcd((int)time.DayOfWeek + 1),
            0x07 => ToBcd(time.Day),
            0x08 => ToBcd(time.Month),
            0x09 => ToBcd(time.Year % 100),
            _ => _cmos[index],
        };
    }

    private static byte ToBcd(int value)
    {
        return (byte)(((value / 10) << 4) | (value % 10));
    }
}
