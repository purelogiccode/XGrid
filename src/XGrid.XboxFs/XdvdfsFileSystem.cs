using System;
using System.Collections.Generic;
using System.IO;
using XISOSharp;
using XISOSharp.Models;

namespace XGrid.XboxFs;

/// <summary>
/// Read-only XDVDFS filesystem reader for Xbox DVD images, backed by the
/// MIT-licensed XISOSharp library (NuGet <c>XISOSharp</c>, keep-open VFS mode).
/// Layout facts (verified against a real Redump image): the volume descriptor
/// lives at file sector 32 (magic <c>MICROSOFT*XBOX*MEDIA</c> at 0x00 and
/// 0x7EC, root sector u32 at 0x14, root size at 0x18); retail discs carry an
/// ISO9660 video partition first and the game area later (the library reports
/// the shift as <c>DiscLseek</c>); directory entries form an AVL-walked table
/// (u16 left/right child in dwords, u32 start sector, u32 size, u8 attributes,
/// u8 name length, Latin-1 name). All probing and parsing is delegated to
/// XISOSharp; this type only adapts its API to the <see cref="FatxFileSystem"/>
/// shape (Open/ListDirectory/ReadFile) and normalizes path separators.
/// </summary>
public sealed class XdvdfsFileSystem : IDisposable
{
    private readonly XisoExplorer _explorer;
    private bool _disposed;

    private XdvdfsFileSystem(XisoExplorer explorer)
    {
        _explorer = explorer;
    }

    /// <summary>A parsed XDVDFS directory entry.</summary>
    public readonly struct Entry
    {
        internal Entry(string name, string fullPath, byte attributes, uint startSector, long size, bool isDirectory)
        {
            Name = name;
            FullPath = fullPath;
            Attributes = attributes;
            StartSector = startSector;
            Size = size;
            IsDirectory = isDirectory;
        }

        /// <summary>Gets the file or directory name.</summary>
        public string Name { get; }

        /// <summary>Gets the image-internal full path.</summary>
        public string FullPath { get; }

        /// <summary>Gets the raw attribute byte.</summary>
        public byte Attributes { get; }

        /// <summary>Gets the partition-relative first data sector.</summary>
        public uint StartSector { get; }

        /// <summary>Gets the size in bytes (0 for directories).</summary>
        public long Size { get; }

        /// <summary>Gets whether the entry is a directory.</summary>
        public bool IsDirectory { get; }
    }

    /// <summary>Opens the XDVDFS volume in the given image file.</summary>
    public static XdvdfsFileSystem Open(string isoPath)
    {
        ArgumentNullException.ThrowIfNull(isoPath);
        try
        {
            var explorer = new XisoExplorer(isoPath, new XisoExplorerOptions { KeepOpen = true });
            if (!explorer.Volume.IsValid)
            {
                explorer.Dispose();
                throw new InvalidDataException($"Not an XDVDFS volume: '{isoPath}'.");
            }

            return new XdvdfsFileSystem(explorer);
        }
        catch (XisoFormatException exception)
        {
            throw new InvalidDataException($"Not an XDVDFS volume: '{isoPath}'.", exception);
        }
    }

    /// <summary>Lists the directory at the given path (both <c>/</c> and <c>\</c> work).</summary>
    public IReadOnlyList<Entry> ListDirectory(string path)
    {
        ThrowIfDisposed();
        string normalized = Normalize(path);
        var entries = new List<Entry>();
        foreach (ExplorerNode node in _explorer.ListChildren(normalized))
        {
            entries.Add(new Entry(node.Name, node.FullPath, node.Attributes, node.StartSector, node.Size, node.IsDirectory));
        }

        return entries;
    }

    /// <summary>Reads the file at the given path.</summary>
    public byte[] ReadFile(string path)
    {
        ThrowIfDisposed();
        string normalized = Normalize(path);
        ExplorerNode? node = _explorer.GetNode(normalized);
        if (node is null)
        {
            throw new FileNotFoundException($"File not found in image: '{path}'.");
        }

        return ReadNode(node);
    }

    /// <summary>Reads the given file entry.</summary>
    public byte[] ReadFile(Entry entry)
    {
        ThrowIfDisposed();
        if (entry.IsDirectory)
        {
            throw new InvalidOperationException($"'{entry.Name}' is a directory.");
        }

        ExplorerNode? node = _explorer.GetNode(entry.FullPath);
        if (node is null)
        {
            throw new FileNotFoundException($"File not found in image: '{entry.FullPath}'.");
        }

        return ReadNode(node);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _explorer.Dispose();
            _disposed = true;
        }
    }

    private byte[] ReadNode(ExplorerNode node)
    {
        if (node.IsDirectory)
        {
            throw new InvalidOperationException($"'{node.Name}' is a directory.");
        }

        using Stream stream = _explorer.OpenReadStream(node);
        byte[] data = new byte[node.Size];
        int offset = 0;
        while (offset < data.Length)
        {
            int read = stream.Read(data, offset, data.Length - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        if (offset != data.Length)
        {
            throw new InvalidDataException($"Short file data for '{node.FullPath}'.");
        }

        return data;
    }

    private static string Normalize(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.StartsWith('/') ? normalized : "/" + normalized;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
