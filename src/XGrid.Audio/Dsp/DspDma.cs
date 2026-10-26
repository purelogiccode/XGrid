// SPDX-License-Identifier: LGPL-2.0-or-later
// Ported from xemu 0.8.139 hw/xbox/mcpx/apu/dsp/dsp_dma.c and dsp_dma.h.
// Original copyright (c) 2015 espes; (c) 2020-2021 Matt Borgerson.

using System;

namespace XGrid.Audio.Dsp;

/// <summary>DSP DMA register selector.</summary>
public enum DspDmaRegister
{
    /// <summary>DMA configuration register.</summary>
    Configuration,

    /// <summary>DMA control register.</summary>
    Control,

    /// <summary>Start-block pointer.</summary>
    StartBlock,

    /// <summary>Next-block pointer.</summary>
    NextBlock,
}

/// <summary>
/// DSP56300 DMA engine, ported from the xemu 0.8.139 C implementation.
/// The engine walks a linked list of DMA blocks in DSP memory and moves data
/// between DSP memory and the APU's scratch/FIFO windows.
/// </summary>
public sealed class DspDma
{
    /// <summary>DMA control: running.</summary>
    public const uint ControlRunning = 1u << 4;

    /// <summary>DMA control: stopped.</summary>
    public const uint ControlStopped = 1u << 5;

    /// <summary>DMA control: frozen.</summary>
    public const uint ControlFrozen = 1u << 3;

    /// <summary>DMA control action mask.</summary>
    public const uint ControlActionMask = 0x7;

    /// <summary>DMA control action: no-op.</summary>
    public const uint ControlActionNop = 0;

    /// <summary>DMA control action: start.</summary>
    public const uint ControlActionStart = 1;

    /// <summary>DMA control action: stop.</summary>
    public const uint ControlActionStop = 2;

    /// <summary>DMA control action: freeze.</summary>
    public const uint ControlActionFreeze = 3;

    /// <summary>DMA control action: unfreeze.</summary>
    public const uint ControlActionUnfreeze = 4;

    /// <summary>DMA control action: abort.</summary>
    public const uint ControlActionAbort = 5;

    /// <summary>Node pointer value mask.</summary>
    public const uint NodePointerValue = 0x3FFF;

    /// <summary>Node pointer end-of-list flag.</summary>
    public const uint NodePointerEol = 1u << 14;

    /// <summary>Node control direction flag.</summary>
    public const uint NodeControlDirection = 1u << 1;

    private byte[] _scratchBuffer = Array.Empty<byte>();

    /// <summary>Initializes a new instance of the <see cref="DspDma"/> class.</summary>
    /// <param name="memRead">DSP memory read callback.</param>
    /// <param name="memWrite">DSP memory write callback.</param>
    /// <param name="scratchReadWrite">Scratch read/write callback.</param>
    /// <param name="fifoReadWrite">FIFO read/write callback.</param>
    public DspDma(
        Func<int, uint, uint> memRead,
        Action<int, uint, uint> memWrite,
        Action<byte[], int, uint, int, bool> scratchReadWrite,
        Action<byte[], int, int, int, bool> fifoReadWrite)
    {
        MemRead = memRead ?? throw new ArgumentNullException(nameof(memRead));
        MemWrite = memWrite ?? throw new ArgumentNullException(nameof(memWrite));
        ScratchReadWrite = scratchReadWrite ?? throw new ArgumentNullException(nameof(scratchReadWrite));
        FifoReadWrite = fifoReadWrite ?? throw new ArgumentNullException(nameof(fifoReadWrite));
    }

    /// <summary>Gets the DSP memory read callback.</summary>
    public Func<int, uint, uint> MemRead { get; }

    /// <summary>Gets the DSP memory write callback.</summary>
    public Action<int, uint, uint> MemWrite { get; }

    /// <summary>Gets the scratch read/write callback.</summary>
    public Action<byte[], int, uint, int, bool> ScratchReadWrite { get; }

    /// <summary>Gets the FIFO read/write callback.</summary>
    public Action<byte[], int, int, int, bool> FifoReadWrite { get; }

    /// <summary>Gets or sets the DMA configuration register.</summary>
    public uint Configuration { get; set; }

    /// <summary>Gets or sets the DMA control register.</summary>
    public uint Control { get; set; }

    /// <summary>Gets or sets the start-block pointer.</summary>
    public uint StartBlock { get; set; }

    /// <summary>Gets or sets the next-block pointer.</summary>
    public uint NextBlock { get; set; }

    /// <summary>Gets or sets a value indicating whether the DMA engine saw an error.</summary>
    public bool Error { get; set; }

    /// <summary>Gets or sets a value indicating whether the engine reached end-of-list.</summary>
    public bool Eol { get; set; }

    /// <summary>Gets or sets the completion read counter.</summary>
    public uint DmaReadCount { get; set; }

    /// <summary>Reads a DMA register.</summary>
    /// <param name="register">The register to read.</param>
    /// <returns>The register value.</returns>
    public uint Read(DspDmaRegister register)
    {
        switch (register)
        {
            case DspDmaRegister.Configuration:
                return Configuration;
            case DspDmaRegister.Control:
                if ((Control & ControlRunning) != 0)
                {
                    DmaReadCount++;
                    if (DmaReadCount > 2)
                    {
                        Control &= ~ControlRunning;
                        Control |= ControlStopped;
                        DmaReadCount = 0;
                    }
                }

                return Control;
            case DspDmaRegister.StartBlock:
                return StartBlock;
            case DspDmaRegister.NextBlock:
                return NextBlock;
            default:
                throw new ArgumentOutOfRangeException(nameof(register));
        }
    }

    /// <summary>Writes a DMA register and runs the engine when control is written.</summary>
    /// <param name="register">The register to write.</param>
    /// <param name="value">The value to write.</param>
    public void Write(DspDmaRegister register, uint value)
    {
        switch (register)
        {
            case DspDmaRegister.Configuration:
                Configuration = value;
                break;
            case DspDmaRegister.Control:
                switch (value & ControlActionMask)
                {
                    case ControlActionStart:
                        Control |= ControlRunning;
                        Control &= ~ControlStopped;
                        DmaReadCount = 0;
                        break;
                    case ControlActionStop:
                        Control |= ControlStopped;
                        Control &= ~ControlRunning;
                        break;
                    case ControlActionFreeze:
                        Control |= ControlFrozen;
                        break;
                    case ControlActionUnfreeze:
                        Control &= ~ControlFrozen;
                        break;
                    case ControlActionNop:
                    case ControlActionAbort:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value), "Invalid DSP DMA control action.");
                }

                Run();
                break;
            case DspDmaRegister.StartBlock:
                StartBlock = value;
                break;
            case DspDmaRegister.NextBlock:
                NextBlock = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(register));
        }
    }

    /// <summary>Runs the DMA list until end-of-list, freeze or stop.</summary>
    public void Run()
    {
        if ((Control & ControlRunning) == 0 || (Control & ControlFrozen) != 0)
        {
            return;
        }

        while ((NextBlock & NodePointerEol) == 0)
        {
            uint address = NextBlock & NodePointerValue;
            int blockSpace;
            uint blockAddress;

            if (address < 0x1800)
            {
                blockSpace = Dsp56300State.SpaceX;
                blockAddress = address;
            }
            else if (address < 0x2000)
            {
                blockSpace = Dsp56300State.SpaceY;
                blockAddress = address - 0x1800;
            }
            else if (address >= 0x2800 && address < 0x3800)
            {
                blockSpace = Dsp56300State.SpaceP;
                blockAddress = address - 0x2800;
            }
            else
            {
                throw new InvalidOperationException("DSP DMA block address is out of range.");
            }

            uint nextBlock = MemRead(blockSpace, blockAddress);
            uint control = MemRead(blockSpace, blockAddress + 1);
            uint count = MemRead(blockSpace, blockAddress + 2);
            uint dspOffset = MemRead(blockSpace, blockAddress + 3);
            uint scratchOffset = MemRead(blockSpace, blockAddress + 4);
            uint scratchBase = MemRead(blockSpace, blockAddress + 5);
            uint scratchSize = MemRead(blockSpace, blockAddress + 6) + 1;

            NextBlock = nextBlock;
            if ((NextBlock & NodePointerEol) != 0)
            {
                Eol = true;
            }

            bool dspInterleave = (control & 1) != 0;
            bool direction = (control & NodeControlDirection) != 0;
            uint unknown2 = (control >> 2) & 0x3;
            bool bufferOffsetWriteback = ((control >> 4) & 1) != 0;
            uint bufferId = (control >> 5) & 0xF;
            uint format = (control >> 10) & 0x7;
            bool unknown13 = ((control >> 13) & 1) != 0;

            if (unknown2 != 0 || unknown13)
            {
                throw new InvalidOperationException("Unhandled DSP DMA control setting.");
            }

            int channelCount = (int)((count & 0xF) + 1);
            int blockCount = (int)(count >> 4);
            int itemCount = (int)count;

            int itemSize;
            uint itemMask;
            switch (format)
            {
                case 0:
                    itemSize = 1;
                    itemMask = 0x000000FF;
                    break;
                case 1:
                    itemSize = 2;
                    itemMask = 0x0000FFFF;
                    break;
                case 2:
                case 6:
                    itemSize = 4;
                    itemMask = 0x00FFFFFF;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown DSP DMA format: 0x{format:X}.");
            }

            uint scratchAddress = scratchBase + scratchOffset;
            int memorySpace;
            uint memoryAddress;

            if (dspOffset < 0x1800)
            {
                memorySpace = Dsp56300State.SpaceX;
                memoryAddress = dspOffset;
            }
            else if (dspOffset < 0x2000)
            {
                memorySpace = Dsp56300State.SpaceY;
                memoryAddress = dspOffset - 0x1800;
            }
            else if (dspOffset >= 0x2800 && dspOffset < 0x3800)
            {
                memorySpace = Dsp56300State.SpaceP;
                memoryAddress = dspOffset - 0x2800;
            }
            else
            {
                throw new InvalidOperationException($"DSP DMA offset is out of range: 0x{dspOffset:X}.");
            }

            int transferSize = checked(itemCount * itemSize);
            if (dspInterleave)
            {
                transferSize = checked(blockCount * itemSize * channelCount);
            }

            EnsureScratchBuffer(transferSize);

            if (direction)
            {
                if (dspInterleave)
                {
                    for (int i = 0; i < blockCount; i++)
                    {
                        for (int channel = 0; channel < channelCount; channel++)
                        {
                            uint value = MemRead(memorySpace, memoryAddress + (uint)(channel * blockCount + i));
                            int offset = (i * itemSize * channelCount) + (channel * itemSize);
                            switch (itemSize)
                            {
                                case 2:
                                    Write16(_scratchBuffer, offset, value >> 8);
                                    break;
                                case 4:
                                    Write32(_scratchBuffer, offset, value);
                                    break;
                                default:
                                    throw new InvalidOperationException("Invalid DSP DMA item size for interleaved samples.");
                            }
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < itemCount; i++)
                    {
                        uint value = MemRead(memorySpace, memoryAddress + (uint)i);
                        switch (itemSize)
                        {
                            case 2:
                                Write16(_scratchBuffer, i * 2, value >> 8);
                                break;
                            case 4:
                                Write32(_scratchBuffer, i * 4, value);
                                break;
                            default:
                                throw new InvalidOperationException("Invalid DSP DMA item size.");
                        }
                    }
                }

                switch (bufferId)
                {
                    case 0x0:
                    case 0x1:
                    case 0x2:
                    case 0x3:
                        FifoReadWrite(_scratchBuffer, 0, (int)bufferId, transferSize, true);
                        break;
                    case 0xE:
                        ScratchCircularCopy(scratchBase, ref scratchOffset, scratchSize, transferSize, true);
                        break;
                    case 0xF:
                        ScratchReadWrite(_scratchBuffer, 0, scratchAddress, transferSize, true);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown DSP DMA buffer: 0x{bufferId:X}.");
                }
            }
            else
            {
                if (dspInterleave)
                {
                    throw new InvalidOperationException("Interleaved DSP DMA reads are not implemented.");
                }

                if (bufferId == 0xE)
                {
                    ScratchCircularCopy(scratchBase, ref scratchOffset, scratchSize, transferSize, false);
                }
                else if (bufferId == 0xF)
                {
                    ScratchReadWrite(_scratchBuffer, 0, scratchAddress, transferSize, false);
                }
                else
                {
                    throw new InvalidOperationException($"Unhandled DSP DMA buffer: 0x{bufferId:X}.");
                }

                for (int i = 0; i < itemCount; i++)
                {
                    uint value;
                    switch (itemSize)
                    {
                        case 2:
                            value = (uint)Read16(_scratchBuffer, i * 2) << 8;
                            break;
                        case 4:
                            value = Read32(_scratchBuffer, i * 4) & itemMask;
                            break;
                        default:
                            throw new InvalidOperationException("Invalid DSP DMA item size.");
                    }

                    MemWrite(memorySpace, memoryAddress + (uint)i, value);
                }
            }

            if (bufferOffsetWriteback)
            {
                MemWrite(blockSpace, blockAddress + 4, scratchOffset);
            }
        }
    }

    private void EnsureScratchBuffer(int size)
    {
        if (_scratchBuffer.Length < size)
        {
            _scratchBuffer = new byte[size];
        }
    }

    private void ScratchCircularCopy(
        uint scratchBase,
        ref uint scratchOffset,
        uint scratchSize,
        int transferSize,
        bool direction)
    {
        if (scratchOffset >= scratchSize)
        {
            scratchOffset = 0;
        }

        int bufferOffset = 0;
        while (transferSize > 0)
        {
            int bytesUntilWrap = (int)(scratchSize - scratchOffset);
            int chunkSize = Math.Min(transferSize, bytesUntilWrap);
            uint scratchAddress = scratchBase + scratchOffset;
            ScratchReadWrite(_scratchBuffer, bufferOffset, scratchAddress, chunkSize, direction);

            scratchOffset += (uint)chunkSize;
            if (scratchOffset >= scratchSize)
            {
                scratchOffset = 0;
            }

            transferSize -= chunkSize;
            bufferOffset += chunkSize;
        }
    }

    private static void Write16(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void Write32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static ushort Read16(byte[] buffer, int offset)
    {
        return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    private static uint Read32(byte[] buffer, int offset)
    {
        return buffer[offset]
            | ((uint)buffer[offset + 1] << 8)
            | ((uint)buffer[offset + 2] << 16)
            | ((uint)buffer[offset + 3] << 24);
    }
}
