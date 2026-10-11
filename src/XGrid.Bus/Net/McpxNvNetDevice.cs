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
/// No DMA/packet engine, no interrupts, no link-state changes yet; KICK
/// transmits are accepted and dropped.
/// </summary>
public sealed class McpxNvNetDevice : IMmioDevice, IXboxDevice
{
    /// <summary>NVNET MMIO window base (fixed by the MCPX, not BAR-programmed by the guest).</summary>
    public const uint BaseAddress = 0xFEF00000;

    /// <summary>NVNET MMIO window size (PCI BAR0 size).</summary>
    public const uint WindowSize = 0x00000400;

    // Register offsets (xemu nvnet_regs.h).
    private const uint IrqStatus = 0x000;
    // ReSharper disable once UnusedMember.Local
    private const uint IrqMask = 0x004;
    private const uint TxStatus = 0x088;
    private const uint RxStatus = 0x098;
    private const uint MacAddrA = 0x0A8;
    private const uint MacAddrB = 0x0AC;
    private const uint SetupReg5 = 0x130;
    private const uint TxRxControl = 0x144;
    private const uint MiiStatus = 0x180;
    private const uint AdapterControl = 0x188;
    private const uint MdioAddr = 0x190;
    private const uint MdioData = 0x194;

    // Register bits (xemu nvnet_regs.h).
    private const uint TxRxKick = 1u << 0;
    private const uint TxRxBit1 = 1u << 1;
    private const uint TxRxIdle = 1u << 3;
    private const uint TxRxReset = 1u << 4;
    private const uint AdapterLinkUp = 1u << 2;
    private const uint MdioWrite = 1u << 10;
    private const uint MdioInUse = 1u << 15;
    private const uint MdioPhyRegMask = 0x1Fu;
    private const uint SetupReg5Bit31 = 1u << 31;

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

    /// <summary>Creates the nvnet controller in its power-on state.</summary>
    public McpxNvNetDevice()
    {
        Reset();
    }

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

    /// <inheritdoc />
    public void Reset()
    {
        Array.Clear(_regs, 0, _regs.Length);
        Array.Clear(_phy, 0, _phy.Length);
        _regReads.Clear();
        _regWrites.Clear();
        _regs[TxRxControl / 4] |= TxRxIdle;
        _regs[AdapterControl / 4] |= AdapterLinkUp;
        _regs[MacAddrA / 4] = MacDefaultA;
        _regs[MacAddrB / 4] = MacDefaultB;
        _phy[PhyBmcr] = PhyBmcrInit;
        _phy[PhyBmsr] = PhyBmsrInit;
        _phy[PhyAnar] = PhyAnInit;
        _phy[PhyAnlpar] = PhyAnInit;
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
        // No DMA engine exists yet, so the transmitter/receiver are always idle.
        // Forcing zero (rather than latching) keeps busy-wait loops honest.
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
                // Write-1-to-clear, xemu set_reg IRQ/MII path (nothing ever sets bits yet).
                _regs[offset >> 2] &= ~value;
                break;
            case TxRxControl:
                // IDLE is sticky (xemu set_reg_with_mask); RESET/BIT1/KICK handled below.
                _regs[offset >> 2] = (value & ~TxRxIdle) | (_regs[offset >> 2] & TxRxIdle);
                // KICK/RESET are accepted; no TX backend or descriptor rings exist yet.
                _ = value & (TxRxKick | TxRxReset);

                if ((value & TxRxBit1) != 0)
                {
                    _regs[IrqStatus >> 2] = 0;
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
                _regs[offset >> 2] = value;
                break;
        }
    }
}
