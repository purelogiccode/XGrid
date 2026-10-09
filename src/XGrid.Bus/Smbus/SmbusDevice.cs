using System;

namespace XGrid.Bus.Smbus;

/// <summary>An SMBus slave device (7-bit address).</summary>
public interface ISmbusDevice
{
    /// <summary>Gets the 7-bit SMBus address.</summary>
    byte Address { get; }

    /// <summary>Handles a quick command. Returns false on error.</summary>
    bool Quick(bool read);

    /// <summary>Reads one byte without a command (protocol BYTE).</summary>
    bool TryReceiveByte(out byte value);

    /// <summary>Reads one byte after a command (protocol BYTE_DATA).</summary>
    bool TryReadByte(byte command, out byte value);

    /// <summary>Reads a word after a command (protocol WORD_DATA).</summary>
    bool TryReadWord(byte command, out ushort value);

    /// <summary>Reads a block after a command; returns the byte count or -1.</summary>
    int ReadBlock(byte command, Span<byte> buffer);

    /// <summary>Writes one byte without a command (protocol BYTE).</summary>
    bool SendByte(byte value);

    /// <summary>Writes one byte after a command (protocol BYTE_DATA).</summary>
    bool WriteByte(byte command, byte value);

    /// <summary>Writes a word after a command (protocol WORD_DATA).</summary>
    bool WriteWord(byte command, ushort value);

    /// <summary>Writes a block after a command (protocol BLOCK_DATA).</summary>
    bool WriteBlock(byte command, ReadOnlySpan<byte> data);
}

/// <summary>Base class with error-returning defaults for unsupported protocols.</summary>
public abstract class SmbusDevice : ISmbusDevice
{
    /// <summary>Creates a device at the given 7-bit address.</summary>
    protected SmbusDevice(byte address)
    {
        Address = address;
    }

    /// <inheritdoc />
    public byte Address { get; }

    /// <inheritdoc />
    public virtual bool Quick(bool read)
    {
        return true;
    }

    /// <inheritdoc />
    public virtual bool TryReceiveByte(out byte value)
    {
        value = 0;
        return false;
    }

    /// <inheritdoc />
    public virtual bool TryReadByte(byte command, out byte value)
    {
        value = 0;
        return false;
    }

    /// <inheritdoc />
    public virtual bool TryReadWord(byte command, out ushort value)
    {
        value = 0;
        return false;
    }

    /// <inheritdoc />
    public virtual int ReadBlock(byte command, Span<byte> buffer)
    {
        return -1;
    }

    /// <inheritdoc />
    public virtual bool SendByte(byte value)
    {
        return false;
    }

    /// <inheritdoc />
    public virtual bool WriteByte(byte command, byte value)
    {
        return false;
    }

    /// <inheritdoc />
    public virtual bool WriteWord(byte command, ushort value)
    {
        return false;
    }

    /// <inheritdoc />
    public virtual bool WriteBlock(byte command, ReadOnlySpan<byte> data)
    {
        return false;
    }
}
