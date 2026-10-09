using System;

namespace XGrid.Bus.Smbus;

/// <summary>24C02-compatible serial EEPROM on the SMBus (Xbox address 0x54).</summary>
public sealed class Eeprom24c02 : SmbusDevice
{
    /// <summary>Xbox EEPROM SMBus address.</summary>
    public const byte DeviceAddress = 0x54;

    /// <summary>Image size in bytes.</summary>
    public const int Size = 256;

    private readonly byte[] _data = new byte[Size];
    private int _pointer;

    /// <summary>Creates the EEPROM from an image (missing bytes stay 0xFF).</summary>
    public Eeprom24c02(ReadOnlySpan<byte> image = default)
        : base(DeviceAddress)
    {
        Array.Fill(_data, (byte)0xFF);
        if (!image.IsEmpty)
        {
            image[..Math.Min(image.Length, Size)].CopyTo(_data);
        }
    }

    /// <summary>Gets the current internal address pointer.</summary>
    public int AddressPointer => _pointer;

    /// <inheritdoc />
    public override bool TryReceiveByte(out byte value)
    {
        value = _data[_pointer];
        _pointer = (_pointer + 1) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override bool TryReadByte(byte command, out byte value)
    {
        _pointer = command;
        value = _data[_pointer];
        _pointer = (_pointer + 1) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override bool TryReadWord(byte command, out ushort value)
    {
        _pointer = command;
        value = (ushort)(_data[_pointer] | (_data[(_pointer + 1) & 0xFF] << 8));
        _pointer = (_pointer + 2) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override int ReadBlock(byte command, Span<byte> buffer)
    {
        _pointer = command;
        int count = Math.Min(buffer.Length, Size - _pointer);
        _data.AsSpan(_pointer, count).CopyTo(buffer);
        _pointer = (_pointer + count) & 0xFF;
        return count;
    }

    /// <inheritdoc />
    public override bool SendByte(byte value)
    {
        _pointer = value;
        return true;
    }

    /// <inheritdoc />
    public override bool WriteByte(byte command, byte value)
    {
        _pointer = command;
        _data[_pointer] = value;
        _pointer = (_pointer + 1) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override bool WriteWord(byte command, ushort value)
    {
        _pointer = command;
        _data[_pointer] = (byte)value;
        _pointer = (_pointer + 1) & 0xFF;
        _data[_pointer] = (byte)(value >> 8);
        _pointer = (_pointer + 1) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override bool WriteBlock(byte command, ReadOnlySpan<byte> data)
    {
        _pointer = command;
        for (int i = 0; i < data.Length; i++)
        {
            _data[_pointer] = data[i];
            _pointer = (_pointer + 1) & 0xFF;
        }

        return true;
    }
}
