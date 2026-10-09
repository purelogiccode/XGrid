using System;
using System.Collections.Generic;
using XGrid.Core.Devices;
using XGrid.Memory;

namespace XGrid.Nv2a;

/// <summary>
/// NV2A GPU control register window: 16 MB at 0xFD000000 (BAR0).
/// Block map and reset defaults follow xemu <c>hw/xbox/nv2a/</c> (blocktable in
/// <c>nv2a.c</c>, per-block read/write in <c>pmc.c</c>/<c>pbus.c</c>/<c>pfb.c</c>/
/// <c>pcrtc.c</c>/<c>pramdac.c</c>/<c>ptimer.c</c>/<c>user.c</c>, zero-read stubs in
/// <c>stubs.c</c>); PFB CFG0 reset default 0x03070103 is the 64 MB hardware value
/// recorded in the Xermu notes. PFIFO/PGRAPH command execution and interrupt
/// assertion through the PIC are deferred: pending/enabled state is tracked but
/// no IRQ is raised yet.
/// </summary>
public sealed class Nv2aMmio : IMmioDevice, IXboxDevice
{
    /// <summary>NV2A control window base (BAR0).</summary>
    public const uint BaseAddress = 0xFD000000u;

    /// <summary>NV2A control window size (16 MB).</summary>
    public const uint WindowSize = 0x01000000u;

    /// <summary>NV2A core clock driving PTIMER (Hz, xemu reset default).</summary>
    public const ulong CoreClockHz = 233333324UL;

    private const uint PmcBase = 0x000000u;
    private const uint PbusBase = 0x001000u;
    private const uint PfifoBase = 0x002000u;
    private const uint PfifoSize = 0x2000u;
    private const uint PvideoBase = 0x008000u;
    private const uint PtimerBase = 0x009000u;
    private const uint PfbBase = 0x100000u;
    private const uint PgraphBase = 0x400000u;
    private const uint PgraphSize = 0x2000u;
    private const uint PcrtcBase = 0x600000u;
    private const uint PramdacBase = 0x680000u;
    private const uint PraminBase = 0x700000u;
    private const uint PraminSize = 0x100000u;
    private const uint UserBase = 0x800000u;
    private const uint UserSize = 0x800000u;

    private const uint PmcBoot0 = 0x02A000A3u;
    private const uint PmcIntr0 = 0x100u;
    private const uint PmcIntrEn0 = 0x140u;

    private const uint PbusPciNv0 = 0x0u;
    private const uint PbusPciNv1 = 0x4u;
    private const uint PbusPciNv2 = 0x8u;
    private const uint PbusIdValue = 0x02A010DEu;
    private const uint PbusClassValue = 0x030000A1u;

    private const uint PfifoCache1Status = 0x1214u;
    private const uint PfifoCache1StatusLowMark = 0x10u;
    private const uint PfifoMode = 0x0504u;
    private const uint PfifoCache1Push1 = 0x1204u;
    private const uint PfifoCache1DmaPut = 0x1240u;
    private const uint PfifoCache1DmaGet = 0x1244u;
    private const uint PfifoCache1Ref = 0x1248u;

    private const uint PtimerIntr0 = 0x100u;
    private const uint PtimerIntrEn0 = 0x140u;
    private const uint PtimerNumerator = 0x200u;
    private const uint PtimerDenominator = 0x210u;
    private const uint PtimerTime0 = 0x400u;
    private const uint PtimerTime1 = 0x410u;

    private const uint PfbCfg0 = 0x200u;
    private const uint PfbCfg0Reset = 0x03070103u;
    private const uint PfbCstatus = 0x20Cu;
    private const uint PfbWbc = 0x30Cu;

    private const uint PcrtcIntr0 = 0x100u;
    private const uint PcrtcIntrEn0 = 0x140u;
    private const uint PcrtcStart = 0x800u;
    private const uint PcrtcRaster = 0x808u;

    private const uint PramdacNvpllCoeff = 0x500u;
    private const uint PramdacNvpllReset = 0x00011C01u;
    private const uint PramdacMpllCoeff = 0x504u;
    private const uint PramdacVpllCoeff = 0x508u;
    private const uint PramdacVpllReset = 0x0003C20Du;
    private const uint PramdacPllTest = 0x514u;
    private const uint PramdacPllLocks = 0xD8000000u;
    private const uint PramdacGeneralControl = 0x600u;

    private const uint UserDmaPut = 0x40u;
    private const uint UserDmaGet = 0x44u;
    private const uint UserRef = 0x48u;

    private readonly string _name;
    private readonly uint[] _pfifo = new uint[PfifoSize / 4];
    private readonly uint[] _pvideo = new uint[0x1000 / 4];
    private readonly uint[] _pfb = new uint[0x1000 / 4];
    private readonly uint[] _pgraph = new uint[PgraphSize / 4];
    private readonly byte[] _ramin = new byte[PraminSize];
    private readonly Dictionary<uint, int> _readCounts = new();
    private readonly Dictionary<uint, int> _writeCounts = new();

    private uint _pmcEnabled;
    private uint _pmcPending;
    private uint _pbusCommand;
    private uint _ptimerPending;
    private uint _ptimerEnabled;
    private uint _ptimerNumerator;
    private uint _ptimerDenominator;
    private ulong _ptimerClock;
    private uint _pcrtcPending;
    private uint _pcrtcEnabled;
    private uint _pcrtcStart;
    private uint _pcrtcRaster;
    private uint _nvpllCoeff = PramdacNvpllReset;
    private uint _mpllCoeff;
    private uint _vpllCoeff = PramdacVpllReset;
    private uint _generalControl;
    private uint _pgraphPending;
    private uint _pgraphEnabled;
    private uint _pfifoPending;
    private uint _pfifoEnabled;

    /// <summary>Creates the NV2A register window for the given RAM size in bytes.</summary>
    public Nv2aMmio(uint ramSize = 64u * 1024 * 1024, string name = "NV2A")
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        RamSize = ramSize;
        _name = name;
        Reset();
    }

    /// <summary>Gets the device name used in traces and diagnostics.</summary>
    public string Name => _name;

    /// <summary>Gets the guest RAM size reported by PFB CSTATUS.</summary>
    public uint RamSize { get; }

    /// <summary>Gets read counts keyed by register-block base offset. Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> ReadCounts => _readCounts;

    /// <summary>Gets write counts keyed by register-block base offset. Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> WriteCounts => _writeCounts;

    /// <summary>Advances the PTIMER free-running counter by the given core-clock ticks.</summary>
    public void Advance(ulong coreTicks)
    {
        _ptimerClock += coreTicks;
    }

    /// <inheritdoc />
    public void Reset()
    {
        Array.Clear(_pfifo);
        Array.Clear(_pvideo);
        Array.Clear(_pfb);
        Array.Clear(_pgraph);
        _pmcEnabled = 0;
        _pmcPending = 0;
        _pbusCommand = 0;
        _ptimerPending = 0;
        _ptimerEnabled = 0;
        _ptimerNumerator = 0;
        _ptimerDenominator = 0;
        _ptimerClock = 0;
        _pcrtcPending = 0;
        _pcrtcEnabled = 0;
        _pcrtcStart = 0;
        _pcrtcRaster = 0;
        _nvpllCoeff = PramdacNvpllReset;
        _mpllCoeff = 0;
        _vpllCoeff = PramdacVpllReset;
        _generalControl = 0;
        _pgraphPending = 0;
        _pgraphEnabled = 0;
        _pfifoPending = 0;
        _pfifoEnabled = 0;
        _pfifo[PfifoCache1Status / 4] = PfifoCache1StatusLowMark;
        _pfb[PfbCfg0 / 4] = PfbCfg0Reset;
    }

    /// <inheritdoc />
    public uint Read(uint address, int size)
    {
        uint offset = address - BaseAddress;
        uint word = ReadAligned(offset & ~3u);
        uint shift = (offset & 3u) * 8;
        return size switch
        {
            1 => (word >> (int)shift) & 0xFFu,
            2 => (word >> (int)shift) & 0xFFFFu,
            _ => word,
        };
    }

    /// <inheritdoc />
    public void Write(uint address, int size, uint value)
    {
        uint offset = address - BaseAddress;
        if (size == 4)
        {
            WriteAligned(offset, value);
            return;
        }

        uint aligned = offset & ~3u;
        uint word = ReadAligned(aligned);
        uint shift = (offset & 3u) * 8;
        uint mask = size == 1 ? 0xFFu << (int)shift : 0xFFFFu << (int)shift;
        WriteAligned(aligned, (word & ~mask) | ((value << (int)shift) & mask));
    }

    private uint ReadAligned(uint offset)
    {
        if (offset >= PraminBase && offset < PraminBase + PraminSize)
        {
            return ReadRamin(offset - PraminBase);
        }

        if (offset >= UserBase && offset < UserBase + UserSize)
        {
            CountRead(UserBase);
            return ReadUser(offset - UserBase);
        }

        if (offset < PmcBase + 0x1000u)
        {
            CountRead(PmcBase);
            return ReadPmc(offset - PmcBase);
        }

        if (offset >= PbusBase && offset < PbusBase + 0x1000u)
        {
            CountRead(PbusBase);
            return ReadPbus(offset - PbusBase);
        }

        if (offset >= PfifoBase && offset < PfifoBase + PfifoSize)
        {
            CountRead(PfifoBase);
            return _pfifo[(offset - PfifoBase) / 4];
        }

        if (offset >= PvideoBase && offset < PvideoBase + 0x1000u)
        {
            CountRead(PvideoBase);
            return _pvideo[(offset - PvideoBase) / 4];
        }

        if (offset >= PtimerBase && offset < PtimerBase + 0x1000u)
        {
            CountRead(PtimerBase);
            return ReadPtimer(offset - PtimerBase);
        }

        if (offset >= PfbBase && offset < PfbBase + 0x1000u)
        {
            CountRead(PfbBase);
            return ReadPfb(offset - PfbBase);
        }

        if (offset >= PgraphBase && offset < PgraphBase + PgraphSize)
        {
            CountRead(PgraphBase);
            return ReadPgraph(offset - PgraphBase);
        }

        if (offset >= PcrtcBase && offset < PcrtcBase + 0x1000u)
        {
            CountRead(PcrtcBase);
            return ReadPcrtc(offset - PcrtcBase);
        }

        if (offset >= PramdacBase && offset < PramdacBase + 0x1000u)
        {
            CountRead(PramdacBase);
            return ReadPramdac(offset - PramdacBase);
        }

        CountRead(offset & ~0xFFFu);
        return 0;
    }

    private void WriteAligned(uint offset, uint value)
    {
        if (offset >= PraminBase && offset < PraminBase + PraminSize)
        {
            WriteRamin(offset - PraminBase, value);
            return;
        }

        if (offset >= UserBase && offset < UserBase + UserSize)
        {
            CountWrite(UserBase);
            WriteUser(offset - UserBase, value);
            return;
        }

        if (offset < PmcBase + 0x1000u)
        {
            CountWrite(PmcBase);
            WritePmc(offset - PmcBase, value);
            return;
        }

        if (offset >= PbusBase && offset < PbusBase + 0x1000u)
        {
            CountWrite(PbusBase);
            WritePbus(offset - PbusBase, value);
            return;
        }

        if (offset >= PfifoBase && offset < PfifoBase + PfifoSize)
        {
            CountWrite(PfifoBase);
            _pfifo[(offset - PfifoBase) / 4] = value;
            return;
        }

        if (offset >= PvideoBase && offset < PvideoBase + 0x1000u)
        {
            CountWrite(PvideoBase);
            _pvideo[(offset - PvideoBase) / 4] = value;
            return;
        }

        if (offset >= PtimerBase && offset < PtimerBase + 0x1000u)
        {
            CountWrite(PtimerBase);
            WritePtimer(offset - PtimerBase, value);
            return;
        }

        if (offset >= PfbBase && offset < PfbBase + 0x1000u)
        {
            CountWrite(PfbBase);
            _pfb[(offset - PfbBase) / 4] = value;
            return;
        }

        if (offset >= PgraphBase && offset < PgraphBase + PgraphSize)
        {
            CountWrite(PgraphBase);
            WritePgraph(offset - PgraphBase, value);
            return;
        }

        if (offset >= PcrtcBase && offset < PcrtcBase + 0x1000u)
        {
            CountWrite(PcrtcBase);
            WritePcrtc(offset - PcrtcBase, value);
            return;
        }

        if (offset >= PramdacBase && offset < PramdacBase + 0x1000u)
        {
            CountWrite(PramdacBase);
            WritePramdac(offset - PramdacBase, value);
            return;
        }

        CountWrite(offset & ~0xFFFu);
    }

    private uint ReadPmc(uint register)
    {
        return register switch
        {
            0x0u => PmcBoot0,
            PmcIntr0 => AggregatePending(),
            PmcIntrEn0 => _pmcEnabled,
            _ => 0u,
        };
    }

    private void WritePmc(uint register, uint value)
    {
        switch (register)
        {
            case PmcIntr0:
                _pmcPending &= ~value;
                _pfifoPending &= ~((value >> 8) & 1u);
                _pgraphPending &= ~((value >> 12) & 1u);
                _pcrtcPending &= ~((value >> 24) & 1u);
                break;
            case PmcIntrEn0:
                _pmcEnabled = value;
                break;
            default:
                break;
        }
    }

    private uint AggregatePending()
    {
        uint pending = _pmcPending;
        if ((_pfifoPending & _pfifoEnabled) != 0)
        {
            pending |= 1u << 8;
        }

        if ((_pgraphPending & _pgraphEnabled) != 0)
        {
            pending |= 1u << 12;
        }

        if ((_pcrtcPending & _pcrtcEnabled) != 0)
        {
            pending |= 1u << 24;
        }

        return pending;
    }

    private uint ReadPbus(uint register)
    {
        return register switch
        {
            PbusPciNv0 => PbusIdValue,
            PbusPciNv1 => _pbusCommand,
            PbusPciNv2 => PbusClassValue,
            _ => 0u,
        };
    }

    private void WritePbus(uint register, uint value)
    {
        if (register == PbusPciNv1)
        {
            _pbusCommand = value;
        }
    }

    private uint ReadPtimer(uint register)
    {
        ulong clock = _ptimerNumerator == 0
            ? _ptimerClock
            : (_ptimerClock * _ptimerDenominator) / _ptimerNumerator;
        return register switch
        {
            PtimerIntr0 => _ptimerPending,
            PtimerIntrEn0 => _ptimerEnabled,
            PtimerNumerator => _ptimerNumerator,
            PtimerDenominator => _ptimerDenominator,
            PtimerTime0 => (uint)((clock & 0x7FFFFFFu) << 5),
            PtimerTime1 => (uint)((clock >> 27) & 0x1FFFFFFFu),
            _ => 0u,
        };
    }

    private void WritePtimer(uint register, uint value)
    {
        switch (register)
        {
            case PtimerIntr0:
                _ptimerPending &= ~value;
                break;
            case PtimerIntrEn0:
                _ptimerEnabled = value;
                break;
            case PtimerNumerator:
                _ptimerNumerator = value;
                break;
            case PtimerDenominator:
                _ptimerDenominator = value;
                break;
            default:
                break;
        }
    }

    private uint ReadPfb(uint register)
    {
        return register switch
        {
            PfbCstatus => RamSize,
            PfbWbc => 0u,
            _ => _pfb[register / 4],
        };
    }

    private uint ReadPgraph(uint register)
    {
        return register switch
        {
            0x100u => _pgraphPending,
            0x140u => _pgraphEnabled,
            _ => _pgraph[register / 4],
        };
    }

    private void WritePgraph(uint register, uint value)
    {
        switch (register)
        {
            case 0x100u:
                _pgraphPending &= ~value;
                break;
            case 0x140u:
                _pgraphEnabled = value;
                break;
            default:
                _pgraph[register / 4] = value;
                break;
        }
    }

    private uint ReadPcrtc(uint register)
    {
        return register switch
        {
            PcrtcIntr0 => _pcrtcPending,
            PcrtcIntrEn0 => _pcrtcEnabled,
            PcrtcStart => _pcrtcStart,
            PcrtcRaster => _pcrtcRaster++,
            _ => 0u,
        };
    }

    private void WritePcrtc(uint register, uint value)
    {
        switch (register)
        {
            case PcrtcIntr0:
                _pcrtcPending &= ~value;
                break;
            case PcrtcIntrEn0:
                _pcrtcEnabled = value;
                break;
            case PcrtcStart:
                _pcrtcStart = value & 0x07FFFFFFu;
                break;
            default:
                break;
        }
    }

    private uint ReadPramdac(uint register)
    {
        return register switch
        {
            PramdacNvpllCoeff => _nvpllCoeff,
            PramdacMpllCoeff => _mpllCoeff,
            PramdacVpllCoeff => _vpllCoeff,
            PramdacPllTest => PramdacPllLocks,
            PramdacGeneralControl => _generalControl,
            _ => 0u,
        };
    }

    private void WritePramdac(uint register, uint value)
    {
        switch (register)
        {
            case PramdacNvpllCoeff:
                _nvpllCoeff = value;
                break;
            case PramdacMpllCoeff:
                _mpllCoeff = value;
                break;
            case PramdacVpllCoeff:
                _vpllCoeff = value;
                break;
            case PramdacGeneralControl:
                _generalControl = value;
                break;
            default:
                break;
        }
    }

    private uint ReadRamin(uint offset)
    {
        CountRead(PraminBase);
        return (uint)(_ramin[offset] | (_ramin[offset + 1] << 8) | (_ramin[offset + 2] << 16) | (_ramin[offset + 3] << 24));
    }

    private void WriteRamin(uint offset, uint value)
    {
        CountWrite(PraminBase);
        _ramin[offset] = (byte)value;
        _ramin[offset + 1] = (byte)(value >> 8);
        _ramin[offset + 2] = (byte)(value >> 16);
        _ramin[offset + 3] = (byte)(value >> 24);
    }

    private uint ReadUser(uint offset)
    {
        if (TryGetDmaChannel(offset, out _) && (offset & 0xFFFFu) switch
        {
            UserDmaPut => true,
            UserDmaGet => true,
            UserRef => true,
            _ => false,
        })
        {
            return (offset & 0xFFFFu) switch
            {
                UserDmaPut => _pfifo[PfifoCache1DmaPut / 4],
                UserDmaGet => _pfifo[PfifoCache1DmaGet / 4],
                _ => _pfifo[PfifoCache1Ref / 4],
            };
        }

        return 0;
    }

    private void WriteUser(uint offset, uint value)
    {
        if (!TryGetDmaChannel(offset, out _))
        {
            return;
        }

        switch (offset & 0xFFFFu)
        {
            case UserDmaPut:
                _pfifo[PfifoCache1DmaPut / 4] = value;
                break;
            case UserDmaGet:
                _pfifo[PfifoCache1DmaGet / 4] = value;
                break;
            case UserRef:
                _pfifo[PfifoCache1Ref / 4] = value;
                break;
            default:
                break;
        }
    }

    private bool TryGetDmaChannel(uint offset, out uint channel)
    {
        channel = offset >> 16;
        if (((_pfifo[PfifoMode / 4] >> (int)channel) & 1u) == 0)
        {
            return false;
        }

        return channel == (_pfifo[PfifoCache1Push1 / 4] & 0x1Fu);
    }

    private void CountRead(uint block)
    {
        _readCounts.TryGetValue(block, out int count);
        _readCounts[block] = count + 1;
    }

    private void CountWrite(uint block)
    {
        _writeCounts.TryGetValue(block, out int count);
        _writeCounts[block] = count + 1;
    }
}
