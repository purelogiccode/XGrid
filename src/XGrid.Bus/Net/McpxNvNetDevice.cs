using System;
using System.Collections.Generic;
using XGrid.Core.Devices;
using XGrid.Memory;

namespace XGrid.Bus.Net;

/// <summary>
/// MCPX nvnet (nForce Ethernet) MMIO window: PCI 0:4.0 BAR0 at 0xFEF00000,
/// 1 KB (XboxDevWiki "Memory" page: NIC registers 0xFEF00000-0xFEF003FF).
/// Register layout and reset semantics follow xemu
/// (hw/xbox/mcpx/nvnet/nvnet.c + nvnet_regs.h): TX/RX idle, TX_RX_CONTROL
/// IDLE preset, link-up steady state (as with xemu's default always-up
/// link), write-1-to-clear IRQ/MII status, instant MDIO completion.
/// Packet engine (P0 skeleton + P1 TX): guest-RAM descriptor rings
/// (8-byte LE {buf, len, flags} arrays), KICK-driven TX walk with
/// coalescing, VALID-clear writeback, TX completion IRQ, and a pluggable
/// <see cref="INetworkBackend"/> (loopback/logger default). RX (P2) and the
/// socket backend (P3) are future work: RX descriptors are latched but never
/// consumed, and no receive IRQs are ever raised.
/// </summary>
public sealed class McpxNvNetDevice : IMmioDevice, IXboxDevice
{
    /// <summary>NVNET MMIO window base (fixed by the MCPX, not BAR-programmed by the guest).</summary>
    public const uint BaseAddress = 0xFEF00000;

    /// <summary>NVNET MMIO window size (PCI BAR0 size).</summary>
    public const uint WindowSize = 0x00000400;

    // Register offsets (xemu nvnet_regs.h).
    private const uint IrqStatus = 0x000;
    private const uint IrqMask = 0x004;
    private const uint TxControl = 0x084;
    private const uint TxStatus = 0x088;
    private const uint PacketFilter = 0x08C;
    private const uint RxControl = 0x094;
    private const uint RxStatus = 0x098;
    private const uint MacAddrA = 0x0A8;
    private const uint MacAddrB = 0x0AC;
    private const uint MulticastAddrA = 0x0B0;
    private const uint MulticastAddrB = 0x0B4;
    private const uint MulticastMaskA = 0x0B8;
    private const uint MulticastMaskB = 0x0BC;
    private const uint TxRingBase = 0x100;
    private const uint RxRingBase = 0x104;
    private const uint RingSize = 0x108;
    private const uint TxRingCurrent = 0x11C;
    private const uint RxRingCurrent = 0x120;
    private const uint SetupReg5 = 0x130;
    private const uint TxRingNext = 0x134;
    private const uint RxRingNext = 0x138;
    private const uint TxRxControl = 0x144;
    private const uint MiiStatus = 0x180;
    private const uint AdapterControl = 0x188;
    private const uint MdioAddr = 0x190;
    private const uint MdioData = 0x194;

    // IRQ status bits (xemu nvnet_regs.h).
    private const uint IrqRx = 1u << 1;
    private const uint IrqRxNoBuf = 1u << 2;
    private const uint IrqTxErr = 1u << 3;
    private const uint IrqTx = 1u << 4;
    private const uint IrqTimer = 1u << 5;
    private const uint IrqMiiEvent = 1u << 6;

    // Register bits (xemu nvnet_regs.h).
    private const uint TxRxKick = 1u << 0;
    private const uint TxRxBit1 = 1u << 1;
    private const uint TxRxBit2 = 1u << 2;
    private const uint TxRxIdle = 1u << 3;
    private const uint TxRxReset = 1u << 4;
    private const uint TxStart = 1u << 0;
    private const uint AdapterLinkUp = 1u << 2;
    private const uint MdioWrite = 1u << 10;
    private const uint MdioInUse = 1u << 15;
    private const uint MdioPhyRegMask = 0x1Fu;
    private const uint SetupReg5Bit31 = 1u << 31;
    private const uint BmsrLinkStatus = 1u << 2;

    // TX descriptor flag bits (xemu nvnet_regs.h NV_TX_*).
    private const uint TxFlagLastPacket = 1u << 0;
    private const uint TxFlagRetryError = 1u << 3;
    private const uint TxFlagDeferred = 1u << 10;
    private const uint TxFlagCarrierLost = 1u << 11;
    private const uint TxFlagLateCollision = 1u << 12;
    private const uint TxFlagUnderflow = 1u << 13;
    private const uint TxFlagError = 1u << 14;
    private const uint TxFlagValid = 1u << 15;
    private const uint TxFlagsConsumed = TxFlagValid | TxFlagRetryError | TxFlagDeferred
        | TxFlagCarrierLost | TxFlagLateCollision | TxFlagUnderflow | TxFlagError;

    private const int DescriptorSize = 8;

    /// <summary>TX coalesce buffer size: default MTU (1500) + 128 slack (xemu TX_ALLOC_BUFSIZE).</summary>
    public const int TxBufferSize = 1500 + 128;

    // PHY registers (MII): xemu phy_reg_init, link-up steady state.
    private const int PhyBmcr = 0;
    private const int PhyBmsr = 1;
    private const int PhyAnar = 4;
    private const int PhyAnlpar = 5;
    private const int PhyCount = 32;
    private const ushort PhyBmcrInit = 0x1100;
    private const ushort PhyBmsrInit = 0x002C;
    private const ushort PhyAnInit = 0x03E0;

    // QEMU default MAC (52:54:00:12:34:56), as xemu reports with unset config.
    private const uint MacDefaultA = 0x12005452;
    private const uint MacDefaultB = 0x00005634;

    private readonly uint[] _regs = new uint[WindowSize / 4];
    private readonly ushort[] _phy = new ushort[PhyCount];
    private readonly Dictionary<uint, int> _regReads = new();
    private readonly Dictionary<uint, int> _regWrites = new();
    private readonly byte[] _txBuffer = new byte[TxBufferSize];
    private uint _txOffset;

    /// <summary>Creates the nvnet controller in its power-on state.</summary>
    public McpxNvNetDevice()
    {
        Backend = new LoopbackNetworkBackend();
        Reset();
    }

    /// <summary>Raised when the nvnet PCI interrupt line changes state (wired to PIC IRQ4).</summary>
    public event Action<bool>? InterruptChanged;

    /// <inheritdoc />
    public string Name => "MCPX NVNET";

    /// <summary>Gets the number of MMIO reads, for diagnostics.</summary>
    public long Reads { get; private set; }

    /// <summary>Gets the number of MMIO writes, for diagnostics.</summary>
    public long Writes { get; private set; }

    /// <summary>Gets per-register read counts, for diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> RegReadCounts => _regReads;

    /// <summary>Gets per-register write counts, for diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> RegWriteCounts => _regWrites;

    /// <summary>Gets or sets the guest-physical memory used for descriptor and packet DMA.</summary>
    public INvNetMemory? DmaMemory { get; set; }

    /// <summary>Gets or sets the network backend receiving transmitted frames (P3 seam).</summary>
    public INetworkBackend Backend { get; set; }

    /// <summary>Gets whether the nvnet interrupt line is asserted, for diagnostics.</summary>
    public bool InterruptAsserted { get; private set; }

    /// <summary>Gets the number of IRQ assert transitions, for diagnostics.</summary>
    public long IrqAsserts { get; private set; }

    /// <summary>Gets the number of IRQ deassert transitions, for diagnostics.</summary>
    public long IrqDeasserts { get; private set; }

    /// <summary>Gets the number of transmitted frames, for diagnostics.</summary>
    public long TxPackets { get; private set; }

    /// <summary>Gets the total transmitted frame bytes, for diagnostics.</summary>
    public long TxBytes { get; private set; }

    /// <summary>Gets the number of KICK doorbell writes, for diagnostics.</summary>
    public long TxKicks { get; private set; }

    /// <summary>Gets the number of KICKs dropped by the transmit gates, for diagnostics.</summary>
    public long CantTransmitCount { get; private set; }

    /// <summary>Gets the TX ring base address, for diagnostics.</summary>
    public uint TxRingBaseAddress => _regs[TxRingBase >> 2];

    /// <summary>Gets the TX current-descriptor address, for diagnostics.</summary>
    public uint TxRingCurrentAddress => _regs[TxRingCurrent >> 2];

    /// <summary>Gets the TX next-descriptor address, for diagnostics.</summary>
    public uint TxRingNextAddress => _regs[TxRingNext >> 2];

    /// <summary>Gets the TX ring descriptor count (SIZE mask + 1), for diagnostics.</summary>
    public uint TxRingCount => (_regs[RingSize >> 2] & 0xFFFFu) + 1;

    /// <summary>Gets the RX ring base address, for diagnostics.</summary>
    public uint RxRingBaseAddress => _regs[RxRingBase >> 2];

    /// <summary>Gets the RX current-descriptor address, for diagnostics.</summary>
    public uint RxRingCurrentAddress => _regs[RxRingCurrent >> 2];

    /// <summary>Gets the RX next-descriptor address, for diagnostics.</summary>
    public uint RxRingNextAddress => _regs[RxRingNext >> 2];

    /// <summary>Gets the RX ring descriptor count (SIZE mask + 1), for diagnostics.</summary>
    public uint RxRingCount => (_regs[RingSize >> 2] >> 16) + 1;

    /// <inheritdoc />
    public void Reset()
    {
        Array.Clear(_regs, 0, _regs.Length);
        Array.Clear(_phy, 0, _phy.Length);
        _regReads.Clear();
        _regWrites.Clear();
        _txOffset = 0;
        TxPackets = 0;
        TxBytes = 0;
        TxKicks = 0;
        CantTransmitCount = 0;
        _regs[TxRxControl / 4] |= TxRxIdle;
        _regs[AdapterControl / 4] |= AdapterLinkUp;
        _regs[MacAddrA / 4] = MacDefaultA;
        _regs[MacAddrB / 4] = MacDefaultB;
        _phy[PhyBmcr] = PhyBmcrInit;
        _phy[PhyBmsr] = PhyBmsrInit;
        _phy[PhyAnar] = PhyAnInit;
        _phy[PhyAnlpar] = PhyAnInit;
        UpdateIrq();
    }

    /// <summary>
    /// Reports whether a KICK would transmit: TX started, DMA enabled (BIT2 clear),
    /// and PHY link up (xemu can_transmit).
    /// </summary>
    public bool CanTransmit()
    {
        return CantTransmitReason() is null;
    }

    /// <summary>Returns the transmit-gate failure reason, or null when transmission is allowed.</summary>
    public string? CantTransmitReason()
    {
        if ((_regs[TxControl >> 2] & TxStart) == 0)
        {
            return "tx-start";
        }

        if ((_regs[TxRxControl >> 2] & TxRxBit2) != 0)
        {
            return "bit2";
        }

        if ((_phy[PhyBmsr] & BmsrLinkStatus) == 0)
        {
            return "link";
        }

        return null;
    }

    /// <inheritdoc />
    public uint Read(uint address, int size)
    {
        uint offset = address - BaseAddress;
        if (offset >= WindowSize)
        {
            return 0;
        }

        Reads++;
        uint aligned = offset & ~3u;
        _regReads.TryGetValue(aligned, out int reads);
        _regReads[aligned] = reads + 1;
        uint word = ReadReg(aligned);
        int shift = (int)(offset & 3u) * 8;
        return size switch
        {
            1 => (word >> shift) & 0xFFu,
            2 => (word >> shift) & 0xFFFFu,
            _ => word,
        };
    }

    /// <inheritdoc />
    public void Write(uint address, int size, uint value)
    {
        uint offset = address - BaseAddress;
        if (offset >= WindowSize)
        {
            return;
        }

        Writes++;
        uint aligned = offset & ~3u;
        _regWrites.TryGetValue(aligned, out int writes);
        _regWrites[aligned] = writes + 1;
        uint shift = (offset & 3u) * 8;
        uint mask = size switch
        {
            1 => 0xFFu << (int)shift,
            2 => 0xFFFFu << (int)shift,
            _ => uint.MaxValue,
        };
        uint current = _regs[aligned >> 2];
        WriteReg(aligned, (current & ~mask) | ((value << (int)shift) & mask));
    }

    private uint ReadReg(uint offset)
    {
        // No RX engine exists yet and the transmitter idles between KICKs, so the
        // busy bits always read clear. Forcing zero (rather than latching) keeps
        // busy-wait loops honest.
        if (offset == TxStatus || offset == RxStatus)
        {
            return 0;
        }

        return _regs[offset >> 2];
    }

    private void WriteReg(uint offset, uint value)
    {
        switch (offset)
        {
            case IrqStatus:
            case MiiStatus:
                // Write-1-to-clear, xemu set_reg IRQ/MII path.
                _regs[offset >> 2] &= ~value;
                UpdateIrq();
                break;
            case IrqMask:
                _regs[offset >> 2] = value;
                UpdateIrq();
                break;
            case TxRxControl:
                // IDLE is sticky (xemu set_reg_with_mask); RESET/BIT1/KICK handled below.
                _regs[offset >> 2] = (value & ~TxRxIdle) | (_regs[offset >> 2] & TxRxIdle);
                if ((value & TxRxKick) != 0)
                {
                    TransmitKick();
                }

                if ((value & TxRxReset) != 0)
                {
                    ResetRingPointers();
                }

                if ((value & TxRxBit1) != 0)
                {
                    _regs[IrqStatus >> 2] = 0;
                    UpdateIrq();
                }
                else if (value == 0)
                {
                    // Forcedeth quirk, xemu TX_RX_CONTROL handler.
                    _regs[SetupReg5 >> 2] |= SetupReg5Bit31;
                }

                break;
            case MdioAddr:
                // MDIO completes instantly (INUSE never reads set).
                _regs[offset >> 2] = value & ~MdioInUse;
                int phyReg = (int)(value & MdioPhyRegMask);
                if ((value & MdioWrite) != 0)
                {
                    _phy[phyReg] = (ushort)_regs[MdioData >> 2];
                }
                else
                {
                    _regs[MdioData >> 2] = _phy[phyReg];
                }

                break;
            default:
                // Ring pointers, TX/RX control, packet filter, multicast, MAC,
                // and setup registers are latched for the packet engine.
                _regs[offset >> 2] = value;
                break;
        }
    }

    private void ResetRingPointers()
    {
        // xemu reset_descriptor_ring_pointers: CURRENT=NEXT=BASE on RESET.
        uint txBase = _regs[TxRingBase >> 2];
        _regs[TxRingCurrent >> 2] = txBase;
        _regs[TxRingNext >> 2] = txBase;

        uint rxBase = _regs[RxRingBase >> 2];
        _regs[RxRingCurrent >> 2] = rxBase;
        _regs[RxRingNext >> 2] = rxBase;

        _txOffset = 0;
    }

    private void TransmitKick()
    {
        // xemu dma_packet_from_guest: gate, clear IDLE, walk at most TX-count
        // descriptors to LASTPACKET, coalesce len+1 bytes each, clear VALID on
        // writeback, raise the TX IRQ only when a frame completes.
        TxKicks++;
        string? blocked = CantTransmitReason();
        if (blocked is not null)
        {
            CantTransmitCount++;
            return;
        }

        SetIdle(false);
        try
        {
            if (DmaMemory is null)
            {
                // Unprogrammed (unit-test) kick: zero descriptors walked.
                return;
            }

            bool packetSent = false;
            uint count = TxRingCount;
            for (uint i = 0; i < count; i++)
            {
                uint descAddr = UpdateCurrentTx();
                LoadDescriptor(descAddr, out uint bufAddr, out uint length, out uint flags);
                if ((flags & TxFlagValid) == 0)
                {
                    break;
                }

                if ((ulong)_txOffset + length > (ulong)_txBuffer.Length)
                {
                    // xemu asserts here; stop the walk instead of overrunning.
                    break;
                }

                for (uint b = 0; b < length; b++)
                {
                    _txBuffer[_txOffset + b] = DmaMemory.ReadPhysical(bufAddr + b);
                }

                _txOffset += length;

                bool lastPacket = (flags & TxFlagLastPacket) != 0;
                if (lastPacket)
                {
                    Backend.SendPacket(new ReadOnlySpan<byte>(_txBuffer, 0, (int)_txOffset));
                    TxPackets++;
                    TxBytes += _txOffset;
                    _txOffset = 0;
                    packetSent = true;
                }

                StoreDescriptor(descAddr, bufAddr, (ushort)(length - 1), flags & ~TxFlagsConsumed);
                AdvanceNextTx();

                if (lastPacket)
                {
                    break;
                }
            }

            if (packetSent)
            {
                _regs[IrqStatus >> 2] |= IrqTx;
                UpdateIrq();
            }
        }
        finally
        {
            SetIdle(true);
        }
    }

    private uint UpdateCurrentTx()
    {
        // xemu update_current_tx_ring_desc_addr: NEXT clamped into the ring.
        uint baseAddr = _regs[TxRingBase >> 2];
        ulong span = (ulong)TxRingCount * DescriptorSize;
        uint next = _regs[TxRingNext >> 2];
        uint current = next;
        if (current < baseAddr || (ulong)current + DescriptorSize > (ulong)baseAddr + span)
        {
            current = baseAddr;
        }

        _regs[TxRingCurrent >> 2] = current;
        return current;
    }

    private void AdvanceNextTx()
    {
        // xemu advance_next_tx_ring_desc_addr: CURRENT+8 with wrap to BASE.
        uint baseAddr = _regs[TxRingBase >> 2];
        ulong span = (ulong)TxRingCount * DescriptorSize;
        uint next = _regs[TxRingCurrent >> 2] + DescriptorSize;
        if ((ulong)next >= (ulong)baseAddr + span)
        {
            next = baseAddr;
        }

        _regs[TxRingNext >> 2] = next;
    }

    private void LoadDescriptor(uint descAddr, out uint bufAddr, out uint length, out uint flags)
    {
        INvNetMemory memory = DmaMemory!;
        uint b0 = memory.ReadPhysical(descAddr);
        uint b1 = memory.ReadPhysical(descAddr + 1);
        uint b2 = memory.ReadPhysical(descAddr + 2);
        uint b3 = memory.ReadPhysical(descAddr + 3);
        uint l0 = memory.ReadPhysical(descAddr + 4);
        uint l1 = memory.ReadPhysical(descAddr + 5);
        uint f0 = memory.ReadPhysical(descAddr + 6);
        uint f1 = memory.ReadPhysical(descAddr + 7);
        bufAddr = b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        // xemu TX bytes: descriptor length field + 1.
        length = (l0 | (l1 << 8)) + 1;
        flags = f0 | (f1 << 8);
    }

    private void StoreDescriptor(uint descAddr, uint bufAddr, ushort length, uint flags)
    {
        INvNetMemory memory = DmaMemory!;
        memory.WritePhysical(descAddr, (byte)bufAddr);
        memory.WritePhysical(descAddr + 1, (byte)(bufAddr >> 8));
        memory.WritePhysical(descAddr + 2, (byte)(bufAddr >> 16));
        memory.WritePhysical(descAddr + 3, (byte)(bufAddr >> 24));
        memory.WritePhysical(descAddr + 4, (byte)length);
        memory.WritePhysical(descAddr + 5, (byte)(length >> 8));
        memory.WritePhysical(descAddr + 6, (byte)flags);
        memory.WritePhysical(descAddr + 7, (byte)(flags >> 8));
    }

    private void SetIdle(bool idle)
    {
        if (idle)
        {
            _regs[TxRxControl >> 2] |= TxRxIdle;
        }
        else
        {
            _regs[TxRxControl >> 2] &= ~TxRxIdle;
        }
    }

    private void UpdateIrq()
    {
        // xemu update_irq: assert when any status bit is masked on.
        bool asserted = (_regs[IrqStatus >> 2] & _regs[IrqMask >> 2]) != 0;
        if (asserted == InterruptAsserted)
        {
            return;
        }

        InterruptAsserted = asserted;
        if (asserted)
        {
            IrqAsserts++;
        }
        else
        {
            IrqDeasserts++;
        }

        InterruptChanged?.Invoke(asserted);
    }
}
