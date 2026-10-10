using System;
using System.Collections.Generic;
using System.Numerics;
using XGrid.Core.Devices;
using XGrid.Memory;

namespace XGrid.Nv2a;

/// <summary>
/// NV2A GPU control register window: 16 MB at 0xFD000000 (BAR0).
/// Block map and reset defaults follow xemu <c>hw/xbox/nv2a/</c> (blocktable in
/// <c>nv2a.c</c>, per-block read/write in <c>pmc.c</c>/<c>pbus.c</c>/<c>pfb.c</c>/
/// <c>pcrtc.c</c>/<c>pramdac.c</c>/<c>ptimer.c</c>/<c>user.c</c>, pushbuffer parsing in
/// <c>pfifo.c</c>, DMA object layout in <c>nv2a.c</c>); PFB CFG0 reset default 0x03070103
/// is the 64 MB hardware value recorded in the Xermu notes. The PFIFO pusher runs
/// synchronously on submission (no thread) and dispatches PGRAPH methods to a
/// recording sink; rendering and interrupt assertion through the PIC are deferred:
/// pending/enabled state is tracked but no IRQ is raised yet.
/// </summary>
public sealed class Nv2AMmio : IMmioDevice, IXboxDevice
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

    private const uint PfifoIntr0 = 0x100u;
    private const uint PfifoIntrEn0 = 0x140u;
    private const uint PfifoRamht = 0x210u;
    private const uint PfifoRunoutStatus = 0x400u;
    private const uint PfifoCache1Push0 = 0x1200u;
    private const uint PfifoCache1DmaPush = 0x1220u;
    private const uint PfifoCache1DmaState = 0x1228u;
    private const uint PfifoCache1DmaInstance = 0x122Cu;
    private const uint PfifoCache1DmaSubroutine = 0x124Cu;
    private const uint PfifoCache1Pull0 = 0x1250u;
    private const uint PfifoCache1DmaDcount = 0x12A0u;
    private const uint PfifoCache1GetJmpShadow = 0x12A4u;
    private const uint PfifoCache1RsvdShadow = 0x12A8u;
    private const uint PfifoCache1DataShadow = 0x12ACu;

    private const uint Push0Access = 1u << 0;
    private const uint Push1ChidMask = 0x1Fu;
    private const uint Push1ModeDma = 0x100u;
    private const uint DmaPushAccess = 1u << 0;
    private const uint DmaPushSuspended = 1u << 12;
    private const uint DmaStateMethodType = 1u;
    private const uint DmaStateMethod = 0x1FFCu;
    private const uint DmaStateSubchannel = 0xE000u;
    private const uint DmaStateMethodCount = 0x1FFC0000u;
    private const uint DmaStateError = 0xE0000000u;
    private const uint DmaErrorNone = 0u;
    private const uint DmaErrorCall = 1u;
    private const uint DmaErrorReturn = 3u;
    private const uint DmaErrorReservedCommand = 4u;
    private const uint DmaErrorProtection = 6u;
    private const uint SubroutineActive = 1u;
    private const uint Pull0Access = 1u << 0;
    private const uint ObjectParamLow = 0x180u;
    private const uint ObjectParamHigh = 0x200u;
    private const uint EngineGraphics = 1u;
    private const uint RamhtBaseMask = 0x1F0u;
    private const uint RamhtSizeMask = 0x30000u;
    private const uint RamhtInstanceMask = 0xFFFFu;
    private const uint RamhtEngineMask = 0x30000u;
    private const uint RamhtValid = 0x80000000u;
    private const uint DmaInstanceAddressMask = 0xFFFFu;
    private const uint DmaAddressMask = 0xFFFFF000u;
    private const uint DmaAdjustMask = 0xFFF00000u;
    private const uint GpuAddressMask = 0x07FFFFFFu;
    private const uint PgraphFifo = 0x720u;
    private const uint PgraphFifoAccess = 1u << 0;
    private const uint PgraphContextSwitch = 1u << 12;
    private const uint PcrtcVblank = 1u << 0;
    private const ulong VsyncTicksPerFrame = CoreClockHz / 60;

    private const uint MethodSurfaceClipH = 0x200u;
    private const uint MethodSurfaceClipV = 0x204u;
    private const uint MethodSurfaceFormat = 0x208u;
    private const uint MethodSurfacePitch = 0x20Cu;
    private const uint MethodSurfaceColorOffset = 0x210u;
    private const uint MethodSurfaceZetaOffset = 0x214u;
    private const uint MethodFlipRead = 0x120u;
    private const uint MethodFlipWrite = 0x124u;
    private const uint MethodFlipModulo = 0x128u;
    private const uint MethodSetContextDmaSemaphore = 0x1A4u;
    private const uint MethodSetSemaphoreOffset = 0x1D6Cu;
    private const uint MethodBackEndWriteSemaphoreRelease = 0x1D70u;
    private const uint MethodNoOperation = 0x100u;
    private const uint PgraphNsource = 0x108u;
    private const uint PgraphTrappedAddr = 0x704u;
    private const uint PgraphTrappedDataLow = 0x708u;
    private const uint PgraphIntrError = 1u << 20;
    private const uint PgraphNsourceNotification = 1u;
    private const uint MethodZetaClearValue = 0x1D8Cu;
    private const uint MethodColorClearValue = 0x1D90u;
    private const uint MethodClearSurface = 0x1D94u;
    private const uint MethodClearRectH = 0x1D98u;
    private const uint MethodClearRectV = 0x1D9Cu;
    private const uint MethodVertexArrayOffsetBase = 0x1720u;
    private const uint MethodVertexArrayOffsetEnd = 0x175Cu;
    private const uint MethodVertexFormatBase = 0x1760u;
    private const uint MethodVertexFormatEnd = 0x179Cu;
    private const uint MethodBeginEnd = 0x17FCu;
    private const uint MethodArrayElement16 = 0x1800u;
    private const uint MethodArrayElement32 = 0x1808u;
    private const uint MethodDrawArrays = 0x1810u;
    private const uint MethodInlineArray = 0x1818u;
    private const uint MethodTransformProgramBase = 0x0B00u;
    private const uint MethodTransformProgramEnd = 0x0B7Cu;
    private const uint MethodTransformConstantBase = 0x0B80u;
    private const uint MethodTransformConstantEnd = 0x0BFCu;
    private const uint MethodTransformExecutionMode = 0x1E94u;
    private const uint MethodTransformProgramCxtWriteEn = 0x1E98u;
    private const uint MethodTransformProgramLoad = 0x1E9Cu;
    private const uint MethodTransformProgramStart = 0x1EA0u;
    private const uint MethodTransformConstantLoad = 0x1EA4u;
    private const int MaxTransformProgramLength = 136;
    private const int MaxVertexShaderConstants = 192;
    private const int MaxInlineElements = 65536;
    private const uint BeginEndTriangles = 5u;
    private const uint BeginEndTriangleStrip = 6u;
    private const uint BeginEndTriangleFan = 7u;
    private const uint BeginEndQuads = 8u;
    private const uint MaxInlineWords = 65536u;
    private const uint SurfaceTypePitch = 1u;
    private const uint MaxClearPixels = 16u * 1024u * 1024u;

    private const uint MaxPusherWords = 4000000u;

    private const uint PtimerIntr0 = 0x100u;
    private const uint PtimerIntrEn0 = 0x140u;
    private const uint PtimerNumerator = 0x200u;
    private const uint PtimerDenominator = 0x210u;
    private const uint PtimerTime0 = 0x400u;
    private const uint PtimerTime1 = 0x410u;

    private const uint PfbCfg0 = 0x200u;
    private const uint PfbCfg0Reset = 0x03070103u;
    private const uint PfbCstatus = 0x20Cu;
    private const uint PfbWbc = 0x410u;

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

    private const uint PrmcioBase = 0x601000u;
    private const uint PrmcioStatus1 = 0x3DAu;
    private const uint VgaRetraceBits = 0x09u;
    private const uint UserDmaPut = 0x40u;
    private const uint UserDmaGet = 0x44u;
    private const uint UserRef = 0x48u;

    private readonly IGpuMemory? _memory;
    private readonly uint[] _boundEngine = new uint[8];
    private readonly uint[] _boundInstance = new uint[8];
    private readonly uint[] _pfifo = new uint[PfifoSize / 4];
    private readonly uint[] _pvideo = new uint[0x1000 / 4];
    private readonly uint[] _pfb = new uint[0x1000 / 4];
    private readonly uint[] _pgraph = new uint[PgraphSize / 4];
    private readonly byte[] _ramin = new byte[PraminSize];
    private readonly uint[] _prmcio = new uint[0x1000 / 4];
    private bool _retrace;
    private readonly Dictionary<uint, int> _readCounts = new();
    private readonly Dictionary<uint, int> _writeCounts = new();
    private readonly Dictionary<uint, int> _userReadOffsets = new();

    private readonly List<uint> _pmcIntrEnLog = new();
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
    private uint _pfifoPending;
    private uint _pfifoEnabled;
    private ulong _vsyncRemainder;
    private uint _surfaceFormat;
    private uint _surfacePitch;
    private uint _surfaceZetaOffset;
    private uint _surfaceClipH;
    private uint _surfaceClipV;
    private uint _zetaClearValue;
    private uint _colorClearValue;
    private uint _dmaSemaphore;
    private uint _semaphoreOffset;
    private uint _clearRectH;
    private uint _clearRectV;
    private uint _flipRead;
    private uint _flipWrite;
    private uint _flipModulo;
    private readonly uint[] _vertexFormat = new uint[16];
    private readonly uint[] _vertexArrayOffset = new uint[16];
    private readonly uint[][] _transformProgram = new uint[MaxTransformProgramLength][];
    private readonly uint[][] _transformConstants = new uint[MaxVertexShaderConstants][];
    private uint _transformProgramLoad;
    private uint _transformProgramStart;
    private uint _transformExecutionMode;
    private uint _transformProgramCxtWriteEn;
    private uint _transformConstantLoad;
    private readonly List<uint> _inlineElements = new();
    private readonly List<uint> _inlineWords = new();
    private uint _beginMode;
    private readonly Dictionary<uint, int> _methodCounts = new();
    private readonly Dictionary<uint, uint> _methodParams = new();
    private readonly List<(uint Subchannel, uint Method, uint Parameter)> _methodStream = new();

    /// <summary>Creates the NV2A register window for the given RAM size in bytes.</summary>
    public Nv2AMmio(uint ramSize = 64u * 1024 * 1024, string name = "NV2A", IGpuMemory? memory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        RamSize = ramSize;
        Name = name;
        _memory = memory;
        for (int i = 0; i < MaxTransformProgramLength; i++)
        {
            _transformProgram[i] = new uint[4];
        }

        for (int i = 0; i < MaxVertexShaderConstants; i++)
        {
            _transformConstants[i] = new uint[4];
        }

        Reset();
    }

    /// <summary>Gets the device name used in traces and diagnostics.</summary>
    public string Name { get; }

    /// <summary>Gets the guest RAM size reported by PFB CSTATUS.</summary>
    public uint RamSize { get; }

    /// <summary>Gets read counts keyed by register-block base offset. Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> ReadCounts => _readCounts;

    /// <summary>Gets write counts keyed by register-block base offset. Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> WriteCounts => _writeCounts;

    /// <summary>Gets USER-area read counts keyed by suboffset. Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> UserReadCounts => _userReadOffsets;

    /// <summary>Gets the number of pushbuffer words consumed by the PFIFO pusher. Diagnostics.</summary>
    public ulong PusherWordsConsumed { get; private set; }

    /// <summary>Gets whether the pusher hit its per-kick word cap. Diagnostics.</summary>
    public bool PusherCapped { get; private set; }

    /// <summary>Gets the last pusher DMA error code (0 when none). Diagnostics.</summary>
    public uint PusherError { get; private set; }

    /// <summary>Gets the number of PGRAPH context switches observed. Diagnostics.</summary>
    public uint ContextSwitches { get; private set; }

    /// <summary>Gets PGRAPH method counts keyed by subchannel and method. Diagnostics.</summary>
    public IReadOnlyDictionary<uint, int> MethodCounts => _methodCounts;

    /// <summary>Gets the last parameter per PGRAPH method key. Diagnostics.</summary>
    public IReadOnlyDictionary<uint, uint> MethodParams => _methodParams;

    /// <summary>Gets the ordered PGRAPH method stream (capped). Diagnostics.</summary>
    public IReadOnlyList<(uint Subchannel, uint Method, uint Parameter)> MethodStream => _methodStream;

    /// <summary>Gets the number of executed surface clears. Diagnostics.</summary>
    public uint ClearsExecuted { get; private set; }

    /// <summary>Gets the number of executed draws. Diagnostics.</summary>
    public ulong DrawsExecuted { get; private set; }

    /// <summary>Gets the number of semaphore release writes performed. Diagnostics.</summary>
    public int SemaphoreReleases { get; private set; }

    /// <summary>Gets the last semaphore release resolution state. Diagnostics.</summary>
    public string SemaphoreDebug { get; private set; } = string.Empty;

    /// <summary>Gets the PGRAPH interrupt enable mask. Diagnostics.</summary>
    public uint PgraphEnabledDebug { get; private set; }

    /// <summary>Gets the PGRAPH pending interrupt mask. Diagnostics.</summary>
    public uint PgraphPendingDebug { get; private set; }

    /// <summary>Gets the PMC interrupt enable mask. Diagnostics.</summary>
    public uint PmcEnabledDebug { get; private set; }

    /// <summary>Gets the PMC_INTR_EN_0 write log. Diagnostics.</summary>
    public IReadOnlyList<uint> PmcIntrEnLog => _pmcIntrEnLog;

    /// <summary>Gets the number of PGRAPH notifications raised. Diagnostics.</summary>
    public int NotificationsRaised { get; private set; }

    /// <summary>Gets whether the PGRAPH interrupt line was asserted when the last notification fired. Diagnostics.</summary>
    public bool NotificationAsserted { get; private set; }

    /// <summary>Gets the number of PRAMIN writes observed. Diagnostics.</summary>
    public int PraminWrites { get; private set; }

    /// <summary>Reads a PRAMIN word for diagnostics.</summary>
    public uint ReadRaminWord(uint offset)
    {
        return offset + 4 <= PraminSize ? RaminWord(offset) : 0;
    }

    /// <summary>Gets a snapshot of PRAMIN contents for diagnostics.</summary>
    public byte[] PraminSnapshot()
    {
        return (byte[])_ramin.Clone();
    }

    /// <summary>Gets the number of pixels written by draws. Diagnostics.</summary>
    public ulong DrawnPixels { get; private set; }

    /// <summary>Gets the number of draws skipped for unsupported layouts. Diagnostics.</summary>
    public ulong SkippedDraws { get; private set; }

    /// <summary>Gets the current color surface offset in guest memory. Diagnostics.</summary>
    public uint PresentedColorOffset { get; private set; }

    /// <summary>Gets the current color surface pitch in bytes. Diagnostics.</summary>
    public uint PresentedColorPitch => _surfacePitch & 0xFFFFu;

    /// <summary>Gets the current surface width in pixels. Diagnostics.</summary>
    public uint PresentedWidth => _surfaceClipH >> 16;

    /// <summary>Gets the current surface height in pixels. Diagnostics.</summary>
    public uint PresentedHeight => _surfaceClipV >> 16;

    /// <summary>Raised when the PCI interrupt line changes state (true asserted).</summary>
    public event Action<bool>? InterruptChanged;

    /// <summary>Gets whether the PCI interrupt line is currently asserted. Diagnostics.</summary>
    public bool InterruptAsserted { get; private set; }

    /// <summary>Gets the current color format code. Diagnostics.</summary>
    public uint PresentedColorFormat => _surfaceFormat & 0xFu;

    /// <summary>Gets the current color surface bytes per pixel (0 for unknown formats). Diagnostics.</summary>
    public uint PresentedColorBpp => ColorBytesPerPixel(_surfaceFormat & 0xFu);

    /// <summary>Gets the number of collected inline array elements. Diagnostics.</summary>
    public int InlineElementCount => _inlineElements.Count;

    /// <summary>Gets the transform program load pointer. Diagnostics.</summary>
    public uint TransformProgramLoadDebug => _transformProgramLoad;

    /// <summary>Gets the transform constant load pointer. Diagnostics.</summary>
    public uint TransformConstantLoadDebug => _transformConstantLoad;

    /// <summary>Reads a transform program word (diagnostics).</summary>
    public uint ReadTransformProgramWord(uint index, int part)
    {
        return index < MaxTransformProgramLength && part is >= 0 and < 4 ? _transformProgram[index][part] : 0;
    }

    /// <summary>Reads a transform constant word (diagnostics).</summary>
    public uint ReadTransformConstantWord(uint index, int part)
    {
        return index < MaxVertexShaderConstants && part is >= 0 and < 4 ? _transformConstants[index][part] : 0;
    }

    /// <summary>Advances the PTIMER free-running counter by the given core-clock ticks.</summary>
    public void Advance(ulong coreTicks)
    {
        _ptimerClock += coreTicks;
        _vsyncRemainder += coreTicks;
        while (_vsyncRemainder >= VsyncTicksPerFrame)
        {
            _vsyncRemainder -= VsyncTicksPerFrame;
            _pcrtcPending |= PcrtcVblank;
            _pcrtcRaster = 0;
            UpdateIrq();
        }
    }

    private void UpdateIrq()
    {
        uint pending = AggregatePending();
        bool asserted = pending != 0 && PmcEnabledDebug != 0;
        if (asserted != InterruptAsserted)
        {
            InterruptAsserted = asserted;
            InterruptChanged?.Invoke(asserted);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        Array.Clear(_pfifo);
        Array.Clear(_pvideo);
        Array.Clear(_pfb);
        Array.Clear(_pgraph);
        PmcEnabledDebug = 0;
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
        PgraphPendingDebug = 0;
        PgraphEnabledDebug = 0;
        _pfifoPending = 0;
        _pfifoEnabled = 0;
        PusherWordsConsumed = 0;
        PusherCapped = false;
        PusherError = 0;
        InterruptAsserted = false;
        _vsyncRemainder = 0;
        ContextSwitches = 0;
        ClearsExecuted = 0;
        _surfaceFormat = 0;
        _surfacePitch = 0;
        PresentedColorOffset = 0;
        _surfaceZetaOffset = 0;
        _surfaceClipH = 0;
        _surfaceClipV = 0;
        _zetaClearValue = 0;
        _colorClearValue = 0;
        _clearRectH = 0;
        _clearRectV = 0;
        _flipRead = 0;
        _flipWrite = 0;
        _flipModulo = 0;
        Array.Clear(_vertexFormat);
        Array.Clear(_vertexArrayOffset);
        _inlineWords.Clear();
        _inlineElements.Clear();
        foreach (uint[] row in _transformProgram)
        {
            Array.Clear(row);
        }

        foreach (uint[] row in _transformConstants)
        {
            Array.Clear(row);
        }

        _transformProgramLoad = 0;
        _transformProgramStart = 0;
        _transformExecutionMode = 0;
        _transformProgramCxtWriteEn = 0;
        _transformConstantLoad = 0;
        _beginMode = 0;
        DrawsExecuted = 0;
        DrawnPixels = 0;
        SkippedDraws = 0;
        _methodCounts.Clear();
        _methodParams.Clear();
        _methodStream.Clear();
        Array.Clear(_boundEngine);
        Array.Clear(_boundInstance);
        Array.Clear(_prmcio);
        _retrace = false;
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
            _userReadOffsets.TryGetValue(offset - UserBase, out int userCount);
            _userReadOffsets[offset - UserBase] = userCount + 1;
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
            return ReadPfifo(offset - PfifoBase);
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

        if (offset >= PrmcioBase && offset < PrmcioBase + 0x1000u)
        {
            CountRead(PrmcioBase);
            return ReadPrmcio(offset - PrmcioBase);
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
            WritePfifo(offset - PfifoBase, value);
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

        if (offset >= PrmcioBase && offset < PrmcioBase + 0x1000u)
        {
            CountWrite(PrmcioBase);
            _prmcio[(offset - PrmcioBase) / 4] = value;
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
            PmcIntrEn0 => PmcEnabledDebug,
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
                PgraphPendingDebug &= ~((value >> 12) & 1u);
                _pcrtcPending &= ~((value >> 24) & 1u);
                break;
            case PmcIntrEn0:
                PmcEnabledDebug = value;
                if (_pmcIntrEnLog.Count < 32)
                {
                    _pmcIntrEnLog.Add(value);
                }

                break;
            default:
                break;
        }

        UpdateIrq();
    }

    private uint AggregatePending()
    {
        uint pending = _pmcPending;
        if ((_pfifoPending & _pfifoEnabled) != 0)
        {
            pending |= 1u << 8;
        }

        if ((PgraphPendingDebug & PgraphEnabledDebug) != 0)
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

    private uint ReadPfifo(uint register)
    {
        return register switch
        {
            PfifoIntr0 => _pfifoPending,
            PfifoIntrEn0 => _pfifoEnabled,
            PfifoRunoutStatus => PfifoCache1StatusLowMark,
            _ => _pfifo[register / 4],
        };
    }

    private void WritePfifo(uint register, uint value)
    {
        switch (register)
        {
            case PfifoIntr0:
                _pfifoPending &= ~value;
                break;
            case PfifoIntrEn0:
                _pfifoEnabled = value;
                break;
            default:
                _pfifo[register / 4] = value;
                break;
        }

        UpdateIrq();
        RunPusher();
    }

    private uint ReadPgraph(uint register)
    {
        return register switch
        {
            0x100u => PgraphPendingDebug,
            0x140u => PgraphEnabledDebug,
            _ => _pgraph[register / 4],
        };
    }

    private void WritePgraph(uint register, uint value)
    {
        switch (register)
        {
            case 0x100u:
                PgraphPendingDebug &= ~value;
                break;
            case 0x140u:
                PgraphEnabledDebug = value;
                break;
            case PgraphFifo:
                _pgraph[register / 4] = value;
                RunPusher();
                break;
            default:
                _pgraph[register / 4] = value;
                break;
        }

        UpdateIrq();
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

    private uint ReadPrmcio(uint register)
    {
        if (register == (PrmcioStatus1 & ~3u))
        {
            _retrace = !_retrace;
            uint stored = _prmcio[register / 4] & ~0xFF0000u;
            return stored | ((_retrace ? VgaRetraceBits : 0u) << 16);
        }

        return _prmcio[register / 4];
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

        UpdateIrq();
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
        return (uint)(_ramin[offset] | (_ramin[offset + 1] << 8) | (_ramin[offset + 2] << 16) |
                      (_ramin[offset + 3] << 24));
    }

    private void WriteRamin(uint offset, uint value)
    {
        CountWrite(PraminBase);
        PraminWrites++;
        _ramin[offset] = (byte)value;
        _ramin[offset + 1] = (byte)(value >> 8);
        _ramin[offset + 2] = (byte)(value >> 16);
        _ramin[offset + 3] = (byte)(value >> 24);
    }

    private uint ReadUser(uint offset)
    {
        if (IsActiveDmaChannel(offset) && (offset & 0xFFFFu) switch
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
        if (!IsActiveDmaChannel(offset))
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
                return;
        }

        RunPusher();
    }

    private bool IsActiveDmaChannel(uint offset)
    {
        uint channel = offset >> 16;
        if (((_pfifo[PfifoMode / 4] >> (int)channel) & 1u) == 0)
        {
            return false;
        }

        return channel == (_pfifo[PfifoCache1Push1 / 4] & Push1ChidMask);
    }

    private uint RaminWord(uint offset)
    {
        return (uint)(_ramin[offset] | (_ramin[offset + 1] << 8) | (_ramin[offset + 2] << 16) |
                      (_ramin[offset + 3] << 24));
    }

    private void SuspendPusher(uint error)
    {
        PusherError = error;
        uint state = _pfifo[PfifoCache1DmaState / 4];
        _pfifo[PfifoCache1DmaState / 4] = (state & ~DmaStateError) | ((error << 29) & DmaStateError);
        _pfifo[PfifoCache1DmaPush / 4] |= DmaPushSuspended;
    }

    private bool TryRamhtLookup(uint handle, uint channel, out uint instance, out uint engine)
    {
        instance = 0;
        engine = 0;
        uint ramht = _pfifo[PfifoRamht / 4];
        uint size = 1u << (int)(((ramht & RamhtSizeMask) >> 16) + 12);
        int bits = BitOperations.TrailingZeroCount(size) - 1;
        uint folded = 0;
        uint remaining = handle;
        uint mask = (1u << bits) - 1u;
        while (remaining != 0)
        {
            folded ^= remaining & mask;
            remaining >>= bits;
        }

        uint hash = folded ^ (channel << (bits - 4));
        uint offset = ((ramht & RamhtBaseMask) << 8) + hash * 8;
        if (offset + 8 > PraminSize)
        {
            return false;
        }

        if (RaminWord(offset) != handle || (RaminWord(offset + 4) & RamhtValid) == 0)
        {
            return false;
        }

        uint context = RaminWord(offset + 4);
        instance = (context & RamhtInstanceMask) << 4;
        engine = (context & RamhtEngineMask) >> 16;
        return true;
    }

    private void RunPusher()
    {
        if (_memory is null)
        {
            return;
        }

        if ((_pfifo[PfifoCache1Push0 / 4] & Push0Access) == 0)
        {
            return;
        }

        uint push1 = _pfifo[PfifoCache1Push1 / 4];
        uint channel = push1 & Push1ChidMask;
        if (((_pfifo[PfifoMode / 4] >> (int)channel) & 1u) == 0)
        {
            return;
        }

        if ((push1 & Push1ModeDma) == 0)
        {
            return;
        }

        uint dmaPush = _pfifo[PfifoCache1DmaPush / 4];
        if ((dmaPush & DmaPushAccess) == 0 || (dmaPush & DmaPushSuspended) != 0)
        {
            return;
        }

        if ((_pfifo[PfifoCache1DmaState / 4] & DmaStateError) != DmaErrorNone)
        {
            return;
        }

        uint instance = (_pfifo[PfifoCache1DmaInstance / 4] & DmaInstanceAddressMask) << 4;
        if (instance + 12 > PraminSize)
        {
            SuspendPusher(DmaErrorProtection);
            return;
        }

        uint dmaFlags = RaminWord(instance);
        uint dmaLimit = RaminWord(instance + 4);
        uint dmaFrame = RaminWord(instance + 8);
        uint dmaBase = ((dmaFrame & DmaAddressMask) | (dmaFlags & DmaAdjustMask)) & GpuAddressMask;

        uint get = _pfifo[PfifoCache1DmaGet / 4];
        uint put = _pfifo[PfifoCache1DmaPut / 4];
        uint words = 0;
        while (true)
        {
            if ((_pgraph[PgraphFifo / 4] & PgraphFifoAccess) == 0)
            {
                break;
            }

            if (get == put)
            {
                break;
            }

            if (get >= dmaLimit)
            {
                SuspendPusher(DmaErrorProtection);
                break;
            }

            if (++words > MaxPusherWords)
            {
                PusherCapped = true;
                break;
            }

            uint word = _memory.ReadPhysical(dmaBase + get);
            get += 4;

            uint state = _pfifo[PfifoCache1DmaState / 4];
            uint count = (state & DmaStateMethodCount) >> 18;
            if (count != 0)
            {
                _pfifo[PfifoCache1DataShadow / 4] = word;
                _pfifo[PfifoCache1Status / 4] &= ~PfifoCache1StatusLowMark;
                int processed = RunPuller(
                    channel,
                    state & DmaStateMethod,
                    (state & DmaStateSubchannel) >> 13,
                    (state & DmaStateMethodType) == 0,
                    word);
                if (processed < 0)
                {
                    break;
                }

                uint consumed = (uint)processed;
                if (consumed > 0)
                {
                    if ((state & DmaStateMethodType) == 0)
                    {
                        uint advanced = ((state & DmaStateMethod) + 4 * consumed) & DmaStateMethod;
                        _pfifo[PfifoCache1DmaState / 4] =
                            (_pfifo[PfifoCache1DmaState / 4] & ~DmaStateMethod) | advanced;
                    }

                    uint remaining = count - Math.Min(count, consumed);
                    _pfifo[PfifoCache1DmaState / 4] =
                        (_pfifo[PfifoCache1DmaState / 4] & ~DmaStateMethodCount) |
                        ((remaining << 18) & DmaStateMethodCount);
                    _pfifo[PfifoCache1DmaDcount / 4] += consumed;
                    get += (consumed - 1) * 4;
                    _pfifo[PfifoCache1Status / 4] |= PfifoCache1StatusLowMark;
                }
            }
            else
            {
                _pfifo[PfifoCache1RsvdShadow / 4] = word;
                if ((word & 0xE0000003u) == 0x20000000u)
                {
                    _pfifo[PfifoCache1GetJmpShadow / 4] = get;
                    get = word & 0x1FFFFFFFu;
                }
                else if ((word & 3u) == 1u)
                {
                    _pfifo[PfifoCache1GetJmpShadow / 4] = get;
                    get = word & 0xFFFFFFFCu;
                }
                else if ((word & 3u) == 2u)
                {
                    uint subroutine = _pfifo[PfifoCache1DmaSubroutine / 4];
                    if ((subroutine & SubroutineActive) != 0)
                    {
                        SuspendPusher(DmaErrorCall);
                    }
                    else
                    {
                        _pfifo[PfifoCache1DmaSubroutine / 4] = get | SubroutineActive;
                        get = word & 0xFFFFFFFCu;
                    }
                }
                else if (word == 0x00020000u)
                {
                    uint subroutine = _pfifo[PfifoCache1DmaSubroutine / 4];
                    if ((subroutine & SubroutineActive) == 0)
                    {
                        SuspendPusher(DmaErrorReturn);
                    }
                    else
                    {
                        get = subroutine & 0xFFFFFFFCu;
                        _pfifo[PfifoCache1DmaSubroutine / 4] &= ~SubroutineActive;
                    }
                }
                else if ((word & 0xE0030003u) == 0u)
                {
                    SetDmaMethod(word, increment: true);
                    _pfifo[PfifoCache1DmaDcount / 4] = 0;
                }
                else if ((word & 0xE0030003u) == 0x40000000u)
                {
                    SetDmaMethod(word, increment: false);
                    _pfifo[PfifoCache1DmaDcount / 4] = 0;
                }
                else
                {
                    SuspendPusher(DmaErrorReservedCommand);
                }
            }

            _pfifo[PfifoCache1DmaGet / 4] = get;
            if ((_pfifo[PfifoCache1DmaState / 4] & DmaStateError) != DmaErrorNone)
            {
                break;
            }
        }

        PusherWordsConsumed += words;
    }

    private void SetDmaMethod(uint word, bool increment)
    {
        uint state = _pfifo[PfifoCache1DmaState / 4];
        state = (state & ~DmaStateMethod) | (word & DmaStateMethod);
        state = (state & ~DmaStateSubchannel) | (word & DmaStateSubchannel);
        state = (state & ~DmaStateMethodCount) | (word & DmaStateMethodCount);
        if (increment)
        {
            state &= ~DmaStateMethodType;
        }
        else
        {
            state |= DmaStateMethodType;
        }

        _pfifo[PfifoCache1DmaState / 4] = state;
    }

    private int RunPuller(uint channel, uint method, uint subchannel, bool increment, uint parameter)
    {
        if (((_pfifo[PfifoCache1Pull0 / 4] & Pull0Access) == 0) ||
            ((_pfifo[PfifoCache1Status / 4] & PfifoCache1StatusLowMark) != 0))
        {
            return -1;
        }

        if (subchannel >= 8)
        {
            SuspendPusher(DmaErrorProtection);
            return -1;
        }

        if (method == 0)
        {
            if (!TryRamhtLookup(parameter, channel, out uint instance, out var engine))
            {
                SuspendPusher(DmaErrorProtection);
                return -1;
            }

            if (engine != EngineGraphics)
            {
                _boundEngine[subchannel] = engine;
                return 1;
            }

            if (_boundEngine[subchannel] != engine)
            {
                _boundEngine[subchannel] = engine;
                ContextSwitches++;
                PgraphPendingDebug |= PgraphContextSwitch;
                UpdateIrq();
            }

            _boundInstance[subchannel] = instance;

            RecordMethod(channel, subchannel, method, parameter);
            return 1;
        }

        if (method >= ObjectParamLow && method < ObjectParamHigh)
        {
            if (!TryRamhtLookup(parameter, channel, out _, out _))
            {
                SuspendPusher(DmaErrorProtection);
                return -1;
            }

            RecordMethod(channel, subchannel, method, parameter);
            return 1;
        }

        if (method >= 0x100u)
        {
            RecordMethod(channel, subchannel, method, parameter);
            return 1;
        }

        SuspendPusher(DmaErrorReservedCommand);
        return -1;
    }

    private void RecordMethod(uint channel, uint subchannel, uint method, uint parameter)
    {
        uint key = (subchannel << 16) | method;
        _methodCounts.TryGetValue(key, out int count);
        _methodCounts[key] = count + 1;
        _methodParams[key] = parameter;
        if (_methodStream.Count < 262144)
        {
            _methodStream.Add((subchannel, method, parameter));
        }

        switch (method)
        {
            case MethodNoOperation:
                if (parameter != 0)
                {
                    RaiseNotification(channel, subchannel, parameter);
                }

                break;
            case MethodSurfaceClipH:
                _surfaceClipH = parameter;
                break;
            case MethodSurfaceClipV:
                _surfaceClipV = parameter;
                break;
            case MethodSurfaceFormat:
                _surfaceFormat = parameter;
                break;
            case MethodSurfacePitch:
                _surfacePitch = parameter;
                break;
            case MethodSurfaceColorOffset:
                PresentedColorOffset = parameter;
                break;
            case MethodSurfaceZetaOffset:
                _surfaceZetaOffset = parameter;
                break;
            case MethodZetaClearValue:
                _zetaClearValue = parameter;
                break;
            case MethodSetContextDmaSemaphore:
                _dmaSemaphore = parameter;
                break;
            case MethodSetSemaphoreOffset:
                _semaphoreOffset = parameter;
                break;
            case MethodBackEndWriteSemaphoreRelease:
                WriteSemaphoreRelease(channel, parameter);
                break;
            case MethodColorClearValue:
                _colorClearValue = parameter;
                break;
            case MethodClearRectH:
                _clearRectH = parameter;
                break;
            case MethodClearRectV:
                _clearRectV = parameter;
                break;
            case MethodFlipRead:
                _flipRead = parameter;
                break;
            case MethodFlipWrite:
                _flipWrite = parameter;
                break;
            case MethodFlipModulo:
                _flipModulo = parameter;
                break;
            case MethodClearSurface:
                ExecuteClear(parameter);
                break;
            case MethodBeginEnd:
                if (parameter == 0)
                {
                    FlushInlineDraw();
                    ResetInlineBuffers();
                }
                else
                {
                    _beginMode = parameter;
                    ResetInlineBuffers();
                }

                break;
            case MethodArrayElement16:
                AppendInlineElement16(parameter);
                break;
            case MethodArrayElement32:
                if (_inlineElements.Count < MaxInlineElements)
                {
                    _inlineElements.Add(parameter);
                }

                break;
            case MethodDrawArrays:
                AppendDrawArrays(parameter);
                break;
            case MethodInlineArray:
                if (_inlineWords.Count < MaxInlineWords)
                {
                    _inlineWords.Add(parameter);
                }

                break;
            case MethodTransformExecutionMode:
                _transformExecutionMode = parameter;
                break;
            case MethodTransformProgramCxtWriteEn:
                _transformProgramCxtWriteEn = parameter;
                break;
            case MethodTransformProgramLoad:
                _transformProgramLoad = parameter;
                break;
            case MethodTransformProgramStart:
                _transformProgramStart = parameter;
                break;
            case MethodTransformConstantLoad:
                _transformConstantLoad = parameter;
                break;
            default:
                break;
        }

        if (method >= MethodVertexFormatBase && method <= MethodVertexFormatEnd &&
            ((method - MethodVertexFormatBase) & 3u) == 0)
        {
            _vertexFormat[(method - MethodVertexFormatBase) / 4] = parameter;
        }

        if (method >= MethodVertexArrayOffsetBase && method <= MethodVertexArrayOffsetEnd &&
            ((method - MethodVertexArrayOffsetBase) & 3u) == 0)
        {
            _vertexArrayOffset[(method - MethodVertexArrayOffsetBase) / 4] = parameter;
        }

        if (method >= MethodTransformProgramBase && method <= MethodTransformProgramEnd &&
            ((method - MethodTransformProgramBase) & 3u) == 0)
        {
            WriteTransformProgram((method - MethodTransformProgramBase) / 4, parameter);
        }

        if (method >= MethodTransformConstantBase && method <= MethodTransformConstantEnd &&
            ((method - MethodTransformConstantBase) & 3u) == 0)
        {
            WriteTransformConstant((method - MethodTransformConstantBase) / 4, parameter);
        }
    }

    private void ResetInlineBuffers()
    {
        _inlineElements.Clear();
        _inlineWords.Clear();
    }

    private void AppendInlineElement16(uint parameter)
    {
        if (_inlineElements.Count + 2 > MaxInlineElements)
        {
            return;
        }

        _inlineElements.Add(parameter & 0xFFFFu);
        _inlineElements.Add(parameter >> 16);
    }

    private void AppendDrawArrays(uint parameter)
    {
        int start = (int)(parameter & 0x00FFFFFFu);
        int count = (int)((parameter >> 24) & 0xFFu) + 1;
        if (_inlineElements.Count != 0)
        {
            for (int i = 0; i < count && _inlineElements.Count < MaxInlineElements; i++)
            {
                _inlineElements.Add((uint)(start + i));
            }

            return;
        }

        for (int i = 0; i < count; i++)
        {
            _inlineElements.Add((uint)(start + i));
        }
    }

    private void WriteTransformProgram(uint slot, uint value)
    {
        if (_transformProgramLoad >= MaxTransformProgramLength)
        {
            return;
        }

        _transformProgram[_transformProgramLoad][slot & 3u] = value;
        if ((slot & 3u) == 3u)
        {
            _transformProgramLoad++;
        }
    }

    private void WriteTransformConstant(uint slot, uint value)
    {
        if (_transformConstantLoad >= MaxVertexShaderConstants)
        {
            return;
        }

        _transformConstants[_transformConstantLoad][slot & 3u] = value;
        if ((slot & 3u) == 3u)
        {
            _transformConstantLoad++;
        }
    }

    private void RaiseNotification(uint channel, uint subchannel, uint parameter)
    {
        _pgraph[PgraphTrappedAddr / 4] =
            ((channel & 0x1Fu) << 20) | ((subchannel & 7u) << 16) | (MethodNoOperation & 0x1FFFu);
        _pgraph[PgraphTrappedDataLow / 4] = parameter;
        _pgraph[PgraphNsource / 4] = PgraphNsourceNotification;
        PgraphPendingDebug |= PgraphIntrError;
        NotificationsRaised++;
        UpdateIrq();
        NotificationAsserted = InterruptAsserted;
    }

    private void WriteSemaphoreRelease(uint channel, uint value)
    {
        if (_memory is null)
        {
            return;
        }

        uint instance;
        if (TryRamhtLookup(_dmaSemaphore, channel, out uint handleInstance, out _))
        {
            instance = handleInstance;
        }
        else
        {
            instance = _dmaSemaphore & DmaInstanceAddressMask;
        }

        if (instance + 12 > PraminSize)
        {
            SemaphoreDebug = $"dma={_dmaSemaphore:X} instance={instance:X} out-of-range";
            return;
        }

        uint flags = RaminWord(instance);
        uint frame = RaminWord(instance + 8);
        uint address = ((frame & DmaAddressMask) | (flags & DmaAdjustMask)) & GpuAddressMask;
        uint target = (address + _semaphoreOffset) & GpuAddressMask;
        SemaphoreDebug =
            $"dma={_dmaSemaphore:X} off={_semaphoreOffset:X} flags={flags:X8} frame={frame:X8} target={target:X8} value={value:X}";
        if (target + 4 <= RamSize)
        {
            _memory.WritePhysical(target, value);
            SemaphoreReleases++;
        }
    }

    private void ExecuteClear(uint parameter)
    {
        ClearsExecuted++;
        if (_memory is null)
        {
            return;
        }

        if ((_surfaceFormat & 0xF00u) >> 8 != SurfaceTypePitch)
        {
            return;
        }

        bool clearColor = (parameter & 0xF0u) != 0;
        bool clearZeta = (parameter & 0x3u) != 0;
        if (!clearColor && !clearZeta)
        {
            return;
        }

        uint width = _surfaceClipH >> 16;
        uint height = _surfaceClipV >> 16;
        if (width == 0 || height == 0)
        {
            return;
        }

        uint xmin = _clearRectH & 0xFFFu;
        uint xmax = (_clearRectH >> 16) & 0xFFFu;
        uint ymin = _clearRectV & 0xFFFu;
        uint ymax = (_clearRectV >> 16) & 0xFFFu;
        if (xmax < xmin || ymax < ymin)
        {
            return;
        }

        uint x1 = Math.Min(xmax, width - 1);
        uint y1 = Math.Min(ymax, height - 1);
        if ((ulong)(x1 - xmin + 1) * (y1 - ymin + 1) > MaxClearPixels)
        {
            return;
        }

        if (clearColor)
        {
            uint bytes = ColorBytesPerPixel(_surfaceFormat & 0xFu);
            if (bytes != 0)
            {
                uint pitch = _surfacePitch & 0xFFFFu;
                uint pixel = _colorClearValue & (bytes switch
                {
                    4 => 0xFFFFFFFFu,
                    2 => 0xFFFFu,
                    _ => 0xFFu
                });
                FillSurface(PresentedColorOffset, pitch, bytes, xmin, ymin, x1, y1, pixel);
            }
        }

        if (clearZeta)
        {
            uint zetaFormat = (_surfaceFormat >> 4) & 0xFu;
            uint bytes = zetaFormat switch
            {
                1u => 2u,
                2u => 4u,
                _ => 0u
            };
            if (bytes != 0)
            {
                uint pitch = (_surfacePitch >> 16) & 0xFFFFu;
                uint pixel = _zetaClearValue & (bytes == 4 ? 0xFFFFFFFFu : 0xFFFFu);
                FillSurface(_surfaceZetaOffset, pitch, bytes, xmin, ymin, x1, y1, pixel);
            }
        }
    }

    private void FillSurface(uint offset, uint pitch, uint bytes, uint xmin, uint ymin, uint xmax, uint ymax,
        uint pixel)
    {
        if (_memory is null || pitch == 0)
        {
            return;
        }

        for (uint y = ymin; y <= ymax; y++)
        {
            uint row = offset + y * pitch;
            for (uint x = xmin; x <= xmax; x++)
            {
                uint address = row + x * bytes;
                if (address + bytes > RamSize)
                {
                    return;
                }

                if (bytes == 4)
                {
                    _memory.WritePhysical(address, pixel);
                }
                else
                {
                    uint current = _memory.ReadPhysical(address & ~3u);
                    uint shift = (address & 3u) * 8;
                    uint mask = bytes == 2 ? 0xFFFFu << (int)shift : 0xFFu << (int)shift;
                    _memory.WritePhysical(address & ~3u, (current & ~mask) | ((pixel << (int)shift) & mask));
                }
            }
        }
    }

    private static uint ColorBytesPerPixel(uint format)
    {
        return format switch
        {
            0x09u => 1u,
            0x0Au => 2u,
            0x01u or 0x02u or 0x03u => 2u,
            0x04u or 0x05u or 0x06u or 0x07u or 0x08u => 4u,
            _ => 0u,
        };
    }

    private void WritePixel(uint address, uint bytes, uint pixel)
    {
        if (_memory is null || address + bytes > RamSize)
        {
            return;
        }

        if (bytes == 4)
        {
            _memory.WritePhysical(address, pixel);
            return;
        }

        uint aligned = address & ~3u;
        uint shift = (address & 3u) * 8;
        uint current = _memory.ReadPhysical(aligned);
        uint mask = bytes == 2 ? 0xFFFFu << (int)shift : 0xFFu << (int)shift;
        _memory.WritePhysical(aligned, (current & ~mask) | ((pixel << (int)shift) & mask));
    }

    private void FlushInlineDraw()
    {
        try
        {
            if (_beginMode != BeginEndTriangles && _beginMode != BeginEndTriangleStrip &&
                _beginMode != BeginEndTriangleFan && _beginMode != BeginEndQuads)
            {
                SkippedDraws++;
                return;
            }

            uint[] offsets = new uint[16];
            int positionSlot = -1;
            int colorSlot = -1;
            uint cursor = 0;
            for (int slot = 0; slot < 16; slot++)
            {
                uint format = _vertexFormat[slot];
                if (((format >> 4) & 0xFu) == 0)
                {
                    continue;
                }

                if ((format & 0xFu) != 2u)
                {
                    SkippedDraws++;
                    return;
                }

                offsets[slot] = cursor / 4;
                cursor += 4u * ((format >> 4) & 0xFu);
                if (positionSlot < 0)
                {
                    positionSlot = slot;
                }
                else if (colorSlot < 0)
                {
                    colorSlot = slot;
                }
            }

            if (positionSlot < 0 || colorSlot < 0 || cursor == 0 ||
                _inlineWords.Count % (cursor / 4) != 0)
            {
                SkippedDraws++;
                return;
            }

            uint bytes = ColorBytesPerPixel(_surfaceFormat & 0xFu);
            uint pitch = _surfacePitch & 0xFFFFu;
            if (bytes != 4 && bytes != 2)
            {
                SkippedDraws++;
                return;
            }

            uint clipX = _surfaceClipH & 0xFFFFu;
            uint clipY = _surfaceClipV & 0xFFFFu;
            uint clipW = _surfaceClipH >> 16;
            uint clipH = _surfaceClipV >> 16;
            if (clipW == 0 || clipH == 0 || pitch == 0)
            {
                SkippedDraws++;
                return;
            }

            uint vertexWords = cursor / 4;
            int vertexCount = _inlineWords.Count / (int)vertexWords;
            uint colorCount = (_vertexFormat[colorSlot] >> 4) & 0xFu;
            DrawsExecuted++;
            switch (_beginMode)
            {
                case BeginEndTriangles:
                    for (int first = 0; first + 2 < vertexCount; first += 3)
                    {
                        RasterizeTriangle(first, first + 1, first + 2);
                    }

                    break;
                case BeginEndTriangleStrip:
                    for (int first = 0; first + 2 < vertexCount; first++)
                    {
                        RasterizeTriangle(first, first + 1, first + 2);
                    }

                    break;
                case BeginEndTriangleFan:
                    for (int first = 1; first + 1 < vertexCount; first++)
                    {
                        RasterizeTriangle(0, first, first + 1);
                    }

                    break;
                default:
                    for (int first = 0; first + 3 < vertexCount; first += 4)
                    {
                        RasterizeTriangle(first, first + 1, first + 2);
                        RasterizeTriangle(first, first + 2, first + 3);
                    }

                    break;
            }

            void RasterizeTriangle(int a, int b, int c)
            {
                float ax = InlineFloat(a, (int)offsets[positionSlot]);
                float ay = InlineFloat(a, (int)offsets[positionSlot] + 1);
                float bx = InlineFloat(b, (int)offsets[positionSlot]);
                float by = InlineFloat(b, (int)offsets[positionSlot] + 1);
                float cx = InlineFloat(c, (int)offsets[positionSlot]);
                float cy = InlineFloat(c, (int)offsets[positionSlot] + 1);
                float ar = InlineColor(a, 0);
                float ag = InlineColor(a, 1);
                float ab = InlineColor(a, 2);
                float aa = colorCount >= 4 ? InlineColor(a, 3) : 1f;
                float br = InlineColor(b, 0);
                float bg = InlineColor(b, 1);
                float bb = InlineColor(b, 2);
                float ba = colorCount >= 4 ? InlineColor(b, 3) : 1f;
                float cr = InlineColor(c, 0);
                float cg = InlineColor(c, 1);
                float cb = InlineColor(c, 2);
                float ca = colorCount >= 4 ? InlineColor(c, 3) : 1f;

                float area = ((bx - ax) * (cy - ay)) - ((cx - ax) * (by - ay));
                if (area == 0f)
                {
                    return;
                }

                uint x0 = (uint)Math.Max(0f, Math.Min(Math.Min(ax, bx), cx));
                uint x1 = (uint)Math.Min((float)clipX + clipW - 1, Math.Max(Math.Max(ax, bx), cx));
                uint y0 = (uint)Math.Max(0f, Math.Min(Math.Min(ay, by), cy));
                uint y1 = (uint)Math.Min((float)clipY + clipH - 1, Math.Max(Math.Max(ay, by), cy));
                if (x1 < x0 || y1 < y0)
                {
                    return;
                }

                float invArea = 1f / area;
                for (uint y = y0; y <= y1; y++)
                {
                    for (uint x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f;
                        float py = y + 0.5f;
                        float w0 = (((bx - ax) * (py - ay)) - ((by - ay) * (px - ax))) * invArea;
                        float w1 = (((cx - bx) * (py - by)) - ((cy - by) * (px - bx))) * invArea;
                        float w2 = 1f - w0 - w1;
                        bool inside = (w0 >= 0f && w1 >= 0f && w2 >= 0f) ||
                                      (w0 <= 0f && w1 <= 0f && w2 <= 0f);
                        if (!inside)
                        {
                            continue;
                        }

                        float r = (w1 * ar) + (w2 * br) + (w0 * cr);
                        float g = (w1 * ag) + (w2 * bg) + (w0 * cg);
                        float bl = (w1 * ab) + (w2 * bb) + (w0 * cb);
                        float al = (w1 * aa) + (w2 * ba) + (w0 * ca);
                        uint pixel = bytes == 4
                            ? ((uint)(Math.Clamp(al, 0f, 1f) * 255f + 0.5f) << 24) |
                              ((uint)(Math.Clamp(r, 0f, 1f) * 255f + 0.5f) << 16) |
                              ((uint)(Math.Clamp(g, 0f, 1f) * 255f + 0.5f) << 8) |
                              (uint)(Math.Clamp(bl, 0f, 1f) * 255f + 0.5f)
                            : ((uint)(Math.Clamp(r, 0f, 1f) * 31f + 0.5f) << 11) |
                              ((uint)(Math.Clamp(g, 0f, 1f) * 63f + 0.5f) << 5) |
                              (uint)(Math.Clamp(bl, 0f, 1f) * 31f + 0.5f);
                        WritePixel(PresentedColorOffset + (y * pitch) + (x * bytes), bytes, pixel);
                        DrawnPixels++;
                    }
                }
            }

            float InlineFloat(int vertex, int word)
            {
                return BitConverter.Int32BitsToSingle((int)_inlineWords[(vertex * (int)vertexWords) + word]);
            }

            float InlineColor(int vertex, int component)
            {
                uint baseWord = (uint)(vertex * (int)vertexWords) + offsets[colorSlot];
                if (colorCount <= (uint)component)
                {
                    return component == 3 ? 1f : 0f;
                }

                return Math.Clamp(BitConverter.Int32BitsToSingle((int)_inlineWords[(int)baseWord + component]), 0f, 1f);
            }
        }
        finally
        {
            _inlineWords.Clear();
        }
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
