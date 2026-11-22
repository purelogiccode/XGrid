using System;

namespace XGrid.Cpu.Tcg;

/// <summary>
/// The translated-block (TB) cache for the managed TCG engine (P1S4). One entry
/// per guest EIP: an IR program covering a straight-line run of up to 16
/// instructions, keyed with the fetch defaults and the head instruction bytes.
/// Mapping changes and stores to decoded code pages clear it through the
/// decode-cache hooks.
/// </summary>
internal sealed class TcgBlockCache
{
    private const int Slots = 4096;
    private const int HeadBytes = 15;

    private readonly Entry?[] _entries = new Entry?[Slots];

    /// <summary>Gets the number of cached blocks.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the number of lookups served from the cache.</summary>
    public long Hits { get; private set; }

    /// <summary>Gets the number of lookups that formed a block.</summary>
    public long Misses { get; private set; }

    /// <summary>Tries to find a block for the guest EIP.</summary>
    /// <param name="eip">The guest EIP.</param>
    /// <param name="head">The first instruction's bytes.</param>
    /// <param name="operandSize">The default operand size.</param>
    /// <param name="addressSize">The default address size.</param>
    /// <param name="program">The cached program.</param>
    /// <param name="length">The block length in bytes.</param>
    /// <returns>True when the cache holds a valid block.</returns>
    public bool TryGet(
        uint eip,
        ReadOnlySpan<byte> head,
        int operandSize,
        int addressSize,
        out TcgProgram? program,
        out int length)
    {
        Entry? entry = _entries[eip & (Slots - 1)];
        if (entry is not null
            && entry.Eip == eip
            && entry.OperandSize == operandSize
            && entry.AddressSize == addressSize
            && head[..HeadBytes].SequenceEqual(entry.Head))
        {
            Hits++;
            program = entry.Program;
            length = entry.Length;
            return true;
        }

        program = null;
        length = 0;
        return false;
    }

    /// <summary>Stores one block.</summary>
    /// <param name="eip">The guest EIP.</param>
    /// <param name="head">The first instruction's bytes.</param>
    /// <param name="operandSize">The default operand size.</param>
    /// <param name="addressSize">The default address size.</param>
    /// <param name="program">The translated program.</param>
    /// <param name="length">The block length in bytes.</param>
    public void Put(
        uint eip,
        ReadOnlySpan<byte> head,
        int operandSize,
        int addressSize,
        TcgProgram program,
        int length)
    {
        Misses++;
        int slot = (int)(eip & (Slots - 1));
        if (_entries[slot] is null)
        {
            Count++;
        }

        _entries[slot] = new Entry
        {
            Eip = eip,
            OperandSize = operandSize,
            AddressSize = addressSize,
            Head = head[..HeadBytes].ToArray(),
            Program = program,
            Length = length,
        };
    }

    /// <summary>Drops every block (mapping change or decoded-code store).</summary>
    public void Clear()
    {
        if (Count == 0)
        {
            return;
        }

        Array.Clear(_entries);
        Count = 0;
    }

    private sealed class Entry
    {
        public uint Eip { get; init; }

        public int OperandSize { get; init; }

        public int AddressSize { get; init; }

        public byte[] Head { get; init; } = [];

        public TcgProgram Program { get; init; } = null!;

        public int Length { get; init; }
    }
}
