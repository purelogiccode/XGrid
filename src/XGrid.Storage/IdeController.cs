using System;
using XGrid.Bus.Io;
using XGrid.Bus.Pci;

namespace XGrid.Storage;

/// <summary>Xbox IDE controller (nForce 10DE:01BC at PCI 0:9.0, IRQ 14) with one primary channel.</summary>
public sealed class IdeController : PciDevice
{
    private const int BusMasterBar = 4;
    private const uint BmStatusActive = 0x01;
    private const uint BmStatusError = 0x02;
    private const uint BmStatusInterrupt = 0x04;
    private const uint BmCommandStart = 0x01;

    /// <summary>Maximum bytes moved per bus-master start (runaway PRDT guard).</summary>
    private const uint MaxDmaBytes = 4u * 1024 * 1024;

    private readonly System.Collections.Generic.List<string> _commandLog = new();
    private readonly System.Collections.Generic.List<string> _accessLog = new();
    private readonly BusMasterChannel _busMaster = new();
    private IoSpace? _io;
    private IDisposable? _busMasterRegistration;
    private int _selected;

    /// <summary>Creates the controller and defines its PCI header.</summary>
    public IdeController()
        : base("nForce IDE", 9, 0, 0x10DE, 0x01BC, 0xB1, 0x01, 0x01, 0x80)
    {
        DefineBar(0, 8, isIo: true);
        DefineBar(1, 4, isIo: true);
        DefineBar(2, 8, isIo: true);
        DefineBar(3, 4, isIo: true);
        DefineBar(4, 16, isIo: true);
        PrimaryMaster.Interrupt += RaiseInterrupt;
        PrimarySlave.Interrupt += RaiseInterrupt;
        PrimaryMaster.InterruptAcknowledged += LowerInterrupt;
        PrimarySlave.InterruptAcknowledged += LowerInterrupt;
    }

    /// <summary>Gets the primary master drive (HDD).</summary>
    public AtaDevice PrimaryMaster { get; } = new();

    /// <summary>Gets the primary slave drive (ATAPI DVD, empty).</summary>
    public AtaDevice PrimarySlave { get; } = new(atapi: true);

    /// <summary>Gets the recent command history (M/S + command byte, diagnostics).</summary>
    public System.Collections.Generic.IReadOnlyList<string> CommandLog => _commandLog;

    /// <summary>Gets the recent IDE register access history (diagnostics).</summary>
    public System.Collections.Generic.IReadOnlyList<string> AccessLog => _accessLog;

    private void LogAccess(string entry)
    {
        _accessLog.Add(entry);
        if (_accessLog.Count > 64)
        {
            _accessLog.RemoveAt(0);
        }
    }

    /// <summary>Gets the total taskfile/control accesses seen on both drives (diagnostics).</summary>
    public int AccessCount => PrimaryMaster.AccessCount + PrimarySlave.AccessCount;

    /// <summary>Gets the last command byte issued to the selected drive (diagnostics).</summary>
    public byte LastCommand => SelectedDrive.LastCommand;

    /// <summary>Gets the bus-master status register (diagnostics).</summary>
    public uint BusMasterStatus => _busMaster.Status;

    /// <summary>Gets the bus-master PRDT address (diagnostics).</summary>
    public uint BusMasterPrdt => _busMaster.PrdtAddress;

    /// <summary>Gets the cumulative bus-master START count (diagnostics).</summary>
    public int BusMasterStarts => _busMaster.Starts;

    /// <summary>Gets or sets the guest-physical memory used for bus-master DMA.</summary>
    public IGuestMemory? DmaMemory { get; set; }

    private AtaDevice SelectedDrive => _selected == 0 ? PrimaryMaster : PrimarySlave;

    private void LogCommand(byte command)
    {
        AtaDevice drive = SelectedDrive;
        string detail = command is 0x20 or 0x24 or 0xC8 or 0xC4 or 0x30 or 0x34 or 0xCA or 0xCB
            ? $"/n{drive.SectorCount:X2}l{drive.LbaLow:X2}{drive.LbaMid:X2}{drive.LbaHigh:X2}"
            : string.Empty;
        _commandLog.Add($"{(_selected == 0 ? 'M' : 'S')}{command:X2}/F{drive.Features:X2}{detail}");
        if (_commandLog.Count > 32)
        {
            _commandLog.RemoveAt(0);
        }
    }

    /// <summary>Attaches media to the primary master drive.</summary>
    public void AttachPrimaryMaster(IDiskImage disk)
    {
        PrimaryMaster.Attach(disk);
    }

    /// <summary>Registers the legacy compatibility ports (0x1F0 and 0x3F6).</summary>
    public void RegisterPorts(IoSpace io)
    {
        ArgumentNullException.ThrowIfNull(io);
        _io = io;
        io.Register(0x1F0, 8, new TaskfileWindow(this));
        io.Register(0x3F6, 2, new ControlWindow(this));
    }

    /// <inheritdoc />
    public override void Reset()
    {
        base.Reset();
        _selected = 0;
        PrimaryMaster.Reset();
        PrimarySlave.Reset();
        _busMaster.Reset();
    }

    /// <inheritdoc />
    protected override void OnBarWritten(int index, uint baseAddress)
    {
        if (index != BusMasterBar || _io is null)
        {
            return;
        }

        _busMasterRegistration?.Dispose();
        _busMasterRegistration = null;
        if (baseAddress != 0)
        {
            _busMasterRegistration = _io.Register((ushort)(baseAddress & 0xFFFEu), 16, new BusMasterWindow(this));
        }
    }

    private void SelectDrive(uint driveHead)
    {
        _selected = ((driveHead >> 4) & 1) == 0 ? 0 : 1;
        PrimaryMaster.WriteTaskfile(6, 8, driveHead);
        PrimarySlave.WriteTaskfile(6, 8, driveHead);
    }

    /// <summary>Runs a pending DMA transfer when the bus master is started (synchronous v1).</summary>
    private void TryRunDma()
    {
        if ((_busMaster.Command & BmCommandStart) == 0 || DmaMemory is null)
        {
            return;
        }

        AtaDevice? drive = PrimaryMaster.DmaPending ? PrimaryMaster
            : PrimarySlave.DmaPending ? PrimarySlave
            : null;
        if (drive is null)
        {
            // No DMA work staged: if a packet just errored, the driver still
            // started the bus master and waits for its interrupt. Complete it
            // (the drive already reported the packet error with its own IRQ).
            if (PrimaryMaster.ConsumePacketError() || PrimarySlave.ConsumePacketError())
            {
                FinishDma(ok: true);
            }

            return;
        }

        var stream = new PrdtStream(DmaMemory, _busMaster.PrdtAddress);
        if (drive.DmaSectorPending)
        {
            bool toMemory = drive.DmaIsRead;
            while (drive.DmaSectorPending)
            {
                if (toMemory)
                {
                    drive.FetchDmaSector();
                    stream.Write(drive.DmaBuffer);
                }
                else
                {
                    stream.Read(drive.DmaBuffer);
                    drive.CommitDmaSector();
                }

                if (stream.Failed || stream.BytesMoved > MaxDmaBytes)
                {
                    break;
                }

                drive.AdvanceDma();
            }

            FinishDma(!stream.Failed && !drive.DmaSectorPending);
            return;
        }

        // ATAPI DMA: the 12-byte command block arrives first through the PRDT
        // stream, then a staged packet response (if any) goes back out on it.
        if (!drive.DmaResponseReady)
        {
            byte[] packet = new byte[AtaDevice.PacketSize];
            stream.Read(packet);
            if (stream.Failed)
            {
                FinishDma(false);
                return;
            }

            drive.ReceiveDmaPacket(packet);
        }

        if (drive.DmaResponseReady)
        {
            stream.Write(drive.DmaResponse.ToArray());
            drive.FinishDmaResponse();
        }

        FinishDma(!stream.Failed);
    }

    private void FinishDma(bool ok)
    {
        _busMaster.Status &= ~BmStatusActive;
        if (ok)
        {
            _busMaster.Status |= BmStatusInterrupt;
        }
        else
        {
            _busMaster.Status |= BmStatusError | BmStatusInterrupt;
        }

        RaiseInterrupt();
    }

    /// <summary>Walks a Physical Region Descriptor Table in guest memory.</summary>
    private sealed class PrdtStream
    {
        private readonly IGuestMemory _memory;
        private uint _entry;
        private uint _base;
        private uint _left;
        private bool _end;

        public PrdtStream(IGuestMemory memory, uint prdtAddress)
        {
            _memory = memory;
            _entry = prdtAddress & 0xFFFFFFFCu;
            LoadEntry();
        }

        public uint BytesMoved { get; private set; }

        public bool Failed { get; private set; }

        public void Read(byte[] destination)
        {
            Read(destination.AsSpan());
        }

        private void Read(Span<byte> destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                if (!EnsureByte())
                {
                    return;
                }

                destination[i] = _memory.ReadPhysical(_base);
                Advance();
            }
        }

        public void Write(byte[] source)
        {
            Write(source.AsSpan());
        }

        private void Write(ReadOnlySpan<byte> source)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (!EnsureByte())
                {
                    return;
                }

                _memory.WritePhysical(_base, source[i]);
                Advance();
            }
        }

        private void LoadEntry()
        {
            uint low = Read32(_entry);
            uint high = Read32(_entry + 4);
            _entry += 8;
            _base = low & 0xFFFFFFFEu;
            uint count = high & 0xFFFFu;
            _left = count == 0 ? 65536u : count;
            _end = (high & 0x80000000u) != 0;
        }

        private bool EnsureByte()
        {
            if (Failed)
            {
                return false;
            }

            if (_left == 0)
            {
                if (_end)
                {
                    Failed = true;
                    return false;
                }

                LoadEntry();
            }

            return true;
        }

        private void Advance()
        {
            _base++;
            _left--;
            BytesMoved++;
        }

        private uint Read32(uint address)
        {
            return (uint)(_memory.ReadPhysical(address)
                          | (_memory.ReadPhysical(address + 1) << 8)
                          | (_memory.ReadPhysical(address + 2) << 16)
                          | (_memory.ReadPhysical(address + 3) << 24));
        }
    }

    private sealed class BusMasterChannel
    {
        public uint Command { get; set; }

        public uint Status { get; set; }

        public uint PrdtAddress { get; set; }

        public int Starts { get; set; }

        public void Reset()
        {
            Command = 0;
            Status = 0;
            PrdtAddress = 0;
        }
    }

    private sealed class BusMasterWindow : IIoPortDevice
    {
        private readonly IdeController _controller;

        public BusMasterWindow(IdeController controller)
        {
            _controller = controller;
        }

        public static string Name => "IDE bus master";

        public uint Read(ushort offset, int size)
        {
            uint result = (offset & 8) == 0 ? ReadPrimary((ushort)(offset & 7)) : 0;
            _controller.LogAccess($"BR{offset:X}:{result:X}");
            return size switch
            {
                8 => result & 0xFFu,
                16 => result & 0xFFFFu,
                _ => result,
            };
        }

        public void Write(ushort offset, int size, uint value)
        {
            if ((offset & 8) != 0)
            {
                return;
            }

            _controller.LogAccess($"BW{offset & 7:X}:{value:X}:{size}");
            var bm = _controller._busMaster;
            switch (offset & 7)
            {
                case 0:
                    bm.Command = size == 8 ? (byte)value : value;
                    if ((bm.Command & BmCommandStart) != 0)
                    {
                        bm.Starts++;
                        bm.Status |= BmStatusActive;
                        _controller.TryRunDma();
                    }
                    else
                    {
                        bm.Status &= ~BmStatusActive;
                    }

                    break;
                case 2:
                    // Writing 1 to ERROR/INTERRUPT clears them.
                    bm.Status &= ~(value & (BmStatusError | BmStatusInterrupt));
                    break;
                case 4:
                    uint width = size switch
                    {
                        8 => 0xFFu,
                        16 => 0xFFFFu,
                        _ => 0xFFFFFFFFu,
                    };
                    int shift = (offset & 3) * 8;
                    bm.PrdtAddress = ((bm.PrdtAddress & ~(width << shift)) | ((value & width) << shift)) & 0xFFFFFFFEu;
                    break;
            }
        }

        private uint ReadPrimary(ushort offset)
        {
            var bm = _controller._busMaster;
            return offset switch
            {
                0 => bm.Command,
                2 => bm.Status,
                4 => bm.PrdtAddress,
                _ => 0,
            };
        }
    }

    private sealed class TaskfileWindow : IIoPortDevice
    {
        private readonly IdeController _controller;

        public TaskfileWindow(IdeController controller)
        {
            _controller = controller;
        }

        public static string Name => "IDE taskfile";

        public uint Read(ushort offset, int size)
        {
            uint result = _controller.SelectedDrive.ReadTaskfile(offset, size);
            if (offset == 7)
            {
                _controller.LogAccess($"T{(_controller._selected == 0 ? 'M' : 'S')}R7:{result:X}:{size}");
            }

            return result;
        }

        public void Write(ushort offset, int size, uint value)
        {
            if (offset == 6)
            {
                _controller.SelectDrive(value);
                return;
            }

            if (offset is 1 or 2 or 3 or 4 or 5)
            {
                // Shared bus: taskfile setup writes land on both drives (QEMU ide_ioport_write).
                _controller.PrimaryMaster.WriteTaskfile(offset, size, value);
                _controller.PrimarySlave.WriteTaskfile(offset, size, value);
                return;
            }

            _controller.SelectedDrive.WriteTaskfile(offset, size, value);
            if (offset == 7)
            {
                _controller.LogCommand((byte)value);
            }

            _controller.LogAccess($"T{(_controller._selected == 0 ? 'M' : 'S')}W{offset:X}:{value:X}:{size}");

            if (offset is 0 or 7)
            {
                // Drive-initiated kick: covers START-then-command order (the bus
                // master may already be armed when the drive stages DMA work).
                _controller.TryRunDma();
            }
        }
    }

    private sealed class ControlWindow : IIoPortDevice
    {
        private readonly IdeController _controller;

        public ControlWindow(IdeController controller)
        {
            _controller = controller;
        }

        public static string Name => "IDE control";

        public uint Read(ushort offset, int size)
        {
            return _controller.SelectedDrive.ReadControl();
        }

        public void Write(ushort offset, int size, uint value)
        {
            _controller.PrimaryMaster.WriteControl(value);
            _controller.PrimarySlave.WriteControl(value);
        }
    }
}
