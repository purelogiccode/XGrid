// SPDX-License-Identifier: GPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/gp_ep.c (LGPL-2.0-or-later),
// hw/xbox/mcpx/apu/dsp/dsp.c and hw/xbox/mcpx/apu/dsp/dsp_c.c (GPL-2.0-or-later),
// and hw/xbox/mcpx/apu/fpconv.h (LGPL-2.0-or-later).
// Original copyright (c) 2012 espes; (c) 2015 espes; (c) 2018-2019 Jannik Vogel;
// (c) 2019-2025 Matt Borgerson.

using System;
using System.IO;

namespace XGrid.Audio.Dsp;

/// <summary>
/// Global APU register access for the DSP glue: the scratch/FIFO SGE and ring
/// registers live in the APU's global window (xemu <c>d->regs[...]</c>), not in
/// the 64 KB GP/EP sub-windows.
/// </summary>
public interface IApuDspRegisters
{
    /// <summary>Reads a global APU register by byte offset.</summary>
    /// <param name="offset">Byte offset inside the global APU window.</param>
    /// <returns>The register value.</returns>
    uint Read(uint offset);

    /// <summary>Writes a global APU register by byte offset.</summary>
    /// <param name="offset">Byte offset inside the global APU window.</param>
    /// <param name="value">The value to store.</param>
    void Write(uint offset, uint value);
}

/// <summary>
/// Host sink for the EP output FIFO (xemu <c>ep_sink_samples</c>). Return true
/// when the samples were consumed; the DSP then writes silence to the guest
/// buffer. Return false to leave the samples in guest memory (the AC97 monitor
/// path, where the codec would read them).
/// </summary>
/// <param name="buffer">The sample bytes.</param>
/// <param name="offset">Offset of the samples in <paramref name="buffer"/>.</param>
/// <param name="length">Sample byte count.</param>
/// <returns>True when the host consumed the samples.</returns>
public delegate bool ApuDspSampleSink(byte[] buffer, int offset, int length);

/// <summary>
/// One MCPX APU DSP window and frame engine (the GP or the EP), ported from
/// xemu 0.8.139 <c>gp_ep.c</c>, <c>dsp.c</c> and <c>dsp_c.c</c>.
/// </summary>
/// <remarks>
/// The 64 KB sub-window maps X (and the GP mix buffer), Y and P memory of a
/// <see cref="Dsp56300Core"/> plus the processor's own reset register. The
/// global APU registers name the guest scatter/gather tables that the scratch
/// and FIFO DMA windows copy through, and the peripheral space
/// (0xFFFF80-0xFFFFFF) carries the halt, interrupt and DMA registers the guest
/// microcode polls. The frame loop writes the VP mix bins into the GP mix
/// buffer, runs the microcode until it halts (xemu's realtime loop) and runs
/// the EP once per eight APU frames.
/// </remarks>
public sealed class McpxApuDsp
{
    /// <summary>GP sub-window base in the APU window.</summary>
    public const uint GpWindowBase = 0x30000;

    /// <summary>EP sub-window base in the APU window.</summary>
    public const uint EpWindowBase = 0x50000;

    /// <summary>Sub-window size in bytes.</summary>
    public const uint WindowSize = 0x10000;

    /// <summary>Reset register offset inside a sub-window (xemu <c>NV_PAPU_GPRST</c>/<c>EPRST</c>).</summary>
    public const uint ResetRegister = 0xFFFC;

    /// <summary>GP mix-buffer base in X memory (xemu <c>GP_DSP_MIXBUF_BASE</c>).</summary>
    public const uint MixBufferBase = 0x1400;

    /// <summary>Mix bins per frame (xemu <c>NUM_MIXBINS</c>).</summary>
    public const int MixBinCount = 32;

    /// <summary>Samples per mix bin per frame (xemu <c>NUM_SAMPLES_PER_FRAME</c>).</summary>
    public const int SamplesPerFrame = 32;

    /// <summary>APU frames per EP DSP frame (xemu <c>ep_frame_div % 8</c>).</summary>
    public const uint EpFrameDivisor = 8;

    /// <summary>Bytes of the DSP program bootstrap (0x800 words).</summary>
    private const int BootstrapBytes = 0x800 * 4;

    private const uint PageSize = 0x1000;
    private const uint FifoValueMask = 0x00FFFFFF;
    private const uint FifoStride = 0x10;
    private const uint ResetRunBits = 0x3;

    // Global APU registers the glue reads (apu_regs.h).
    private const uint GpScratchAddress = 0x2040;
    private const uint GpFifoAddress = 0x2044;
    private const uint EpScratchAddress = 0x2048;
    private const uint EpFifoAddress = 0x204C;
    private const uint GpScratchMaxSge = 0x20D4;
    private const uint GpFifoMaxSge = 0x20D8;
    private const uint EpScratchMaxSge = 0x20DC;
    private const uint EpFifoMaxSge = 0x20E0;

    private const uint GpOutputFifoBase = 0x3024;
    private const uint GpInputFifoBase = 0x3064;
    private const uint EpOutputFifoBase = 0x4024;
    private const uint EpInputFifoBase = 0x4064;
    private const uint OutputFifoCount = 4;
    private const uint InputFifoCount = 2;

    // Peripheral-space registers the microcode polls (dsp.c).
    private const uint InstructionCounterAddress = 0xFFFFB3;
    private const uint HaltAddress = 0xFFFFC4;
    private const uint InterruptStatusAddress = 0xFFFFC5;
    private const uint DmaNextBlockAddress = 0xFFFFD4;
    private const uint DmaStartBlockAddress = 0xFFFFD5;
    private const uint DmaControlAddress = 0xFFFFD6;
    private const uint DmaConfigurationAddress = 0xFFFFD7;

    private const uint InterruptStartFrame = 1u << 1;
    private const uint InterruptDmaEol = 1u << 7;
    private const uint UnmappedPeripheralValue = 0x000ABABA;
    private const int SilenceBytes = 256 * 2 * 2;

    private const int SnapshotVersion = 1;

    private readonly IApuMemory _memory;
    private readonly IApuDspRegisters _registers;
    private readonly uint[] _regs = new uint[WindowSize / 4];
    private readonly byte[] _scratch = new byte[BootstrapBytes];
    private readonly byte[] _silence = new byte[SilenceBytes];
    private uint _interrupts;

    /// <summary>Initializes a new instance of the <see cref="McpxApuDsp"/> class.</summary>
    /// <param name="memory">Guest physical memory (the scatter/gather target).</param>
    /// <param name="registers">The APU's global register window.</param>
    /// <param name="isGp">True for the GP DSP, false for the EP DSP.</param>
    /// <param name="sampleSink">Optional host sink for the EP output FIFO.</param>
    public McpxApuDsp(
        IApuMemory memory,
        IApuDspRegisters registers,
        bool isGp,
        ApuDspSampleSink? sampleSink = null)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _registers = registers ?? throw new ArgumentNullException(nameof(registers));
        SampleSink = sampleSink;
        IsGp = isGp;

        Core = new Dsp56300Core(isGp);
        Dma = new DspDma(Core.ReadMemory, Core.WriteMemory, ScratchReadWrite, FifoReadWrite);
        Core.State.ReadPeripheral = ReadPeripheral;
        Core.State.WritePeripheral = WritePeripheral;
        Core.Log = message => Log?.Invoke(message);
        PowerOnReset();
    }

    /// <summary>Gets a value indicating whether this is the GP DSP.</summary>
    public bool IsGp { get; }

    /// <summary>Gets the DSP core.</summary>
    public Dsp56300Core Core { get; }

    /// <summary>Gets the DMA engine wired to this DSP's memory and windows.</summary>
    public DspDma Dma { get; }

    /// <summary>Gets or sets the host sink for the EP output FIFO (xemu <c>ep_sink_samples</c>).</summary>
    public ApuDspSampleSink? SampleSink { get; set; }

    /// <summary>Gets or sets the diagnostic log sink.</summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public Action<string>? Log { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the frame loop keeps running the
    /// microcode until it halts (xemu's <c>realtime</c> loop). When false one
    /// 1000-cycle slice runs per frame, which is what a deterministic test wants.
    /// </summary>
    public bool Realtime { get; set; } = true;

    /// <summary>
    /// Gets or sets the per-frame cycle ceiling. xemu's loop only ends when the
    /// microcode halts; a runaway program would hang the emulator, so the port
    /// stops at this budget and counts it in <see cref="FrameCycleLimitHits"/>.
    /// </summary>
    public int FrameCycleLimit { get; set; } = 1_000_000;

    /// <summary>Gets the DSP interrupt status word (xemu <c>DSPState.interrupts</c>).</summary>
    public uint Interrupts => _interrupts;

    /// <summary>Gets a value indicating whether the run bits in the reset register are set.</summary>
    public bool IsRunning => (_regs[ResetRegister >> 2] & ResetRunBits) == ResetRunBits;

    /// <summary>Gets the reset-register value latched by the last write.</summary>
    public uint ResetValue => _regs[ResetRegister >> 2];

    /// <summary>Gets the number of frames this DSP has run.</summary>
    public long Frames { get; private set; }

    /// <summary>Gets the number of frames the cycle ceiling ended early.</summary>
    public long FrameCycleLimitHits { get; private set; }

    /// <summary>Gets the number of scratch transfers that named an SGE entry past the table.</summary>
    public long ScratchOutOfRange { get; private set; }

    /// <summary>Gets the number of FIFO transfers that named an unusable ring.</summary>
    public long FifoOutOfRange { get; private set; }

    /// <summary>Gets the number of bytes handed to the sample sink.</summary>
    public long SunkBytes { get; private set; }

    /// <summary>Returns the DSP to its power-on state: memory, registers, DMA and interrupts clear.</summary>
    public void PowerOnReset()
    {
        Array.Clear(_regs);
        Array.Clear(_silence);
        _interrupts = 0;
        Dma.Configuration = 0;
        Dma.Control = 0;
        Dma.StartBlock = 0;
        Dma.NextBlock = 0;
        Dma.Error = false;
        Dma.Eol = false;
        Dma.DmaReadCount = 0;
        Frames = 0;
        FrameCycleLimitHits = 0;
        ScratchOutOfRange = 0;
        FifoOutOfRange = 0;
        SunkBytes = 0;
        ResetCore();
    }

    /// <summary>Resets the DSP core (xemu <c>dsp_reset</c>); memory contents and the interrupt word survive.</summary>
    public void ResetCore()
    {
        Core.Reset();
        Core.State.ExceptionDebugging = false;
    }

    /// <summary>
    /// Loads the DSP program from the scratch window (xemu <c>dsp_c_bootstrap</c>):
    /// 0x800 words through the SGE table, high byte stripped.
    /// </summary>
    public void Bootstrap()
    {
        ScratchReadWrite(_scratch, 0, 0, BootstrapBytes, write: false);
        Core.Bootstrap(_scratch);
    }

    /// <summary>Reads a word from the processor's 64 KB sub-window.</summary>
    /// <param name="offset">Sub-window byte offset (must be 4-byte aligned).</param>
    /// <returns>The word.</returns>
    public uint ReadWindow(uint offset)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, WindowSize);

        uint word;
        if (IsGp)
        {
            if (offset <= 0x3FFF)
            {
                return Core.ReadMemory(Dsp56300State.SpaceX, offset / 4);
            }

            if (offset is >= 0x5000 and <= 0x5FFF)
            {
                return Core.ReadMemory(
                    Dsp56300State.SpaceX,
                    MixBufferBase + ((offset - 0x5000) / 4));
            }

            word = offset is >= 0x6000 and <= 0x7FFF
                ? Core.ReadMemory(Dsp56300State.SpaceY, (offset - 0x6000) / 4)
                : offset is >= 0xA000 and <= 0xEFFF
                    ? Core.ReadMemory(Dsp56300State.SpaceP, (offset - 0xA000) / 4)
                    : _regs[offset >> 2];
            return word;
        }

        if (offset <= 0x2FFF)
        {
            return Core.ReadMemory(Dsp56300State.SpaceX, offset / 4);
        }

        word = offset is >= 0x6000 and <= 0x63FF
            ? Core.ReadMemory(Dsp56300State.SpaceY, (offset - 0x6000) / 4)
            : offset is >= 0xA000 and <= 0xEFFF
                ? Core.ReadMemory(Dsp56300State.SpaceP, (offset - 0xA000) / 4)
                : _regs[offset >> 2];
        return word;
    }

    /// <summary>Writes a word to the processor's 64 KB sub-window.</summary>
    /// <param name="offset">Sub-window byte offset (must be 4-byte aligned).</param>
    /// <param name="value">The word to write.</param>
    public void WriteWindow(uint offset, uint value)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, WindowSize);

        if (IsGp)
        {
            if (offset <= 0x3FFF)
            {
                Core.WriteMemory(Dsp56300State.SpaceX, offset / 4, value);
                return;
            }

            if (offset is >= 0x5000 and <= 0x5FFF)
            {
                Core.WriteMemory(
                    Dsp56300State.SpaceX,
                    MixBufferBase + ((offset - 0x5000) / 4),
                    value);
                return;
            }

            if (offset is >= 0x6000 and <= 0x7FFF)
            {
                Core.WriteMemory(Dsp56300State.SpaceY, (offset - 0x6000) / 4, value);
                return;
            }

            if (offset is >= 0xA000 and <= 0xEFFF)
            {
                Core.WriteMemory(Dsp56300State.SpaceP, (offset - 0xA000) / 4, value);
                return;
            }
        }
        else
        {
            if (offset <= 0x2FFF)
            {
                Core.WriteMemory(Dsp56300State.SpaceX, offset / 4, value);
                return;
            }

            if (offset is >= 0x6000 and <= 0x63FF)
            {
                Core.WriteMemory(Dsp56300State.SpaceY, (offset - 0x6000) / 4, value);
                return;
            }

            if (offset is >= 0xA000 and <= 0xEFFF)
            {
                Core.WriteMemory(Dsp56300State.SpaceP, (offset - 0xA000) / 4, value);
                return;
            }
        }

        if (offset == ResetRegister)
        {
            ProcResetWrite(_regs[ResetRegister >> 2], value);
        }

        _regs[offset >> 2] = value;
    }

    /// <summary>
    /// Writes one frame of VP mix bins into the GP mix buffer as 24-bit samples
    /// (xemu <c>mcpx_apu_dsp_frame</c>). An empty span writes silence.
    /// </summary>
    /// <param name="mixBins">32 bins x 32 samples, or empty for silence.</param>
    public void WriteMixBuffer(ReadOnlySpan<float> mixBins)
    {
        int expected = MixBinCount * SamplesPerFrame;
        if (!mixBins.IsEmpty && mixBins.Length < expected)
        {
            throw new ArgumentException("The mix-bin frame is shorter than 32x32 samples.", nameof(mixBins));
        }

        for (int bin = 0; bin < MixBinCount; bin++)
        {
            uint baseAddress = MixBufferBase + (uint)(bin * SamplesPerFrame);
            for (int sample = 0; sample < SamplesPerFrame; sample++)
            {
                float value = mixBins.IsEmpty ? 0f : mixBins[(bin * SamplesPerFrame) + sample];
                Core.WriteMemory(Dsp56300State.SpaceX, baseAddress + (uint)sample, FloatTo24(value));
            }
        }
    }

    /// <summary>
    /// Runs one DSP frame (xemu's <c>dsp_start_frame</c> + <c>dsp_run</c> loop):
    /// set the frame-start interrupt, clear the halt request and the cycle
    /// counter, then run until the microcode halts.
    /// </summary>
    public void RunFrame()
    {
        _interrupts |= InterruptStartFrame;
        Core.State.IsIdle = false;
        Core.State.CycleCount = 0;

        int budget = 0;
        do
        {
            Core.Run(1000);
            budget += 1000;
            if (!Realtime)
            {
                break;
            }

            if (budget >= FrameCycleLimit)
            {
                FrameCycleLimitHits++;
                Log?.Invoke($"dsp frame cycle limit {FrameCycleLimit}");
                break;
            }
        }
        while (!Core.State.IsIdle);

        Frames++;
    }

    /// <summary>Converts a float sample to the DSP's 24-bit fixed-point word (xemu <c>float_to_24b</c>).</summary>
    /// <param name="value">The sample value.</param>
    /// <returns>The 24-bit word in the low bits.</returns>
    public static uint FloatTo24(float value)
    {
        double scaled = value * (8.0 * 0x100000);
        int int24;
        if (scaled >= 0x7FFFFF)
        {
            int24 = 0x7FFFFF;
        }
        else if (scaled <= -8.0 * 0x100000)
        {
            int24 = -1 - 0x7FFFFF;
        }
        else if (double.IsNaN(scaled))
        {
            // xemu leaves this to lrint(); the x86 conversion yields zero.
            int24 = 0;
        }
        else
        {
            int24 = (int)Math.Round(scaled, MidpointRounding.ToEven);
        }

        return (uint)int24 & 0xFFFFFF;
    }

    /// <summary>Saves the DSP core, window and interrupt state.</summary>
    /// <param name="writer">The snapshot writer.</param>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(SnapshotVersion);
        writer.Write(_interrupts);
        for (int i = 0; i < _regs.Length; i++)
        {
            writer.Write(_regs[i]);
        }

        writer.Write(Dma.Configuration);
        writer.Write(Dma.Control);
        writer.Write(Dma.StartBlock);
        writer.Write(Dma.NextBlock);
        writer.Write(Dma.Error);
        writer.Write(Dma.Eol);
        writer.Write(Dma.DmaReadCount);
        writer.Write(Frames);
        Core.SaveState(writer);
    }

    /// <summary>Restores the DSP core, window and interrupt state.</summary>
    /// <param name="reader">The snapshot reader.</param>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != SnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported DSP snapshot version {version}.");
        }

        _interrupts = reader.ReadUInt32();
        for (int i = 0; i < _regs.Length; i++)
        {
            _regs[i] = reader.ReadUInt32();
        }

        Dma.Configuration = reader.ReadUInt32();
        Dma.Control = reader.ReadUInt32();
        Dma.StartBlock = reader.ReadUInt32();
        Dma.NextBlock = reader.ReadUInt32();
        Dma.Error = reader.ReadBoolean();
        Dma.Eol = reader.ReadBoolean();
        Dma.DmaReadCount = reader.ReadUInt32();
        Frames = reader.ReadInt64();
        Core.LoadState(reader);
        Core.State.ExceptionDebugging = false;
    }

    /// <summary>
    /// xemu <c>proc_rst_write</c>: clearing either run bit resets the core; the
    /// rising edge of both bootstraps the program from the scratch window.
    /// </summary>
    /// <param name="oldValue">The previous reset-register value.</param>
    /// <param name="value">The value being written.</param>
    private void ProcResetWrite(uint oldValue, uint value)
    {
        bool running = (value & ResetRunBits) == ResetRunBits;
        bool wasRunning = (oldValue & ResetRunBits) == ResetRunBits;
        if (!running)
        {
            ResetCore();
        }
        else if (!wasRunning)
        {
            Bootstrap();
        }
    }

    private uint ReadPeripheral(uint address)
    {
        uint value = address switch
        {
            InstructionCounterAddress => 0,
            InterruptStatusAddress => _interrupts | (Dma.Eol ? InterruptDmaEol : 0u),
            DmaNextBlockAddress => Dma.Read(DspDmaRegister.NextBlock),
            DmaStartBlockAddress => Dma.Read(DspDmaRegister.StartBlock),
            DmaControlAddress => Dma.Read(DspDmaRegister.Control),
            DmaConfigurationAddress => Dma.Read(DspDmaRegister.Configuration),
            _ => UnmappedPeripheralValue,
        };
        return value;
    }

    private void WritePeripheral(uint address, uint value)
    {
        switch (address)
        {
            case HaltAddress:
                if ((value & 1) != 0)
                {
                    Core.State.IsIdle = true;
                }

                break;
            case InterruptStatusAddress:
                _interrupts &= ~value;
                if ((value & InterruptDmaEol) != 0)
                {
                    Dma.Eol = false;
                }

                break;
            case DmaNextBlockAddress:
                Dma.Write(DspDmaRegister.NextBlock, value);
                break;
            case DmaStartBlockAddress:
                Dma.Write(DspDmaRegister.StartBlock, value);
                break;
            case DmaControlAddress:
                Dma.Write(DspDmaRegister.Control, value);
                break;
            case DmaConfigurationAddress:
                Dma.Write(DspDmaRegister.Configuration, value);
                break;
        }
    }

    private void ScratchReadWrite(byte[] buffer, int offset, uint address, int length, bool write)
    {
        uint sgeBase = _registers.Read(IsGp ? GpScratchAddress : EpScratchAddress);
        uint maxSge = _registers.Read(IsGp ? GpScratchMaxSge : EpScratchMaxSge);
        ScatterGather(sgeBase, maxSge, buffer, offset, address, length, write);
    }

    /// <summary>
    /// xemu <c>scatter_gather_rw</c>: copies through the SGE page table, one
    /// entry per 4 KB page (entry 0 maps the first page of the range).
    /// </summary>
    private void ScatterGather(
        uint sgeBase,
        uint maxSge,
        byte[] buffer,
        int bufferOffset,
        uint address,
        int length,
        bool write)
    {
        uint pageEntry = address / PageSize;
        uint offsetInPage = address % PageSize;
        int bytesToCopy = (int)(PageSize - offsetInPage);
        int remaining = length;

        while (remaining > 0)
        {
            if (pageEntry > maxSge)
            {
                ScratchOutOfRange++;
                return;
            }

            uint prdAddress = _memory.Read32(sgeBase + (pageEntry * 8));
            uint physical = prdAddress + offsetInPage;
            int chunk = Math.Min(bytesToCopy, remaining);
            CopyGuest(buffer, bufferOffset, physical, chunk, write);

            bufferOffset += chunk;
            remaining -= chunk;
            pageEntry++;
            bytesToCopy = (int)PageSize;
            offsetInPage = 0;
        }
    }

    private void CopyGuest(byte[] buffer, int bufferOffset, uint physical, int length, bool write)
    {
        int index = 0;
        if (((physical | (uint)bufferOffset) & 3u) == 0)
        {
            for (; index + 4 <= length; index += 4)
            {
                if (write)
                {
                    _memory.Write32(physical + (uint)index, Read32(buffer, bufferOffset + index));
                }
                else
                {
                    Write32(buffer, bufferOffset + index, _memory.Read32(physical + (uint)index));
                }
            }
        }

        for (; index < length; index++)
        {
            if (write)
            {
                _memory.Write8(physical + (uint)index, buffer[bufferOffset + index]);
            }
            else
            {
                buffer[bufferOffset + index] = _memory.Read8(physical + (uint)index);
            }
        }
    }

    private void FifoReadWrite(byte[] buffer, int bufferOffset, int index, int length, bool write)
    {
        uint baseRegister;
        uint endRegister;
        uint curRegister;
        uint sgeRegister;
        uint maxSgeRegister;
        uint fifoCount;

        if (IsGp)
        {
            if (write)
            {
                baseRegister = GpOutputFifoBase;
                endRegister = GpOutputFifoBase + 4;
                curRegister = GpOutputFifoBase + 8;
                fifoCount = OutputFifoCount;
            }
            else
            {
                baseRegister = GpInputFifoBase;
                endRegister = GpInputFifoBase + 4;
                curRegister = GpInputFifoBase + 8;
                fifoCount = InputFifoCount;
            }

            sgeRegister = GpFifoAddress;
            maxSgeRegister = GpFifoMaxSge;
        }
        else
        {
            if (write)
            {
                baseRegister = EpOutputFifoBase;
                endRegister = EpOutputFifoBase + 4;
                curRegister = EpOutputFifoBase + 8;
                fifoCount = OutputFifoCount;
            }
            else
            {
                baseRegister = EpInputFifoBase;
                endRegister = EpInputFifoBase + 4;
                curRegister = EpInputFifoBase + 8;
                fifoCount = InputFifoCount;
            }

            sgeRegister = EpFifoAddress;
            maxSgeRegister = EpFifoMaxSge;
        }

        if ((uint)index >= fifoCount)
        {
            FifoOutOfRange++;
            return;
        }

        uint registerOffset = (uint)index * FifoStride;
        uint baseAddress = _registers.Read(baseRegister + registerOffset) & FifoValueMask;
        uint endAddress = _registers.Read(endRegister + registerOffset) & FifoValueMask;
        uint currentRegister = curRegister + registerOffset;
        uint current = _registers.Read(currentRegister) & FifoValueMask;

        if (write && index == 0 && !IsGp && SampleSink is { } sink)
        {
            if (sink(buffer, bufferOffset, length))
            {
                // Since we are sinking, push silence out (xemu ep_silence).
                SunkBytes += length;
                int silent = Math.Min(length, _silence.Length);
                Array.Clear(buffer, bufferOffset, silent);
                length = silent;
            }
        }

        // The DSP hangs if current >= end; the EP path folds it back into the ring.
        if (current >= endAddress)
        {
            if (IsGp || endAddress <= baseAddress)
            {
                FifoOutOfRange++;
                return;
            }

            current %= endAddress - baseAddress;
        }

        if (current < baseAddress)
        {
            current = baseAddress;
        }

        uint sgeBase = _registers.Read(sgeRegister);
        uint maxSge = _registers.Read(maxSgeRegister);
        current = CircularScatterGather(
            sgeBase,
            maxSge,
            buffer,
            bufferOffset,
            baseAddress,
            endAddress,
            current,
            length,
            write);

        uint stored = _registers.Read(currentRegister);
        _registers.Write(currentRegister, (stored & ~FifoValueMask) | (current & FifoValueMask));
    }

    /// <summary>
    /// xemu <c>circular_scatter_gather_rw</c>: copies a ring range, wrapping at
    /// the end address.
    /// </summary>
    private uint CircularScatterGather(
        uint sgeBase,
        uint maxSge,
        byte[] buffer,
        int bufferOffset,
        uint baseAddress,
        uint endAddress,
        uint current,
        int length,
        bool write)
    {
        int remaining = length;
        while (remaining > 0)
        {
            int bytesToCopy = (int)(endAddress - current);
            if (bytesToCopy > remaining)
            {
                bytesToCopy = remaining;
            }

            if (bytesToCopy <= 0)
            {
                FifoOutOfRange++;
                return current;
            }

            ScatterGather(sgeBase, maxSge, buffer, bufferOffset, current, bytesToCopy, write);

            bufferOffset += bytesToCopy;
            remaining -= bytesToCopy;
            current += (uint)bytesToCopy;
            if (current >= endAddress)
            {
                current = baseAddress;
            }
        }

        return current;
    }

    private static uint Read32(byte[] buffer, int offset)
    {
        return buffer[offset]
            | ((uint)buffer[offset + 1] << 8)
            | ((uint)buffer[offset + 2] << 16)
            | ((uint)buffer[offset + 3] << 24);
    }

    private static void Write32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}

/// <summary>
/// The GP and EP DSPs of one MCPX APU: sub-window dispatch and the shared frame
/// loop (xemu <c>mcpx_apu_dsp_init</c> and <c>mcpx_apu_dsp_frame</c>).
/// </summary>
public sealed class McpxApuDspPair
{
    /// <summary>Initializes a new instance of the <see cref="McpxApuDspPair"/> class.</summary>
    /// <param name="memory">Guest physical memory (the scatter/gather target).</param>
    /// <param name="registers">The APU's global register window.</param>
    /// <param name="sampleSink">Optional host sink for the EP output FIFO.</param>
    public McpxApuDspPair(
        IApuMemory memory,
        IApuDspRegisters registers,
        ApuDspSampleSink? sampleSink = null)
    {
        Gp = new McpxApuDsp(memory, registers, isGp: true);
        Ep = new McpxApuDsp(memory, registers, isGp: false, sampleSink);
    }

    /// <summary>Gets the global processor.</summary>
    public McpxApuDsp Gp { get; }

    /// <summary>Gets the encode processor.</summary>
    public McpxApuDsp Ep { get; }

    /// <summary>Returns both DSPs to their power-on state.</summary>
    public void Reset()
    {
        Gp.PowerOnReset();
        Ep.PowerOnReset();
    }

    /// <summary>Reports whether a sub-window offset belongs to one of the DSPs.</summary>
    /// <param name="offset">APU window byte offset.</param>
    /// <returns>True when the offset is inside the GP or EP sub-window.</returns>
    public static bool Contains(uint offset)
    {
        return (offset >= McpxApuDsp.GpWindowBase && offset < McpxApuDsp.GpWindowBase + McpxApuDsp.WindowSize)
            || (offset >= McpxApuDsp.EpWindowBase && offset < McpxApuDsp.EpWindowBase + McpxApuDsp.WindowSize);
    }

    /// <summary>Reads a word from the GP or EP sub-window.</summary>
    /// <param name="offset">APU window byte offset.</param>
    /// <returns>The word.</returns>
    public uint ReadWindow(uint offset)
    {
        return offset < McpxApuDsp.EpWindowBase
            ? Gp.ReadWindow(offset - McpxApuDsp.GpWindowBase)
            : Ep.ReadWindow(offset - McpxApuDsp.EpWindowBase);
    }

    /// <summary>Writes a word to the GP or EP sub-window.</summary>
    /// <param name="offset">APU window byte offset.</param>
    /// <param name="value">The word to write.</param>
    public void WriteWindow(uint offset, uint value)
    {
        if (offset < McpxApuDsp.EpWindowBase)
        {
            Gp.WriteWindow(offset - McpxApuDsp.GpWindowBase, value);
        }
        else
        {
            Ep.WriteWindow(offset - McpxApuDsp.EpWindowBase, value);
        }
    }

    /// <summary>
    /// Runs one APU frame of DSP work (xemu <c>mcpx_apu_dsp_frame</c>): the VP
    /// mix bins go into the GP mix buffer, the GP runs every frame and the EP
    /// runs once per eight frames.
    /// </summary>
    /// <param name="epFrameDiv">The APU frame divider value before this frame's increment.</param>
    /// <param name="mixBins">32 bins x 32 samples, or empty for silence.</param>
    public void Frame(uint epFrameDiv, ReadOnlySpan<float> mixBins)
    {
        GpFrame(mixBins);

        if (Ep.IsRunning && (epFrameDiv % McpxApuDsp.EpFrameDivisor) == 0)
        {
            EpFrame();
        }
    }

    /// <summary>
    /// Runs one GP DSP frame on a 32-sample mix-bin frame (xemu
    /// <c>mcpx_apu_dsp_frame</c>'s mix-buffer write and GP run).
    /// </summary>
    /// <param name="mixBins">32 bins x 32 samples, or empty for silence.</param>
    public void GpFrame(ReadOnlySpan<float> mixBins)
    {
        Gp.WriteMixBuffer(mixBins);

        if (Gp.IsRunning)
        {
            Gp.RunFrame();
        }
    }

    /// <summary>Runs one EP DSP frame if the engine is running.</summary>
    public void EpFrame()
    {
        if (Ep.IsRunning)
        {
            Ep.RunFrame();
        }
    }

    /// <summary>Saves both DSPs' state.</summary>
    /// <param name="writer">The snapshot writer.</param>
    public void SaveState(BinaryWriter writer)
    {
        Gp.SaveState(writer);
        Ep.SaveState(writer);
    }

    /// <summary>Restores both DSPs' state.</summary>
    /// <param name="reader">The snapshot reader.</param>
    public void LoadState(BinaryReader reader)
    {
        Gp.LoadState(reader);
        Ep.LoadState(reader);
    }
}
