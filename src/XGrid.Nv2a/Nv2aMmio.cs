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
    private const uint MethodFlipIncrementWrite = 0x12Cu;
    private const uint MethodFlipStall = 0x130u;
    private const uint MaxPresentDimension = 4096u;
    private const ulong MaxPresentPixels = (ulong)MaxPresentDimension * MaxPresentDimension;
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
    private const uint MethodContextDmaZeta = 0x198u;
    private const uint MethodAlphaTestEnable = 0x300u;
    private const uint MethodBlendEnable = 0x304u;
    private const uint MethodDepthTestEnable = 0x30Cu;
    private const uint MethodStencilTestEnable = 0x32Cu;
    private const uint MethodAlphaFunc = 0x33Cu;
    private const uint MethodAlphaRef = 0x340u;
    private const uint MethodBlendSrcFactor = 0x344u;
    private const uint MethodBlendDstFactor = 0x348u;
    private const uint MethodBlendColor = 0x34Cu;
    private const uint MethodBlendEquation = 0x350u;
    private const uint MethodDepthFunc = 0x354u;
    private const uint MethodColorMask = 0x358u;
    private const uint MethodDepthMask = 0x35Cu;
    private const uint MethodStencilWriteMask = 0x360u;
    private const uint MethodStencilFunc = 0x364u;
    private const uint MethodStencilRef = 0x368u;
    private const uint MethodStencilReadMask = 0x36Cu;
    private const uint MethodStencilOpFail = 0x370u;
    private const uint MethodStencilOpZfail = 0x374u;
    private const uint MethodStencilOpZpass = 0x378u;
    private const uint MethodVertexArrayOffsetBase = 0x1720u;
    private const uint MethodVertexArrayOffsetEnd = 0x175Cu;
    private const uint MethodVertexFormatBase = 0x1760u;
    private const uint MethodVertexFormatEnd = 0x179Cu;
    private const uint MethodBeginEnd = 0x17FCu;
    private const uint MethodArrayElement16 = 0x1800u;
    private const uint MethodArrayElement32 = 0x1808u;
    private const uint MethodDrawArrays = 0x1810u;
    private const uint MethodInlineArray = 0x1818u;
    private const uint MethodVertexData2FBase = 0x1880u;
    private const uint MethodVertexData2FEnd = 0x18FCu;
    private const uint MethodVertexData2SBase = 0x1900u;
    private const uint MethodVertexData2SEnd = 0x193Cu;
    private const uint MethodVertexData4UbBase = 0x1940u;
    private const uint MethodVertexData4UbEnd = 0x197Cu;
    private const uint MethodVertexData4SBase = 0x1980u;
    private const uint MethodVertexData4SEnd = 0x19FCu;
    private const uint MethodVertexData4FBase = 0x1A00u;
    private const uint MethodVertexData4FEnd = 0x1AFCu;
    private const uint MethodTransformProgramBase = 0x0B00u;
    private const uint MethodTransformProgramEnd = 0x0B7Cu;
    private const uint MethodTransformConstantBase = 0x0B80u;
    private const uint MethodTransformConstantEnd = 0x0BFCu;
    private const uint MethodTransformExecutionMode = 0x1E94u;
    private const uint MethodTransformProgramCxtWriteEn = 0x1E98u;
    private const uint MethodTransformProgramLoad = 0x1E9Cu;
    private const uint MethodTransformProgramStart = 0x1EA0u;
    private const uint MethodTransformConstantLoad = 0x1EA4u;
    private const uint MethodCompositeMatrix = 0x0680u;
    private const uint MethodCompositeMatrixEnd = 0x06BCu;
    private const uint MethodProjectionMatrix = 0x0440u;
    private const uint MethodProjectionMatrixEnd = 0x047Cu;
    private const uint MethodModelViewMatrix = 0x0480u;
    private const uint MethodModelViewMatrixEnd = 0x057Cu;
    private const uint MethodInverseModelViewMatrix = 0x0580u;
    private const uint MethodInverseModelViewMatrixEnd = 0x067Cu;
    private const uint MethodTextureMatrix = 0x06C0u;
    private const uint MethodTextureMatrixEnd = 0x07BCu;
    private const uint MethodTexGenPlane = 0x0840u;
    private const uint MethodTexGenPlaneEnd = 0x093Cu;
    private const uint MethodFogPlane = 0x09D0u;
    private const uint MethodFogPlaneEnd = 0x09DCu;
    private const uint MethodViewportOffset = 0x0A20u;
    private const uint MethodViewportOffsetEnd = 0x0A2Cu;
    private const uint MethodEyePosition = 0x0A50u;
    private const uint MethodEyePositionEnd = 0x0A5Cu;
    private const uint MethodViewportScale = 0x0AF0u;
    private const uint MethodViewportScaleEnd = 0x0AFCu;
    private const int XfConstantComposite = 0x00;
    private const int XfConstantProjection = 0x04;
    private const int XfConstantModelView = 0x08;
    private const int XfConstantInverseModelView = 0x0C;
    private const int XfConstantEyePosition = 0x38;
    private const int XfConstantFogPlane = 0x39;
    private const int XfConstantViewportScale = 0x3A;
    private const int XfConstantViewportOffset = 0x3B;
    private const int XfConstantTexGen = 0x40;
    private const int XfConstantTexture = 0x44;
    private const uint MethodTextureBase = 0x1B00u;
    private const uint MethodTextureEnd = 0x1BFCu;
    private const uint TextureSlotStride = 0x40u;
    private const uint TextureControl0Enable = 1u << 30;
    private const int MaxTextureSlots = 4;
    private const int MaxTransformProgramLength = 136;
    private const int MaxVertexShaderConstants = 192;
    private const int MaxInlineElements = 65536;
    private const int MaxDrawArrayRanges = 1250;
    private const int VertexAttributeCount = 16;
    private const int VertexAttributeComponentCount = 4;
    private const int InlineVertexWordCount = VertexAttributeCount * VertexAttributeComponentCount;
    private const uint BeginEndTriangles = 5u;
    private const uint BeginEndTriangleStrip = 6u;
    private const uint BeginEndTriangleFan = 7u;
    private const uint BeginEndQuads = 8u;
    private const uint MaxInlineWords = 65536u;
    private const uint SurfaceTypePitch = 1u;
    private const uint ZetaFormatZ16 = 1u;
    private const uint ZetaFormatZ24S8 = 2u;
    private const uint CompareLess = 1u;
    private const uint CompareAlways = 7u;
    private const uint BlendZero = 0u;
    private const uint BlendOne = 1u;
    private const uint EquationAdd = 2u;
    private const uint StencilOpKeep = 1u;
    private const uint ColorMaskAll = 0x01010101u;
    private const uint StencilByteMask = 0xFFu;
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
    private uint _presentedOffset;
    private uint _presentedWidth;
    private uint _presentedHeight;
    private uint _presentedPitch;
    private uint _presentedBytes;
    private bool _presentedValid;
    private ulong _presentedFrame;
    private readonly uint[] _vertexFormat = new uint[16];
    private readonly uint[] _vertexArrayOffset = new uint[16];
    private readonly uint[][] _transformProgram = new uint[MaxTransformProgramLength][];
    private readonly uint[][] _transformConstants = new uint[MaxVertexShaderConstants][];
    private uint _transformProgramStart;
    private uint _transformExecutionMode;
    private uint _transformProgramCxtWriteEn;
    private readonly List<uint> _inlineElements = new();
    private readonly List<uint> _inlineWords = new();
    private readonly List<float> _inlineVertices = new();
    private readonly List<(int Start, int Count)> _drawArraysRanges = new();
    private readonly float[][] _inlineValues;
    private readonly uint[] _textureOffset = new uint[MaxTextureSlots];
    private readonly uint[] _textureFormat = new uint[MaxTextureSlots];
    private readonly uint[] _textureAddress = new uint[MaxTextureSlots];
    private readonly uint[] _textureControl0 = new uint[MaxTextureSlots];
    private readonly uint[] _textureControl1 = new uint[MaxTextureSlots];
    private readonly uint[] _textureFilter = new uint[MaxTextureSlots];
    private readonly uint[] _textureImageRect = new uint[MaxTextureSlots];
    private bool _alphaTestEnable;
    private uint _alphaFunc;
    private uint _alphaRef;
    private bool _blendEnable;
    private uint _blendSrcFactor;
    private uint _blendDstFactor;
    private uint _blendColor;
    private uint _blendEquation;
    private bool _depthTestEnable;
    private uint _depthFunc;
    private bool _depthMask;
    private uint _colorMask;
    private bool _stencilTestEnable;
    private uint _stencilWriteMask;
    private uint _stencilFunc;
    private uint _stencilRef;
    private uint _stencilReadMask;
    private uint _stencilOpFail;
    private uint _stencilOpZfail;
    private uint _stencilOpZpass;
    private uint _dmaZeta;
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

        _inlineValues = new float[VertexAttributeCount][];
        for (int i = 0; i < VertexAttributeCount; i++)
        {
            _inlineValues[i] = new float[VertexAttributeComponentCount];
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

    /// <summary>Gets or sets the method stream cap (0 uncaps). Diagnostics.</summary>
    public ulong MethodStreamCap { get; set; } = 262144;

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

    /// <summary>Gets the number of pixels that sampled a texture. Diagnostics.</summary>
    public ulong TexturedPixels { get; private set; }

    /// <summary>Gets the number of pixels that ran the blend equation. Diagnostics.</summary>
    public ulong BlendedPixels { get; private set; }

    /// <summary>Gets the number of fragments that ran the depth test. Diagnostics.</summary>
    public ulong DepthTestedPixels { get; private set; }

    /// <summary>Gets the number of presented frames (flip increment events). Diagnostics.</summary>
    public ulong FramesPresented { get; private set; }

    /// <summary>Gets the number of flip stall waits observed. Diagnostics.</summary>
    public ulong FlipStalls { get; private set; }

    /// <summary>Gets the presented-frame state summary. Diagnostics.</summary>
    public string PresentedFrameDebug =>
        $"frames={FramesPresented} stalls={FlipStalls} read={FlipReadDebug} write={FlipWriteDebug} modulo={FlipModuloDebug} " +
        $"surface={_presentedOffset:X} {_presentedWidth}x{_presentedHeight} pitch={_presentedPitch} " +
        $"valid={_presentedValid} frame={_presentedFrame}";

    /// <summary>Gets the flip read buffer index. Diagnostics.</summary>
    public uint FlipReadDebug { get; private set; }

    /// <summary>Gets the flip write buffer index. Diagnostics.</summary>
    public uint FlipWriteDebug { get; private set; }

    /// <summary>Gets the flip buffer modulo. Diagnostics.</summary>
    public uint FlipModuloDebug { get; private set; }

    /// <summary>Gets the fragment-pipeline state summary. Diagnostics.</summary>
    public string FragmentStateDebug =>
        $"alpha={_alphaTestEnable}:{_alphaFunc:X} blend={_blendEnable}:{_blendSrcFactor:X}/{_blendDstFactor:X} " +
        $"depth={_depthTestEnable}:{_depthFunc:X}:{_depthMask} stencil={_stencilTestEnable}:{_stencilFunc:X} " +
        $"mask={_colorMask:X8} zetaDma={_dmaZeta:X}";

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

    /// <summary>Gets the number of collected inline vertices. Diagnostics.</summary>
    public int InlineVertexCount => _inlineVertices.Count / InlineVertexWordCount;

    /// <summary>Gets the number of pending DRAW_ARRAYS ranges. Diagnostics.</summary>
    public int DrawArrayRangeCount => _drawArraysRanges.Count;

    /// <summary>Reads a current inline attribute component (diagnostics).</summary>
    public uint ReadInlineValueWord(int attribute, int component)
    {
        return attribute is >= 0 and < VertexAttributeCount &&
               component is >= 0 and < VertexAttributeComponentCount
            ? BitConverter.SingleToUInt32Bits(_inlineValues[attribute][component])
            : 0u;
    }

    /// <summary>Gets the transform program load pointer. Diagnostics.</summary>
    public uint TransformProgramLoadDebug { get; private set; }

    /// <summary>Gets the transform constant load pointer. Diagnostics.</summary>
    public uint TransformConstantLoadDebug { get; private set; }

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
            AdvanceFlipRead();
            UpdateIrq();
        }
    }

    private void AdvanceFlipRead()
    {
        if (FlipReadDebug == FlipWriteDebug)
        {
            return;
        }

        uint modulo = FlipModuloDebug == 0 ? 1u : FlipModuloDebug;
        FlipReadDebug = (FlipReadDebug + 1) % modulo;
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
        FlipReadDebug = 0;
        FlipWriteDebug = 0;
        FlipModuloDebug = 0;
        _presentedOffset = 0;
        _presentedWidth = 0;
        _presentedHeight = 0;
        _presentedPitch = 0;
        _presentedBytes = 0;
        _presentedValid = false;
        _presentedFrame = 0;
        FramesPresented = 0;
        FlipStalls = 0;
        _alphaTestEnable = false;
        _alphaFunc = CompareAlways;
        _alphaRef = 0;
        _blendEnable = false;
        _blendSrcFactor = BlendOne;
        _blendDstFactor = BlendZero;
        _blendColor = 0;
        _blendEquation = EquationAdd;
        _depthTestEnable = false;
        _depthFunc = CompareLess;
        _depthMask = true;
        _colorMask = ColorMaskAll;
        _stencilTestEnable = false;
        _stencilWriteMask = StencilByteMask;
        _stencilFunc = CompareAlways;
        _stencilRef = 0;
        _stencilReadMask = StencilByteMask;
        _stencilOpFail = StencilOpKeep;
        _stencilOpZfail = StencilOpKeep;
        _stencilOpZpass = StencilOpKeep;
        _dmaZeta = 0;
        Array.Clear(_vertexFormat);
        Array.Clear(_vertexArrayOffset);
        Array.Clear(_textureOffset);
        Array.Clear(_textureFormat);
        Array.Clear(_textureAddress);
        Array.Clear(_textureControl0);
        Array.Clear(_textureControl1);
        Array.Clear(_textureFilter);
        Array.Clear(_textureImageRect);
        _inlineWords.Clear();
        _inlineElements.Clear();
        _inlineVertices.Clear();
        _drawArraysRanges.Clear();
        foreach (float[] value in _inlineValues)
        {
            value[0] = 0f;
            value[1] = 0f;
            value[2] = 0f;
            value[3] = 1f;
        }
        foreach (uint[] row in _transformProgram)
        {
            Array.Clear(row);
        }

        foreach (uint[] row in _transformConstants)
        {
            Array.Clear(row);
        }

        TransformProgramLoadDebug = 0;
        _transformProgramStart = 0;
        _transformExecutionMode = 0;
        _transformProgramCxtWriteEn = 0;
        TransformConstantLoadDebug = 0;
        _beginMode = 0;
        DrawsExecuted = 0;
        DrawnPixels = 0;
        TexturedPixels = 0;
        BlendedPixels = 0;
        DepthTestedPixels = 0;
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
        if (MethodStreamCap == 0 || (ulong)_methodStream.Count < MethodStreamCap)
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
            case MethodContextDmaZeta:
                _dmaZeta = parameter;
                break;
            case MethodAlphaTestEnable:
                _alphaTestEnable = (parameter & 1u) != 0;
                break;
            case MethodBlendEnable:
                _blendEnable = (parameter & 1u) != 0;
                break;
            case MethodDepthTestEnable:
                _depthTestEnable = (parameter & 1u) != 0;
                break;
            case MethodStencilTestEnable:
                _stencilTestEnable = (parameter & 1u) != 0;
                break;
            case MethodAlphaFunc:
                _alphaFunc = parameter & 0xFu;
                break;
            case MethodAlphaRef:
                _alphaRef = parameter & 0xFFu;
                break;
            case MethodBlendSrcFactor:
                _blendSrcFactor = MapBlendFactor(parameter, _blendSrcFactor);
                break;
            case MethodBlendDstFactor:
                _blendDstFactor = MapBlendFactor(parameter, _blendDstFactor);
                break;
            case MethodBlendColor:
                _blendColor = parameter;
                break;
            case MethodBlendEquation:
                _blendEquation = MapBlendEquation(parameter, _blendEquation);
                break;
            case MethodDepthFunc:
                if (parameter is >= 0x200u and <= 0x207u)
                {
                    _depthFunc = parameter & 0x7u;
                }

                break;
            case MethodColorMask:
                _colorMask = parameter;
                break;
            case MethodDepthMask:
                _depthMask = (parameter & 1u) != 0;
                break;
            case MethodStencilWriteMask:
                _stencilWriteMask = parameter & 0xFFu;
                break;
            case MethodStencilFunc:
                _stencilFunc = parameter & 0xFu;
                break;
            case MethodStencilRef:
                _stencilRef = parameter & 0xFFu;
                break;
            case MethodStencilReadMask:
                _stencilReadMask = parameter & 0xFFu;
                break;
            case MethodStencilOpFail:
                _stencilOpFail = MapStencilOp(parameter, _stencilOpFail);
                break;
            case MethodStencilOpZfail:
                _stencilOpZfail = MapStencilOp(parameter, _stencilOpZfail);
                break;
            case MethodStencilOpZpass:
                _stencilOpZpass = MapStencilOp(parameter, _stencilOpZpass);
                break;
            case MethodFlipRead:
                FlipReadDebug = parameter;
                break;
            case MethodFlipWrite:
                FlipWriteDebug = parameter;
                break;
            case MethodFlipModulo:
                FlipModuloDebug = parameter;
                break;
            case MethodFlipIncrementWrite:
                {
                    uint flipModulo = FlipModuloDebug == 0 ? 1u : FlipModuloDebug;
                    FlipWriteDebug = (FlipWriteDebug + 1) % flipModulo;
                    FramesPresented++;
                    CapturePresentedFrame();
                    break;
                }
            case MethodFlipStall:
                FlipStalls++;
                break;
            case MethodClearSurface:
                ExecuteClear(parameter);
                break;
            case MethodBeginEnd:
                if (parameter == 0)
                {
                    FlushDraw();
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
                AppendInlineElement32(parameter);
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
                TransformProgramLoadDebug = parameter;
                break;
            case MethodTransformProgramStart:
                _transformProgramStart = parameter;
                break;
            case MethodTransformConstantLoad:
                TransformConstantLoadDebug = parameter;
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

        if (method >= MethodCompositeMatrix && method <= MethodCompositeMatrixEnd)
        {
            WriteAliasedConstant(method, MethodCompositeMatrix, XfConstantComposite, parameter);
        }

        if (method >= MethodProjectionMatrix && method <= MethodProjectionMatrixEnd)
        {
            WriteAliasedConstant(method, MethodProjectionMatrix, XfConstantProjection, parameter);
        }

        if (method >= MethodModelViewMatrix && method <= MethodModelViewMatrixEnd)
        {
            WriteAliasedTextureConstant(method, MethodModelViewMatrix, XfConstantModelView, parameter);
        }

        if (method >= MethodInverseModelViewMatrix && method <= MethodInverseModelViewMatrixEnd)
        {
            WriteAliasedTextureConstant(method, MethodInverseModelViewMatrix, XfConstantInverseModelView, parameter);
        }

        if (method >= MethodTextureMatrix && method <= MethodTextureMatrixEnd)
        {
            WriteAliasedTextureConstant(method, MethodTextureMatrix, XfConstantTexture, parameter);
        }

        if (method >= MethodTexGenPlane && method <= MethodTexGenPlaneEnd)
        {
            WriteAliasedTextureConstant(method, MethodTexGenPlane, XfConstantTexGen, parameter);
        }

        if (method >= MethodFogPlane && method <= MethodFogPlaneEnd)
        {
            WriteAliasedConstant(method, MethodFogPlane, XfConstantFogPlane, parameter);
        }

        if (method >= MethodViewportOffset && method <= MethodViewportOffsetEnd)
        {
            WriteAliasedConstant(method, MethodViewportOffset, XfConstantViewportOffset, parameter);
        }

        if (method >= MethodEyePosition && method <= MethodEyePositionEnd)
        {
            WriteAliasedConstant(method, MethodEyePosition, XfConstantEyePosition, parameter);
        }

        if (method >= MethodViewportScale && method <= MethodViewportScaleEnd)
        {
            WriteAliasedConstant(method, MethodViewportScale, XfConstantViewportScale, parameter);
        }

        if (method >= MethodTextureBase && method <= MethodTextureEnd)
        {
            int slot = (int)((method - MethodTextureBase) / TextureSlotStride);
            uint register = (method - MethodTextureBase) % TextureSlotStride;
            if (slot < MaxTextureSlots)
            {
                switch (register)
                {
                    case 0x00u:
                        _textureOffset[slot] = parameter;
                        break;
                    case 0x04u:
                        _textureFormat[slot] = parameter;
                        break;
                    case 0x08u:
                        _textureAddress[slot] = parameter;
                        break;
                    case 0x0Cu:
                        _textureControl0[slot] = parameter;
                        break;
                    case 0x10u:
                        _textureControl1[slot] = parameter;
                        break;
                    case 0x14u:
                        _textureFilter[slot] = parameter;
                        break;
                    case 0x1Cu:
                        _textureImageRect[slot] = parameter;
                        break;
                    default:
                        break;
                }
            }
        }

        HandleVertexDataMethod(method, parameter);
    }

    private void HandleVertexDataMethod(uint method, uint parameter)
    {
        if (method >= MethodVertexData2FBase && method <= MethodVertexData2FEnd)
        {
            int slot = (int)(method - MethodVertexData2FBase) / 4;
            int attribute = slot / 2;
            int part = slot % 2;
            _inlineValues[attribute][part] = BitConverter.Int32BitsToSingle((int)parameter);
            _inlineValues[attribute][2] = 0f;
            _inlineValues[attribute][3] = 1f;
            if (attribute == 0 && part == 1)
            {
                FinishInlineVertex();
            }

            return;
        }

        if (method >= MethodVertexData2SBase && method <= MethodVertexData2SEnd)
        {
            int attribute = (int)(method - MethodVertexData2SBase) / 4;
            _inlineValues[attribute][0] = (short)(parameter & 0xFFFFu);
            _inlineValues[attribute][1] = (short)(parameter >> 16);
            _inlineValues[attribute][2] = 0f;
            _inlineValues[attribute][3] = 1f;
            if (attribute == 0)
            {
                FinishInlineVertex();
            }

            return;
        }

        if (method >= MethodVertexData4UbBase && method <= MethodVertexData4UbEnd)
        {
            int attribute = (int)(method - MethodVertexData4UbBase) / 4;
            _inlineValues[attribute][0] = (parameter & 0xFFu) / 255f;
            _inlineValues[attribute][1] = ((parameter >> 8) & 0xFFu) / 255f;
            _inlineValues[attribute][2] = ((parameter >> 16) & 0xFFu) / 255f;
            _inlineValues[attribute][3] = ((parameter >> 24) & 0xFFu) / 255f;
            if (attribute == 0)
            {
                FinishInlineVertex();
            }

            return;
        }

        if (method >= MethodVertexData4SBase && method <= MethodVertexData4SEnd)
        {
            int slot = (int)(method - MethodVertexData4SBase) / 4;
            int attribute = slot / 2;
            int part = slot % 2;
            _inlineValues[attribute][part * 2] = (short)(parameter & 0xFFFFu);
            _inlineValues[attribute][part * 2 + 1] = (short)(parameter >> 16);
            if (attribute == 0 && part == 1)
            {
                FinishInlineVertex();
            }

            return;
        }

        if (method >= MethodVertexData4FBase && method <= MethodVertexData4FEnd)
        {
            int slot = (int)(method - MethodVertexData4FBase) / 4;
            int attribute = slot / 4;
            int part = slot % 4;
            _inlineValues[attribute][part] = BitConverter.Int32BitsToSingle((int)parameter);
            if (attribute == 0 && part == 3)
            {
                FinishInlineVertex();
            }
        }
    }

    private void FinishInlineVertex()
    {
        if (InlineVertexCount >= MaxInlineElements)
        {
            return;
        }

        foreach (float[] value in _inlineValues)
        {
            _inlineVertices.Add(value[0]);
            _inlineVertices.Add(value[1]);
            _inlineVertices.Add(value[2]);
            _inlineVertices.Add(value[3]);
        }
    }

    private void ResetInlineBuffers()
    {
        _inlineElements.Clear();
        _inlineWords.Clear();
        _inlineVertices.Clear();
        _drawArraysRanges.Clear();
    }

    private void AppendInlineElement16(uint parameter)
    {
        ExpandPendingDrawArrays();
        if (_inlineElements.Count + 2 > MaxInlineElements)
        {
            return;
        }

        _inlineElements.Add(parameter & 0xFFFFu);
        _inlineElements.Add(parameter >> 16);
    }

    private void AppendInlineElement32(uint parameter)
    {
        ExpandPendingDrawArrays();
        if (_inlineElements.Count < MaxInlineElements)
        {
            _inlineElements.Add(parameter);
        }
    }

    private void ExpandPendingDrawArrays()
    {
        if (_drawArraysRanges.Count == 0)
        {
            return;
        }

        (int start, int count) = _drawArraysRanges[^1];
        if (_drawArraysRanges.Count > 1)
        {
            FlushDrawArraysDraw();
            ResetInlineBuffers();
        }

        for (int i = 0; i < count && _inlineElements.Count < MaxInlineElements; i++)
        {
            _inlineElements.Add((uint)(start + i));
        }

        _drawArraysRanges.Clear();
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

        if (_drawArraysRanges.Count > 0)
        {
            (int lastStart, int lastCount) = _drawArraysRanges[^1];
            if (start == lastStart + lastCount)
            {
                _drawArraysRanges[^1] = (lastStart, lastCount + count);
                return;
            }
        }

        if (_drawArraysRanges.Count < MaxDrawArrayRanges)
        {
            _drawArraysRanges.Add((start, count));
        }
    }

    private void WriteTransformProgram(uint slot, uint value)
    {
        if (TransformProgramLoadDebug >= MaxTransformProgramLength)
        {
            return;
        }

        _transformProgram[TransformProgramLoadDebug][slot & 3u] = value;
        if ((slot & 3u) == 3u)
        {
            TransformProgramLoadDebug++;
        }
    }

    private void WriteTransformConstant(uint slot, uint value)
    {
        if (TransformConstantLoadDebug >= MaxVertexShaderConstants)
        {
            return;
        }

        _transformConstants[TransformConstantLoadDebug][slot & 3u] = value;
        if ((slot & 3u) == 3u)
        {
            TransformConstantLoadDebug++;
        }
    }

    private void WriteAliasedConstant(uint method, uint baseMethod, int constant, uint value)
    {
        int slot = (int)(method - baseMethod) / 4;
        WriteConstantWord(constant + (slot / 4), slot % 4, value);
    }

    private void WriteAliasedTextureConstant(uint method, uint baseMethod, int constant, uint value)
    {
        int slot = (int)(method - baseMethod) / 4;
        int entry = slot % 16;
        WriteConstantWord(constant + ((slot / 16) * 8) + (entry / 4), entry % 4, value);
    }

    private void WriteConstantWord(int index, int part, uint value)
    {
        if (index is >= 0 and < MaxVertexShaderConstants)
        {
            _transformConstants[index][part & 3] = value;
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

    private static uint MapBlendFactor(uint parameter, uint current)
    {
        return parameter switch
        {
            0x0000u => 0u,
            0x0001u => 1u,
            0x0300u => 2u,
            0x0301u => 3u,
            0x0302u => 4u,
            0x0303u => 5u,
            0x0304u => 6u,
            0x0305u => 7u,
            0x0306u => 8u,
            0x0307u => 9u,
            0x0308u => 10u,
            0x8001u => 12u,
            0x8002u => 13u,
            0x8003u => 14u,
            0x8004u => 15u,
            _ => current,
        };
    }

    private static uint MapBlendEquation(uint parameter, uint current)
    {
        return parameter switch
        {
            0x800Au => 0u,
            0x800Bu => 1u,
            0x8006u => 2u,
            0x8007u => 3u,
            0x8008u => 4u,
            0xF005u => 1u,
            0xF006u => 2u,
            _ => current,
        };
    }

    private static uint MapStencilOp(uint parameter, uint current)
    {
        return parameter switch
        {
            0x1E00u => 1u,
            0x0000u => 2u,
            0x1E01u => 3u,
            0x1E02u => 4u,
            0x1E03u => 5u,
            0x150Au => 6u,
            0x8507u => 7u,
            0x8508u => 8u,
            _ => current,
        };
    }

    private static bool CompareFunc(uint func, float x, float y)
    {
        return func switch
        {
            0u => false,
            1u => x < y,
            2u => x == y,
            3u => x <= y,
            4u => x > y,
            5u => x != y,
            6u => x >= y,
            _ => true,
        };
    }

    private static uint ApplyStencilOp(uint op, uint stencil, uint reference)
    {
        return op switch
        {
            2u => 0u,
            3u => reference & StencilByteMask,
            4u => Math.Min(stencil + 1, StencilByteMask),
            5u => stencil == 0 ? 0u : stencil - 1,
            6u => ~stencil & StencilByteMask,
            7u => (stencil + 1) & StencilByteMask,
            8u => (stencil - 1) & StencilByteMask,
            _ => stencil,
        };
    }

    private static (float R, float G, float B, float A) BlendFactor(uint index,
        (float R, float G, float B, float A) src,
        (float R, float G, float B, float A) dst,
        (float R, float G, float B, float A) constant)
    {
        return index switch
        {
            0u => (0f, 0f, 0f, 0f),
            2u => src,
            3u => (1f - src.R, 1f - src.G, 1f - src.B, 1f - src.A),
            4u => (src.A, src.A, src.A, src.A),
            5u => (1f - src.A, 1f - src.A, 1f - src.A, 1f - src.A),
            6u => (dst.A, dst.A, dst.A, dst.A),
            7u => (1f - dst.A, 1f - dst.A, 1f - dst.A, 1f - dst.A),
            8u => dst,
            9u => (1f - dst.R, 1f - dst.G, 1f - dst.B, 1f - dst.A),
            10u => (Math.Min(src.A, 1f - dst.A), Math.Min(src.A, 1f - dst.A), Math.Min(src.A, 1f - dst.A), 1f),
            12u => constant,
            13u => (1f - constant.R, 1f - constant.G, 1f - constant.B, 1f - constant.A),
            14u => (constant.A, constant.A, constant.A, constant.A),
            15u => (1f - constant.A, 1f - constant.A, 1f - constant.A, 1f - constant.A),
            _ => (1f, 1f, 1f, 1f),
        };
    }

    private static uint PackPixel(uint bytes, float r, float g, float b, float a)
    {
        return bytes == 4
            ? ((uint)(Math.Clamp(a, 0f, 1f) * 255f + 0.5f) << 24) |
              ((uint)(Math.Clamp(r, 0f, 1f) * 255f + 0.5f) << 16) |
              ((uint)(Math.Clamp(g, 0f, 1f) * 255f + 0.5f) << 8) |
              (uint)(Math.Clamp(b, 0f, 1f) * 255f + 0.5f)
            : ((uint)(Math.Clamp(r, 0f, 1f) * 31f + 0.5f) << 11) |
              ((uint)(Math.Clamp(g, 0f, 1f) * 63f + 0.5f) << 5) |
              (uint)(Math.Clamp(b, 0f, 1f) * 31f + 0.5f);
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

                FillTriangle(
                    ax, ay, 0f, ar, ag, ab, aa, 0f, 0f,
                    bx, by, 0f, br, bg, bb, ba, 0f, 0f,
                    cx, cy, 0f, cr, cg, cb, ca, 0f, 0f,
                    clipX, clipY, clipW, clipH, bytes, pitch);
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

    private void FlushDraw()
    {
        if (_drawArraysRanges.Count != 0)
        {
            FlushDrawArraysDraw();
            return;
        }

        if (_inlineElements.Count != 0)
        {
            FlushElementDraw();
            return;
        }

        if (_inlineVertices.Count != 0)
        {
            FlushInlineVertexDraw();
            return;
        }

        FlushInlineDraw();
    }

    private void FlushDrawArraysDraw()
    {
        FlushProgramDraw(VertexFetchMode.Arrays);
    }

    private void FlushElementDraw()
    {
        FlushProgramDraw(VertexFetchMode.Elements);
    }

    private void FlushInlineVertexDraw()
    {
        FlushProgramDraw(VertexFetchMode.Inline);
    }

    private enum VertexFetchMode
    {
        Arrays,
        Elements,
        Inline,
    }

    private void FlushProgramDraw(VertexFetchMode mode)
    {
        if (_memory is null || TransformProgramLoadDebug == 0)
        {
            SkippedDraws++;
            return;
        }

        uint bytes = ColorBytesPerPixel(_surfaceFormat & 0xFu);
        uint pitch = _surfacePitch & 0xFFFFu;
        uint clipX = _surfaceClipH & 0xFFFFu;
        uint clipY = _surfaceClipV & 0xFFFFu;
        uint clipW = _surfaceClipH >> 16;
        uint clipH = _surfaceClipV >> 16;
        if ((bytes != 4 && bytes != 2) || pitch == 0 || clipW == 0 || clipH == 0)
        {
            SkippedDraws++;
            return;
        }

        if (_beginMode is not (BeginEndTriangles or BeginEndTriangleStrip or BeginEndTriangleFan or BeginEndQuads))
        {
            SkippedDraws++;
            return;
        }

        var constants = new float[VshProgram.ConstantCount][];
        for (int i = 0; i < constants.Length; i++)
        {
            constants[i] = new float[4];
            for (int j = 0; j < 4; j++)
            {
                constants[i][j] = BitConverter.UInt32BitsToSingle(_transformConstants[i][j]);
            }
        }

        var inputs = new float[VshProgram.RegisterCount][];
        var outputs = new float[VshProgram.RegisterCount][];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i] = new float[4];
            outputs[i] = new float[4];
        }

        var transformed = new Dictionary<int, (float X, float Y, float Z, float R, float G, float B, float A, float U, float V)>();

        uint[] elements = mode == VertexFetchMode.Elements ? _inlineElements.ToArray() : Array.Empty<uint>();
        DrawsExecuted++;
        bool colorWrite = (_colorMask & ColorMaskAll) != 0;
        if (!colorWrite && !_depthTestEnable && !_stencilTestEnable)
        {
            return;
        }

        switch (mode)
        {
            case VertexFetchMode.Arrays:
                foreach ((int start, int count) in _drawArraysRanges)
                {
                    AssemblePrimitives(start, count);
                }

                break;
            case VertexFetchMode.Inline:
                AssemblePrimitives(0, InlineVertexCount);
                break;
            default:
                AssemblePrimitives(0, elements.Length);
                break;
        }

        return;

        uint ElementAt(int offset, int i)
        {
            return mode switch
            {
                VertexFetchMode.Arrays => (uint)(offset + i),
                VertexFetchMode.Elements => elements[offset + i],
                _ => (uint)i,
            };
        }

        void AssemblePrimitives(int offset, int count)
        {
            switch (_beginMode)
            {
                case BeginEndTriangles:
                    for (int i = 0; i + 2 < count; i += 3)
                    {
                        Triangle(ElementAt(offset, i), ElementAt(offset, i + 1), ElementAt(offset, i + 2));
                    }

                    break;
                case BeginEndTriangleStrip:
                    for (int i = 0; i + 2 < count; i++)
                    {
                        Triangle(ElementAt(offset, i), ElementAt(offset, i + 1), ElementAt(offset, i + 2));
                    }

                    break;
                case BeginEndTriangleFan:
                    for (int i = 1; i + 1 < count; i++)
                    {
                        Triangle(ElementAt(offset, 0), ElementAt(offset, i), ElementAt(offset, i + 1));
                    }

                    break;
                default:
                    for (int i = 0; i + 3 < count; i += 4)
                    {
                        Triangle(ElementAt(offset, i), ElementAt(offset, i + 1), ElementAt(offset, i + 2));
                        Triangle(ElementAt(offset, i), ElementAt(offset, i + 2), ElementAt(offset, i + 3));
                    }

                    break;
            }
        }

        (float X, float Y, float Z, float R, float G, float B, float A, float U, float V) Vertex(uint element)
        {
            int index = (int)element;
            if (transformed.TryGetValue(index, out (float, float, float, float, float, float, float, float, float) cached))
            {
                return cached;
            }

            for (int slot = 0; slot < VshProgram.RegisterCount; slot++)
            {
                if (mode == VertexFetchMode.Inline)
                {
                    CopyInlineVertex(index, slot, inputs[slot]);
                }
                else
                {
                    FetchVertexAttribute(slot, index, inputs[slot]);
                }

                Array.Clear(outputs[slot]);
            }

            VshProgram.Execute(
                _transformProgram,
                (int)Math.Min(TransformProgramLoadDebug, (uint)VshProgram.MaxInstructions),
                constants,
                inputs,
                outputs);

            (float X, float Y, float Z, float R, float G, float B, float A, float U, float V) vertex = (
                outputs[0][0], outputs[0][1], outputs[0][2],
                outputs[3][0], outputs[3][1], outputs[3][2], outputs[3][3],
                outputs[9][0], outputs[9][1]);
            transformed[index] = vertex;
            return vertex;
        }

        void Triangle(uint a, uint b, uint c)
        {
            (float ax, float ay, float az, float ar, float ag, float ab, float aa, float au, float av) = Vertex(a);
            (float bx, float by, float bz, float br, float bg, float bb, float ba, float bu, float bv) = Vertex(b);
            (float cx, float cy, float cz, float cr, float cg, float cb, float ca, float cu, float cv) = Vertex(c);
            FillTriangle(
                ax, ay, az, ar, ag, ab, aa, au, av,
                bx, by, bz, br, bg, bb, ba, bu, bv,
                cx, cy, cz, cr, cg, cb, ca, cu, cv,
                clipX, clipY, clipW, clipH, bytes, pitch);
        }
    }

    private void CopyInlineVertex(int index, int attribute, float[] destination)
    {
        int offset = (index * InlineVertexWordCount) + (attribute * VertexAttributeComponentCount);
        for (int i = 0; i < VertexAttributeComponentCount; i++)
        {
            destination[i] = _inlineVertices[offset + i];
        }
    }

    private void FetchVertexAttribute(int slot, int index, float[] destination)
    {
        float[] inline = _inlineValues[slot];
        destination[0] = inline[0];
        destination[1] = inline[1];
        destination[2] = inline[2];
        destination[3] = inline[3];

        uint format = _vertexFormat[slot];
        int count = (int)((format >> 4) & 0xFu);
        if (count == 0 || _memory is null)
        {
            return;
        }

        int type = (int)(format & 0xFu);
        int stride = (int)(format >> 8);
        int size = type switch
        {
            0 or 4 => 1,
            1 or 5 => 2,
            _ => 4,
        };
        int step = stride != 0 ? stride : size * count;
        uint address = _vertexArrayOffset[slot] + (uint)(index * step);

        switch (type)
        {
            case 0:
                destination[0] = ReadByte(address + 2) / 255f;
                destination[1] = ReadByte(address + 1) / 255f;
                destination[2] = ReadByte(address) / 255f;
                destination[3] = ReadByte(address + 3) / 255f;
                break;
            case 1:
                for (int i = 0; i < count; i++)
                {
                    destination[i] = Math.Max(-1f, ReadInt16(address + (uint)(i * 2)) / 32767f);
                }

                break;
            case 2:
                for (int i = 0; i < count; i++)
                {
                    destination[i] = BitConverter.Int32BitsToSingle((int)_memory.ReadPhysical(address + (uint)(i * 4)));
                }

                break;
            case 4:
                for (int i = 0; i < count; i++)
                {
                    destination[i] = ReadByte(address + (uint)i) / 255f;
                }

                break;
            case 5:
                for (int i = 0; i < count; i++)
                {
                    destination[i] = ReadInt16(address + (uint)(i * 2));
                }

                break;
            case 6:
                {
                    int packed = (int)_memory.ReadPhysical(address);
                    int x = SignExtend(packed & 0x7FF, 11);
                    int y = SignExtend((packed >> 11) & 0x7FF, 11);
                    int z = SignExtend((packed >> 22) & 0x3FF, 10);
                    destination[0] = Math.Max(-1f, x / 1023f);
                    destination[1] = Math.Max(-1f, y / 1023f);
                    destination[2] = Math.Max(-1f, z / 511f);
                    destination[3] = 1f;
                    break;
                }

            default:
                break;
        }
    }

    private byte ReadByte(uint address)
    {
        return (byte)(_memory!.ReadPhysical(address) & 0xFFu);
    }

    private short ReadInt16(uint address)
    {
        return (short)(_memory!.ReadPhysical(address) & 0xFFFFu);
    }

    private bool TrySampleTexture0(float u, float v, out float r, out float g, out float b, out float a)
    {
        r = 1f;
        g = 1f;
        b = 1f;
        a = 1f;
        if (_memory is null || (_textureControl0[0] & TextureControl0Enable) == 0)
        {
            return false;
        }

        int width = (int)(_textureImageRect[0] >> 16);
        int height = (int)(_textureImageRect[0] & 0xFFFFu);
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        int x = WrapCoordinate((int)MathF.Floor(u * width), width, (int)(_textureAddress[0] & 0xFu));
        int y = WrapCoordinate((int)MathF.Floor(v * height), height, (int)((_textureAddress[0] >> 4) & 0xFu));
        uint color = (_textureFormat[0] >> 8) & 0xFFu;
        uint address = _textureOffset[0];
        uint texel;
        switch (color)
        {
            case 0x06u:
                address += SwizzleOffset(x, y, width, height) * 4u;
                texel = _memory.ReadPhysical(address);
                break;
            case 0x12u:
                {
                    int pitch = (int)(_textureControl1[0] >> 16);
                    if (pitch <= 0)
                    {
                        pitch = width * 4;
                    }

                    address += (uint)((y * pitch) + (x * 4));
                    texel = _memory.ReadPhysical(address);
                    break;
                }
            case 0x05u:
                address += SwizzleOffset(x, y, width, height) * 2u;
                texel = _memory.ReadPhysical(address) & 0xFFFFu;
                r = ((texel >> 11) & 0x1Fu) / 31f;
                g = ((texel >> 5) & 0x3Fu) / 63f;
                b = (texel & 0x1Fu) / 31f;
                a = 1f;
                return true;
            default:
                return false;
        }

        a = ((texel >> 24) & 0xFFu) / 255f;
        r = ((texel >> 16) & 0xFFu) / 255f;
        g = ((texel >> 8) & 0xFFu) / 255f;
        b = (texel & 0xFFu) / 255f;
        return true;
    }

    private static int WrapCoordinate(int value, int size, int mode)
    {
        switch (mode)
        {
            case 0:
                value %= size;
                return value < 0 ? value + size : value;
            case 1:
                {
                    int period = size * 2;
                    value %= period;
                    if (value < 0)
                    {
                        value += period;
                    }

                    return value < size ? value : period - 1 - value;
                }
            default:
                return Math.Clamp(value, 0, size - 1);
        }
    }

    private static uint SwizzleOffset(int x, int y, int width, int height)
    {
        uint maskX = 0u;
        uint maskY = 0u;
        uint bit = 1u;
        uint maskBit = 1u;
        bool done;
        do
        {
            done = true;
            if (bit < (uint)width)
            {
                maskX |= maskBit;
                maskBit <<= 1;
                done = false;
            }

            if (bit < (uint)height)
            {
                maskY |= maskBit;
                maskBit <<= 1;
                done = false;
            }

            bit <<= 1;
        }
        while (!done);

        return InterleaveBits((uint)x, maskX) | InterleaveBits((uint)y, maskY);
    }

    private static uint InterleaveBits(uint value, uint mask)
    {
        uint result = 0u;
        uint maskBit = 1u;
        int valueBit = 0;
        while (maskBit != 0u)
        {
            if ((mask & maskBit) != 0u)
            {
                if ((value & (1u << valueBit)) != 0u)
                {
                    result |= maskBit;
                }

                valueBit++;
            }

            maskBit <<= 1;
        }

        return result;
    }

    private static int SignExtend(int value, int bits)
    {
        int sign = 1 << (bits - 1);
        return (value & sign) != 0 ? value | ~((sign << 1) - 1) : value;
    }

    private uint ZetaBytesPerPixel()
    {
        if (((_surfaceFormat & 0xF00u) >> 8) != SurfaceTypePitch)
        {
            return 0;
        }

        if (((_surfacePitch >> 16) & 0xFFFFu) == 0)
        {
            return 0;
        }

        return ((_surfaceFormat >> 4) & 0xFu) switch
        {
            ZetaFormatZ16 => 2u,
            ZetaFormatZ24S8 => 4u,
            _ => 0u,
        };
    }

    private uint ReadHalf(uint address)
    {
        uint word = _memory!.ReadPhysical(address & ~3u);
        return ((address & 2u) == 0) ? word & 0xFFFFu : word >> 16;
    }

    private void CapturePresentedFrame()
    {
        _presentedFrame = FramesPresented;
        uint bytes = ColorBytesPerPixel(_surfaceFormat & 0xFu);
        uint width = _surfaceClipH >> 16;
        uint height = _surfaceClipV >> 16;
        uint pitch = _surfacePitch & 0xFFFFu;
        bool pitchType = ((_surfaceFormat & 0xF00u) >> 8) == SurfaceTypePitch;
        _presentedValid = (bytes == 2 || bytes == 4) &&
            width != 0 && width <= MaxPresentDimension &&
            height != 0 && height <= MaxPresentDimension &&
            pitch != 0 && pitchType && _memory is not null;
        if (_presentedValid)
        {
            _presentedOffset = PresentedColorOffset;
            _presentedWidth = width;
            _presentedHeight = height;
            _presentedPitch = pitch;
            _presentedBytes = bytes;
        }
    }

    /// <summary>Copies the latest presented frame as BGRA bytes (one byte per channel).</summary>
    /// <returns>False when no valid presented frame is available.</returns>
    public bool TryCopyPresentedFrame(out uint width, out uint height, out byte[] bgra)
    {
        width = 0;
        height = 0;
        bgra = Array.Empty<byte>();
        if (!_presentedValid || _memory is null)
        {
            return false;
        }

        ulong count = (ulong)_presentedWidth * _presentedHeight;
        if (count == 0 || count > MaxPresentPixels)
        {
            return false;
        }

        byte[] pixels = new byte[count * 4];
        for (uint y = 0; y < _presentedHeight; y++)
        {
            uint row = _presentedOffset + (y * _presentedPitch);
            for (uint x = 0; x < _presentedWidth; x++)
            {
                uint address = row + (x * _presentedBytes);
                if (address + _presentedBytes > RamSize)
                {
                    return false;
                }

                uint red;
                uint green;
                uint blue;
                uint alpha;
                if (_presentedBytes == 4)
                {
                    uint pixel = _memory.ReadPhysical(address);
                    blue = pixel & 0xFFu;
                    green = (pixel >> 8) & 0xFFu;
                    red = (pixel >> 16) & 0xFFu;
                    alpha = (pixel >> 24) & 0xFFu;
                }
                else
                {
                    uint half = ReadHalf(address);
                    red = (((half >> 11) & 0x1Fu) * 255u) / 31u;
                    green = (((half >> 5) & 0x3Fu) * 255u) / 63u;
                    blue = (half & 0x1Fu) * 255u / 31u;
                    alpha = 255u;
                }

                int at = (int)(((ulong)y * _presentedWidth + x) * 4);
                pixels[at] = (byte)blue;
                pixels[at + 1] = (byte)green;
                pixels[at + 2] = (byte)red;
                pixels[at + 3] = (byte)alpha;
            }
        }

        width = _presentedWidth;
        height = _presentedHeight;
        bgra = pixels;
        return true;
    }

    private void WriteZeta(uint address, uint bytes, bool writeDepth, float depth, bool writeStencil, uint stencil)
    {
        if (_memory is null || address + bytes > RamSize)
        {
            return;
        }

        if (bytes == 4)
        {
            uint current = _memory.ReadPhysical(address);
            uint depthBits = writeDepth
                ? (uint)(Math.Clamp(depth, 0f, 1f) * 16777215f + 0.5f)
                : (current >> 8) & 0xFFFFFFu;
            uint writeMask = _stencilWriteMask & StencilByteMask;
            uint newStencil = writeStencil
                ? ((current & StencilByteMask) & ~writeMask) | (stencil & writeMask)
                : current & StencilByteMask;
            _memory.WritePhysical(address, (depthBits << 8) | newStencil);
            return;
        }

        uint wordAddress = address & ~3u;
        int shift = (int)((address & 2u) * 8);
        uint currentWord = _memory.ReadPhysical(wordAddress);
        uint half = writeDepth
            ? (uint)(Math.Clamp(depth, 0f, 1f) * 65535f + 0.5f)
            : (currentWord >> shift) & 0xFFFFu;
        uint halfMask = 0xFFFFu << shift;
        _memory.WritePhysical(wordAddress, (currentWord & ~halfMask) | ((half << shift) & halfMask));
    }

    private bool TryReadPixelFloat(uint address, uint bytes, out float r, out float g, out float b, out float a)
    {
        r = 0f;
        g = 0f;
        b = 0f;
        a = 1f;
        if (_memory is null || address + bytes > RamSize)
        {
            return false;
        }

        if (bytes == 4)
        {
            uint pixel = _memory.ReadPhysical(address);
            a = ((pixel >> 24) & 0xFFu) / 255f;
            r = ((pixel >> 16) & 0xFFu) / 255f;
            g = ((pixel >> 8) & 0xFFu) / 255f;
            b = (pixel & 0xFFu) / 255f;
            return true;
        }

        uint word = _memory.ReadPhysical(address & ~3u);
        uint half = ((address & 2u) == 0) ? word & 0xFFFFu : word >> 16;
        r = ((half >> 11) & 0x1Fu) / 31f;
        g = ((half >> 5) & 0x3Fu) / 63f;
        b = (half & 0x1Fu) / 31f;
        return true;
    }

    private void ProcessFragment(uint x, uint y, uint bytes, uint pitch,
        float r, float g, float b, float a, float depth,
        uint zetaBytes, uint zetaPitch)
    {
        if (_memory is null)
        {
            return;
        }

        if (_alphaTestEnable && !CompareFunc(_alphaFunc, a, _alphaRef / 255f))
        {
            return;
        }

        bool colorWrite = (_colorMask & ColorMaskAll) != 0;
        if (!colorWrite && !_depthTestEnable && !_stencilTestEnable)
        {
            return;
        }

        bool stencilActive = _stencilTestEnable && zetaBytes == 4;
        bool depthActive = _depthTestEnable && zetaBytes != 0;
        uint zetaAddress = 0;
        uint storedZeta = 0;
        bool haveZeta = false;
        if ((stencilActive || depthActive) && zetaBytes != 0)
        {
            zetaAddress = _surfaceZetaOffset + (y * zetaPitch) + (x * zetaBytes);
            if (zetaAddress + zetaBytes <= RamSize)
            {
                storedZeta = zetaBytes == 4 ? _memory.ReadPhysical(zetaAddress) : ReadHalf(zetaAddress);
                haveZeta = true;
            }
        }

        stencilActive &= haveZeta;
        depthActive &= haveZeta;

        float storedDepth = 0f;
        uint stencil = 0u;
        if (haveZeta)
        {
            if (zetaBytes == 4)
            {
                storedDepth = ((storedZeta >> 8) & 0xFFFFFFu) / 16777215f;
                stencil = storedZeta & StencilByteMask;
            }
            else
            {
                storedDepth = (storedZeta & 0xFFFFu) / 65535f;
            }
        }

        uint stencilRef = _stencilRef & StencilByteMask;
        if (stencilActive)
        {
            uint readMask = _stencilReadMask & StencilByteMask;
            if (!CompareFunc(_stencilFunc, (stencil & readMask) / 255f, (stencilRef & readMask) / 255f))
            {
                WriteZeta(zetaAddress, zetaBytes, false, 0f, true, ApplyStencilOp(_stencilOpFail, stencil, stencilRef));
                return;
            }
        }

        float clampedDepth = Math.Clamp(depth, 0f, 1f);
        bool depthPass = true;
        if (depthActive)
        {
            DepthTestedPixels++;
            depthPass = CompareFunc(_depthFunc, clampedDepth, storedDepth);
            if (!depthPass)
            {
                if (stencilActive)
                {
                    WriteZeta(zetaAddress, zetaBytes, false, 0f, true,
                        ApplyStencilOp(_stencilOpZfail, stencil, stencilRef));
                }

                return;
            }
        }

        bool writeStencil = stencilActive;
        uint stencilValue = stencilActive ? ApplyStencilOp(_stencilOpZpass, stencil, stencilRef) : 0u;
        bool writeDepth = depthActive && _depthMask;
        if (writeStencil || writeDepth)
        {
            WriteZeta(zetaAddress, zetaBytes, writeDepth, clampedDepth, writeStencil, stencilValue);
        }

        if (colorWrite)
        {
            uint address = PresentedColorOffset + (y * pitch) + (x * bytes);
            (float R, float G, float B, float A) src = (r, g, b, a);
            (float R, float G, float B, float A) result = src;
            bool needDst = _blendEnable || (_colorMask & ColorMaskAll) != ColorMaskAll;
            (float R, float G, float B, float A) dst = (0f, 0f, 0f, 1f);
            if (needDst &&
                TryReadPixelFloat(address, bytes, out float dr, out float dg, out float db, out float da))
            {
                dst = (dr, dg, db, da);
            }

            if (_blendEnable)
            {
                uint blend = _blendColor;
                (float R, float G, float B, float A) constant = (
                    ((blend >> 16) & 0xFFu) / 255f,
                    ((blend >> 8) & 0xFFu) / 255f,
                    (blend & 0xFFu) / 255f,
                    ((blend >> 24) & 0xFFu) / 255f);
                (float R, float G, float B, float A) fs = BlendFactor(_blendSrcFactor, src, dst, constant);
                (float R, float G, float B, float A) fd = BlendFactor(_blendDstFactor, src, dst, constant);
                result = _blendEquation switch
                {
                    0u => (src.R * fs.R - dst.R * fd.R, src.G * fs.G - dst.G * fd.G,
                        src.B * fs.B - dst.B * fd.B, src.A * fs.A - dst.A * fd.A),
                    1u or 5u => (dst.R * fd.R - src.R * fs.R, dst.G * fd.G - src.G * fs.G,
                        dst.B * fd.B - src.B * fs.B, dst.A * fd.A - src.A * fs.A),
                    3u => (Math.Min(src.R, dst.R), Math.Min(src.G, dst.G),
                        Math.Min(src.B, dst.B), Math.Min(src.A, dst.A)),
                    4u => (Math.Max(src.R, dst.R), Math.Max(src.G, dst.G),
                        Math.Max(src.B, dst.B), Math.Max(src.A, dst.A)),
                    _ => (src.R * fs.R + dst.R * fd.R, src.G * fs.G + dst.G * fd.G,
                        src.B * fs.B + dst.B * fd.B, src.A * fs.A + dst.A * fd.A),
                };
                result = (
                    Math.Clamp(result.R, 0f, 1f),
                    Math.Clamp(result.G, 0f, 1f),
                    Math.Clamp(result.B, 0f, 1f),
                    Math.Clamp(result.A, 0f, 1f));
                BlendedPixels++;
            }

            uint mask = _colorMask;
            float fr = (mask & (1u << 16)) != 0 ? result.R : dst.R;
            float fg = (mask & (1u << 8)) != 0 ? result.G : dst.G;
            float fb = (mask & 1u) != 0 ? result.B : dst.B;
            float fa = (mask & (1u << 24)) != 0 ? result.A : dst.A;
            WritePixel(address, bytes, PackPixel(bytes, fr, fg, fb, fa));
            DrawnPixels++;
        }
    }

    private void FillTriangle(
        float ax, float ay, float az, float ar, float ag, float ab, float aa, float au, float av,
        float bx, float by, float bz, float br, float bg, float bb, float ba, float bu, float bv,
        float cx, float cy, float cz, float cr, float cg, float cb, float ca, float cu, float cv,
        uint clipX, uint clipY, uint clipW, uint clipH, uint bytes, uint pitch)
    {
        float area = ((bx - ax) * (cy - ay)) - ((cx - ax) * (by - ay));
        if (area == 0f)
        {
            return;
        }

        uint zetaBytes = ZetaBytesPerPixel();
        uint zetaPitch = (_surfacePitch >> 16) & 0xFFFFu;

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
                float depth = (w1 * az) + (w2 * bz) + (w0 * cz);
                float tu = (w1 * au) + (w2 * bu) + (w0 * cu);
                float tv = (w1 * av) + (w2 * bv) + (w0 * cv);
                if (TrySampleTexture0(tu, tv, out float tr, out float tg, out float tb, out float ta))
                {
                    r *= tr;
                    g *= tg;
                    bl *= tb;
                    al *= ta;
                    TexturedPixels++;
                }

                ProcessFragment(x, y, bytes, pitch, r, g, bl, al, depth, zetaBytes, zetaPitch);
            }
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
