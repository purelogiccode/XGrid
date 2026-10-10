using System;
using XGrid.Core.Devices;
using XGrid.Memory;

namespace XGrid.Bus.Usb;

/// <summary>Guest memory access used by the OHCI HCCA writeback.</summary>
public interface IUsbMemory
{
    /// <summary>Writes a 16-bit little-endian value.</summary>
    void Write16(uint address, ushort value);

    /// <summary>Writes a 32-bit little-endian value.</summary>
    void Write32(uint address, uint value);
}

/// <summary>
/// OHCI 1.0 host controller as found in the MCPX (PCI 0:2.0 at 0xFED00000 and
/// 0:3.0 at 0xFED08000). Register model, root hub, and interrupt semantics follow
/// xemu <c>hw/usb/hcd-ohci.c</c> (region size 256, four root ports per xemu
/// <c>hw/xbox/xbox.c</c>). No USB devices are attached, so the root hub reports
/// empty ports; frame timing is driven by <see cref="Advance"/> (1 ms frames)
/// producing SOF interrupts and HCCA writeback.
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

    // HcCommandStatus.
    private const uint StatusHcr = 1u << 0;
    private const uint StatusSoc = (1u << 6) | (1u << 7);

    // HcInterruptStatus / HcInterruptEnable.
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

    private readonly IUsbMemory _memory;
    private readonly uint[] _ports = new uint[PortCount];
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

    /// <summary>Gets the number of register accesses, for diagnostics.</summary>
    public long RegisterAccesses { get; private set; }

    /// <summary>Raised when the interrupt line level changes.</summary>
    public event Action<bool>? InterruptChanged;

    /// <inheritdoc />
    public void Reset()
    {
        SoftResetState();
        _ctl = 0;
        RootHubReset();
    }

    /// <inheritdoc />
    public uint Read(uint address, int size)
    {
        uint offset = address - BaseAddress;
        RegisterAccesses++;
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

    /// <inheritdoc />
    public void Write(uint address, int size, uint value)
    {
        uint offset = address - BaseAddress;
        RegisterAccesses++;
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
            // No device is attached, so there is nothing to reset; the port
            // reports enable plus reset-status-change as xemu does.
            _ports[index] &= ~PortPrs;
            _ports[index] |= PortPes | PortPrsc;
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

        _frt = _fit;
        _frameNumber = (_frameNumber + 1) & 0xFFFF;
        if (_hcca != 0)
        {
            _memory.Write16(_hcca + 0x80, (ushort)_frameNumber);
            _memory.Write16(_hcca + 0x82, 0);
            _memory.Write32(_hcca + 0x84, _done);
            _done = 0;
        }

        SetInterrupt(IntrSf);
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
