using System;
using System.Collections.Generic;
using XGrid.Core.Devices;
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
/// (hw/xbox/mcpx/apu/apu.c and apu/vp/vp.c): global registers below 0x20000 (ISTS/IEN,
/// FE control, memory port) and the voice-processor front-end PIO region at 0x20000+.
/// The DSP and voice processor are not emulated yet: FE commands are stored, and
/// VOICE_OFF completes immediately (voice inactive + notifier status + FEVINTSTS/FENINTSTS),
/// matching xemu's synchronous voice-off path.
/// </summary>
public sealed class McpxApuDevice : IMmioDevice, IXboxDevice
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
    private const uint VpPioFree = 0x20010;
    private const uint VpVoiceOn = 0x20124;
    private const uint VpVoiceOff = 0x20128;

    private const uint IsTsGintsts = 1u << 0;
    private const uint IsTsFeTintsts = 1u << 4;
    private const uint IsTsFenintsts = 1u << 5;
    private const uint IsTsFevintsts = 1u << 6;

    private const uint FectlFemethmode = 0xE0;
    private const uint FectlFemethmodeTrapped = 0xE0;
    private const uint FectlFetrapreason = 0xF00;
    private const uint FectlFetrapreasonRequested = 0xF00;
    private const uint FetForce1Se2FeIdleVoice = 1u << 15;
    private const uint Se2FeIdleVoice = 0x00008000;

    private const uint VoiceActiveVoice = 1u << 21;
    private const uint VoiceStateOffset = 0x54;
    private const uint VoiceSize = 0x80;
    private const uint PioFreeCount = 0x80;
    private const byte NotificationDoneSuccess = 0x01;

    /// <summary>Voice-processor frame period (xemu EP_FRAME_US): 256 samples at 48 kHz.</summary>
    private const ulong FrameCycles = (733333333ul * 256ul) / 48000ul;

    private readonly uint[] _regs = new uint[WindowSize / 4];
    private readonly IApuMemory _memory;
    private readonly List<ushort> _pendingIdleVoices = new();
    private ulong _frameRemainder;

    /// <summary>Creates the APU in its power-on state.</summary>
    public McpxApuDevice(IApuMemory memory)
    {
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        Reset();
    }

    /// <summary>Raised when the APU PCI interrupt line changes state.</summary>
    public event Action<bool>? InterruptChanged;

    /// <inheritdoc />
    public string Name => "MCPX APU";

    /// <summary>Gets the number of register reads, for diagnostics.</summary>
    public long ReadCount { get; private set; }

    /// <summary>Gets the number of register writes, for diagnostics.</summary>
    public long WriteCount { get; private set; }

    /// <summary>Gets the number of voice-off completions raised, for diagnostics.</summary>
    public long VoiceOffCompletions { get; private set; }

    /// <summary>Gets the interrupt status register, for diagnostics.</summary>
    public uint InterruptStatus => _regs[IsTs >> 2];

    /// <summary>Gets the interrupt enable register, for diagnostics.</summary>
    public uint InterruptEnable => _regs[Ien >> 2];

    /// <summary>Gets the front-end control register, for diagnostics.</summary>
    public uint FrontEndControl => _regs[Fectl >> 2];

    /// <summary>Gets whether the APU interrupt line is asserted, for diagnostics.</summary>
    public bool InterruptAsserted { get; private set; }

    /// <summary>Gets a bounded log of interrupt-relevant events, for diagnostics.</summary>
    public IReadOnlyList<string> EventLog => _eventLog;

    private readonly List<string> _eventLog = new();

    /// <inheritdoc />
    public void Reset()
    {
        Array.Clear(_regs);
        _pendingIdleVoices.Clear();
        _frameRemainder = 0;
        SetAsserted(false);
    }

    /// <summary>
    /// Advances voice-processor frame timing. Voices turned off are reported to the front end
    /// through the SE2FE_IDLE_VOICE message at frame boundaries (xemu vp.c frame processing).
    /// </summary>
    public void Advance(ulong cycles)
    {
        _frameRemainder += cycles;
        while (_frameRemainder >= FrameCycles)
        {
            _frameRemainder -= FrameCycles;
            if (_pendingIdleVoices.Count == 0)
            {
                continue;
            }

            ushort voice = _pendingIdleVoices[0];
            _pendingIdleVoices.RemoveAt(0);
            DeliverIdleVoice(voice);
        }
    }

    /// <inheritdoc />
    public uint Read(uint address, int size)
    {
        uint offset = address - BaseAddress;
        ReadCount++;
        uint word = offset switch
        {
            // xemu's VP does not simulate the PIO queue and always reports it empty.
            VpPioFree => PioFreeCount,
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

    /// <inheritdoc />
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
            case VpPioFree:
                break;
            case VpVoiceOn:
                SetVoiceActive(value & 0xFFFF, true);
                _regs[offset >> 2] = value;
                break;
            case VpVoiceOff:
                VoiceOff(value & 0xFFFF);
                _regs[offset >> 2] = value;
                break;
            default:
                _regs[offset >> 2] = value;
                break;
        }
    }

    private void VoiceOff(uint voice)
    {
        Log($"voice-off {voice}");
        SetVoiceActive(voice, false);
        WriteNotifierStatus(voice, 0, NotificationDoneSuccess);
        _regs[IsTs >> 2] |= IsTsFevintsts | IsTsFenintsts;

        // The voice processor reports the idle voice on its next frame, not synchronously:
        // delivering it now would race the front end's in-flight operation counter.
        _pendingIdleVoices.Add((ushort)voice);
        VoiceOffCompletions++;
        UpdateIrq();
    }

    private void DeliverIdleVoice(ushort voice)
    {
        Log($"idle-voice {voice}");
        if ((_regs[FetForce1 >> 2] & FetForce1Se2FeIdleVoice) == 0)
        {
            return;
        }

        _regs[FedecMeth >> 2] = Se2FeIdleVoice;
        _regs[FedecParam >> 2] = voice;
        _regs[Fectl >> 2] = (_regs[Fectl >> 2] & ~(FectlFemethmode | FectlFetrapreason))
            | FectlFemethmodeTrapped | FectlFetrapreasonRequested;
        UpdateIrq();
    }

    private void SetVoiceActive(uint voice, bool active)
    {
        uint stateAddress = _regs[VpVaddr >> 2] + (voice * VoiceSize) + VoiceStateOffset;
        uint state = _memory.Read32(stateAddress);
        state = active ? state | VoiceActiveVoice : state & ~VoiceActiveVoice;
        _memory.Write32(stateAddress, state);
    }

    private void WriteNotifierStatus(uint voice, int notifier, byte status)
    {
        // FENADDR + 16 * (2 + voice * 4 + notifier), status in the last byte (xemu set_notify_status).
        uint notifyOffset = _regs[FenAddr >> 2] + (16u * (2 + (voice * 4) + (uint)notifier));
        _memory.Write8(notifyOffset + 15, status);
        _memory.Write8(notifyOffset - 1, 1);
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
