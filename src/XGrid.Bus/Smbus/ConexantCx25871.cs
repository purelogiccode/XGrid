using System;

namespace XGrid.Bus.Smbus;

/// <summary>Conexant CX25871 video encoder stub (SMBus address 0x45) with a byte register file.</summary>
public sealed class ConexantCx25871 : SmbusDevice
{
    /// <summary>Conexant CX25871 SMBus address.</summary>
    public const byte DeviceAddress = 0x45;

    private readonly byte[] _registers = new byte[256];
    private byte _pointer;

    /// <summary>Creates the encoder stub.</summary>
    public ConexantCx25871()
        : base(DeviceAddress)
    {
    }

    /// <summary>Gets the register file (indexed by register number).</summary>
    public byte this[byte register] => _registers[register];

    /// <inheritdoc />
    public override bool TryReceiveByte(out byte value)
    {
        value = _registers[_pointer++];
        return true;
    }

    /// <inheritdoc />
    public override bool TryReadByte(byte command, out byte value)
    {
        _pointer = command;
        value = _registers[_pointer++];
        return true;
    }

    /// <inheritdoc />
    public override bool TryReadWord(byte command, out ushort value)
    {
        _pointer = command;
        value = (ushort)(_registers[_pointer] | (_registers[(byte)(_pointer + 1)] << 8));
        _pointer += 2;
        return true;
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
        _registers[_pointer++] = value;
        return true;
    }

    /// <inheritdoc />
    public override bool WriteWord(byte command, ushort value)
    {
        _pointer = command;
        _registers[_pointer++] = (byte)value;
        _registers[_pointer++] = (byte)(value >> 8);
        return true;
    }

    /// <inheritdoc />
    public override bool WriteBlock(byte command, ReadOnlySpan<byte> data)
    {
        _pointer = command;
        for (int i = 0; i < data.Length; i++)
        {
            _registers[_pointer++] = data[i];
        }

        return true;
    }
}
