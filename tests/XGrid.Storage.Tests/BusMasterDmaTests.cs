using System;
using System.Collections.Generic;
using XGrid.Bus.Io;
using Xunit;

namespace XGrid.Storage.Tests;

public sealed class BusMasterDmaTests
{
    private const ushort Taskfile = 0x1F0;
    private const uint BmBase = 0xFF60;

    private sealed class ArrayDvd : IDvdImage
    {
        private readonly byte[] _data;

        public ArrayDvd(int sectors)
        {
            _data = new byte[sectors * AtaDevice.DvdSectorSize];
            for (int s = 0; s < sectors; s++)
            {
                for (int i = 0; i < AtaDevice.DvdSectorSize; i++)
                {
                    _data[s * AtaDevice.DvdSectorSize + i] = (byte)(s + 3);
                }
            }
        }

        public uint SectorCount => (uint)(_data.Length / AtaDevice.DvdSectorSize);

        public void Dispose()
        {
        }

        public void ReadSector(uint lba, Span<byte> destination)
        {
            _data.AsSpan((int)lba * AtaDevice.DvdSectorSize, AtaDevice.DvdSectorSize).CopyTo(destination);
        }
    }

    private sealed class ArrayDisk : IDiskImage
    {
        private readonly byte[] _data;

        public ArrayDisk(int sectors)
        {
            _data = new byte[sectors * AtaDevice.SectorSize];
            for (int i = 0; i < _data.Length; i++)
            {
                _data[i] = (byte)(i + 7);
            }
        }

        public uint SectorCount => (uint)(_data.Length / AtaDevice.SectorSize);

        public void Dispose()
        {
        }

        public void ReadSector(uint lba, Span<byte> destination)
        {
            _data.AsSpan((int)lba * AtaDevice.SectorSize, AtaDevice.SectorSize).CopyTo(destination);
        }

        public void WriteSector(uint lba, ReadOnlySpan<byte> source)
        {
            source.CopyTo(_data.AsSpan((int)lba * AtaDevice.SectorSize, AtaDevice.SectorSize));
        }
    }

    private sealed class SparseMemory : IGuestMemory
    {
        private readonly Dictionary<uint, byte> _cells = new();

        public byte ReadPhysical(uint address)
        {
            return _cells.GetValueOrDefault(address, (byte)0);
        }

        public void WritePhysical(uint address, byte value)
        {
            _cells[address] = value;
        }

        public void WritePrdt(uint table, uint buffer, uint count)
        {
            Write32(table, buffer);
            Write32(table + 4, count | 0x80000000u);
        }

        private void Write32(uint address, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                WritePhysical(address + (uint)i, (byte)(value >> (8 * i)));
            }
        }
    }

    private static (IdeController Ide, IoSpace Io, SparseMemory Ram) Channel()
    {
        IdeController ide = new();
        ide.AttachPrimaryMaster(new ArrayDisk(16));
        IoSpace io = new();
        ide.RegisterPorts(io);
        SparseMemory ram = new();
        ide.DmaMemory = ram;
        ide.WriteConfig(0x20, 4, BmBase | 1u);
        return (ide, io, ram);
    }

    [Fact]
    public void AtaReadDmaMovesSectorToMemory()
    {
        (IdeController ide, IoSpace io, SparseMemory ram) = Channel();

        io.Write8(Taskfile + 6, 0xA0);
        io.Write8(Taskfile + 2, 0x01);
        io.Write8(Taskfile + 3, 0x00);
        io.Write8(Taskfile + 4, 0x00);
        io.Write8(Taskfile + 5, 0x00);
        io.Write8(Taskfile + 7, 0xC8);

        Assert.Equal(0x50u, io.Read8(Taskfile + 7));

        ram.WritePrdt(0x1000, 0x2000, 512);
        io.Write32((ushort)(BmBase + 4), 0x1000);
        io.Write8((ushort)BmBase, 0x01);

        for (int i = 0; i < 512; i++)
        {
            Assert.Equal((byte)(i + 7), ram.ReadPhysical(0x2000u + (uint)i));
        }

        Assert.Equal(0x50u, io.Read8(Taskfile + 7));
        Assert.Equal(0x04u, ide.BusMasterStatus & 0x04u);
    }

    [Fact]
    public void AtapiDmaInquiryMovesDataToMemory()
    {
        (IdeController ide, IoSpace io, SparseMemory ram) = Channel();

        io.Write8(Taskfile + 6, 0xB0);
        io.Write8(Taskfile + 1, 0x01);
        io.Write8(Taskfile + 7, 0xA0);
        Assert.Equal(0x58u, io.Read8(Taskfile + 7));

        byte[] packet = new byte[12];
        packet[0] = 0x12;
        packet[4] = 36;
        foreach (byte b in packet)
        {
            io.Write8(Taskfile, b);
        }

        // Mixed delivery (PIO packet despite DMA flag, DMA data): ready, awaiting the bus master.
        Assert.True(ide.PrimarySlave.DmaResponseReady);
        Assert.Equal(0x50u, io.Read8(Taskfile + 7));

        ram.WritePrdt(0x1000, 0x3000, 36);
        io.Write32((ushort)(BmBase + 4), 0x1000);
        io.Write8((ushort)BmBase, 0x01);

        Assert.Equal(0x05u, ram.ReadPhysical(0x3000));
        Assert.Equal(0x80u, ram.ReadPhysical(0x3001));
        Assert.Equal(0x50u, io.Read8(Taskfile + 7));
        Assert.Equal(0x04u, ide.BusMasterStatus & 0x04u);
    }
    [Fact]
    public void AtapiDmaReadCapacityMovesDataToMemory()
    {
        (IdeController ide, IoSpace io, SparseMemory ram) = Channel();
        ide.PrimarySlave.AttachDvd(new ArrayDvd(16));

        io.Write8(Taskfile + 6, 0xB0);
        io.Write8(Taskfile + 1, 0x01);
        io.Write8(Taskfile + 7, 0xA0);
        byte[] packet = new byte[12];
        packet[0] = 0x25;
        foreach (byte b in packet)
        {
            io.Write8(Taskfile, b);
        }

        Assert.True(ide.PrimarySlave.DmaResponseReady);

        ram.WritePrdt(0x1000, 0x3000, 8);
        io.Write32((ushort)(BmBase + 4), 0x1000);
        io.Write8((ushort)BmBase, 0x01);

        // Big-endian last LBA (15), block length (2048).
        Assert.Equal(0x00u, ram.ReadPhysical(0x3000));
        Assert.Equal(0x0Fu, ram.ReadPhysical(0x3003));
        Assert.Equal(0x08u, ram.ReadPhysical(0x3006));
        Assert.Equal(0x50u, io.Read8(Taskfile + 7));
        Assert.Equal(0x04u, ide.BusMasterStatus & 0x04u);
    }

    [Fact]
    public void AtapiDmaRead10MovesSectorToMemory()
    {
        (IdeController ide, IoSpace io, SparseMemory ram) = Channel();
        ide.PrimarySlave.AttachDvd(new ArrayDvd(16));

        io.Write8(Taskfile + 6, 0xB0);
        io.Write8(Taskfile + 1, 0x01);
        io.Write8(Taskfile + 7, 0xA0);
        byte[] packet = new byte[12];
        packet[0] = 0x28;
        packet[5] = 0x04;
        packet[8] = 0x01;
        foreach (byte b in packet)
        {
            io.Write8(Taskfile, b);
        }

        Assert.True(ide.PrimarySlave.DmaResponseReady);

        ram.WritePrdt(0x1000, 0x4000, 2048);
        io.Write32((ushort)(BmBase + 4), 0x1000);
        io.Write8((ushort)BmBase, 0x01);

        // Sector 4 is filled with 0x07.
        Assert.Equal(0x07u, ram.ReadPhysical(0x4000));
        Assert.Equal(0x07u, ram.ReadPhysical(0x47FF));
        Assert.Equal(0x50u, io.Read8(Taskfile + 7));
        Assert.Equal(0x04u, ide.BusMasterStatus & 0x04u);
    }

    [Fact]
    public void AtapiDmaRead10FromIsoFileMovesSectorToMemory()
    {
        byte[] file = new byte[4 * AtaDevice.DvdSectorSize];
        for (int i = 0; i < file.Length; i++)
        {
            file[i] = (byte)(0xA0 + (i & 0x0F));
        }

        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "xgrid-dvddma-" + System.Guid.NewGuid().ToString("N") + ".iso");
        try
        {
            System.IO.File.WriteAllBytes(path, file);
            (IdeController ide, IoSpace io, SparseMemory ram) = Channel();
            using var dvd = new IsoDvdImage(path);
            ide.PrimarySlave.AttachDvd(dvd);

            io.Write8(Taskfile + 6, 0xB0);
            io.Write8(Taskfile + 1, 0x01);
            io.Write8(Taskfile + 7, 0xA0);
            byte[] packet = new byte[12];
            packet[0] = 0x28;
            packet[5] = 0x02;
            packet[8] = 0x01;
            foreach (byte b in packet)
            {
                io.Write8(Taskfile, b);
            }

            Assert.True(ide.PrimarySlave.DmaResponseReady);

            ram.WritePrdt(0x1000, 0x5000, 2048);
            io.Write32((ushort)(BmBase + 4), 0x1000);
            io.Write8((ushort)BmBase, 0x01);

            Assert.Equal(0xA0u, ram.ReadPhysical(0x5000));
            Assert.Equal(0xAFu, ram.ReadPhysical(0x500F));
            Assert.Equal(0x50u, io.Read8(Taskfile + 7));
            Assert.Equal(0x04u, ide.BusMasterStatus & 0x04u);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void BusMasterInterruptClearsOnWrite()
    {
        (IdeController ide, IoSpace io, SparseMemory ram) = Channel();

        io.Write8(Taskfile + 6, 0xA0);
        io.Write8(Taskfile + 2, 0x01);
        io.Write8(Taskfile + 7, 0xC8);
        ram.WritePrdt(0x1000, 0x2000, 512);
        io.Write32((ushort)(BmBase + 4), 0x1000);
        io.Write8((ushort)BmBase, 0x01);
        Assert.Equal(0x04u, ide.BusMasterStatus & 0x04u);

        io.Write8((ushort)(BmBase + 2), 0x04);
        Assert.Equal(0u, ide.BusMasterStatus & 0x04u);
    }

    [Fact]
    public void AtapiDmaPacketArrivesViaPrdtStream()
    {
        (IdeController ide, IoSpace io, SparseMemory ram) = Channel();

        // DMA packet command: no PIO bytes follow; the engine pulls the
        // 12-byte packet and the 36-byte INQUIRY reply through one stream.
        io.Write8(Taskfile + 6, 0xB0);
        io.Write8(Taskfile + 1, 0x01);
        io.Write8(Taskfile + 7, 0xA0);
        Assert.Equal(0x58u, io.Read8(Taskfile + 7));

        byte[] packet = new byte[12];
        packet[0] = 0x12;
        packet[4] = 36;
        for (int i = 0; i < packet.Length; i++)
        {
            ram.WritePhysical(0x4000u + (uint)i, packet[i]);
        }

        ram.WritePrdt(0x1000, 0x4000, 48);
        io.Write32((ushort)(BmBase + 4), 0x1000);
        io.Write8((ushort)BmBase, 0x01);

        Assert.Equal(0x05u, ram.ReadPhysical(0x4000 + 12));
        Assert.Equal(0x80u, ram.ReadPhysical(0x4000 + 13));
        Assert.Equal(0x50u, io.Read8(Taskfile + 7));
        Assert.Equal(0x04u, ide.BusMasterStatus & 0x04u);
    }
}
