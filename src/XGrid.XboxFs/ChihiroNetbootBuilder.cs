using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace XGrid.XboxFs;

/// <summary>
/// Builds a Sega Chihiro netboot image (a raw FATX volume) from an Xbox XISO
/// that carries a Chihiro file tree. The CONVERTER is for the homebrew
/// "XBOXOG - Chihiro" style images: their <c>boot.id</c>, game executable and
/// <c>media</c> tree are already decrypted, so no GD-ROM key is involved.
/// </summary>
/// <remarks>
/// The DIMM image size is one of the Chihiro board sizes (128, 256, 512 or
/// 1024 MiB). The FATX layout is computed for the whole DIMM, not just the
/// files, because the guest's FATX mount sees the DIMM size from the IDE
/// identify data. Files are packed from cluster 1; unused clusters stay zero.
/// </remarks>
public static class ChihiroNetbootBuilder
{
    private const int SectorSize = 512;
    private const int SuperblockBytes = 4096;
    private const int ClusterSectors = 32;
    private const int ClusterBytes = ClusterSectors * SectorSize;
    private const int EntryBytes = 64;
    private const int MaxNameBytes = 42;
    private const long MiB = 1024 * 1024;
    private const uint FatEnd16 = 0xFFFF;
    private const uint FatEnd32 = 0xFFFFFFFF;

    /// <summary>The converted image and the game metadata read from boot.id.</summary>
    /// <param name="OutputPath">Absolute path of the written .bin.</param>
    /// <param name="Executable">Game executable named by boot.id (after aliasing).</param>
    /// <param name="GameId">Four-character game id from boot.id.</param>
    /// <param name="Title">Title field from boot.id.</param>
    /// <param name="ImageBytes">Size of the written FATX netboot image (the game area).</param>
    /// <param name="DimmBytes">DIMM size the image is formatted for (game area + 16 MiB system area).</param>
    /// <param name="FileCount">Number of files copied.</param>
    public sealed record Result(
        string OutputPath,
        string Executable,
        string GameId,
        string Title,
        long ImageBytes,
        long DimmBytes,
        int FileCount);

    /// <summary>Converts a Chihiro XISO into a netboot FATX image.</summary>
    /// <param name="isoPath">Path to the Xbox/Chihiro XISO.</param>
    /// <param name="outputPath">Destination .bin path; an existing file is replaced.</param>
    /// <param name="progress">Optional 0..1 progress over copied file bytes.</param>
    /// <param name="cancellationToken">Cancels the copy between files.</param>
    /// <returns>The converted image metadata.</returns>
    public static Result Build(
        string isoPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string source = Path.GetFullPath(isoPath);
        string destination = Path.GetFullPath(outputPath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"The Chihiro XISO was not found: {source}", source);
        }

        using XdvdfsFileSystem xiso = XdvdfsFileSystem.Open(source);
        Node root = ReadDirectory(xiso, "\\", "\\");
        Node bootId = FindChild(root, "boot.id", directoryExpected: false)
            ?? throw new InvalidDataException(
                "The image has no boot.id; it is not a Chihiro game image.");

        byte[] bootData = xiso.ReadFile(bootId.SourcePath);
        (string executableName, string gameId, string title) = ReadBootId(bootData);
        Node? executable = FindNode(root, executableName);
        if (executable is null || executable.IsDirectory)
        {
            // The "XBOXOG - Chihiro" patched discs rename the original Chihiro
            // executable and keep it as <name>.bak; use it for the boot.id name.
            Node? original = FindNode(root, executableName + ".bak")
                ?? FindNode(root, "default.xbe");
            if (original is null || original.IsDirectory)
            {
                throw new InvalidDataException(
                    $"boot.id names '{executableName}', but the image has neither it, " +
                    $"'{executableName}.bak' nor default.xbe.");
            }

            _ = AddAlias(root, executableName, original);
        }

        long fileBytes = 0;
        int fileCount = 0;
        CountBytes(root, ref fileBytes, ref fileCount);
        long payloadClusters = CountClusters(root);

        (long imageBytes, long dimmBytes) = ChooseImageSize(payloadClusters);
        int imageSectors = checked((int)(imageBytes / SectorSize));
        int maxCluster = (imageSectors - 8) / ClusterSectors;
        int entries = maxCluster + 1;
        int fatEntryBytes = entries < 0xFFF0 ? 2 : 4;
        int fatBytes = checked(entries * fatEntryBytes);
        int dataStartSectors = 8 + RoundUp(fatBytes, SuperblockBytes) / SectorSize;
        int availableClusters = (imageSectors - dataStartSectors) / ClusterSectors;
        if (payloadClusters > availableClusters)
        {
            throw new InvalidDataException(
                $"The game needs {payloadClusters} clusters, but a {dimmBytes / MiB} MiB " +
                $"DIMM only leaves {availableClusters} after the FAT.");
        }

        uint nextCluster = 1;
        AssignClusters(root, ref nextCluster);
        uint[] fat = new uint[entries];
        MarkChains(root, fat, fatEntryBytes == 4 ? FatEnd32 : FatEnd16);

        string temporary = destination + ".tmp";
        _ = Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       1 << 20,
                       FileOptions.SequentialScan))
            {
                stream.SetLength(imageBytes);
                WriteSuperblock(stream);
                WriteFat(stream, fat, fatEntryBytes);
                var state = new CopyState(
                    xiso,
                    stream,
                    dataStartSectors * (long)SectorSize,
                    fileBytes,
                    progress,
                    cancellationToken);
                WriteDirectoryTree(root, state);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        return new Result(destination, executableName, gameId, title, imageBytes, dimmBytes, fileCount);
    }

    private static Node ReadDirectory(XdvdfsFileSystem xiso, string path, string name)
    {
        var node = new Node(name, path, isDirectory: true);
        foreach (XdvdfsFileSystem.Entry entry in xiso.ListDirectory(path))
        {
            if (entry.Name is "." or "..")
            {
                continue;
            }

            ValidateName(entry.Name);
            if (entry.IsDirectory)
            {
                node.Children.Add(ReadDirectory(xiso, entry.FullPath, entry.Name));
            }
            else
            {
                if (entry.Size is < 0 or > uint.MaxValue)
                {
                    throw new InvalidDataException(
                        $"File '{entry.Name}' is too large for FATX ({entry.Size} bytes).");
                }

                node.Children.Add(new Node(entry.Name, entry.FullPath, isDirectory: false)
                {
                    Size = entry.Size,
                });
            }
        }

        node.Children.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        return node;
    }

    private static (string Executable, string GameId, string Title) ReadBootId(byte[] bootData)
    {
        if (bootData.Length < 0xC0
            || !bootData.AsSpan(0, 4).SequenceEqual("BTID"u8)
            || !bootData.AsSpan(0x20, 4).SequenceEqual("XBAM"u8))
        {
            throw new InvalidDataException("boot.id is not a Sega netboot boot.id.");
        }

        string executable = ReadAscii(bootData, 0xA0, 31).TrimStart('\\', '/');
        if (executable.Length == 0)
        {
            throw new InvalidDataException("boot.id names no game executable.");
        }

        ValidateExecutable(executable);
        string gameId = ReadAscii(bootData, 0x30, 4).Trim();
        string title = ReadAscii(bootData, 0x80, 32).Trim();
        return (executable, gameId, title);
    }

    private static string ReadAscii(byte[] data, int offset, int length)
    {
        int end = offset;
        int limit = Math.Min(data.Length, offset + length);
        while (end < limit && data[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    private static void ValidateExecutable(string name)
    {
        if (name.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"boot.id names an unsafe executable: '{name}'.");
        }

        foreach (string part in name.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, "..", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"boot.id names an unsafe executable: '{name}'.");
            }
        }
    }

    private static void ValidateName(string name)
    {
        if (name.Length is 0 or > MaxNameBytes)
        {
            throw new InvalidDataException(
                $"The XISO name '{name}' does not fit FATX (1..{MaxNameBytes} characters).");
        }

        foreach (char c in name)
        {
            if (c is < ' ' or > '~' || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
            {
                throw new InvalidDataException(
                    $"The XISO name '{name}' contains a character FATX does not allow.");
            }
        }
    }

    private static Node? FindChild(Node directory, string name, bool directoryExpected)
    {
        foreach (Node child in directory.Children)
        {
            if (child.IsDirectory == directoryExpected
                && string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static Node? FindNode(Node root, string path)
    {
        Node current = root;
        foreach (string part in path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            Node? next = FindChild(current, part, directoryExpected: true)
                ?? FindChild(current, part, directoryExpected: false);
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static Node AddAlias(Node root, string path, Node target)
    {
        string[] parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new InvalidDataException("boot.id names an empty executable path.");
        }

        Node parent = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            parent = FindChild(parent, parts[i], directoryExpected: true)
                ?? throw new InvalidDataException(
                    $"boot.id names '{path}', but its directory does not exist in the image.");
        }

        if (FindChild(parent, parts[^1], directoryExpected: false) is not null)
        {
            throw new InvalidDataException(
                $"boot.id names '{path}', which already exists in the image.");
        }

        var alias = new Node(parts[^1], target.SourcePath, isDirectory: false)
        {
            Size = target.Size,
        };
        parent.Children.Add(alias);
        parent.Children.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        return alias;
    }

    private static void CountBytes(Node node, ref long bytes, ref int files)
    {
        if (node.IsDirectory)
        {
            foreach (Node child in node.Children)
            {
                CountBytes(child, ref bytes, ref files);
            }

            return;
        }

        bytes = checked(bytes + node.Size);
        files++;
    }

    private static long CountClusters(Node node)
    {
        if (!node.IsDirectory)
        {
            return FileClusters(node.Size);
        }

        long clusters = DirectoryClusters(node.Children.Count);
        foreach (Node child in node.Children)
        {
            clusters = checked(clusters + CountClusters(child));
        }

        return clusters;
    }

    private static (long ImageBytes, long DimmBytes) ChooseImageSize(long payloadClusters)
    {
        // The DIMM's last 0x8000 sectors are the media board's system area
        // (mbsys/mbcom); the FATX game volume is the DIMM minus 16 MiB. The
        // netboot file is exactly that game volume, which is also what makes
        // chihiro_dimm_factor() pick the intended board size.
        const long systemArea = 0x8000L * SectorSize;
        for (long dimmBytes = 128 * MiB; dimmBytes <= 1024 * MiB; dimmBytes *= 2)
        {
            long imageBytes = dimmBytes - systemArea;
            int sectors = checked((int)(imageBytes / SectorSize));
            int maxCluster = (sectors - 8) / ClusterSectors;
            int entries = maxCluster + 1;
            int entryBytes = entries < 0xFFF0 ? 2 : 4;
            int fatBytes = checked(entries * entryBytes);
            int dataStart = 8 + RoundUp(fatBytes, SuperblockBytes) / SectorSize;
            int available = (sectors - dataStart) / ClusterSectors;
            if (payloadClusters <= available)
            {
                return (imageBytes, dimmBytes);
            }
        }

        throw new InvalidDataException(
            "The game does not fit a 1024 MiB Chihiro DIMM.");
    }

    private static void AssignClusters(Node node, ref uint nextCluster)
    {
        if (node.IsDirectory)
        {
            node.StartCluster = nextCluster;
            node.ClusterCount = checked((uint)DirectoryClusters(node.Children.Count));
            nextCluster = checked(nextCluster + node.ClusterCount);
            foreach (Node child in node.Children)
            {
                AssignClusters(child, ref nextCluster);
            }

            return;
        }

        node.ClusterCount = checked((uint)FileClusters(node.Size));
        if (node.ClusterCount == 0)
        {
            node.StartCluster = 0;
            return;
        }

        node.StartCluster = nextCluster;
        nextCluster = checked(nextCluster + node.ClusterCount);
    }

    private static void MarkChains(Node node, uint[] fat, uint endMarker)
    {
        if (node.ClusterCount > 0)
        {
            uint start = node.StartCluster;
            for (uint i = 0; i < node.ClusterCount; i++)
            {
                uint cluster = start + i;
                if (cluster >= fat.Length)
                {
                    throw new InvalidDataException("FATX cluster allocation left the volume.");
                }

                fat[cluster] = i == node.ClusterCount - 1 ? endMarker : cluster + 1;
            }
        }

        if (node.IsDirectory)
        {
            foreach (Node child in node.Children)
            {
                MarkChains(child, fat, endMarker);
            }
        }
    }

    private static void WriteSuperblock(Stream stream)
    {
        byte[] superblock = new byte[SuperblockBytes];
        "FATX"u8.CopyTo(superblock);
        // A stable non-zero serial; the guest does not care about its value.
        BinaryPrimitives.WriteUInt32LittleEndian(superblock.AsSpan(4), 0x58475244u);
        BinaryPrimitives.WriteUInt32LittleEndian(superblock.AsSpan(8), ClusterSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(superblock.AsSpan(12), 1);
        stream.Position = 0;
        stream.Write(superblock);
    }

    private static void WriteFat(Stream stream, uint[] fat, int entryBytes)
    {
        byte[] bytes = new byte[checked(fat.Length * entryBytes)];
        for (int i = 0; i < fat.Length; i++)
        {
            if (entryBytes == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(i * 2),
                    fat[i] >= FatEnd16 ? (ushort)FatEnd16 : (ushort)fat[i]);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), fat[i]);
            }
        }

        stream.Position = SuperblockBytes;
        stream.Write(bytes);
    }

    private static void WriteDirectoryTree(Node node, CopyState state)
    {
        state.CancellationToken.ThrowIfCancellationRequested();
        if (node.IsDirectory)
        {
            byte[] directory = new byte[checked((int)node.ClusterCount * ClusterBytes)];
            int position = 0;
            foreach (Node child in node.Children)
            {
                WriteDirectoryEntry(directory, position, child);
                position += EntryBytes;
            }

            directory[position] = 0xFF;
            state.Stream.Position = ClusterOffset(state.DataStart, node.StartCluster);
            state.Stream.Write(directory);
            foreach (Node child in node.Children)
            {
                WriteDirectoryTree(child, state);
            }

            return;
        }

        if (node.ClusterCount == 0)
        {
            return;
        }

        byte[] data = state.Xiso.ReadFile(node.SourcePath);
        if (data.LongLength != node.Size)
        {
            throw new InvalidDataException(
                $"The XISO returned {data.LongLength} bytes for '{node.Name}', expected {node.Size}.");
        }

        state.Stream.Position = ClusterOffset(state.DataStart, node.StartCluster);
        state.Stream.Write(data);
        state.CopiedBytes += data.Length;
        if (state.Progress is not null && state.TotalBytes > 0)
        {
            state.Progress.Report(Math.Clamp(
                (double)state.CopiedBytes / state.TotalBytes,
                0.0,
                1.0));
        }
    }

    private static void WriteDirectoryEntry(byte[] directory, int position, Node child)
    {
        byte[] name = Encoding.ASCII.GetBytes(child.Name);
        directory[position] = checked((byte)name.Length);
        directory[position + 1] = child.IsDirectory ? (byte)0x10 : (byte)0x20;
        name.CopyTo(directory, position + 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            directory.AsSpan(position + 44),
            child.StartCluster);
        BinaryPrimitives.WriteUInt32LittleEndian(
            directory.AsSpan(position + 48),
            checked((uint)child.Size));
    }

    private static long ClusterOffset(long dataStart, uint cluster)
    {
        return dataStart + checked(((long)cluster - 1) * ClusterBytes);
    }

    private static int RoundUp(int value, int multiple)
    {
        return checked(((value + multiple - 1) / multiple) * multiple);
    }

    private static long FileClusters(long size)
    {
        return size == 0 ? 0 : (size + ClusterBytes - 1) / ClusterBytes;
    }

    private static long DirectoryClusters(int childCount)
    {
        long bytes = checked(((long)childCount + 1) * EntryBytes);
        return Math.Max(1, (bytes + ClusterBytes - 1) / ClusterBytes);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class Node(string name, string sourcePath, bool isDirectory)
    {
        internal string Name { get; } = name;

        internal string SourcePath { get; } = sourcePath;

        internal bool IsDirectory { get; } = isDirectory;

        internal long Size { get; init; }

        internal List<Node> Children { get; } = [];

        internal uint StartCluster { get; set; }

        internal uint ClusterCount { get; set; }
    }

    private sealed class CopyState(
        XdvdfsFileSystem xiso,
        Stream stream,
        long dataStart,
        long totalBytes,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        internal XdvdfsFileSystem Xiso { get; } = xiso;

        internal Stream Stream { get; } = stream;

        internal long DataStart { get; } = dataStart;

        internal long TotalBytes { get; } = totalBytes;

        internal IProgress<double>? Progress { get; } = progress;

        internal CancellationToken CancellationToken { get; } = cancellationToken;

        internal long CopiedBytes { get; set; }
    }
}
