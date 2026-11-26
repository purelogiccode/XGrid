using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace XGrid.XboxFs.Tests;

public sealed class ChihiroRedumpConverterTests : IDisposable
{
    private const int IsoSector = 2048;
    private const int RawSector = 2352;
    private const uint GdOffset = 45000;
    private const uint RootLba = GdOffset + 17;
    private const uint LoaderLba = GdOffset + 18;
    private const uint FatxLba = GdOffset + 19;
    private const int LoaderSize = 0xE0;
    private const int FatxSize = 4096;

    private readonly string _root;
    private readonly byte[] _plaintext;
    private readonly byte[] _key =
    [
        0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0,
    ];

    public ChihiroRedumpConverterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "xgrid-chihiro-redump-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
        _plaintext = new byte[FatxSize];
        Encoding.ASCII.GetBytes("FATX").CopyTo(_plaintext, 0);
        for (int i = 4; i < _plaintext.Length; i++)
        {
            _plaintext[i] = (byte)(i * 7);
        }
    }

    [Fact]
    public void Convert_ReadsTheCueDecryptsTheFatxAndNamesTheFiles()
    {
        string cue = WriteFixture();
        string keyZip = WriteKeyZip();
        string output = Path.Combine(_root, "game.bin");

        ChihiroRedumpConverter.Result result = ChihiroRedumpConverter.Convert(cue, keyZip, output);

        Assert.Equal(output, result.OutputPath);
        Assert.Equal("L.BIN", result.Loader);
        Assert.Equal("GAME.BIN", result.FatxName);
        Assert.Equal(FatxSize, result.ImageBytes);
        Assert.Equal(_plaintext, File.ReadAllBytes(output));
    }

    [Fact]
    public void Convert_ReadsA16400BytePicAndIgnoresTheTrailingBytes()
    {
        string cue = WriteFixture();
        string keyZip = WritePicKeyZip();
        string output = Path.Combine(_root, "pic.bin");

        ChihiroRedumpConverter.Result result = ChihiroRedumpConverter.Convert(cue, keyZip, output);

        Assert.Equal(FatxSize, result.ImageBytes);
        Assert.Contains(".pic", result.KeySource, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_plaintext, File.ReadAllBytes(output));
    }

    [Fact]
    public void Convert_RejectsAKeyThatDoesNotDecryptToFatx()
    {
        string cue = WriteFixture();
        string keyZip = WriteKeyZip();
        string output = Path.Combine(_root, "wrong.bin");
        _key[0] ^= 0xFF;
        string wrongKeyZip = WriteKeyZip("wrong.zip");

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => ChihiroRedumpConverter.Convert(cue, wrongKeyZip, output));

        Assert.Contains("FATX", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private string WriteFixture()
    {
        byte[] track = new byte[32 * RawSector];
        WriteIsoSector(track, 16, BuildPvd());
        WriteIsoSector(track, 17, BuildRootDirectory());
        WriteIsoSector(track, 18, BuildLoader());
        WriteIsoFile(track, 19, ByteSwap(Encrypt(ByteSwap(_plaintext))));
        string trackPath = Path.Combine(_root, "track03.bin");
        File.WriteAllBytes(trackPath, track);
        string cuePath = Path.Combine(_root, "game.cue");
        File.WriteAllText(
            cuePath,
            "FILE \"track03.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");
        return cuePath;
    }

    private string WriteKeyZip(string name = "key.zip")
    {
        byte[] data = BuildDataResponse();
        string path = Path.Combine(_root, name);
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        ZipArchiveEntry entry = zip.CreateEntry("test.data");
        using Stream stream = entry.Open();
        stream.Write(data);
        return path;
    }

    private byte[] BuildDataResponse()
    {
        byte[] data = new byte[80];
        Encoding.ASCII.GetBytes("L.BIN").CopyTo(data, 33);
        Array.Copy(_key, 0, data, 49, 7);
        data[41] = _key[7];
        return data;
    }

    private string WritePicKeyZip(string name = "pic.zip")
    {
        int[] offsets =
        [
            0x6E0, 0x6BC, 0x6AA, 0x7DE, 0x7BE,
            0x79E, 0x77E, 0x698, 0x686, 0x674,
        ];
        byte[] data = new byte[0x4000 + 16];
        byte[] response = BuildDataResponse();
        for (int i = 0; i < offsets.Length; i++)
        {
            for (int j = 0; j < 8; j++)
            {
                data[offsets[i] + (j * 2)] = response[(i * 8) + j];
            }
        }

        // The dump's 16 trailing bytes are not part of the response table.
        data[^1] = 0xFF;
        string path = Path.Combine(_root, name);
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        ZipArchiveEntry entry = zip.CreateEntry("317-0398-com.pic");
        using Stream stream = entry.Open();
        stream.Write(data);
        return path;
    }

    private static byte[] BuildPvd()
    {
        byte[] sector = new byte[IsoSector];
        sector[0] = 0x01;
        Encoding.ASCII.GetBytes("CD001").CopyTo(sector, 1);
        sector[6] = 0x01;
        WriteDirectoryRecord(sector, 156, RootLba, IsoSector, 0x02, [0]);
        return sector;
    }

    private static byte[] BuildRootDirectory()
    {
        byte[] sector = new byte[IsoSector];
        int offset = 0;
        offset += WriteDirectoryRecord(sector, offset, RootLba, IsoSector, 0x02, [0]);
        offset += WriteDirectoryRecord(sector, offset, RootLba, IsoSector, 0x02, [1]);
        offset += WriteDirectoryRecord(sector, offset, LoaderLba, LoaderSize, 0, "L.BIN"u8.ToArray());
        _ = WriteDirectoryRecord(
            sector,
            offset,
            FatxLba,
            FatxSize,
            0,
            "GAME.BIN"u8.ToArray());
        return sector;
    }

    private static byte[] BuildLoader()
    {
        byte[] loader = new byte[LoaderSize];
        Encoding.ASCII.GetBytes("GAME.BIN").CopyTo(loader, 0xC0);
        return loader;
    }

    private static void WriteIsoSector(byte[] track, int index, byte[] sector)
    {
        sector.CopyTo(track, (index * RawSector) + 16);
    }

    private static void WriteIsoFile(byte[] track, int firstIndex, byte[] data)
    {
        for (int offset = 0; offset < data.Length; offset += IsoSector)
        {
            byte[] sector = new byte[IsoSector];
            Array.Copy(data, offset, sector, 0, Math.Min(IsoSector, data.Length - offset));
            WriteIsoSector(track, firstIndex + (offset / IsoSector), sector);
        }
    }

    private static int WriteDirectoryRecord(
        byte[] sector,
        int offset,
        uint lba,
        uint size,
        byte flags,
        byte[] name)
    {
        int length = 33 + name.Length;
        if ((length & 1) != 0)
        {
            length++;
        }

        sector[offset] = (byte)length;
        BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(offset + 2), lba);
        BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(offset + 10), size);
        sector[offset + 25] = flags;
        sector[offset + 32] = (byte)name.Length;
        name.CopyTo(sector, offset + 33);
        return length;
    }

    private byte[] Encrypt(byte[] data)
    {
        byte[] key = _key[..];
        Array.Reverse(key);
#pragma warning disable CA5351 // The test mirrors the hardware DES container.
        using DES des = DES.Create();
#pragma warning restore CA5351
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key = key;
        using ICryptoTransform encryptor = des.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] ByteSwap(byte[] data)
    {
        byte[] result = data[..];
        for (int i = 0; i + 8 <= result.Length; i += 8)
        {
            (result[i], result[i + 7]) = (result[i + 7], result[i]);
            (result[i + 1], result[i + 6]) = (result[i + 6], result[i + 1]);
            (result[i + 2], result[i + 5]) = (result[i + 5], result[i + 2]);
            (result[i + 3], result[i + 4]) = (result[i + 4], result[i + 3]);
        }

        return result;
    }
}
