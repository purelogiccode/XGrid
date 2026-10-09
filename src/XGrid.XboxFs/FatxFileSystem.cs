using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Storage;

namespace XGrid.XboxFs;

/// <summary>
/// Read-only FATX filesystem reader for Xbox hard-disk partitions.
/// Layout facts (verified against a real image): 4096-byte superblock with ASCII magic
/// "FATX", volume serial, cluster size in 512-byte sectors (32 on HDD); FAT of
/// little-endian entries after the superblock (16-bit below 0xFFF5 clusters, 32-bit
/// above; free entries read back as zero); data clusters numbered from 1 with the root
/// directory first; 64-byte directory entries (name length, attributes, 42-byte ASCII
/// name, start cluster, size; 0xFF ends a directory, 0x00 is a free slot). Timestamps
/// are not interpreted.
/// </summary>
public sealed class FatxFileSystem
{
    private const uint SuperblockSectors = 8;
    private const uint Signature = 0x58544146u;
    private const int EntrySize = 64;
    private const int MaxNameLength = 42;
    private const uint WideThreshold = 0xFFF5u;

    private readonly IDiskImage _disk;
    private readonly uint _base;
    private readonly uint _sectors;
    private readonly uint _clusterSectors;
    private readonly uint _dataStart;
    private readonly uint _maxCluster;
    private readonly bool _wide;
    private readonly uint _endMarker;

    private FatxFileSystem(
        IDiskImage disk,
        uint partitionLba,
        uint partitionSectors,
        uint clusterSectors,
        uint dataStart,
        uint maxCluster,
        bool wide)
    {
        _disk = disk;
        _base = partitionLba;
        _sectors = partitionSectors;
        _clusterSectors = clusterSectors;
        _dataStart = dataStart;
        _maxCluster = maxCluster;
        _wide = wide;
        _endMarker = wide ? 0xFFFFFFF8u : 0xFFF8u;
    }

    /// <summary>A parsed FATX directory entry.</summary>
    public readonly struct Entry
    {
        /// <summary>Creates an entry record.</summary>
        public Entry(string name, byte attributes, uint startCluster, uint size)
        {
            Name = name;
            Attributes = attributes;
            StartCluster = startCluster;
            Size = size;
        }

        /// <summary>Gets the file or directory name.</summary>
        public string Name { get; }

        /// <summary>Gets the attribute byte (0x10 = directory).</summary>
        public byte Attributes { get; }

        /// <summary>Gets the first data cluster (0 when empty).</summary>
        public uint StartCluster { get; }

        /// <summary>Gets the size in bytes.</summary>
        public uint Size { get; }

        /// <summary>Gets whether the entry is a directory.</summary>
        public bool IsDirectory => (Attributes & 0x10) != 0;
    }

    /// <summary>Opens the FATX partition at the given LBA with the given size in sectors.</summary>
    public static FatxFileSystem Open(IDiskImage disk, uint partitionLba, uint partitionSectors)
    {
        ArgumentNullException.ThrowIfNull(disk);
        if (partitionSectors < 16)
        {
            throw new ArgumentException("Partition is too small.", nameof(partitionSectors));
        }

        Span<byte> superblock = stackalloc byte[16];
        ReadSectors(disk, partitionLba, superblock);
        if (ReadU32(superblock, 0) != Signature)
        {
            throw new InvalidDataException(
                $"Not a FATX superblock at LBA 0x{partitionLba:X}.");
        }

        uint clusterSectors = ReadU32(superblock, 8);
        if (clusterSectors == 0 || clusterSectors > 256)
        {
            throw new InvalidDataException($"Bad FATX cluster size {clusterSectors}.");
        }

        uint maxCluster = (partitionSectors - SuperblockSectors) / clusterSectors;
        bool wide = maxCluster > WideThreshold;
        uint dataStart = FindDataStart(disk, partitionLba, clusterSectors, maxCluster, wide);
        return new FatxFileSystem(
            disk, partitionLba, partitionSectors, clusterSectors, dataStart, maxCluster, wide);
    }

    /// <summary>Lists the directory at the given path (both <c>/</c> and <c>\</c> work).</summary>
    public IReadOnlyList<Entry> ListDirectory(string path)
    {
        uint cluster = ResolveDirectory(path);
        var entries = new List<Entry>();
        foreach (uint dataCluster in WalkChain(cluster))
        {
            if (!ReadDirectoryCluster(dataCluster, entries))
            {
                break;
            }
        }

        return entries;
    }

    /// <summary>Reads the file at the given path.</summary>
    public byte[] ReadFile(string path)
    {
        return ReadFile(ResolveFile(path));
    }

    /// <summary>Reads the given file entry.</summary>
    public byte[] ReadFile(Entry entry)
    {
        if (entry.IsDirectory)
        {
            throw new InvalidOperationException($"'{entry.Name}' is a directory.");
        }

        byte[] data = new byte[entry.Size];
        if (entry.Size == 0)
        {
            return data;
        }

        int filled = 0;
        foreach (uint cluster in WalkChain(entry.StartCluster))
        {
            int want = Math.Min(data.Length - filled, (int)(_clusterSectors * 512));
            ReadCluster(cluster, data.AsSpan(filled, want));
            filled += want;
            if (filled >= data.Length)
            {
                break;
            }
        }

        if (filled < data.Length)
        {
            throw new InvalidDataException(
                $"File '{entry.Name}' is truncated at {filled} of {data.Length} bytes.");
        }

        return data;
    }

    private static uint FindDataStart(
        IDiskImage disk, uint baseLba, uint clusterSectors, uint maxCluster, bool wide)
    {
        Span<byte> sector = stackalloc byte[512];
        for (uint lba = baseLba + SuperblockSectors; lba < baseLba + SuperblockSectors + 4096; lba++)
        {
            ReadSectors(disk, lba, sector);
            int valid = 0;
            for (int offset = 0; offset + EntrySize <= sector.Length; offset += EntrySize)
            {
                if (TryParseEntry(sector.Slice(offset, EntrySize), maxCluster, out _))
                {
                    valid++;
                }
            }

            if (valid >= 3)
            {
                return lba;
            }
        }

        uint entryBytes = wide ? 4u : 2u;
        uint minFat = ((maxCluster * entryBytes) + 511u) / 512u;
        return baseLba + SuperblockSectors + minFat;
    }

    private static bool TryParseEntry(ReadOnlySpan<byte> raw, uint maxCluster, out Entry entry)
    {
        entry = default;
        if (raw.Length < EntrySize)
        {
            return false;
        }

        byte length = raw[0];
        if (length == 0xFF || length == 0x00 || length > MaxNameLength)
        {
            return false;
        }

        byte attributes = raw[1];
        if ((attributes & ~0x37u) != 0)
        {
            return false;
        }

        for (int i = 0; i < length; i++)
        {
            byte c = raw[2 + i];
            if (c < 32 || c > 126)
            {
                return false;
            }
        }

        uint start = ReadU32(raw, 44);
        uint size = ReadU32(raw, 48);
        if (start > maxCluster || size > 2000000000u)
        {
            return false;
        }

        string name = System.Text.Encoding.ASCII.GetString(raw.Slice(2, length));
        entry = new Entry(name, attributes, start, size);
        return true;
    }

    private uint ResolveDirectory(string path)
    {
        uint cluster = 1;
        foreach (string part in Split(path))
        {
            cluster = Find(cluster, part, expectDirectory: true).StartCluster;
        }

        return cluster;
    }

    private Entry ResolveFile(string path)
    {
        string[] parts = Split(path);
        if (parts.Length == 0)
        {
            throw new FileNotFoundException($"Empty path '{path}'.");
        }

        uint cluster = 1;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            cluster = Find(cluster, parts[i], expectDirectory: true).StartCluster;
        }

        return Find(cluster, parts[^1], expectDirectory: false);
    }

    private Entry Find(uint cluster, string name, bool expectDirectory)
    {
        bool wrongType = false;
        foreach (Entry entry in ListDirectoryClusters(cluster))
        {
            if (!string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.IsDirectory != expectDirectory)
            {
                wrongType = true;
                continue;
            }

            return entry;
        }

        if (wrongType)
        {
            throw new InvalidOperationException(
                expectDirectory ? $"'{name}' is not a directory." : $"'{name}' is not a file.");
        }

        if (expectDirectory)
        {
            throw new DirectoryNotFoundException($"Directory '{name}' was not found.");
        }

        throw new FileNotFoundException($"File '{name}' was not found.");
    }

    private List<Entry> ListDirectoryClusters(uint cluster)
    {
        var entries = new List<Entry>();
        foreach (uint dataCluster in WalkChain(cluster))
        {
            if (!ReadDirectoryCluster(dataCluster, entries))
            {
                break;
            }
        }

        return entries;
    }

    private bool ReadDirectoryCluster(uint cluster, List<Entry> entries)
    {
        Span<byte> sector = stackalloc byte[512];
        uint baseLba = ClusterToLba(cluster);
        for (uint i = 0; i < _clusterSectors; i++)
        {
            sector.Clear();
            ReadSectors(_disk, baseLba + i, sector);
            for (int offset = 0; offset + EntrySize <= sector.Length; offset += EntrySize)
            {
                ReadOnlySpan<byte> raw = sector.Slice(offset, EntrySize);
                if (raw[0] == 0xFF)
                {
                    return false;
                }

                if (TryParseEntry(raw, _maxCluster, out Entry entry))
                {
                    entries.Add(entry);
                }
            }
        }

        return true;
    }

    private IEnumerable<uint> WalkChain(uint start)
    {
        uint cluster = start;
        uint guard = _maxCluster + 1;
        while (cluster != 0 && cluster < _endMarker && guard-- > 0)
        {
            if (cluster > _maxCluster)
            {
                throw new InvalidDataException($"FAT chain leaves the partition at {cluster}.");
            }

            yield return cluster;
            cluster = ReadFat(cluster);
        }

        if (guard == 0)
        {
            throw new InvalidDataException("FAT chain loops.");
        }
    }

    private uint ReadFat(uint cluster)
    {
        int entrySize = _wide ? 4 : 2;
        uint byteOffset = cluster * (uint)entrySize;
        uint lba = _base + SuperblockSectors + (byteOffset / 512u);
        int offset = (int)(byteOffset % 512u);
        Span<byte> sector = stackalloc byte[512];
        if (offset + entrySize <= 512)
        {
            ReadSectors(_disk, lba, sector);
            return _wide ? ReadU32(sector, offset) : ReadU16(sector, offset);
        }

        Span<byte> next = stackalloc byte[512];
        ReadSectors(_disk, lba, sector);
        ReadSectors(_disk, lba + 1, next);
        return (uint)(sector[511] | (next[0] << 8));
    }

    private void ReadCluster(uint cluster, Span<byte> destination)
    {
        uint baseLba = ClusterToLba(cluster);
        Span<byte> sector = stackalloc byte[512];
        int filled = 0;
        while (filled < destination.Length)
        {
            ReadSectors(_disk, baseLba + (uint)(filled / 512), sector);
            int count = Math.Min(destination.Length - filled, 512);
            sector.Slice(0, count).CopyTo(destination.Slice(filled, count));
            filled += count;
        }
    }

    private uint ClusterToLba(uint cluster)
    {
        if (cluster == 0 || cluster > _maxCluster)
        {
            throw new InvalidDataException($"Cluster {cluster} is out of range.");
        }

        checked
        {
            uint lba = _dataStart + ((cluster - 1) * _clusterSectors);
            if (lba + _clusterSectors > _base + _sectors)
            {
                throw new InvalidDataException($"Cluster {cluster} leaves the partition.");
            }

            return lba;
        }
    }

    private static string[] Split(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static void ReadSectors(IDiskImage disk, uint lba, Span<byte> destination)
    {
        Span<byte> sector = stackalloc byte[512];
        int filled = 0;
        while (filled < destination.Length)
        {
            disk.ReadSector(lba + (uint)(filled / 512), sector);
            int count = Math.Min(destination.Length - filled, 512);
            sector.Slice(0, count).CopyTo(destination.Slice(filled, count));
            filled += count;
        }
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset)
    {
        return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
    }

    private static uint ReadU16(ReadOnlySpan<byte> data, int offset)
    {
        return (uint)(data[offset] | (data[offset + 1] << 8));
    }
}
