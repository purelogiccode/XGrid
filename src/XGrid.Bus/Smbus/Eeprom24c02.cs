using System;

namespace XGrid.Bus.Smbus;

/// <summary>24C02-compatible serial EEPROM on the SMBus (Xbox address 0x54).</summary>
public sealed class Eeprom24C02 : SmbusDevice
{
    /// <summary>Xbox EEPROM SMBus address.</summary>
    public const byte DeviceAddress = 0x54;

    /// <summary>Image size in bytes.</summary>
    public const int Size = 256;

    private readonly byte[] _data = new byte[Size];

    /// <summary>Creates the EEPROM from an image (missing bytes stay 0xFF).</summary>
    public Eeprom24C02(ReadOnlySpan<byte> image = default)
        : base(DeviceAddress)
    {
        Array.Fill(_data, (byte)0xFF);
        if (!image.IsEmpty)
        {
            image[..Math.Min(image.Length, Size)].CopyTo(_data);
        }
    }

    /// <summary>Gets the current internal address pointer.</summary>
    public int AddressPointer { get; private set; }

    /// <inheritdoc />
    public override bool TryReceiveByte(out byte value)
    {
        value = _data[AddressPointer];
        AddressPointer = (AddressPointer + 1) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override bool TryReadByte(byte command, out byte value)
    {
        AddressPointer = command;
        value = _data[AddressPointer];
        AddressPointer = (AddressPointer + 1) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override bool TryReadWord(byte command, out ushort value)
    {
        AddressPointer = command;
        value = (ushort)(_data[AddressPointer] | (_data[(AddressPointer + 1) & 0xFF] << 8));
        AddressPointer = (AddressPointer + 2) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override int ReadBlock(byte command, Span<byte> buffer)
    {
        AddressPointer = command;
        int count = Math.Min(buffer.Length, Size - AddressPointer);
        _data.AsSpan(AddressPointer, count).CopyTo(buffer);
        AddressPointer = (AddressPointer + count) & 0xFF;
        return count;
    }

    /// <inheritdoc />
    public override bool SendByte(byte value)
    {
        AddressPointer = value;
        return true;
    }

    /// <inheritdoc />
    public override bool WriteByte(byte command, byte value)
    {
        AddressPointer = command;
        _data[AddressPointer] = value;
        AddressPointer = (AddressPointer + 1) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override bool WriteWord(byte command, ushort value)
    {
        AddressPointer = command;
        _data[AddressPointer] = (byte)value;
        AddressPointer = (AddressPointer + 1) & 0xFF;
        _data[AddressPointer] = (byte)(value >> 8);
        AddressPointer = (AddressPointer + 1) & 0xFF;
        return true;
    }

    /// <inheritdoc />
    public override bool WriteBlock(byte command, ReadOnlySpan<byte> data)
    {
        AddressPointer = command;
        for (int i = 0; i < data.Length; i++)
        {
            _data[AddressPointer] = data[i];
            AddressPointer = (AddressPointer + 1) & 0xFF;
        }

        return true;
    }
}
