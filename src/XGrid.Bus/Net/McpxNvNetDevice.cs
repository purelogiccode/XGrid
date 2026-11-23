using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;
using XGrid.Memory;

namespace XGrid.Bus.Net;

/// <summary>
/// MCPX nvnet (nForce Ethernet) MMIO window: PCI 0:4.0 BAR0 at 0xFEF00000,
/// 1 KB (XboxDevWiki "Memory" page: NIC registers 0xFEF00000-0xFEF003FF).
/// Register layout and reset semantics follow xemu
/// (hw/xbox/mcpx/nvnet/nvnet.c + nvnet_regs.h): TX/RX idle, TX_RX_CONTROL
/// IDLE preset, link-up steady state (as with xemu's default always-up
/// link), write-1-to-clear IRQ/MII status, instant MDIO completion.
/// Packet engine (P0 skeleton + P1 TX + P2 RX + P3 link/backend): guest-RAM
/// descriptor rings (8-byte LE {buf, len, flags} arrays), KICK-driven TX walk
/// with coalescing, VALID-clear writeback, TX completion IRQ, RX filter +
/// DMA-write with RX/RX_NOBUF IRQs (polled from the backend via
/// <see cref="PollReceive"/> on <see cref="Advance"/>), link toggle with
/// 250 ms autoneg, and a pluggable <see cref="INetworkBackend"/>
/// (loopback/logger default, socket user-mode backend behind a CLI flag).
/// </summary>
public sealed class McpxNvNetDevice : IMmioDevice, IXboxDevice, ISnapshotable
{
    /// <summary>NVNET MMIO window base (fixed by the MCPX, not BAR-programmed by the guest).</summary>
    public const uint BaseAddress = 0xFEF00000;

    /// <summary>NVNET MMIO window size (PCI BAR0 size).</summary>
    private const uint WindowSize = 0x00000400;

    // Register offsets (xemu nvnet_regs.h).
    private const uint IrqStatus = 0x000;

    /// <summary>Word index of <see cref="IrqStatus"/> (<c>IrqStatus >> 2</c>; the offset is zero).</summary>
    private const int IrqStatusIndex = 0;
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
    private const uint IrqTx = 1u << 4;
    private const uint IrqMiiEvent = 1u << 6;

    // Register bits (xemu nvnet_regs.h).
    private const uint TxRxKick = 1u << 0;
    private const uint TxRxBit1 = 1u << 1;
    private const uint TxRxBit2 = 1u << 2;
    private const uint TxRxIdle = 1u << 3;
    private const uint TxRxReset = 1u << 4;
    private const uint TxStart = 1u << 0;
    private const uint RxStart = 1u << 0;
    private const uint PacketFilterMyAddr = 1u << 5;
    private const uint AdapterLinkUp = 1u << 2;
    private const uint MiiLinkChange = 1u << 3;
    private const uint MdioWrite = 1u << 10;
    private const uint MdioInUse = 1u << 15;
    private const uint MdioPhyRegMask = 0x1Fu;
    private const uint SetupReg5Bit31 = 1u << 31;
    private const uint BmsrLinkStatus = 1u << 2;
    private const uint BmsrAnComp = 1u << 5;
    private const uint BmcrAutoEn = 1u << 12;
    private const uint AnlparAck = 1u << 14;

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
    private const int TxBufferSize = 1500 + 128;

    /// <summary>RX frame size limit: default MTU (1500) + 128 slack (xemu RX_ALLOC_BUFSIZE).</summary>
    public const int RxBufferSize = 1500 + 128;

    /// <summary>Link autonegotiation delay in milliseconds (xemu AUTONEG_DURATION_MS).</summary>
    private const ulong AutonegDelayMs = 250;

    // RX descriptor flag bits (xemu nvnet_regs.h NV_RX_*).
    private const uint RxFlagDescriptorValid = 1u << 0;
    private const uint RxFlagBit4 = 1u << 4;
    private const uint RxFlagAvail = 1u << 15;

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
    private readonly Dictionary<uint, int> _regReads = [];
    private readonly Dictionary<uint, int> _regWrites = [];
    private readonly byte[] _txBuffer = new byte[TxBufferSize];
    private uint _txOffset;
    private byte[]? _pendingFrame;
    private ulong _autoNegElapsedMs;
    private ulong _autoNegRemainder;

    /// <summary>Creates the nvnet controller in its power-on state.</summary>
    public McpxNvNetDevice()
    {
        Backend = new LoopbackNetworkBackend();
        Reset();
    }

    /// <summary>Raised when the nvnet PCI interrupt line changes state (wired to PIC IRQ4).</summary>
    public event Action<bool>? InterruptChanged;

    /// <summary>Gets the device name used in traces and diagnostics ("MCPX NVNET").</summary>
#pragma warning disable CA1822
    public string Name => "MCPX NVNET";
#pragma warning restore CA1822

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

    /// <summary>Gets the number of received frames DMA-written to the guest, for diagnostics.</summary>
    public long RxPackets { get; private set; }

    /// <summary>Gets the total received frame bytes, for diagnostics.</summary>
    public long RxBytes { get; private set; }

    /// <summary>Gets the number of received frames dropped by the receive filter, for diagnostics.</summary>
    public long RxDropFilter { get; private set; }

    /// <summary>Gets the number of received frames dropped for lack of an AVAIL buffer, for diagnostics.</summary>
    public long RxDropNoBuf { get; private set; }

    /// <summary>Gets the number of received frames dropped as oversized, for diagnostics.</summary>
    public long RxOversize { get; private set; }

    /// <summary>Gets the number of injected frames blocked by the receive gates, for diagnostics.</summary>
    public long CantReceiveCount { get; private set; }

    /// <summary>Gets the number of link up/down transitions, for diagnostics.</summary>
    public long LinkChanges { get; private set; }

    /// <summary>Gets whether the PHY reports link up, for diagnostics.</summary>
    public bool LinkUp => (_phy[PhyBmsr] & BmsrLinkStatus) != 0;

    /// <summary>Gets whether a 250 ms autonegotiation is in flight, for diagnostics.</summary>
    public bool AutoNegPending { get; private set; }

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

    /// <summary>Returns the nvnet controller to its power-on state: clears registers, PHY, and
    /// diagnostics counters, drops any pending frame, then applies xemu's reset defaults
    /// (TX_RX_CONTROL IDLE preset, adapter link-up, default MAC, PHY init values) and
    /// recomputes the interrupt line.</summary>
    public void Reset()
    {
        Array.Clear(_regs, 0, _regs.Length);
        Array.Clear(_phy, 0, _phy.Length);
        _regReads.Clear();
        _regWrites.Clear();
        _txOffset = 0;
        _pendingFrame = null;
        AutoNegPending = false;
        _autoNegElapsedMs = 0;
        _autoNegRemainder = 0;
        TxPackets = 0;
        TxBytes = 0;
        TxKicks = 0;
        CantTransmitCount = 0;
        RxPackets = 0;
        RxBytes = 0;
        RxDropFilter = 0;
        RxDropNoBuf = 0;
        RxOversize = 0;
        CantReceiveCount = 0;
        LinkChanges = 0;
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

    /// <summary>Saves registers, PHY, rings, TX coalescing, autoneg, counters, and the backend.</summary>
    /// <remarks>Snapshot is valid only with the loopback backend: socket state (host sockets,
    /// NAT mappings) is not restorable. Save throws when the backend is not loopback, and load
    /// throws when the live backend is not loopback. The guest-memory adapter is host wiring.</remarks>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (Backend is not LoopbackNetworkBackend loopback)
        {
            throw new InvalidDataException(
                $"nvnet snapshot requires the loopback backend (live={Backend.GetType().Name}). Sockets are not restorable.");
        }

        writer.Write(SnapshotVersion);
        for (int i = 0; i < _regs.Length; i++)
        {
            writer.Write(_regs[i]);
        }

        for (int i = 0; i < _phy.Length; i++)
        {
            writer.Write(_phy[i]);
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

        writer.Write(_txBuffer, 0, _txBuffer.Length);
        writer.Write(_txOffset);
        writer.Write(_pendingFrame is not null);
        if (_pendingFrame is not null)
        {
            writer.Write(_pendingFrame.Length);
            writer.Write(_pendingFrame);
        }

        writer.Write(AutoNegPending);
        writer.Write(_autoNegElapsedMs);
        writer.Write(_autoNegRemainder);
        writer.Write(Reads);
        writer.Write(Writes);
        writer.Write(InterruptAsserted);
        writer.Write(IrqAsserts);
        writer.Write(IrqDeasserts);
        writer.Write(TxPackets);
        writer.Write(TxBytes);
        writer.Write(TxKicks);
        writer.Write(CantTransmitCount);
        writer.Write(RxPackets);
        writer.Write(RxBytes);
        writer.Write(RxDropFilter);
        writer.Write(RxDropNoBuf);
        writer.Write(RxOversize);
        writer.Write(CantReceiveCount);
        writer.Write(LinkChanges);
        loopback.SaveState(writer);
    }

    /// <summary>Restores registers, PHY, rings, TX coalescing, autoneg, counters, and the backend.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (Backend is not LoopbackNetworkBackend loopback)
        {
            throw new InvalidDataException(
                $"nvnet snapshot load requires the loopback backend (live={Backend.GetType().Name}). Sockets are not restorable.");
        }

        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported nvnet snapshot version {version}.");
        }

        for (int i = 0; i < _regs.Length; i++)
        {
            _regs[i] = reader.ReadUInt32();
        }

        for (int i = 0; i < _phy.Length; i++)
        {
            _phy[i] = reader.ReadUInt16();
        }

        int reads = reader.ReadInt32();
        if (reads < 0)
        {
            throw new InvalidDataException("Negative nvnet read histogram snapshot.");
        }

        _regReads.Clear();
        for (int i = 0; i < reads; i++)
        {
            _regReads[reader.ReadUInt32()] = reader.ReadInt32();
        }

        int writes = reader.ReadInt32();
        if (writes < 0)
        {
            throw new InvalidDataException("Negative nvnet write histogram snapshot.");
        }

        _regWrites.Clear();
        for (int i = 0; i < writes; i++)
        {
            _regWrites[reader.ReadUInt32()] = reader.ReadInt32();
        }

        byte[] tx = reader.ReadBytes(_txBuffer.Length);
        if (tx.Length != _txBuffer.Length)
        {
            throw new InvalidDataException("Truncated nvnet TX buffer snapshot.");
        }

        Array.Copy(tx, _txBuffer, _txBuffer.Length);
        _txOffset = reader.ReadUInt32();
        if (reader.ReadBoolean())
        {
            int pending = reader.ReadInt32();
            if (pending < 0 || pending > RxBufferSize)
            {
                throw new InvalidDataException("Bad nvnet pending-frame snapshot.");
            }

            _pendingFrame = reader.ReadBytes(pending);
            if (_pendingFrame.Length != pending)
            {
                throw new InvalidDataException("Truncated nvnet pending-frame snapshot.");
            }
        }
        else
        {
            _pendingFrame = null;
        }

        AutoNegPending = reader.ReadBoolean();
        _autoNegElapsedMs = reader.ReadUInt64();
        _autoNegRemainder = reader.ReadUInt64();
        Reads = reader.ReadInt64();
        Writes = reader.ReadInt64();
        InterruptAsserted = reader.ReadBoolean();
        IrqAsserts = reader.ReadInt64();
        IrqDeasserts = reader.ReadInt64();
        TxPackets = reader.ReadInt64();
        TxBytes = reader.ReadInt64();
        TxKicks = reader.ReadInt64();
        CantTransmitCount = reader.ReadInt64();
        RxPackets = reader.ReadInt64();
        RxBytes = reader.ReadInt64();
        RxDropFilter = reader.ReadInt64();
        RxDropNoBuf = reader.ReadInt64();
        RxOversize = reader.ReadInt64();
        CantReceiveCount = reader.ReadInt64();
        LinkChanges = reader.ReadInt64();
        loopback.LoadState(reader);
    }

    private const int SnapshotVersion = 1;

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

        return (_regs[TxRxControl >> 2] & TxRxBit2) != 0 ? "bit2" : (_phy[PhyBmsr] & BmsrLinkStatus) == 0 ? "link" : null;
    }

    /// <summary>
    /// Reports whether a received frame can be DMA-written: RX started, DMA
    /// enabled (BIT2 clear), PHY link up, and an AVAIL descriptor posted
    /// (xemu nvnet_can_receive).
    /// </summary>
    public bool CanReceive()
    {
        return CanReceiveReason() is null;
    }

    /// <summary>Returns the receive-gate failure reason, or null when reception is allowed.</summary>
    public string? CanReceiveReason()
    {
        string? gate = GateReason();
        if (gate is not null)
        {
            return gate;
        }

        if (DmaMemory is null)
        {
            return "no-buf";
        }

        LoadDescriptor(PeekCurrentRx(), out _, out _, out uint flags);
        return (flags & RxFlagAvail) != 0 ? null : "no-buf";
    }

    /// <summary>
    /// Receive gates without the buffer check (RX started, DMA enabled, link up).
    /// The no-buf miss is detected at DMA time so a deliverable frame raises the
    /// RX_NOBUF IRQ instead of vanishing.
    /// </summary>
    private string? GateReason()
    {
        if ((_regs[RxControl >> 2] & RxStart) == 0)
        {
            return "rx-start";
        }

        return (_regs[TxRxControl >> 2] & TxRxBit2) != 0 ? "bit2" : (_phy[PhyBmsr] & BmsrLinkStatus) == 0 ? "link" : null;
    }

    /// <summary>
    /// Receive filter (xemu receive_filter): broadcast always passes; when the
    /// MYADDR packet-filter bit is clear everything passes; otherwise the
    /// masked-multicast compare runs first, then the unicast MAC compare. The
    /// multicast path is skipped only when the programmed address is broadcast,
    /// so reset-zero address/mask registers match every frame (xemu-literal).
    /// </summary>
    public bool PassesFilter(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 6)
        {
            return false;
        }

        if (IsBroadcast(frame))
        {
            return true;
        }

        if ((_regs[PacketFilter >> 2] & PacketFilterMyAddr) == 0)
        {
            return true;
        }

        Span<byte> programmed = stackalloc byte[6];
        ReadMacRegs(MulticastAddrA, MulticastAddrB, programmed);
        if (!IsBroadcast(programmed))
        {
            Span<byte> mask = stackalloc byte[6];
            ReadMacRegs(MulticastMaskA, MulticastMaskB, mask);
            bool match = true;
            for (int i = 0; i < 6; i++)
            {
                if ((byte)(frame[i] & mask[i]) != programmed[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        Span<byte> mac = stackalloc byte[6];
        ReadMacRegs(MacAddrA, MacAddrB, mac);
        for (int i = 0; i < 6; i++)
        {
            if (frame[i] != mac[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// One-shot host injection running the wire pipeline (oversize drop, filter,
    /// receive gates, RX DMA). Returns true when the frame reached guest memory.
    /// A no-buf frame raises RX_NOBUF and is stashed for a later
    /// <see cref="PollReceive"/>; gate-blocked frames are counted, never retried.
    /// </summary>
    public bool InjectFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length > RxBufferSize)
        {
            RxOversize++;
            return false;
        }

        if (!PassesFilter(frame))
        {
            RxDropFilter++;
            return false;
        }

        if (GateReason() is not null || DmaMemory is null)
        {
            CantReceiveCount++;
            return false;
        }

        if (!TryDmaFrameToGuest(frame))
        {
            RaiseNoBuf(frame);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Drains queued backend frames through the RX engine (oversize drop, filter,
    /// RX DMA with RX/RX_NOBUF IRQs). A frame that meets no AVAIL buffer raises
    /// RX_NOBUF and is held in the pending slot (QEMU hold-and-retry), delivered
    /// on a later call once the guest posts buffers. Returns the frames delivered.
    /// </summary>
    public int PollReceive()
    {
        int delivered = 0;
        if (_pendingFrame is not null)
        {
            if (GateReason() is not null)
            {
                return 0;
            }

            if (!TryDmaFrameToGuest(_pendingFrame))
            {
                return 0;
            }

            _pendingFrame = null;
            delivered++;
        }

        while (GateReason() is null && DmaMemory is not null)
        {
            if (!Backend.TryReceive(out byte[]? frame) || frame is null)
            {
                break;
            }

            if (frame.Length > RxBufferSize)
            {
                RxOversize++;
                continue;
            }

            if (!PassesFilter(frame))
            {
                RxDropFilter++;
                continue;
            }

            if (!TryDmaFrameToGuest(frame))
            {
                RaiseNoBuf(frame);
                break;
            }

            delivered++;
        }

        return delivered;
    }

    /// <summary>
    /// Sets the link state. Down applies immediately; up completes immediately
    /// unless autonegotiation is enabled and incomplete, in which case the link
    /// comes up after 250 ms of <see cref="Advance"/> (xemu restart_autoneg).
    /// Both transitions raise MII LINKCHANGE + the MIIEVENT IRQ.
    /// </summary>
    public void SetLinkUp(bool up)
    {
        if (up)
        {
            if (LinkUp)
            {
                AutoNegPending = false;
                _autoNegElapsedMs = 0;
                _autoNegRemainder = 0;
                return;
            }

            if ((_phy[PhyBmcr] & BmcrAutoEn) != 0 && (_phy[PhyBmsr] & BmsrAnComp) == 0)
            {
                AutoNegPending = true;
                _autoNegElapsedMs = 0;
                _autoNegRemainder = 0;
            }
            else
            {
                CompleteLinkUp(negotiated: false);
            }
        }
        else
        {
            AutoNegPending = false;
            _autoNegElapsedMs = 0;
            _autoNegRemainder = 0;
            if (!LinkUp)
            {
                return;
            }

            ApplyLinkDown();
        }
    }

    /// <summary>
    /// Advances the 250 ms autonegotiation timer and drains queued backend
    /// frames through the RX engine. Called from the machine advance path.
    /// </summary>
    public void Advance(ulong cycles)
    {
        if (AutoNegPending)
        {
            _autoNegRemainder += cycles * 1000;
            ulong ms = _autoNegRemainder / XboxBus.CpuClockHz;
            _autoNegRemainder %= XboxBus.CpuClockHz;
            _autoNegElapsedMs += ms;
            if (_autoNegElapsedMs >= AutonegDelayMs)
            {
                _autoNegElapsedMs = 0;
                _autoNegRemainder = 0;
                CompleteLinkUp(negotiated: true);
            }
        }

        PollReceive();
    }

    /// <summary>Reads a register in the 1 KB nvnet window at 0xFEF00000: the aligned 32-bit
    /// register word is selected for the given 1/2/4-byte access at the unaligned offset, counting
    /// the access in <see cref="Reads"/> and the per-register histogram; addresses outside the
    /// window read as 0.</summary>
    /// <param name="address">Absolute address inside the window (base 0xFEF00000).</param>
    /// <param name="size">Access width in bytes (1, 2, or 4).</param>
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

    /// <summary>Writes a register in the 1 KB nvnet window at 0xFEF00000: the value is merged into
    /// the aligned register word for the requested byte lane (partial writes update only their
    /// bytes) and routed through the register write side effects, counting the access in
    /// <see cref="Writes"/>; addresses outside the window are ignored.</summary>
    /// <param name="address">Absolute address inside the window (base 0xFEF00000).</param>
    /// <param name="size">Access width in bytes (1, 2, or 4).</param>
    /// <param name="value">Value to write, right-aligned in the low bytes.</param>
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
        return offset == TxStatus || offset == RxStatus ? 0 : _regs[offset >> 2];
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
                    _regs[IrqStatusIndex] = 0;
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
                _regs[IrqStatusIndex] |= IrqTx;
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
        if (current < baseAddr || (ulong)current + DescriptorSize > baseAddr + span)
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
        if (next >= baseAddr + span)
        {
            next = baseAddr;
        }

        _regs[TxRingNext >> 2] = next;
    }

    private uint PeekCurrentRx()
    {
        // Clamped NEXT read without latching CURRENT (side-effect-free probe
        // for CanReceive; xemu's rx_buf_available latches instead).
        uint baseAddr = _regs[RxRingBase >> 2];
        ulong span = (ulong)RxRingCount * DescriptorSize;
        uint next = _regs[RxRingNext >> 2];
        if (next < baseAddr || (ulong)next + DescriptorSize > baseAddr + span)
        {
            next = baseAddr;
        }

        return next;
    }

    private uint UpdateCurrentRx()
    {
        // xemu update_current_rx_ring_desc_addr: NEXT clamped into the ring.
        uint current = PeekCurrentRx();
        _regs[RxRingCurrent >> 2] = current;
        return current;
    }

    private void AdvanceNextRx()
    {
        // xemu advance_next_rx_ring_desc_addr: CURRENT+8 with wrap to BASE.
        uint baseAddr = _regs[RxRingBase >> 2];
        ulong span = (ulong)RxRingCount * DescriptorSize;
        uint next = _regs[RxRingCurrent >> 2] + DescriptorSize;
        if (next >= baseAddr + span)
        {
            next = baseAddr;
        }

        _regs[RxRingNext >> 2] = next;
    }

    private void RaiseNoBuf(ReadOnlySpan<byte> frame)
    {
        // Deliberate deviation: xemu's dma_packet_to_guest returns -1 silently
        // when no buffer is available (QEMU holds the packet). The forcedeth
        // driver replenishes rings off the RX_NOBUF interrupt, so a silent drop
        // would wedge it; raise the specified IRQ and hold the frame instead.
        _regs[IrqStatusIndex] |= IrqRxNoBuf;
        UpdateIrq();
        RxDropNoBuf++;
        _pendingFrame ??= frame.ToArray();
    }

    private bool TryDmaFrameToGuest(ReadOnlySpan<byte> frame)
    {
        // xemu dma_packet_to_guest: AVAIL consume, DMA-write into desc.buf,
        // len=size writeback, flags=BIT4|DESCRIPTORVALID, RX IRQ, advance.
        // The xemu (len+1)>=size assert becomes a no-buf miss here.
        if (DmaMemory is null)
        {
            return false;
        }

        uint descAddr = UpdateCurrentRx();
        LoadDescriptor(descAddr, out uint bufAddr, out uint capacity, out uint flags);
        if ((flags & RxFlagAvail) == 0 || capacity < (uint)frame.Length)
        {
            return false;
        }

        INvNetMemory memory = DmaMemory;
        for (int i = 0; i < frame.Length; i++)
        {
            memory.WritePhysical(bufAddr + (uint)i, frame[i]);
        }

        StoreDescriptor(descAddr, bufAddr, (ushort)frame.Length, RxFlagBit4 | RxFlagDescriptorValid);
        AdvanceNextRx();
        RxPackets++;
        RxBytes += frame.Length;
        _regs[IrqStatusIndex] |= IrqRx;
        UpdateIrq();
        return true;
    }

    private void ApplyLinkDown()
    {
        // xemu update_regs_on_link_down + set_link_down.
        _phy[PhyBmsr] = (ushort)(_phy[PhyBmsr] & ~(BmsrLinkStatus | BmsrAnComp));
        _phy[PhyAnlpar] = (ushort)(_phy[PhyAnlpar] & ~AnlparAck);
        _regs[AdapterControl >> 2] &= ~AdapterLinkUp;
        _regs[MiiStatus >> 2] |= MiiLinkChange;
        _regs[IrqStatusIndex] |= IrqMiiEvent;
        LinkChanges++;
        UpdateIrq();
    }

    private void CompleteLinkUp(bool negotiated)
    {
        // xemu autoneg_done + update_regs_on_link_up + set_link_up.
        if (negotiated)
        {
            _phy[PhyAnlpar] = (ushort)(_phy[PhyAnlpar] | AnlparAck);
            _phy[PhyBmsr] = (ushort)(_phy[PhyBmsr] | BmsrAnComp);
        }

        _phy[PhyBmsr] = (ushort)(_phy[PhyBmsr] | BmsrLinkStatus);
        _regs[AdapterControl >> 2] |= AdapterLinkUp;
        _regs[MiiStatus >> 2] |= MiiLinkChange;
        _regs[IrqStatusIndex] |= IrqMiiEvent;
        AutoNegPending = false;
        LinkChanges++;
        UpdateIrq();
    }

    private static bool IsBroadcast(ReadOnlySpan<byte> address)
    {
        for (int i = 0; i < 6; i++)
        {
            if (address[i] != 0xFF)
            {
                return false;
            }
        }

        return true;
    }

    private void ReadMacRegs(uint addrA, uint addrB, Span<byte> address)
    {
        // xemu compares the destination MAC against the LE register pair
        // (first four bytes from A, last two from B) in memory order.
        uint regA = _regs[addrA >> 2];
        uint regB = _regs[addrB >> 2];
        address[0] = (byte)regA;
        address[1] = (byte)(regA >> 8);
        address[2] = (byte)(regA >> 16);
        address[3] = (byte)(regA >> 24);
        address[4] = (byte)regB;
        address[5] = (byte)(regB >> 8);
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
        bool asserted = (_regs[IrqStatusIndex] & _regs[IrqMask >> 2]) != 0;
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
