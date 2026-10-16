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

    /// <summary>Gets the 7-bit SMBus address the device was created with.</summary>
    public byte Address { get; }

    /// <summary>Handles a quick command. This base implementation accepts every quick command and returns true.</summary>
    /// <param name="read">Whether the quick command is a read.</param>
    /// <returns>Always true (no error).</returns>
    public virtual bool Quick(bool read)
    {
        return true;
    }

    /// <summary>Reads one byte without a command (protocol BYTE). This base implementation rejects the protocol: it returns false and reports 0.</summary>
    /// <param name="value">Receives the byte read; 0 when the protocol is unsupported.</param>
    /// <returns>Always false (protocol unsupported).</returns>
    public virtual bool TryReceiveByte(out byte value)
    {
        value = 0;
        return false;
    }

    /// <summary>Reads one byte after a command (protocol BYTE_DATA). This base implementation rejects the protocol: it returns false and reports 0.</summary>
    /// <param name="command">The command byte sent before the read.</param>
    /// <param name="value">Receives the byte read; 0 when the protocol is unsupported.</param>
    /// <returns>Always false (protocol unsupported).</returns>
    public virtual bool TryReadByte(byte command, out byte value)
    {
        value = 0;
        return false;
    }

    /// <summary>Reads a word after a command (protocol WORD_DATA). This base implementation rejects the protocol: it returns false and reports 0.</summary>
    /// <param name="command">The command byte sent before the read.</param>
    /// <param name="value">Receives the word read; 0 when the protocol is unsupported.</param>
    /// <returns>Always false (protocol unsupported).</returns>
    public virtual bool TryReadWord(byte command, out ushort value)
    {
        value = 0;
        return false;
    }

    /// <summary>Reads a block after a command (protocol BLOCK_DATA). This base implementation rejects the protocol and returns -1 without touching the buffer.</summary>
    /// <param name="command">The command byte sent before the read.</param>
    /// <param name="buffer">Receives the block bytes; left untouched when the protocol is unsupported.</param>
    /// <returns>Always -1 (protocol unsupported).</returns>
    public virtual int ReadBlock(byte command, Span<byte> buffer)
    {
        return -1;
    }

    /// <summary>Writes one byte without a command (protocol BYTE). This base implementation rejects the protocol and returns false.</summary>
    /// <param name="value">The byte to write.</param>
    /// <returns>Always false (protocol unsupported).</returns>
    public virtual bool SendByte(byte value)
    {
        return false;
    }

    /// <summary>Writes one byte after a command (protocol BYTE_DATA). This base implementation rejects the protocol and returns false.</summary>
    /// <param name="command">The command byte sent before the write.</param>
    /// <param name="value">The byte to write.</param>
    /// <returns>Always false (protocol unsupported).</returns>
    public virtual bool WriteByte(byte command, byte value)
    {
        return false;
    }

    /// <summary>Writes a word after a command (protocol WORD_DATA). This base implementation rejects the protocol and returns false.</summary>
    /// <param name="command">The command byte sent before the write.</param>
    /// <param name="value">The word to write.</param>
    /// <returns>Always false (protocol unsupported).</returns>
    public virtual bool WriteWord(byte command, ushort value)
    {
        return false;
    }

    /// <summary>Writes a block after a command (protocol BLOCK_DATA). This base implementation rejects the protocol and returns false.</summary>
    /// <param name="command">The command byte sent before the write.</param>
    /// <param name="data">The block bytes to write.</param>
    /// <returns>Always false (protocol unsupported).</returns>
    public virtual bool WriteBlock(byte command, ReadOnlySpan<byte> data)
    {
        return false;
    }
}
