using System;

namespace XGrid.Storage;

/// <summary>ATA-1 PIO taskfile state for a single drive.</summary>
public sealed class AtaDevice
{
    /// <summary>Sector size in bytes.</summary>
    public const int SectorSize = 512;

    private const byte StatusBusy = 0x80;
    private const byte StatusReady = 0x50;
    private const byte StatusDataRequest = 0x58;
    private const byte StatusError = 0x51;
    private const byte ErrorAbort = 0x04;

    private readonly byte[] _identify = new byte[SectorSize];
    private readonly byte[] _data = new byte[SectorSize];
    private IDiskImage? _disk;
    private int _dataIndex;
    private uint _remaining;
    private uint _lba;
    private Phase _phase;

    /// <summary>Creates a detached drive with no media.</summary>
    public AtaDevice()
    {
        Status = StatusReady;
        DriveHead = 0xA0;
    }

    private enum Phase
    {
        None,
        Identify,
        Read,
        Write,
        SecurityUnlock,
    }

    /// <summary>Raised when the drive asserts its interrupt line.</summary>
    public event Action? Interrupt;

    /// <summary>Gets the error register.</summary>
    public byte Error { get; private set; }

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

    /// <summary>Gets whether media is attached.</summary>
    public bool HasMedia => _disk is not null;

    /// <summary>Gets the number of taskfile/control accesses seen (diagnostics).</summary>
    public int AccessCount { get; private set; }

    /// <summary>Gets the last command byte written (diagnostics).</summary>
    public byte LastCommand { get; private set; }

    /// <summary>Gets whether the ATA security set is locked (IDENTIFY word 128).</summary>
    public bool SecurityLocked { get; private set; } = true;

    /// <summary>Attaches media and rebuilds the IDENTIFY page.</summary>
    public void Attach(IDiskImage disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        _disk = disk;
        BuildIdentify(disk);
        Reset();
    }

    /// <summary>Returns the drive to its power-on state.</summary>
    public void Reset()
    {
        _phase = Phase.None;
        _dataIndex = 0;
        _remaining = 0;
        _lba = 0;
        Error = 0;
        Status = StatusReady;
        SectorCount = 0;
        LbaLow = 0;
        LbaMid = 0;
        LbaHigh = 0;
        DriveHead = 0xA0;
    }

    /// <summary>Reads a taskfile register (offsets 0-7, size in bits).</summary>
    public uint ReadTaskfile(int offset, int size)
    {
        AccessCount++;
        if (offset == 0 && size != 8)
        {
            uint low = ReadData();
            uint high = ReadData();
            return (high << 8) | low;
        }

        uint value = offset switch
        {
            0 => ReadData(),
            1 => Error,
            2 => SectorCount,
            3 => LbaLow,
            4 => LbaMid,
            5 => LbaHigh,
            6 => DriveHead,
            _ => Status,
        };

        return size == 8 ? value & 0xFF : value & 0xFFFF;
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
                if (size != 8)
                {
                    WriteData((byte)(value >> 8));
                }

                break;
            case 1:
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
        if (_phase is Phase.None or Phase.SecurityUnlock)
        {
            return 0;
        }

        byte value = _data[_dataIndex];
        _dataIndex++;
        if (_dataIndex == SectorSize)
        {
            OnSectorRead();
        }

        return value;
    }

    private void WriteData(byte value)
    {
        if (_phase != Phase.Write && _phase != Phase.SecurityUnlock)
        {
            return;
        }

        _data[_dataIndex] = value;
        _dataIndex++;
        if (_dataIndex == SectorSize)
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
            _disk.ReadSector(_lba, _data);
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
        _disk?.WriteSector(_lba, _data);
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
        switch (command)
        {
            case 0xEC:
                _phase = Phase.Identify;
                _dataIndex = 0;
                _identify.CopyTo(_data, 0);
                Status = StatusDataRequest;
                Interrupt?.Invoke();
                break;

            case 0x20 or 0x24:
                StartRead();
                break;

            case 0x30 or 0x34:
                StartWrite();
                break;

            case 0xF2:
                _phase = Phase.SecurityUnlock;
                _dataIndex = 0;
                Array.Clear(_data);
                Status = StatusDataRequest;
                break;

            case 0x91 or 0xEF or 0xE7 or 0x00:
                Status = StatusReady;
                break;

            default:
                Error = ErrorAbort;
                Status = StatusError;
                break;
        }
    }

    private void StartRead()
    {
        if (!TryPrepareTransfer())
        {
            return;
        }

        _disk!.ReadSector(_lba, _data);
        _dataIndex = 0;
        _phase = Phase.Read;
        Status = StatusDataRequest;
        Interrupt?.Invoke();
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
