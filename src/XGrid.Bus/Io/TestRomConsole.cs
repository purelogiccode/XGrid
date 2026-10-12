using System.Collections.Generic;
using System.Text;

namespace XGrid.Bus.Io;

/// <summary>
/// Single-byte test-ROM console on I/O port 0xE9 (Bochs-style debug output).
/// </summary>
/// <remarks>
/// The guest writes ASCII bytes with <c>OUT 0xE9, AL</c>; a <c>0x0A</c> byte
/// completes a line. Completed lines are buffered in <see cref="Lines"/> so the
/// CLI can print them after the TESTROM PASS/FAIL verdict (which must be the
/// first line of output). Only the low byte of each write is consumed.
/// </remarks>
public sealed class TestRomConsole : IIoPortDevice
{
    private readonly StringBuilder _pending = new();
    private readonly List<string> _lines = new();

    /// <summary>Gets completed log lines (without the terminating newline).</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Gets the total number of bytes received on the port.</summary>
    public long ByteCount { get; private set; }

    /// <inheritdoc />
    public uint Read(ushort offset, int size)
    {
        return 0u;
    }

    /// <inheritdoc />
    public void Write(ushort offset, int size, uint value)
    {
        byte current = (byte)value;
        ByteCount++;
        if (current == 0x0A)
        {
            _lines.Add(_pending.ToString());
            _pending.Clear();
        }
        else
        {
            _pending.Append((char)current);
        }
    }

    /// <summary>Flushes any partial line so no guest output is lost at HLT.</summary>
    public void Flush()
    {
        if (_pending.Length > 0)
        {
            _lines.Add(_pending.ToString());
            _pending.Clear();
        }
    }
}
