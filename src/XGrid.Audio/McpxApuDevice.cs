using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;
using XGrid.Memory;

namespace XGrid.Audio;

/// <summary>Guest physical memory access used by the APU for notifier status and voice state.</summary>
public interface IApuMemory
{
    /// <summary>Reads a 32-bit little-endian value from guest physical memory.</summary>
    uint Read32(uint address);

    /// <summary>Writes a 32-bit little-endian value to guest physical memory.</summary>
    void Write32(uint address, uint value);

    /// <summary>Writes a byte to guest physical memory.</summary>
    void Write8(uint address, byte value);
}

/// <summary>
/// MCPX APU (audio processing unit) register window at 0xFE800000 (PCI 0:5.0 BAR0, 512 KB;
/// XboxDevWiki "Memory"). Register layout and semantics follow xemu's MCPX APU
/// (hw/xbox/mcpx/apu/apu.c, apu/vp/vp.c): global registers below 0x20000 (ISTS/IEN,
/// FE control, memory port), the voice-processor front-end PIO region at 0x20000+ and the
/// GP/EP DSP register windows at 0x30000+/0x50000+.
/// The voice processor keeps per-voice state in guest RAM at VPVADDR + voice*0x80 (xemu
/// voice_get_mask/voice_set_mask) and advances it one 48 kHz frame (256 samples, the
/// existing XGrid APU frame quantum) at a time: the current buffer offset walks the voice's
/// SSL segment list, segment completion flips the SSL index and writes a notifier status,
/// and voices that finish (or are turned off) report SE2FE_IDLE_VOICE through the FE trap
/// registers on the next frame.
/// The GP/EP DSP cores are modeled as frame-driven engines, not executed microcode
/// (xemu's M56001 core in dsp/ is GPL and too large to port faithfully for this gate):
/// GPRST/EPRST writes track run-state with xemu's proc_rst_write semantics (gp_ep.c),
/// and each frame a running GP engine performs the DSP-side completion writeback the
/// XDK DSOUND microcode performs through its scratch window — absorbing a pending mixer
/// block (state 2/3 to 0) at the scratch-mapped mailbox — and raises ISTS bit3 as the
/// DSP subunit service request (serviced by the guest's own IRQ5 ISR/worker/FB restart
/// chain). Scratch access is gated on the guest's GPSADDR SGE table actually mapping the
/// mailbox page, so unprogrammed titles can never be touched.
/// </summary>
public sealed class McpxApuDevice : IMmioDevice, IXboxDevice, ISnapshotable
{
    /// <summary>APU MMIO window base (fixed by the MCPX, not BAR-programmed by the guest).</summary>
    public const uint BaseAddress = 0xFE800000;

    /// <summary>APU MMIO window size (PCI BAR0 size).</summary>
    public const uint WindowSize = 0x00080000;

    private const uint IsTs = 0x1000;
    private const uint Ien = 0x1004;
    private const uint Fectl = 0x1100;
    private const uint FenAddr = 0x115C;
    private const uint FedecMeth = 0x1300;
    private const uint FedecParam = 0x1304;
    private const uint FemMemAddr = 0x1324;
    private const uint FemMemData = 0x1334;
    private const uint FetForce1 = 0x1504;
    private const uint VpVaddr = 0x202C;
    private const uint VpSgeAddr = 0x2030;
    private const uint VpSslAddr = 0x2034;
    private const uint GpsAddr = 0x2040;
    private const uint VpPioFree = 0x20010;
    private const uint VpVoiceOn = 0x20124;
    private const uint VpVoiceOff = 0x20128;

    // The EP frame counter register and the mixed samples it counts per frame
    // (xemu apu_regs.h NV_PAPU_XGSCNT / NUM_SAMPLES_PER_FRAME).
    private const uint XgsCnt = 0x200C;
    private const uint SamplesPerFrame = 32;

    private const uint GpRst = 0x3FFFC;
    private const uint EpRst = 0x5FFFC;
    private const uint DspRunBits = 0x3u;

    private const uint IsTsGintsts = 1u << 0;
    private const uint IsTsDspService = 1u << 3;
    private const uint IsTsFeTintsts = 1u << 4;
    private const uint IsTsFenintsts = 1u << 5;
    private const uint IsTsFevintsts = 1u << 6;

    private const uint FectlFemethmode = 0xE0;
    private const uint FectlFemethmodeTrapped = 0xE0;
    private const uint FectlFetrapreason = 0xF00;
    private const uint FectlFetrapreasonRequested = 0xF00;
    private const uint FetForce1Se2FeIdleVoice = 1u << 15;
    private const uint Se2FeIdleVoice = 0x00008000;

    // ReSharper disable once UnusedMember.Local
    private const uint VoiceStateOffset = 0x54;
    private const uint VoiceSize = 0x80;
    private const uint PioFreeCount = 0x80;
    private const uint MaxVoices = 256;
    private const byte NotificationDoneSuccess = 0x01;

    // Voice state offsets (xemu apu_regs.h NV_PAVS_VOICE_*).
    private const uint VoiceCfgFmt = 0x04;
    private const uint VoiceCfgEnva = 0x0C;
    private const uint VoiceCfgEnvf = 0x14;
    private const uint VoiceCurPslStart = 0x20;
    private const uint VoiceCurPshSample = 0x24;
    private const uint VoiceParState = 0x54;
    private const uint VoiceParOffset = 0x58;
    private const uint VoiceParNext = 0x5C;
    private const uint VoiceTarPitchLink = 0x7C;

    private const uint VoiceStatePaused = 1u << 18;
    private const uint VoiceStateNewVoice = 1u << 20;
    private const uint VoiceStateActiveVoice = 1u << 21;

    private const uint VoiceFmtPersist = 1u << 23;
    private const uint VoiceFmtDataType = 1u << 24;
    private const uint VoiceFmtLoop = 1u << 25;

    private const uint VoiceEnvelopeMask = 0xF;
    private const uint VoiceEnvelopeDelay = 1;
    private const uint VoiceEnvelopeAttack = 2;
    private const uint VoiceEnvelopeHold = 3;

    // DSP mixer-block mailbox (observed XDK DSOUND protocol fact): the GP DSP's
    // scratch window (GPSADDR SGE table) maps the shared mixer block, and the state
    // word is DSP scratch address 0x810 (phys 0x3924810, VA 0x83924810). State 2 is a
    // posted mixer submission, state 3 is loaded parameters; the DSP absorbs either
    // (writes 0) when it consumes the block.
    private const uint DspStateWordPhys = 0x03924810u;
    private const uint DspStateSubmitPosted = 2u;
    private const uint DspStateParamsLoaded = 3u;
    private const uint PageMask = ~0xFFFu;

    // Wake debounce: at most one DSP service raise per this many frames (~1.28 s).
    // Continuous mixer submissions flow silently (hardware steady state needs no
    // restart); only batch boundaries raise. FB's unconditional ~10 ms stall makes
    // sub-second restart rates unserviceable.
    private const uint MinWakeGapFrames = 240u;

    // SGE entries scanned for the mailbox page (MCPX_HW_MAX_PRD_ENTRIES_PER_SSL).
    private const int DspSgeScanEntries = 16;

    private const uint OffsetMask24 = 0x00FFFFFF;
    private const uint HandleMask16 = 0x0000FFFF;

    // VP PIO method offsets (xemu apu_regs.h NV1BA0_PIO_*).
    private const uint PioFree = 0x10;
    private const uint PioSetAntecedentVoice = 0x120;
    private const uint PioVoiceOn = 0x124;
    private const uint PioVoiceOff = 0x128;
    private const uint PioVoiceRelease = 0x12C;
    private const uint PioVoicePause = 0x140;
    private const uint PioSetCurrentSsl = 0x190;
    private const uint PioSetCurrentVoice = 0x2F8;
    private const uint PioVoiceLock = 0x2FC;
    private const uint PioSetVoiceCfgVbin = 0x300;
    private const uint PioSetVoiceCfgFmt = 0x304;
    private const uint PioSetVoiceCfgEnv0 = 0x308;
    private const uint PioSetVoiceCfgEnva = 0x30C;
    private const uint PioSetVoiceCfgEnv1 = 0x310;
    private const uint PioSetVoiceCfgEnvf = 0x314;
    private const uint PioSetVoiceCfgMisc = 0x318;
    private const uint PioSetVoiceTarHrtf = 0x31C;
    private const uint PioSetVoiceSslA = 0x320;
    private const uint PioSetVoiceSslB = 0x35C;
    private const uint PioSetVoiceTarVola = 0x360;
    private const uint PioSetVoiceTarVolb = 0x364;
    private const uint PioSetVoiceTarVolc = 0x368;
    private const uint PioSetVoiceLfoEnv = 0x36C;
    private const uint PioSetVoiceTarFca = 0x374;
    private const uint PioSetVoiceTarFcb = 0x378;
    private const uint PioSetVoiceTarPitch = 0x37C;
    private const uint PioSetVoiceCfgBufBase = 0x3A0;
    private const uint PioSetVoiceCfgBufLbo = 0x3A4;
    private const uint PioSetVoiceBufCbo = 0x3D8;
    private const uint PioSetVoiceCfgBufEbo = 0x3DC;
    private const uint PioSetSslSegmentOffset = 0x600;
    private const uint PioSetSslSegmentEnd = 0x7FF;
    private const uint PioSetCurrentInbufSge = 0x804;
    private const uint PioSetCurrentInbufSgeOffset = 0x808;
    private const uint PioSetOutbufBa = 0x1000;
    private const uint PioSetOutbufLen = 0x1004;
    private const uint PioSetCurrentOutbufSge = 0x1800;
    private const uint PioSetCurrentOutbufSgeOffset = 0x1808;

    private const int NotifierSslaDone = 0;
    private const uint NotifierBaseOffset = 2;
    private const uint NotifierCountPerVoice = 4;

    // Voice list registers: top/current/next for 2D, 3D and MP (xemu voice_list_regs).
    private static readonly uint[] VoiceListTop = { 0x2054, 0x2060, 0x206C };
    private static readonly uint[] VoiceListCurrent = { 0x2058, 0x2064, 0x2070 };
    private static readonly uint[] VoiceListNext = { 0x205C, 0x2068, 0x2074 };

    /// <summary>Voice-processor frame period (xemu EP_FRAME_US): 256 samples at 48 kHz.</summary>
    private const ulong FrameCycles = (733333333ul * 256ul) / 48000ul;

    /// <summary>Samples consumed per APU frame (the existing XGrid frame quantum).</summary>
    private const uint FrameSamples = 256;

    private readonly uint[] _regs = new uint[WindowSize / 4];
    private readonly IApuMemory _memory;
    private readonly List<ushort> _pendingIdleVoices = new();
    private readonly ulong[] _voiceLocked = new ulong[MaxVoices / 64];
    private readonly uint[] _sslBase = new uint[MaxVoices * 2];
    private readonly byte[] _sslCount = new byte[MaxVoices * 2];
    private readonly int[] _sslIndex = new int[MaxVoices];
    private readonly int[] _sslSeg = new int[MaxVoices];
    private readonly ulong[] _frameVisited = new ulong[MaxVoices / 64];
    private uint _sslBasePage;
    private uint _inbufSgeHandle;
    private uint _outbufSgeHandle;
    private ulong _frameRemainder;
    private bool _gpRunning;
    private bool _epRunning;
    private uint _epFrameDiv;
    private uint _framesSinceWake;

    /// <summary>Creates the APU in its power-on state.</summary>
    public McpxApuDevice(IApuMemory memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        Reset();
    }

    /// <summary>Raised when the APU PCI interrupt line changes state.</summary>
    public event Action<bool>? InterruptChanged;

    /// <summary>Gets the device name used in traces and diagnostics (<c>MCPX APU</c>).</summary>
#pragma warning disable CA1822
    public string Name => "MCPX APU";
#pragma warning restore CA1822

    /// <summary>Gets the number of register reads, for diagnostics.</summary>
    public long ReadCount { get; private set; }

    /// <summary>Gets the number of register writes, for diagnostics.</summary>
    public long WriteCount { get; private set; }

    /// <summary>Gets the number of voice-off completions raised, for diagnostics.</summary>
    public long VoiceOffCompletions { get; private set; }

    /// <summary>Gets the number of SSL segment completions raised, for diagnostics.</summary>
    public long SegmentCompletions { get; private set; }

    /// <summary>Gets the number of notifier statuses written, for diagnostics.</summary>
    public long NotifierWrites { get; private set; }

    /// <summary>Gets the number of DSP mixer blocks absorbed, for diagnostics.</summary>
    public long DspAbsorbs { get; private set; }

    /// <summary>Gets the number of DSP subunit service (ISTS bit3) raises, for diagnostics.</summary>
    public long DspBit3Raises { get; private set; }

    /// <summary>Gets whether the GP DSP engine is running (GPRST run bits set).</summary>
    public bool GpRunning => _gpRunning;

    /// <summary>Gets whether the EP DSP engine is running (EPRST run bits set).</summary>
    public bool EpRunning => _epRunning;

    /// <summary>Gets the interrupt status register, for diagnostics.</summary>
    public uint InterruptStatus => _regs[IsTs >> 2];

    /// <summary>Gets the interrupt enable register, for diagnostics.</summary>
    public uint InterruptEnable => _regs[Ien >> 2];

    /// <summary>Gets the front-end control register, for diagnostics.</summary>
    public uint FrontEndControl => _regs[Fectl >> 2];

    /// <summary>Gets the voice-state page physical address, for diagnostics.</summary>
    public uint VoicePageAddress => _regs[VpVaddr >> 2];

    /// <summary>Gets whether the APU interrupt line is asserted, for diagnostics.</summary>
    public bool InterruptAsserted { get; private set; }

    /// <summary>Gets a bounded log of interrupt-relevant events, for diagnostics.</summary>
    public IReadOnlyList<string> EventLog => _eventLog;

    private readonly List<string> _eventLog = new();

    /// <summary>Returns the APU to its power-on state: clears registers, voice-lock and SSL state, empties the pending idle-voice queue, and deasserts the interrupt line.</summary>
    public void Reset()
    {
        Array.Clear(_regs);
        _pendingIdleVoices.Clear();
        Array.Clear(_voiceLocked);
        Array.Clear(_sslBase);
        Array.Clear(_sslCount);
        Array.Clear(_sslIndex);
        Array.Clear(_sslSeg);
        _sslBasePage = 0;
        _inbufSgeHandle = 0;
        _outbufSgeHandle = 0;
        _frameRemainder = 0;
        _gpRunning = false;
        _epRunning = false;
        _epFrameDiv = 0;
        _framesSinceWake = MinWakeGapFrames;
        SetAsserted(false);
    }

    /// <summary>Saves the register file, voice-processor SSL/lock state, and counters.</summary>
    /// <remarks>The guest-memory voice-state page is part of the RAM snapshot and is not
    /// serialized here. The guest-memory adapter is host wiring and is not serialized.
    /// InterruptChanged is not fired on load; the PIC latch is restored separately.</remarks>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        for (int i = 0; i < _regs.Length; i++)
        {
            writer.Write(_regs[i]);
        }

        writer.Write(_pendingIdleVoices.Count);
        foreach (ushort voice in _pendingIdleVoices)
        {
            writer.Write(voice);
        }

        for (int i = 0; i < _voiceLocked.Length; i++)
        {
            writer.Write(_voiceLocked[i]);
        }

        for (int i = 0; i < _sslBase.Length; i++)
        {
            writer.Write(_sslBase[i]);
            writer.Write(_sslCount[i]);
        }

        for (int i = 0; i < _sslIndex.Length; i++)
        {
            writer.Write(_sslIndex[i]);
            writer.Write(_sslSeg[i]);
        }

        writer.Write(_sslBasePage);
        writer.Write(_inbufSgeHandle);
        writer.Write(_outbufSgeHandle);
        writer.Write(_frameRemainder);
        writer.Write(_gpRunning);
        writer.Write(_epRunning);
        writer.Write(_epFrameDiv);
        writer.Write(_framesSinceWake);
        writer.Write(DspAbsorbs);
        writer.Write(DspBit3Raises);
        writer.Write(ReadCount);
        writer.Write(WriteCount);
        writer.Write(VoiceOffCompletions);
        writer.Write(SegmentCompletions);
        writer.Write(NotifierWrites);
        writer.Write(InterruptAsserted);
        writer.Write(_eventLog.Count);
        foreach (string entry in _eventLog)
        {
            writer.Write(entry);
        }
    }

    /// <summary>Restores the register file, voice-processor state, and counters.</summary>
    /// <remarks>V2 snapshots (which predate DSP run-state) load with run-state
    /// derived from the stored GPRST/EPRST registers, so restores such as the 007
    /// park snapshot resume a running engine.</remarks>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion && version != SnapshotVersionV2)
        {
            throw new InvalidDataException($"Unsupported APU snapshot version {version}.");
        }

        for (int i = 0; i < _regs.Length; i++)
        {
            _regs[i] = reader.ReadUInt32();
        }

        int pending = reader.ReadInt32();
        if (pending < 0 || pending > MaxVoices)
        {
            throw new InvalidDataException("Bad APU pending-voice snapshot.");
        }

        _pendingIdleVoices.Clear();
        for (int i = 0; i < pending; i++)
        {
            _pendingIdleVoices.Add(reader.ReadUInt16());
        }

        for (int i = 0; i < _voiceLocked.Length; i++)
        {
            _voiceLocked[i] = reader.ReadUInt64();
        }

        for (int i = 0; i < _sslBase.Length; i++)
        {
            _sslBase[i] = reader.ReadUInt32();
            _sslCount[i] = reader.ReadByte();
        }

        for (int i = 0; i < _sslIndex.Length; i++)
        {
            _sslIndex[i] = reader.ReadInt32();
            _sslSeg[i] = reader.ReadInt32();
        }

        _sslBasePage = reader.ReadUInt32();
        _inbufSgeHandle = reader.ReadUInt32();
        _outbufSgeHandle = reader.ReadUInt32();
        _frameRemainder = reader.ReadUInt64();
        if (version == SnapshotVersionV2)
        {
            _gpRunning = (_regs[GpRst >> 2] & DspRunBits) == DspRunBits;
            _epRunning = (_regs[EpRst >> 2] & DspRunBits) == DspRunBits;
            _epFrameDiv = 0;
            _framesSinceWake = MinWakeGapFrames;
            DspAbsorbs = 0;
            DspBit3Raises = 0;
        }
        else
        {
            _gpRunning = reader.ReadBoolean();
            _epRunning = reader.ReadBoolean();
            _epFrameDiv = reader.ReadUInt32();
            _framesSinceWake = reader.ReadUInt32();
            DspAbsorbs = reader.ReadInt64();
            DspBit3Raises = reader.ReadInt64();
        }

        ReadCount = reader.ReadInt64();
        WriteCount = reader.ReadInt64();
        VoiceOffCompletions = reader.ReadInt64();
        SegmentCompletions = reader.ReadInt64();
        NotifierWrites = reader.ReadInt64();
        InterruptAsserted = reader.ReadBoolean();
        int events = reader.ReadInt32();
        if (events < 0 || events > 128)
        {
            throw new InvalidDataException("Bad APU event-log snapshot.");
        }

        _eventLog.Clear();
        for (int i = 0; i < events; i++)
        {
            _eventLog.Add(reader.ReadString());
        }
    }

    private const int SnapshotVersion = 3;
    private const int SnapshotVersionV2 = 2;

    /// <summary>
    /// Advances voice-processor frame timing. Each frame walks the voice lists, advances
    /// active voices through their buffer/SSL data, reports notifiers on segment completion
    /// and sends SE2FE_IDLE_VOICE for inactive voices (xemu mcpx_apu_vp_frame).
    /// </summary>
    public void Advance(ulong cycles)
    {
        _frameRemainder += cycles;
        while (_frameRemainder >= FrameCycles)
        {
            _frameRemainder -= FrameCycles;
            ProcessFrame();
            ProcessDspFrame();
        }
    }

    /// <summary>
    /// Advances the GP/EP DSP engines one frame. A running GP engine absorbs a
    /// pending mixer block through its scratch mapping and raises the DSP subunit
    /// service request (ISTS bit3) on a debounced submission wake; the EP engine
    /// only advances its frame divider here (its FIFO drain needs an S/PDIF sink).
    /// </summary>
    private void ProcessDspFrame()
    {
        _epFrameDiv++;
        if (_framesSinceWake < MinWakeGapFrames)
        {
            _framesSinceWake++;
        }

        if (!_gpRunning || !IsStateWordMapped())
        {
            return;
        }

        uint state = _memory.Read32(DspStateWordPhys);
        if (state != DspStateSubmitPosted && state != DspStateParamsLoaded)
        {
            return;
        }

        _memory.Write32(DspStateWordPhys, 0u);
        DspAbsorbs++;
        Log($"dsp-absorb {state}->0 n={DspAbsorbs}");
        if (state == DspStateSubmitPosted && _framesSinceWake >= MinWakeGapFrames)
        {
            _framesSinceWake = 0;
            RaiseDspService();
        }
    }

    /// <summary>
    /// Reports whether the DSP mixer-block mailbox page is covered by the guest's
    /// GP scratch (SGE) mapping. The DSP can only touch guest RAM through this
    /// mapping (xemu gp_scratch_rw), so an unprogrammed title never absorbs.
    /// </summary>
    private bool IsStateWordMapped()
    {
        uint table = _regs[GpsAddr >> 2];
        if (table == 0)
        {
            return false;
        }

        uint wantPage = DspStateWordPhys & PageMask;
        for (int i = 0; i < DspSgeScanEntries; i++)
        {
            if ((_memory.Read32(table + (uint)(i * 8)) & PageMask) == wantPage)
            {
                return true;
            }
        }

        return false;
    }

    private void RaiseDspService()
    {
        _regs[IsTs >> 2] |= IsTsDspService;
        DspBit3Raises++;
        Log($"dsp bit3 raise n={DspBit3Raises}");
        UpdateIrq();
    }

    private void ProcessFrame()
    {
        Array.Clear(_frameVisited);
        for (int list = 0; list < VoiceListTop.Length; list++)
        {
            uint currentReg = VoiceListCurrent[list] >> 2;
            uint nextReg = VoiceListNext[list] >> 2;
            _regs[currentReg] = _regs[VoiceListTop[list] >> 2];
            for (uint i = 0; _regs[currentReg] != HandleMask16; i++)
            {
                if (i >= MaxVoices)
                {
                    Log(
                        $"voice list overflow list={list} top={_regs[VoiceListTop[list] >> 2]:X} v={_regs[currentReg]:X}");
                    break;
                }

                uint voice = _regs[currentReg];
                if (IsFrameVisited(voice))
                {
                    Log($"voice list cycle list={list} v={voice:X}");
                    break;
                }

                MarkFrameVisited(voice);
                _regs[nextReg] = GetVoiceMask(voice, VoiceTarPitchLink, HandleMask16);
                if ((GetVoiceMask(voice, VoiceParState, uint.MaxValue) & VoiceStateActiveVoice) == 0)
                {
                    DeliverIdleVoice((ushort)voice);
                }
                else
                {
                    AdvanceVoice(voice);
                }

                _regs[currentReg] = _regs[nextReg];
            }
        }

        if (_pendingIdleVoices.Count > 0)
        {
            ushort voice = _pendingIdleVoices[0];
            _pendingIdleVoices.RemoveAt(0);
            DeliverIdleVoice(voice);
        }
    }

    private bool IsFrameVisited(uint voice)
    {
        return (_frameVisited[voice / 64] & (1UL << (int)(voice % 64))) != 0;
    }

    private void MarkFrameVisited(uint voice)
    {
        _frameVisited[voice / 64] |= 1UL << (int)(voice % 64);
    }

    private void AdvanceVoice(uint voice)
    {
        uint state = GetVoiceMask(voice, VoiceParState, uint.MaxValue);
        if ((state & VoiceStatePaused) != 0)
        {
            return;
        }

        // Cleared when the first sample is played (xemu voice_get_samples).
        SetVoiceMask(voice, VoiceParState, VoiceStateNewVoice, 0);

        uint fmt = GetVoiceMask(voice, VoiceCfgFmt, uint.MaxValue);
        bool stream = (fmt & VoiceFmtDataType) != 0;
        uint cbo = GetVoiceMask(voice, VoiceParOffset, OffsetMask24);
        if (stream)
        {
            AdvanceStreamVoice(voice, fmt, cbo);
        }
        else
        {
            AdvanceBufferVoice(voice, fmt, cbo);
        }
    }

    private void AdvanceStreamVoice(uint voice, uint fmt, uint cbo)
    {
        int index = _sslIndex[voice];
        int segment = _sslSeg[voice];
        int count = _sslCount[(voice * 2) + index];
        if (count == 0)
        {
            // The stream has ended (xemu voice_get_samples count == 0 path).
            SetVoiceMask(voice, VoiceParOffset, OffsetMask24, 0);
            _sslSeg[voice] = 0;
            if ((fmt & VoiceFmtPersist) == 0)
            {
                _sslIndex[voice] = 0;
                CompleteVoiceOff(voice, queueIdle: false);
            }
            else
            {
                WriteNotifierStatus(voice, NotifierSslaDone + index);
            }

            return;
        }

        uint page = _sslBase[(voice * 2) + index] + (uint)segment;
        uint segmentLength = _memory.Read32(_regs[VpSslAddr >> 2] + (page * 8) + 4);
        uint segmentSamples = segmentLength & HandleMask16;
        uint ebo = segmentSamples > 0 ? segmentSamples - 1 : 0;
        cbo += FrameSamples;
        if (cbo >= ebo)
        {
            _sslSeg[voice] = segment + 1;
            cbo = 0;
            if (_sslSeg[voice] < count)
            {
                // The next segment in this SSL continues on the next frame.
            }
            else
            {
                _sslIndex[voice] = index ^ 1;
                _sslSeg[voice] = 0;
                WriteNotifierStatus(voice, NotifierSslaDone + index);
                SegmentCompletions++;
            }
        }

        SetVoiceMask(voice, VoiceParOffset, OffsetMask24, cbo);
    }

    private void AdvanceBufferVoice(uint voice, uint fmt, uint cbo)
    {
        uint ebo = GetVoiceMask(voice, VoiceParNext, OffsetMask24);
        cbo += FrameSamples;
        if (cbo >= ebo)
        {
            if ((fmt & VoiceFmtLoop) != 0)
            {
                cbo = GetVoiceMask(voice, VoiceCurPshSample, OffsetMask24);
            }
            else
            {
                cbo = ebo;
                CompleteVoiceOff(voice, queueIdle: false);
            }
        }

        SetVoiceMask(voice, VoiceParOffset, OffsetMask24, cbo);
    }

    private void CompleteVoiceOff(uint voice, bool queueIdle)
    {
        SetVoiceMask(voice, VoiceParState, VoiceStateActiveVoice, 0);
        bool stream = (GetVoiceMask(voice, VoiceCfgFmt, VoiceFmtDataType) != 0);
        int notifier = NotifierSslaDone;
        if (stream)
        {
            notifier += _sslIndex[voice];
        }

        WriteNotifierStatus(voice, notifier);
        if (queueIdle)
        {
            // The voice processor reports the idle voice on its next frame, not
            // synchronously: delivering it now would race the front end's in-flight
            // operation counter.
            _pendingIdleVoices.Add((ushort)voice);
        }

        VoiceOffCompletions++;
    }

    /// <summary>Reads one register from the 512 KB APU window at the given absolute address; the VP PIO-free register always reports the queue empty (xemu-faithful) and sub-word reads extract the addressed byte lane.</summary>
    /// <param name="address">Absolute address inside the APU window.</param>
    /// <param name="size">Access size in bytes (1, 2, or 4).</param>
    /// <returns>The register value truncated and shifted to the addressed lane.</returns>
    public uint Read(uint address, int size)
    {
        uint offset = address - BaseAddress;
        ReadCount++;
        uint word = offset switch
        {
            // xemu's VP does not simulate the PIO queue and always reports it empty.
            VpPioFree => PioFreeCount,
            // The EP frame counter, in mixed samples (xemu apu.c:
            // ep_frame_div * NUM_SAMPLES_PER_FRAME). The guest watches it as a
            // playback clock, so it must advance with the frame divider.
            XgsCnt => _epFrameDiv * SamplesPerFrame,
            < WindowSize => _regs[offset >> 2],
            _ => 0,
        };
        uint shift = (offset & 3u) * 8;
        return size switch
        {
            1 => (word >> (int)shift) & 0xFFu,
            2 => (word >> (int)shift) & 0xFFFFu,
            _ => word,
        };
    }

    /// <summary>Writes one register in the 512 KB APU window at the given absolute address; sub-word writes read-modify-write only the addressed byte lane and out-of-window writes are ignored.</summary>
    /// <param name="address">Absolute address inside the APU window.</param>
    /// <param name="size">Access size in bytes (1, 2, or 4).</param>
    /// <param name="value">Value to write, in the low <paramref name="size"/> bytes.</param>
    public void Write(uint address, int size, uint value)
    {
        uint offset = address - BaseAddress;
        WriteCount++;
        if (offset >= WindowSize)
        {
            return;
        }

        uint aligned = offset & ~3u;
        uint shift = (offset & 3u) * 8;
        uint mask = size switch
        {
            1 => 0xFFu << (int)shift,
            2 => 0xFFFFu << (int)shift,
            _ => uint.MaxValue,
        };
        WriteRegister(aligned, size == 4 ? value : (_regs[aligned >> 2] & ~mask) | ((value << (int)shift) & mask));
    }

    private void WriteRegister(uint offset, uint value)
    {
        switch (offset)
        {
            case IsTs:
                // Writing ISTS clears the written bits, then the IRQ state is recomputed.
                _regs[offset >> 2] &= ~value;
                Log($"ISTS ack {value:X8} -> ist={_regs[offset >> 2]:X8}");
                UpdateIrq();
                break;
            case Fectl:
                _regs[offset >> 2] = value;
                Log($"FECTL write {value:X8}");
                UpdateIrq();
                break;
            case FemMemData:
                // Magic write: store the value into guest memory at FEMEMADDR (xemu apu.c).
                _memory.Write32(_regs[FemMemAddr >> 2], value);
                _regs[offset >> 2] = value;
                break;
            case GpRst:
                DspResetWrite(isGp: true, _regs[offset >> 2], value);
                _regs[offset >> 2] = value;
                break;
            case EpRst:
                DspResetWrite(isGp: false, _regs[offset >> 2], value);
                _regs[offset >> 2] = value;
                _epFrameDiv = 0;
                break;
            default:
                if (offset >= 0x20000 && WriteVoiceProcessor(offset - 0x20000, value))
                {
                    break;
                }

                _regs[offset >> 2] = value;
                break;
        }
    }

    /// <summary>Dispatches a VP front-end PIO method. Returns true when the method is handled.</summary>
    private bool WriteVoiceProcessor(uint method, uint value)
    {
        switch (method)
        {
            case PioFree:
                return true;
            case PioSetAntecedentVoice:
                _regs[0x1118 >> 2] = value;
                return true;
            case PioVoiceOn:
                VoiceOn(value);
                _regs[VpVoiceOn >> 2] = value;
                return true;
            case PioVoiceOff:
                VoiceOff(value & HandleMask16);
                _regs[VpVoiceOff >> 2] = value;
                return true;
            case PioVoiceRelease:
                VoiceRelease(value & HandleMask16);
                return true;
            case PioVoicePause:
                SetVoiceMask(value & HandleMask16, VoiceParState, VoiceStatePaused,
                    (value & (1u << 18)) != 0 ? 1u : 0u);
                return true;
            case PioSetCurrentVoice:
                _regs[0x1110 >> 2] = value;
                return true;
            case PioVoiceLock:
                VoiceLock(_regs[0x1110 >> 2], (value & 1) != 0);
                return true;
            case PioSetVoiceCfgVbin:
                return WriteCurrentVoice(0x00, value);
            case PioSetVoiceCfgFmt:
                return WriteCurrentVoice(VoiceCfgFmt, value);
            case PioSetVoiceCfgEnv0:
                return WriteCurrentVoice(0x08, value);
            case PioSetVoiceCfgEnva:
                return WriteCurrentVoice(VoiceCfgEnva, value);
            case PioSetVoiceCfgEnv1:
                return WriteCurrentVoice(0x10, value);
            case PioSetVoiceCfgEnvf:
                return WriteCurrentVoice(VoiceCfgEnvf, value);
            case PioSetVoiceCfgMisc:
                return WriteCurrentVoice(0x18, value);
            case PioSetVoiceTarHrtf:
                SetVoiceMask(_regs[0x1110 >> 2], 0x1C, HandleMask16, value);
                return true;
            case PioSetVoiceSslA:
                SetSsl(_regs[0x1110 >> 2], 0, value);
                return true;
            case PioSetVoiceSslB:
                SetSsl(_regs[0x1110 >> 2], 1, value);
                return true;
            case PioSetVoiceTarVola:
                return WriteCurrentVoice(0x60, value);
            case PioSetVoiceTarVolb:
                return WriteCurrentVoice(0x64, value);
            case PioSetVoiceTarVolc:
                return WriteCurrentVoice(0x68, value);
            case PioSetVoiceLfoEnv:
                return WriteCurrentVoice(0x6C, value);
            case PioSetVoiceTarFca:
                return WriteCurrentVoice(0x74, value);
            case PioSetVoiceTarFcb:
                return WriteCurrentVoice(0x78, value);
            case PioSetVoiceTarPitch:
                {
                    // The low half of TAR_PITCH_LINK keeps the linked next-voice handle.
                    uint voice = _regs[0x1110 >> 2];
                    uint link = GetVoiceMask(voice, VoiceTarPitchLink, HandleMask16);
                    SetVoiceMask(voice, VoiceTarPitchLink, uint.MaxValue, (value & 0xFFFF0000u) | link);
                    return true;
                }

            case PioSetVoiceCfgBufBase:
                return WriteCurrentVoice(VoiceCurPslStart, value & OffsetMask24);
            case PioSetVoiceCfgBufLbo:
                return WriteCurrentVoice(VoiceCurPshSample, value & OffsetMask24);
            case PioSetVoiceBufCbo:
                return WriteCurrentVoice(VoiceParOffset, value & OffsetMask24);
            case PioSetVoiceCfgBufEbo:
                return WriteCurrentVoice(VoiceParNext, value & OffsetMask24);
            case PioSetCurrentSsl:
                _sslBasePage = value;
                return true;
            case PioSetCurrentInbufSge:
                _inbufSgeHandle = value;
                return true;
            case PioSetCurrentInbufSgeOffset:
                WriteSgeEntry(_inbufSgeHandle, value & 0xFFFFF000u);
                return true;
            case PioSetCurrentOutbufSge:
                _outbufSgeHandle = value;
                return true;
            case PioSetCurrentOutbufSgeOffset:
                WriteSgeEntry(_outbufSgeHandle, value & 0xFFFFF000u);
                return true;
            default:
                if (method >= PioSetSslSegmentOffset && method <= PioSetSslSegmentEnd)
                {
                    uint address = _regs[VpSslAddr >> 2] + (_sslBasePage * 8) + (method - PioSetSslSegmentOffset);
                    _memory.Write32(address, value);
                    return true;
                }

                if ((method >= PioSetOutbufBa && method < PioSetOutbufBa + 0x40) ||
                    (method >= PioSetOutbufLen && method < PioSetOutbufLen + 0x40))
                {
                    // xemu ignores the outbuf base/length methods.
                    return true;
                }

                return false;
        }
    }

    private bool WriteCurrentVoice(uint voiceOffset, uint value)
    {
        uint voice = _regs[0x1110 >> 2];
        if (voice >= MaxVoices)
        {
            return true;
        }

        SetVoiceMask(voice, voiceOffset, uint.MaxValue, value);
        return true;
    }

    private void SetSsl(uint voice, int ssl, uint value)
    {
        if (voice >= MaxVoices)
        {
            return;
        }

        _sslBase[(voice * 2) + ssl] = value >> 8;
        _sslCount[(voice * 2) + ssl] = (byte)(value & 0xFF);
    }

    private void WriteSgeEntry(uint handle, uint value)
    {
        _memory.Write32(_regs[VpSgeAddr >> 2] + (handle * 8), value);
    }

    private void VoiceOn(uint value)
    {
        uint voice = value & HandleMask16;
        if (voice >= MaxVoices)
        {
            return;
        }

        bool locked = IsVoiceLocked(voice);
        if (!locked)
        {
            VoiceLock(voice, true);
        }

        uint feav = _regs[0x1118 >> 2];
        uint list = (feav >> 16) & 3;
        if (list != 0)
        {
            // Voice is added to the top of the selected list.
            uint topReg = VoiceListTop[list - 1] >> 2;
            SetVoiceMask(voice, VoiceTarPitchLink, HandleMask16, _regs[topReg]);
            _regs[topReg] = voice;
        }
        else
        {
            // Voice is added after the antecedent voice.
            uint antecedent = feav & HandleMask16;
            if (antecedent < MaxVoices)
            {
                uint next = GetVoiceMask(antecedent, VoiceTarPitchLink, HandleMask16);
                SetVoiceMask(voice, VoiceTarPitchLink, HandleMask16, next);
                SetVoiceMask(antecedent, VoiceTarPitchLink, HandleMask16, voice);
            }
        }

        SetVoiceMask(voice, VoiceParOffset, OffsetMask24, 0);
        _sslIndex[voice] = 0;
        _sslSeg[voice] = 0;

        // Initial envelope state (xemu NV1BA0_PIO_VOICE_ON_ENVA/ENVF).
        uint envf = (value >> 24) & VoiceEnvelopeMask;
        uint enva = (value >> 28) & VoiceEnvelopeMask;
        SetEnvelopeCount(voice, enva, 0x08, VoiceCfgEnva, 0x0000FFFF);
        SetEnvelopeCount(voice, envf, 0x10, VoiceCfgEnvf, 0xFFFF0000);

        SetVoiceMask(voice, VoiceParState, VoiceStateActiveVoice, 1);

        if (!locked)
        {
            VoiceLock(voice, false);
        }
    }

    private void SetEnvelopeCount(uint voice, uint envelope, uint delayOffset, uint holdOffset, uint countMask)
    {
        uint count;
        switch (envelope)
        {
            case VoiceEnvelopeDelay:
                count = GetVoiceMask(voice, delayOffset, 0xFFF << 12) >> 12;
                count *= 16;
                break;
            case VoiceEnvelopeAttack:
                count = 0;
                break;
            case VoiceEnvelopeHold:
                count = GetVoiceMask(voice, holdOffset, 0xFFF << 12) >> 12;
                count *= 16;
                break;
            default:
                return;
        }

        SetVoiceMask(voice, 0x34, countMask, count);
    }

    private void VoiceRelease(uint voice)
    {
        if (voice >= MaxVoices)
        {
            return;
        }

        uint rr = GetVoiceMask(voice, 0x6C, 0xFFF);
        SetVoiceMask(voice, 0x34, 0x0000FFFF, rr * 16);
        SetVoiceMask(voice, VoiceParState, 0xF0000000u, 6u << 28);

        rr = GetVoiceMask(voice, 0x18, 0xFFF);
        SetVoiceMask(voice, 0x34, 0xFFFF0000, rr * 16);
        SetVoiceMask(voice, VoiceParState, 0x0F000000u, 6u << 24);
    }

    private void VoiceOff(uint voice)
    {
        if (voice >= MaxVoices)
        {
            return;
        }

        Log($"voice-off {voice}");
        CompleteVoiceOff(voice, queueIdle: true);
    }

    /// <summary>
    /// Tracks GP/EP DSP run-state with xemu's proc_rst_write semantics
    /// (gp_ep.c): clearing either run bit resets the engine; the rising edge of
    /// both bootstraps it (program load). A fresh bootstrap is service-eligible.
    /// </summary>
    private void DspResetWrite(bool isGp, uint oldValue, uint value)
    {
        bool wasRunning = (oldValue & DspRunBits) == DspRunBits;
        bool running = (value & DspRunBits) == DspRunBits;
        if (!running)
        {
            if (isGp)
            {
                _gpRunning = false;
            }
            else
            {
                _epRunning = false;
            }

            if (wasRunning)
            {
                Log(isGp ? "GP DSP stop" : "EP DSP stop");
            }

            return;
        }

        if (wasRunning)
        {
            return;
        }

        if (isGp)
        {
            _gpRunning = true;
        }
        else
        {
            _epRunning = true;
        }

        _framesSinceWake = MinWakeGapFrames;
        Log(isGp ? "GP DSP start" : "EP DSP start");
    }

    private void VoiceLock(uint voice, bool locked)
    {
        if (voice >= MaxVoices)
        {
            return;
        }

        ulong mask = 1UL << (int)(voice % 64);
        if (locked)
        {
            _voiceLocked[voice / 64] |= mask;
        }
        else
        {
            _voiceLocked[voice / 64] &= ~mask;
        }
    }

    private bool IsVoiceLocked(uint voice)
    {
        ulong mask = 1UL << (int)(voice % 64);
        return (_voiceLocked[voice / 64] & mask) != 0;
    }

    private uint GetVoiceMask(uint voice, uint offset, uint mask)
    {
        uint stateAddress = _regs[VpVaddr >> 2] + (voice * VoiceSize) + offset;
        return (_memory.Read32(stateAddress) & mask) >> BitOperations.TrailingZeroCount(mask);
    }

    private void SetVoiceMask(uint voice, uint offset, uint mask, uint value)
    {
        uint stateAddress = _regs[VpVaddr >> 2] + (voice * VoiceSize) + offset;
        uint current = _memory.Read32(stateAddress);
        int shift = BitOperations.TrailingZeroCount(mask);
        _memory.Write32(stateAddress, (current & ~mask) | ((value << shift) & mask));
    }

    private void DeliverIdleVoice(ushort voice)
    {
        if ((_regs[FetForce1 >> 2] & FetForce1Se2FeIdleVoice) == 0)
        {
            return;
        }

        Log($"idle-voice {voice}");
        _regs[FedecMeth >> 2] = Se2FeIdleVoice;
        _regs[FedecParam >> 2] = voice;
        _regs[Fectl >> 2] = (_regs[Fectl >> 2] & ~(FectlFemethmode | FectlFetrapreason))
                            | FectlFemethmodeTrapped | FectlFetrapreasonRequested;
        UpdateIrq();
    }

    private void WriteNotifierStatus(uint voice, int notifier)
    {
        // FENADDR + 16 * (2 + voice * 4 + notifier), status in the last byte (xemu set_notify_status).
        uint notifyOffset = _regs[FenAddr >> 2]
                            + (16u * (NotifierBaseOffset + (voice * NotifierCountPerVoice) + (uint)notifier));
        _memory.Write8(notifyOffset + 15, NotificationDoneSuccess);
        _memory.Write8(notifyOffset - 1, 1);
        _regs[IsTs >> 2] |= IsTsFevintsts | IsTsFenintsts;
        NotifierWrites++;
        UpdateIrq();
    }

    private void UpdateIrq()
    {
        if ((_regs[Fectl >> 2] & FectlFemethmode) == FectlFemethmodeTrapped)
        {
            _regs[IsTs >> 2] |= IsTsFeTintsts;
        }

        uint ists = _regs[IsTs >> 2];
        uint ien = _regs[Ien >> 2];
        if ((ien & IsTsGintsts) != 0 && (ists & ~IsTsGintsts & ien) != 0)
        {
            _regs[IsTs >> 2] = ists | IsTsGintsts;
            Log($"raise ist={_regs[IsTs >> 2]:X8} ien={ien:X8} fectl={_regs[Fectl >> 2]:X8}");
            SetAsserted(true);
        }
        else
        {
            _regs[IsTs >> 2] = ists & ~IsTsGintsts;
            SetAsserted(false);
        }
    }

    private void SetAsserted(bool asserted)
    {
        if (InterruptAsserted == asserted)
        {
            return;
        }

        InterruptAsserted = asserted;
        Log(asserted ? "irq assert" : "irq deassert");
        InterruptChanged?.Invoke(asserted);
    }

    private void Log(string entry)
    {
        if (_eventLog.Count >= 128)
        {
            _eventLog.RemoveAt(0);
        }

        _eventLog.Add(entry);
    }
}
