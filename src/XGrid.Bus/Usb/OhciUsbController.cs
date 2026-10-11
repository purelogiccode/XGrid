using System;
using System.Collections.Generic;
using XGrid.Core.Devices;
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
/// <see cref="IUsbDeviceFunction"/> (normally an <see cref="XidGamepad"/> on port 0)
/// can be attached; the frame boundary walks the control, bulk, and periodic
/// endpoint lists and retires general transfer descriptors synchronously
/// (NoError/STALL/DataUnderrun completion codes, done-queue writeback with the
/// WDH interrupt). Isochronous transfers are not serviced.
/// </summary>
public sealed class OhciUsbController : IMmioDevice, IXboxDevice
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
    private uint _hcca;
    private uint _perCur;
    private uint _ctrlHead;
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
    private uint _doneHeadReg;
    private readonly List<string> _transferLog = new();
    private readonly Dictionary<uint, int> _regReads = new();
    private readonly Dictionary<uint, int> _regWrites = new();
    private bool _pendingSetup; private bool _pendingIn;
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

    /// <inheritdoc />
    public string Name => "OHCI USB";

    /// <summary>Gets whether the controller interrupt line is asserted, for diagnostics.</summary>
    public bool InterruptAsserted { get; private set; }

    /// <summary>Gets the raw root-hub port status register, for diagnostics.</summary>
    public uint GetPortStatus(int index)
    {
        return (uint)index < (uint)PortCount ? _ports[index] : 0;
    }

    /// <summary>Gets the number of register accesses, for diagnostics.</summary>
    public long RegisterAccesses { get; private set; }

    /// <summary>Gets the attached device function (a gamepad on port 0), if any.</summary>
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
    public uint ControlHead => _ctrlHead;

    /// <summary>Gets the programmed HCCA address, for diagnostics.</summary>
    public uint Hcca => _hcca;

    /// <summary>Gets recent retired transfer outcomes, for diagnostics.</summary>
    public IReadOnlyList<string> TransferLog => _transferLog;

    /// <summary>Gets per-offset register read counts, for diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> RegReadCounts => _regReads;

    /// <summary>Gets per-offset register write counts, for diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> RegWriteCounts => _regWrites;

    /// <summary>Raised when the interrupt line level changes.</summary>
    public event Action<bool>? InterruptChanged;

    /// <inheritdoc />
    public void Reset()
    {
        SoftResetState();
        _ctl = 0;
        _doneHeadReg = 0;
        Function?.Reset();
        RootHubReset();
    }

    /// <inheritdoc />
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
            6 => _hcca,
            7 => _perCur,
            8 => _ctrlHead,
            9 => _ctrlCur,
            10 => _bulkHead,
            11 => _bulkCur,
            12 => _doneHeadReg,
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

    /// <inheritdoc />
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
                _hcca = value & HccaMask;
                break;

            case 8:
                _ctrlHead = value & EdptrMask;
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

            default:
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
        _hcca = 0;
        _perCur = 0;
        _ctrlHead = 0;
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

            default:
                break;
        }
    }

    private void RootHubReset()
    {
        _rhdescA = RhaNps | PortCount;
        _rhdescB = 0;
        _rhstatus = 0;
        Array.Clear(_ports);
        ReportPortZeroConnect();
    }

    /// <summary>Reports the attached function as connected on port 0, if any.</summary>
    private void ReportPortZeroConnect()
    {
        // The hub reports no power switching, so port 0 is powered by default
        // and an attached function reads connected without guest power writes.
        if (_function is not null)
        {
            _ports[0] |= PortCcs | PortCsc;
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
            if (index == 0 && Function is not null)
            {
                // The attached function appears as a full-speed device once powered.
                _ports[index] |= PortCcs | PortCsc;
                SetInterrupt(IntrRhsc);
            }
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
        uint doneWriteback = 0;
        if (_doneCount == 0 && (_intrStatus & IntrWd) == 0 && _done != 0)
        {
            doneWriteback = _done;
            if ((_intr & _intrStatus) != 0)
            {
                doneWriteback |= 1;
            }

            _doneHeadReg = doneWriteback;
            _done = 0;
            _doneCount = 7;
            SetInterrupt(IntrWd);
        }
        else if (_doneCount != 7 && _doneCount != 0)
        {
            _doneCount--;
        }

        _frameNumber = (_frameNumber + 1) & 0xFFFF;
        if (_hcca != 0)
        {
            _memory.Write16(_hcca + 0x80, (ushort)_frameNumber);
            _memory.Write16(_hcca + 0x82, 0);
            _memory.Write32(_hcca + 0x84, doneWriteback);
            _done = 0;
        }

        SetInterrupt(IntrSf);
    }

    /// <summary>Walks the periodic, control, and bulk endpoint lists once per frame.</summary>
    private void ServiceLists()
    {
        if ((_ctl & CtlPle) != 0 && _hcca != 0)
        {
            uint head = _memory.Read32(_hcca + ((uint)(_frameNumber & 0x1F) * 4));
            ServiceEdList(head & DptrMask, isPeriodic: true);
        }

        if ((_ctl & CtlCle) != 0 && (_status & StatusClf) != 0 && _ctrlHead != 0)
        {
            if (!ServiceEdList(_ctrlHead, isPeriodic: false))
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
        if (Function is null || (edFlags & EdFuncMask) != Function.Address)
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
                result = ServiceSetup(cbp, out consumed);
                break;
            case 1:
                result = ServiceOut(endpoint, cbp, length, isPeriodic, out consumed);
                break;
            case 2:
                result = ServiceIn(endpoint, cbp, length, isPeriodic, (tdFlags & TdRound) != 0, out consumed);
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

        // XID-gate-II probe: short IN stages retire successfully even when the
        // guest clears the Rounding bit. Strict OHCI (and xemu) reports
        // DataUnderrun here, but the Xbox stack answers every such halt with a
        // port reset and a byte-identical re-enumeration, looping forever, so
        // strictness provably deadlocks this guest. Revisit if a title needs it.
        uint condition = result == UsbTransferResult.Ack &&
            (consumed == length || direction == 2)
            ? CcNoError
            : result == UsbTransferResult.Ack ? CcDataUnderrun : CcStall;
        if (condition == CcNoError)
        {
            // Successful stages advance the buffer pointer; a fully consumed
            // buffer retires with CBP cleared, per the OHCI retire path.
            if (((cbp & 0xFFFu) + (uint)consumed) > 0xFFFu)
            {
                cbp = (be & ~0xFFFu) + ((cbp + (uint)consumed) & 0xFFFu);
            }
            else
            {
                cbp += (uint)consumed;
            }

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
        string stage = direction == 0 ? "setup" : direction == 1 ? "out" : "in";
        string round = direction == 2 ? FormattableString.Invariant($" r={(tdFlags & TdRound) != 0}") : string.Empty;
        _transferLog.Add(FormattableString.Invariant(
            $"f={_frameNumber} {kind} {stage} ep{endpoint} len={length} got={consumed}{round} {note}").TrimEnd());
    }

    private UsbTransferResult ServiceSetup(uint cbp, out int consumed)
    {
        consumed = 8;
        var setup = new UsbSetupPacket(
            _memory.Read8(cbp),
            _memory.Read8(cbp + 1),
            _memory.Read16(cbp + 2),
            _memory.Read16(cbp + 4),
            _memory.Read16(cbp + 6));
        UsbTransferResult result = Function!.HandleSetup(in setup, out byte[] payload);
        if (result != UsbTransferResult.Ack)
        {
            return result;
        }

        _pendingSetup = true;
        _pendingIn = (setup.RequestType & 0x80) != 0 && setup.Length > 0;
        _pendingPayload = payload;
        _pendingOut = setup.Length > 0 && !_pendingIn ? new byte[setup.Length] : Array.Empty<byte>();
        _pendingOffset = 0;
        return UsbTransferResult.Ack;
    }

    private UsbTransferResult ServiceOut(uint endpoint, uint cbp, int length, bool isPeriodic, out int consumed)
    {
        consumed = length;
        if (length == 0)
        {
            // Zero-length stage completes a control transfer (status stage).
            if (_pendingSetup && !isPeriodic)
            {
                Function!.CompleteStatusStage();
                _pendingSetup = false;
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
            return Function!.HandleOut((byte)endpoint, data);
        }

        if (!_pendingSetup || _pendingIn)
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

        UsbTransferResult result = Function!.CompleteControlOut(_pendingOut);
        _pendingSetup = result == UsbTransferResult.Ack;
        return result;
    }

    private UsbTransferResult ServiceIn(
        uint endpoint, uint cbp, int length, bool isPeriodic, bool round, out int consumed)
    {
        consumed = 0;
        if (length == 0)
        {
            // Zero-length IN is the status stage of an OUT control transfer.
            if (_pendingSetup && !isPeriodic)
            {
                Function!.CompleteStatusStage();
                _pendingSetup = false;
            }

            return UsbTransferResult.Ack;
        }

        byte[] payload;
        UsbTransferResult result;
        if (isPeriodic)
        {
            result = Function!.HandleIn((byte)endpoint, length, out payload);
        }
        else
        {
            if (!_pendingSetup || !_pendingIn)
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
        uint tdAddress, uint tdFlags, uint cbp, uint tdNext, uint delay, uint condition, bool isPeriodic, ref uint edHead)
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
