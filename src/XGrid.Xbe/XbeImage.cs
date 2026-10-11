using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace XGrid.Xbe;

/// <summary>
/// Xbox Executable (XBE) image parser for inspection tooling.
/// Field facts (from Cxbx kernel/XBE documentation): all structs `#pragma pack(1)`;
/// file offset of a virtual address is VA minus the image base; magic `XBEH`;
/// image base is usually `0x00010000`; entry point and kernel thunk table are
/// XOR-obfuscated per image type (retail/debug/Chihiro); sections carry raw bytes
/// (no section encryption). Signatures are not verified (unsigned XBEs run).
/// </summary>
public sealed class XbeImage
{
    private const uint Magic = 0x48454258u;
    private const uint RetailEntryXor = 0xA8FC57ABu;
    private const uint DebugEntryXor = 0x94859D4Bu;
    private const uint ChihiroEntryXor = 0x40B5C16Eu;
    private const uint RetailThunkXor = 0x5B6D40B6u;
    private const uint DebugThunkXor = 0xEFB1F152u;
    private const uint ChihiroThunkXor = 0x2290059Du;

    private XbeImage()
    {
    }

    /// <summary>Retail, debug, or Chihiro image, detected per Cxbx rules.</summary>
    public enum ImageType
    {
        /// <summary>Retail signed image.</summary>
        Retail,

        /// <summary>Debug image (kernel thunk address in kernel space).</summary>
        Debug,

        /// <summary>Chihiro arcade image.</summary>
        Chihiro,
    }

    /// <summary>A parsed section header with its raw bytes.</summary>
    public readonly struct Section
    {
        /// <summary>Creates a section record.</summary>
        public Section(
            string name, uint flags, uint virtualAddress, uint virtualSize,
            uint rawSize, byte[] rawData)
        {
            Name = name;
            Flags = flags;
            VirtualAddress = virtualAddress;
            VirtualSize = virtualSize;
            RawSize = rawSize;
            RawData = rawData;
        }

        /// <summary>Gets the section name.</summary>
        public string Name { get; }

        /// <summary>Gets the flag bits (1 = writable, 2 = preload, 4 = executable).</summary>
        public uint Flags { get; }

        /// <summary>Gets the virtual address.</summary>
        public uint VirtualAddress { get; }

        /// <summary>Gets the virtual (memory) size.</summary>
        public uint VirtualSize { get; }

        /// <summary>Gets the raw (file) size.</summary>
        public uint RawSize { get; }

        /// <summary>Gets the raw section bytes.</summary>
        public byte[] RawData { get; }
    }

    /// <summary>A parsed library version entry.</summary>
    public readonly struct LibraryVersion
    {
        /// <summary>Creates a library version record.</summary>
        public LibraryVersion(string name, ushort major, ushort minor, ushort build, ushort flags)
        {
            Name = name;
            Major = major;
            Minor = minor;
            Build = build;
            Flags = flags;
        }

        /// <summary>Gets the library name.</summary>
        public string Name { get; }

        /// <summary>Gets the major version.</summary>
        public ushort Major { get; }

        /// <summary>Gets the minor version.</summary>
        public ushort Minor { get; }

        /// <summary>Gets the build version.</summary>
        public ushort Build { get; }

        /// <summary>Gets the flag bits.</summary>
        public ushort Flags { get; }
    }

    /// <summary>Thread-local storage directory (24 bytes).</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct TlsDirectory
    {
        /// <summary>Creates a TLS directory record.</summary>
        public TlsDirectory(
            uint dataStart, uint dataEnd, uint indexAddress, uint callbackAddress,
            uint zeroFill, uint characteristics)
        {
            DataStart = dataStart;
            DataEnd = dataEnd;
            IndexAddress = indexAddress;
            CallbackAddress = callbackAddress;
            ZeroFill = zeroFill;
            Characteristics = characteristics;
        }

        /// <summary>Gets the TLS data start address.</summary>
        public uint DataStart { get; }

        /// <summary>Gets the TLS data end address.</summary>
        public uint DataEnd { get; }

        /// <summary>Gets the TLS index address.</summary>
        public uint IndexAddress { get; }

        /// <summary>Gets the TLS callback address.</summary>
        public uint CallbackAddress { get; }

        /// <summary>Gets the zero-fill size.</summary>
        public uint ZeroFill { get; }

        /// <summary>Gets the characteristics.</summary>
        public uint Characteristics { get; }
    }

    /// <summary>Gets the image base address (usually 0x00010000).</summary>
    public uint BaseAddress { get; private set; }

    /// <summary>Gets the detected image type.</summary>
    public ImageType Type { get; private set; }

    /// <summary>Gets the stored (possibly XOR-encoded) entry point.</summary>
    public uint EntryPointStored { get; private set; }

    /// <summary>Gets the decoded entry point virtual address.</summary>
    public uint EntryPoint { get; private set; }

    /// <summary>Gets the stored kernel thunk table address.</summary>
    public uint KernelThunkStored { get; private set; }

    /// <summary>Gets the decoded kernel thunk table address.</summary>
    public uint KernelThunk { get; private set; }

    /// <summary>Gets the init flags.</summary>
    public uint InitFlags { get; private set; }

    /// <summary>Gets the certificate title ID.</summary>
    public uint TitleId { get; private set; }

    /// <summary>Gets the certificate title name.</summary>
    public string TitleName { get; private set; } = string.Empty;

    /// <summary>Gets the allowed media mask.</summary>
    public uint AllowedMedia { get; private set; }

    /// <summary>Gets the game region mask.</summary>
    public uint GameRegion { get; private set; }

    /// <summary>Gets the disc version.</summary>
    public uint Version { get; private set; }

    /// <summary>Gets the section headers.</summary>
    public IReadOnlyList<Section> Sections { get; private set; } = Array.Empty<Section>();

    /// <summary>Gets the library versions.</summary>
    public IReadOnlyList<LibraryVersion> Libraries { get; private set; } = Array.Empty<LibraryVersion>();

    /// <summary>Gets the TLS directory (null when absent).</summary>
    public TlsDirectory? Tls { get; private set; }

    /// <summary>Loads and validates an XBE image from memory.</summary>
    public static XbeImage Load(ReadOnlySpan<byte> image)
    {
        if (image.Length < 0x178 || ReadU32(image, 0) != Magic)
        {
            throw new InvalidDataException("Not an XBE image.");
        }

        var parsed = new XbeImage();
        parsed.BaseAddress = ReadU32(image, 0x104);
        uint headersSize = ReadU32(image, 0x108);
        if (headersSize > image.Length)
        {
            throw new InvalidDataException("XBE headers extend past the file.");
        }

        parsed.EntryPointStored = ReadU32(image, 0x128);
        parsed.KernelThunkStored = ReadU32(image, 0x158);
        parsed.InitFlags = ReadU32(image, 0x124);
        parsed.Type = DetectType(parsed.EntryPointStored, parsed.KernelThunkStored);
        parsed.EntryPoint = parsed.EntryPointStored ^ EntryXor(parsed.Type);
        parsed.KernelThunk = parsed.KernelThunkStored ^ ThunkXor(parsed.Type);
        parsed.ReadCertificate(image);
        parsed.ReadSections(image);
        parsed.ReadLibraries(image);
        parsed.ReadTls(image);
        return parsed;
    }

    private static ImageType DetectType(uint entry, uint thunk)
    {
        if ((entry & 0xF0000000u) == 0x40000000u)
        {
            return ImageType.Chihiro;
        }

        return thunk >= 0x80000000u ? ImageType.Debug : ImageType.Retail;
    }

    private static uint EntryXor(ImageType type)
    {
        return type switch
        {
            ImageType.Chihiro => ChihiroEntryXor,
            ImageType.Debug => DebugEntryXor,
            _ => RetailEntryXor,
        };
    }

    private static uint ThunkXor(ImageType type)
    {
        return type switch
        {
            ImageType.Chihiro => ChihiroThunkXor,
            ImageType.Debug => DebugThunkXor,
            _ => RetailThunkXor,
        };
    }

    private void ReadCertificate(ReadOnlySpan<byte> image)
    {
        uint certAddr = ReadU32(image, 0x118);
        uint certOffset = ToFileOffset(certAddr, image.Length, "certificate");
        if (certOffset + 0xB0 > (uint)image.Length)
        {
            throw new InvalidDataException("XBE certificate extends past the file.");
        }

        TitleId = ReadU32(image, (int)certOffset + 0x08);
        TitleName = Encoding.Unicode.GetString(image.Slice((int)certOffset + 0x0C, 80)).TrimEnd('\0');
        AllowedMedia = ReadU32(image, (int)certOffset + 0x9C);
        GameRegion = ReadU32(image, (int)certOffset + 0xA0);
        Version = ReadU32(image, (int)certOffset + 0xAC);
    }

    private void ReadSections(ReadOnlySpan<byte> image)
    {
        uint count = ReadU32(image, 0x11C);
        if (count == 0 || count > 256)
        {
            throw new InvalidDataException($"XBE section count {count} is out of range.");
        }

        uint tableAddr = ReadU32(image, 0x120);
        uint offset = ToFileOffset(tableAddr, image.Length, "section table");
        var sections = new List<Section>();
        for (uint i = 0; i < count; i++)
        {
            if (offset + 0x38 > (uint)image.Length)
            {
                throw new InvalidDataException("XBE section table extends past the file.");
            }

            uint flags = ReadU32(image, (int)offset);
            uint va = ReadU32(image, (int)offset + 0x04);
            uint vsize = ReadU32(image, (int)offset + 0x08);
            uint rawAddr = ReadU32(image, (int)offset + 0x0C);
            uint rawSize = ReadU32(image, (int)offset + 0x10);
            uint nameAddr = ReadU32(image, (int)offset + 0x14);
            if ((ulong)rawAddr + rawSize > (ulong)image.Length)
            {
                throw new InvalidDataException($"Section at 0x{va:X8} extends past the file.");
            }

            string name = ReadCString(image, nameAddr);
            byte[] raw = image.Slice((int)rawAddr, (int)rawSize).ToArray();
            sections.Add(new Section(name, flags, va, vsize, rawSize, raw));
            offset += 0x38;
        }

        Sections = sections;
    }

    private void ReadLibraries(ReadOnlySpan<byte> image)
    {
        uint count = ReadU32(image, 0x160);
        uint tableAddr = ReadU32(image, 0x164);
        if (count == 0 || tableAddr == 0)
        {
            return;
        }

        uint offset = ToFileOffset(tableAddr, image.Length, "library table");
        var libraries = new List<LibraryVersion>();
        for (uint i = 0; i < count && i < 64; i++)
        {
            if (offset + 16 > (uint)image.Length)
            {
                throw new InvalidDataException("XBE library table extends past the file.");
            }

            string name = Encoding.ASCII.GetString(image.Slice((int)offset, 8)).TrimEnd('\0');
            libraries.Add(new LibraryVersion(
                name,
                (ushort)ReadU16(image, (int)offset + 8),
                (ushort)ReadU16(image, (int)offset + 10),
                (ushort)ReadU16(image, (int)offset + 12),
                (ushort)ReadU16(image, (int)offset + 14)));
            offset += 16;
        }

        Libraries = libraries;
    }

    private void ReadTls(ReadOnlySpan<byte> image)
    {
        uint tlsAddr = ReadU32(image, 0x12C);
        if (tlsAddr == 0)
        {
            return;
        }

        uint offset = ToFileOffset(tlsAddr, image.Length, "TLS directory");
        if (offset + 24 > (uint)image.Length)
        {
            throw new InvalidDataException("XBE TLS directory extends past the file.");
        }

        Tls = new TlsDirectory(
            ReadU32(image, (int)offset),
            ReadU32(image, (int)offset + 4),
            ReadU32(image, (int)offset + 8),
            ReadU32(image, (int)offset + 12),
            ReadU32(image, (int)offset + 16),
            ReadU32(image, (int)offset + 20));
    }

    private uint ToFileOffset(uint virtualAddress, int length, string what)
    {
        if (virtualAddress < BaseAddress)
        {
            throw new InvalidDataException($"XBE {what} address 0x{virtualAddress:X8} is below the image base.");
        }

        uint offset = virtualAddress - BaseAddress;
        if (offset >= (uint)length)
        {
            throw new InvalidDataException($"XBE {what} address 0x{virtualAddress:X8} is past the file.");
        }

        return offset;
    }

    private string ReadCString(ReadOnlySpan<byte> image, uint virtualAddress)
    {
        if (virtualAddress == 0)
        {
            return string.Empty;
        }

        uint offset = ToFileOffset(virtualAddress, image.Length, "section name");
        int end = (int)offset;
        while (end < image.Length && end < offset + 64 && image[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(image.Slice((int)offset, end - (int)offset));
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
