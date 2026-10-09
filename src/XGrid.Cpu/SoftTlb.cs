using System;

namespace XGrid.Cpu;

/// <summary>Direct-mapped software TLB for 4 KB pages.</summary>
internal sealed class SoftTlb
{
    private const int EntryCount = 256;

    private readonly Entry[] _entries = new Entry[EntryCount];

    public void Flush()
    {
        Array.Clear(_entries);
    }

    public void FlushPage(uint virtualPageNumber)
    {
        _entries[(int)(virtualPageNumber & (EntryCount - 1))].Valid = false;
    }

    public bool TryLookup(uint virtualPageNumber, out uint physicalPage, out bool writable)
    {
        Entry entry = _entries[(int)(virtualPageNumber & (EntryCount - 1))];
        if (entry.Valid && entry.VirtualPageNumber == virtualPageNumber)
        {
            physicalPage = entry.PhysicalPage;
            writable = entry.Writable;
            return true;
        }

        physicalPage = 0;
        writable = false;
        return false;
    }

    public void Insert(uint virtualPageNumber, uint physicalPage, bool writable)
    {
        _entries[(int)(virtualPageNumber & (EntryCount - 1))] = new Entry
        {
            VirtualPageNumber = virtualPageNumber,
            PhysicalPage = physicalPage,
            Writable = writable,
            Valid = true,
        };
    }

    private struct Entry
    {
        public uint VirtualPageNumber;
        public uint PhysicalPage;
        public bool Writable;
        public bool Valid;
    }
}
