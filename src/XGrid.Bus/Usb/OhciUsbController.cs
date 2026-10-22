using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;
using XGrid.Memory;

namespace XGrid.Bus.Usb;

/// <summary>Guest memory access used by the OHCI schedule engine and HCCA writeback.</summary>
public interface IUsbMemory
{
    /// <summary>Reads an 8-bit little-endian value.</summary>
    byte Read8(uint address);

    /// <summary>Reads a 16-bit little-endian value.</summary>
    ushort Read16(uint address);

    /// <summary>Reads a 32-bit little-endian value.</summary>
    uint Read32(uint address);

    /// <summary>Writes an 8-bit value.</summary>
    void Write8(uint address, byte value);

    /// <summary>Writes a 16-bit little-endian value.</summary>
    void Write16(uint address, ushort value);

    /// <summary>Writes a 32-bit little-endian value.</summary>
    void Write32(uint address, uint value);
}

/// <summary>
/// OHCI 1.0 host controller as found in the MCPX (PCI 0:2.0 at 0xFED00000 and
/// 0:3.0 at 0xFED08000). Register model, root hub, and interrupt semantics follow
/// xemu <c>hw/usb/hcd-ohci.c</c> (region size 256, four root ports per xemu
/// <c>hw/xbox/xbox.c</c>). Frame timing is driven by <see cref="Advance"/> (1 ms frames)
/// producing SOF interrupts and HCCA writeback. A single
/// <see cref="IUsbDeviceFunction"/> (normally a <see cref="UsbHub"/> topology with
/// an <see cref="XidGamepad"/> behind it) can be attached to root port 0; transfers
/// addressed to other device addresses are routed through attached hubs
/// (<see cref="IUsbDeviceHub"/>). The frame boundary walks the control, bulk, and
/// periodic endpoint lists and retires general transfer descriptors synchronously
/// (NoError/STALL/DataUnderrun completion codes, done-queue writeback with the
/// WDH interrupt). Isochronous transfers are not serviced.
/// </summary>
public sealed class OhciUsbController : IMmioDevice, IXboxDevice, ISnapshotable
{
    /// <summary>Register window size (xemu registers a 256-byte OHCI region).</summary>
    public const uint WindowSize = 0x100;

    /// <summary>Number of root hub ports (xemu xbox.c: num-ports=4).</summary>
    public const int PortCount = 4;

    // HcControl (OHCI 1.0).
    private const uint CtlHcfs = (1u << 6) | (1u << 7);
    private const uint UsbReset = 0x00;
    private const uint UsbResume = 0x40;
    private const uint UsbOperational = 0x80;
    private const uint UsbSuspend = 0xc0;
    private const uint CtlIr = 1u << 8;
    private const uint CtlPle = 1u << 2;
    private const uint CtlCle = 1u << 4;
    private const uint CtlBle = 1u << 5;

    // HcCommandStatus.
    private const uint StatusHcr = 1u << 0;
    private const uint StatusClf = 1u << 1;
    private const uint StatusBlf = 1u << 2;
    private const uint StatusSoc = (1u << 6) | (1u << 7);

    // HcInterruptStatus / HcInterruptEnable.
    private const uint IntrWd = 1u << 1;
    private const uint IntrSf = 1u << 2;
    private const uint IntrRd = 1u << 3;
    private const uint IntrUe = 1u << 4;
    private const uint IntrRhsc = 1u << 6;
    private const uint IntrMie = 1u << 31;

    // Masks.
    private const uint HccaMask = 0xFFFFFF00;
    private const uint EdptrMask = 0xFFFFFFF0;
    private const uint FmiFi = 0x00003FFF;
    private const uint FmiFsmps = 0xFFFF0000;
    private const uint FmiFit = 0x80000000;
    private const uint LsThresh = 0x628;
    private const uint RhaNps = 1u << 9;
    private const uint RhaRwMask = 0;

    // HcRhStatus.
    private const uint RhsLps = 1u << 0;
    private const uint RhsDrwe = 1u << 15;
    private const uint RhsLpsc = 1u << 16;
    private const uint RhsOcic = 1u << 17;
    private const uint RhsCrwe = 1u << 31;

    // HcRhPortStatus.
    private const uint PortCcs = 1u << 0;
    private const uint PortPes = 1u << 1;
    private const uint PortPss = 1u << 2;
    private const uint PortPrs = 1u << 4;
    private const uint PortPps = 1u << 8;
    private const uint PortLsda = 1u << 9;
    private const uint PortCsc = 1u << 16;
    private const uint PortPesc = 1u << 17;
    private const uint PortPssc = 1u << 18;
    private const uint PortOcic = 1u << 19;
    private const uint PortPrsc = 1u << 20;
    private const uint PortWtc = PortCsc | PortPesc | PortPssc | PortOcic | PortPrsc;

    // Endpoint/transfer descriptor fields (OHCI 1.0a §4; xemu hcd-ohci.c bit facts).
    private const uint EdFuncMask = 0x7Fu;
    private const int EdEndpointShift = 7;
    private const int EdDirectionShift = 11;
    private const uint EdSkip = 1u << 14;
    private const uint EdIsochronous = 1u << 15;
    private const uint EdHalted = 1u;
    private const uint EdToggleCarry = 2u;
    private const uint DptrMask = 0xFFFFFFF0u;
    private const uint TdRound = 1u << 18;
    private const int TdDirectionShift = 19;
    private const int TdDelayShift = 21;
    private const uint TdToggle0 = 1u << 24;
    private const uint TdToggle1 = 1u << 25;
    private const int TdConditionShift = 28;
    private const uint CcNoError = 0u;
    private const uint CcStall = 4u;
    private const uint CcDataUnderrun = 9u;
    private const int EdLinkLimit = 32;
    private const int MaxTransferBytes = 512;

    private readonly IUsbMemory _memory;
    private readonly uint[] _ports = new uint[PortCount];
    private IUsbDeviceFunction? _function;
    private uint _ctl;
    private uint _status;
    private uint _intrStatus;
    private uint _intr;
    private uint _perCur;
    private uint _ctrlCur;
    private uint _bulkHead;
    private uint _bulkCur;
    private uint _done;
    private uint _fi;
    private uint _fsmps;
    private uint _fit;
    private uint _frt;
    private uint _frameNumber;
    private uint _pstart;
    private uint _lst;
    private uint _rhdescA;
    private uint _rhdescB;
    private uint _rhstatus;
    private bool _busRunning;
    private ulong _frameRemainder;
    private uint _doneCount;
    private readonly List<string> _transferLog = new();
    private readonly Dictionary<uint, int> _regReads = new();
    private readonly Dictionary<uint, int> _regWrites = new();
    private bool _pendingSetup;
    private bool _pendingIn;
    private IUsbDeviceFunction? _pendingFunction;
    private byte _pendingFunctionAddress;
    private byte[] _pendingPayload = Array.Empty<byte>();
    private int _pendingOffset;
    private byte[] _pendingOut = Array.Empty<byte>();

    /// <summary>Creates the controller at the given MMIO base.</summary>
    public OhciUsbController(uint baseAddress, IUsbMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        BaseAddress = baseAddress;
        _memory = memory;
        Reset();
    }

    /// <summary>Gets the MMIO base address of this controller.</summary>
    public uint BaseAddress { get; }

    /// <summary>Gets the device name reported to traces and diagnostics ("OHCI USB").</summary>
#pragma warning disable CA1822
    public string Name => "OHCI USB";
#pragma warning restore CA1822

    /// <summary>Gets whether the controller interrupt line is asserted, for diagnostics.</summary>
    public bool InterruptAsserted { get; private set; }

    /// <summary>Gets the raw root-hub port status register, for diagnostics.</summary>
    public uint GetPortStatus(int index)
    {
        return (uint)index < PortCount ? _ports[index] : 0;
    }

    /// <summary>Gets the number of register accesses, for diagnostics.</summary>
    public long RegisterAccesses { get; private set; }

    /// <summary>Gets the device function attached to root port 0 (normally the daughterboard hub), if any.</summary>
    public IUsbDeviceFunction? Function
    {
        get => _function;
        set
        {
            _function = value;
            ReportPortZeroConnect();
        }
    }

    /// <summary>Gets the number of retired control transfer descriptors, for diagnostics.</summary>
    public long ControlTransfers { get; private set; }

    /// <summary>Gets the number of retired periodic (interrupt) transfer descriptors.</summary>
    public long InterruptTransfers { get; private set; }

    /// <summary>Gets the attached device address, for diagnostics.</summary>
    public uint DeviceAddress => Function?.Address ?? 0;

    /// <summary>Gets the programmed control list head, for diagnostics.</summary>
    public uint ControlHead { get; private set; }

    /// <summary>Gets the programmed HCCA address, for diagnostics.</summary>
    public uint Hcca { get; private set; }

    /// <summary>Gets recent retired transfer outcomes, for diagnostics.</summary>
    public IReadOnlyList<string> TransferLog => _transferLog;

    /// <summary>Gets per-offset register read counts, for diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> RegReadCounts => _regReads;

    /// <summary>Gets per-offset register write counts, for diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> RegWriteCounts => _regWrites;

    /// <summary>Raised when the interrupt line level changes.</summary>
    public event Action<bool>? InterruptChanged;

    /// <summary>Returns the controller to its power-on state: soft-resets the OHCI registers (HcControl enters suspend), clears the frame/done/pending-transfer state, and resets the root hub ports and attached function.</summary>
    public void Reset()
    {
        SoftResetState();
        _ctl = 0;
        RootHubReset();
    }

    /// <summary>Saves registers, ports, frame/done state, pending stages, counters, and the hub tree.</summary>
    /// <remarks>Quiesce: save at a 1 ms frame boundary with no transfer mid-stage
    /// (guest idle or between frames). The guest-memory adapter is host wiring and is not
    /// serialized. Topology is caller-owned; the attached function is cascaded when snapshotable.</remarks>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(BaseAddress);
        writer.Write(_ctl);
        writer.Write(_status);
        writer.Write(_intrStatus);
        writer.Write(_intr);
        writer.Write(_perCur);
        writer.Write(_ctrlCur);
        writer.Write(_bulkHead);
        writer.Write(_bulkCur);
        writer.Write(_done);
        writer.Write(_fi);
        writer.Write(_fsmps);
        writer.Write(_fit);
        writer.Write(_frt);
        writer.Write(_frameNumber);
        writer.Write(_pstart);
        writer.Write(_lst);
        writer.Write(_rhdescA);
        writer.Write(_rhdescB);
        writer.Write(_rhstatus);
        for (int i = 0; i < PortCount; i++)
        {
            writer.Write(_ports[i]);
        }

        writer.Write(_busRunning);
        writer.Write(_frameRemainder);
        writer.Write(_doneCount);
        writer.Write(RegisterAccesses);
        writer.Write(ControlTransfers);
        writer.Write(InterruptTransfers);
        writer.Write(ControlHead);
        writer.Write(Hcca);
        writer.Write(InterruptAsserted);
        writer.Write(_pendingSetup);
        writer.Write(_pendingIn);
        writer.Write(_pendingFunction is not null);
        if (_pendingFunction is not null)
        {
            writer.Write(_pendingFunctionAddress);
        }

        writer.Write(_pendingPayload.Length);
        writer.Write(_pendingPayload);
        writer.Write(_pendingOffset);
        writer.Write(_pendingOut.Length);
        writer.Write(_pendingOut);
        writer.Write(_transferLog.Count);
        foreach (string entry in _transferLog)
        {
            writer.Write(entry);
        }

        writer.Write(_regReads.Count);
        foreach (var entry in _regReads)
        {
            writer.Write(entry.Key);
            writer.Write(entry.Value);
        }

        writer.Write(_regWrites.Count);
        foreach (var entry in _regWrites)
        {
            writer.Write(entry.Key);
            writer.Write(entry.Value);
        }

        writer.Write(_function is not null);
        if (_function is ISnapshotable snapshotable)
        {
            snapshotable.SaveState(writer);
        }
        else if (_function is not null)
        {
            throw new InvalidDataException("OHCI attached function is not snapshotable.");
        }
    }

    /// <summary>Restores registers, ports, frame/done state, pending stages, counters, and the hub tree.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported OHCI snapshot version {version}.");
        }

        if (reader.ReadUInt32() != BaseAddress)
        {
            throw new InvalidDataException("OHCI base-address mismatch on snapshot load.");
        }

        _ctl = reader.ReadUInt32();
        _status = reader.ReadUInt32();
        _intrStatus = reader.ReadUInt32();
        _intr = reader.ReadUInt32();
        _perCur = reader.ReadUInt32();
        _ctrlCur = reader.ReadUInt32();
        _bulkHead = reader.ReadUInt32();
        _bulkCur = reader.ReadUInt32();
        _done = reader.ReadUInt32();
        _fi = reader.ReadUInt32();
        _fsmps = reader.ReadUInt32();
        _fit = reader.ReadUInt32();
        _frt = reader.ReadUInt32();
        _frameNumber = reader.ReadUInt32();
        _pstart = reader.ReadUInt32();
        _lst = reader.ReadUInt32();
        _rhdescA = reader.ReadUInt32();
        _rhdescB = reader.ReadUInt32();
        _rhstatus = reader.ReadUInt32();
        for (int i = 0; i < PortCount; i++)
        {
            _ports[i] = reader.ReadUInt32();
        }

        _busRunning = reader.ReadBoolean();
        _frameRemainder = reader.ReadUInt64();
        _doneCount = reader.ReadUInt32();
        RegisterAccesses = reader.ReadInt64();
        ControlTransfers = reader.ReadInt64();
        InterruptTransfers = reader.ReadInt64();
        ControlHead = reader.ReadUInt32();
        Hcca = reader.ReadUInt32();
        InterruptAsserted = reader.ReadBoolean();
        _pendingSetup = reader.ReadBoolean();
        _pendingIn = reader.ReadBoolean();
        bool hasPendingFunction = reader.ReadBoolean();
        byte pendingAddress = 0;
        if (hasPendingFunction)
        {
            pendingAddress = reader.ReadByte();
        }

        int payloadLength = reader.ReadInt32();
        if (payloadLength < 0)
        {
            throw new InvalidDataException("Negative OHCI payload snapshot.");
        }

        _pendingPayload = reader.ReadBytes(payloadLength);
        if (_pendingPayload.Length != payloadLength)
        {
            throw new InvalidDataException("Truncated OHCI payload snapshot.");
        }

        _pendingOffset = reader.ReadInt32();
        int outLength = reader.ReadInt32();
        if (outLength < 0)
        {
            throw new InvalidDataException("Negative OHCI out-buffer snapshot.");
        }

        _pendingOut = reader.ReadBytes(outLength);
        if (_pendingOut.Length != outLength)
        {
            throw new InvalidDataException("Truncated OHCI out-buffer snapshot.");
        }

        int transfers = reader.ReadInt32();
        if (transfers < 0 || transfers > 128)
        {
            throw new InvalidDataException("Bad OHCI transfer-log snapshot.");
        }

        _transferLog.Clear();
        for (int i = 0; i < transfers; i++)
        {
            _transferLog.Add(reader.ReadString());
        }

        int reads = reader.ReadInt32();
        if (reads < 0)
        {
            throw new InvalidDataException("Negative OHCI read histogram snapshot.");
        }

        _regReads.Clear();
        for (int i = 0; i < reads; i++)
        {
            _regReads[reader.ReadUInt32()] = reader.ReadInt32();
        }

        int writes = reader.ReadInt32();
        if (writes < 0)
        {
            throw new InvalidDataException("Negative OHCI write histogram snapshot.");
        }

        _regWrites.Clear();
        for (int i = 0; i < writes; i++)
        {
            _regWrites[reader.ReadUInt32()] = reader.ReadInt32();
        }

        bool hasFunction = reader.ReadBoolean();
        if (hasFunction != (_function is not null))
        {
            throw new InvalidDataException("OHCI topology mismatch on snapshot load.");
        }

        if (_function is ISnapshotable snapshotable)
        {
            snapshotable.LoadState(reader);
        }

        if (hasPendingFunction)
        {
            _pendingFunctionAddress = pendingAddress;
            _pendingFunction = ResolveFunction(pendingAddress);
            if (_pendingSetup && _pendingFunction is null)
            {
                throw new InvalidDataException("OHCI pending transfer target missing on snapshot load.");
            }
        }
        else
        {
            _pendingFunction = null;
            _pendingFunctionAddress = 0;
        }
    }

    private const int SnapshotVersion = 1;

    /// <summary>Reads an OHCI register at the given absolute address in the 256-byte window, counting the access and falling back to all-ones for out-of-window or non-dword-aligned offsets.</summary>
    /// <param name="address">The absolute MMIO address (mapped to a window offset by subtracting <see cref="BaseAddress"/>).</param>
    /// <param name="size">The access size in bytes; the register map is decoded on 4-byte accesses.</param>
    /// <returns>The register value, or 0xFFFFFFFF when the offset is outside the window or is not 4-byte aligned.</returns>
    public uint Read(uint address, int size)
    {
        uint offset = address - BaseAddress;
        RegisterAccesses++;
        if (offset < WindowSize)
        {
            _regReads.TryGetValue(offset, out int reads);
            _regReads[offset] = reads + 1;
        }

        if (offset >= WindowSize || (offset & 3) != 0)
        {
            return 0xFFFFFFFFu;
        }

        if (offset >= 0x54 && offset < 0x54 + ((uint)PortCount * 4))
        {
            return _ports[(offset - 0x54) >> 2] | PortPps;
        }

        return (offset >> 2) switch
        {
            0 => 0x10u,
            1 => _ctl,
            2 => _status,
            3 => _intrStatus,
            4 or 5 => _intr,
            6 => Hcca,
            7 => _perCur,
            8 => ControlHead,
            9 => _ctrlCur,
            10 => _bulkHead,
            11 => _bulkCur,
            12 => _done,
            13 => (_fit << 31) | (_fsmps << 16) | _fi,
            14 => _frt << 31,
            15 => _frameNumber,
            16 => _pstart,
            17 => _lst,
            18 => _rhdescA,
            19 => _rhdescB,
            20 => _rhstatus,
            _ => 0xFFFFFFFFu,
        };
    }

    /// <summary>Writes an OHCI register at the given absolute address in the 256-byte window, counting the access; root-hub port offsets are routed to the port state machine, and out-of-window or non-dword-aligned offsets are ignored.</summary>
    /// <param name="address">The absolute MMIO address (mapped to a window offset by subtracting <see cref="BaseAddress"/>).</param>
    /// <param name="size">The access size in bytes; the register map is decoded on 4-byte accesses.</param>
    /// <param name="value">The value written to the register.</param>
    public void Write(uint address, int size, uint value)
    {
        uint offset = address - BaseAddress;
        RegisterAccesses++;
        if (offset < WindowSize)
        {
            _regWrites.TryGetValue(offset, out int writes);
            _regWrites[offset] = writes + 1;
        }

        if (offset >= WindowSize || (offset & 3) != 0)
        {
            return;
        }

        if (offset >= 0x54 && offset < 0x54 + ((uint)PortCount * 4))
        {
            SetPortStatus((int)((offset - 0x54) >> 2), value);
            return;
        }

        switch (offset >> 2)
        {
            case 1:
                SetControl(value);
                break;

            case 2:
                value &= ~StatusSoc;
                _status |= value;
                if ((_status & StatusHcr) != 0)
                {
                    SoftReset();
                }

                break;

            case 3:
                _intrStatus &= ~value;
                UpdateIrq();
                break;

            case 4:
                _intr |= value;
                UpdateIrq();
                break;

            case 5:
                _intr &= ~value;
                UpdateIrq();
                break;

            case 6:
                Hcca = value & HccaMask;
                break;

            case 8:
                ControlHead = value & EdptrMask;
                break;

            case 9:
                _ctrlCur = value & EdptrMask;
                break;

            case 10:
                _bulkHead = value & EdptrMask;
                break;

            case 11:
                _bulkCur = value & EdptrMask;
                break;

            case 13:
                _fsmps = (value & FmiFsmps) >> 16;
                _fit = (value & FmiFit) >> 31;
                _fi = value & FmiFi;
                break;

            case 16:
                _pstart = value & 0xFFFF;
                break;

            case 17:
                _lst = value & 0xFFFF;
                break;

            case 18:
                _rhdescA = (_rhdescA & ~RhaRwMask) | (value & RhaRwMask);
                break;

            case 20:
                SetHubStatus(value);
                break;

        }
    }

    /// <summary>
    /// Advances the 1 ms frame clock. Frame boundaries raise SOF interrupts and
    /// write the frame number back into the HCCA, matching xemu's EOF timer.
    /// </summary>
    public void Advance(ulong cycles)
    {
        if (!_busRunning)
        {
            return;
        }

        _frameRemainder += cycles * 1000;
        ulong frames = _frameRemainder / XboxBus.CpuClockHz;
        _frameRemainder %= XboxBus.CpuClockHz;
        for (ulong i = 0; i < frames; i++)
        {
            FrameBoundary();
        }
    }

    private void SoftReset()
    {
        SoftResetState();
    }

    private void SoftResetState()
    {
        // xemu ohci_soft_reset: the root hub and its descriptors are untouched.
        _ctl = (_ctl & CtlIr) | UsbSuspend;
        _status = 0;
        _intrStatus = 0;
        _intr = IntrMie;
        Hcca = 0;
        _perCur = 0;
        ControlHead = 0;
        _ctrlCur = 0;
        _bulkHead = 0;
        _bulkCur = 0;
        _done = 0;
        _fi = 0x2EDF;
        _fsmps = 0;
        _fit = 0;
        _frt = 0;
        _frameNumber = 0;
        _pstart = 0;
        _lst = LsThresh;
        _busRunning = false;
        _frameRemainder = 0;
        _doneCount = 7;
        _pendingSetup = false;
        _pendingFunction = null;
        _pendingFunctionAddress = 0;
        _pendingPayload = Array.Empty<byte>();
        _pendingOut = Array.Empty<byte>();
        _pendingOffset = 0;
        UpdateIrq();
    }

    private void SetControl(uint value)
    {
        uint oldState = _ctl & CtlHcfs;
        _ctl = value;
        uint newState = _ctl & CtlHcfs;
        if (oldState == newState)
        {
            return;
        }

        switch (newState)
        {
            case UsbOperational:
                _busRunning = true;
                break;

            case UsbSuspend:
                _busRunning = false;
                _intrStatus &= ~IntrSf;
                UpdateIrq();
                break;

            case UsbResume:
                break;

            case UsbReset:
                RootHubReset();
                break;

        }
    }

    private void RootHubReset()
    {
        _busRunning = false;
        _rhdescA = RhaNps | PortCount;
        _rhdescB = 0;
        _rhstatus = 0;
        Array.Clear(_ports);
        Function?.Reset();
        ReportPortZeroConnect();
    }

    /// <summary>Reports the attached function as connected on port 0, if any.</summary>
    private void ReportPortZeroConnect()
    {
        // The hub reports no power switching, so port 0 is powered by default
        // and an attached function reads connected without guest power writes.
        if (_function is null)
        {
            return;
        }

        uint oldState = _ports[0];
        _ports[0] |= PortCcs | PortCsc;
        if ((_ctl & CtlHcfs) == UsbSuspend)
        {
            SetInterrupt(IntrRd);
        }

        if (oldState != _ports[0])
        {
            SetInterrupt(IntrRhsc);
        }
    }

    private void SetHubStatus(uint value)
    {
        uint oldState = _rhstatus;
        if ((value & RhsOcic) != 0)
        {
            _rhstatus &= ~RhsOcic;
        }

        if ((value & RhsLps) != 0)
        {
            for (int i = 0; i < PortCount; i++)
            {
                SetPortPower(i, power: false);
            }
        }

        if ((value & RhsLpsc) != 0)
        {
            for (int i = 0; i < PortCount; i++)
            {
                SetPortPower(i, power: true);
            }
        }

        if ((value & RhsDrwe) != 0)
        {
            _rhstatus |= RhsDrwe;
        }

        if ((value & RhsCrwe) != 0)
        {
            _rhstatus &= ~RhsDrwe;
        }

        if (oldState != _rhstatus)
        {
            SetInterrupt(IntrRhsc);
        }
    }

    private void SetPortPower(int index, bool power)
    {
        if (power)
        {
            _ports[index] |= PortPps;
        }
        else
        {
            _ports[index] &= ~(PortPps | PortCcs | PortPss | PortPrs);
        }
    }

    private void SetPortStatus(int index, uint value)
    {
        uint oldState = _ports[index];
        if ((value & PortWtc) != 0)
        {
            _ports[index] &= ~(value & PortWtc);
        }

        if ((value & PortCcs) != 0)
        {
            _ports[index] &= ~PortPes;
        }

        SetPortIfConnected(index, value & PortPes);
        SetPortIfConnected(index, value & PortPss);
        if (SetPortIfConnected(index, value & PortPrs))
        {
            // Reset completes immediately; the port reports enable plus
            // reset-status-change as xemu does.
            _ports[index] &= ~PortPrs;
            _ports[index] |= PortPes | PortPrsc;
            if (index == 0)
            {
                // Port reset is a bus reset: the attached function returns to
                // the Default state (address zero, unconfigured), as hardware.
                Function?.Reset();
            }
        }

        if ((value & PortLsda) != 0)
        {
            SetPortPower(index, power: false);
        }

        if ((value & PortPps) != 0)
        {
            SetPortPower(index, power: true);
        }

        if (oldState != _ports[index])
        {
            SetInterrupt(IntrRhsc);
        }
    }

    private bool SetPortIfConnected(int index, uint value)
    {
        if (value == 0)
        {
            return false;
        }

        if ((_ports[index] & PortCcs) == 0)
        {
            // No device on the port: report a connect-status change instead.
            _ports[index] |= PortCsc;
            return false;
        }

        bool newlySet = (_ports[index] & value) == 0;
        _ports[index] |= value;
        return newlySet;
    }

    private void FrameBoundary()
    {
        if ((_intrStatus & IntrUe) != 0)
        {
            return;
        }

        ServiceLists();

        _frt = _fit;
        _frameNumber = (_frameNumber + 1) & 0xFFFF;
        bool wroteDone = false;
        uint doneWriteback = 0;
        if (_doneCount == 0 && (_intrStatus & IntrWd) == 0 && _done != 0)
        {
            doneWriteback = _done;
            if ((_intr & _intrStatus) != 0)
            {
                doneWriteback |= 1;
            }

            _done = 0;
            _doneCount = 7;
            wroteDone = true;
            SetInterrupt(IntrWd);
        }
        else if (_doneCount != 7 && _doneCount != 0)
        {
            _doneCount--;
        }

        if (Hcca != 0)
        {
            _memory.Write16(Hcca + 0x80, (ushort)_frameNumber);
            _memory.Write16(Hcca + 0x82, 0);
            if (wroteDone)
            {
                _memory.Write32(Hcca + 0x84, doneWriteback);
            }
        }

        SetInterrupt(IntrSf);
    }

    /// <summary>Walks the periodic, control, and bulk endpoint lists once per frame.</summary>
    private void ServiceLists()
    {
        if ((_ctl & CtlPle) != 0 && Hcca != 0)
        {
            uint head = _memory.Read32(Hcca + ((_frameNumber & 0x1F) * 4));
            ServiceEdList(head & DptrMask, isPeriodic: true);
        }

        if ((_ctl & CtlCle) != 0 && (_status & StatusClf) != 0 && ControlHead != 0)
        {
            if (!ServiceEdList(ControlHead, isPeriodic: false))
            {
                _ctrlCur = 0;
                _status &= ~StatusClf;
            }
        }

        if ((_ctl & CtlBle) != 0 && (_status & StatusBlf) != 0 && _bulkHead != 0)
        {
            if (!ServiceEdList(_bulkHead, isPeriodic: false))
            {
                _bulkCur = 0;
                _status &= ~StatusBlf;
            }
        }
    }

    /// <summary>Services one endpoint list; returns true when transfer work remains.</summary>
    private bool ServiceEdList(uint head, bool isPeriodic)
    {
        bool active = false;
        uint current = head;
        for (int link = 0; link < EdLinkLimit && current != 0; link++)
        {
            uint flags = _memory.Read32(current);
            uint tail = _memory.Read32(current + 4);
            uint edHead = _memory.Read32(current + 8);
            uint next = _memory.Read32(current + 12) & DptrMask;
            if ((edHead & EdHalted) != 0 || (flags & EdSkip) != 0 || (flags & EdIsochronous) != 0)
            {
                current = next;
                continue;
            }

            bool dirty = false;
            while ((edHead & DptrMask) != (tail & DptrMask))
            {
                active = true;
                dirty = true;
                if (ServiceTd(flags, ref edHead, isPeriodic))
                {
                    break;
                }
            }

            if (dirty)
            {
                _memory.Write32(current, flags);
                _memory.Write32(current + 8, edHead);
            }

            current = next;
        }

        return active;
    }

    /// <summary>Services one transfer descriptor; returns true when the list must stop.</summary>
    private bool ServiceTd(uint edFlags, ref uint edHead, bool isPeriodic)
    {
        uint tdAddress = edHead & DptrMask;
        if (tdAddress == 0)
        {
            edHead |= EdHalted;
            return true;
        }

        uint tdFlags = _memory.Read32(tdAddress);
        uint cbp = _memory.Read32(tdAddress + 4);
        uint tdNext = _memory.Read32(tdAddress + 8);
        uint be = _memory.Read32(tdAddress + 12);
        int direction = (int)((edFlags >> EdDirectionShift) & 3u);
        if (direction != 1 && direction != 2)
        {
            direction = (int)((tdFlags >> TdDirectionShift) & 3u);
        }

        uint endpoint = (edFlags >> EdEndpointShift) & 0xFu;
        byte functionAddress = (byte)(edFlags & EdFuncMask);
        IUsbDeviceFunction? target = ResolveTarget(functionAddress, isPeriodic);
        if (target is null)
        {
            RetireTd(tdAddress, tdFlags, cbp, tdNext, 0, CcStall, isPeriodic, ref edHead);
            edHead |= EdHalted;
            return true;
        }

        int length = TransferLength(cbp, be);
        UsbTransferResult result;
        int consumed = 0;
        switch (direction)
        {
            case 0 when endpoint == 0:
                result = ServiceSetup(target, functionAddress, cbp, out consumed);
                break;
            case 1:
                result = ServiceOut(target, endpoint, cbp, length, isPeriodic, out consumed);
                break;
            case 2:
                result = ServiceIn(target, endpoint, cbp, length, isPeriodic, (tdFlags & TdRound) != 0, out consumed);
                break;
            default:
                result = UsbTransferResult.Stall;
                break;
        }

        if (result == UsbTransferResult.Nak)
        {
            LogTransfer(isPeriodic, direction, endpoint, length, 0, tdFlags, "nak");
            return true;
        }

        // XID-gate-II finding: the buffer pointer advances by every transferred
        // byte even when the stage completes with an error (xemu retire path);
        // only the CBP=0 clear and the toggle update are success-only. The
        // Xbox stack reads back the advanced CBP to learn the actual length.
        uint condition = result == UsbTransferResult.Ack &&
                         (consumed == length || (direction == 2 && (tdFlags & TdRound) != 0))
            ? CcNoError
            : result == UsbTransferResult.Ack
                ? CcDataUnderrun
                : CcStall;
        if (consumed > 0)
        {
            if (((cbp & 0xFFFu) + (uint)consumed) > 0xFFFu)
            {
                cbp = (be & ~0xFFFu) + ((cbp + (uint)consumed) & 0xFFFu);
            }
            else
            {
                cbp += (uint)consumed;
            }
        }

        if (condition == CcNoError)
        {
            if (consumed == length)
            {
                cbp = 0;
            }

            tdFlags |= TdToggle1;
            tdFlags ^= TdToggle0;
            edHead &= ~EdToggleCarry;
            if ((tdFlags & TdToggle0) != 0)
            {
                edHead |= EdToggleCarry;
            }
        }

        RetireTd(tdAddress, tdFlags, cbp, tdNext, (tdFlags >> TdDelayShift) & 7u, condition, isPeriodic, ref edHead);
        LogTransfer(
            isPeriodic, direction, endpoint, length, consumed, tdFlags,
            condition == CcNoError ? string.Empty : FormattableString.Invariant($"cc={condition}"));
        if (condition != CcNoError)
        {
            edHead |= EdHalted;
            return true;
        }

        return false;
    }

    private void LogTransfer(
        bool periodic, int direction, uint endpoint, int length, int consumed, uint tdFlags, string note)
    {
        // Successful periodic polls would flood the log; only errors are kept there.
        if (periodic && string.IsNullOrEmpty(note))
        {
            return;
        }

        if (_transferLog.Count >= 128)
        {
            _transferLog.RemoveAt(0);
        }

        string kind = periodic ? "int" : "ctl";
        string stage = direction switch
        {
            0 => "setup",
            1 => "out",
            _ => "in"
        };
        string round = direction == 2 ? FormattableString.Invariant($" r={(tdFlags & TdRound) != 0}") : string.Empty;
        _transferLog.Add(FormattableString.Invariant(
            $"f={_frameNumber} {kind} {stage} ep{endpoint} len={length} got={consumed}{round} {note}").TrimEnd());
    }

    /// <summary>Resolves the function addressed by an endpoint descriptor, routing through hubs.</summary>
    private IUsbDeviceFunction? ResolveFunction(byte address)
    {
        if (_function is null)
        {
            return null;
        }

        if (_function.Address == address)
        {
            return _function;
        }

        return _function is IUsbDeviceHub hub ? hub.FindDevice(address) : null;
    }

    /// <summary>
    /// Resolves the target of a transfer. Control data and status stages stay
    /// bound to the function captured at setup time so an address change on the
    /// status stage cannot retarget the transfer.
    /// </summary>
    private IUsbDeviceFunction? ResolveTarget(byte address, bool isPeriodic)
    {
        if (!isPeriodic && _pendingSetup && _pendingFunction is not null && address == _pendingFunctionAddress)
        {
            return _pendingFunction;
        }

        return ResolveFunction(address);
    }

    private UsbTransferResult ServiceSetup(IUsbDeviceFunction target, byte address, uint cbp, out int consumed)
    {
        consumed = 8;
        var setup = new UsbSetupPacket(
            _memory.Read8(cbp),
            _memory.Read8(cbp + 1),
            _memory.Read16(cbp + 2),
            _memory.Read16(cbp + 4),
            _memory.Read16(cbp + 6));
        UsbTransferResult result = target.HandleSetup(in setup, out byte[] payload);
        if (result != UsbTransferResult.Ack)
        {
            return result;
        }

        _pendingSetup = true;
        _pendingFunction = target;
        _pendingFunctionAddress = address;
        _pendingIn = (setup.RequestType & 0x80) != 0 && setup.Length > 0;
        _pendingPayload = payload;
        _pendingOut = setup.Length > 0 && !_pendingIn ? new byte[setup.Length] : Array.Empty<byte>();
        _pendingOffset = 0;
        return UsbTransferResult.Ack;
    }

    private UsbTransferResult ServiceOut(
        IUsbDeviceFunction target, uint endpoint, uint cbp, int length, bool isPeriodic, out int consumed)
    {
        consumed = length;
        if (length == 0)
        {
            // Zero-length stage completes a control transfer (status stage).
            if (_pendingSetup && !isPeriodic && target == _pendingFunction)
            {
                target.CompleteStatusStage();
                _pendingSetup = false;
                _pendingFunction = null;
            }

            return UsbTransferResult.Ack;
        }

        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = _memory.Read8(cbp + (uint)i);
        }

        if (isPeriodic)
        {
            return target.HandleOut((byte)endpoint, data);
        }

        if (!_pendingSetup || _pendingIn || target != _pendingFunction)
        {
            return UsbTransferResult.Stall;
        }

        if (_pendingOffset >= _pendingOut.Length)
        {
            return UsbTransferResult.Stall;
        }

        int room = Math.Min(length, _pendingOut.Length - _pendingOffset);
        Array.Copy(data, 0, _pendingOut, _pendingOffset, room);
        _pendingOffset += room;
        if (_pendingOffset < _pendingOut.Length)
        {
            return UsbTransferResult.Ack;
        }

        UsbTransferResult result = target.CompleteControlOut(_pendingOut);
        _pendingSetup = result == UsbTransferResult.Ack;
        if (!_pendingSetup)
        {
            _pendingFunction = null;
        }

        return result;
    }

    private UsbTransferResult ServiceIn(
        IUsbDeviceFunction target, uint endpoint, uint cbp, int length, bool isPeriodic, bool round, out int consumed)
    {
        consumed = 0;
        if (length == 0)
        {
            // Zero-length IN is the status stage of an OUT control transfer.
            if (_pendingSetup && !isPeriodic && target == _pendingFunction)
            {
                target.CompleteStatusStage();
                _pendingSetup = false;
                _pendingFunction = null;
            }

            return UsbTransferResult.Ack;
        }

        byte[] payload;
        UsbTransferResult result;
        if (isPeriodic)
        {
            result = target.HandleIn((byte)endpoint, length, out payload);
        }
        else
        {
            if (!_pendingSetup || !_pendingIn || target != _pendingFunction)
            {
                return UsbTransferResult.Stall;
            }

            int available = _pendingPayload.Length - _pendingOffset;
            if (available <= 0)
            {
                // The function has no more bytes for this stage: with Rounding
                // set the stage completes as an empty short packet, otherwise
                // the over-request stalls, per the OHCI data-stage rules.
                if (!round)
                {
                    return UsbTransferResult.Stall;
                }

                payload = Array.Empty<byte>();
                result = UsbTransferResult.Ack;
            }
            else
            {
                payload = new byte[Math.Min(length, available)];
                Array.Copy(_pendingPayload, _pendingOffset, payload, 0, payload.Length);
                result = UsbTransferResult.Ack;
            }
        }

        if (result != UsbTransferResult.Ack)
        {
            return result;
        }

        for (int i = 0; i < payload.Length; i++)
        {
            _memory.Write8(cbp + (uint)i, payload[i]);
        }

        consumed = payload.Length;
        if (!isPeriodic)
        {
            _pendingOffset += consumed;
        }

        return UsbTransferResult.Ack;
    }

    private void RetireTd(
        uint tdAddress, uint tdFlags, uint cbp, uint tdNext, uint delay, uint condition, bool isPeriodic,
        ref uint edHead)
    {
        edHead &= ~DptrMask;
        edHead |= tdNext & DptrMask;
        _memory.Write32(tdAddress, (tdFlags & ~(0xFu << TdConditionShift)) | (condition << TdConditionShift));
        _memory.Write32(tdAddress + 4, cbp);
        _memory.Write32(tdAddress + 8, _done);
        _done = tdAddress;
        if (delay < _doneCount)
        {
            _doneCount = delay;
        }

        if (condition != CcNoError)
        {
            _doneCount = 0;
        }

        if (isPeriodic)
        {
            InterruptTransfers++;
        }
        else
        {
            ControlTransfers++;
        }
    }

    private static int TransferLength(uint cbp, uint be)
    {
        if (cbp == 0 || be == 0)
        {
            return 0;
        }

        int length = (cbp & 0xFFFFF000u) != (be & 0xFFFFF000u)
            ? (int)(be & 0xFFFu) + 0x1001 - (int)(cbp & 0xFFFu)
            : (int)(be - cbp) + 1;
        return Math.Min(Math.Max(length, 0), MaxTransferBytes);
    }

    private void SetInterrupt(uint bits)
    {
        _intrStatus |= bits;
        UpdateIrq();
    }

    private void UpdateIrq()
    {
        bool asserted = (_intr & IntrMie) != 0 && (_intrStatus & _intr) != 0;
        if (asserted != InterruptAsserted)
        {
            InterruptAsserted = asserted;
            InterruptChanged?.Invoke(asserted);
        }
    }
}
