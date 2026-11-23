using System;
using System.IO;
using XGrid.Core.Snapshots;

namespace XGrid.Storage;

/// <summary>ATA-1 PIO taskfile state for a single drive (HDD or ATAPI DVD).</summary>
public sealed class AtaDevice : ISnapshotable
{
    /// <summary>Sector size in bytes.</summary>
    public const int SectorSize = 512;

    /// <summary>DVD sector size in bytes (ATAPI reads).</summary>
    public const int DvdSectorSize = 2048;

    /// <summary>Maximum DVD sectors served per READ command (256 KiB cap).</summary>
    private const int MaxDvdTransferSectors = 128;

    /// <summary>ATAPI packet command size in bytes.</summary>
    public const int PacketSize = 12;

    private const byte StatusReady = 0x50;
    private const byte StatusDataRequest = 0x58;
    private const byte StatusError = 0x51;
    private const byte StatusAtapiError = 0x41;
    private const byte ErrorAbort = 0x04;

    private readonly byte[] _identify = new byte[SectorSize];
    private readonly byte[] _packet = new byte[PacketSize];
    private IDiskImage? _disk;
    private IDvdImage? _dvd;
    private byte[]? _dmaLarge;
    private int _dataIndex;
    private int _transferLength = SectorSize;
    private int _packetIndex;
    private uint _remaining;
    private uint _lba;
    private Phase _phase;
    private bool _packetDma;

    /// <summary>Creates a detached drive with no media.</summary>
    /// <param name="atapi">When true, the drive behaves as an ATAPI DVD device (no media).</param>
    public AtaDevice(bool atapi = false)
    {
        IsAtapi = atapi;
        Status = StatusReady;
        DriveHead = 0xA0;
        if (IsAtapi)
        {
            BuildAtapiIdentify();
        }
    }

    private enum Phase
    {
        None,
        Identify,
        Read,
        Write,
        SecurityUnlock,
        Packet,
        PacketData,
        ReadDma,
        WriteDma,
        PacketDma,
        PacketDataDma,
    }

    /// <summary>Raised when the drive asserts its interrupt line.</summary>
    public event Action? Interrupt;

    /// <summary>Raised when the guest acknowledges the interrupt (status read or new command).</summary>
    public event Action? InterruptAcknowledged;

    /// <summary>Gets whether this drive behaves as an ATAPI packet device.</summary>
    public bool IsAtapi { get; }

    /// <summary>Gets whether a DMA transfer is pending bus-master start.</summary>
    public bool DmaPending => _phase is Phase.ReadDma or Phase.WriteDma or Phase.PacketDma or Phase.PacketDataDma;

    /// <summary>Gets whether DMA sector copies are pending (read or write path).</summary>
    public bool DmaSectorPending => _phase is Phase.ReadDma or Phase.WriteDma;

    /// <summary>Gets whether the pending DMA is a device-to-memory read.</summary>
    public bool DmaIsRead => _phase == Phase.ReadDma;

    /// <summary>Gets whether a DMA packet-data response is staged for the bus master to pull.</summary>
    public bool DmaResponseReady => _phase == Phase.PacketDataDma;

    /// <summary>Gets the staged DMA response bytes (valid while <see cref="DmaResponseReady"/>).</summary>
    public ReadOnlySpan<byte> DmaResponse => StagedData();

    private Span<byte> StagedData()
    {
        return (_dmaLarge ?? DmaBuffer).AsSpan(0, _transferLength);
    }

    /// <summary>Gets the drive staging buffer for bus-master sector copies.</summary>
    public byte[] DmaBuffer { get; } = new byte[SectorSize];

    /// <summary>Receives a 12-byte packet delivered by bus-master DMA (no IRQ; the engine completes).</summary>
    public void ReceiveDmaPacket(ReadOnlySpan<byte> packet)
    {
        packet[..PacketSize].CopyTo(_packet);
        _packetDma = true;
        DispatchPacket();
        if (_phase == Phase.PacketData)
        {
            _phase = Phase.PacketDataDma;
        }
    }

    /// <summary>Marks the staged DMA response as consumed; the packet command is complete.</summary>
    public void FinishDmaResponse()
    {
        _dataIndex = 0;
        _dmaLarge = null;
        _phase = Phase.None;
        Status = StatusReady;
        SectorCount = (byte)((SectorCount & ~7) | 0x03);
    }

    /// <summary>Loads the current DMA LBA sector into the staging buffer (read path).</summary>
    public void FetchDmaSector()
    {
        _disk!.ReadSector(_lba, DmaBuffer);
    }

    /// <summary>Stores the staging buffer to the current DMA LBA sector (write path).</summary>
    public void CommitDmaSector()
    {
        _disk?.WriteSector(_lba, DmaBuffer);
    }

    /// <summary>Advances the DMA transfer; completes the command when the last sector is done.</summary>
    public void AdvanceDma()
    {
        _dataIndex = 0;
        _remaining--;
        _lba++;
        if (_remaining == 0)
        {
            _phase = Phase.None;
            Status = StatusReady;
        }
    }

    /// <summary>Maximum ATAPI packet log entries retained (diagnostics).</summary>
    public const int PacketLogCapacity = 256;

    /// <summary>Gets the log of received ATAPI packet opcodes (diagnostics).</summary>
    public System.Collections.Generic.IReadOnlyList<string> PacketLog => _packetLog;

    /// <summary>Gets the bytes received for the in-progress PIO packet (diagnostics).</summary>
    public int PacketBytesReceived => _phase == Phase.Packet ? _packetIndex : -1;

    /// <summary>Gets the cumulative PIO packet bytes received (diagnostics; survives reset).</summary>
    public int TotalPacketBytes { get; private set; }

    private readonly System.Collections.Generic.List<string> _packetLog = [];

    /// <summary>Gets the error register.</summary>
    public byte Error { get; private set; }

    /// <summary>Gets the features register (last value written to offset 1).</summary>
    public byte Features { get; private set; }

    /// <summary>Gets the current ATAPI sense key (0 when clear).</summary>
    public byte SenseKey { get; private set; }

    /// <summary>Gets the current ATAPI additional sense code (0 when clear).</summary>
    public byte SenseAsc { get; private set; }

    /// <summary>Consumes a just-completed packet error for bus-master completion (diagnostics/sync).</summary>
    /// <returns>True when a packet error completed since the last command or reset.</returns>
    public bool ConsumePacketError()
    {
        if (!_packetError)
        {
            return false;
        }

        _packetError = false;
        return true;
    }

    private bool _packetError;

    /// <summary>Gets the sector count register.</summary>
    public byte SectorCount { get; private set; }

    /// <summary>Gets the LBA low register.</summary>
    public byte LbaLow { get; private set; }

    /// <summary>Gets the LBA mid register.</summary>
    public byte LbaMid { get; private set; }

    /// <summary>Gets the LBA high register.</summary>
    public byte LbaHigh { get; private set; }

    /// <summary>Gets the drive/head register.</summary>
    public byte DriveHead { get; private set; }

    /// <summary>Gets the status register.</summary>
    public byte Status { get; private set; }

    /// <summary>Gets the number of taskfile/control accesses seen (diagnostics).</summary>
    public int AccessCount { get; private set; }

    /// <summary>Gets the last command byte written (diagnostics).</summary>
    public byte LastCommand { get; private set; }

    /// <summary>Gets whether the ATA security set is locked (IDENTIFY word 128).</summary>
    public bool SecurityLocked { get; private set; } = true;

    /// <summary>Saves taskfile, phase, buffers, sense, and media validation (quiesce: no DMA in flight).</summary>
    /// <remarks>Media handles are host resources and are never serialized. The HDD overlay
    /// (guest writes) is cascaded when it is snapshotable; the DVD reader needs no state
    /// when no DMA is in flight. Load fails loudly when media presence or sector counts differ.</remarks>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(IsAtapi);
        writer.Write(_identify);
        writer.Write(_packet);
        writer.Write(DmaBuffer);
        writer.Write(_dmaLarge is not null);
        if (_dmaLarge is not null)
        {
            writer.Write(_dmaLarge.Length);
            writer.Write(_dmaLarge);
        }

        writer.Write(_dataIndex);
        writer.Write(_transferLength);
        writer.Write(_packetIndex);
        writer.Write(_remaining);
        writer.Write(_lba);
        writer.Write((int)_phase);
        writer.Write(_packetDma);
        writer.Write(_packetError);
        writer.Write(Error);
        writer.Write(Features);
        writer.Write(SenseKey);
        writer.Write(SenseAsc);
        writer.Write(SectorCount);
        writer.Write(LbaLow);
        writer.Write(LbaMid);
        writer.Write(LbaHigh);
        writer.Write(DriveHead);
        writer.Write(Status);
        writer.Write(AccessCount);
        writer.Write(LastCommand);
        writer.Write(SecurityLocked);
        writer.Write(TotalPacketBytes);
        writer.Write(_packetLog.Count);
        foreach (string entry in _packetLog)
        {
            writer.Write(entry);
        }

        writer.Write(_disk is not null);
        if (_disk is not null)
        {
            writer.Write(_disk.SectorCount);
        }

        writer.Write(_disk is ISnapshotable);
        if (_disk is ISnapshotable snapshotable)
        {
            snapshotable.SaveState(writer);
        }

        writer.Write(_dvd is not null);
        if (_dvd is not null)
        {
            writer.Write(_dvd.SectorCount);
        }
    }

    /// <summary>Restores taskfile, phase, buffers, sense, and media validation.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported ATA snapshot version {version}.");
        }

        if (reader.ReadBoolean() != IsAtapi)
        {
            throw new InvalidDataException("ATA ATAPI personality mismatch on snapshot load.");
        }

        CopyBytes(reader, _identify, SectorSize);
        CopyBytes(reader, _packet, PacketSize);
        CopyBytes(reader, DmaBuffer, SectorSize);
        if (reader.ReadBoolean())
        {
            int large = reader.ReadInt32();
            if (large < 0 || large > MaxDvdTransferSectors * DvdSectorSize)
            {
                throw new InvalidDataException("Bad ATA staged-transfer snapshot.");
            }

            _dmaLarge = reader.ReadBytes(large);
            if (_dmaLarge.Length != large)
            {
                throw new InvalidDataException("Truncated ATA staged-transfer snapshot.");
            }
        }
        else
        {
            _dmaLarge = null;
        }

        _dataIndex = reader.ReadInt32();
        _transferLength = reader.ReadInt32();
        _packetIndex = reader.ReadInt32();
        _remaining = reader.ReadUInt32();
        _lba = reader.ReadUInt32();
        _phase = (Phase)reader.ReadInt32();
        _packetDma = reader.ReadBoolean();
        _packetError = reader.ReadBoolean();
        Error = reader.ReadByte();
        Features = reader.ReadByte();
        SenseKey = reader.ReadByte();
        SenseAsc = reader.ReadByte();
        SectorCount = reader.ReadByte();
        LbaLow = reader.ReadByte();
        LbaMid = reader.ReadByte();
        LbaHigh = reader.ReadByte();
        DriveHead = reader.ReadByte();
        Status = reader.ReadByte();
        AccessCount = reader.ReadInt32();
        LastCommand = reader.ReadByte();
        SecurityLocked = reader.ReadBoolean();
        TotalPacketBytes = reader.ReadInt32();
        int packets = reader.ReadInt32();
        if (packets < 0 || packets > PacketLogCapacity)
        {
            throw new InvalidDataException("Bad ATA packet-log snapshot.");
        }

        _packetLog.Clear();
        for (int i = 0; i < packets; i++)
        {
            _packetLog.Add(reader.ReadString());
        }

        bool hadDisk = reader.ReadBoolean();
        uint diskSectors = 0;
        if (hadDisk)
        {
            diskSectors = reader.ReadUInt32();
        }

        if (hadDisk != (_disk is not null))
        {
            throw new InvalidDataException("ATA HDD media presence mismatch on snapshot load.");
        }

        if (hadDisk && _disk!.SectorCount != diskSectors)
        {
            throw new InvalidDataException(
                $"ATA HDD sector count mismatch on snapshot load (snapshot={diskSectors}, live={_disk.SectorCount}).");
        }

        if (reader.ReadBoolean())
        {
            if (_disk is not ISnapshotable snapshotable)
            {
                throw new InvalidDataException("ATA HDD overlay snapshot has no live overlay.");
            }

            snapshotable.LoadState(reader);
        }

        bool hadDvd = reader.ReadBoolean();
        uint dvdSectors = 0;
        if (hadDvd)
        {
            dvdSectors = reader.ReadUInt32();
        }

        if (hadDvd != (_dvd is not null))
        {
            throw new InvalidDataException("ATA DVD media presence mismatch on snapshot load.");
        }

        if (hadDvd && _dvd!.SectorCount != dvdSectors)
        {
            throw new InvalidDataException(
                $"ATA DVD sector count mismatch on snapshot load (snapshot={dvdSectors}, live={_dvd.SectorCount}).");
        }

        if (DmaPending)
        {
            throw new InvalidDataException("ATA snapshot loaded with DMA in flight; save only when idle.");
        }
    }

    private const int SnapshotVersion = 1;

    private static void CopyBytes(BinaryReader reader, byte[] target, int length)
    {
        byte[] data = reader.ReadBytes(length);
        if (data.Length != length)
        {
            throw new InvalidDataException("Truncated ATA snapshot.");
        }

        Array.Copy(data, target, length);
    }

    /// <summary>Attaches media and rebuilds the IDENTIFY page.</summary>
    public void Attach(IDiskImage disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        _disk = disk;
        BuildIdentify(disk);
        Reset();
    }

    /// <summary>Attaches DVD media to an ATAPI drive.</summary>
    public void AttachDvd(IDvdImage dvd)
    {
        ArgumentNullException.ThrowIfNull(dvd);
        if (!IsAtapi)
        {
            throw new InvalidOperationException("DVD media requires an ATAPI drive.");
        }

        _dvd = dvd;
        Reset();
    }

    /// <summary>Returns the drive to its power-on state.</summary>
    public void Reset()
    {
        _phase = Phase.None;
        _dataIndex = 0;
        _transferLength = SectorSize;
        _packetIndex = 0;
        _remaining = 0;
        _lba = 0;
        Error = 0;
        Features = 0;
        SenseKey = 0;
        SenseAsc = 0;
        if (IsAtapi && _dvd is null)
        {
            // A fresh empty drive reports NOT_READY / MEDIUM_NOT_PRESENT until
            // a command latches real sense (matches the no-media probe oracle).
            SenseKey = 0x02;
            SenseAsc = 0x3A;
        }

        Status = StatusReady;
        SectorCount = 0;
        LbaLow = 0;
        LbaMid = 0;
        LbaHigh = 0;
        DriveHead = 0xA0;
        _packetDma = false;
        _packetError = false;
        _dmaLarge = null;
        SetSignature();
    }

    /// <summary>Applies the ATA/ATAPI device signature to the taskfile (QEMU ide_set_signature).</summary>
    private void SetSignature()
    {
        SectorCount = 1;
        LbaLow = 1;
        if (IsAtapi)
        {
            LbaMid = 0x14;
            LbaHigh = 0xEB;
        }
        else if (_disk is not null)
        {
            LbaMid = 0;
            LbaHigh = 0;
        }
        else
        {
            LbaMid = 0xFF;
            LbaHigh = 0xFF;
        }
    }

    /// <summary>Reads a taskfile register (offsets 0-7, size in bits).</summary>
    public uint ReadTaskfile(int offset, int size)
    {
        AccessCount++;
        if (offset == 0)
        {
            // The data port honors the access width: NT drivers move IDENTIFY
            // and sector blocks with 32-bit REP INSD (128 dwords per 512-byte
            // block), so a dword read must advance four bytes, not one word.
            return size switch
            {
                8 => ReadData(),
                16 => (uint)(ReadData() | (ReadData() << 8)),
                _ => ReadData() | ((uint)ReadData() << 8) | ((uint)ReadData() << 16) | ((uint)ReadData() << 24),
            };
        }

        uint value = offset switch
        {
            // ReSharper disable once UnreachableSwitchArmDueToIntegerAnalysis
            0 => ReadData(),
            1 => Error,
            2 => SectorCount,
            3 => LbaLow,
            4 => LbaMid,
            5 => LbaHigh,
            6 => DriveHead,
            _ => ReadStatus(),
        };

        return size == 8 ? value & 0xFF : value & 0xFFFF;
    }

    /// <summary>Reads the status register; the guest read clears the pending interrupt.</summary>
    private uint ReadStatus()
    {
        InterruptAcknowledged?.Invoke();
        return Status;
    }

    /// <summary>Writes a taskfile register (offsets 0-7, size in bits).</summary>
    public void WriteTaskfile(int offset, int size, uint value)
    {
        AccessCount++;
        byte b = (byte)value;
        switch (offset)
        {
            case 0:
                WriteData(b);
                if (size >= 16)
                {
                    WriteData((byte)(value >> 8));
                }

                if (size >= 32)
                {
                    WriteData((byte)(value >> 16));
                    WriteData((byte)(value >> 24));
                }

                break;
            case 1:
                Features = b;
                Error = 0;
                break;
            case 2:
                SectorCount = b;
                break;
            case 3:
                LbaLow = b;
                break;
            case 4:
                LbaMid = b;
                break;
            case 5:
                LbaHigh = b;
                break;
            case 6:
                DriveHead = b;
                break;
            default:
                Execute(b);
                break;
        }
    }

    /// <summary>Reads the alternate status register.</summary>
    public uint ReadControl()
    {
        return Status;
    }

    /// <summary>Writes the device control register (bit 1 nIEN, bit 2 reset).</summary>
    public void WriteControl(uint value)
    {
        if ((value & 0x04) != 0)
        {
            Reset();
        }
    }

    private byte ReadData()
    {
        if (_phase is Phase.None or Phase.SecurityUnlock or Phase.Packet || _dataIndex >= _transferLength)
        {
            return 0;
        }

        byte value = StagedData()[_dataIndex];
        _dataIndex++;
        if (_dataIndex >= _transferLength)
        {
            OnTransferComplete();
        }

        return value;
    }

    private void WriteData(byte value)
    {
        if (_phase is Phase.Packet or Phase.PacketDma)
        {
            if (_packetIndex < PacketSize)
            {
                _packet[_packetIndex] = value;
                _packetIndex++;
                TotalPacketBytes++;
                if (_packetIndex >= PacketSize)
                {
                    CompletePacket();
                }
            }

            return;
        }

        if ((_phase != Phase.Write && _phase != Phase.SecurityUnlock) || _dataIndex >= SectorSize)
        {
            return;
        }

        DmaBuffer[_dataIndex] = value;
        _dataIndex++;
        if (_dataIndex >= SectorSize)
        {
            if (_phase == Phase.SecurityUnlock)
            {
                OnSecurityUnlockReceived();
            }
            else
            {
                OnSectorWritten();
            }
        }
    }

    private void OnTransferComplete()
    {
        _dataIndex = 0;
        if (_phase == Phase.PacketData)
        {
            // Command complete with CoD + IO reason bits (QEMU ide_atapi_cmd_ok).
            _phase = Phase.None;
            Status = StatusReady;
            SectorCount = (byte)((SectorCount & ~7) | 0x03);
            Interrupt?.Invoke();
            return;
        }

        if (_phase == Phase.Identify)
        {
            _phase = Phase.None;
            Status = StatusReady;
            Interrupt?.Invoke();
            return;
        }

        OnSectorRead();
    }

    private void OnSectorRead()
    {
        _dataIndex = 0;
        if (_phase == Phase.Identify)
        {
            _phase = Phase.None;
            Status = StatusReady;
            Interrupt?.Invoke();
            return;
        }

        _remaining--;
        _lba++;
        if (_remaining > 0 && _disk is not null)
        {
            _disk.ReadSector(_lba, DmaBuffer);
            Status = StatusDataRequest;
        }
        else
        {
            _phase = Phase.None;
            Status = StatusReady;
        }

        Interrupt?.Invoke();
    }

    private void OnSectorWritten()
    {
        _dataIndex = 0;
        _disk?.WriteSector(_lba, DmaBuffer);
        _remaining--;
        _lba++;
        if (_remaining > 0)
        {
            Status = StatusDataRequest;
        }
        else
        {
            _phase = Phase.None;
            Status = StatusReady;
        }

        Interrupt?.Invoke();
    }

    private void OnSecurityUnlockReceived()
    {
        _dataIndex = 0;
        _phase = Phase.None;
        SecurityLocked = false;
        WriteWord(128, 0x0003);
        Error = 0;
        Status = StatusReady;
        Interrupt?.Invoke();
    }

    private void Execute(byte command)
    {
        LastCommand = command;
        Error = 0;
        _packetError = false;
        InterruptAcknowledged?.Invoke();
        switch (command)
        {
            case 0xEC:
                if (IsAtapi)
                {
                    // IDENTIFY on a packet device aborts and leaves the ATAPI
                    // signature (QEMU cmd_identify with drive_kind == IDE_CD).
                    SetSignature();
                    Abort();
                }
                else
                {
                    _phase = Phase.Identify;
                    _dataIndex = 0;
                    _transferLength = SectorSize;
                    _identify.CopyTo(DmaBuffer, 0);
                    Status = StatusDataRequest;
                    Interrupt?.Invoke();
                }

                break;

            case 0xA1:
                if (IsAtapi)
                {
                    _phase = Phase.Identify;
                    _dataIndex = 0;
                    _transferLength = SectorSize;
                    _identify.CopyTo(DmaBuffer, 0);
                    Status = StatusDataRequest;
                    Interrupt?.Invoke();
                }
                else
                {
                    Abort();
                }

                break;

            case 0xA0:
                if (!IsAtapi)
                {
                    Abort();
                }
                else if ((Features & 0x02) != 0)
                {
                    // Overlapping packet commands are not supported.
                    Abort();
                }
                else if ((Features & 0x01) != 0)
                {
                    // DMA packet: this kernel delivers the 12-byte command block
                    // through the bus master (proven: zero PIO bytes, BM started),
                    // but still requires DRQ set after A0 or it will not start
                    // the bus master and falls back to PIO retries.
                    _packetDma = true;
                    _phase = Phase.PacketDma;
                    _packetIndex = 0;
                    SectorCount = 1;
                    Array.Clear(_packet);
                    Status = StatusDataRequest;
                }
                else
                {
                    // DRQ set: the host must now deliver the 12-byte packet
                    // (QEMU ide_transfer_start asserts DRQ for the packet phase).
                    _packetDma = false;
                    _phase = Phase.Packet;
                    _packetIndex = 0;
                    SectorCount = 1;
                    Array.Clear(_packet);
                    Status = StatusDataRequest;
                }

                break;

            case 0x08:
                // ATAPI device reset: signature + clear status (QEMU cmd_device_reset).
                _phase = Phase.None;
                _dataIndex = 0;
                _transferLength = SectorSize;
                Error = IsAtapi ? (byte)0x01 : (byte)0;
                SetSignature();
                Status = IsAtapi ? (byte)0x00 : StatusReady;
                break;

            case 0x20 or 0x24:
                StartRead();
                break;

            case 0xC8 or 0xC4:
                StartReadDma();
                break;

            case 0x30 or 0x34:
                StartWrite();
                break;

            case 0xCA or 0xCB:
                StartWriteDma();
                break;

            case 0xF2:
                _phase = Phase.SecurityUnlock;
                _dataIndex = 0;
                Array.Clear(DmaBuffer);
                Status = StatusDataRequest;
                break;

            case 0x91 or 0xEF or 0xE7 or 0x00:
                Status = StatusReady;
                break;

            default:
                Abort();
                break;
        }
    }

    private void Abort()
    {
        Error = ErrorAbort;
        Status = StatusError;
        Interrupt?.Invoke();
    }

    /// <summary>Completes a PIO-delivered packet (both PIO and DMA data phases).</summary>
    private void CompletePacket()
    {
        if (_phase == Phase.PacketDma)
        {
            _packetDma = true;
            DispatchPacket();
            if (_phase == Phase.PacketData)
            {
                _phase = Phase.PacketDataDma;
            }

            return;
        }

        DispatchPacket();
    }

    /// <summary>Dispatches a received 12-byte ATAPI packet (QEMU ide_atapi_cmd, empty-drive subset).</summary>
    private void DispatchPacket()
    {
        _packetIndex = 0;
        _dmaLarge = null;
        byte opcode = _packet[0];
        _packetLog.Add($"{opcode:X2}:{_packet[1]:X2}{_packet[2]:X2}{_packet[3]:X2}{_packet[4]:X2}{_packet[5]:X2}");
        if (_packetLog.Count > PacketLogCapacity)
        {
            _packetLog.RemoveAt(0);
        }

        if (opcode == 0x03)
        {
            // REQUEST SENSE returns the latched sense (QEMU cmd_request_sense);
            // a fresh empty drive reports NOT_READY / MEDIUM_NOT_PRESENT.
            int alloc = _packet[4] == 0 ? 18 : Math.Min(18, (int)_packet[4]);
            Array.Clear(DmaBuffer);
            DmaBuffer[0] = 0x70;
            DmaBuffer[2] = SenseKey;
            DmaBuffer[7] = 10;
            DmaBuffer[12] = SenseAsc;
            StagePacketData(alloc);
            SenseKey = 0;
            SenseAsc = 0;
            return;
        }

        if (opcode == 0x12)
        {
            // INQUIRY succeeds without media: 36-byte standard response.
            Array.Clear(DmaBuffer);
            DmaBuffer[0] = 0x05;
            DmaBuffer[1] = 0x80;
            DmaBuffer[2] = 0x02;
            DmaBuffer[3] = 0x02;
            DmaBuffer[4] = 31;
            WriteAscii(8, "XGRID   ");
            WriteAscii(16, "VIRTUAL DVD     ");
            WriteAscii(32, "1.0 ");
            StagePacketData(36);
            return;
        }

        // TEST UNIT READY succeeds only with media loaded.
        if (opcode == 0x00)
        {
            if (_dvd is null)
            {
                FailPacket(0x02, 0x3A);
            }
            else
            {
                CompletePacketOk();
            }

            return;
        }

        // Media-backed reads (LBA counts 2048-byte DVD sectors).
        if (_dvd is not null)
        {
            switch (opcode)
            {
                case 0x25:
                    StageDvdCapacity();
                    return;
                case 0x28:
                    StageDvdRead(ReadBe32(_packet, 2), ReadBe16(_packet, 7));
                    return;
                case 0xA8:
                    StageDvdRead(ReadBe32(_packet, 2), ReadBe32(_packet, 6));
                    return;
                default:
                    // 0x43/0xAD/0xBE and friends are unimplemented with media.
                    FailPacket(0x05, 0x20);
                    return;
            }
        }

        // Media-dependent commands on an empty drive: NOT_READY / MEDIUM_NOT_PRESENT.
        // (TEST UNIT READY is handled above.)
        if (opcode is 0x25 or 0x28 or 0x43 or 0xA8 or 0xAD or 0xBE)
        {
            FailPacket(0x02, 0x3A);
            return;
        }

        // MODE SENSE(10): page 0x3E (and all other unimplemented pages) is an
        // invalid field (xemu oracle: sense 5/0x24 for this exact packet).
        if (opcode == 0x5A)
        {
            FailPacket(0x05, 0x24);
            return;
        }

        // Unknown opcodes: ILLEGAL_REQUEST / ILLEGAL_OPCODE (QEMU ide_atapi_cmd).
        FailPacket(0x05, 0x20);
    }

    /// <summary>Completes a packet command with no data phase (e.g. TEST UNIT READY).</summary>
    private void CompletePacketOk()
    {
        Error = 0;
        Status = StatusReady;
        SectorCount = (byte)((SectorCount & ~7) | 0x03);
        _phase = Phase.None;
        Interrupt?.Invoke();
    }

    /// <summary>Stages a READ CAPACITY response: big-endian last LBA + 2048-byte blocks.</summary>
    private void StageDvdCapacity()
    {
        Array.Clear(DmaBuffer);
        WriteBe32(DmaBuffer, 0, _dvd!.SectorCount - 1);
        WriteBe32(DmaBuffer, 4, DvdSectorSize);
        StagePacketData(8);
    }

    /// <summary>Stages DVD sectors for a READ(10/12) command (single DRQ, PIO or DMA).</summary>
    private void StageDvdRead(uint lba, uint sectors)
    {
        if (sectors == 0)
        {
            CompletePacketOk();
            return;
        }

        if ((ulong)lba + sectors > _dvd!.SectorCount)
        {
            FailPacket(0x05, 0x21);
            return;
        }

        if (sectors > MaxDvdTransferSectors)
        {
            FailPacket(0x05, 0x24);
            return;
        }

        byte[] data = new byte[(int)sectors * DvdSectorSize];
        for (uint i = 0; i < sectors; i++)
        {
            _dvd.ReadSector(lba + i, data.AsSpan((int)i * DvdSectorSize, DvdSectorSize));
        }

        _dmaLarge = data;
        StagePacketData(data.Length);
    }

    private static uint ReadBe32(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16)
                                            | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];
    }

    private static ushort ReadBe16(byte[] buffer, int offset)
    {
        return (ushort)(((uint)buffer[offset] << 8) | buffer[offset + 1]);
    }

    private static void WriteBe32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>Fails a packet command with sense data (QEMU ide_atapi_cmd_error).</summary>
    private void FailPacket(byte senseKey, byte asc)
    {
        Error = (byte)(senseKey << 4);
        Status = StatusAtapiError;
        SenseKey = senseKey;
        SenseAsc = asc;
        SectorCount = (byte)((SectorCount & ~7) | 0x03);
        _phase = Phase.None;
        _packetError = true;
        Interrupt?.Invoke();
    }

    /// <summary>Stages a packet-data response for PIO delivery or bus-master pickup.</summary>
    /// <param name="length">Response length in bytes (already placed in the staging buffer).</param>
    /// <param name="raiseIrq">Whether the PIO path raises the completion interrupt.</param>
    private void StagePacketData(int length, bool raiseIrq = true)
    {
        _dataIndex = 0;
        _transferLength = length;
        LbaMid = (byte)(length & 0xFF);
        LbaHigh = (byte)(length >> 8);
        SectorCount = (byte)((SectorCount & ~7) | 0x02);
        if (_packetDma)
        {
            // The bus-master engine moves the bytes and completes with its own IRQ.
            _phase = Phase.PacketDataDma;
            Status = StatusReady;
            return;
        }

        _phase = Phase.PacketData;
        Status = StatusDataRequest;
        if (raiseIrq)
        {
            Interrupt?.Invoke();
        }
    }

    private void WriteAscii(int offset, string text)
    {
        for (int i = 0; i < text.Length && offset + i < DmaBuffer.Length; i++)
        {
            DmaBuffer[offset + i] = (byte)text[i];
        }
    }

    private void StartRead()
    {
        if (!TryPrepareTransfer())
        {
            return;
        }

        _disk!.ReadSector(_lba, DmaBuffer);
        _dataIndex = 0;
        _transferLength = SectorSize;
        _phase = Phase.Read;
        Status = StatusDataRequest;
        Interrupt?.Invoke();
    }

    /// <summary>Stages a READ DMA transfer; bytes move when the bus master starts.</summary>
    private void StartReadDma()
    {
        if (!TryPrepareTransfer())
        {
            return;
        }

        _dataIndex = 0;
        _phase = Phase.ReadDma;
        Status = StatusReady;
    }

    /// <summary>Stages a WRITE DMA transfer; bytes move when the bus master starts.</summary>
    private void StartWriteDma()
    {
        if (!TryPrepareTransfer())
        {
            return;
        }

        _dataIndex = 0;
        _phase = Phase.WriteDma;
        Status = StatusReady;
    }

    private void StartWrite()
    {
        if (!TryPrepareTransfer())
        {
            return;
        }

        _dataIndex = 0;
        _phase = Phase.Write;
        Status = StatusDataRequest;
        Interrupt?.Invoke();
    }

    private bool TryPrepareTransfer()
    {
        if (_disk is null)
        {
            Error = ErrorAbort;
            Status = StatusError;
            return false;
        }

        uint count = SectorCount == 0 ? 256u : SectorCount;
        uint lba = (uint)(LbaLow | (LbaMid << 8) | (LbaHigh << 16));
        if (lba + count > _disk.SectorCount)
        {
            Error = ErrorAbort;
            Status = StatusError;
            return false;
        }

        _lba = lba;
        _remaining = count;
        return true;
    }

    private void BuildIdentify(IDiskImage disk)
    {
        Array.Clear(_identify);
        SecurityLocked = true;
        WriteWord(0, 0x0040);
        WriteString(10, "XGRID0000000000000000");
        WriteString(23, "1.0     ");
        WriteString(27, "XGRID VIRTUAL HDD                       ");
        WriteWord(47, 0x8010);
        WriteWord(49, 0x0200);
        WriteWord(53, 0x0007);
        WriteWord(59, 0x0100);
        WriteWord(60, (ushort)(disk.SectorCount & 0xFFFF));
        WriteWord(61, (ushort)(disk.SectorCount >> 16));
        WriteWord(63, 0x0007);
        WriteWord(64, 0x0003);
        WriteWord(65, 0x0078);
        WriteWord(80, 0x001E);
        WriteWord(82, 0x4000);
        WriteWord(128, 0x0007);
        WriteWord(83, 0x4000);
        WriteWord(84, 0x4000);
        WriteWord(85, 0x4000);
        WriteWord(86, 0x4000);
        WriteWord(87, 0x4000);
        WriteWord(93, 0x4000);
    }

    /// <summary>Builds the ATAPI IDENTIFY PACKET page (QEMU ide_atapi_identify).</summary>
    private void BuildAtapiIdentify()
    {
        Array.Clear(_identify);
        WriteIdentifyWord(0, 0x85C0);
        WriteIdentifyString(10, "XGRID0000000000000000");
        WriteIdentifyWord(20, 3);
        WriteIdentifyWord(21, 512);
        WriteIdentifyWord(22, 4);
        WriteIdentifyString(23, "1.0     ");
        WriteIdentifyString(27, "XGRID VIRTUAL DVD                     ");
        WriteIdentifyWord(48, 1);
        WriteIdentifyWord(49, 1 << 9);
        WriteIdentifyWord(53, 3);
        WriteIdentifyWord(63, 0x0103);
        WriteIdentifyWord(64, 3);
        WriteIdentifyWord(65, 0x00B4);
        WriteIdentifyWord(66, 0x00B4);
        WriteIdentifyWord(67, 0x012C);
        WriteIdentifyWord(68, 0x00B4);
        WriteIdentifyWord(71, 30);
        WriteIdentifyWord(72, 30);
        WriteIdentifyWord(80, 0x001E);
    }

    private void WriteIdentifyWord(int index, ushort value)
    {
        _identify[index * 2] = (byte)value;
        _identify[(index * 2) + 1] = (byte)(value >> 8);
    }

    private void WriteIdentifyString(int index, string text)
    {
        for (int i = 0; i < text.Length; i += 2)
        {
            char high = text[i];
            char low = i + 1 < text.Length ? text[i + 1] : ' ';
            WriteIdentifyWord(index + (i / 2), (ushort)((high << 8) | low));
        }
    }

    private void WriteWord(int index, ushort value)
    {
        _identify[index * 2] = (byte)value;
        _identify[(index * 2) + 1] = (byte)(value >> 8);
    }

    private void WriteString(int index, string text)
    {
        for (int i = 0; i < text.Length; i += 2)
        {
            char high = text[i];
            char low = i + 1 < text.Length ? text[i + 1] : ' ';
            WriteWord(index + (i / 2), (ushort)((high << 8) | low));
        }
    }
}
