using System;
using System.IO;
using XGrid.Core.Snapshots;

namespace XGrid.Bus.Smbus;

/// <summary>Conexant CX25871 video encoder stub (SMBus address 0x45) with a byte register file.</summary>
public sealed class ConexantCx25871 : SmbusDevice, ISnapshotable
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

    /// <summary>Saves the 256-byte register file plus the pointer (quiesce: no SMBus transaction in flight).</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(_registers);
        writer.Write(_pointer);
    }

    /// <summary>Restores the 256-byte register file plus the pointer.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported encoder snapshot version {version}.");
        }

        byte[] registers = reader.ReadBytes(256);
        if (registers.Length != 256)
        {
            throw new InvalidDataException("Truncated encoder snapshot.");
        }

        Array.Copy(registers, _registers, 256);
        _pointer = reader.ReadByte();
    }

    private const int SnapshotVersion = 1;

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
