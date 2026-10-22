using System;
using System.IO;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;
using XGrid.Memory;

namespace XGrid.Bus.Aci;

/// <summary>
/// MCPX ACI (AC'97 audio codec interface) register window: PCI 0:6.0 BAR2 at 0xFEC00000,
/// 4 KB (XboxDevWiki "Memory" and "PCI" pages). Layout and semantics follow xemu's QEMU
/// AC'97 core (hw/audio/ac97.c, used by xemu/hw/xbox/mcpx/aci.c): the NAM codec register
/// file at 0x000-0x0FF and the NABM bus-master block at 0x100-0x17F.
/// Audio voices and DMA playback are not modeled yet; the register interface is.
/// </summary>
public sealed class McpxAciDevice : IMmioDevice, IXboxDevice, ISnapshotable
{
    /// <summary>ACI MMIO window base (fixed by the MCPX, not BAR-programmed by the guest).</summary>
    public const uint BaseAddress = 0xFEC00000;

    /// <summary>ACI MMIO window size (PCI BAR2 size).</summary>
    public const uint WindowSize = 0x00001000;

    private const uint NamLimit = 0x100;
    private const uint NabmLimit = 0x180;

    // NABM register offsets (relative to 0x100), from QEMU ac97.c.
    private const uint GlobCnt = 0x2C;
    private const uint GlobSta = 0x30;
    private const uint Cas = 0x34;

    private const uint GcWr = 1u << 2;
    private const uint GcCr = 1u << 1;
    private const uint GcValidMask = (1u << 6) - 1;
    private const uint GsS0Cr = 1u << 8;
    private const uint GsValidMask = (1u << 18) - 1;
    private const uint GsWclearMask = (1u << 15) | (1u << 11) | (1u << 10) | 1u;

    private const uint GsRoMask =
        (1u << 14) | (1u << 13) | (1u << 12) | (1u << 9) | (1u << 8) |
        (1u << 7) | (1u << 6) | (1u << 5) | (1u << 4) | (1u << 3) | (1u << 2) | (1u << 1);

    private const byte CrRpbm = 1 << 0;
    private const byte CrRr = 1 << 1;
    private const byte CrDontClearMask = (1 << 4) | (1 << 3) | (1 << 2);
    private const byte CrValidMask = (1 << 5) - 1;
    private const byte SrDch = 1 << 0;
    private const byte SrWclearMask = (1 << 4) | (1 << 3) | (1 << 2);
    private const byte SrRoMask = (1 << 1) | (1 << 0);

    // Codec (NAM) register offsets used by the reset defaults (QEMU ac97.h/ac97.c).
    private const uint Ac97Reset = 0x00;
    private const uint Ac97MasterVolumeMute = 0x02;
    private const uint Ac97PcmOutVolumeMute = 0x18;
    private const uint Ac97RecordGainMute = 0x1C;
    private const uint Ac97RecordSelect = 0x1A;
    private const uint Ac97PowerdownCtrlStat = 0x26;
    private const uint Ac97ExtendedAudioId = 0x28;
    private const uint Ac97ExtendedAudioCtrlStat = 0x2A;
    private const uint Ac97PcmFrontDacRate = 0x2C;
    private const uint Ac97PcmSurroundDacRate = 0x2E;
    private const uint Ac97PcmLfeDacRate = 0x30;
    private const uint Ac97PcmLrAdcRate = 0x32;
    private const uint Ac97MicAdcRate = 0x34;
    private const uint Ac97VendorId1 = 0x7C;
    private const uint Ac97VendorId2 = 0x7E;

    private readonly byte[] _mixer = new byte[0x80];
    private readonly BmBlock[] _bm = new BmBlock[8];
    private uint _globCnt;
    private uint _globSta;
    private bool _cas;

    /// <summary>Creates the ACI in its power-on state.</summary>
    public McpxAciDevice()
    {
        for (int i = 0; i < _bm.Length; i++)
        {
            _bm[i] = new BmBlock();
        }

        Reset();
    }

    /// <summary>Gets the device name used in traces and diagnostics ("MCPX ACI").</summary>
#pragma warning disable CA1822
    public string Name => "MCPX ACI";
#pragma warning restore CA1822

    /// <summary>Gets the number of codec (NAM) register accesses, for diagnostics.</summary>
    public long NamAccesses { get; private set; }

    /// <summary>Gets the number of bus-master (NABM) register accesses, for diagnostics.</summary>
    public long NabmAccesses { get; private set; }

    /// <summary>Returns the ACI to its power-on state: clears the mixer file, global control/status
    /// registers, and CAS semaphore, clears all bus-master blocks, then applies QEMU's
    /// ac97_on_reset defaults (blocks 0-2 reset, SO clear, AC'97 mixer register defaults).</summary>
    public void Reset()
    {
        Array.Clear(_mixer);
        _globCnt = 0;
        _globSta = 0;
        _cas = false;
        foreach (BmBlock block in _bm)
        {
            block.Clear();
        }

        // QEMU ac97_on_reset: bus-master blocks 0-2 reset, SO (7) stays clear, mixer reset.
        ResetBlock(_bm[0]);
        ResetBlock(_bm[1]);
        ResetBlock(_bm[2]);
        MixerReset();
    }

    /// <summary>Saves mixer, bus-master blocks, global regs, semaphore, and access counters.</summary>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(_mixer);
        writer.Write(_globCnt);
        writer.Write(_globSta);
        writer.Write(_cas);
        writer.Write(NamAccesses);
        writer.Write(NabmAccesses);
        foreach (BmBlock block in _bm)
        {
            writer.Write(block.BdbAr);
            writer.Write(block.Civ);
            writer.Write(block.Lvi);
            writer.Write(block.Sr);
            writer.Write(block.Picb);
            writer.Write(block.Piv);
            writer.Write(block.Cr);
        }
    }

    /// <summary>Restores mixer, bus-master blocks, global regs, semaphore, and access counters.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported ACI snapshot version {version}.");
        }

        byte[] mixer = reader.ReadBytes(0x80);
        if (mixer.Length != 0x80)
        {
            throw new InvalidDataException("Truncated ACI mixer snapshot.");
        }

        Array.Copy(mixer, _mixer, 0x80);
        _globCnt = reader.ReadUInt32();
        _globSta = reader.ReadUInt32();
        _cas = reader.ReadBoolean();
        NamAccesses = reader.ReadInt64();
        NabmAccesses = reader.ReadInt64();
        foreach (BmBlock block in _bm)
        {
            block.BdbAr = reader.ReadUInt32();
            block.Civ = reader.ReadByte();
            block.Lvi = reader.ReadByte();
            block.Sr = reader.ReadByte();
            block.Picb = reader.ReadUInt16();
            block.Piv = reader.ReadByte();
            block.Cr = reader.ReadByte();
        }
    }

    private const int SnapshotVersion = 1;

    /// <summary>Reads a register in the 4 KB ACI window at 0xFEC00000: offsets 0x000-0x0FF serve
    /// the NAM codec file (16-bit mixer reads, any access releasing the CAS semaphore), offsets
    /// 0x100-0x17F serve the NABM bus-master block, and offsets 0x180 and above read as all ones.</summary>
    /// <param name="address">Absolute address inside the window (base 0xFEC00000).</param>
    /// <param name="size">Access width in bytes (1, 2, or 4).</param>
    public uint Read(uint address, int size)
    {
        uint offset = address - BaseAddress;
        if (offset < NamLimit)
        {
            NamAccesses++;
            return ReadNam(offset, size);
        }

        if (offset < NabmLimit)
        {
            NabmAccesses++;
            return ReadNabm(offset - NamLimit, size);
        }

        return size switch
        {
            1 => 0xFFu,
            2 => 0xFFFFu,
            _ => 0xFFFFFFFFu,
        };
    }

    /// <summary>Writes a register in the 4 KB ACI window at 0xFEC00000: 16-bit NAM writes update
    /// the mixer (with QEMU's per-register masking and read-only vendor IDs), other NAM sizes only
    /// release the CAS semaphore, and NABM writes update the global registers or bus-master blocks;
    /// offsets 0x180 and above are ignored.</summary>
    /// <param name="address">Absolute address inside the window (base 0xFEC00000).</param>
    /// <param name="size">Access width in bytes (1, 2, or 4).</param>
    /// <param name="value">Value to write, right-aligned in the low bytes.</param>
    public void Write(uint address, int size, uint value)
    {
        uint offset = address - BaseAddress;
        if (offset < NamLimit)
        {
            NamAccesses++;
            WriteNam(offset, size, value);
            return;
        }

        if (offset < NabmLimit)
        {
            NabmAccesses++;
            WriteNabm(offset - NamLimit, size, value);
        }
    }

    private static int BlockIndex(uint offset)
    {
        return (int)((offset >> 4) & 7);
    }

    private static bool IsBusMasterBlock(uint offset)
    {
        int index = BlockIndex(offset);
        return index is 0 or 1 or 2 or 7;
    }

    private static uint BlockRegister(uint offset)
    {
        return offset & 0xFu;
    }

    private uint ReadNam(uint offset, int size)
    {
        // Any NAM access releases the codec access semaphore (QEMU nam_read*).
        _cas = false;
        return size switch
        {
            1 => 0xFFu,
            2 => MixerLoad(offset & ~1u),
            _ => 0xFFFFFFFFu,
        };
    }

    private void WriteNam(uint offset, int size, uint value)
    {
        _cas = false;
        if (size != 2)
        {
            // QEMU nam_writeb/writel only clear the semaphore.
            return;
        }

        uint reg = offset & ~1u;
        switch (reg)
        {
            case Ac97Reset:
                MixerReset();
                break;
            case Ac97PowerdownCtrlStat:
                MixerStore(reg, (ushort)((value & ~0x800Fu) | (uint)(MixerLoad(reg) & 0xF)));
                break;
            case Ac97MasterVolumeMute:
                MixerStore(reg, (ushort)(value & 0xBF3F));
                break;
            case Ac97PcmOutVolumeMute:
                MixerStore(reg, (ushort)(value & 0x9F1F));
                break;
            case Ac97RecordGainMute:
                MixerStore(reg, (ushort)(value & 0x8F0F));
                break;
            case Ac97RecordSelect:
                MixerStore(reg, (ushort)((value & 7) | (((value >> 8) & 7) << 8)));
                break;
            case Ac97VendorId1:
            case Ac97VendorId2:
                // Vendor ID registers are read-only (QEMU ignores writes).
                break;
            default:
                MixerStore(reg, (ushort)value);
                break;
        }
    }

    private uint ReadNabm(uint offset, int size)
    {
        return size switch
        {
            1 => ReadNabmByte(offset),
            2 => ReadNabmWord(offset),
            _ => ReadNabmDword(offset),
        };
    }

    private uint ReadNabmByte(uint offset)
    {
        if (offset == Cas)
        {
            // Reading CAS returns the current value and locks the semaphore (QEMU nabm_readb).
            uint value = _cas ? 1u : 0u;
            _cas = true;
            return value;
        }

        if (!IsBusMasterBlock(offset))
        {
            return 0xFFu;
        }

        BmBlock block = _bm[BlockIndex(offset)];
        return BlockRegister(offset) switch
        {
            0x04 => block.Civ,
            0x05 => block.Lvi,
            0x06 => block.Sr,
            0x0A => block.Piv,
            0x0B => block.Cr,
            _ => 0xFFu,
        };
    }

    private uint ReadNabmWord(uint offset)
    {
        if (!IsBusMasterBlock(offset))
        {
            return ReadNabmByte(offset) | (ReadNabmByte(offset + 1) << 8);
        }

        BmBlock block = _bm[BlockIndex(offset)];
        return BlockRegister(offset) switch
        {
            0x06 => block.Sr,
            0x08 => block.Picb,
            _ => ReadNabmByte(offset) | (ReadNabmByte(offset + 1) << 8),
        };
    }

    private uint ReadNabmDword(uint offset)
    {
        if (offset == GlobCnt)
        {
            return _globCnt;
        }

        if (offset == GlobSta)
        {
            return _globSta | GsS0Cr;
        }

        if (!IsBusMasterBlock(offset))
        {
            return ReadNabmWord(offset) | (ReadNabmWord(offset + 2) << 16);
        }

        BmBlock block = _bm[BlockIndex(offset)];
        return BlockRegister(offset) switch
        {
            0x00 => block.BdbAr,
            0x04 => block.Civ | ((uint)block.Lvi << 8) | ((uint)block.Sr << 16),
            0x08 => block.Picb | ((uint)block.Piv << 16) | ((uint)block.Cr << 24),
            _ => ReadNabmWord(offset) | (ReadNabmWord(offset + 2) << 16),
        };
    }

    private void WriteNabm(uint offset, int size, uint value)
    {
        switch (size)
        {
            case 1:
                WriteNabmByte(offset, value);
                break;
            case 2:
                WriteNabmWord(offset, value);
                break;
            default:
                WriteNabmDword(offset, value);
                break;
        }
    }

    private void WriteNabmByte(uint offset, uint value)
    {
        if (!IsBusMasterBlock(offset))
        {
            // CAS and reserved writes are ignored (QEMU nabm_writeb default).
            return;
        }

        BmBlock block = _bm[BlockIndex(offset)];
        switch (BlockRegister(offset))
        {
            case 0x05:
                block.Lvi = (byte)(value % 32);
                break;
            case 0x0B:
                if ((value & CrRr) != 0)
                {
                    ResetBlock(block);
                }
                else
                {
                    block.Cr = (byte)(value & CrValidMask);
                    if ((block.Cr & CrRpbm) == 0)
                    {
                        block.Sr |= SrDch;
                    }
                    else
                    {
                        // DMA start is accepted; buffer-descriptor fetch/voices are not modeled.
                        block.Sr &= unchecked((byte)~SrDch);
                    }
                }

                break;
            case 0x06:
                block.Sr |= (byte)(value & ~(SrRoMask | SrWclearMask));
                block.Sr &= (byte)~(value & SrWclearMask);
                break;
        }
    }

    private void WriteNabmWord(uint offset, uint value)
    {
        if (IsBusMasterBlock(offset) && BlockRegister(offset) == 0x06)
        {
            BmBlock block = _bm[BlockIndex(offset)];
            block.Sr |= (byte)(value & ~(SrRoMask | SrWclearMask));
            block.Sr &= (byte)~(value & SrWclearMask);
            return;
        }

        WriteNabmByte(offset, value & 0xFF);
        WriteNabmByte(offset + 1, (value >> 8) & 0xFF);
    }

    private void WriteNabmDword(uint offset, uint value)
    {
        if (offset == GlobCnt)
        {
            // Cold/warm reset requests are accepted but not acted on (QEMU TODO).
            if ((value & (GcWr | GcCr)) == 0)
            {
                _globCnt = value & GcValidMask;
            }

            return;
        }

        if (offset == GlobSta)
        {
            _globSta &= ~(value & GsWclearMask);
            _globSta |= value & ~(GsWclearMask | GsRoMask) & GsValidMask;
            return;
        }

        if (IsBusMasterBlock(offset) && BlockRegister(offset) == 0x00)
        {
            _bm[BlockIndex(offset)].BdbAr = value & ~3u;
            return;
        }

        WriteNabmWord(offset, value & 0xFFFF);
        WriteNabmWord(offset + 2, (value >> 16) & 0xFFFF);
    }

    private static void ResetBlock(BmBlock block)
    {
        block.BdbAr = 0;
        block.Civ = 0;
        block.Lvi = 0;
        block.Sr = SrDch;
        block.Picb = 0;
        block.Piv = 0;
        block.Cr &= CrDontClearMask;
    }

    private void MixerReset()
    {
        Array.Clear(_mixer);
        MixerStore(Ac97Reset, 0x0000);
        MixerStore(Ac97PowerdownCtrlStat, 0x000F);
        MixerStore(Ac97VendorId1, 0x8384);
        MixerStore(Ac97VendorId2, 0x7600);
        MixerStore(Ac97ExtendedAudioId, 0x0809);
        MixerStore(Ac97ExtendedAudioCtrlStat, 0x0009);
        MixerStore(Ac97PcmFrontDacRate, 0xBB80);
        MixerStore(Ac97PcmSurroundDacRate, 0xBB80);
        MixerStore(Ac97PcmLfeDacRate, 0xBB80);
        MixerStore(Ac97PcmLrAdcRate, 0xBB80);
        MixerStore(Ac97MicAdcRate, 0xBB80);
        MixerStore(Ac97RecordSelect, 0x0000);
        MixerStore(Ac97MasterVolumeMute, 0x8000);
        MixerStore(Ac97PcmOutVolumeMute, 0x8808);
        MixerStore(Ac97RecordGainMute, 0x8808);
    }

    private void MixerStore(uint offset, ushort value)
    {
        if (offset + 2 > _mixer.Length)
        {
            return;
        }

        _mixer[offset] = (byte)value;
        _mixer[offset + 1] = (byte)(value >> 8);
    }

    private ushort MixerLoad(uint offset)
    {
        if (offset + 2 > _mixer.Length)
        {
            return 0xFFFF;
        }

        return (ushort)(_mixer[offset] | (_mixer[offset + 1] << 8));
    }

    private sealed class BmBlock
    {
        public uint BdbAr { get; set; }

        public byte Civ { get; set; }

        public byte Lvi { get; set; }

        public byte Sr { get; set; }

        public ushort Picb { get; set; }

        public byte Piv { get; set; }

        public byte Cr { get; set; }

        public void Clear()
        {
            BdbAr = 0;
            Civ = 0;
            Lvi = 0;
            Sr = 0;
            Picb = 0;
            Piv = 0;
            Cr = 0;
        }
    }
}
