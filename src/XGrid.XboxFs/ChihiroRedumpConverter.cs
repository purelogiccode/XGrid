// Chihiro Redump GD-ROM -> FATX netboot converter.
//
// The format and the DES byte-swap scheme follow the MIT-licensed
// chihiro-netboot by Tovarichtch (https://github.com/Tovarichtch/chihiro-netboot),
// itself based on JayFoxRox's Chihiro-Tools. This is an independent C#
// implementation; the original MIT notice follows.
//
// MIT License
//
// Copyright (c) 2017 Jannik Vogel (JayFoxRox)
// Copyright (c) 2026 Réda Chérif-Touil (Tovarichtch)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
// SPDX-License-Identifier: MIT
//
// XGrid is GPL-2.0-or-later; the MIT terms are compatible. See
// THIRD_PARTY_NOTICES.md.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace XGrid.XboxFs;

/// <summary>
/// Converts a Sega Chihiro Redump GD-ROM cue/bin set into the FATX netboot
/// image the emulated DIMM boots. The game's small MAME key ZIP supplies the
/// DES key and the loader name; the high-density track is read directly, so
/// no extraction or external tool is involved.
/// </summary>
public static class ChihiroRedumpConverter
{
    private const int IsoSector = 2048;
    private const int RawSector = 2352;
    private const long GdLbaOffset = 45000;

    /// <summary>The converted image and the files selected from the disc.</summary>
    /// <param name="OutputPath">Absolute path of the written .bin.</param>
    /// <param name="KeySource">Key entry that unlocked the image.</param>
    /// <param name="Loader">Loader file named by the key entry.</param>
    /// <param name="FatxName">FATX file the loader names.</param>
    /// <param name="ImageBytes">Size of the written FATX image.</param>
    public sealed record Result(
        string OutputPath,
        string KeySource,
        string Loader,
        string FatxName,
        long ImageBytes);

    /// <summary>Converts one Redump cue/bin set.</summary>
    /// <param name="cuePath">The Redump .cue file.</param>
    /// <param name="keyZipPath">The game's small MAME key .zip.</param>
    /// <param name="outputPath">Destination .bin; an existing file is replaced.</param>
    /// <param name="progress">Optional 0..1 progress over the FATX bytes.</param>
    /// <param name="cancellationToken">Cancels between chunks.</param>
    /// <returns>The converted image metadata.</returns>
    public static Result Convert(
        string cuePath,
        string keyZipPath,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cuePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyZipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!File.Exists(cuePath))
        {
            throw new FileNotFoundException($"The cue sheet was not found: {cuePath}", cuePath);
        }

        if (!File.Exists(keyZipPath))
        {
            throw new FileNotFoundException($"The key ZIP was not found: {keyZipPath}", keyZipPath);
        }

        using GdTrackReader track = GdTrackReader.Open(cuePath);
        IsoVolume volume = IsoVolume.Open(track);
        List<KeyCandidate> keys = LoadKeys(keyZipPath);
        if (keys.Count == 0)
        {
            throw new InvalidDataException($"No Chihiro key was found in {Path.GetFileName(keyZipPath)}.");
        }

        (KeyCandidate key, IsoFile fatx, string loaderName) = SelectImage(volume, track, keys);
        string destination = Path.GetFullPath(outputPath);
        string temporary = destination + ".tmp";
        _ = Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");

        try
        {
            DecryptImage(track, fatx, key.Key, temporary, progress, cancellationToken);

            using (FileStream check = File.OpenRead(temporary))
            {
                Span<byte> magic = stackalloc byte[4];
                if (check.Read(magic) != magic.Length || !magic.SequenceEqual("FATX"u8))
                {
                    throw new InvalidDataException(
                        "The selected key did not produce a FATX image (wrong key?).");
                }
            }

            File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        return new Result(destination, key.Source, loaderName, fatx.Name, fatx.Size);
    }

    private static (KeyCandidate Key, IsoFile Fatx, string Loader) SelectImage(
        IsoVolume volume,
        GdTrackReader track,
        List<KeyCandidate> keys)
    {
        foreach (KeyCandidate key in keys)
        {
            IsoFile? loader = volume.FindFile(key.Loader);
            if (loader is null)
            {
                continue;
            }

            byte[] loaderData = track.ReadFile(loader);
            if (loaderData.Length < 0xE0)
            {
                continue;
            }

            string fatxName = ReadFixedAscii(loaderData, 0xC0, 0x20);
            if (fatxName.Length == 0)
            {
                continue;
            }

            IsoFile? fatx = volume.FindFile(fatxName);
            if (fatx is not null && fatx.Size >= 8)
            {
                return (key, fatx, key.Loader);
            }
        }

        // A CD-ROM-shaped image has no loader file; the tool falls back to the
        // largest file and the first key.
        IsoFile? largest = volume.FindLargestFile();
        if (largest is null || largest.Size < 8)
        {
            throw new InvalidDataException(
                "The disc has no loader/FATX pair and no large file to decrypt.");
        }

        return (keys[0], largest, "(largest file on disc)");
    }

    private static void DecryptImage(
        GdTrackReader track,
        IsoFile fatx,
        byte[] key,
        string temporary,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if ((fatx.Size & 7) != 0)
        {
            throw new InvalidDataException(
                $"The encrypted FATX size ({fatx.Size}) is not a multiple of 8 bytes.");
        }

        byte[] desKey = key[..];
        Array.Reverse(desKey);
#pragma warning disable CA5351 // The Chihiro media board's container is DES-ECB.
        using DES des = DES.Create();
#pragma warning restore CA5351
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key = desKey;
        using ICryptoTransform decryptor = des.CreateDecryptor();

        const int chunkBytes = 8 * 1024 * 1024;
        byte[] input = new byte[chunkBytes];
        byte[] output = new byte[chunkBytes];
        using var stream = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1 << 20);

        long offset = 0;
        while (offset < fatx.Size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int chunk = (int)Math.Min(chunkBytes, fatx.Size - offset);
            chunk &= ~7;
            if (chunk == 0)
            {
                break;
            }

            track.ReadFileRange(fatx.Lba, fatx.Size, offset, input.AsSpan(0, chunk));
            ByteSwap(input.AsSpan(0, chunk));
            int written = decryptor.TransformBlock(input, 0, chunk, output, 0);
            ByteSwap(output.AsSpan(0, written));
            stream.Write(output, 0, written);
            offset += chunk;
            progress?.Report((double)offset / fatx.Size);
        }

        if (offset != fatx.Size)
        {
            throw new InvalidDataException("The FATX image ended on a partial DES block.");
        }
    }

    private static void ByteSwap(Span<byte> data)
    {
        for (int i = 0; i + 8 <= data.Length; i += 8)
        {
            (data[i], data[i + 7]) = (data[i + 7], data[i]);
            (data[i + 1], data[i + 6]) = (data[i + 6], data[i + 1]);
            (data[i + 2], data[i + 5]) = (data[i + 5], data[i + 2]);
            (data[i + 3], data[i + 4]) = (data[i + 4], data[i + 3]);
        }
    }

    private static List<KeyCandidate> LoadKeys(string zipPath)
    {
        var candidates = new List<KeyCandidate>();
        var fallbacks = new List<KeyCandidate>();
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        string zipName = Path.GetFileName(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.EndsWith(".data", StringComparison.OrdinalIgnoreCase))
            {
                byte[] data = ReadEntry(entry);
                KeyCandidate? candidate = ParseDataKey(data, $"{zipName} -> {entry.FullName}");
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }
            else if (entry.FullName.EndsWith(".pic", StringComparison.OrdinalIgnoreCase))
            {
                byte[] data = ReadEntry(entry);
                KeyCandidate? candidate = ParsePicKey(data, $"{zipName} -> {entry.FullName}");
                if (candidate is null)
                {
                    continue;
                }

                if (IsBiosPic(Path.GetFileName(entry.FullName)))
                {
                    fallbacks.Add(candidate);
                }
                else
                {
                    candidates.Add(candidate);
                }
            }
        }

        candidates.AddRange(fallbacks);
        return candidates;
    }

    private static bool IsBiosPic(string name)
    {
        return name.Equals("317-0352-exp.pic", StringComparison.OrdinalIgnoreCase)
               || name.Equals("317-0422-jpn.pic", StringComparison.OrdinalIgnoreCase)
               || name.Equals("317-unknown.pic", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > 0x10000)
        {
            throw new InvalidDataException(
                $"The key entry '{entry.FullName}' is unexpectedly large ({entry.Length} bytes).");
        }

        using Stream stream = entry.Open();
        byte[] data = new byte[(int)entry.Length];
        stream.ReadExactly(data);
        return data;
    }

    private static KeyCandidate? ParseDataKey(byte[] data, string source)
    {
        if (data.Length != 80)
        {
            return null;
        }

        byte[] key = new byte[8];
        Array.Copy(data, 49, key, 0, 7);
        key[7] = data[41];
        bool any = false;
        foreach (byte b in key)
        {
            any |= b != 0;
        }

        string loader = ReadFixedAscii(data, 33, 7);
        if (!any || loader.Length == 0 || !IsPrintable(loader))
        {
            return null;
        }

        return new KeyCandidate(key, loader, source);
    }

    private static KeyCandidate? ParsePicKey(byte[] data, string source)
    {
        if (data.Length == 0x4000 + 16)
        {
            data = data[..0x4000];
        }

        if (data.Length != 0x4000)
        {
            return null;
        }

        int[] offsets =
        [
            0x6E0, 0x6BC, 0x6AA, 0x7DE, 0x7BE,
            0x79E, 0x77E, 0x698, 0x686, 0x674,
        ];
        byte[] response = new byte[80];
        for (int i = 0; i < offsets.Length; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                response[(i * 8) + j] = data[offsets[i] + (j * 2)];
            }
        }

        return ParseDataKey(response, source);
    }

    private static bool IsPrintable(string text)
    {
        foreach (char c in text)
        {
            if (c is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadFixedAscii(byte[] data, int offset, int length)
    {
        ReadOnlySpan<byte> span = data.AsSpan(offset, length);
        int end = span.IndexOf((byte)0);
        if (end < 0)
        {
            end = span.Length;
        }

        return Encoding.ASCII.GetString(span[..end]).Trim();
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

    private sealed record KeyCandidate(byte[] Key, string Loader, string Source);

    private sealed record IsoFile(string Name, uint Lba, uint Size);

    private sealed class GdTrackReader : IDisposable
    {
        private readonly FileStream _stream;
        private readonly int _sectorSize;
        private readonly long _lbaOffset;

        private GdTrackReader(FileStream stream, int sectorSize, long lbaOffset, uint sectorCount)
        {
            _stream = stream;
            _sectorSize = sectorSize;
            _lbaOffset = lbaOffset;
            SectorCount = sectorCount;
        }

        public uint SectorCount { get; }

        public uint PvdLba => (uint)_lbaOffset + 16;

        public static GdTrackReader Open(string cuePath)
        {
            string fullCue = Path.GetFullPath(cuePath);
            string directory = Path.GetDirectoryName(fullCue) ?? ".";
            string? currentFile = null;
            var tracks = new List<(string Path, int SectorSize)>();
            foreach (string raw in File.ReadLines(fullCue))
            {
                string line = raw.Trim();
                if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
                {
                    int first = line.IndexOf('"');
                    int last = line.LastIndexOf('"');
                    string name = first >= 0 && last > first
                        ? line[(first + 1)..last]
                        : line[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                    currentFile = Path.Combine(directory, name);
                }
                else if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase)
                         && currentFile is not null)
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        int sectorSize = parts[2].ToUpperInvariant() switch
                        {
                            "MODE1/2352" => RawSector,
                            "MODE1/2048" => IsoSector,
                            _ => 0,
                        };
                        if (sectorSize != 0)
                        {
                            tracks.Add((currentFile, sectorSize));
                        }
                    }
                }
            }

            (string Path, int SectorSize) chosen = default;
            long chosenSize = -1;
            foreach ((string path, int sectorSize) in tracks)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                long size = new FileInfo(path).Length;
                if (size > chosenSize)
                {
                    chosen = (path, sectorSize);
                    chosenSize = size;
                }
            }

            if (chosenSize <= 0)
            {
                throw new InvalidDataException(
                    $"No MODE1 data track with its .bin beside {Path.GetFileName(fullCue)} was found.");
            }

            string trackPath = chosen.Path
                ?? throw new InvalidDataException("The data track path is empty.");
            var stream = new FileStream(trackPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            uint sectors = (uint)(chosenSize / chosen.SectorSize);
            long offset = chosen.SectorSize == RawSector ? GdLbaOffset : 0;
            return new GdTrackReader(stream, chosen.SectorSize, offset, sectors);
        }

        public void ReadSector(uint lba, Span<byte> destination)
        {
            long index = (long)lba - _lbaOffset;
            if (index < 0 || index >= SectorCount)
            {
                throw new InvalidDataException($"ISO sector {lba} is outside the data track.");
            }

            _stream.Position = index * _sectorSize + (_sectorSize == RawSector ? 16 : 0);
            _stream.ReadExactly(destination[..IsoSector]);
        }

        public byte[] ReadFile(IsoFile file)
        {
            if (file.Size > int.MaxValue)
            {
                throw new InvalidDataException($"File '{file.Name}' is too large to read in one piece.");
            }

            byte[] data = new byte[(int)file.Size];
            ReadFileRange(file.Lba, file.Size, 0, data);
            return data;
        }

        public void ReadFileRange(uint lba, uint fileSize, long offset, Span<byte> destination)
        {
            if (offset < 0 || offset + destination.Length > fileSize)
            {
                throw new InvalidDataException("The file range is outside the ISO file.");
            }

            Span<byte> sector = stackalloc byte[IsoSector];
            int done = 0;
            while (done < destination.Length)
            {
                long position = offset + done;
                uint sectorLba = lba + (uint)(position / IsoSector);
                int inSector = (int)(position % IsoSector);
                if (inSector == 0 && destination.Length - done >= IsoSector)
                {
                    ReadSector(sectorLba, destination.Slice(done, IsoSector));
                    done += IsoSector;
                    continue;
                }

                ReadSector(sectorLba, sector);
                int count = Math.Min(IsoSector - inSector, destination.Length - done);
                sector.Slice(inSector, count).CopyTo(destination[done..]);
                done += count;
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }

    private sealed class IsoVolume
    {
        private readonly GdTrackReader _track;
        private readonly uint _rootLba;
        private readonly uint _rootSize;
        private List<IsoFile>? _files;

        private IsoVolume(GdTrackReader track, uint rootLba, uint rootSize)
        {
            _track = track;
            _rootLba = rootLba;
            _rootSize = rootSize;
        }

        public static IsoVolume Open(GdTrackReader track)
        {
            byte[] pvd = new byte[IsoSector];
            track.ReadSector(track.PvdLba, pvd);
            if (pvd[0] != 0x01 || !pvd.AsSpan(1, 5).SequenceEqual("CD001"u8))
            {
                throw new InvalidDataException("No ISO 9660 primary volume descriptor was found.");
            }

            uint rootLba = BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(156 + 2));
            uint rootSize = BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(156 + 10));
            return new IsoVolume(track, rootLba, rootSize);
        }

        public IsoFile? FindFile(string name)
        {
            return EnumerateFiles().Find(file =>
                string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public IsoFile? FindLargestFile()
        {
            IsoFile? best = null;
            foreach (IsoFile file in EnumerateFiles())
            {
                if (best is null || file.Size > best.Size)
                {
                    best = file;
                }
            }

            return best;
        }

        private List<IsoFile> EnumerateFiles()
        {
            if (_files is not null)
            {
                return _files;
            }

            var files = new List<IsoFile>();
            Walk(_rootLba, _rootSize, files);
            _files = files;
            return files;
        }

        private void Walk(uint directoryLba, uint directorySize, List<IsoFile> files)
        {
            byte[] sector = new byte[IsoSector];
            uint offset = 0;
            while (offset < directorySize)
            {
                uint sectorOffset = offset % IsoSector;
                if (sectorOffset == 0)
                {
                    _track.ReadSector(directoryLba + (offset / IsoSector), sector);
                }

                int start = (int)sectorOffset;
                int length = sector[start];
                if (length == 0)
                {
                    offset = ((offset / IsoSector) + 1) * IsoSector;
                    continue;
                }

                if (start + length > sector.Length || length < 34)
                {
                    throw new InvalidDataException("A malformed ISO directory record was found.");
                }

                uint fileLba = BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(start + 2));
                uint fileSize = BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(start + 10));
                byte flags = sector[start + 25];
                int nameLength = sector[start + 32];
                if (nameLength == 0 || start + 33 + nameLength > sector.Length)
                {
                    throw new InvalidDataException("A malformed ISO file name was found.");
                }

                string name = Encoding.ASCII.GetString(sector, start + 33, nameLength);
                offset += (uint)length;
                if (nameLength == 1 && (name[0] == '\0' || name[0] == '\u0001'))
                {
                    continue;
                }

                int semicolon = name.IndexOf(';', StringComparison.Ordinal);
                if (semicolon >= 0)
                {
                    name = name[..semicolon];
                }

                if ((flags & 0x02) != 0)
                {
                    Walk(fileLba, fileSize, files);
                }
                else
                {
                    files.Add(new IsoFile(name, fileLba, fileSize));
                }
            }
        }
    }
}
