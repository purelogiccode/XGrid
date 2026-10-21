using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using XGrid.Core.Devices;
using XGrid.Core.Snapshots;
using XGrid.Memory;

namespace XGrid.Nv2a;

/// <summary>
/// NV2A GPU control register window: 16 MB at 0xFD000000 (BAR0).
/// Block map and reset defaults follow xemu <c>hw/xbox/nv2a/</c> (blocktable in
/// <c>nv2a.c</c>, per-block read/write in <c>pmc.c</c>/<c>pbus.c</c>/<c>pfb.c</c>/
/// <c>pcrtc.c</c>/<c>pramdac.c</c>/<c>ptimer.c</c>/<c>user.c</c>, pushbuffer parsing in
/// <c>pfifo.c</c>, DMA object layout in <c>nv2a.c</c>); PFB CFG0 reset default 0x03070103
/// is the 64 MB hardware value recorded in the XboxDevWiki/xemu notes. The PFIFO pusher runs
/// synchronously on submission (no thread) and dispatches PGRAPH methods to a
/// recording sink; rendering and interrupt assertion through the PIC are deferred:
/// pending/enabled state is tracked but no IRQ is raised yet.
/// </summary>
public sealed class Nv2AMmio : IMmioDevice, IXboxDevice, ISnapshotable
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
    private const uint PgraphColorClearValue = 0x186Cu;
    private const uint PgraphZStencilClearValue = 0x1A88u;
    private const uint MethodClearSurface = 0x1D94u;
    private const uint MethodClearRectH = 0x1D98u;
    private const uint MethodClearRectV = 0x1D9Cu;
    private const uint MethodContextDmaZeta = 0x198u;
    private const uint MethodContextDmaColor = 0x194u;
    private const uint MethodAlphaTestEnable = 0x300u;
    private const uint MethodBlendEnable = 0x304u;
    private const uint MethodCullFaceEnable = 0x308u;
    private const uint MethodCullFace = 0x39Cu;
    private const uint MethodFrontFace = 0x3A0u;
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
    private const uint MethodLightControl = 0x0294u;
    private const uint MethodColorMaterial = 0x0298u;
    private const uint MethodMaterialEmission = 0x03A8u;
    private const uint MethodMaterialEmissionEnd = 0x03B0u;
    private const uint MethodMaterialAlpha = 0x03B4u;
    private const uint MethodSceneAmbient = 0x0A10u;
    private const uint MethodSceneAmbientEnd = 0x0A18u;
    private const uint MethodLightingEnable = 0x0314u;
    private const uint MethodSkinMode = 0x0328u;
    private const uint MethodNormalizationEnable = 0x03A4u;
    private const uint MethodSpecularEnable = 0x03B8u;
    private const uint MethodLightEnableMask = 0x03BCu;
    private const uint MethodTexGenBase = 0x03C0u;
    private const uint MethodTexGenEnd = 0x03CCu;
    private const uint TexGenStageStride = 0x10u;
    private const uint MethodTextureMatrixEnable = 0x0420u;
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
    private const uint MethodCombinerAlphaIcwBase = 0x260u;
    private const uint MethodCombinerAlphaIcwEnd = 0x27Cu;
    private const uint MethodCombinerSpecFog0 = 0x288u;
    private const uint MethodCombinerSpecFog1 = 0x28Cu;
    private const uint MethodCombinerFactor0Base = 0x0A60u;
    private const uint MethodCombinerFactor0End = 0x0A7Cu;
    private const uint MethodCombinerFactor1Base = 0x0A80u;
    private const uint MethodCombinerFactor1End = 0x0A9Cu;
    private const uint MethodCombinerAlphaOcwBase = 0x0AA0u;
    private const uint MethodCombinerAlphaOcwEnd = 0x0ABCu;
    private const uint MethodCombinerColorIcwBase = 0x0AC0u;
    private const uint MethodCombinerColorIcwEnd = 0x0ADCu;
    private const uint MethodSpecularFogFactorBase = 0x1E20u;
    private const uint MethodSpecularFogFactorEnd = 0x1E24u;
    private const uint MethodCombinerColorOcwBase = 0x1E40u;
    private const uint MethodCombinerColorOcwEnd = 0x1E5Cu;
    private const uint MethodCombinerControl = 0x1E60u;
    private const uint MethodShaderStageProgram = 0x1E70u;
    private const uint MethodDotRgbMapping = 0x1E74u;
    private const uint MethodShaderOtherStageInput = 0x1E78u;
    private const uint MethodFogMode = 0x29Cu;
    private const uint MethodFogGenMode = 0x2A0u;
    private const uint MethodFogEnable = 0x2A4u;
    private const uint MethodFogColor = 0x2A8u;
    private const uint MethodFogCoord = 0x1698u;
    private const int VertexAttributeFogCoord = 5;
    private const uint MethodFogParamsBase = 0x9C0u;
    private const uint MethodFogParamsEnd = 0x9C8u;
    private const uint MethodTextureBase = 0x1B00u;
    private const uint MethodTextureEnd = 0x1BFCu;
    private const uint TextureSlotStride = 0x40u;
    private const uint TextureControl0Enable = 1u << 30;
    private const uint TextureFormatCubemapEnable = 1u << 2;
    private const uint TextureFormatDimensionality = 0xC0u;
    private const uint TextureFormatMipmapLevels = 0x000F0000u;
    private const uint TextureFormatBaseSizeP = 0xF0000000u;
    private const uint MethodShaderClipPlaneMode = 0x17F8u;
    private const uint MethodShadowDepthFunc = 0x1E6Cu;
    private const uint ShadowDepthFuncNever = 0u;
    private const uint ShadowDepthFuncAlways = 7u;
    private const uint TextureAddressU = 0x7u;
    private const uint TextureAddressV = 0x700u;
    private const uint TextureAddressP = 0x70000u;
    private const uint TextureAddressWrap = 1u;
    private const uint TextureAddressMirror = 2u;
    private const uint TextureAddressClampToEdge = 3u;
    private const uint TextureAddressBorder = 4u;
    private const uint TextureFilterMin = 0x003F0000u;
    private const uint TextureFilterMag = 0x0F000000u;
    private const uint TextureFilterKernel = 0x0000E000u;
    private const uint TextureFilterMinConvolution2DLod0 = 7u;
    private const uint TextureFilterKernelQuincunx = 1u;
    private const uint TextureFilterKernelGaussian3 = 2u;
    private const uint TextureBumpMatBase = 0x28u;
    private const int MaxTextureSlots = 4;



    private const int MaxTransformProgramLength = 136;
    private const int MaxVertexShaderConstants = 192;
    private const uint VertexArrayOffsetMask = 0x7FFFFFFFu;
    private const uint TransformProgramStartMask = 0xFFu;
    private const uint TransformExecutionModeMask = 3u;
    private const uint TransformModeProgrammable = 2u;
    private const uint TransformModeFixed = 0u;
    private const uint SkinModeMask = 7u;
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
    private const float ZetaDepthRangeZ16 = 65535f;
    private const float ZetaDepthRangeZ24S8 = 16777215f;
    private const float ZetaDepthRangeF16 = 511.9375f;
    private const float ZetaDepthRangeF24 = 1.0E30f;
    private const uint MethodControl0 = 0x290u;
    private const uint Control0ZFormat = 1u << 12;
    private const uint MethodClipMin = 0x394u;
    private const uint MethodClipMax = 0x398u;
    private const uint MethodZminMaxControl = 0x1D78u;
    private const uint ZminMaxZclampEnMask = 0xF0u;
    private const uint ZminMaxZclampEnClamp = 0x10u;
    private const uint ZetaFixedMax16 = 0xFFFFu;
    private const uint ZetaFixedMax24 = 0xFFFFFFu;
    private const uint ZetaF16Bias = 0x3C000000u;
    private const uint ZetaF16Max = 0xFFF8u;
    private const uint CompareLess = 1u;
    private const uint CompareAlways = 7u;

    // NV097_SET_CULL_FACE_V_* and NV097_SET_FRONT_FACE_V_* (xemu nv2a_regs.h).
    private const uint CullFrontFace = 0u;
    private const uint CullBackFace = 1u;
    private const uint CullFrontAndBackFace = 2u;
    private const uint FrontFaceValueCw = 0x0900u;
    private const uint FrontFaceValueCcw = 0x0901u;
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

    /// <summary>Optional O2 fast lane for per-pixel RAM traffic (null for test fakes).</summary>
    private readonly IGpuMemoryFast? _memoryFast;
    // Diagnostic (`--gpu-time`): distinct combiner-configuration histogram.
    private string _diagCombinerConfig = "none";
    private int _combinerFastKind;
    private readonly long[] _diagFastKindFills = new long[4];
    private long _diagLeanFills;
    // Per-fill fragment constants (set by PrepareFragmentConstants at the start
    // of every fill; no guest register write can interleave with a fill).
    private float _alphaRefScaled;
    private float _blendConstR;
    private float _blendConstG;
    private float _blendConstB;
    private float _blendConstA;
    private bool _blendFastSrcAlpha;
    private float _zclipRange;
    // Reused PSH fill scratch (bit-identical to the previous per-triangle
    // allocations; fully re-initialized at the start of every fill).
    private readonly float[] _pshDiffuse = new float[4];
    private readonly float[] _pshSpecular = new float[4];
    private readonly float[] _pshFog = new float[4];
    private readonly float[][] _pshTexels = { new float[4], new float[4], new float[4], new float[4] };
    private readonly float[][] _pshCoords = { new float[4], new float[4], new float[4], new float[4] };
    private readonly float[] _pshStageDots = new float[PshCombiner.TextureSlots];
    private readonly int[] _pshTexModes = new int[PshCombiner.TextureSlots];
    // Per-draw sampler plans (rebuilt in EnterTextureState for both draw paths).
    private readonly SamplerPlan[] _samplerPlans = new SamplerPlan[MaxTextureSlots];

    /// <summary>Gets the per-configuration fill/pixel counts (`--gpu-time` diagnostic).</summary>
    public Dictionary<string, (long Draws, long Pixels)> CombinerConfigStats { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Diagnostic switch for differential tests: forces the generic per-sample
    /// sampler path (and with <see cref="DiagnosticGenericFill"/> the generic
    /// pixel loop), so the specialized paths can be compared against the
    /// reference path on an identical draw. Never set in production runs.
    /// </summary>
    public bool DiagnosticGenericSampler { get; set; }

    /// <summary>Diagnostic switch for differential tests: disables the compiled modulate fill.</summary>
    public bool DiagnosticGenericFill { get; set; }

    /// <summary>Gets the per-kind fast-path fill counts. Diagnostics.</summary>
    public string FastKindDebug
    {
        get
        {
            string text =
                $"k1={_diagFastKindFills[1]} k2={_diagFastKindFills[2]} k3={_diagFastKindFills[3]} lean={_diagLeanFills} " +
                $"compiled={_diagCompiledFills}/{_diagCompiledPixels}/{_rasterCompiledTicks / (double)Stopwatch.Frequency:F2}s";
            if (_diagRejects.Count > 0)
            {
                var list = new List<KeyValuePair<int, long>>(_diagRejects);
                list.Sort((a, b) => b.Value.CompareTo(a.Value));
                text += " rej=";
                for (int i = 0; i < list.Count && i < 6; i++)
                {
                    text += $"{list[i].Key:X}:{list[i].Value},";
                }
            }


            return text;
        }
    }

    private readonly Dictionary<int, long> _diagRejects = new();

    private readonly float[] _lightTexels = new float[9];

    private long _diagCompiledFills;
    private long _diagCompiledPixels;
    private long _rasterCompiledTicks;
    private long _rasterLegacyTicks;
    private long _rasterPshTicks;
    private long _rasterLegacyCalls;
    private long _rasterPshCalls;
    private readonly uint[] _boundEngine = new uint[8];
    private readonly uint[] _boundInstance = new uint[8];
    private readonly uint[] _pfifo = new uint[PfifoSize / 4];
    private readonly uint[] _pvideo = new uint[0x1000 / 4];
    private readonly uint[] _pfb = new uint[0x1000 / 4];
    private readonly uint[] _pgraph = new uint[PgraphSize / 4];
    private readonly byte[] _ramin = new byte[PraminSize];
    private readonly uint[] _prmcio = new uint[0x1000 / 4];

    // VGA register file behind the NV_PRMCIO/NV_PRMVIO windows. The guest
    // programs the display mode here (xemu forwards these MMIO offsets to
    // vga_ioport_write), and the CRTC geometry is what the display controller
    // actually scans out - see TryGetDisplayGeometry.
    private readonly byte[] _vgaCrtc = new byte[256];
    private readonly byte[] _vgaSequencer = new byte[8];
    private readonly byte[] _vgaGraphics = new byte[16];
    private readonly byte[] _vgaAttribute = new byte[21];
    private byte _vgaCrtcIndex;
    private byte _vgaSequencerIndex;
    private byte _vgaGraphicsIndex;
    private byte _vgaAttributeIndex;
    private bool _vgaAttributeFlipFlop;
    private bool _vgaCrtcWritten;
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
    private readonly FixedFunctionVertexState _fixedFunction = new();
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
    private readonly uint[] _textureBorderColor = new uint[MaxTextureSlots];
    private uint _shaderClipMode;
    private uint _shadowDepthFunc;
    private readonly float[][] _bumpMatrices = new float[MaxTextureSlots - 1][];
    private readonly float[] _bumpScales = new float[MaxTextureSlots - 1];
    private readonly float[] _bumpOffsets = new float[MaxTextureSlots - 1];
    private bool _fogEnable;
    private int _fogMode;
    private uint _fogGenMode;
    private readonly float[] _fogColor = new float[4];
    private readonly float[] _fogParams = new float[3];
    private readonly PshCombiner _combiner = new();
    private static readonly float[] CombinerFogConst = new[] { 0f, 0f, 0f, 1f };
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
    private bool _cullFaceEnable;
    private uint _cullFace;
    private bool _frontFaceCw = true;
    private bool _zFormatFloat;
    private float _zclipMin;
    private float _zclipMax;
    private bool _zclipActive;
    private bool _zclipCull;
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
    private uint _dmaColor;
    private bool _waitingForNop;
    private bool _pusherKickPending;
    private uint _surfaceChannel;
    private uint _colorSurfaceBase;
    private uint _zetaSurfaceBase;
    private uint _beginMode;
    private readonly Dictionary<uint, int> _methodCounts = new();
    private readonly Dictionary<uint, uint> _methodParams = new();
    private readonly List<(uint Subchannel, uint Method, uint Parameter)> _methodStream = new();
    private readonly long[] _textureFormatAttempts = new long[128];
    private readonly long[] _textureFormatMisses = new long[128];
    private readonly long[] _texModeFallbackByMode = new long[0x20];
    private long _combinerRgbZeroFragments;
    private long _combinerRgbZeroAlphaFragments;
    private const int TextureStateCap = 512;
    private readonly Dictionary<string, int> _textureStateIndex = new(StringComparer.Ordinal);
    private readonly List<string> _textureStateKeys = new();
    private readonly long[] _textureStateSamples = new long[TextureStateCap];
    private readonly long[] _textureStateRgbZero = new long[TextureStateCap];
    private readonly long[] _textureStateFallbacks = new long[TextureStateCap];
    private readonly long[] _textureStateMisses = new long[TextureStateCap];
    private readonly uint[] _textureStateFirstAddress = new uint[TextureStateCap];
    private readonly uint[] _textureStateFirstTexel = new uint[TextureStateCap];
    private readonly float[] _textureStateMinU = new float[TextureStateCap];
    private readonly float[] _textureStateMaxU = new float[TextureStateCap];
    private readonly float[] _textureStateMinV = new float[TextureStateCap];
    private readonly float[] _textureStateMaxV = new float[TextureStateCap];
    private int _currentTextureState;

    /// <summary>Creates the NV2A register window for the given RAM size in bytes.</summary>
    public Nv2AMmio(uint ramSize = 64u * 1024 * 1024, string name = "NV2A", IGpuMemory? memory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        RamSize = ramSize;
        Name = name;
        _memory = memory;
        _memoryFast = memory as IGpuMemoryFast;
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

        for (int i = 0; i < _bumpMatrices.Length; i++)
        {
            _bumpMatrices[i] = new float[4];
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

    /// <summary>
    /// Optional GPU-backend seam (slice 1). When null (the default and the
    /// <c>--gpu sw</c> path) behavior is byte-identical to the un-seamed
    /// software path; when attached the software path still executes
    /// unconditionally and the backend observes the same resolved state
    /// synchronously at the flush sites.
    /// </summary>
    public IPgraphBackend? Backend
    {
        get => _backend;
        set
        {
            _backend = value;
            _observeMethods = value is { ObserveMethods: true };
        }
    }

    private IPgraphBackend? _backend;
    private bool _observeMethods;

    /// <summary>Gets the total number of PGRAPH method triples observed (diagnostics for every backend).</summary>
    public long MethodTotal { get; private set; }

    /// <summary>
    /// Gets or sets whether the per-method histogram (<see cref="MethodCounts"/>
    /// / <see cref="MethodParams"/>) is collected. Two dictionary updates per
    /// PGRAPH method cost wall time on method-heavy workloads (18.1M methods
    /// per 1B menu steps); the CLI turns it on only when a method dump is
    /// requested (<c>--pgraph</c>). The step/method totals and the method
    /// stream are unaffected.
    /// </summary>
    public bool CollectMethodHistogram { get; set; } = true;

    /// <summary>Gets the number of executed surface clears. Diagnostics.</summary>
    public uint ClearsExecuted { get; private set; }

    /// <summary>Gets the number of executed draws. Diagnostics.</summary>
    public ulong DrawsExecuted { get; private set; }

    /// <summary>Gets the number of draws that ran the register-combiner stage. Diagnostics.</summary>
    public ulong CombinerDraws { get; private set; }

    /// <summary>Gets the number of triangles discarded by the cull-face test. Diagnostics.</summary>
    public long TrianglesCulled { get; private set; }

    /// <summary>
    /// When set, both software fill paths accumulate host wall time per draw
    /// for the <c>--gpu-time</c> diagnostic. Host-only: never snapshotted,
    /// never affects guest state, and the pixel math is untouched.
    /// </summary>
    public bool RasterTimingEnabled { get; set; }

    /// <summary>Gets the number of timed legacy fill-path invocations. Diagnostics.</summary>
    public long RasterLegacyCalls => _rasterLegacyCalls;

    /// <summary>Gets the number of timed combiner fill-path invocations. Diagnostics.</summary>
    public long RasterPshCalls => _rasterPshCalls;

    /// <summary>Gets the accumulated legacy fill-path wall time (seconds). Diagnostics.</summary>
    public double RasterLegacySeconds => _rasterLegacyTicks / (double)Stopwatch.Frequency;

    /// <summary>Gets the accumulated combiner fill-path wall time (seconds). Diagnostics.</summary>
    public double RasterPshSeconds => _rasterPshTicks / (double)Stopwatch.Frequency;

    /// <summary>Gets the register-combiner state summary. Diagnostics.</summary>
    public string CombinerStateDebug =>
        $"stages={_combiner.StageCount} control={_combiner.Control:X8} " +
        $"final={_combiner.FinalEnabled} draws={CombinerDraws} " +
        $"texmodes={_combiner.DecodeTexMode(0):X}{_combiner.DecodeTexMode(1):X}" +
        $"{_combiner.DecodeTexMode(2):X}{_combiner.DecodeTexMode(3):X}";

    /// <summary>Gets the fog state summary (enable, mode, color, params). Diagnostics.</summary>
    public string FogStateDebug =>
        $"enable={_fogEnable} mode={_fogMode} gen={_fogGenMode:X} " +
        $"color={_fogColor[0]:F3},{_fogColor[1]:F3},{_fogColor[2]:F3},{_fogColor[3]:F3} " +
        $"params={_fogParams[0]:G},{_fogParams[1]:G},{_fogParams[2]:G}";

    /// <summary>Gets the number of texels that fell back to NONE for an unimplemented mode or stage. Diagnostics.</summary>
    public ulong TexModeFallbacks { get; private set; }

    /// <summary>Gets the number of texels that fell back to NONE per texture mode. Diagnostics.</summary>
    public IReadOnlyDictionary<int, long> TexModeFallbacksByMode => SnapshotByIndex(_texModeFallbackByMode);

    /// <summary>Gets the number of dot-map applications that fell back to identity (HILO family). Diagnostics.</summary>
    public ulong UnsupportedDotMaps { get; private set; }

    /// <summary>Gets the number of successful texture samples. Diagnostics.</summary>
    public ulong TextureSampleSuccesses { get; private set; }

    /// <summary>Gets the number of samples skipped because the texture stage was disabled. Diagnostics.</summary>
    public ulong TextureSampleDisabled { get; private set; }

    /// <summary>Gets the number of samples skipped because the texture size was zero. Diagnostics.</summary>
    public ulong TextureSampleBadSize { get; private set; }

    /// <summary>Gets the number of successful samples whose RGB decoded to zero. Diagnostics.</summary>
    public ulong TextureSampleRgbZero { get; private set; }

    /// <summary>
    /// Gets per-color-format texture sample attempts. Diagnostics.
    /// </summary>
    public IReadOnlyDictionary<uint, long> TextureFormatAttempts => Snapshot(_textureFormatAttempts);

    /// <summary>
    /// Gets or sets whether the sampler honors TEXFILTER (a tent filter at
    /// LOD 0 when MIN/MAG ask for one, and the 3x3 gaussian convolution on
    /// PROJECT2D when MIN selects CONVOLUTION_2D_LOD0). The sampler has always
    /// been nearest-only, so this is a behavior change and stays off unless a
    /// caller opts in; the corpus oracle for it is xgu bump_cubemap_reflect
    /// against the reference screenshot.
    /// </summary>
    public bool TextureFilterRegisters { get; set; }

    /// <summary>
    /// Gets or sets whether transform execution mode FIXED (0) runs the
    /// fixed-function vertex stage (<see cref="FixedFunctionVertex"/>) instead
    /// of the last-loaded vertex program.
    /// </summary>
    /// <remarks>
    /// Opt-in: mode-0 draws used to run whatever vertex program happened to be
    /// resident, so switching them to the fixed-function stage changes the
    /// picture on titles that mix both paths (the EvoX dashboard issues 22,207
    /// fixed-function draws per 1B steps). The stage itself is a transcription
    /// of the reference (<c>vsh-ff.c</c>); it is gated because no reference
    /// frame exists for those dashboard draws, so the default path has to stay
    /// byte-identical for the gate anchors.
    /// </remarks>
    public bool FixedFunctionTransform { get; set; }

    /// <summary>
    /// Gets the four TEXFILTER words as the sampler reads them (MIN in bits
    /// 16-21, MAG in bits 24-27, the convolution kernel in bits 13-15; 1 =
    /// nearest, 2 = linear, MIN 7 = CONVOLUTION_2D_LOD0). The kernel field is
    /// printed as the *effective* kernel - zero unless MIN selects the
    /// convolution, which is the only case the reference applies it in - so a
    /// stage that programs kernel bits next to an ordinary tent filter reads
    /// <c>2/2/0</c> and is sampled as a plain tent.
    /// </summary>
    public string TextureFilterDebug
    {
        get
        {
            var builder = new System.Text.StringBuilder(40);
            for (int slot = 0; slot < MaxTextureSlots; slot++)
            {
                if (slot != 0)
                {
                    builder.Append(',');
                }

                uint word = _textureFilter[slot];
                uint min = (word & TextureFilterMin) >> 16;
                uint mag = (word & TextureFilterMag) >> 24;
                int conv = EffectiveConvolutionKernel(word);
                builder.Append(CultureInfo.InvariantCulture, $"{min}/{mag}/{conv}");
            }

            return builder.ToString();
        }
    }

    /// <summary>Gets per-color-format texture sample misses (unsupported format). Diagnostics.</summary>
    public IReadOnlyDictionary<uint, long> TextureFormatMisses => Snapshot(_textureFormatMisses);

    /// <summary>Gets the number of combiner fragments whose output RGB was zero. Diagnostics.</summary>
    public ulong CombinerRgbZeroFragments => (ulong)_combinerRgbZeroFragments;

    /// <summary>Gets the number of combiner fragments whose output RGB was zero with nonzero alpha. Diagnostics.</summary>
    public ulong CombinerRgbZeroAlphaPositiveFragments => (ulong)_combinerRgbZeroAlphaFragments;

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

    /// <summary>Gets the number of fragments whose device-unit depth was moved by the ZCLIP bounds. Diagnostics.</summary>
    public ulong ZclipClampedPixels { get; private set; }

    /// <summary>Gets the number of fragments discarded by the ZCLIP bounds (ZCLAMP_EN = CULL). Diagnostics.</summary>
    public ulong ZclipCulledPixels { get; private set; }

    /// <summary>Gets the number of presented frames (flip increment events). Diagnostics.</summary>
    public ulong FramesPresented { get; private set; }

    /// <summary>Gets the number of flip stall waits observed. Diagnostics.</summary>
    public ulong FlipStalls { get; private set; }

    /// <summary>Gets the presented-frame state summary. Diagnostics.</summary>
    public string PresentedFrameDebug =>
        $"frames={FramesPresented} stalls={FlipStalls} read={FlipReadDebug} write={FlipWriteDebug} modulo={FlipModuloDebug} " +
        $"surface={_presentedOffset:X} {_presentedWidth}x{_presentedHeight} pitch={_presentedPitch} " +
        $"pcrtc={_pcrtcStart:X} valid={_presentedValid} frame={_presentedFrame} " +
        $"crtc={DisplayWidth}x{DisplayHeight}+{DisplayLineOffset} bpp={DisplayBytesPerPixel} " +
        $"display={DisplayStartAddress:X}";

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
        $"cull={_cullFaceEnable}:{_cullFace}:{(_frontFaceCw ? "cw" : "ccw")}:{TrianglesCulled} " +
        $"mask={_colorMask:X8} zetaDma={_dmaZeta:X}";

    /// <summary>Gets the number of draws skipped for unsupported layouts. Diagnostics.</summary>
    public ulong SkippedDraws { get; private set; }

    /// <summary>Gets the number of executed draws whose transform execution mode was not programmable. Diagnostics.</summary>
    public ulong NonProgrammableModeDraws { get; private set; }

    /// <summary>Gets the number of executed draws that ran the fixed-function vertex stage. Diagnostics.</summary>
    public ulong FixedFunctionDraws { get; private set; }

    /// <summary>
    /// Gets the number of fixed-function draws that had <c>SET_LIGHTING_ENABLE</c>
    /// set. Those run the reference's material/ambient/emission chain.
    /// </summary>
    public ulong FixedFunctionLitDraws { get; private set; }

    /// <summary>
    /// Gets the number of lit fixed-function draws that also enabled at least one
    /// light (<c>SET_LIGHT_ENABLE_MASK</c> != 0). The enabled-light loop is not
    /// implemented, so these draws keep the material chain without the light
    /// terms; the counter keeps that documented gap measurable.
    /// </summary>
    public ulong FixedFunctionLightedDraws { get; private set; }

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
        if (_pusherKickPending)
        {
            _pusherKickPending = false;
            RunPusher();
        }

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

    /// <summary>Returns the NV2A control window to its power-on state: clears the PFIFO/PVIDEO/PFB/PGRAPH register arrays, pending/enabled IRQ state, pusher state, and presented-surface bookkeeping.</summary>
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
        _cullFaceEnable = false;
        _cullFace = CullBackFace;
        _frontFaceCw = true;
        _zFormatFloat = false;
        _zclipMin = 0f;
        _zclipMax = float.PositiveInfinity;
        _zclipActive = false;
        _zclipCull = true;
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
        _dmaColor = 0;
        _waitingForNop = false;
        _pusherKickPending = false;
        _surfaceChannel = 0;
        _colorSurfaceBase = 0;
        _zetaSurfaceBase = 0;
        Array.Clear(_vertexFormat);
        Array.Clear(_vertexArrayOffset);
        Array.Clear(_textureOffset);
        Array.Clear(_textureFormat);
        Array.Clear(_textureAddress);
        Array.Clear(_textureControl0);
        Array.Clear(_textureControl1);
        Array.Clear(_textureFilter);
        Array.Clear(_textureImageRect);
        Array.Clear(_textureBorderColor);
        _shaderClipMode = 0;
        _shadowDepthFunc = ShadowDepthFuncNever;
        foreach (float[] matrix in _bumpMatrices)
        {
            Array.Clear(matrix);
        }

        Array.Clear(_bumpScales);
        Array.Clear(_bumpOffsets);
        _fogEnable = false;
        _fogMode = 0;
        _fogGenMode = 0;
        _fogColor[0] = 0f;
        _fogColor[1] = 0f;
        _fogColor[2] = 0f;
        _fogColor[3] = 0f;
        Array.Clear(_fogParams);
        TexModeFallbacks = 0;
        UnsupportedDotMaps = 0;
        TextureSampleSuccesses = 0;
        TextureSampleDisabled = 0;
        TextureSampleBadSize = 0;
        TextureSampleRgbZero = 0;
        Array.Clear(_textureFormatAttempts);
        Array.Clear(_textureFormatMisses);
        Array.Clear(_texModeFallbackByMode);
        _combinerRgbZeroFragments = 0;
        _combinerRgbZeroAlphaFragments = 0;
        _textureStateIndex.Clear();
        _textureStateKeys.Clear();
        Array.Clear(_textureStateSamples);
        Array.Clear(_textureStateRgbZero);
        Array.Clear(_textureStateFallbacks);
        Array.Clear(_textureStateMisses);
        Array.Clear(_textureStateFirstAddress);
        Array.Clear(_textureStateFirstTexel);
        Array.Clear(_textureStateMinU);
        Array.Clear(_textureStateMaxU);
        Array.Clear(_textureStateMinV);
        Array.Clear(_textureStateMaxV);
        _currentTextureState = 0;
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
        _fixedFunction.Reset();
        TransformConstantLoadDebug = 0;
        _beginMode = 0;
        DrawsExecuted = 0;
        CombinerDraws = 0;
        _combiner.Reset();
        DrawnPixels = 0;
        TexturedPixels = 0;
        BlendedPixels = 0;
        DepthTestedPixels = 0;
        ZclipClampedPixels = 0;
        ZclipCulledPixels = 0;
        SkippedDraws = 0;
        NonProgrammableModeDraws = 0;
        FixedFunctionDraws = 0;
        FixedFunctionLitDraws = 0;
        FixedFunctionLightedDraws = 0;
        _methodCounts.Clear();
        _methodParams.Clear();
        _methodStream.Clear();
        Array.Clear(_boundEngine);
        Array.Clear(_boundInstance);
        Array.Clear(_prmcio);
        Array.Clear(_vgaCrtc);
        Array.Clear(_vgaSequencer);
        Array.Clear(_vgaGraphics);
        Array.Clear(_vgaAttribute);
        _vgaCrtcIndex = 0;
        _vgaSequencerIndex = 0;
        _vgaGraphicsIndex = 0;
        _vgaAttributeIndex = 0;
        _vgaAttributeFlipFlop = false;
        _vgaCrtcWritten = false;
        _retrace = false;
        _pfifo[PfifoCache1Status / 4] = PfifoCache1StatusLowMark;
        _pfb[PfbCfg0 / 4] = PfbCfg0Reset;
        Backend?.ResetState();
    }

    /// <summary>Saves FIFO positions, PGRAPH/method state, surfaces, flip/semaphore/PRAMIN, texture/combiner/zclip/fog/exotic state, and all diagnostic counters.</summary>
    /// <remarks>Quiesce: save with the FIFO drained at a flip boundary (no pusher in flight;
    /// the synchronous pusher leaves no partial command when idle). The guest-memory adapter
    /// is host wiring and is not serialized. Presented-frame pixels live in guest RAM and are
    /// re-read from there; only the at-flip surface descriptors are stored.</remarks>
    public void SaveState(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Nv2ASnapshotVersion);
        writer.Write(RamSize);
        WriteUints(writer, _boundEngine);
        WriteUints(writer, _boundInstance);
        WriteUints(writer, _pfifo);
        WriteUints(writer, _pvideo);
        WriteUints(writer, _pfb);
        WriteUints(writer, _pgraph);
        writer.Write(_ramin);
        WriteUints(writer, _prmcio);
        writer.Write(_vgaCrtc);
        writer.Write(_vgaSequencer);
        writer.Write(_vgaGraphics);
        writer.Write(_vgaAttribute);
        writer.Write(_vgaCrtcIndex);
        writer.Write(_vgaSequencerIndex);
        writer.Write(_vgaGraphicsIndex);
        writer.Write(_vgaAttributeIndex);
        writer.Write(_vgaAttributeFlipFlop);
        writer.Write(_vgaCrtcWritten);
        writer.Write(_retrace);
        WriteCountMap(writer, _readCounts);
        WriteCountMap(writer, _writeCounts);
        WriteCountMap(writer, _userReadOffsets);
        writer.Write(_pmcIntrEnLog.Count);
        foreach (uint entry in _pmcIntrEnLog)
        {
            writer.Write(entry);
        }

        writer.Write(_pmcPending);
        writer.Write(PmcEnabledDebug);
        writer.Write(_pbusCommand);
        writer.Write(_ptimerPending);
        writer.Write(_ptimerEnabled);
        writer.Write(_ptimerNumerator);
        writer.Write(_ptimerDenominator);
        writer.Write(_ptimerClock);
        writer.Write(_pcrtcPending);
        writer.Write(_pcrtcEnabled);
        writer.Write(_pcrtcStart);
        writer.Write(_pcrtcRaster);
        writer.Write(_nvpllCoeff);
        writer.Write(_mpllCoeff);
        writer.Write(_vpllCoeff);
        writer.Write(_generalControl);
        writer.Write(_pfifoPending);
        writer.Write(_pfifoEnabled);
        writer.Write(_vsyncRemainder);
        writer.Write(PusherWordsConsumed);
        writer.Write(PusherCapped);
        writer.Write(PusherError);
        writer.Write(ContextSwitches);
        writer.Write(ClearsExecuted);
        writer.Write(_surfaceFormat);
        writer.Write(_surfacePitch);
        writer.Write(PresentedColorOffset);
        writer.Write(_surfaceZetaOffset);
        writer.Write(_surfaceClipH);
        writer.Write(_surfaceClipV);
        writer.Write(_zetaClearValue);
        writer.Write(_colorClearValue);
        writer.Write(_dmaSemaphore);
        writer.Write(_semaphoreOffset);
        writer.Write(_clearRectH);
        writer.Write(_clearRectV);
        writer.Write(FlipReadDebug);
        writer.Write(FlipWriteDebug);
        writer.Write(FlipModuloDebug);
        writer.Write(_presentedOffset);
        writer.Write(_presentedWidth);
        writer.Write(_presentedHeight);
        writer.Write(_presentedPitch);
        writer.Write(_presentedBytes);
        writer.Write(_presentedValid);
        writer.Write(_presentedFrame);
        writer.Write(FramesPresented);
        writer.Write(FlipStalls);
        WriteUints(writer, _vertexFormat);
        WriteUints(writer, _vertexArrayOffset);
        for (int i = 0; i < MaxTransformProgramLength; i++)
        {
            WriteUints(writer, _transformProgram[i]);
        }

        for (int i = 0; i < MaxVertexShaderConstants; i++)
        {
            WriteUints(writer, _transformConstants[i]);
        }

        writer.Write(_transformProgramStart);
        writer.Write(_transformExecutionMode);
        writer.Write(_transformProgramCxtWriteEn);
        _fixedFunction.Write(writer);
        writer.Write(TransformProgramLoadDebug);
        writer.Write(TransformConstantLoadDebug);
        writer.Write(_beginMode);
        writer.Write(_inlineElements.Count);
        foreach (uint entry in _inlineElements)
        {
            writer.Write(entry);
        }

        writer.Write(_inlineWords.Count);
        foreach (uint entry in _inlineWords)
        {
            writer.Write(entry);
        }

        writer.Write(_inlineVertices.Count);
        foreach (float entry in _inlineVertices)
        {
            writer.Write(entry);
        }

        writer.Write(_drawArraysRanges.Count);
        foreach (var range in _drawArraysRanges)
        {
            writer.Write(range.Start);
            writer.Write(range.Count);
        }

        for (int i = 0; i < VertexAttributeCount; i++)
        {
            for (int j = 0; j < VertexAttributeComponentCount; j++)
            {
                writer.Write(_inlineValues[i][j]);
            }
        }

        WriteUints(writer, _textureOffset);
        WriteUints(writer, _textureFormat);
        WriteUints(writer, _textureAddress);
        WriteUints(writer, _textureControl0);
        WriteUints(writer, _textureControl1);
        WriteUints(writer, _textureFilter);
        WriteUints(writer, _textureImageRect);
        WriteUints(writer, _textureBorderColor);
        writer.Write(_shaderClipMode);
        writer.Write(_shadowDepthFunc);
        for (int i = 0; i < _bumpMatrices.Length; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                writer.Write(_bumpMatrices[i][j]);
            }
        }

        for (int i = 0; i < _bumpScales.Length; i++)
        {
            writer.Write(_bumpScales[i]);
        }

        for (int i = 0; i < _bumpOffsets.Length; i++)
        {
            writer.Write(_bumpOffsets[i]);
        }

        writer.Write(_fogEnable);
        writer.Write(_fogMode);
        writer.Write(_fogGenMode);
        for (int i = 0; i < 4; i++)
        {
            writer.Write(_fogColor[i]);
        }

        for (int i = 0; i < 3; i++)
        {
            writer.Write(_fogParams[i]);
        }

        _combiner.SaveState(writer);
        writer.Write(_alphaTestEnable);
        writer.Write(_alphaFunc);
        writer.Write(_alphaRef);
        writer.Write(_blendEnable);
        writer.Write(_blendSrcFactor);
        writer.Write(_blendDstFactor);
        writer.Write(_blendColor);
        writer.Write(_blendEquation);
        writer.Write(_depthTestEnable);
        writer.Write(_depthFunc);
        writer.Write(_depthMask);
        writer.Write(_cullFaceEnable);
        writer.Write(_cullFace);
        writer.Write(_frontFaceCw);
        writer.Write(_zFormatFloat);
        writer.Write(_zclipMin);
        writer.Write(_zclipMax);
        writer.Write(_zclipActive);
        writer.Write(_zclipCull);
        writer.Write(_colorMask);
        writer.Write(_stencilTestEnable);
        writer.Write(_stencilWriteMask);
        writer.Write(_stencilFunc);
        writer.Write(_stencilRef);
        writer.Write(_stencilReadMask);
        writer.Write(_stencilOpFail);
        writer.Write(_stencilOpZfail);
        writer.Write(_stencilOpZpass);
        writer.Write(_dmaZeta);
        writer.Write(_dmaColor);
        writer.Write(_waitingForNop);
        writer.Write(_pusherKickPending);
        writer.Write(DrawsExecuted);
        writer.Write(CombinerDraws);
        writer.Write(TexModeFallbacks);
        writer.Write(UnsupportedDotMaps);
        writer.Write(TextureSampleSuccesses);
        writer.Write(TextureSampleDisabled);
        writer.Write(TextureSampleBadSize);
        writer.Write(TextureSampleRgbZero);
        WriteLongs(writer, _textureFormatAttempts);
        WriteLongs(writer, _textureFormatMisses);
        WriteLongs(writer, _texModeFallbackByMode);
        writer.Write(_combinerRgbZeroFragments);
        writer.Write(_combinerRgbZeroAlphaFragments);
        writer.Write(_textureStateIndex.Count);
        foreach (var entry in _textureStateIndex)
        {
            writer.Write(entry.Key);
            writer.Write(entry.Value);
        }

        writer.Write(_textureStateKeys.Count);
        foreach (string entry in _textureStateKeys)
        {
            writer.Write(entry);
        }

        WriteLongs(writer, _textureStateSamples);
        WriteLongs(writer, _textureStateRgbZero);
        WriteLongs(writer, _textureStateFallbacks);
        WriteLongs(writer, _textureStateMisses);
        WriteUints(writer, _textureStateFirstAddress);
        WriteUints(writer, _textureStateFirstTexel);
        WriteFloats(writer, _textureStateMinU);
        WriteFloats(writer, _textureStateMaxU);
        WriteFloats(writer, _textureStateMinV);
        WriteFloats(writer, _textureStateMaxV);
        writer.Write(_currentTextureState);
        writer.Write(DrawnPixels);
        writer.Write(TexturedPixels);
        writer.Write(BlendedPixels);
        writer.Write(DepthTestedPixels);
        writer.Write(ZclipClampedPixels);
        writer.Write(ZclipCulledPixels);
        writer.Write(SkippedDraws);
        writer.Write(NonProgrammableModeDraws);
        writer.Write(FixedFunctionDraws);
        writer.Write(FixedFunctionLitDraws);
        writer.Write(FixedFunctionLightedDraws);
        writer.Write(SemaphoreReleases);
        writer.Write(SemaphoreDebug);
        writer.Write(PgraphEnabledDebug);
        writer.Write(PgraphPendingDebug);
        writer.Write(NotificationsRaised);
        writer.Write(NotificationAsserted);
        writer.Write(PraminWrites);
        writer.Write(InterruptAsserted);
        writer.Write(MethodStreamCap);
        WriteCountMap(writer, _methodCounts);
        writer.Write(_methodParams.Count);
        foreach (var entry in _methodParams)
        {
            writer.Write(entry.Key);
            writer.Write(entry.Value);
        }

        writer.Write(_methodStream.Count);
        foreach (var entry in _methodStream)
        {
            writer.Write(entry.Subchannel);
            writer.Write(entry.Method);
            writer.Write(entry.Parameter);
        }
    }

    /// <summary>Restores FIFO positions, PGRAPH/method state, surfaces, flip/semaphore/PRAMIN, texture/combiner/zclip/fog/exotic state, and all diagnostic counters.</summary>
    public void LoadState(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int version = reader.ReadInt32();
        if (version != Nv2ASnapshotVersion)
        {
            throw new InvalidDataException($"Unsupported NV2A snapshot version {version}.");
        }

        if (reader.ReadUInt32() != RamSize)
        {
            throw new InvalidDataException("NV2A RAM-size mismatch on snapshot load.");
        }

        ReadUints(reader, _boundEngine);
        ReadUints(reader, _boundInstance);
        ReadUints(reader, _pfifo);
        ReadUints(reader, _pvideo);
        ReadUints(reader, _pfb);
        ReadUints(reader, _pgraph);
        ReadBytesExact(reader, _ramin);
        ReadUints(reader, _prmcio);
        ReadBytesExact(reader, _vgaCrtc);
        ReadBytesExact(reader, _vgaSequencer);
        ReadBytesExact(reader, _vgaGraphics);
        ReadBytesExact(reader, _vgaAttribute);
        _vgaCrtcIndex = reader.ReadByte();
        _vgaSequencerIndex = reader.ReadByte();
        _vgaGraphicsIndex = reader.ReadByte();
        _vgaAttributeIndex = reader.ReadByte();
        _vgaAttributeFlipFlop = reader.ReadBoolean();
        _vgaCrtcWritten = reader.ReadBoolean();
        _retrace = reader.ReadBoolean();
        ReadCountMap(reader, _readCounts);
        ReadCountMap(reader, _writeCounts);
        ReadCountMap(reader, _userReadOffsets);
        _pmcIntrEnLog.Clear();
        int pmcLog = reader.ReadInt32();
        if (pmcLog < 0)
        {
            throw new InvalidDataException("Bad NV2A PMC log snapshot.");
        }

        for (int i = 0; i < pmcLog; i++)
        {
            _pmcIntrEnLog.Add(reader.ReadUInt32());
        }

        _pmcPending = reader.ReadUInt32();
        PmcEnabledDebug = reader.ReadUInt32();
        _pbusCommand = reader.ReadUInt32();
        _ptimerPending = reader.ReadUInt32();
        _ptimerEnabled = reader.ReadUInt32();
        _ptimerNumerator = reader.ReadUInt32();
        _ptimerDenominator = reader.ReadUInt32();
        _ptimerClock = reader.ReadUInt64();
        _pcrtcPending = reader.ReadUInt32();
        _pcrtcEnabled = reader.ReadUInt32();
        _pcrtcStart = reader.ReadUInt32();
        _pcrtcRaster = reader.ReadUInt32();
        _nvpllCoeff = reader.ReadUInt32();
        _mpllCoeff = reader.ReadUInt32();
        _vpllCoeff = reader.ReadUInt32();
        _generalControl = reader.ReadUInt32();
        _pfifoPending = reader.ReadUInt32();
        _pfifoEnabled = reader.ReadUInt32();
        _vsyncRemainder = reader.ReadUInt64();
        PusherWordsConsumed = reader.ReadUInt64();
        PusherCapped = reader.ReadBoolean();
        PusherError = reader.ReadUInt32();
        ContextSwitches = reader.ReadUInt32();
        ClearsExecuted = reader.ReadUInt32();
        _surfaceFormat = reader.ReadUInt32();
        _surfacePitch = reader.ReadUInt32();
        PresentedColorOffset = reader.ReadUInt32();
        _surfaceZetaOffset = reader.ReadUInt32();
        _surfaceClipH = reader.ReadUInt32();
        _surfaceClipV = reader.ReadUInt32();
        _zetaClearValue = reader.ReadUInt32();
        _colorClearValue = reader.ReadUInt32();
        _dmaSemaphore = reader.ReadUInt32();
        _semaphoreOffset = reader.ReadUInt32();
        _clearRectH = reader.ReadUInt32();
        _clearRectV = reader.ReadUInt32();
        FlipReadDebug = reader.ReadUInt32();
        FlipWriteDebug = reader.ReadUInt32();
        FlipModuloDebug = reader.ReadUInt32();
        _presentedOffset = reader.ReadUInt32();
        _presentedWidth = reader.ReadUInt32();
        _presentedHeight = reader.ReadUInt32();
        _presentedPitch = reader.ReadUInt32();
        _presentedBytes = reader.ReadUInt32();
        _presentedValid = reader.ReadBoolean();
        _presentedFrame = reader.ReadUInt64();
        FramesPresented = reader.ReadUInt64();
        FlipStalls = reader.ReadUInt64();
        ReadUints(reader, _vertexFormat);
        ReadUints(reader, _vertexArrayOffset);
        for (int i = 0; i < MaxTransformProgramLength; i++)
        {
            ReadUints(reader, _transformProgram[i]);
        }

        for (int i = 0; i < MaxVertexShaderConstants; i++)
        {
            ReadUints(reader, _transformConstants[i]);
        }

        _transformProgramStart = reader.ReadUInt32();
        _transformExecutionMode = reader.ReadUInt32();
        _transformProgramCxtWriteEn = reader.ReadUInt32();
        _fixedFunction.Read(reader);
        TransformProgramLoadDebug = reader.ReadUInt32();
        TransformConstantLoadDebug = reader.ReadUInt32();
        _beginMode = reader.ReadUInt32();
        _inlineElements.Clear();
        int inlineElements = reader.ReadInt32();
        if (inlineElements < 0 || inlineElements > MaxInlineElements)
        {
            throw new InvalidDataException("Bad NV2A inline-element snapshot.");
        }

        for (int i = 0; i < inlineElements; i++)
        {
            _inlineElements.Add(reader.ReadUInt32());
        }

        _inlineWords.Clear();
        int inlineWords = reader.ReadInt32();
        if (inlineWords < 0 || inlineWords > (int)MaxInlineWords)
        {
            throw new InvalidDataException("Bad NV2A inline-word snapshot.");
        }

        for (int i = 0; i < inlineWords; i++)
        {
            _inlineWords.Add(reader.ReadUInt32());
        }

        _inlineVertices.Clear();
        int inlineVertices = reader.ReadInt32();
        if (inlineVertices < 0 || inlineVertices > MaxInlineElements * InlineVertexWordCount)
        {
            throw new InvalidDataException("Bad NV2A inline-vertex snapshot.");
        }

        for (int i = 0; i < inlineVertices; i++)
        {
            _inlineVertices.Add(reader.ReadSingle());
        }

        _drawArraysRanges.Clear();
        int ranges = reader.ReadInt32();
        if (ranges < 0 || ranges > MaxDrawArrayRanges)
        {
            throw new InvalidDataException("Bad NV2A draw-array snapshot.");
        }

        for (int i = 0; i < ranges; i++)
        {
            _drawArraysRanges.Add((reader.ReadInt32(), reader.ReadInt32()));
        }

        for (int i = 0; i < VertexAttributeCount; i++)
        {
            for (int j = 0; j < VertexAttributeComponentCount; j++)
            {
                _inlineValues[i][j] = reader.ReadSingle();
            }
        }

        ReadUints(reader, _textureOffset);
        ReadUints(reader, _textureFormat);
        ReadUints(reader, _textureAddress);
        ReadUints(reader, _textureControl0);
        ReadUints(reader, _textureControl1);
        ReadUints(reader, _textureFilter);
        ReadUints(reader, _textureImageRect);
        ReadUints(reader, _textureBorderColor);
        _shaderClipMode = reader.ReadUInt32();
        _shadowDepthFunc = reader.ReadUInt32();
        for (int i = 0; i < _bumpMatrices.Length; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                _bumpMatrices[i][j] = reader.ReadSingle();
            }
        }

        for (int i = 0; i < _bumpScales.Length; i++)
        {
            _bumpScales[i] = reader.ReadSingle();
        }

        for (int i = 0; i < _bumpOffsets.Length; i++)
        {
            _bumpOffsets[i] = reader.ReadSingle();
        }

        _fogEnable = reader.ReadBoolean();
        _fogMode = reader.ReadInt32();
        _fogGenMode = reader.ReadUInt32();
        for (int i = 0; i < 4; i++)
        {
            _fogColor[i] = reader.ReadSingle();
        }

        for (int i = 0; i < 3; i++)
        {
            _fogParams[i] = reader.ReadSingle();
        }

        _combiner.LoadState(reader);
        _alphaTestEnable = reader.ReadBoolean();
        _alphaFunc = reader.ReadUInt32();
        _alphaRef = reader.ReadUInt32();
        _blendEnable = reader.ReadBoolean();
        _blendSrcFactor = reader.ReadUInt32();
        _blendDstFactor = reader.ReadUInt32();
        _blendColor = reader.ReadUInt32();
        _blendEquation = reader.ReadUInt32();
        _depthTestEnable = reader.ReadBoolean();
        _depthFunc = reader.ReadUInt32();
        _depthMask = reader.ReadBoolean();
        _cullFaceEnable = reader.ReadBoolean();
        _cullFace = reader.ReadUInt32();
        _frontFaceCw = reader.ReadBoolean();
        _zFormatFloat = reader.ReadBoolean();
        _zclipMin = reader.ReadSingle();
        _zclipMax = reader.ReadSingle();
        _zclipActive = reader.ReadBoolean();
        _zclipCull = reader.ReadBoolean();
        _colorMask = reader.ReadUInt32();
        _stencilTestEnable = reader.ReadBoolean();
        _stencilWriteMask = reader.ReadUInt32();
        _stencilFunc = reader.ReadUInt32();
        _stencilRef = reader.ReadUInt32();
        _stencilReadMask = reader.ReadUInt32();
        _stencilOpFail = reader.ReadUInt32();
        _stencilOpZfail = reader.ReadUInt32();
        _stencilOpZpass = reader.ReadUInt32();
        _dmaZeta = reader.ReadUInt32();
        _dmaColor = reader.ReadUInt32();
        _waitingForNop = reader.ReadBoolean();
        _pusherKickPending = reader.ReadBoolean();
        DrawsExecuted = reader.ReadUInt64();
        CombinerDraws = reader.ReadUInt64();
        TexModeFallbacks = reader.ReadUInt64();
        UnsupportedDotMaps = reader.ReadUInt64();
        TextureSampleSuccesses = reader.ReadUInt64();
        TextureSampleDisabled = reader.ReadUInt64();
        TextureSampleBadSize = reader.ReadUInt64();
        TextureSampleRgbZero = reader.ReadUInt64();
        ReadLongs(reader, _textureFormatAttempts);
        ReadLongs(reader, _textureFormatMisses);
        ReadLongs(reader, _texModeFallbackByMode);
        _combinerRgbZeroFragments = reader.ReadInt64();
        _combinerRgbZeroAlphaFragments = reader.ReadInt64();
        int stateIndex = reader.ReadInt32();
        if (stateIndex < 0 || stateIndex > TextureStateCap)
        {
            throw new InvalidDataException("Bad NV2A texture-state snapshot.");
        }

        _textureStateIndex.Clear();
        for (int i = 0; i < stateIndex; i++)
        {
            _textureStateIndex[reader.ReadString()] = reader.ReadInt32();
        }

        int stateKeys = reader.ReadInt32();
        if (stateKeys < 0 || stateKeys > TextureStateCap)
        {
            throw new InvalidDataException("Bad NV2A texture-key snapshot.");
        }

        _textureStateKeys.Clear();
        for (int i = 0; i < stateKeys; i++)
        {
            _textureStateKeys.Add(reader.ReadString());
        }

        ReadLongs(reader, _textureStateSamples);
        ReadLongs(reader, _textureStateRgbZero);
        ReadLongs(reader, _textureStateFallbacks);
        ReadLongs(reader, _textureStateMisses);
        ReadUints(reader, _textureStateFirstAddress);
        ReadUints(reader, _textureStateFirstTexel);
        ReadFloats(reader, _textureStateMinU);
        ReadFloats(reader, _textureStateMaxU);
        ReadFloats(reader, _textureStateMinV);
        ReadFloats(reader, _textureStateMaxV);
        _currentTextureState = reader.ReadInt32();
        DrawnPixels = reader.ReadUInt64();
        TexturedPixels = reader.ReadUInt64();
        BlendedPixels = reader.ReadUInt64();
        DepthTestedPixels = reader.ReadUInt64();
        ZclipClampedPixels = reader.ReadUInt64();
        ZclipCulledPixels = reader.ReadUInt64();
        SkippedDraws = reader.ReadUInt64();
        NonProgrammableModeDraws = reader.ReadUInt64();
        FixedFunctionDraws = reader.ReadUInt64();
        FixedFunctionLitDraws = reader.ReadUInt64();
        FixedFunctionLightedDraws = reader.ReadUInt64();
        SemaphoreReleases = reader.ReadInt32();
        SemaphoreDebug = reader.ReadString();
        PgraphEnabledDebug = reader.ReadUInt32();
        PgraphPendingDebug = reader.ReadUInt32();
        NotificationsRaised = reader.ReadInt32();
        NotificationAsserted = reader.ReadBoolean();
        PraminWrites = reader.ReadInt32();
        InterruptAsserted = reader.ReadBoolean();
        MethodStreamCap = reader.ReadUInt64();
        ReadCountMap(reader, _methodCounts);
        int methodParams = reader.ReadInt32();
        if (methodParams < 0)
        {
            throw new InvalidDataException("Bad NV2A method-param snapshot.");
        }

        _methodParams.Clear();
        for (int i = 0; i < methodParams; i++)
        {
            _methodParams[reader.ReadUInt32()] = reader.ReadUInt32();
        }

        int stream = reader.ReadInt32();
        if (stream < 0 || (ulong)stream > MethodStreamCap)
        {
            throw new InvalidDataException("Bad NV2A method-stream snapshot.");
        }

        _methodStream.Clear();
        for (int i = 0; i < stream; i++)
        {
            _methodStream.Add((reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()));
        }

        // Backend state is host-side only and not part of the snapshot
        // format (v2 stays software-reproducible): drop it and let the
        // backend rebuild from guest state as methods flow again.
        Backend?.ResetState();
    }

    private const int Nv2ASnapshotVersion = 6;

    private static void WriteUints(BinaryWriter writer, uint[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static void ReadUints(BinaryReader reader, uint[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadUInt32();
        }
    }

    private static void WriteLongs(BinaryWriter writer, long[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static void ReadLongs(BinaryReader reader, long[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadInt64();
        }
    }

    private static void WriteFloats(BinaryWriter writer, float[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            writer.Write(values[i]);
        }
    }

    private static void ReadFloats(BinaryReader reader, float[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadSingle();
        }
    }

    private static void WriteCountMap(BinaryWriter writer, Dictionary<uint, int> map)
    {
        writer.Write(map.Count);
        foreach (var entry in map)
        {
            writer.Write(entry.Key);
            writer.Write(entry.Value);
        }
    }

    private static void ReadCountMap(BinaryReader reader, Dictionary<uint, int> map)
    {
        int count = reader.ReadInt32();
        if (count < 0)
        {
            throw new InvalidDataException("Bad NV2A histogram snapshot.");
        }

        map.Clear();
        for (int i = 0; i < count; i++)
        {
            map[reader.ReadUInt32()] = reader.ReadInt32();
        }
    }

    private static void ReadBytesExact(BinaryReader reader, byte[] target)
    {
        byte[] data = reader.ReadBytes(target.Length);
        if (data.Length != target.Length)
        {
            throw new InvalidDataException("Truncated NV2A PRAMIN snapshot.");
        }

        Array.Copy(data, target, target.Length);
    }

    /// <summary>Reads from the NV2A register window at the given absolute address, extracting the addressed byte lane from the aligned 32-bit register.</summary>
    /// <param name="address">Absolute address inside the 16 MB control window.</param>
    /// <param name="size">Access size in bytes (1, 2, or 4).</param>
    /// <returns>The register value truncated and shifted to the addressed lane.</returns>
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

    /// <summary>Writes to the NV2A register window at the given absolute address; sub-word writes read-modify-write only the addressed byte lane of the aligned 32-bit register.</summary>
    /// <param name="address">Absolute address inside the 16 MB control window.</param>
    /// <param name="size">Access size in bytes (1, 2, or 4).</param>
    /// <param name="value">Value to write, in the low <paramref name="size"/> bytes.</param>
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
        uint next = (word & ~mask) | ((value << (int)shift) & mask);
        if (aligned >= PrmcioBase && aligned < PrmcioBase + 0x1000u)
        {
            // A VGA port write must be applied even when the data byte equals
            // the previously written one - two registers may share a value,
            // and a diff against the stored word would swallow the second.
            WritePrmcio(aligned - PrmcioBase, next, (int)(offset - aligned), size);
            return;
        }

        WriteAligned(aligned, next);
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
            WritePrmcio(offset - PrmcioBase, value, 0, 4);
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
                if ((PgraphPendingDebug & PgraphIntrError) == 0)
                {
                    if (_waitingForNop)
                    {
                        // The guest acknowledges the notification before it reads the
                        // subprogram parameter registers. Model the GPU engine latency
                        // by resuming on the next tick, after the handler has consumed
                        // the parameters it was notified for.
                        _waitingForNop = false;
                        _pusherKickPending = true;
                    }
                    else
                    {
                        RunPusher();
                    }
                }

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

    /// <summary>
    /// Stores a word of the NV_PRMCIO window and applies the addressed byte
    /// lanes to the VGA register file. The bus reports which lanes a write
    /// covered, so an index/data pair is never inferred from value changes.
    /// </summary>
    private void WritePrmcio(uint register, uint value, int laneStart, int laneCount)
    {
        _prmcio[(int)(register / 4)] = value;
        for (int lane = laneStart; lane < laneStart + laneCount && lane < 4; lane++)
        {
            ApplyVgaPortWrite((ushort)(register + lane), (byte)(value >> (lane * 8)));
        }
    }

    /// <summary>
    /// VGA I/O port semantics for the ports NV2A mirrors at NV_PRMCIO
    /// (xemu prmcio.c/prmvio.c -> vga_ioport_write). Only the index/data
    /// latches the display model needs are decoded; unmodelled ports are
    /// already stored verbatim in <see cref="_prmcio"/> for read-back.
    /// </summary>
    private void ApplyVgaPortWrite(ushort port, byte value)
    {
        switch (port)
        {
            case 0x3C0: // VGA_ATT_W: index on flip-flop 0, data on flip-flop 1
                if (!_vgaAttributeFlipFlop)
                {
                    _vgaAttributeIndex = (byte)(value & 0x3F);
                }
                else
                {
                    int attribute = _vgaAttributeIndex & 0x1F;
                    if (attribute < _vgaAttribute.Length)
                    {
                        _vgaAttribute[attribute] = value;
                    }
                }

                _vgaAttributeFlipFlop = !_vgaAttributeFlipFlop;
                break;

            case 0x3C4: // VGA_SEQ_I
                _vgaSequencerIndex = (byte)(value & 7);
                break;

            case 0x3C5: // VGA_SEQ_D
                _vgaSequencer[_vgaSequencerIndex] = value;
                break;

            case 0x3CE: // VGA_GFX_I
                _vgaGraphicsIndex = (byte)(value & 0x0F);
                break;

            case 0x3CF: // VGA_GFX_D
                _vgaGraphics[_vgaGraphicsIndex] = value;
                break;

            case 0x3D4: // VGA_CRT_IC (colour index port)
                _vgaCrtcIndex = value;
                break;

            case 0x3D5: // VGA_CRT_DC (colour data port)
                // CR0-CR7 are write-protected while CR11 bit 7 is set; CR7
                // bit 4 stays writable (QEMU vga_ioport_write).
                if ((_vgaCrtc[0x11] & 0x80) != 0 && _vgaCrtcIndex <= 7)
                {
                    if (_vgaCrtcIndex == 7)
                    {
                        _vgaCrtc[7] = (byte)((_vgaCrtc[7] & ~0x10) | (value & 0x10));
                    }

                    return;
                }

                _vgaCrtc[_vgaCrtcIndex] = value;
                _vgaCrtcWritten = true;
                break;

            default:
                break;
        }
    }

    /// <summary>Gets the display-controller scanout base (NV_PCRTC START, in bytes).</summary>
    public uint DisplayStartAddress => _pcrtcStart;

    /// <summary>Gets the scanout bytes per line the VGA CRTC programs (xemu nv2a_get_params).</summary>
    public uint DisplayLineOffset =>
        (uint)(_vgaCrtc[0x13] | ((_vgaCrtc[0x19] & 0xE0) << 3) | ((_vgaCrtc[0x25] & 0x20) << 6)) << 3;

    /// <summary>Gets the active display width in pixels (QEMU vga_get_resolution).</summary>
    public uint DisplayWidth => ((uint)_vgaCrtc[0x01] + 1u) * 8u;

    /// <summary>Gets the active display height in pixels (QEMU vga_get_resolution).</summary>
    public uint DisplayHeight =>
        (uint)(_vgaCrtc[0x12] | ((_vgaCrtc[0x07] & 0x02) << 7) | ((_vgaCrtc[0x07] & 0x40) << 3)) + 1u;

    /// <summary>Gets the bytes per scanout pixel the CRTC mode programs (NV CR28 bits 0-1).</summary>
    public uint DisplayBytesPerPixel => (_vgaCrtc[0x28] & 3) switch
    {
        2 => 2u,
        3 => 4u,
        _ => 0u,
    };

    /// <summary>Gets the VGA sequencer clocking mode byte (bit 5 = screen off).</summary>
    public byte DisplayClockMode => _vgaSequencer[1];

    /// <summary>
    /// Gets whether a 16 bpp display mode is 15-bit (X1R5G5B5) rather than
    /// 16-bit RGB565: NV CR28 selects 16 bpp, and PRAMDAC general control bit 12
    /// (ALT_MODE_SEL) selects which one (xemu nv2a_get_bpp).
    /// </summary>
    public bool DisplayFifteenBit => DisplayBytesPerPixel == 2 && (_generalControl & (1u << 12)) == 0;

    /// <summary>Gets the CRTC register summary the display model derives from. Diagnostics.</summary>
    public string VgaCrtcDebug =>
        $"cr13={_vgaCrtc[0x13]:X2} cr19={_vgaCrtc[0x19]:X2} cr25={_vgaCrtc[0x25]:X2} " +
        $"cr01={_vgaCrtc[0x01]:X2} cr12={_vgaCrtc[0x12]:X2} cr07={_vgaCrtc[0x07]:X2} cr28={_vgaCrtc[0x28]:X2} " +
        $"index={_vgaCrtcIndex:X2} sr01={_vgaSequencer[1]:X2}";

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
        uint offset = ((ramht & RamhtBaseMask) << 8) + (hash * 8);
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

    private bool TryResolveDmaContext(uint handle, uint channel, out uint baseAddress, out uint limit)
    {
        baseAddress = 0;
        limit = 0;
        if (handle == 0 || !TryRamhtLookup(handle, channel, out uint instance, out _) ||
            instance + 12 > PraminSize)
        {
            return false;
        }

        uint flags = RaminWord(instance);
        uint frame = RaminWord(instance + 8);
        baseAddress = ((frame & DmaAddressMask) | ((flags & DmaAdjustMask) >> 20)) & GpuAddressMask;
        limit = RaminWord(instance + 4);
        return true;
    }

    private uint SurfaceBase(uint handle)
    {
        return TryResolveDmaContext(handle, _surfaceChannel, out uint baseAddress, out _) ? baseAddress : 0u;
    }

    private bool SurfaceFitsContext(uint handle, uint offset, uint pitch, uint height)
    {
        if (!TryResolveDmaContext(handle, _surfaceChannel, out _, out uint limit))
        {
            return true;
        }

        return (ulong)offset + ((ulong)pitch * height) <= (ulong)limit + 1;
    }

    private void RunPusher()
    {
        if (_memory is null)
        {
            return;
        }

        if (_waitingForNop)
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
        while (!_waitingForNop)
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
                        uint advanced = ((state & DmaStateMethod) + (4 * consumed)) & DmaStateMethod;
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
        _surfaceChannel = channel;
        MethodTotal++;
        if (CollectMethodHistogram)
        {
            uint key = (subchannel << 16) | method;
            _methodCounts.TryGetValue(key, out int count);
            _methodCounts[key] = count + 1;
            _methodParams[key] = parameter;
        }

        if (MethodStreamCap == 0 || (ulong)_methodStream.Count < MethodStreamCap)
        {
            _methodStream.Add((subchannel, method, parameter));
        }

        if (_observeMethods)
        {
            _backend!.Method(subchannel, method, parameter);
        }

        switch (method)
        {
            case MethodNoOperation:
                if (parameter != 0)
                {
                    RaiseNotification(channel, subchannel, parameter);
                    _waitingForNop = true;
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
                _pgraph[PgraphZStencilClearValue / 4] = parameter;
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
                _pgraph[PgraphColorClearValue / 4] = parameter;
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
            case MethodContextDmaColor:
                _dmaColor = parameter;
                break;
            case MethodAlphaTestEnable:
                _alphaTestEnable = (parameter & 1u) != 0;
                break;
            case MethodBlendEnable:
                _blendEnable = (parameter & 1u) != 0;
                break;
            case MethodCullFaceEnable:
                _cullFaceEnable = (parameter & 1u) != 0;
                break;
            case MethodCullFace:
                _cullFace = parameter switch
                {
                    // NV097_SET_CULL_FACE_V_FRONT / _BACK / _FRONT_AND_BACK.
                    0x0404u => CullFrontFace,
                    0x0405u => CullBackFace,
                    0x0406u => CullFrontAndBackFace,
                    _ => _cullFace,
                };
                break;
            case MethodFrontFace:
                if (parameter is FrontFaceValueCw or FrontFaceValueCcw)
                {
                    _frontFaceCw = parameter == FrontFaceValueCw;
                }

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
            case MethodControl0:
                _zFormatFloat = (parameter & Control0ZFormat) != 0;
                break;
            case MethodClipMin:
                _zclipMin = BitConverter.UInt32BitsToSingle(parameter);
                _zclipActive = true;
                break;
            case MethodClipMax:
                _zclipMax = BitConverter.UInt32BitsToSingle(parameter);
                _zclipActive = true;
                break;
            case MethodZminMaxControl:
                // xemu pgraph SET_ZMIN_MAX_CONTROL: only the ZCLAMP_EN values
                // CULL (0) and CLAMP (1, raw field 0x10) are defined; reserved
                // values leave the previous mode untouched (reference default
                // asserts in debug, no-ops in release).
                _zclipCull = (parameter & ZminMaxZclampEnMask) switch
                {
                    0u => true,
                    ZminMaxZclampEnClamp => false,
                    _ => _zclipCull,
                };

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
            case MethodFogMode:
                _fogMode = MapFogMode(parameter, _fogMode);
                break;
            case MethodFogGenMode:
                _fogGenMode = parameter;
                break;
            case MethodFogEnable:
                _fogEnable = (parameter & 1u) != 0;
                break;
            case MethodFogColor:
                // Parameter channels are ABGR (red in bits 0-7); stored as RGBA floats.
                _fogColor[0] = (parameter & 0xFFu) / 255f;
                _fogColor[1] = ((parameter >> 8) & 0xFFu) / 255f;
                _fogColor[2] = ((parameter >> 16) & 0xFFu) / 255f;
                _fogColor[3] = ((parameter >> 24) & 0xFFu) / 255f;
                break;
            case MethodFogCoord:
                // SET_FOG_COORD writes the FOG vertex attribute (v5) inline
                // value, replicated across all four components; it does not
                // finish an inline vertex (reference pgraph SET_FOG_COORD).
                float fogCoord = BitConverter.UInt32BitsToSingle(parameter);
                _inlineValues[VertexAttributeFogCoord][0] = fogCoord;
                _inlineValues[VertexAttributeFogCoord][1] = fogCoord;
                _inlineValues[VertexAttributeFogCoord][2] = fogCoord;
                _inlineValues[VertexAttributeFogCoord][3] = fogCoord;
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
                    Backend?.Flip(new PgraphSurfaceRecord(
                        _surfaceClipH >> 16, _surfaceClipV >> 16,
                        ColorBytesPerPixel(_surfaceFormat & 0xFu), FramesPresented,
                        SurfaceBase(_dmaColor) + PresentedColorOffset,
                        _surfacePitch & 0xFFFFu));
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
                _transformExecutionMode = parameter & TransformExecutionModeMask;
                break;
            case MethodLightControl:
                _fixedFunction.SetLightControl(parameter);
                break;
            case MethodMaterialAlpha:
                _fixedFunction.MaterialAlpha = BitConverter.UInt32BitsToSingle(parameter);
                break;
            case MethodColorMaterial:
                _fixedFunction.SetColorMaterial(parameter);
                break;
            case MethodLightingEnable:
                _fixedFunction.Lighting = (parameter & 1u) != 0;
                break;
            case MethodSkinMode:
                _fixedFunction.Skinning = (int)(parameter & SkinModeMask);
                break;
            case MethodNormalizationEnable:
                _fixedFunction.Normalization = (parameter & 1u) != 0;
                break;
            case MethodSpecularEnable:
                _fixedFunction.SpecularEnable = (parameter & 1u) != 0;
                break;
            case MethodLightEnableMask:
                _fixedFunction.LightEnableMask = parameter;
                break;
            case MethodTransformProgramCxtWriteEn:
                _transformProgramCxtWriteEn = parameter;
                break;
            case MethodTransformProgramLoad:
                TransformProgramLoadDebug = parameter;
                break;
            case MethodTransformProgramStart:
                _transformProgramStart = parameter & TransformProgramStartMask;
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
            _vertexArrayOffset[(method - MethodVertexArrayOffsetBase) / 4] = parameter & VertexArrayOffsetMask;
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

        if (method >= MethodMaterialEmission && method <= MethodMaterialEmissionEnd &&
            ((method - MethodMaterialEmission) & 3u) == 0)
        {
            _fixedFunction.MaterialEmission[(method - MethodMaterialEmission) / 4] =
                BitConverter.UInt32BitsToSingle(parameter);
        }

        if (method >= MethodSceneAmbient && method <= MethodSceneAmbientEnd &&
            ((method - MethodSceneAmbient) & 3u) == 0)
        {
            _fixedFunction.SceneAmbient[(method - MethodSceneAmbient) / 4] =
                BitConverter.UInt32BitsToSingle(parameter);
        }

        if (method >= MethodTexGenBase && method <= MethodTexGenEnd + (TexGenStageStride * 3u) &&
            ((method - MethodTexGenBase) & 3u) == 0)
        {
            uint index = (method - MethodTexGenBase) / 4u;
            _fixedFunction.SetTexGen((int)(index / 4u), (int)(index % 4u), parameter);
        }

        if (method >= MethodTextureMatrixEnable && method < MethodTextureMatrixEnable + (uint)MaxTextureSlots)
        {
            _fixedFunction.SetTextureMatrixEnable(
                (int)(method - MethodTextureMatrixEnable), parameter);
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
                    case 0x24u:
                        _textureBorderColor[slot] = parameter;
                        break;
                    case 0x28u:
                    case 0x2Cu:
                    case 0x30u:
                    case 0x34u:
                        WriteBumpMatrix(slot, register, parameter);
                        break;
                    case 0x38u:
                        if (slot >= 1)
                        {
                            _bumpScales[slot - 1] = BitConverter.UInt32BitsToSingle(parameter);
                        }

                        break;
                    case 0x3Cu:
                        if (slot >= 1)
                        {
                            _bumpOffsets[slot - 1] = BitConverter.UInt32BitsToSingle(parameter);
                        }

                        break;
                    default:
                        break;
                }
            }
        }

        if (method >= MethodCombinerAlphaIcwBase && method <= MethodCombinerAlphaIcwEnd &&
            ((method - MethodCombinerAlphaIcwBase) & 3u) == 0)
        {
            _combiner.AlphaInputs[(method - MethodCombinerAlphaIcwBase) / 4] = parameter;
        }

        if (method == MethodCombinerSpecFog0)
        {
            _combiner.Final0 = parameter;
        }

        if (method == MethodCombinerSpecFog1)
        {
            _combiner.Final1 = parameter;
        }

        if (method >= MethodCombinerFactor0Base && method <= MethodCombinerFactor0End &&
            ((method - MethodCombinerFactor0Base) & 3u) == 0)
        {
            _combiner.Factor0[(method - MethodCombinerFactor0Base) / 4] = parameter;
        }

        if (method >= MethodCombinerFactor1Base && method <= MethodCombinerFactor1End &&
            ((method - MethodCombinerFactor1Base) & 3u) == 0)
        {
            _combiner.Factor1[(method - MethodCombinerFactor1Base) / 4] = parameter;
        }

        if (method >= MethodCombinerAlphaOcwBase && method <= MethodCombinerAlphaOcwEnd &&
            ((method - MethodCombinerAlphaOcwBase) & 3u) == 0)
        {
            _combiner.AlphaOutputs[(method - MethodCombinerAlphaOcwBase) / 4] = parameter;
        }

        if (method >= MethodCombinerColorIcwBase && method <= MethodCombinerColorIcwEnd &&
            ((method - MethodCombinerColorIcwBase) & 3u) == 0)
        {
            _combiner.RgbInputs[(method - MethodCombinerColorIcwBase) / 4] = parameter;
        }

        if (method >= MethodFogParamsBase && method <= MethodFogParamsEnd &&
            ((method - MethodFogParamsBase) & 3u) == 0)
        {
            _fogParams[(method - MethodFogParamsBase) / 4] = BitConverter.UInt32BitsToSingle(parameter);
        }

        if (method >= MethodSpecularFogFactorBase && method <= MethodSpecularFogFactorEnd &&
            ((method - MethodSpecularFogFactorBase) & 3u) == 0)
        {
            _combiner.SpecFogFactor[(method - MethodSpecularFogFactorBase) / 4] = parameter;
        }

        if (method >= MethodCombinerColorOcwBase && method <= MethodCombinerColorOcwEnd &&
            ((method - MethodCombinerColorOcwBase) & 3u) == 0)
        {
            _combiner.RgbOutputs[(method - MethodCombinerColorOcwBase) / 4] = parameter;
        }

        if (method == MethodCombinerControl)
        {
            _combiner.Control = parameter;
        }

        if (method == MethodShaderStageProgram)
        {
            _combiner.StageProgram = parameter;
        }

        if (method == MethodDotRgbMapping)
        {
            _combiner.DotRgbMapping = parameter & 0xFFFu;
        }

        if (method == MethodShaderOtherStageInput)
        {
            _combiner.OtherStageInput =
                (_combiner.OtherStageInput & ~0xFFFF000u) | (parameter & 0xFFFF000u);
        }

        if (method == MethodShaderClipPlaneMode)
        {
            _shaderClipMode = parameter;
        }

        if (method == MethodShadowDepthFunc)
        {
            _shadowDepthFunc = parameter & 0x7u;
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
            _inlineValues[attribute][(part * 2) + 1] = (short)(parameter >> 16);
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

    private int ActiveTransformProgramStart()
    {
        return (int)Math.Min(_transformProgramStart, (uint)MaxTransformProgramLength);
    }

    private int ActiveTransformProgramLength(int start)
    {
        for (int i = start; i < MaxTransformProgramLength; i++)
        {
            if ((_transformProgram[i][3] & 1u) != 0)
            {
                return i - start + 1;
            }
        }

        return MaxTransformProgramLength - start;
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
            WriteGpu(target, value);
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

        bool backendClearColor = false;
        bool backendClearZeta = false;

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
                if (pitch != 0 && SurfaceFitsContext(_dmaColor, PresentedColorOffset, pitch, y1 - ymin + 1))
                {
                    uint pixel = _colorClearValue & (bytes switch
                    {
                        4 => 0xFFFFFFFFu,
                        2 => 0xFFFFu,
                        _ => 0xFFu
                    });
                    FillSurface(SurfaceBase(_dmaColor) + PresentedColorOffset, pitch, bytes, xmin, ymin, x1, y1,
                        pixel);
                    backendClearColor = true;
                }
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
                if (pitch != 0 && SurfaceFitsContext(_dmaZeta, _surfaceZetaOffset, pitch, y1 - ymin + 1))
                {
                    uint pixel = _zetaClearValue & (bytes == 4 ? 0xFFFFFFFFu : 0xFFFFu);
                    FillSurface(SurfaceBase(_dmaZeta) + _surfaceZetaOffset, pitch, bytes, xmin, ymin, x1, y1, pixel);
                    backendClearZeta = true;
                }
            }
        }

        if (Backend is not null && (backendClearColor || backendClearZeta))
        {
            uint zetaBytesBackend = ((_surfaceFormat >> 4) & 0xFu) switch
            {
                ZetaFormatZ16 => 2u,
                ZetaFormatZ24S8 => 4u,
                _ => 0u,
            };
            Backend.Clear(new PgraphClearInfo(
                backendClearColor, backendClearZeta,
                xmin, ymin, x1, y1,
                _surfaceClipH & 0xFFFFu, _surfaceClipV & 0xFFFFu,
                _surfaceClipH >> 16, _surfaceClipV >> 16,
                _colorClearValue, _zetaClearValue,
                ColorBytesPerPixel(_surfaceFormat & 0xFu), zetaBytesBackend,
                _zFormatFloat));
        }
    }

    private void FillSurface(uint offset, uint pitch, uint bytes, uint xmin, uint ymin, uint xmax, uint ymax,
        uint pixel)
    {
        using GpuWriteBatch batch = new(_memoryFast);
        if (_memory is null || pitch == 0)
        {
            return;
        }

        for (uint y = ymin; y <= ymax; y++)
        {
            uint row = offset + (y * pitch);
            for (uint x = xmin; x <= xmax; x++)
            {
                uint address = row + (x * bytes);
                if (address + bytes > RamSize)
                {
                    return;
                }

                if (bytes == 4)
                {
                    WriteGpu(address, pixel);
                }
                else
                {
                    uint current = ReadGpu(address & ~3u);
                    uint shift = (address & 3u) * 8;
                    uint mask = bytes == 2 ? 0xFFFFu << (int)shift : 0xFFu << (int)shift;
                    WriteGpu(address & ~3u, (current & ~mask) | ((pixel << (int)shift) & mask));
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

    private static int MapFogMode(uint parameter, int current)
    {
        return parameter switch
        {
            0x2601u => 0,
            0x0800u => 1,
            0x0801u => 3,
            0x0802u => 5,
            0x0803u => 7,
            0x0804u => 4,
            _ => current,
        };
    }

    private void WriteBumpMatrix(int slot, uint register, uint parameter)
    {
        if (slot < 1)
        {
            return;
        }

        // Method order within a slot is 00,01,11,10 (the reference swizzle);
        // stored canonically as m00,m01,m10,m11 for the mat2 multiply.
        int entry = register switch
        {
            TextureBumpMatBase => 0,
            0x2Cu => 1,
            0x30u => 3,
            _ => 2,
        };
        _bumpMatrices[slot - 1][entry] = BitConverter.UInt32BitsToSingle(parameter);
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

    /// <summary>
    /// Bit-exact specialization of the blend configuration the goldens run
    /// for ~all blended fragments (SrcAlpha / InvSrcAlpha / ADD, measured:
    /// 211,696,842 of 211,696,978 blended fragments on the menu 1B):
    /// fs = (src.A, src.A, src.A, src.A), fd = (1 - src.A, ...),
    /// result = src * fs + dst * fd, clamped per channel. The generic path
    /// computes the identical operations (one subtraction reused, same
    /// multiply/add order, same clamps); the engagement is per-fill
    /// (<see cref="PrepareFragmentConstants"/>) and pinned by the goldens
    /// plus the blend tests.
    /// </summary>
    private static (float R, float G, float B, float A) BlendSrcAlphaAdd(
        (float R, float G, float B, float A) src,
        (float R, float G, float B, float A) dst)
    {
        float sa = src.A;
        float ia = 1f - sa;
        return (
            Math.Clamp((src.R * sa) + (dst.R * ia), 0f, 1f),
            Math.Clamp((src.G * sa) + (dst.G * ia), 0f, 1f),
            Math.Clamp((src.B * sa) + (dst.B * ia), 0f, 1f),
            Math.Clamp((src.A * sa) + (dst.A * ia), 0f, 1f));
    }

    private static uint PackPixel(uint bytes, float r, float g, float b, float a)
    {
        return bytes == 4
            ? ((uint)((Math.Clamp(a, 0f, 1f) * 255f) + 0.5f) << 24) |
              ((uint)((Math.Clamp(r, 0f, 1f) * 255f) + 0.5f) << 16) |
              ((uint)((Math.Clamp(g, 0f, 1f) * 255f) + 0.5f) << 8) |
              (uint)((Math.Clamp(b, 0f, 1f) * 255f) + 0.5f)
            : ((uint)((Math.Clamp(r, 0f, 1f) * 31f) + 0.5f) << 11) |
              ((uint)((Math.Clamp(g, 0f, 1f) * 63f) + 0.5f) << 5) |
              (uint)((Math.Clamp(b, 0f, 1f) * 31f) + 0.5f);
    }

    private static float NaNToOne(float value)
    {
        return float.IsNaN(value) ? 1f : value;
    }

    /// <summary>
    /// O2 fast-lane 32-bit read: a direct pinned-RAM word when the adapter
    /// supports it, otherwise the exact <see cref="IGpuMemory"/> path.
    /// </summary>
    private uint ReadGpu(uint address) =>
        _memoryFast is not null && _memoryFast.TryReadPhysical(address, out uint value)
            ? value
            : _memory!.ReadPhysical(address);

    /// <summary>
    /// O2 fast-lane 32-bit write: direct inside an open draw batch, otherwise
    /// the exact adapter path (suppression scope, decode drop, bus watch).
    /// </summary>
    private void WriteGpu(uint address, uint value)
    {
        if (_memoryFast is null || !_memoryFast.TryWritePhysical(address, value))
        {
            _memory!.WritePhysical(address, value);
        }
    }

    /// <summary>
    /// Draw-wide GPU write batch (rasterizer O2): opens the writer-suppression
    /// scope and resets the adapter's decode-page memo for the whole fill.
    /// </summary>
    private readonly ref struct GpuWriteBatch
    {
        private readonly IGpuMemoryFast? _fast;

        public GpuWriteBatch(IGpuMemoryFast? fast)
        {
            _fast = fast;
            fast?.BeginGpuBatch();
        }

        public void Dispose() => _fast?.EndGpuBatch();
    }

    private void WritePixel(uint address, uint bytes, uint pixel)
    {
        if (_memory is null || address + bytes > RamSize)
        {
            return;
        }

        if (bytes == 4)
        {
            WriteGpu(address, pixel);
            return;
        }

        uint aligned = address & ~3u;
        uint shift = (address & 3u) * 8;
        uint current = ReadGpu(aligned);
        uint mask = bytes == 2 ? 0xFFFFu << (int)shift : 0xFFu << (int)shift;
        WriteGpu(aligned, (current & ~mask) | ((pixel << (int)shift) & mask));
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
            EnterTextureState(false, -1, 0);
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
                    ax, ay, 0f, 1f, ar, ag, ab, aa, 0f, 0f,
                    bx, by, 0f, 1f, br, bg, bb, ba, 0f, 0f,
                    cx, cy, 0f, 1f, cr, cg, cb, ca, 0f, 0f,
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

                return Math.Clamp(
                    NaNToOne(BitConverter.Int32BitsToSingle((int)_inlineWords[(int)baseWord + component])), 0f, 1f);
            }
        }
        finally
        {
            _inlineWords.Clear();
        }
    }

    private void FlushDraw()
    {
        // One draw command: every triangle below shares the live PGRAPH state
        // (methods are processed sequentially and the primitive buffer is
        // closed before the next one), so a backend may batch the whole run
        // into one host submission. The boundary is server-side bookkeeping
        // only; the software rasterizer is untouched.
        IPgraphBackend? backend = Backend;
        if (backend is null)
        {
            FlushDrawCommand();
            return;
        }

        backend.BeginDrawBatch();
        try
        {
            FlushDrawCommand();
        }
        finally
        {
            backend.EndDrawBatch();
        }
    }

    private void FlushDrawCommand()
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

    /// <summary>
    /// Transformed vertex for the combiner path: screen-space position, diffuse
    /// (oD0), specular (oD1), four texture coordinate quads (oT0-oT3; xyz kept
    /// for the dot/projective/reflection texture modes, w kept for projective
    /// division and the reflection-spec eye vector), and the per-vertex fog
    /// factor computed from oFog.x (or the fixed-function GEN_MODE distance
    /// when the program leaves oFog unwritten) through the fog-mode equation.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct PshVertex
    {
        public PshVertex(
            float x, float y, float z, float w,
            float diffuseR, float diffuseG, float diffuseB, float diffuseA,
            float specR, float specG, float specB, float specA,
            float u0, float v0, float s0,
            float u1, float v1, float s1,
            float u2, float v2, float s2,
            float u3, float v3, float s3,
            float t0W, float t1W, float t2W, float t3W,
            float fog)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
            DiffuseR = diffuseR;
            DiffuseG = diffuseG;
            DiffuseB = diffuseB;
            DiffuseA = diffuseA;
            SpecR = specR;
            SpecG = specG;
            SpecB = specB;
            SpecA = specA;
            U0 = u0;
            V0 = v0;
            S0 = s0;
            U1 = u1;
            V1 = v1;
            S1 = s1;
            U2 = u2;
            V2 = v2;
            S2 = s2;
            U3 = u3;
            V3 = v3;
            S3 = s3;
            T0W = t0W;
            T1W = t1W;
            T2W = t2W;
            T3W = t3W;
            Fog = fog;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public float W { get; }

        public float DiffuseR { get; }

        public float DiffuseG { get; }

        public float DiffuseB { get; }

        public float DiffuseA { get; }

        public float SpecR { get; }

        public float SpecG { get; }

        public float SpecB { get; }

        public float SpecA { get; }

        public float U0 { get; }

        public float V0 { get; }

        public float S0 { get; }

        public float U1 { get; }

        public float V1 { get; }

        public float S1 { get; }

        public float U2 { get; }

        public float V2 { get; }

        public float S2 { get; }

        public float U3 { get; }

        public float V3 { get; }

        public float S3 { get; }

        public float T0W { get; }

        public float T1W { get; }

        public float T2W { get; }

        public float T3W { get; }

        public float Fog { get; }
    }

    private void FlushProgramDraw(VertexFetchMode mode)
    {
        // Transform execution mode FIXED (0) runs the fixed-function vertex
        // stage instead of a vertex program: the guest uploads matrices into
        // the XF constant context instead of loading a program, so no program
        // needs to be resident (reference pgraph.c/glsl/vsh.c). Opt-in
        // (FixedFunctionTransform): mode-0 draws used to run the resident
        // program, so enabling this changes what those draws render.
        bool fixedFunction = FixedFunctionTransform && _transformExecutionMode == TransformModeFixed;
        if (_memory is null || (!fixedFunction && TransformProgramLoadDebug == 0))
        {
            SkippedDraws++;
            return;
        }

        int programStart = ActiveTransformProgramStart();
        int programLength = ActiveTransformProgramLength(programStart);
        if (!fixedFunction && programLength == 0)
        {
            SkippedDraws++;
            return;
        }

        uint[][] program = Array.Empty<uint[]>();
        if (!fixedFunction)
        {
            program = new uint[programLength][];
            Array.Copy(_transformProgram, programStart, program, 0, programLength);
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

        var transformed = new Dictionary<int, PshVertex>();

        uint[] elements = mode == VertexFetchMode.Elements ? _inlineElements.ToArray() : Array.Empty<uint>();
        DrawsExecuted++;
        bool combinerActive = _combiner.FinalEnabled;
        EnterTextureState(combinerActive, programStart, programLength);
        float[][] combinerConsts =
            combinerActive ? PshCombiner.BuildConsts(_combiner) : Array.Empty<float[]>();
        if (combinerActive)
        {
            CombinerDraws++;
            _combinerFastKind = _combiner.GetSpecializedKind();
            _combiner.RebuildPlan(combinerConsts);
            if (RasterTimingEnabled)
            {
                // Diagnostic: capture the distinct combiner configuration.
                var cfg = new System.Text.StringBuilder();
                cfg.Append(CultureInfo.InvariantCulture,
                    $"stages={_combiner.StageCount} c={_combiner.Control:X8} f0={_combiner.Final0:X8} f1={_combiner.Final1:X8}");
                for (int i = 0; i < _combiner.StageCount; i++)
                {
                    cfg.Append(CultureInfo.InvariantCulture,
                        $" ri{i}={_combiner.RgbInputs[i]:X8} ai{i}={_combiner.AlphaInputs[i]:X8}" +
                        $" ro{i}={_combiner.RgbOutputs[i]:X8} ao{i}={_combiner.AlphaOutputs[i]:X8}");
                }

                _diagCombinerConfig = cfg.ToString();
            }
        }
        else
        {
            _combinerFastKind = 0;
        }

        if (_transformExecutionMode != TransformModeProgrammable)
        {
            NonProgrammableModeDraws++;
            if (fixedFunction)
            {
                FixedFunctionDraws++;
                if (_fixedFunction.Lighting)
                {
                    FixedFunctionLitDraws++;
                    if (_fixedFunction.LightEnableMask != 0)
                    {
                        FixedFunctionLightedDraws++;
                    }
                }
            }
        }

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

        PshVertex Vertex(uint element)
        {
            int index = (int)element;
            if (transformed.TryGetValue(index, out PshVertex cached))
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
            }

            bool fogWritten;
            if (fixedFunction)
            {
                // The fixed-function stage owns the whole vertex, including
                // the eye-space fog distance (oFog.x), so it is always written.
                FixedFunctionVertex.Execute(
                    _fixedFunction, constants, inputs, outputs, _fogEnable, (int)_fogGenMode);
                fogWritten = true;
            }
            else
            {
                VshProgram.Execute(program, programLength, constants, inputs, outputs, out fogWritten);
            }

            // Device-depth normalization (xemu clipRange.y): Xbox shaders emit
            // screen-space Z in depth-buffer units (the driver bakes the
            // zeta-format scale into the viewport scale and shader constants,
            // e.g. C0.z = 16777215 for Z24S8), so divide by the current
            // surface zeta maximum to recover the [0,1] range the fragment
            // depth clamp and compare below operate in. Matches xemu's
            // vertex epilogue (oPos.z / clipRange.y) and fragment store
            // (zvalue / clipRange.y); with CONTROL0 Z_FORMAT the range is the
            // float-depth maximum (F16_MAX/F24_MAX per xemu util.h).
            outputs[0][2] /= ZetaDepthRange((_surfaceFormat >> 4) & 0xFu, _zFormatFloat);

            // Fog distance: a program-written oFog.x wins (reference
            // programmable path). When the program leaves oFog unwritten,
            // fall back to the fixed-function GEN_MODE distance (reference
            // vsh-ff path): FOG_X from the FOG_COORD attribute (v5.x),
            // SPEC_ALPHA from the specular attribute alpha (v4.a, equal to
            // the reference oD1.a in both lighting branches), RADIAL/PLANAR
            // from the object-space position (v0.xyz; the reference
            // eye-space product is unavailable without a fixed-function
            // transform unit), PLANAR against constant c57 (XFCTX_FOG).
            float fogDistance = outputs[5][0];
            if (_fogEnable && !fogWritten)
            {
                fogDistance = VshProgram.FogGenDistance(
                    (int)_fogGenMode,
                    inputs[5][0],
                    inputs[0][0],
                    inputs[0][1],
                    inputs[0][2],
                    inputs[4][3],
                    constants[XfConstantFogPlane]);
            }

            float fogFactor = _fogEnable
                ? VshProgram.FogFactor(_fogMode, _fogParams[0], _fogParams[1], fogDistance)
                : 1f;
            var vertex = new PshVertex(
                outputs[0][0], outputs[0][1], outputs[0][2], outputs[0][3],
                NaNToOne(outputs[3][0]), NaNToOne(outputs[3][1]), NaNToOne(outputs[3][2]), NaNToOne(outputs[3][3]),
                NaNToOne(outputs[4][0]), NaNToOne(outputs[4][1]), NaNToOne(outputs[4][2]), NaNToOne(outputs[4][3]),
                outputs[9][0], outputs[9][1], outputs[9][2],
                outputs[10][0], outputs[10][1], outputs[10][2],
                outputs[11][0], outputs[11][1], outputs[11][2],
                outputs[12][0], outputs[12][1], outputs[12][2],
                outputs[9][3], outputs[10][3], outputs[11][3], outputs[12][3],
                fogFactor);
            transformed[index] = vertex;
            return vertex;
        }

        void Triangle(uint a, uint b, uint c)
        {
            PshVertex va = Vertex(a);
            PshVertex vb = Vertex(b);
            PshVertex vc = Vertex(c);
            if (combinerActive)
            {
                FillTrianglePsh(va, vb, vc, clipX, clipY, clipW, clipH, bytes, pitch, combinerConsts);
                return;
            }

            FillTriangle(
                va.X, va.Y, va.Z, va.W, va.DiffuseR, va.DiffuseG, va.DiffuseB, va.DiffuseA, va.U0, va.V0,
                vb.X, vb.Y, vb.Z, vb.W, vb.DiffuseR, vb.DiffuseG, vb.DiffuseB, vb.DiffuseA, vb.U0, vb.V0,
                vc.X, vc.Y, vc.Z, vc.W, vc.DiffuseR, vc.DiffuseG, vc.DiffuseB, vc.DiffuseA, vc.U0, vc.V0,
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
        uint address = _vertexArrayOffset[slot];
        if (stride != 0)
        {
            address += (uint)(index * stride);
        }

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
                    destination[i] = BitConverter.Int32BitsToSingle((int)ReadGpu(address + (uint)(i * 4)));
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
                    int packed = (int)ReadGpu(address);
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
        return TrySampleTexture(0, u, v, out r, out g, out b, out a);
    }

    private bool TrySampleTexture(int slot, float u, float v, out float r, out float g, out float b, out float a)
    {
        r = 1f;
        g = 1f;
        b = 1f;
        a = 1f;
        if (_memory is null || (_textureControl0[slot] & TextureControl0Enable) == 0)
        {
            TextureSampleDisabled++;
            return false;
        }

        ref readonly SamplerPlan plan = ref _samplerPlans[slot];
        uint color = plan.Color;
        _textureFormatAttempts[color]++;
        if (plan.Width <= 0 || plan.Height <= 0)
        {
            TextureSampleBadSize++;
            return false;
        }

        if (plan.Bilinear)
        {
            return SampleBilinear(in plan, u, v, out r, out g, out b, out a);
        }

        if (plan.FastKind != 0)
        {
            return SampleFast(in plan, u, v, out r, out g, out b, out a);
        }

        // Linear (LU_IMAGE_*) formats address texels directly: the guest supplies
        // texel-unit coordinates and the reference shader normalizes by the
        // texture size (psh.c rect_tex norm). Swizzled (SZ_*) formats use
        // normalized coordinates, as on the hardware.
        int rawX = (int)MathF.Floor(plan.Linear ? u : u * plan.Width);
        int rawY = (int)MathF.Floor(plan.Linear ? v : v * plan.Height);
        if (plan.UAddr == (int)TextureAddressBorder || plan.VAddr == (int)TextureAddressBorder)
        {
            // A coordinate outside [0,size) on a BORDER axis returns the
            // NV097_SET_TEXTURE_BORDER_COLOR texel (xemu GL_CLAMP_TO_BORDER).
            bool outU = plan.UAddr == (int)TextureAddressBorder && (rawX < 0 || rawX >= plan.Width);
            bool outV = plan.VAddr == (int)TextureAddressBorder && (rawY < 0 || rawY >= plan.Height);
            if (outU || outV)
            {
                DecodeBorderColor(_textureBorderColor[slot], out r, out g, out b, out a);
                TrackTextureCoords(rawX, rawY);
                return CountSample(_textureOffset[slot], _textureBorderColor[slot], r, g, b);
            }
        }

        int x = WrapCoordinatePlanned(rawX, plan.Width, plan.WrapModeU, plan.WrapMaskU, plan.MirrorMaskU);
        int y = WrapCoordinatePlanned(rawY, plan.Height, plan.WrapModeV, plan.WrapMaskV, plan.MirrorMaskV);
        TrackTextureCoords(x, y);

        uint bytesPerTexel = plan.BytesPerTexel;
        if (bytesPerTexel == 0)
        {
            _textureFormatMisses[color]++;
            _textureStateMisses[_currentTextureState]++;
            return false;
        }

        uint address = plan.Offset;
        if (plan.Linear)
        {
            address += (uint)((y * plan.Pitch) + (x * (int)bytesPerTexel));
        }
        else if (plan.MortonMasks)
        {
            // O4: the plan masks are the standard alternating Morton masks
            // (POT dimensions), so the loop-based InterleaveBits is the exact
            // bit-spread below - masked to the same bit positions.
            uint swizzled = (SpreadMortonBits((uint)x) & plan.SwizzleMaskX)
                          | ((SpreadMortonBits((uint)y) << 1) & plan.SwizzleMaskY);
            address += swizzled * bytesPerTexel;
        }
        else
        {
            address += (InterleaveBits((uint)x, plan.SwizzleMaskX) |
                        InterleaveBits((uint)y, plan.SwizzleMaskY)) * bytesPerTexel;
        }

        uint texel = bytesPerTexel == 2
            ? ReadGpu(address) & 0xFFFFu
            : ReadGpu(address);
        DecodeTextureTexel(color, texel, out r, out g, out b, out a);
        return CountSample(address, texel, r, g, b);
    }

    /// <summary>
    /// Rasterizer slice: specialized sampler for the two per-fill shapes the
    /// titles actually run (linear 4-byte and Morton-swizzled 4-byte texels,
    /// clamp/mirror/wrap folds only, no border axis). Every step is the exact
    /// expression the generic path uses - the width/height scale, floor,
    /// coordinate fold, address arithmetic and decode - so results and
    /// counters are identical; only loop-invariant tests (border axis, bytes
    /// per texel, layout, wrap switch) are gone.
    /// </summary>
    private bool SampleFast(ref readonly SamplerPlan plan, float u, float v, out float r, out float g, out float b,
        out float a)
    {
        int rawX = (int)MathF.Floor(plan.Linear ? u : u * plan.Width);
        int rawY = (int)MathF.Floor(plan.Linear ? v : v * plan.Height);
        int x = WrapFast(rawX, plan.Width, plan.FastWrapU, plan.WrapMaskU, plan.MirrorMaskU);
        int y = WrapFast(rawY, plan.Height, plan.FastWrapV, plan.WrapMaskV, plan.MirrorMaskV);
        uint address;
        if (plan.Linear)
        {
            address = plan.Offset + (uint)((y * plan.Pitch) + (x * plan.FastBytes));
        }
        else if (plan.MortonMasks)
        {
            uint swizzled = (SpreadMortonBits((uint)x) & plan.SwizzleMaskX)
                          | ((SpreadMortonBits((uint)y) << 1) & plan.SwizzleMaskY);
            address = plan.Offset + (swizzled * (uint)plan.FastBytes);
        }
        else
        {
            address = plan.Offset + ((InterleaveBits((uint)x, plan.SwizzleMaskX) |
                                      InterleaveBits((uint)y, plan.SwizzleMaskY)) * (uint)plan.FastBytes);
        }

        uint texel = plan.FastBytes == 4 ? ReadGpu(address) : ReadGpu(address) & 0xFFFFu;
        DecodeTextureTexel(plan.Color, texel, out r, out g, out b, out a);
        TrackTextureCoords(x, y);
        return CountSample(address, texel, r, g, b);
    }

    /// <summary>
    /// TEXFILTER tent filter (opt-in <see cref="TextureFilterRegisters"/>):
    /// GL_LINEAR at LOD 0. The sample position is the texel-center-offset
    /// coordinate GL uses (<c>s*size - 0.5</c>), and each of the four taps is
    /// wrapped independently - the address modes apply per tap, exactly as GL
    /// wraps a bilinear footprint. One counter sample is recorded per
    /// fragment (the base tap), not four.
    /// </summary>
    private bool SampleBilinear(ref readonly SamplerPlan plan, float u, float v, out float r, out float g, out float b,
        out float a)
    {
        r = 1f;
        g = 1f;
        b = 1f;
        a = 1f;
        float fx = (plan.Linear ? u : u * plan.Width) - 0.5f;
        float fy = (plan.Linear ? v : v * plan.Height) - 0.5f;
        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        float wx = fx - x0;
        float wy = fy - y0;
        if (!TryReadPlannedTexel(in plan, x0, y0, out float r00, out float g00, out float b00, out float a00,
                out uint baseAddress, out uint baseTexel) ||
            !TryReadPlannedTexel(in plan, x0 + 1, y0, out float r10, out float g10, out float b10, out float a10,
                out _, out _) ||
            !TryReadPlannedTexel(in plan, x0, y0 + 1, out float r01, out float g01, out float b01, out float a01,
                out _, out _) ||
            !TryReadPlannedTexel(in plan, x0 + 1, y0 + 1, out float r11, out float g11, out float b11,
                out float a11, out _, out _))
        {
            _textureFormatMisses[plan.Color]++;
            _textureStateMisses[_currentTextureState]++;
            return false;
        }

        float w00 = (1f - wx) * (1f - wy);
        float w10 = wx * (1f - wy);
        float w01 = (1f - wx) * wy;
        float w11 = wx * wy;
        r = (r00 * w00) + (r10 * w10) + (r01 * w01) + (r11 * w11);
        g = (g00 * w00) + (g10 * w10) + (g01 * w01) + (g11 * w11);
        b = (b00 * w00) + (b10 * w10) + (b01 * w01) + (b11 * w11);
        a = (a00 * w00) + (a10 * w10) + (a01 * w01) + (a11 * w11);
        TrackTextureCoords(x0, y0);
        return CountSample(baseAddress, baseTexel, r, g, b);
    }

    /// <summary>
    /// Reads and decodes one texel of a plan at an unwrapped coordinate: the
    /// per-tap half of <see cref="SampleBilinear"/>, sharing the generic
    /// sampler's border, wrap, layout and decode rules.
    /// </summary>
    private bool TryReadPlannedTexel(
        ref readonly SamplerPlan plan,
        int rawX,
        int rawY,
        out float r,
        out float g,
        out float b,
        out float a,
        out uint address,
        out uint texel)
    {
        r = 1f;
        g = 1f;
        b = 1f;
        a = 1f;
        address = plan.Offset;
        texel = 0u;
        if (plan.UAddr == (int)TextureAddressBorder || plan.VAddr == (int)TextureAddressBorder)
        {
            bool outU = plan.UAddr == (int)TextureAddressBorder && (rawX < 0 || rawX >= plan.Width);
            bool outV = plan.VAddr == (int)TextureAddressBorder && (rawY < 0 || rawY >= plan.Height);
            if (outU || outV)
            {
                texel = _textureBorderColor[plan.Slot];
                DecodeBorderColor(texel, out r, out g, out b, out a);
                return true;
            }
        }

        int x = WrapCoordinatePlanned(rawX, plan.Width, plan.WrapModeU, plan.WrapMaskU, plan.MirrorMaskU);
        int y = WrapCoordinatePlanned(rawY, plan.Height, plan.WrapModeV, plan.WrapMaskV, plan.MirrorMaskV);
        uint bytesPerTexel = plan.BytesPerTexel;
        if (bytesPerTexel == 0)
        {
            return false;
        }

        uint bytesAddress = plan.Offset;
        if (plan.Linear)
        {
            bytesAddress += (uint)((y * plan.Pitch) + (x * (int)bytesPerTexel));
        }
        else if (plan.MortonMasks)
        {
            uint swizzled = (SpreadMortonBits((uint)x) & plan.SwizzleMaskX)
                          | ((SpreadMortonBits((uint)y) << 1) & plan.SwizzleMaskY);
            bytesAddress += swizzled * bytesPerTexel;
        }
        else
        {
            bytesAddress += (InterleaveBits((uint)x, plan.SwizzleMaskX) |
                             InterleaveBits((uint)y, plan.SwizzleMaskY)) * bytesPerTexel;
        }

        texel = bytesPerTexel == 2 ? ReadGpu(bytesAddress) & 0xFFFFu : ReadGpu(bytesAddress);
        DecodeTextureTexel(plan.Color, texel, out r, out g, out b, out a);
        address = bytesAddress;
        return true;
    }

    /// <summary>
    /// One tap of the compiled fills' bilinear sampler: the per-tap half of
    /// <see cref="SampleBilinear"/> transcribed operation-for-operation (the
    /// read and decode half of <see cref="TryReadPlannedTexel"/>, with the
    /// per-fill direct-RAM lane and per-fill decode branch). Only called for
    /// plans whose <see cref="SamplerPlan.FastKind"/> is non-zero, so the texel
    /// is 2 or 4 bytes and the tap can never fail.
    /// </summary>
    /// <param name="ramBase">Reference to the RAM array data (unused when
    /// <paramref name="directTexel"/> is false).</param>
    /// <param name="directTexel">The fill proved every tap address is in RAM.</param>
    /// <param name="rawX">Unwrapped (pre-fold) texel x coordinate.</param>
    /// <param name="rawY">Unwrapped (pre-fold) texel y coordinate.</param>
    /// <param name="plan">The stage's sampler plan.</param>
    /// <param name="r">Decoded red.</param>
    /// <param name="g">Decoded green.</param>
    /// <param name="b">Decoded blue.</param>
    /// <param name="a">Decoded alpha.</param>
    /// <param name="address">Physical address of the (folded) tap.</param>
    /// <param name="texel">Raw texel word of the (folded) tap.</param>
    private void ReadTapFast(
        ref byte ramBase,
        bool directTexel,
        int rawX,
        int rawY,
        ref readonly SamplerPlan plan,
        out float r,
        out float g,
        out float b,
        out float a,
        out uint address,
        out uint texel)
    {
        int x = WrapFast(rawX, plan.Width, plan.FastWrapU, plan.WrapMaskU, plan.MirrorMaskU);
        int y = WrapFast(rawY, plan.Height, plan.FastWrapV, plan.WrapMaskV, plan.MirrorMaskV);
        if (plan.Linear)
        {
            address = plan.Offset + (uint)((y * plan.Pitch) + (x * plan.FastBytes));
        }
        else if (plan.MortonMasks)
        {
            uint swizzled = (SpreadMortonBits((uint)x) & plan.SwizzleMaskX)
                          | ((SpreadMortonBits((uint)y) << 1) & plan.SwizzleMaskY);
            address = plan.Offset + (swizzled * (uint)plan.FastBytes);
        }
        else
        {
            address = plan.Offset + ((InterleaveBits((uint)x, plan.SwizzleMaskX) |
                                      InterleaveBits((uint)y, plan.SwizzleMaskY)) * (uint)plan.FastBytes);
        }

        if (directTexel)
        {
            texel = BinaryPrimitives.ReadUInt32LittleEndian(
                MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref ramBase, (nint)address), 4));
            if (plan.FastBytes != 4)
            {
                texel &= 0xFFFFu;
            }
        }
        else
        {
            texel = plan.FastBytes == 4 ? ReadGpu(address) : ReadGpu(address) & 0xFFFFu;
        }

        if (plan.DecodeKind == 0)
        {
            a = Unit255[(int)((texel >> 24) & 0xFFu)];
            r = Unit255[(int)((texel >> 16) & 0xFFu)];
            g = Unit255[(int)((texel >> 8) & 0xFFu)];
            b = Unit255[(int)(texel & 0xFFu)];
        }
        else if (plan.DecodeKind == 1)
        {
            r = Unit15[(int)((texel >> 8) & 0xFu)];
            g = Unit15[(int)((texel >> 4) & 0xFu)];
            b = Unit15[(int)(texel & 0xFu)];
            a = Unit15[(int)((texel >> 12) & 0xFu)];
        }
        else
        {
            DecodeTextureTexel(plan.Color, texel, out r, out g, out b, out a);
        }
    }

    /// <summary>
    /// Highest physical address a fast sampler plan can read, given the folds
    /// the plan allows: every accepted wrap kind keeps the folded coordinate
    /// inside <c>[0, Width)</c> / <c>[0, Height)</c>, and every swizzled
    /// coordinate bit lands inside the union of the two swizzle masks. Used by
    /// the compiled fills to hoist the RAM bounds test out of the pixel loop;
    /// a plan whose bound does not fit keeps the per-access adapter path.
    /// </summary>
    private static uint SamplerAddressBound(ref readonly SamplerPlan plan)
    {
        long bound;
        if (plan.Linear)
        {
            long width = plan.Width > 0 ? plan.Width : 1;
            long height = plan.Height > 0 ? plan.Height : 1;
            long pitch = plan.Pitch > 0 ? plan.Pitch : 0;
            bound = (long)plan.Offset + ((height - 1) * pitch) + ((width - 1) * plan.FastBytes);
        }
        else
        {
            bound = (long)plan.Offset + ((long)(plan.SwizzleMaskX | plan.SwizzleMaskY) * plan.FastBytes);
        }

        return bound is < 0 or > uint.MaxValue ? uint.MaxValue : (uint)bound;
    }

    /// <summary>
    /// Per-draw texture sampler descriptor: the per-sample decode of format,
    /// shape, wrap modes, pitch and swizzle masks is loop-invariant, so it is
    /// resolved once per draw in <see cref="BuildSamplerPlans"/> (called from
    /// <c>EnterTextureState</c> for both draw paths). Pure hoist: every value
    /// is the same expression the per-sample code used, so sampling results
    /// and counters are unchanged.
    /// </summary>
    private struct SamplerPlan
    {
        public uint Color;
        public bool Linear;
        public int Width;
        public int Height;
        public uint BytesPerTexel;
        public int Pitch;
        public int WrapModeU;
        public int WrapModeV;
        public int WrapMaskU;
        public int WrapMaskV;
        public int MirrorMaskU;
        public int MirrorMaskV;
        public int UAddr;
        public int VAddr;
        public uint Offset;
        public uint SwizzleMaskX;
        public uint SwizzleMaskY;
        public bool MortonMasks;

        /// <summary>
        /// Rasterizer slice: non-zero when the sampler can bypass the generic
        /// per-sample descriptor decode. 1 = linear 4-byte texels, 2 = swizzled
        /// 4-byte texels with Morton masks (both only for clamp/mirror/wrap
        /// modes that reduce to an integer fold and no border axis).
        /// </summary>
        public int FastKind;

        /// <summary>Texel size in bytes for the fast sampler (2 or 4).</summary>
        public int FastBytes;

        /// <summary>
        /// Per-fill decode shape: 0 = the <see cref="DecodeTextureTexel"/>
        /// default branch (A8R8G8B8), 1 = the A4R4G4B4 branch, 2 = any other
        /// color (the compiled loop calls the generic decode). Every value is
        /// the exact branch the generic switch takes for this color.
        /// </summary>
        public int DecodeKind;

        /// <summary>Fast fold per axis: 0 = clamp, 1 = mirror (POT), 2 = wrap (POT).</summary>
        public int FastWrapU;
        public int FastWrapV;

        /// <summary>
        /// Cube-map sampling descriptors (faces are stored contiguously, each a
        /// full mip chain rounded to 128 bytes). Pure functions of the per-draw
        /// texture registers, hoisted so the cube sampler does not rebuild the
        /// face stride and swizzle masks per sample.
        /// </summary>
        public int CubeWidth;
        public int CubeHeight;
        public uint CubeBytes;
        public uint CubeFaceStride;
        public uint CubeMaskX;
        public uint CubeMaskY;
        public bool CubeMorton;

        /// <summary>Texture stage this plan belongs to (border color lookup).</summary>
        public int Slot;

        /// <summary>
        /// TEXFILTER asks for a tent filter at LOD 0 (MIN/MAG 2 or 4, plus the
        /// tent minification variants). Only set when
        /// <see cref="TextureFilterRegisters"/> is on, so the legacy nearest
        /// sampler is byte-identical by construction.
        /// </summary>
        public bool Bilinear;

        /// <summary>
        /// TEXFILTER convolution kernel (bits 13-15): 0 none, 1 quincunx,
        /// 2 gaussian-3. Applied to PROJECT2D like xemu
        /// <c>apply_convolution_filter</c>: a 3x3 gaussian blur over nine
        /// textureProj taps.
        /// </summary>
        public int Convolution;
    }

    private void BuildSamplerPlans()
    {
        for (int slot = 0; slot < MaxTextureSlots; slot++)
        {
            ref SamplerPlan plan = ref _samplerPlans[slot];
            uint color = (_textureFormat[slot] >> 8) & 0x7Fu;
            plan.Color = color;
            plan.Slot = slot;
            bool linear = IsLinearTextureFormat(color);
            plan.Linear = linear;
            int width;
            int height;
            if (linear)
            {
                width = (int)(_textureImageRect[slot] >> 16);
                height = (int)(_textureImageRect[slot] & 0xFFFFu);
            }
            else
            {
                width = 1 << (int)((_textureFormat[slot] >> 20) & 0xFu);
                height = 1 << (int)((_textureFormat[slot] >> 24) & 0xFu);
            }

            plan.Width = width;
            plan.Height = height;
            uint bytesPerTexel = TextureFormatBytesPerTexel(color);
            plan.BytesPerTexel = bytesPerTexel;
            int pitch = (int)(_textureControl1[slot] >> 16);
            if (pitch <= 0)
            {
                pitch = (int)(width * bytesPerTexel);
            }

            plan.Pitch = pitch;
            int addressWord = (int)_textureAddress[slot];
            plan.UAddr = DecodeTextureAddressU(addressWord);
            plan.VAddr = DecodeTextureAddressV(addressWord);
            plan.WrapModeU = ToWrapMode(plan.UAddr);
            plan.WrapModeV = ToWrapMode(plan.VAddr);
            bool potU = width > 0 && (width & (width - 1)) == 0;
            bool potV = height > 0 && (height & (height - 1)) == 0;
            plan.WrapMaskU = potU ? width - 1 : -1;
            plan.WrapMaskV = potV ? height - 1 : -1;
            plan.MirrorMaskU = potU ? (width * 2) - 1 : -1;
            plan.MirrorMaskV = potV ? (height * 2) - 1 : -1;
            plan.Offset = _textureOffset[slot];
            if (width > 0 && height > 0)
            {
                ComputeSwizzleMasks(width, height, out uint maskX, out uint maskY);
                plan.SwizzleMaskX = maskX;
                plan.SwizzleMaskY = maskY;
                plan.MortonMasks = (maskX & 0xAAAAAAAAu) == 0 && (maskY & 0x55555555u) == 0;
            }
            else
            {
                plan.SwizzleMaskX = 0u;
                plan.SwizzleMaskY = 0u;
                plan.MortonMasks = false;
            }

            // Rasterizer slice: per-fill fast sampler eligibility. Only pure
            // integer folds qualify (clamp, or POT mirror/wrap) with 4-byte
            // texels and no border axis, so the fast path is a strict subset of
            // the generic semantics.
            plan.FastKind = 0;
            plan.FastBytes = 0;
            plan.FastWrapU = 0;
            plan.FastWrapV = 0;
            plan.DecodeKind = color switch
            {
                0x02u or 0x10u or 0x03u or 0x1Cu or 0x04u or 0x1Du or 0x05u or 0x11u or 0x07u or 0x1Eu
                    => color is 0x04u or 0x1Du ? 1 : 2,
                // Byte-order variants are 32-bit but not A8R8G8B8: they must take
                // the generic decode or their channels silently swap.
                0x3Au or 0x3Bu or 0x3Cu or 0x3Fu or 0x40u or 0x41u => 2,
                // G8B8/R8B8 are 16-bit two-channel words: generic decode too.
                0x17u or 0x28u or 0x29u => 2,
                _ => 0,
            };
            if (bytesPerTexel is 2 or 4 && width > 0 && height > 0 && !DiagnosticGenericSampler &&
                plan.UAddr != (int)TextureAddressBorder && plan.VAddr != (int)TextureAddressBorder)
            {
                int fastU = FastWrapKind(plan.WrapModeU, plan.WrapMaskU, plan.MirrorMaskU);
                int fastV = FastWrapKind(plan.WrapModeV, plan.WrapMaskV, plan.MirrorMaskV);
                if (fastU >= 0 && fastV >= 0)
                {
                    plan.FastKind = linear ? 1 : 2;
                    plan.FastBytes = (int)bytesPerTexel;
                    plan.FastWrapU = fastU;
                    plan.FastWrapV = fastV;
                }
            }

            // Cube maps are always swizzled, and xemu takes a swizzled
            // texture's extent from TEXFMT BASE_SIZE_U/V - TEXIMAGERECT is
            // only read for linear formats (pgraph_get_texture_shape). Using
            // IMAGE_RECT here samples a 512x512 grid (the guest's stale
            // default) over 256x256 faces.
            int cubeWidth = 1 << (int)((_textureFormat[slot] >> 20) & 0xFu);
            int cubeHeight = 1 << (int)((_textureFormat[slot] >> 24) & 0xFu);
            uint cubeBytes = color switch
            {
                0x06u => 4u,
                0x05u => 2u,
                0x3Au or 0x3Bu or 0x3Cu => 4u,
                _ => 0u,
            };
            plan.CubeWidth = cubeWidth;
            plan.CubeHeight = cubeHeight;
            plan.CubeBytes = cubeBytes;
            plan.CubeFaceStride = 0u;
            plan.CubeMaskX = 0u;
            plan.CubeMaskY = 0u;
            plan.CubeMorton = false;
            if (cubeBytes != 0 && cubeWidth > 0 && cubeHeight > 0)
            {
                plan.CubeFaceStride = CubeFaceStride(slot, cubeWidth, cubeHeight, cubeBytes);
                ComputeSwizzleMasks(cubeWidth, cubeHeight, out uint cubeMaskX, out uint cubeMaskY);
                plan.CubeMaskX = cubeMaskX;
                plan.CubeMaskY = cubeMaskY;
                plan.CubeMorton = (cubeMaskX & 0xAAAAAAAAu) == 0 && (cubeMaskY & 0x55555555u) == 0;
            }

            // TEXFILTER decode (opt-in): MIN in bits 16-21, MAG in bits 24-27,
            // convolution kernel in bits 13-15. Values follow nv2a_regs.h.
            // With level 0 only, every tent variant is the same bilinear tap:
            // the reference's GL filter map turns MIN 2/4/6/7 into GL_LINEAR
            // and 1/3/5 into GL_NEAREST (texture.c), i.e. exactly the mipmap
            // variants collapsed onto level 0.
            plan.Bilinear = false;
            plan.Convolution = 0;
            if (TextureFilterRegisters)
            {
                uint filter = _textureFilter[slot];
                uint min = (filter & TextureFilterMin) >> 16;
                uint mag = (filter & TextureFilterMag) >> 24;
                plan.Bilinear = min is 2u or 4u or 6u or 7u || mag is 2u or 4u;
                plan.Convolution = EffectiveConvolutionKernel(filter);
            }
        }
    }

    /// <summary>
    /// The TEXFILTER convolution kernel the sampler applies, following the
    /// reference: xemu <c>psh.c</c> only looks at the kernel field when MIN is
    /// <c>NV_PGRAPH_TEXFILTER0_MIN_CONVOLUTION_2D_LOD0</c> (7) and only
    /// QUINCUNX (1) / GAUSSIAN_3 (2) convolve; every other pairing is a plain
    /// sampler filter. The guest programs kernel bits next to an ordinary tent
    /// MIN (the dashboard's four slots are <c>2/2/1</c>), which the reference
    /// samples as a single bilinear tap, not as a 3x3 convolution.
    /// </summary>
    private static int EffectiveConvolutionKernel(uint filter)
    {
        uint min = (filter & TextureFilterMin) >> 16;
        uint kernel = (filter & TextureFilterKernel) >> 13;
        if (min != TextureFilterMinConvolution2DLod0)
        {
            return 0;
        }

        return kernel is TextureFilterKernelQuincunx or TextureFilterKernelGaussian3 ? (int)kernel : 0;
    }

    /// <summary>
    /// Folds an address mode into the fast-sampler kind: 0 clamps, 1 mirrors
    /// (power-of-two extent required), 2 wraps by mask (power-of-two extent
    /// required); -1 means the generic per-sample fold must be used.
    /// </summary>
    private static int FastWrapKind(int wrapMode, int wrapMask, int mirrorMask)
    {
        if (wrapMode == 2)
        {
            return 0;
        }

        if (wrapMode == 1)
        {
            return mirrorMask >= 0 ? 1 : -1;
        }

        return wrapMask >= 0 ? 2 : -1;
    }

    /// <summary>
    /// Fast per-axis fold for the specialized sampler. Bit-identical to
    /// <see cref="WrapCoordinatePlanned"/> for the three modes it accepts
    /// (clamp, POT mirror, POT wrap) and never called otherwise.
    /// </summary>
    private static int WrapFast(int value, int size, int mode, int wrapMask, int mirrorMask)
    {
        if (mode == 0)
        {
            if (value < 0)
            {
                return 0;
            }

            return value >= size ? size - 1 : value;
        }

        if (mode == 1)
        {
            value &= mirrorMask;
            return value < size ? value : (size * 2) - 1 - value;
        }

        return value & wrapMask;
    }

    /// <summary>
    /// Address-mode fold with the power-of-two fast path: for POT extents the
    /// modulo folds to a mask (identical results for every int, including
    /// negatives); non-POT extents keep the modulo.
    /// </summary>
    private static int WrapCoordinatePlanned(int value, int size, int mode, int wrapMask, int mirrorMask)
    {
        switch (mode)
        {
            case 0:
                if (wrapMask >= 0)
                {
                    value &= wrapMask;
                    return value;
                }

                value %= size;
                return value < 0 ? value + size : value;
            case 1:
                {
                    int period = size * 2;
                    if (mirrorMask >= 0)
                    {
                        value &= mirrorMask;
                    }
                    else
                    {
                        value %= period;
                        if (value < 0)
                        {
                            value += period;
                        }
                    }

                    return value < size ? value : period - 1 - value;
                }
            default:
                return Math.Clamp(value, 0, size - 1);
        }
    }

    /// <summary>
    /// Builds the Morton-order interleave masks for a texture shape (xemu
    /// <c>generate_swizzle_masks</c>): x bits and y bits take alternating
    /// positions in the texel index.
    /// </summary>
    private static void ComputeSwizzleMasks(int width, int height, out uint maskX, out uint maskY)
    {
        maskX = 0u;
        maskY = 0u;
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
        } while (!done);
    }

    /// <summary>
    /// Gets whether a texture color format uses linear (pitched) layout rather than
    /// swizzled layout. The linear formats are the pitch-addressed twins of the
    /// swizzled color formats (0x10/0x11/0x12/0x1C/0x1D/0x1E, plus the LU byte-order
    /// twins); the sampler walks them with the CONTROL1 pitch instead of
    /// <see cref="SwizzleOffset"/>.
    /// </summary>
    /// <param name="color">Texture color-format id (FORMAT bits 8-15).</param>
    /// <returns>True for the linear formats; false for swizzled formats.</returns>
    public static bool IsLinearTextureFormat(uint color)
    {
        return color is 0x10u or 0x11u or 0x12u or 0x1Cu or 0x1Du or 0x1Eu or 0x3Fu or 0x40u or 0x41u or 0x17u;
    }

    /// <summary>
    /// Gets the bytes per texel for a texture color format: 2 for the 16-bit
    /// formats (0x02-0x05, their linear twins and the G8B8/R8B8 pair), 4 for the
    /// 32-bit formats (0x06/0x07, their linear twins, and the byte-order variants
    /// 0x3A-0x3C / 0x3F-0x41), 0 for unsupported formats (the caller counts a
    /// format miss).
    /// </summary>
    /// <param name="color">Texture color-format id (FORMAT bits 8-15).</param>
    /// <returns>2, 4, or 0 when the format is not backed by the sampler.</returns>
    public static uint TextureFormatBytesPerTexel(uint color)
    {
        return color switch
        {
            0x02u or 0x03u or 0x04u or 0x05u or 0x10u or 0x11u or 0x1Cu or 0x1Du
                or 0x17u or 0x28u or 0x29u => 2u,
            0x06u or 0x07u or 0x12u or 0x1Eu => 4u,
            0x3Au or 0x3Bu or 0x3Cu or 0x3Fu or 0x40u or 0x41u => 4u,
            _ => 0u,
        };
    }

    /// <summary>
    /// Exact unit-conversion tables for the texel/pixel decoders: entry i is
    /// <c>i / divisor</c> evaluated with the same float division the per-pixel
    /// expressions used, so every decoded value is bit-identical while the
    /// samplers and the blend destination read avoid a per-channel divide.
    /// </summary>
    private static readonly float[] Unit31 = BuildUnitTable(32, 31f);
    private static readonly float[] Unit63 = BuildUnitTable(64, 63f);
    private static readonly float[] Unit15 = BuildUnitTable(16, 15f);
    private static readonly float[] Unit255 = BuildUnitTable(256, 255f);

    private static float[] BuildUnitTable(int count, float divisor)
    {
        var table = new float[count];
        for (int i = 0; i < count; i++)
        {
            table[i] = i / divisor;
        }

        return table;
    }

    /// <summary>
    /// Decodes a raw texel for the supported color formats. Bit layouts follow
    /// the Vulkan format mapping in xemu's renderer (A1R5G5B5, A4R4G4B4,
    /// R5G6B5, A8R8G8B8 and the X8 alpha-ignored variants); SZ and LU variants
    /// of a format share the layout. Channel values come from exact unit
    /// tables (<see cref="Unit255"/> etc.), so the results are bit-identical
    /// to the previous per-channel divisions.
    /// </summary>
    /// <remarks>
    /// The byte-order variants (0x3A-0x3C SZ, 0x3F-0x41 LU) are the same 32-bit
    /// words with the channels permuted, so they only change which byte each
    /// channel reads. They are enumerated in the newer xemu fork's
    /// <c>nv2a_regs.h</c> (0x3F = <c>LU_IMAGE_A8B8G8R8</c>), not in 0.8.137.
    /// </remarks>
    public static void DecodeTextureTexel(uint color, uint texel, out float r, out float g, out float b, out float a)
    {
        switch (color)
        {
            case 0x3Au:
            case 0x3Fu:
                // A8B8G8R8: A high byte, then B, G, R.
                r = Unit255[(int)(texel & 0xFFu)];
                g = Unit255[(int)((texel >> 8) & 0xFFu)];
                b = Unit255[(int)((texel >> 16) & 0xFFu)];
                a = Unit255[(int)((texel >> 24) & 0xFFu)];
                break;
            case 0x3Bu:
            case 0x40u:
                // B8G8R8A8: B high byte, then G, R, A.
                b = Unit255[(int)((texel >> 24) & 0xFFu)];
                g = Unit255[(int)((texel >> 16) & 0xFFu)];
                r = Unit255[(int)((texel >> 8) & 0xFFu)];
                a = Unit255[(int)(texel & 0xFFu)];
                break;
            case 0x3Cu:
            case 0x41u:
                // R8G8B8A8: R high byte, then G, B, A.
                r = Unit255[(int)((texel >> 24) & 0xFFu)];
                g = Unit255[(int)((texel >> 16) & 0xFFu)];
                b = Unit255[(int)((texel >> 8) & 0xFFu)];
                a = Unit255[(int)(texel & 0xFFu)];
                break;
            case 0x28u:
            case 0x17u:
                // G8B8 (SZ and LU twins): 16-bit word, B in the low byte, G in
                // the high byte. xemu samples it as GL_RG8 and swizzles the
                // channels (R, G, R, G), i.e. r/b = low byte, g/a = high byte.
                r = Unit255[(int)(texel & 0xFFu)];
                g = Unit255[(int)((texel >> 8) & 0xFFu)];
                b = r;
                a = g;
                break;
            case 0x29u:
                // R8B8: the same 16-bit word with the channel swizzle
                // (G, R, R, G) - r/b = high byte, g/a = low byte.
                r = Unit255[(int)((texel >> 8) & 0xFFu)];
                g = Unit255[(int)(texel & 0xFFu)];
                b = r;
                a = g;
                break;
            case 0x02u:
            case 0x10u:
                r = Unit31[(int)((texel >> 10) & 0x1Fu)];
                g = Unit31[(int)((texel >> 5) & 0x1Fu)];
                b = Unit31[(int)(texel & 0x1Fu)];
                a = (texel >> 15) & 1u;
                break;
            case 0x03u:
            case 0x1Cu:
                r = Unit31[(int)((texel >> 10) & 0x1Fu)];
                g = Unit31[(int)((texel >> 5) & 0x1Fu)];
                b = Unit31[(int)(texel & 0x1Fu)];
                a = 1f;
                break;
            case 0x04u:
            case 0x1Du:
                r = Unit15[(int)((texel >> 8) & 0xFu)];
                g = Unit15[(int)((texel >> 4) & 0xFu)];
                b = Unit15[(int)(texel & 0xFu)];
                a = Unit15[(int)((texel >> 12) & 0xFu)];
                break;
            case 0x05u:
            case 0x11u:
                r = Unit31[(int)((texel >> 11) & 0x1Fu)];
                g = Unit63[(int)((texel >> 5) & 0x3Fu)];
                b = Unit31[(int)(texel & 0x1Fu)];
                a = 1f;
                break;
            case 0x07u:
            case 0x1Eu:
                r = Unit255[(int)((texel >> 16) & 0xFFu)];
                g = Unit255[(int)((texel >> 8) & 0xFFu)];
                b = Unit255[(int)(texel & 0xFFu)];
                a = 1f;
                break;
            default:
                a = Unit255[(int)((texel >> 24) & 0xFFu)];
                r = Unit255[(int)((texel >> 16) & 0xFFu)];
                g = Unit255[(int)((texel >> 8) & 0xFFu)];
                b = Unit255[(int)(texel & 0xFFu)];
                break;
        }
    }

    /// <summary>
    /// Samples a stage's cube map (TEXFMT CUBEMAPENABLE) for a direction
    /// vector. Faces are stored contiguously in the guest's +X,-X,+Y,-Y,+Z,-Z
    /// order (xemu <c>generate_texture</c>), each face occupying the full mip
    /// chain rounded up to the 128-byte alignment. Level 0 is sampled; the
    /// per-face swizzle matches the 2D sampler. Faces always clamp at the
    /// edges (GL cube sampling has no wrap across faces).
    /// </summary>
    private bool TrySampleTextureCube(int slot, float x, float y, float z, out float r, out float g, out float b,
        out float a)
    {
        r = 1f;
        g = 1f;
        b = 1f;
        a = 1f;
        IGpuMemory? memory = _memory;
        if (memory is null || (_textureControl0[slot] & TextureControl0Enable) == 0)
        {
            return false;
        }

        ref readonly SamplerPlan plan = ref _samplerPlans[slot];
        int width = plan.CubeWidth;
        int height = plan.CubeHeight;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        uint color = (_textureFormat[slot] >> 8) & 0xFFu;
        uint bytes = plan.CubeBytes;
        if (bytes == 0)
        {
            return false;
        }

        int face = PshCombiner.SelectCubeFace(x, y, z, out float s, out float t);
        if (plan.Bilinear)
        {
            return SampleCubeBilinear(memory, in plan, color, bytes, face, s, t, out r, out g, out b, out a);
        }

        int tx = Math.Clamp((int)MathF.Floor(s * width), 0, width - 1);
        int ty = Math.Clamp((int)MathF.Floor(t * height), 0, height - 1);
        uint swizzled = plan.CubeMorton
            ? (SpreadMortonBits((uint)tx) & plan.CubeMaskX) | ((SpreadMortonBits((uint)ty) << 1) & plan.CubeMaskY)
            : InterleaveBits((uint)tx, plan.CubeMaskX) | InterleaveBits((uint)ty, plan.CubeMaskY);
        uint address = _textureOffset[slot] + (plan.CubeFaceStride * (uint)face) + (swizzled * bytes);
        return TryDecodeSwizzledTexel(memory, address, color, out r, out g, out b, out a);
    }

    /// <summary>
    /// TEXFILTER tent filter for a cube map (opt-in): GL samples a cubemap
    /// with LINEAR at LOD 0 inside the selected face and clamps the four taps
    /// at the face edge - it never blends across faces - so this is the 2D
    /// bilinear footprint with a face-local clamp.
    /// </summary>
    private bool SampleCubeBilinear(
        IGpuMemory memory,
        ref readonly SamplerPlan plan,
        uint color,
        uint bytes,
        int face,
        float s,
        float t,
        out float r,
        out float g,
        out float b,
        out float a)
    {
        r = 1f;
        g = 1f;
        b = 1f;
        a = 1f;
        int width = plan.CubeWidth;
        int height = plan.CubeHeight;
        float fx = (s * width) - 0.5f;
        float fy = (t * height) - 0.5f;
        int x0 = (int)MathF.Floor(fx);
        int y0 = (int)MathF.Floor(fy);
        float wx = fx - x0;
        float wy = fy - y0;
        if (!TryReadCubeTexel(memory, in plan, color, bytes, face, x0, y0, out float r00, out float g00, out float b00,
                out float a00) ||
            !TryReadCubeTexel(memory, in plan, color, bytes, face, x0 + 1, y0, out float r10, out float g10,
                out float b10, out float a10) ||
            !TryReadCubeTexel(memory, in plan, color, bytes, face, x0, y0 + 1, out float r01, out float g01,
                out float b01, out float a01) ||
            !TryReadCubeTexel(memory, in plan, color, bytes, face, x0 + 1, y0 + 1, out float r11, out float g11,
                out float b11, out float a11))
        {
            return false;
        }

        float w00 = (1f - wx) * (1f - wy);
        float w10 = wx * (1f - wy);
        float w01 = (1f - wx) * wy;
        float w11 = wx * wy;
        r = (r00 * w00) + (r10 * w10) + (r01 * w01) + (r11 * w11);
        g = (g00 * w00) + (g10 * w10) + (g01 * w01) + (g11 * w11);
        b = (b00 * w00) + (b10 * w10) + (b01 * w01) + (b11 * w11);
        a = (a00 * w00) + (a10 * w10) + (a01 * w01) + (a11 * w11);
        return true;
    }

    private bool TryReadCubeTexel(
        IGpuMemory memory,
        ref readonly SamplerPlan plan,
        uint color,
        uint bytes,
        int face,
        int tx,
        int ty,
        out float r,
        out float g,
        out float b,
        out float a)
    {
        tx = Math.Clamp(tx, 0, plan.CubeWidth - 1);
        ty = Math.Clamp(ty, 0, plan.CubeHeight - 1);
        uint swizzled = plan.CubeMorton
            ? (SpreadMortonBits((uint)tx) & plan.CubeMaskX) | ((SpreadMortonBits((uint)ty) << 1) & plan.CubeMaskY)
            : InterleaveBits((uint)tx, plan.CubeMaskX) | InterleaveBits((uint)ty, plan.CubeMaskY);
        uint address = _textureOffset[plan.Slot] + (plan.CubeFaceStride * (uint)face) + (swizzled * bytes);
        return TryDecodeSwizzledTexel(memory, address, color, out r, out g, out b, out a);
    }

    /// <summary>
    /// Samples a stage's swizzled volume texture (xemu <c>unswizzle_box</c>
    /// layout) at normalized xyz coordinates. The volume depth comes from
    /// TEXFMT BASE_SIZE_P; the P address mode from TEXADDRESS bits 16-18.
    /// </summary>
    private bool TrySampleTexture3D(int slot, float u, float v, float w, out float r, out float g, out float b,
        out float a)
    {
        r = 1f;
        g = 1f;
        b = 1f;
        a = 1f;
        IGpuMemory? memory = _memory;
        if (memory is null || (_textureControl0[slot] & TextureControl0Enable) == 0)
        {
            return false;
        }

        int width = (int)(_textureImageRect[slot] >> 16);
        int height = (int)(_textureImageRect[slot] & 0xFFFFu);
        int depth = 1 << (int)((_textureFormat[slot] & TextureFormatBaseSizeP) >> 28);
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        uint color = (_textureFormat[slot] >> 8) & 0xFFu;
        uint bytes = color switch
        {
            0x06u => 4u,
            0x05u => 2u,
            _ => 0u,
        };
        if (bytes == 0)
        {
            return false;
        }

        uint address = _textureAddress[slot];
        int x = WrapCoordinate((int)MathF.Floor(u * width), width, ToWrapMode(DecodeTextureAddressU((int)address)));
        int y = WrapCoordinate((int)MathF.Floor(v * height), height, ToWrapMode(DecodeTextureAddressV((int)address)));
        int z = WrapCoordinate((int)MathF.Floor(w * depth), depth, ToWrapMode(DecodeTextureAddressP((int)address)));
        address = _textureOffset[slot] + (SwizzleOffset3D(x, y, z, width, height, depth) * bytes);
        return TryDecodeSwizzledTexel(memory, address, color, out r, out g, out b, out a);
    }

    /// <summary>
    /// Byte length of one cube face: all mip levels of the level-0 extent
    /// (TEXFMT MIPMAP_LEVELS clamped by TEXCTL0 MAX_LOD_CLAMP and the base
    /// size), rounded up to the 128-byte face alignment. Matches xemu's
    /// <c>pgraph_get_texture_length</c> cubemap path.
    /// </summary>
    private uint CubeFaceStride(int slot, int width, int height, uint bytes)
    {
        int levels = (int)((_textureFormat[slot] & TextureFormatMipmapLevels) >> 16);
        int maxLevel = (int)((_textureControl0[slot] >> 6) & 0x3FFu);
        int logMax = 0;
        int maxDim = Math.Max(width, height);
        while ((1 << logMax) < maxDim)
        {
            logMax++;
        }

        levels = Math.Min(Math.Max(levels, 1), Math.Min(maxLevel + 1, logMax + 1));
        uint total = 0u;
        int w = width;
        int h = height;
        for (int level = 0; level < levels; level++)
        {
            w = Math.Max(w, 1);
            h = Math.Max(h, 1);
            total += (uint)(w * h) * bytes;
            w /= 2;
            h /= 2;
        }

        return (total + 127u) & ~127u;
    }

    /// <summary>
    /// Decodes a swizzled texel for any format the 2D sampler can decode
    /// (A8R8G8B8, R5G6B5 and the byte-order twins), sharing
    /// <see cref="DecodeTextureTexel"/> so the cube/volume paths can never
    /// silently disagree with the 2D path about a format's channel order.
    /// </summary>
    private static bool TryDecodeSwizzledTexel(IGpuMemory memory, uint address, uint color, out float r, out float g,
        out float b, out float a)
    {
        r = 1f;
        g = 1f;
        b = 1f;
        a = 1f;
        uint bytes = TextureFormatBytesPerTexel(color);
        if (bytes is not (2u or 4u))
        {
            return false;
        }

        uint texel = memory.ReadPhysical(address);
        if (bytes == 2u)
        {
            texel &= 0xFFFFu;
        }

        DecodeTextureTexel(color, texel, out r, out g, out b, out a);
        return true;
    }

    /// <summary>
    /// Applies a texture address mode to one texel coordinate. Mode 0 wraps
    /// (modulo), mode 1 mirrors about the edge, and any other mode clamps to
    /// [0, size - 1].
    /// </summary>
    /// <param name="value">Unwrapped texel coordinate.</param>
    /// <param name="size">Texture extent (width or height) in texels.</param>
    /// <param name="mode">Address mode from the SET_TEXTURE_ADDRESS word.</param>
    /// <returns>The coordinate folded into the texture extent.</returns>
    public static int WrapCoordinate(int value, int size, int mode)
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

    /// <summary>
    /// Decodes the U address mode from a NV097_SET_TEXTURE_ADDRESS word
    /// (PGRAPH_TEXADDRESS ADDRU, bits 0-2). Values follow
    /// <c>nv2a_regs.h</c>: 1 = wrap, 2 = mirror, 3 = clamp-to-edge,
    /// 4 = border, 5 = OpenGL clamp; 0 is the reset value (wraps via
    /// <see cref="ToWrapMode"/>). Reserved modes 6-7 resolve to
    /// clamp-to-edge here so no downstream consumer ever sees a reserved
    /// mode (the reference has no behavior for them: its address-mode map
    /// only covers 0-5 and it asserts on anything larger).
    /// </summary>
    public static int DecodeTextureAddressU(int address)
    {
        return NormalizeTextureAddressMode(address & (int)TextureAddressU);
    }

    /// <summary>
    /// Decodes the V address mode from a NV097_SET_TEXTURE_ADDRESS word
    /// (PGRAPH_TEXADDRESS ADDRV, bits 8-10). This is the §96-noted V-bit
    /// fix: the 2D sampler previously read bits 4-7. Reserved modes 6-7
    /// resolve to clamp-to-edge, as in <see cref="DecodeTextureAddressU"/>.
    /// </summary>
    public static int DecodeTextureAddressV(int address)
    {
        return NormalizeTextureAddressMode((address & (int)TextureAddressV) >> 8);
    }

    /// <summary>
    /// Decodes the P (volume-depth) address mode (ADDRP, bits 16-18).
    /// Reserved modes 6-7 resolve to clamp-to-edge, as in
    /// <see cref="DecodeTextureAddressU"/>.
    /// </summary>
    public static int DecodeTextureAddressP(int address)
    {
        return NormalizeTextureAddressMode((address & (int)TextureAddressP) >> 16);
    }

    /// <summary>
    /// Maps a raw 3-bit texture address mode to the decoded mode: reserved
    /// values 6-7 become clamp-to-edge (the standing safe choice; the
    /// reference defines no behavior for them). All other values pass
    /// through unchanged (mode 0 keeps its reset-means-wrap handling in
    /// <see cref="ToWrapMode"/>).
    /// </summary>
    private static int NormalizeTextureAddressMode(int mode)
    {
        return mode >= 6 ? (int)TextureAddressClampToEdge : mode;
    }

    /// <summary>
    /// Maps a reference address mode to a <see cref="WrapCoordinate"/> mode
    /// (0 = wrap, 1 = mirror, 2 = clamp). Mode 0 (reset) wraps, preserving
    /// the long-standing sampler behavior; modes 3-5 clamp and reserved
    /// values fall through to clamp. Border resolves as clamp here;
    /// out-of-range border axes are returned before wrapping by the caller.
    /// </summary>
    public static int ToWrapMode(int addressMode)
    {
        return addressMode switch
        {
            (int)TextureAddressMirror => 1,
            0 => 0,
            (int)TextureAddressWrap => 0,
            _ => 2,
        };
    }

    /// <summary>
    /// Decodes an ARGB-packed NV097_SET_TEXTURE_BORDER_COLOR word to RGBA
    /// floats (xemu <c>pgraph_argb_pack32_to_rgba_float</c>).
    /// </summary>
    public static void DecodeBorderColor(uint argb, out float r, out float g, out float b, out float a)
    {
        a = ((argb >> 24) & 0xFFu) / 255f;
        r = ((argb >> 16) & 0xFFu) / 255f;
        g = ((argb >> 8) & 0xFFu) / 255f;
        b = (argb & 0xFFu) / 255f;
    }

    /// <summary>
    /// Gets whether a texture color format is a depth (shadow-map) format.
    /// Depths sample through the shadow path (xemu <c>psh_append_shadowmap</c>,
    /// selected by <c>f.depth</c> in <c>texture.c</c>), not the color decoder.
    /// </summary>
    public static bool IsDepthTextureFormat(uint color)
    {
        return color is 0x2Cu or 0x2Eu or 0x2Fu or 0x30u or 0x31u;
    }

    /// <summary>
    /// Computes the 2D swizzled (Morton-order) texel offset: x and y bits are
    /// interleaved through width/height-derived masks (xemu
    /// <c>generate_swizzle_masks</c>), so the caller scales it by bytes per texel.
    /// </summary>
    /// <param name="x">Texel column.</param>
    /// <param name="y">Texel row.</param>
    /// <param name="width">Texture width in texels.</param>
    /// <param name="height">Texture height in texels.</param>
    /// <returns>The texel index within the swizzled surface.</returns>
    public static uint SwizzleOffset(int x, int y, int width, int height)
    {
        ComputeSwizzleMasks(width, height, out uint maskX, out uint maskY);
        return InterleaveBits((uint)x, maskX) | InterleaveBits((uint)y, maskY);
    }

    /// <summary>
    /// Volume (3D) swizzle offset: the same interleaved-mask construction as
    /// the 2D swizzle with the z bits woven in after x and y (xemu
    /// <c>generate_swizzle_masks</c> / <c>unswizzle_box</c>).
    /// </summary>
    private static uint SwizzleOffset3D(int x, int y, int z, int width, int height, int depth)
    {
        uint maskX = 0u;
        uint maskY = 0u;
        uint maskZ = 0u;
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

            if (bit < (uint)depth)
            {
                maskZ |= maskBit;
                maskBit <<= 1;
                done = false;
            }

            bit <<= 1;
        } while (!done);

        return InterleaveBits((uint)x, maskX) | InterleaveBits((uint)y, maskY) | InterleaveBits((uint)z, maskZ);
    }

    /// <summary>
    /// Spreads the low 16 bits of <paramref name="value"/> into the even bit
    /// positions (Morton interleave), the exact result of
    /// <c>InterleaveBits(value, 0x55555555)</c>; the caller masks to the
    /// plan's actual mask. Branchless, unlike the per-bit loop it replaces.
    /// </summary>
    private static uint SpreadMortonBits(uint value)
    {
        value &= 0xFFFFu;
        value = (value | (value << 8)) & 0x00FF00FFu;
        value = (value | (value << 4)) & 0x0F0F0F0Fu;
        value = (value | (value << 2)) & 0x33333333u;
        value = (value | (value << 1)) & 0x55555555u;
        return value;
    }

    private static uint InterleaveBits(uint value, uint mask)
    {
        uint result = 0u;
        uint maskBit = 1u;
        int valueBit = 0;
        // Bits above the mask's highest set bit contribute nothing; stopping
        // there is exact (the skipped iterations were no-ops).
        while (maskBit != 0u && maskBit <= mask)
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

    /// <summary>
    /// Device-depth normalization range for the post-program bake. Matches
    /// xemu's clipRange.y (zeta-format maximum): Z16 maps from 65535 units,
    /// Z24S8 from 16777215 units; with CONTROL0 Z_FORMAT the float-depth
    /// maxima apply instead (F16_MAX 511.9375, F24_MAX 1e30 per xemu
    /// util.h, selected by the same SETUPRASTER Z_FORMAT bit the Kelvin
    /// CONTROL0 Z_FORMAT feeds in xemu pgraph.c). Unknown or absent zeta
    /// formats return 1, so shader Z passes through exactly like the legacy
    /// direct clamp.
    /// </summary>
    public static float ZetaDepthRange(uint zetaFormat, bool floatDepth)
    {
        if (floatDepth)
        {
            return zetaFormat switch
            {
                ZetaFormatZ16 => ZetaDepthRangeF16,
                ZetaFormatZ24S8 => ZetaDepthRangeF24,
                _ => 1f,
            };
        }

        return zetaFormat switch
        {
            ZetaFormatZ16 => ZetaDepthRangeZ16,
            ZetaFormatZ24S8 => ZetaDepthRangeZ24S8,
            _ => 1f,
        };
    }

    /// <summary>
    /// Decodes a 16-bit custom-float zeta value to device units (xemu
    /// util.h convert_f16_to_float: zero is exact zero, otherwise the field
    /// is shifted into float32 mantissa position over a small bias).
    /// </summary>
    public static float ConvertF16ToFloat(uint f16)
    {
        if (f16 == 0)
        {
            return 0f;
        }

        return BitConverter.UInt32BitsToSingle((f16 << 11) + ZetaF16Bias);
    }

    /// <summary>
    /// Decodes a 24-bit custom-float zeta value to device units (xemu
    /// util.h convert_f24_to_float: zero is exact zero, otherwise the field
    /// is shifted into float32 mantissa position).
    /// </summary>
    public static float ConvertF24ToFloat(uint f24)
    {
        f24 &= 0xFFFFFFu;
        if (f24 == 0)
        {
            return 0f;
        }

        return BitConverter.UInt32BitsToSingle(f24 << 7);
    }

    /// <summary>
    /// Encodes a normalized [0,1] depth into the 16-bit custom-float zeta
    /// field (inverse of <see cref="ConvertF16ToFloat"/> over its sane
    /// domain). The raw inverse can exceed the range xemu's decode maps back
    /// to non-negative device values (it wraps to negative floats past
    /// 0xFFF8), so the result saturates there; that bin decodes to
    /// 511.5/511.9375 and the clamp preserves ordering.
    /// </summary>
    public static uint ConvertFloatToF16(float depth)
    {
        float scaled = Math.Clamp(depth, 0f, 1f) * ZetaDepthRangeF16;
        uint bits = BitConverter.SingleToUInt32Bits(scaled);
        if (bits < ZetaF16Bias)
        {
            return 0u;
        }

        return Math.Min((bits - ZetaF16Bias) >> 11, ZetaF16Max);
    }

    /// <summary>
    /// Encodes a normalized [0,1] depth into the 24-bit custom-float zeta
    /// field (inverse of <see cref="ConvertF24ToFloat"/>). The float maximum
    /// sits far below this encoding's wrap point, so no saturation is needed.
    /// </summary>
    public static uint ConvertFloatToF24(float depth)
    {
        float scaled = Math.Clamp(depth, 0f, 1f) * ZetaDepthRangeF24;
        return (BitConverter.SingleToUInt32Bits(scaled) >> 7) & 0xFFFFFFu;
    }

    /// <summary>
    /// Encodes a normalized depth into a 24-bit zeta field (fixed-point, or
    /// the custom float encoding when CONTROL0 Z_FORMAT is set). The
    /// fixed-point result is capped at the field maximum 0xFFFFFF: for a
    /// normalized depth of exactly 1.0 the round-to-nearest step
    /// (<c>16777215 + 0.5</c>) rounds up to 16777216, which without the cap
    /// shifts out of the zeta word entirely (the pre-existing exact-1.0
    /// overflow). Capping stores 0xFFFFFF, which decodes back to exactly 1.0
    /// and is the value xemu's D24 path stores for device z = clipRange.w.
    /// </summary>
    public uint EncodeDepth24(float depth)
    {
        if (_zFormatFloat)
        {
            return ConvertFloatToF24(depth);
        }

        float scaled = (Math.Clamp(depth, 0f, 1f) * ZetaDepthRangeZ24S8) + 0.5f;
        return Math.Min((uint)scaled, ZetaFixedMax24);
    }

    /// <summary>
    /// Encodes a normalized depth into a 16-bit zeta field (fixed-point, or
    /// the custom float encoding when CONTROL0 Z_FORMAT is set). Capped at
    /// 0xFFFF for the same exact-1.0 overflow reason as
    /// <see cref="EncodeDepth24"/> (unreachable with the current float
    /// arithmetic, kept as a guard).
    /// </summary>
    public uint EncodeDepth16(float depth)
    {
        if (_zFormatFloat)
        {
            return ConvertFloatToF16(depth);
        }

        float scaled = (Math.Clamp(depth, 0f, 1f) * ZetaDepthRangeZ16) + 0.5f;
        return Math.Min((uint)scaled, ZetaFixedMax16);
    }

    /// <summary>
    /// ZCLIP bounds for one fragment (xemu psh.c depth clipping). The
    /// interpolated depth is converted to device units and tested against
    /// [ZCLIPMIN, ZCLIPMAX]: with ZCLAMP_EN = CULL (the register reset state,
    /// also what the dashboard programs via SET_ZMIN_MAX_CONTROL) an
    /// out-of-bounds fragment is discarded exactly like the reference
    /// shader's `discard` — returns false and the caller must not write
    /// depth, stencil or color. With ZCLAMP_EN = CLAMP the depth is pulled
    /// to the nearest bound before the compare/store below. In-bounds
    /// fragments keep their normalized depth bit-exact. Only runs once the
    /// guest has programmed either bound; an unprogrammed unit keeps the
    /// legacy [0,1] clamp exactly (xemu has no enable bit and resets both
    /// regs to zero, but guests always program them before 3D, so
    /// passthrough-until-written is observationally identical).
    /// </summary>
    private bool ApplyZclip(ref float depth)
    {
        float zvalue = depth * _zclipRange;
        if (zvalue < _zclipMin || zvalue > _zclipMax)
        {
            if (_zclipCull)
            {
                ZclipCulledPixels++;
                return false;
            }

            ZclipClampedPixels++;
            depth = Math.Clamp(zvalue, _zclipMin, _zclipMax) / _zclipRange;
        }

        // In-bounds depths keep their normalized value bit-exact: the
        // device-unit round-trip (d * range / range) can cost a ULP, which
        // would perturb depth compares/stores for guests whose bounds
        // contain the whole scene (e.g. the dashboard's [0, 16777215]).
        return true;
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
            _presentedOffset = SurfaceBase(_dmaColor) + PresentedColorOffset;
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
        if (Backend is not null)
        {
            // Backend attached: the presented frame is exclusively the
            // backend's readback — never a mix of software and GPU pixels.
            return Backend.ReadbackPresented(out width, out height, out bgra);
        }

        if (!_presentedValid)
        {
            return false;
        }

        if (!CopySurfacePixels(_presentedOffset, _presentedWidth, _presentedHeight, _presentedPitch, _presentedBytes, out bgra))
        {
            return false;
        }

        width = _presentedWidth;
        height = _presentedHeight;
        return true;
    }

    /// <summary>
    /// Resolves the display-controller scanout geometry: base, visible size,
    /// line pitch and bytes per pixel.
    /// </summary>
    /// <remarks>
    /// The guest programs the video mode through the VGA register file the NV2A
    /// mirrors at NV_PRMCIO, so the CRTC is the authority (xemu
    /// <c>nv2a_get_params</c> for the pitch and start address, QEMU
    /// <c>vga_get_resolution</c> for the visible size; NV CR28 bits 0-1 select
    /// the depth, <c>nv2a_get_bpp</c>). Until the guest has written a CRTC data
    /// register there is no mode to speak of, and callers fall back to the last
    /// flipped surface.
    /// </remarks>
    /// <returns>True when the CRTC describes a scannable buffer inside RAM.</returns>
    public bool TryGetDisplayGeometry(out uint start, out uint width, out uint height, out uint lineOffset, out uint bytesPerPixel)
    {
        start = _pcrtcStart;
        width = DisplayWidth;
        height = DisplayHeight;
        lineOffset = DisplayLineOffset;
        bytesPerPixel = DisplayBytesPerPixel;
        if (bytesPerPixel == 0)
        {
            bytesPerPixel = _presentedBytes;
        }

        bool screenOn = (DisplayClockMode & 0x20) == 0;
        return _vgaCrtcWritten &&
               screenOn &&
               start != 0 &&
               (bytesPerPixel == 2 || bytesPerPixel == 4) &&
               width >= 64 && width <= MaxPresentDimension &&
               height >= 32 && height <= MaxPresentDimension &&
               (ulong)width * height <= MaxPresentPixels &&
               lineOffset >= width * bytesPerPixel &&
               lineOffset <= 0x4000u &&
               (ulong)start + ((ulong)(height - 1) * lineOffset) + (width * bytesPerPixel) <= RamSize;
    }

    /// <summary>
    /// Copies the frame the display controller is scanning out - what a TV
    /// attached to the console would show at this instant.
    /// </summary>
    /// <remarks>
    /// Programs that render straight into the framebuffer (nxdk's
    /// <c>XVideoGetFB</c>, <c>debugPrint</c>) issue no 3D draws and no flips, so
    /// <see cref="TryCopyPresentedFrame"/> never sees their output. The display
    /// buffer is PCRTC START read with the CRTC pitch; until the guest programs a
    /// mode this falls back to the flipped surface so early boot still shows the
    /// 3D output. A GPU backend owns the presented image, so it is never mixed
    /// with the software scanout.
    /// </remarks>
    /// <returns>False when neither a display mode nor a presented frame is available.</returns>
    public bool TryCopyDisplayFrame(out uint width, out uint height, out byte[] bgra)
    {
        width = 0;
        height = 0;
        bgra = Array.Empty<byte>();
        if (Backend is not null)
        {
            return Backend.ReadbackPresented(out width, out height, out bgra);
        }

        if (_memory is not null &&
            TryGetDisplayGeometry(out uint start, out uint displayWidth, out uint displayHeight, out uint lineOffset, out uint bytesPerPixel) &&
            CopySurfacePixels(start, displayWidth, displayHeight, lineOffset, bytesPerPixel, DisplayFifteenBit, out bgra))
        {
            width = displayWidth;
            height = displayHeight;
            return true;
        }

        return TryCopyPresentedFrame(out width, out height, out bgra);
    }

    /// <summary>
    /// Copies the current scanout surface - the buffer the display controller is
    /// reading - instead of the last flipped 3D surface.
    /// </summary>
    /// <remarks>
    /// Same source as <see cref="TryCopyDisplayFrame"/>, but without the backend
    /// delegation and without the flipped-surface fallback: this is the raw
    /// scanout export, and it fails when no display mode is programmed. Geometry
    /// comes from the VGA CRTC, falling back to the presented surface records.
    /// </remarks>
    /// <returns>False when there is no scanout or no usable geometry.</returns>
    public bool TryCopyScanoutFrame(out uint width, out uint height, out byte[] bgra)
    {
        width = 0;
        height = 0;
        bgra = Array.Empty<byte>();
        if (Backend is not null || _memory is null || _pcrtcStart == 0)
        {
            return false;
        }

        if (!TryGetDisplayGeometry(out uint start, out uint scanWidth, out uint scanHeight, out uint lineOffset, out uint bytesPerPixel))
        {
            if (!_presentedValid)
            {
                return false;
            }

            start = _pcrtcStart;
            scanWidth = _presentedWidth;
            scanHeight = _presentedHeight;
            lineOffset = _presentedPitch;
            bytesPerPixel = _presentedBytes;
        }

        if (!CopySurfacePixels(start, scanWidth, scanHeight, lineOffset, bytesPerPixel, DisplayFifteenBit, out bgra))
        {
            return false;
        }

        width = scanWidth;
        height = scanHeight;
        return true;
    }

    private bool CopySurfacePixels(uint offset, uint width, uint height, uint pitch, uint bytesPerPixel, out byte[] bgra) =>
        CopySurfacePixels(offset, width, height, pitch, bytesPerPixel, fifteenBit: false, out bgra);

    private bool CopySurfacePixels(
        uint offset, uint width, uint height, uint pitch, uint bytesPerPixel, bool fifteenBit, out byte[] bgra)
    {
        bgra = Array.Empty<byte>();
        ulong count = (ulong)width * height;
        if (count == 0 || count > MaxPresentPixels || _memory is null)
        {
            return false;
        }

        byte[] pixels = new byte[count * 4];
        for (uint y = 0; y < height; y++)
        {
            uint row = offset + (y * pitch);
            for (uint x = 0; x < width; x++)
            {
                uint address = row + (x * bytesPerPixel);
                if (address + bytesPerPixel > RamSize)
                {
                    return false;
                }

                uint red;
                uint green;
                uint blue;
                uint alpha;
                if (bytesPerPixel == 4)
                {
                    uint pixel = ReadGpu(address);
                    blue = pixel & 0xFFu;
                    green = (pixel >> 8) & 0xFFu;
                    red = (pixel >> 16) & 0xFFu;
                    alpha = (pixel >> 24) & 0xFFu;
                }
                else
                {
                    uint half = ReadHalf(address);
                    if (fifteenBit)
                    {
                        // VGA 15bpp (X1R5G5B5): 5 bits per channel, no green
                        // extension (xemu nv2a_get_bpp -> QEMU vga_draw_line15).
                        red = (((half >> 10) & 0x1Fu) * 255u) / 31u;
                        green = (((half >> 5) & 0x1Fu) * 255u) / 31u;
                    }
                    else
                    {
                        red = (((half >> 11) & 0x1Fu) * 255u) / 31u;
                        green = (((half >> 5) & 0x3Fu) * 255u) / 63u;
                    }

                    blue = (half & 0x1Fu) * 255u / 31u;
                    alpha = 255u;
                }

                int at = (int)((((ulong)y * width) + x) * 4);
                pixels[at] = (byte)blue;
                pixels[at + 1] = (byte)green;
                pixels[at + 2] = (byte)red;
                pixels[at + 3] = (byte)alpha;
            }
        }

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
            uint current = ReadGpu(address);
            uint depthBits = writeDepth
                ? EncodeDepth24(depth)
                : (current >> 8) & 0xFFFFFFu;
            uint writeMask = _stencilWriteMask & StencilByteMask;
            uint newStencil = writeStencil
                ? (current & StencilByteMask & ~writeMask) | (stencil & writeMask)
                : current & StencilByteMask;
            WriteGpu(address, (depthBits << 8) | newStencil);
            return;
        }

        uint wordAddress = address & ~3u;
        int shift = (int)((address & 2u) * 8);
        uint currentWord = ReadGpu(wordAddress);
        uint half = writeDepth
            ? EncodeDepth16(depth)
            : (currentWord >> shift) & 0xFFFFu;
        uint halfMask = 0xFFFFu << shift;
        WriteGpu(wordAddress, (currentWord & ~halfMask) | ((half << shift) & halfMask));
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
            uint pixel = ReadGpu(address);
            a = Unit255[(int)((pixel >> 24) & 0xFFu)];
            r = Unit255[(int)((pixel >> 16) & 0xFFu)];
            g = Unit255[(int)((pixel >> 8) & 0xFFu)];
            b = Unit255[(int)(pixel & 0xFFu)];
            return true;
        }

        uint word = ReadGpu(address & ~3u);
        uint half = ((address & 2u) == 0) ? word & 0xFFFFu : word >> 16;
        r = Unit31[(int)((half >> 11) & 0x1Fu)];
        g = Unit63[(int)((half >> 5) & 0x3Fu)];
        b = Unit31[(int)(half & 0x1Fu)];
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

        // ZCLIP runs at the top of the reference fragment shader (before the
        // alpha test and before any depth/stencil/color write), so discard
        // mode kills the fragment even when the depth test is disabled.
        if (_zclipActive && !ApplyZclip(ref depth))
        {
            return;
        }

        if (_alphaTestEnable && !CompareFunc(_alphaFunc, a, _alphaRefScaled))
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
            zetaAddress = _zetaSurfaceBase + _surfaceZetaOffset + (y * zetaPitch) + (x * zetaBytes);
            if (zetaAddress + zetaBytes <= RamSize)
            {
                storedZeta = zetaBytes == 4 ? ReadGpu(zetaAddress) : ReadHalf(zetaAddress);
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
                storedDepth = _zFormatFloat
                    ? ConvertF24ToFloat((storedZeta >> 8) & 0xFFFFFFu) / ZetaDepthRangeF24
                    : ((storedZeta >> 8) & 0xFFFFFFu) / 16777215f;
                stencil = storedZeta & StencilByteMask;
            }
            else
            {
                storedDepth = _zFormatFloat
                    ? ConvertF16ToFloat(storedZeta & 0xFFFFu) / ZetaDepthRangeF16
                    : (storedZeta & 0xFFFFu) / 65535f;
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
            if (float.IsNaN(clampedDepth))
            {
                // NaN keeps IEEE ordered-compare semantics (all of <, <=, >,
                // >=, == are false), matching the pre-quantization behavior.
                depthPass = CompareFunc(_depthFunc, clampedDepth, storedDepth);
            }
            else
            {
                // The fragment depth converts to the zeta buffer's internal
                // precision before the depth compare (xemu psh.c: "With
                // integer depth buffers Xbox hardware floors values" — the
                // reference shader emits the already-quantized gl_FragDepth,
                // and GL/Vulkan/D3D all convert FragDepth to the attachment
                // format prior to the fixed-function test). Comparing the raw
                // interpolated float against the quantized stored depth lets
                // ULP-level barycentric rounding fail coplanar equal-z fights:
                // overlapping triangles then interleave pass/fail pixels (the
                // nxdk triangle demo's diagonal hatch). Encode the fragment
                // through the same conversion the store uses and compare in
                // zeta field space, which is monotonic with the stored value.
                uint storedField = zetaBytes == 4
                    ? (storedZeta >> 8) & 0xFFFFFFu
                    : storedZeta & 0xFFFFu;
                uint fragmentField = zetaBytes == 4
                    ? EncodeDepth24(clampedDepth)
                    : EncodeDepth16(clampedDepth);
                depthPass = CompareFunc(_depthFunc, fragmentField, storedField);
            }

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
            uint address = _colorSurfaceBase + PresentedColorOffset + (y * pitch) + (x * bytes);
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
                if (_blendFastSrcAlpha)
                {
                    result = BlendSrcAlphaAdd(src, dst);
                }
                else
                {
                    (float R, float G, float B, float A) constant = (
                        _blendConstR, _blendConstG, _blendConstB, _blendConstA);
                    (float R, float G, float B, float A) fs = BlendFactor(_blendSrcFactor, src, dst, constant);
                    (float R, float G, float B, float A) fd = BlendFactor(_blendDstFactor, src, dst, constant);
                    result = _blendEquation switch
                    {
                        0u => ((src.R * fs.R) - (dst.R * fd.R), (src.G * fs.G) - (dst.G * fd.G),
                            (src.B * fs.B) - (dst.B * fd.B), (src.A * fs.A) - (dst.A * fd.A)),
                        1u or 5u => ((dst.R * fd.R) - (src.R * fs.R), (dst.G * fd.G) - (src.G * fs.G),
                            (dst.B * fd.B) - (src.B * fs.B), (dst.A * fd.A) - (src.A * fs.A)),
                        3u => (Math.Min(src.R, dst.R), Math.Min(src.G, dst.G),
                            Math.Min(src.B, dst.B), Math.Min(src.A, dst.A)),
                        4u => (Math.Max(src.R, dst.R), Math.Max(src.G, dst.G),
                            Math.Max(src.B, dst.B), Math.Max(src.A, dst.A)),
                        _ => ((src.R * fs.R) + (dst.R * fd.R), (src.G * fs.G) + (dst.G * fd.G),
                            (src.B * fs.B) + (dst.B * fd.B), (src.A * fs.A) + (dst.A * fd.A)),
                    };
                    result = (
                        Math.Clamp(result.R, 0f, 1f),
                        Math.Clamp(result.G, 0f, 1f),
                        Math.Clamp(result.B, 0f, 1f),
                        Math.Clamp(result.A, 0f, 1f));
                }

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

    private PgraphFragmentState BuildFragmentState()
    {
        return new PgraphFragmentState(
            _alphaTestEnable, _alphaFunc, _alphaRef,
            _blendEnable, _blendSrcFactor, _blendDstFactor, _blendColor, _blendEquation,
            _depthTestEnable, _depthFunc, _depthMask,
            _colorMask,
            _stencilTestEnable, _stencilWriteMask, _stencilFunc, _stencilRef,
            _stencilReadMask, _stencilOpFail, _stencilOpZfail, _stencilOpZpass,
            _zclipActive, _zclipMin, _zclipMax, _zclipCull);
    }

    private PgraphTextureSlot BuildTextureSlot(int slot)
    {
        return new PgraphTextureSlot(
            _textureOffset[slot], _textureFormat[slot], _textureAddress[slot],
            _textureControl0[slot], _textureControl1[slot], _textureImageRect[slot]);
    }

    private PgraphDrawInfo BuildDrawInfo(
        in PshVertex a, in PshVertex b, in PshVertex c,
        uint clipX, uint clipY, uint clipW, uint clipH, uint bytes, uint zetaBytes, bool combinerActive)
    {
        return new PgraphDrawInfo(
            ToBackendVertex(a), ToBackendVertex(b), ToBackendVertex(c),
            clipX, clipY, clipW, clipH,
            bytes, zetaBytes, combinerActive,
            BuildFragmentState(),
            BuildTextureSlot(0), BuildTextureSlot(1), BuildTextureSlot(2), BuildTextureSlot(3),
            _combiner,
            _fogEnable, _fogColor[0], _fogColor[1], _fogColor[2], _fogColor[3],
            _shaderClipMode,
            _zFormatFloat);
    }

    private PgraphDrawInfo BuildLegacyDrawInfo(
        float ax, float ay, float az, float aw, float ar, float ag, float ab, float aa, float au, float av,
        float bx, float by, float bz, float bw, float br, float bg, float bb, float ba, float bu, float bv,
        float cx, float cy, float cz, float cw, float cr, float cg, float cb, float ca, float cu, float cv,
        uint clipX, uint clipY, uint clipW, uint clipH, uint bytes, uint zetaBytes)
    {
        // The non-combiner fill path (legacy inline draws and program draws
        // with the final combiner disabled) only consumes position, diffuse
        // and the oT0 texcoord pair; fog/spec are unused and the fog factor
        // is irrelevant because the combiner never reads it here.
        var a = new PgraphVertex(ax, ay, az, aw, ar, ag, ab, aa, 0f, 0f, 0f, 0f, au, av, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f, 0f, 0f, 1f);
        var b = new PgraphVertex(bx, by, bz, bw, br, bg, bb, ba, 0f, 0f, 0f, 0f, bu, bv, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f, 0f, 0f, 1f);
        var c = new PgraphVertex(cx, cy, cz, cw, cr, cg, cb, ca, 0f, 0f, 0f, 0f, cu, cv, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f, 0f, 0f, 1f);
        return new PgraphDrawInfo(
            a, b, c,
            clipX, clipY, clipW, clipH,
            bytes, zetaBytes, combinerActive: false,
            BuildFragmentState(),
            BuildTextureSlot(0), BuildTextureSlot(1), BuildTextureSlot(2), BuildTextureSlot(3),
            _combiner,
            _fogEnable, _fogColor[0], _fogColor[1], _fogColor[2], _fogColor[3],
            _shaderClipMode,
            _zFormatFloat);
    }

    private static PgraphVertex ToBackendVertex(in PshVertex v)
    {
        return new PgraphVertex(
            v.X, v.Y, v.Z, v.W,
            v.DiffuseR, v.DiffuseG, v.DiffuseB, v.DiffuseA,
            v.SpecR, v.SpecG, v.SpecB, v.SpecA,
            v.U0, v.V0, v.S0,
            v.U1, v.V1, v.S1,
            v.U2, v.V2, v.S2,
            v.U3, v.V3, v.S3,
            v.T0W, v.T1W, v.T2W, v.T3W,
            v.Fog);
    }

    /// <summary>
    /// Caches per-fill fragment constants (alpha reference scale, blend
    /// constant vector, zclip depth range) so the per-pixel fragment path
    /// does not recompute them. Values are computed with the exact same
    /// expressions as the per-pixel path (bit-identical); a fill cannot be
    /// interleaved with guest register writes, so the cache is always
    /// current inside the pixel loops.
    /// </summary>
    private void PrepareFragmentConstants(uint zetaBytes)
    {
        _alphaRefScaled = _alphaRef / 255f;
        uint blend = _blendColor;
        _blendConstR = ((blend >> 16) & 0xFFu) / 255f;
        _blendConstG = ((blend >> 8) & 0xFFu) / 255f;
        _blendConstB = (blend & 0xFFu) / 255f;
        _blendConstA = ((blend >> 24) & 0xFFu) / 255f;
        uint zetaFormat = zetaBytes switch
        {
            2u => ZetaFormatZ16,
            4u => ZetaFormatZ24S8,
            _ => 0u,
        };
        _zclipRange = ZetaDepthRange(zetaFormat, _zFormatFloat);
        _blendFastSrcAlpha = _blendEnable && _blendSrcFactor == 4u && _blendDstFactor == 5u &&
                             _blendEquation is not (0u or 1u or 3u or 4u or 5u);
    }

    /// <summary>Decides whether a screen-space triangle is discarded by the cull-face test.</summary>
    /// <remarks>
    /// Screen space is Y-down, so a visually clockwise triangle has a positive
    /// signed area under <c>((bx-ax)*(cy-ay)) - ((cx-ax)*(by-ay))</c>.
    /// <c>SET_FRONT_FACE</c> selects which winding counts as front facing and
    /// <c>SET_CULL_FACE</c> selects which winding is discarded; degenerate
    /// triangles are already rejected by the caller.
    /// </remarks>
    private bool ShouldCull(float area)
    {
        if (!_cullFaceEnable)
        {
            return false;
        }

        if (_cullFace == CullFrontAndBackFace)
        {
            return true;
        }

        bool frontFacing = area > 0f == _frontFaceCw;
        return frontFacing == (_cullFace == CullFrontFace);
    }

    private void FillTriangle(
        float ax, float ay, float az, float aw, float ar, float ag, float ab, float aa, float au, float av,
        float bx, float by, float bz, float bw, float br, float bg, float bb, float ba, float bu, float bv,
        float cx, float cy, float cz, float cw, float cr, float cg, float cb, float ca, float cu, float cv,
        uint clipX, uint clipY, uint clipW, uint clipH, uint bytes, uint pitch)
    {
        using GpuWriteBatch batch = new(_memoryFast);
        float area = ((bx - ax) * (cy - ay)) - ((cx - ax) * (by - ay));
        if (area == 0f)
        {
            return;
        }

        if (ShouldCull(area))
        {
            TrianglesCulled++;
            return;
        }

        uint zetaBytes = ZetaBytesPerPixel();
        uint zetaPitch = (_surfacePitch >> 16) & 0xFFFFu;
        _colorSurfaceBase = SurfaceBase(_dmaColor);
        _zetaSurfaceBase = SurfaceBase(_dmaZeta);
        PrepareFragmentConstants(zetaBytes);

        uint x0 = (uint)Math.Max(0f, Math.Min(Math.Min(ax, bx), cx));
        uint x1 = (uint)Math.Min((float)clipX + clipW - 1, Math.Max(Math.Max(ax, bx), cx));
        uint y0 = (uint)Math.Max(0f, Math.Min(Math.Min(ay, by), cy));
        uint y1 = (uint)Math.Min((float)clipY + clipH - 1, Math.Max(Math.Max(ay, by), cy));
        if (x1 < x0 || y1 < y0)
        {
            return;
        }

        IPgraphBackend? backend = Backend;
        if (backend is not null)
        {
            PgraphDrawInfo backendInfo = BuildLegacyDrawInfo(
                ax, ay, az, aw, ar, ag, ab, aa, au, av,
                bx, by, bz, bw, br, bg, bb, ba, bu, bv,
                cx, cy, cz, cw, cr, cg, cb, ca, cu, cv,
                clipX, clipY, clipW, clipH, bytes, zetaBytes);
            if (backend.ReplaceMode)
            {
                if (backend.TryDraw(backendInfo))
                {
                    // Replacement mode: the backend owns the pixels for this
                    // draw; the software rasterizer is skipped entirely.
                    return;
                }
            }
            else
            {
                backend.Draw(backendInfo);
            }
        }

        float invArea = 1f / area;
        float invAw = aw != 0f ? 1f / aw : 0f;
        float invBw = bw != 0f ? 1f / bw : 0f;
        float invCw = cw != 0f ? 1f / cw : 0f;
        bool perspective = invAw != 0f && invBw != 0f && invCw != 0f && (aw != bw || bw != cw);
        long rasterStart = RasterTimingEnabled ? Stopwatch.GetTimestamp() : 0L;
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

                float p0 = w0;
                float p1 = w1;
                float p2 = w2;
                if (perspective)
                {
                    float s0 = w0 * invCw;
                    float s1 = w1 * invAw;
                    float s2 = w2 * invBw;
                    float sum = s0 + s1 + s2;
                    if (float.IsFinite(sum) && sum != 0f)
                    {
                        float invSum = 1f / sum;
                        p0 = s0 * invSum;
                        p1 = s1 * invSum;
                        p2 = s2 * invSum;
                    }
                }

                float r = (p1 * ar) + (p2 * br) + (p0 * cr);
                float g = (p1 * ag) + (p2 * bg) + (p0 * cg);
                float bl = (p1 * ab) + (p2 * bb) + (p0 * cb);
                float al = (p1 * aa) + (p2 * ba) + (p0 * ca);
                float depth = (w1 * az) + (w2 * bz) + (w0 * cz);
                float tu = (p1 * au) + (p2 * bu) + (p0 * cu);
                float tv = (p1 * av) + (p2 * bv) + (p0 * cv);
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

        if (RasterTimingEnabled)
        {
            _rasterLegacyTicks += Stopwatch.GetTimestamp() - rasterStart;
            _rasterLegacyCalls++;
        }
    }

    /// <summary>
    /// Resolves a depth (shadow-map) texture for the PROJECT2D/PROJECT3D
    /// stages. Depth formats sample through xemu
    /// <c>psh_append_shadowmap</c>, not the color decoder: NEVER yields
    /// <c>vec4(0.0)</c>, ALWAYS yields <c>vec4(1.0)</c>. Other depth
    /// functions need the depth-compare path (open) and keep the
    /// format-miss accounting. Returns true when the texel was resolved
    /// here (the caller produces no texture sample). Disabled textures
    /// fall through to the normal disabled path.
    /// </summary>
    private bool TryResolveShadow(int stage, float[] texel)
    {
        if (_memory is null || (_textureControl0[stage] & TextureControl0Enable) == 0)
        {
            return false;
        }

        uint color = (_textureFormat[stage] >> 8) & 0x7Fu;
        if (!IsDepthTextureFormat(color))
        {
            return false;
        }

        if (_shadowDepthFunc == ShadowDepthFuncNever)
        {
            SetTexelZero(texel);
            return true;
        }

        if (_shadowDepthFunc == ShadowDepthFuncAlways)
        {
            texel[0] = 1f;
            texel[1] = 1f;
            texel[2] = 1f;
            texel[3] = 1f;
            return true;
        }

        _textureFormatAttempts[color]++;
        _textureFormatMisses[color]++;
        _textureStateMisses[_currentTextureState]++;
        SetTexelNone(texel);
        return true;
    }

    /// <summary>
    /// Resolves one combiner-stage texel for the current fragment, following the
    /// reference per-mode equations. <paramref name="coords"/> holds the four
    /// interpolated texture-coordinate quads (x, y, z, w) for this fragment.
    /// Returns true when a texture was sampled (drives the TexturedPixels
    /// diagnostic). Modes the rasterizer cannot back (reserved values, a valid
    /// mode in an invalid stage, or an unsupported texture format for
    /// cube/volume sampling) degrade to NONE and count a TexModeFallback.
    /// BRDF and DOT_RFLCT_SPEC_CONST mirror xemu's observable stub (zero texel
    /// plus UNIMPLEMENTED); no reliable reference implements their math.
    /// </summary>
    private bool ResolveTexel(int stage, int mode, float[][] coords, float[][] texels, float[] stageDots)
    {
        float[] texel = texels[stage];
        float[] coord = coords[stage];
        float tu = coord[0];
        float tv = coord[1];
        float ts = coord[2];
        if (!PshCombiner.IsModeValidForStage(mode, stage))
        {
            SetTexelNone(texel);
            CountTexModeFallback(mode);
            return false;
        }

        switch (mode)
        {
            case PshCombiner.TexModeNone:
                SetTexelNone(texel);
                return false;
            case PshCombiner.TexModeProject2D:
                if (IsTextureCubemap(stage))
                {
                    // xemu: texture(texSamp, remap2DToCube(pT.xyw)), i.e.
                    // normalize(vec3(1, y/w, -x/w)).
                    return TrySampleCubeDirection(stage, 1f, tv / coord[3], -tu / coord[3], texel);
                }

                if (TryResolveShadow(stage, texel))
                {
                    return false;
                }

                if (_samplerPlans[stage].Convolution is 1 or 2)
                {
                    return SampleConvolved(stage, tu, tv, coord[3], texel);
                }

                // xemu: textureProj(texSamp, norm(pT.xyw)) — the 2D coords
                // divide by w even without a cube map. A zero w keeps the
                // undivided coords (GLSL textureProj with w = 0 is undefined;
                // no stream programs it).
                if (coord[3] != 0f)
                {
                    return SampleInto(stage, tu / coord[3], tv / coord[3], texel);
                }

                return SampleInto(stage, tu, tv, texel);
            case PshCombiner.TexModeProject3D:
                {
                    if (TryResolveShadow(stage, texel))
                    {
                        return false;
                    }

                    // xemu: textureProj(texSamp, pT.xyzw) — (x/w, y/w) for 2D
                    // samplers, (x/w, y/w, z/w) for volume textures.
                    float invW = 1f / coord[3];
                    if (TextureDimensionality(stage) == 3)
                    {
                        return SampleInto3D(stage, tu * invW, tv * invW, ts * invW, texel);
                    }

                    return SampleInto(stage, tu * invW, tv * invW, texel);
                }

            case PshCombiner.TexModeCubemap:
                if (IsTextureCubemap(stage))
                {
                    return TrySampleCubeDirection(stage, tu, tv, ts, texel);
                }

                PshCombiner.CubeRemapTo2D(tu, tv, ts, out float cu, out float cv);
                return SampleInto(stage, cu, cv, texel);
            case PshCombiner.TexModePassthru:
                texel[0] = tu;
                texel[1] = tv;
                texel[2] = 0f;
                texel[3] = 1f;
                return false;
            case PshCombiner.TexModeClipPlane:
                // The fragment discard is applied by the caller before the
                // resolve loop (xemu emits it in the stage preamble); the
                // hardware texel value stays zero.
                texel[0] = 0f;
                texel[1] = 0f;
                texel[2] = 0f;
                texel[3] = 0f;
                return false;
            case PshCombiner.TexModeBumpEnvMap:
            case PshCombiner.TexModeBumpEnvMapLum:
                return ResolveBump(stage, mode, tu, tv, texels, texel);
            case PshCombiner.TexModeDpndntAr:
            case PshCombiner.TexModeDpndntGb:
                return ResolveDependent(stage, mode, texels, texel);
            case PshCombiner.TexModeBrdf:
            case PshCombiner.TexModeDotRflctSpecConst:
                // xemu emits vec4(0.0) plus NV2A_UNIMPLEMENTED for these two
                // (the Cxbx variants are explicitly untested HLE guesses).
                // Counted per mode so a corpus row can name the offending stage
                // instead of only showing a fallback total.
                SetTexelZero(texel);
                CountTexModeFallback(mode);
                return false;
            case PshCombiner.TexModeDotProduct:
            case PshCombiner.TexModeDotZw:
                if (!TryStageDot(stage, coords, texels, stageDots, out _))
                {
                    SetTexelNone(texel);
                    CountTexModeFallback(mode);
                    return false;
                }

                texel[0] = 0f;
                texel[1] = 0f;
                texel[2] = 0f;
                texel[3] = 0f;
                return false;
            case PshCombiner.TexModeDotSt:
                if (!TryStageDot(stage, coords, texels, stageDots, out float dotSt))
                {
                    SetTexelNone(texel);
                    CountTexModeFallback(mode);
                    return false;
                }

                return SampleInto(stage, stageDots[stage - 1], dotSt, texel);
            case PshCombiner.TexModeDotRflctDiff:
                return ResolveDotReflection(stage, coords, texels, texel, specular: false);
            case PshCombiner.TexModeDotRflctSpec:
                return ResolveDotReflection(stage, coords, texels, texel, specular: true);
            case PshCombiner.TexModeDotStrCube:
                return ResolveDotStrCube(stage, coords, texels, texel);
            case PshCombiner.TexModeDotStr3D:
                if (!TryStageDot(stage, coords, texels, stageDots, out _))
                {
                    SetTexelNone(texel);
                    CountTexModeFallback(mode);
                    return false;
                }

                return SampleInto(stage, stageDots[1], stageDots[2], texel);
            default:
                SetTexelNone(texel);
                CountTexModeFallback(mode);
                return false;
        }
    }

    private void CountTexModeFallback(int mode)
    {
        TexModeFallbacks++;
        _texModeFallbackByMode[mode & 0x1F]++;
        _textureStateFallbacks[_currentTextureState]++;
    }

    private void EnterTextureState(bool combinerActive, int programStart, int programLength)
    {
        var key = new System.Text.StringBuilder(200);
        key.Append(combinerActive ? 'c' : 'l');
        key.Append(CultureInfo.InvariantCulture, $"|p{programStart}+{programLength}");
        for (int slot = 0; slot < MaxTextureSlots; slot++)
        {
            bool enabled = (_textureControl0[slot] & TextureControl0Enable) != 0;
            key.Append(CultureInfo.InvariantCulture,
                $"|{(enabled ? 1 : 0)}:{(_textureFormat[slot] >> 8) & 0x7Fu:X2}:{_textureOffset[slot]:X8}:{_textureImageRect[slot]:X8}:{_textureControl1[slot]:X8}:{_textureAddress[slot]:X8}");
        }

        key.Append(CultureInfo.InvariantCulture,
            $"|m{_combiner.DecodeTexMode(0):X}{_combiner.DecodeTexMode(1):X}{_combiner.DecodeTexMode(2):X}{_combiner.DecodeTexMode(3):X}");
        key.Append(CultureInfo.InvariantCulture, $"|s{_combiner.StageCount}|f{(_combiner.FinalEnabled ? 1 : 0)}");
        string text = key.ToString();
        if (!_textureStateIndex.TryGetValue(text, out int index))
        {
            if (_textureStateKeys.Count < TextureStateCap - 1)
            {
                index = _textureStateKeys.Count;
                _textureStateKeys.Add(text);
            }
            else
            {
                index = TextureStateCap - 1;
                if (_textureStateKeys.Count == TextureStateCap - 1)
                {
                    _textureStateKeys.Add("(other)");
                }
            }

            _textureStateIndex[text] = index;
        }

        _currentTextureState = index;
        BuildSamplerPlans();
    }

    private void TrackTextureCoords(float u, float v)
    {
        int index = _currentTextureState;
        if (_textureStateSamples[index] + _textureStateMisses[index] == 0)
        {
            _textureStateMinU[index] = u;
            _textureStateMaxU[index] = u;
            _textureStateMinV[index] = v;
            _textureStateMaxV[index] = v;
            return;
        }

        if (u < _textureStateMinU[index])
        {
            _textureStateMinU[index] = u;
        }

        if (u > _textureStateMaxU[index])
        {
            _textureStateMaxU[index] = u;
        }

        if (v < _textureStateMinV[index])
        {
            _textureStateMinV[index] = v;
        }

        if (v > _textureStateMaxV[index])
        {
            _textureStateMaxV[index] = v;
        }
    }

    private bool CountSample(uint address, uint texel, float r, float g, float b)
    {
        TextureSampleSuccesses++;
        bool rgbZero = r == 0f && g == 0f && b == 0f;
        if (rgbZero)
        {
            TextureSampleRgbZero++;
        }

        int index = _currentTextureState;
        if (_textureStateSamples[index] == 0)
        {
            _textureStateFirstAddress[index] = address;
            _textureStateFirstTexel[index] = texel;
        }

        _textureStateSamples[index]++;
        if (rgbZero)
        {
            _textureStateRgbZero[index]++;
        }

        return true;
    }

    /// <summary>Gets per-texture-state sample statistics for the last run. Diagnostics.</summary>
    public List<(string Key, long Samples, long RgbZero, long Fallbacks, long Misses, uint FirstAddress, uint FirstTexel
        , float MinU, float MaxU, float MinV, float MaxV)> TextureStateStats
    {
        get
        {
            var list =
                new List<(string Key, long Samples, long RgbZero, long Fallbacks, long Misses, uint FirstAddress, uint
                    FirstTexel, float MinU, float MaxU, float MinV, float MaxV)>();
            for (int i = 0; i < _textureStateKeys.Count; i++)
            {
                list.Add((
                    _textureStateKeys[i],
                    _textureStateSamples[i],
                    _textureStateRgbZero[i],
                    _textureStateFallbacks[i],
                    _textureStateMisses[i],
                    _textureStateFirstAddress[i],
                    _textureStateFirstTexel[i],
                    _textureStateMinU[i],
                    _textureStateMaxU[i],
                    _textureStateMinV[i],
                    _textureStateMaxV[i]));
            }

            return list;
        }
    }

    /// <summary>
    /// xemu DOT_RFLCT_DIFF (stage 2) / DOT_RFLCT_SPEC (stage 3): builds a
    /// direction from the dot chain and cube/2D-samples it. Diffuse uses
    /// n = (dot1, dot2, dot3') where dot3' dots stage 3's coords against
    /// stage 3's input; specular forms n = (dot1, dot2, dot3) and reflects
    /// the eye vector e = (oT1.w, oT2.w, oT3.w):
    /// rv = 2*n*dot(n,e)/dot(n,n) - e.
    /// </summary>
    private bool ResolveDotReflection(int stage, float[][] coords, float[][] texels, float[] texel, bool specular)
    {
        float nx;
        float ny;
        float nz;
        if (!specular)
        {
            // Diffuse (stage 2): n = (dot1, dot2, dot_i_n) where the third
            // component is computed against stage 3's coords/dot map/input
            // (xemu's dot%d_n). Requires input texels from earlier stages.
            if (!TryComputeDot(stage - 1, coords, texels, out nx) ||
                !TryComputeDot(stage, coords, texels, out ny) ||
                !TryComputeDot(stage + 1, coords, texels, out nz))
            {
                SetTexelNone(texel);
                TexModeFallbacks++;
                return false;
            }

            return SampleCubeOrRemap(stage, nx, ny, nz, texel);
        }

        // Specular (stage 3): n = (dot1, dot2, dot3), eye vector is the w
        // component of texture coords 1-3.
        if (!TryComputeDot(stage - 2, coords, texels, out nx) ||
            !TryComputeDot(stage - 1, coords, texels, out ny) ||
            !TryComputeDot(stage, coords, texels, out nz))
        {
            SetTexelNone(texel);
            TexModeFallbacks++;
            return false;
        }

        float ex = coords[1][3];
        float ey = coords[2][3];
        float ez = coords[3][3];
        float nDotN = (nx * nx) + (ny * ny) + (nz * nz);
        float scale = (2f * ((nx * ex) + (ny * ey) + (nz * ez))) / nDotN;
        return SampleCubeOrRemap(stage, (scale * nx) - ex, (scale * ny) - ey, (scale * nz) - ez, texel);
    }

    /// <summary>
    /// xemu DOT_STR_CUBE (stage 3): direction = (dot1, dot2, dot3), cube/2D-sampled.
    /// </summary>
    private bool ResolveDotStrCube(int stage, float[][] coords, float[][] texels, float[] texel)
    {
        if (!TryComputeDot(stage - 2, coords, texels, out float nx) ||
            !TryComputeDot(stage - 1, coords, texels, out float ny) ||
            !TryComputeDot(stage, coords, texels, out float nz))
        {
            SetTexelNone(texel);
            TexModeFallbacks++;
            return false;
        }

        return SampleCubeOrRemap(stage, nx, ny, nz, texel);
    }

    private bool SampleCubeOrRemap(int stage, float x, float y, float z, float[] texel)
    {
        if (IsTextureCubemap(stage))
        {
            return TrySampleCubeDirection(stage, x, y, z, texel);
        }

        PshCombiner.CubeRemapTo2D(x, y, z, out float u, out float v);
        return SampleInto(stage, u, v, texel);
    }

    private bool TrySampleCubeDirection(int stage, float x, float y, float z, float[] texel)
    {
        if (!IsSupportedSwizzledFormat(stage))
        {
            // Linear cube maps are invalid in the reference (assert), and no
            // other cube format is backed by the software sampler. A disabled
            // or empty texture is not a mode failure.
            if (IsTextureSamplingConfigured(stage))
            {
                TexModeFallbacks++;
            }

            SetTexelNone(texel);
            return false;
        }

        if (TrySampleTextureCube(stage, x, y, z, out float r, out float g, out float b, out float a))
        {
            texel[0] = r;
            texel[1] = g;
            texel[2] = b;
            texel[3] = a;
            return true;
        }

        SetTexelNone(texel);
        return false;
    }

    private bool IsTextureCubemap(int stage)
    {
        return (_textureFormat[stage] & TextureFormatCubemapEnable) != 0;
    }

    private int TextureDimensionality(int stage)
    {
        return (int)((_textureFormat[stage] & TextureFormatDimensionality) >> 6);
    }

    private bool IsSupportedSwizzledFormat(int stage)
    {
        uint color = (_textureFormat[stage] >> 8) & 0xFFu;

        // 0x05/0x06 are the formats the reference renderer backs; the SZ
        // byte-order variants (0x3A-0x3C) are the same swizzled layouts with
        // the channels permuted (207h/208g). Cube sampling has THREE gates -
        // this list, SamplerPlan.CubeBytes and the swizzled texel decode - so
        // they are widened together; widening this one alone makes the
        // sampler return false before any fallback counter moves.
        return color is 0x05u or 0x06u or 0x3Au or 0x3Bu or 0x3Cu;
    }

    private static void SetTexelZero(float[] texel)
    {
        texel[0] = 0f;
        texel[1] = 0f;
        texel[2] = 0f;
        texel[3] = 0f;
    }

    /// <summary>
    /// xemu CLIPPLANE: for stage i, component j, the compare-mode bit
    /// (SHADERCLIPMODE bit 4*i+j) selects discard when pT[j] &gt;= 0, else
    /// when pT[j] &lt; 0. Any of the four components can kill the fragment.
    /// </summary>
    private bool ClipPlaneDiscards(int stage, float[] coord)
    {
        for (int j = 0; j < 4; j++)
        {
            bool discardOnNonNegative = ((_shaderClipMode >> ((4 * stage) + j)) & 1u) != 0;
            if (discardOnNonNegative ? coord[j] >= 0f : coord[j] < 0f)
            {
                return true;
            }
        }

        return false;
    }

    private static void SetTexelNone(float[] texel)
    {
        texel[0] = 0f;
        texel[1] = 0f;
        texel[2] = 0f;
        texel[3] = 1f;
    }

    private bool SampleInto(int stage, float u, float v, float[] texel)
    {
        if (TrySampleTexture(stage, u, v, out float r, out float g, out float b, out float a))
        {
            texel[0] = r;
            texel[1] = g;
            texel[2] = b;
            texel[3] = a;
            return true;
        }

        SetTexelNone(texel);
        return false;
    }

    /// <summary>1-2-1 gaussian kernel used by the QUINCUNX/GAUSSIAN_3 convolution.</summary>
    private static readonly float[] ConvolutionWeights =
    {
        1f / 16f, 2f / 16f, 1f / 16f,
        2f / 16f, 4f / 16f, 2f / 16f,
        1f / 16f, 2f / 16f, 1f / 16f,
    };

    /// <summary>Texel offsets of the 3x3 convolution, row-major.</summary>
    private static readonly int[] ConvolutionOffsets =
    {
        -1, -1, 0, -1, 1, -1,
        -1, 0, 0, 0, 1, 0,
        -1, 1, 0, 1, 1, 1,
    };

    /// <summary>
    /// TEXFILTER convolution (opt-in): xemu <c>apply_convolution_filter</c>, a
    /// nine-tap textureProj blur with the 1-2-1 kernel. The tap offsets are
    /// one texel of the stage's base level, expressed in the same normalized
    /// coordinates textureProj divides by w.
    /// </summary>
    private bool SampleConvolved(int stage, float tu, float tv, float w, float[] texel)
    {
        ref readonly SamplerPlan plan = ref _samplerPlans[stage];
        float invW = w != 0f ? 1f / w : 1f;

        // One tap is one texel in the stage's own coordinate space. The
        // reference normalizes a rect (LU_IMAGE_*) coordinate first and then
        // offsets by 1/textureSize (psh.c apply_convolution_filter: norm%d +
        // texCoordDelta/textureSize), which in this sampler's texel-unit space
        // for linear formats is a delta of exactly one texel; a swizzled stage
        // is already normalized, so there the delta is 1/size.
        float du = plan.Linear ? invW : invW / Math.Max(plan.Width, 1);
        float dv = plan.Linear ? invW : invW / Math.Max(plan.Height, 1);
        float bu = tu * invW;
        float bv = tv * invW;
        float ar = 0f;
        float ag = 0f;
        float ab = 0f;
        float aa = 0f;
        bool sampled = false;
        for (int i = 0; i < 9; i++)
        {
            float weight = ConvolutionWeights[i];
            float u = bu + (ConvolutionOffsets[i * 2] * du);
            float v = bv + (ConvolutionOffsets[(i * 2) + 1] * dv);
            if (TrySampleTexture(stage, u, v, out float r, out float g, out float b, out float a))
            {
                ar += r * weight;
                ag += g * weight;
                ab += b * weight;
                aa += a * weight;
                sampled = true;
            }
        }

        if (!sampled)
        {
            SetTexelNone(texel);
            return false;
        }

        texel[0] = ar;
        texel[1] = ag;
        texel[2] = ab;
        texel[3] = aa;
        return true;
    }

    /// <summary>
    /// Samples a stage's swizzled volume texture at the given normalized
    /// coordinates (xemu <c>unswizzle_box</c> layout, level 0). Linear and
    /// compressed volume formats are not backed; when one is bound the texel
    /// degrades to (0,0,0,1). A disabled or empty texture is not a mode
    /// failure and does not count a fallback.
    /// </summary>
    private bool SampleInto3D(int stage, float u, float v, float w, float[] texel)
    {
        if (!IsSupportedSwizzledFormat(stage))
        {
            if (IsTextureSamplingConfigured(stage))
            {
                SetTexelNone(texel);
                TexModeFallbacks++;
                return false;
            }

            SetTexelNone(texel);
            return false;
        }

        if (TrySampleTexture3D(stage, u, v, w, out float r, out float g, out float b, out float a))
        {
            texel[0] = r;
            texel[1] = g;
            texel[2] = b;
            texel[3] = a;
            return true;
        }

        SetTexelNone(texel);
        return false;
    }

    private bool IsTextureSamplingConfigured(int stage)
    {
        return _memory is not null &&
               (_textureControl0[stage] & TextureControl0Enable) != 0 &&
               (_textureImageRect[stage] >> 16) != 0 &&
               (_textureImageRect[stage] & 0xFFFFu) != 0;
    }

    private bool TryDependentInput(int stage, float[][] texels, out float[] previous)
    {
        int input = _combiner.DecodeInputTex(stage);
        if (input is < 0 or >= PshCombiner.TextureSlots)
        {
            previous = texels[0];
            return false;
        }

        previous = texels[input];
        return true;
    }

    private bool ResolveDependent(int stage, int mode, float[][] texels, float[] texel)
    {
        if (!TryDependentInput(stage, texels, out float[] previous))
        {
            SetTexelNone(texel);
            CountTexModeFallback(mode);
            return false;
        }

        float u = mode == PshCombiner.TexModeDpndntAr ? previous[3] : previous[1];
        float v = mode == PshCombiner.TexModeDpndntAr ? previous[0] : previous[2];
        return SampleInto(stage, u, v, texel);
    }

    private bool ResolveBump(int stage, int mode, float tu, float tv, float[][] texels, float[] texel)
    {
        if (!TryDependentInput(stage, texels, out float[] previous))
        {
            SetTexelNone(texel);
            CountTexModeFallback(mode);
            return false;
        }

        // Unsigned textures take the sign3 path (no SNORM textures exist here).
        float dx = PshCombiner.Sign3(previous[2]);
        float dy = PshCombiner.Sign3(previous[1]);
        float[] matrix = _bumpMatrices[stage - 1];
        float ox = (matrix[0] * dx) + (matrix[2] * dy);
        float oy = (matrix[1] * dx) + (matrix[3] * dy);
        if (!SampleInto(stage, tu + ox, tv + oy, texel))
        {
            return false;
        }

        if (mode == PshCombiner.TexModeBumpEnvMapLum)
        {
            float luminance = previous[0];
            float scale = (_bumpScales[stage - 1] * luminance) + _bumpOffsets[stage - 1];
            texel[0] *= scale;
            texel[1] *= scale;
            texel[2] *= scale;
            texel[3] *= scale;
        }

        return true;
    }

    private bool TryStageDot(int stage, float[][] coords, float[][] texels, float[] stageDots, out float dot)
    {
        if (!TryComputeDot(stage, coords, texels, out dot))
        {
            return false;
        }

        stageDots[stage] = dot;
        return true;
    }

    /// <summary>
    /// Computes xemu's per-stage dot value: dot(pT.xyz, dotmap(t[input_tex])),
    /// without storing the stageDots chain. Used by the dot texture modes and
    /// by the reflection modes, which need earlier/next stage dots.
    /// </summary>
    private bool TryComputeDot(int stage, float[][] coords, float[][] texels, out float dot)
    {
        if (!TryDependentInput(stage, texels, out float[] previous))
        {
            dot = 0f;
            return false;
        }

        int map = _combiner.DecodeDotMap(stage);
        if (!PshCombiner.IsSupportedDotMap(map))
        {
            UnsupportedDotMaps++;
        }

        PshCombiner.ApplyDotMap(map, previous[0], previous[1], previous[2], previous[3], out float mx, out float my,
            out float mz);
        float[] coord = coords[stage];
        dot = (coord[0] * mx) + (coord[1] * my) + (coord[2] * mz);
        return true;
    }

    /// <summary>
    /// Rasterizer slice: compiled fill for the shape the menu spends 86% of
    /// its pixels in - kind-1 modulate combiner, one Project2D texture stage,
    /// no zclip/depth/stencil, opaque color mask, SrcAlpha/InvSrcAlpha/Add
    /// blend and a fast-layout sampler. The pixel body is the generic loop's
    /// arithmetic transcribed operation-for-operation (same barycentric
    /// expressions, same perspective renormalization, same interpolation
    /// order, same sampler and the same shared helpers for modulate, alpha
    /// test, blend, pack and write), with per-draw values in locals instead of
    /// fields and no combiner/tap/registry walks. Returns the inside-pixel
    /// count for the per-fill diagnostics.
    /// </summary>
    private long FillTrianglePshModulate(
        PshVertex a, PshVertex b, PshVertex c,
        uint x0, uint x1, uint y0, uint y1,
        float invArea, bool perspective, float invAw, float invBw, float invCw,
        uint pitch, ref readonly SamplerPlan texturing,
        bool alphaTest, uint alphaFunc, float alphaRefScaled, uint colorBase,
        bool zclipActive, bool zclipCull, float zclipRange, float zclipMin, float zclipMax,
        bool bilinear)
    {
        long pixels = 0;
        uint color = texturing.Color;

        // Per-fill sampler fast lane (exact): the fast folds keep the sampled
        // texel inside [0,Width) x [0,Height), so the highest address the
        // samples can reach is a per-fill bound. When that bound lies inside
        // RAM the loop reads the texel through the RAM array reference after
        // the same bounds test TryReadPhysical performs; anything else takes
        // the adapter path unchanged.
        byte[] ram = Array.Empty<byte>();
        uint ramLimit = 0;
        if (_memoryFast is not null && _memoryFast.TryGetRamBytes(out byte[] ramBytes, out uint ramSize) &&
            ramSize >= 4)
        {
            ram = ramBytes;
            ramLimit = ramSize - 4;
        }

        bool directTexel = ramLimit != 0 && SamplerAddressBound(in texturing) <= ramLimit;
        int decodeKind = texturing.DecodeKind;

        // The framebuffer read for the blend source is a direct RAM word when
        // the fill's whole address span is inside RAM (the same test the
        // adapter reader performs, hoisted per fill).
        long maxDest = (long)colorBase + ((long)y1 * pitch) + ((long)x1 * 4u);
        bool directDest = ramLimit != 0 && maxDest <= ramLimit;
        ref byte ramBase = ref MemoryMarshal.GetArrayDataReference(ram);

        for (uint y = y0; y <= y1; y++)
        {
            float py = y + 0.5f;
            float t0 = (b.X - a.X) * (py - a.Y);
            float t1 = (c.X - b.X) * (py - b.Y);
            uint rowBase = colorBase + (y * pitch);
            for (uint x = x0; x <= x1; x++)
            {
                float px = x + 0.5f;
                float w0 = (t0 - ((b.Y - a.Y) * (px - a.X))) * invArea;
                float w1 = (t1 - ((c.Y - b.Y) * (px - b.X))) * invArea;
                float w2 = 1f - w0 - w1;
                bool inside = (w0 >= 0f && w1 >= 0f && w2 >= 0f) ||
                              (w0 <= 0f && w1 <= 0f && w2 <= 0f);
                if (!inside)
                {
                    continue;
                }

                float p0 = w0;
                float p1 = w1;
                float p2 = w2;
                if (perspective)
                {
                    float s0 = w0 * invCw;
                    float s1 = w1 * invAw;
                    float s2 = w2 * invBw;
                    float sum = s0 + s1 + s2;
                    if (float.IsFinite(sum) && sum != 0f)
                    {
                        float invSum = 1f / sum;
                        p0 = s0 * invSum;
                        p1 = s1 * invSum;
                        p2 = s2 * invSum;
                    }
                }

                pixels++;
                float dr = (p1 * a.DiffuseR) + (p2 * b.DiffuseR) + (p0 * c.DiffuseR);
                float dg = (p1 * a.DiffuseG) + (p2 * b.DiffuseG) + (p0 * c.DiffuseG);
                float db = (p1 * a.DiffuseB) + (p2 * b.DiffuseB) + (p0 * c.DiffuseB);
                float da = (p1 * a.DiffuseA) + (p2 * b.DiffuseA) + (p0 * c.DiffuseA);
                float tu0 = (p1 * a.U0) + (p2 * b.U0) + (p0 * c.U0);
                float tv0 = (p1 * a.V0) + (p2 * b.V0) + (p0 * c.V0);
                float tw0 = (p1 * a.T0W) + (p2 * b.T0W) + (p0 * c.T0W);
                float su = tw0 != 0f ? tu0 / tw0 : tu0;
                float sv = tw0 != 0f ? tv0 / tw0 : tv0;

                _textureFormatAttempts[color]++;
                float tr;
                float tg;
                float tb;
                float ta;

                if (bilinear)
                {
                    // TEXFILTER tent (SampleBilinear transcribed): the
                    // texel-center-offset coordinate, four independently
                    // folded taps and the bilinear weights. One sampler
                    // counter per fragment (the base tap), as the generic
                    // path records it.
                    float fx = (texturing.Linear ? su : su * texturing.Width) - 0.5f;
                    float fy = (texturing.Linear ? sv : sv * texturing.Height) - 0.5f;
                    int tx0 = (int)MathF.Floor(fx);
                    int ty0 = (int)MathF.Floor(fy);
                    float wx = fx - tx0;
                    float wy = fy - ty0;
                    ReadTapFast(ref ramBase, directTexel, tx0, ty0, in texturing,
                        out float r00, out float g00, out float b00, out float a00,
                        out uint baseAddress, out uint baseTexel);
                    ReadTapFast(ref ramBase, directTexel, tx0 + 1, ty0, in texturing,
                        out float r10, out float g10, out float b10, out float a10, out _, out _);
                    ReadTapFast(ref ramBase, directTexel, tx0, ty0 + 1, in texturing,
                        out float r01, out float g01, out float b01, out float a01, out _, out _);
                    ReadTapFast(ref ramBase, directTexel, tx0 + 1, ty0 + 1, in texturing,
                        out float r11, out float g11, out float b11, out float a11, out _, out _);
                    float w00 = (1f - wx) * (1f - wy);
                    float w10 = wx * (1f - wy);
                    float w01 = (1f - wx) * wy;
                    float w11 = wx * wy;
                    tr = (r00 * w00) + (r10 * w10) + (r01 * w01) + (r11 * w11);
                    tg = (g00 * w00) + (g10 * w10) + (g01 * w01) + (g11 * w11);
                    tb = (b00 * w00) + (b10 * w10) + (b01 * w01) + (b11 * w11);
                    ta = (a00 * w00) + (a10 * w10) + (a01 * w01) + (a11 * w11);
                    TrackTextureCoords(tx0, ty0);
                    CountSample(baseAddress, baseTexel, tr, tg, tb);
                }
                else
                {
                    // SampleFast transcribed with the per-fill direct-RAM texel
                    // read and the per-fill decode branch (see SamplerPlan).
                    int rawX = (int)MathF.Floor(texturing.Linear ? su : su * texturing.Width);
                    int rawY = (int)MathF.Floor(texturing.Linear ? sv : sv * texturing.Height);
                    int sx = WrapFast(rawX, texturing.Width, texturing.FastWrapU, texturing.WrapMaskU,
                        texturing.MirrorMaskU);
                    int sy = WrapFast(rawY, texturing.Height, texturing.FastWrapV, texturing.WrapMaskV,
                        texturing.MirrorMaskV);
                    uint taddr;
                    if (texturing.Linear)
                    {
                        taddr = texturing.Offset + (uint)((sy * texturing.Pitch) + (sx * texturing.FastBytes));
                    }
                    else if (texturing.MortonMasks)
                    {
                        uint swizzled = (SpreadMortonBits((uint)sx) & texturing.SwizzleMaskX)
                                      | ((SpreadMortonBits((uint)sy) << 1) & texturing.SwizzleMaskY);
                        taddr = texturing.Offset + (swizzled * (uint)texturing.FastBytes);
                    }
                    else
                    {
                        taddr = texturing.Offset + ((InterleaveBits((uint)sx, texturing.SwizzleMaskX) |
                                                     InterleaveBits((uint)sy, texturing.SwizzleMaskY)) *
                                                    (uint)texturing.FastBytes);
                    }

                    uint texel;
                    if (directTexel)
                    {
                        texel = BinaryPrimitives.ReadUInt32LittleEndian(
                            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref ramBase, (nint)taddr), 4));
                        if (texturing.FastBytes != 4)
                        {
                            texel &= 0xFFFFu;
                        }
                    }
                    else
                    {
                        texel = texturing.FastBytes == 4 ? ReadGpu(taddr) : ReadGpu(taddr) & 0xFFFFu;
                    }

                    if (decodeKind == 0)
                    {
                        // DecodeTextureTexel's default branch (A8R8G8B8).
                        ta = Unit255[(int)((texel >> 24) & 0xFFu)];
                        tr = Unit255[(int)((texel >> 16) & 0xFFu)];
                        tg = Unit255[(int)((texel >> 8) & 0xFFu)];
                        tb = Unit255[(int)(texel & 0xFFu)];
                    }
                    else if (decodeKind == 1)
                    {
                        // DecodeTextureTexel's A4R4G4B4 branch.
                        tr = Unit15[(int)((texel >> 8) & 0xFu)];
                        tg = Unit15[(int)((texel >> 4) & 0xFu)];
                        tb = Unit15[(int)(texel & 0xFu)];
                        ta = Unit15[(int)((texel >> 12) & 0xFu)];
                    }
                    else
                    {
                        DecodeTextureTexel(color, texel, out tr, out tg, out tb, out ta);
                    }

                    TrackTextureCoords(sx, sy);
                    CountSample(taddr, texel, tr, tg, tb);
                }

                float fr = PshCombiner.ModulatePass(Math.Max(tr, 0f), Math.Max(dr, 0f));
                float fg = PshCombiner.ModulatePass(Math.Max(tg, 0f), Math.Max(dg, 0f));
                float fb = PshCombiner.ModulatePass(Math.Max(tb, 0f), Math.Max(db, 0f));
                float fa = PshCombiner.ModulatePass(Math.Max(ta, 0f), Math.Max(da, 0f));
                TexturedPixels++;
                if (fr == 0f && fg == 0f && fb == 0f)
                {
                    _combinerRgbZeroFragments++;
                    if (fa > 0f)
                    {
                        _combinerRgbZeroAlphaFragments++;
                    }
                }

                // ZCLIP runs at the top of the reference fragment shader, after
                // the stage resolve and before the alpha test (xemu psh.c); the
                // clamped depth itself is dead here because no depth/stencil
                // state is active. Culled fragments keep their sampler and
                // combiner counters exactly as the generic path does.
                if (zclipActive)
                {
                    float depth = (w1 * a.Z) + (w2 * b.Z) + (w0 * c.Z);
                    float zvalue = depth * zclipRange;
                    if (zvalue < zclipMin || zvalue > zclipMax)
                    {
                        if (zclipCull)
                        {
                            ZclipCulledPixels++;
                            continue;
                        }

                        ZclipClampedPixels++;
                    }
                }

                if (alphaTest && !CompareFunc(alphaFunc, fa, alphaRefScaled))
                {
                    continue;
                }

                uint address = rowBase + (x * 4u);
                (float R, float G, float B, float A) src = (fr, fg, fb, fa);
                (float R, float G, float B, float A) dst = (0f, 0f, 0f, 1f);
                if (directDest)
                {
                    // TryReadPixelFloat's 4-byte branch on the direct RAM word.
                    uint pixel = BinaryPrimitives.ReadUInt32LittleEndian(
                        MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref ramBase, (nint)address), 4));
                    dst = (Unit255[(int)((pixel >> 16) & 0xFFu)], Unit255[(int)((pixel >> 8) & 0xFFu)],
                        Unit255[(int)(pixel & 0xFFu)], Unit255[(int)((pixel >> 24) & 0xFFu)]);
                }
                else if (TryReadPixelFloat(address, 4, out float sr, out float sg, out float sb, out float sa))
                {
                    dst = (sr, sg, sb, sa);
                }

                (float R, float G, float B, float A) result = BlendSrcAlphaAdd(src, dst);
                WritePixel(address, 4, PackPixel(4, result.R, result.G, result.B, result.A));
                BlendedPixels++;
                DrawnPixels++;
            }
        }

        return pixels;
    }

    /// <summary>
    /// Rasterizer slice: compiled fill for the k2/k3 "squared lighting"
    /// combiner shapes (the whole triangle raster and 22% of the menu). The
    /// per-fill predicate proves that stages 1-3 of <see cref="ResolveTexel"/>
    /// resolve to the constant <c>SetTexelNone</c> texel with no counter
    /// updates (unconfigured cube/volume textures), so the compiled loop only
    /// resolves stage 0 and carries the constant texels as scalars. The
    /// combiner math is <see cref="PshCombiner.EvaluateLightingSpecialized"/>
    /// transcribed operation-for-operation with its per-draw constants hoisted;
    /// the fragment stage is the ProcessFragment color path with the proven
    /// per-draw state (no alpha test, no blend, opaque mask, no zeta traffic).
    /// </summary>
    /// <summary>Writes one stage's per-fill constant texel (RGB) into the lighting scratch.</summary>
    private void SetConstTexel(int stage, float r, float g, float b)
    {
        int offset = (stage - 1) * 3;
        _lightTexels[offset] = r;
        _lightTexels[offset + 1] = g;
        _lightTexels[offset + 2] = b;
    }

    private long FillTrianglePshLighting(
        PshVertex a, PshVertex b, PshVertex c,
        uint x0, uint x1, uint y0, uint y1,
        float invArea, bool perspective, float invAw, float invBw, float invCw,
        uint pitch, int kind, float[][] consts, float[] constTexels,
        bool sampleStage0, ref readonly SamplerPlan stage0Plan, bool bilinear,
        bool zclipActive, bool zclipCull, float zclipRange, float zclipMin, float zclipMax,
        uint colorBase)
    {
        bool stage0FromC0 = kind == 2;
        int powerStages = stage0FromC0 ? 5 : 4;
        int combineStage = stage0FromC0 ? 6 : 5;
        int addStage = stage0FromC0 ? 7 : 6;
        float[] c0Stage0 = consts[0];
        float[] c0Combine = consts[combineStage * 2];
        float[] c1Combine = consts[(combineStage * 2) + 1];
        float[] c0Add = consts[addStage * 2];
        float[] specFog0 = consts[16];
        float c0s0R = c0Stage0[0];
        float c0s0G = c0Stage0[1];
        float c0s0B = c0Stage0[2];
        float c0cR = c0Combine[0];
        float c0cG = c0Combine[1];
        float c0cB = c0Combine[2];
        float c1cR = c1Combine[0];
        float c1cG = c1Combine[1];
        float c1cB = c1Combine[2];
        float c0aR = c0Add[0];
        float c0aG = c0Add[1];
        float c0aB = c0Add[2];
        float sfR = specFog0[0];
        float sfG = specFog0[1];
        float sfB = specFog0[2];
        float sfA = specFog0[3];
        float b0 = (2f * Math.Max(constTexels[0], 0f)) - 1f;
        float b1 = (2f * Math.Max(constTexels[1], 0f)) - 1f;
        float b2 = (2f * Math.Max(constTexels[2], 0f)) - 1f;
        float d0 = (2f * Math.Max(constTexels[3], 0f)) - 1f;
        float d1 = (2f * Math.Max(constTexels[4], 0f)) - 1f;
        float d2 = (2f * Math.Max(constTexels[5], 0f)) - 1f;
        float bxR = Math.Max(constTexels[6], 0f);
        float bxG = Math.Max(constTexels[7], 0f);
        float bxB = Math.Max(constTexels[8], 0f);
        uint color = stage0Plan.Color;

        // Per-fill hoists (exact): the add-stage constants pass through
        // clamp(1 * c, -1..1), i.e. clamp(c), with no per-fragment input; the
        // barycentric y terms depend on y only and are loop invariant in x.
        float addCdR = Math.Clamp(1f * c0aR, -1f, 1f);
        float addCdG = Math.Clamp(1f * c0aG, -1f, 1f);
        float addCdB = Math.Clamp(1f * c0aB, -1f, 1f);

        // Per-fill sampler fast lane (see the modulate compiled fill): the
        // direct RAM texel read and the decode branch are selected once.
        byte[] ram = Array.Empty<byte>();
        uint ramLimit = 0;
        if (_memoryFast is not null && _memoryFast.TryGetRamBytes(out byte[] ramBytes, out uint ramSize) &&
            ramSize >= 4)
        {
            ram = ramBytes;
            ramLimit = ramSize - 4;
        }

        bool directTexel = ramLimit != 0 && SamplerAddressBound(in stage0Plan) <= ramLimit;
        int decodeKind = stage0Plan.DecodeKind;
        ref byte ramBase = ref MemoryMarshal.GetArrayDataReference(ram);
        long pixels = 0;
        for (uint y = y0; y <= y1; y++)
        {
            float py = y + 0.5f;
            float t0 = (b.X - a.X) * (py - a.Y);
            float t1 = (c.X - b.X) * (py - b.Y);
            for (uint x = x0; x <= x1; x++)
            {
                float px = x + 0.5f;
                float w0 = (t0 - ((b.Y - a.Y) * (px - a.X))) * invArea;
                float w1 = (t1 - ((c.Y - b.Y) * (px - b.X))) * invArea;
                float w2 = 1f - w0 - w1;
                bool inside = (w0 >= 0f && w1 >= 0f && w2 >= 0f) ||
                              (w0 <= 0f && w1 <= 0f && w2 <= 0f);
                if (!inside)
                {
                    continue;
                }

                float p0 = w0;
                float p1 = w1;
                float p2 = w2;
                if (perspective)
                {
                    float s0 = w0 * invCw;
                    float s1 = w1 * invAw;
                    float s2 = w2 * invBw;
                    float sum = s0 + s1 + s2;
                    if (float.IsFinite(sum) && sum != 0f)
                    {
                        float invSum = 1f / sum;
                        p0 = s0 * invSum;
                        p1 = s1 * invSum;
                        p2 = s2 * invSum;
                    }
                }

                pixels++;
                float dr = (p1 * a.DiffuseR) + (p2 * b.DiffuseR) + (p0 * c.DiffuseR);
                float dg = (p1 * a.DiffuseG) + (p2 * b.DiffuseG) + (p0 * c.DiffuseG);
                float db = (p1 * a.DiffuseB) + (p2 * b.DiffuseB) + (p0 * c.DiffuseB);

                // Stage 0 (k3): Project2D 2D sample at (oT0.x / oT0.w, oT0.y / oT0.w).
                float t0r = 0f;
                float t0g = 0f;
                float t0b = 0f;
                if (sampleStage0)
                {
                    float tu0 = (p1 * a.U0) + (p2 * b.U0) + (p0 * c.U0);
                    float tv0 = (p1 * a.V0) + (p2 * b.V0) + (p0 * c.V0);
                    float tw0 = (p1 * a.T0W) + (p2 * b.T0W) + (p0 * c.T0W);
                    float su = tw0 != 0f ? tu0 / tw0 : tu0;
                    float sv = tw0 != 0f ? tv0 / tw0 : tv0;
                    _textureFormatAttempts[color]++;

                    if (bilinear)
                    {
                        // TEXFILTER tent (SampleBilinear transcribed; same
                        // expressions as the modulate compiled fill).
                        float fx = (stage0Plan.Linear ? su : su * stage0Plan.Width) - 0.5f;
                        float fy = (stage0Plan.Linear ? sv : sv * stage0Plan.Height) - 0.5f;
                        int tx0 = (int)MathF.Floor(fx);
                        int ty0 = (int)MathF.Floor(fy);
                        float wx = fx - tx0;
                        float wy = fy - ty0;
                        ReadTapFast(ref ramBase, directTexel, tx0, ty0, in stage0Plan,
                            out float r00, out float g00, out float b00, out _,
                            out uint baseAddress, out uint baseTexel);
                        ReadTapFast(ref ramBase, directTexel, tx0 + 1, ty0, in stage0Plan,
                            out float r10, out float g10, out float b10, out _, out _, out _);
                        ReadTapFast(ref ramBase, directTexel, tx0, ty0 + 1, in stage0Plan,
                            out float r01, out float g01, out float b01, out _, out _, out _);
                        ReadTapFast(ref ramBase, directTexel, tx0 + 1, ty0 + 1, in stage0Plan,
                            out float r11, out float g11, out float b11, out _, out _, out _);
                        float w00 = (1f - wx) * (1f - wy);
                        float w10 = wx * (1f - wy);
                        float w01 = (1f - wx) * wy;
                        float w11 = wx * wy;
                        t0r = (r00 * w00) + (r10 * w10) + (r01 * w01) + (r11 * w11);
                        t0g = (g00 * w00) + (g10 * w10) + (g01 * w01) + (g11 * w11);
                        t0b = (b00 * w00) + (b10 * w10) + (b01 * w01) + (b11 * w11);
                        TrackTextureCoords(tx0, ty0);
                        CountSample(baseAddress, baseTexel, t0r, t0g, t0b);
                        TexturedPixels++;
                    }
                    else
                    {
                        // SampleFast transcribed with the per-fill direct-RAM texel
                        // read and decode branch (same expressions as the modulate
                        // compiled fill and as SampleFast).
                        int rawX = (int)MathF.Floor(stage0Plan.Linear ? su : su * stage0Plan.Width);
                        int rawY = (int)MathF.Floor(stage0Plan.Linear ? sv : sv * stage0Plan.Height);
                        int sx = WrapFast(rawX, stage0Plan.Width, stage0Plan.FastWrapU, stage0Plan.WrapMaskU,
                            stage0Plan.MirrorMaskU);
                        int sy = WrapFast(rawY, stage0Plan.Height, stage0Plan.FastWrapV, stage0Plan.WrapMaskV,
                            stage0Plan.MirrorMaskV);
                        uint taddr;
                        if (stage0Plan.Linear)
                        {
                            taddr = stage0Plan.Offset +
                                    (uint)((sy * stage0Plan.Pitch) + (sx * stage0Plan.FastBytes));
                        }
                        else if (stage0Plan.MortonMasks)
                        {
                            uint swizzled = (SpreadMortonBits((uint)sx) & stage0Plan.SwizzleMaskX)
                                          | ((SpreadMortonBits((uint)sy) << 1) & stage0Plan.SwizzleMaskY);
                            taddr = stage0Plan.Offset + (swizzled * (uint)stage0Plan.FastBytes);
                        }
                        else
                        {
                            taddr = stage0Plan.Offset + ((InterleaveBits((uint)sx, stage0Plan.SwizzleMaskX) |
                                                          InterleaveBits((uint)sy, stage0Plan.SwizzleMaskY)) *
                                                         (uint)stage0Plan.FastBytes);
                        }

                        uint texel;
                        if (directTexel)
                        {
                            texel = BinaryPrimitives.ReadUInt32LittleEndian(
                                MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref ramBase, (nint)taddr), 4));
                            if (stage0Plan.FastBytes != 4)
                            {
                                texel &= 0xFFFFu;
                            }
                        }
                        else
                        {
                            texel = stage0Plan.FastBytes == 4 ? ReadGpu(taddr) : ReadGpu(taddr) & 0xFFFFu;
                        }

                        float ta0;
                        if (decodeKind == 0)
                        {
                            ta0 = Unit255[(int)((texel >> 24) & 0xFFu)];
                            t0r = Unit255[(int)((texel >> 16) & 0xFFu)];
                            t0g = Unit255[(int)((texel >> 8) & 0xFFu)];
                            t0b = Unit255[(int)(texel & 0xFFu)];
                        }
                        else if (decodeKind == 1)
                        {
                            t0r = Unit15[(int)((texel >> 8) & 0xFu)];
                            t0g = Unit15[(int)((texel >> 4) & 0xFu)];
                            t0b = Unit15[(int)(texel & 0xFu)];
                            ta0 = Unit15[(int)((texel >> 12) & 0xFu)];
                        }
                        else
                        {
                            DecodeTextureTexel(color, texel, out t0r, out t0g, out t0b, out ta0);
                        }

                        TrackTextureCoords(sx, sy);
                        CountSample(taddr, texel, t0r, t0g, t0b);
                        TexturedPixels++;
                    }
                }

                // Stages 1-3 are per-fill constants (SetTexelNone, or the
                // constant shadow result for a depth-format stage).
                float a0 = stage0FromC0 ? c0s0R : (2f * Math.Max(t0r, 0f)) - 1f;
                float a1 = stage0FromC0 ? c0s0G : (2f * Math.Max(t0g, 0f)) - 1f;
                float a2 = stage0FromC0 ? c0s0B : (2f * Math.Max(t0b, 0f)) - 1f;
                float r0R = Math.Clamp((a0 * b0) + (a1 * b1) + (a2 * b2), -1f, 1f);
                float r0G = r0R;
                float r0B = r0R;
                float r1R = Math.Clamp((a0 * d0) + (a1 * d1) + (a2 * d2), -1f, 1f);
                float r1G = r1R;
                float r1B = r1R;

                // The unsigned max-square keeps R1 in [0,1]; every
                // following signed square multiplies two values in [0,1],
                // so the -1..1 clamp of the reference expression is a
                // provable no-op (NaN passes through both forms
                // identically).
                float mR = Math.Max(r1R, 0f);
                float mG = Math.Max(r1G, 0f);
                float mB = Math.Max(r1B, 0f);
                r1R = mR * mR;
                r1G = mG * mG;
                r1B = mB * mB;
                for (int stage = 1; stage < powerStages; stage++)
                {
                    r1R *= r1R;
                    r1G *= r1G;
                    r1B *= r1B;
                }

                // |c0c|, |c1c| <= 1 is proven per fill by the compiled-fill
                // predicate: with max(r,0) in [0,1] each product stays
                // inside -1..1, so the reference clamps are no-ops here.
                float abR = c0cR * Math.Max(r0R, 0f);
                float abG = c0cG * Math.Max(r0G, 0f);
                float abB = c0cB * Math.Max(r0B, 0f);
                float cdR = c1cR * Math.Max(r1R, 0f);
                float cdG = c1cG * Math.Max(r1G, 0f);
                float cdB = c1cB * Math.Max(r1B, 0f);
                float v1R = Math.Clamp(abR + cdR, -1f, 1f);
                float v1G = Math.Clamp(abG + cdG, -1f, 1f);
                float v1B = Math.Clamp(abB + cdB, -1f, 1f);

                float addR = Math.Clamp(dr * Math.Max(v1R, 0f), -1f, 1f);
                float addG = Math.Clamp(dg * Math.Max(v1G, 0f), -1f, 1f);
                float addB = Math.Clamp(db * Math.Max(v1B, 0f), -1f, 1f);
                r0R = Math.Clamp(addR + addCdR, -1f, 1f);
                r0G = Math.Clamp(addG + addCdG, -1f, 1f);
                r0B = Math.Clamp(addB + addCdB, -1f, 1f);

                float efR = Math.Max(r0R, 0f) * Math.Max(sfR, 0f);
                float efG = Math.Max(r0G, 0f) * Math.Max(sfG, 0f);
                float efB = Math.Max(r0B, 0f) * Math.Max(sfB, 0f);
                float dxR = Math.Max(efR, 0f);
                float dxG = Math.Max(efG, 0f);
                float dxB = Math.Max(efB, 0f);
                float axR = Math.Max(r0R, 0f);
                float axG = Math.Max(r0G, 0f);
                float axB = Math.Max(r0B, 0f);
                float fr = dxR + (0f + ((bxR - 0f) * axR));
                float fg = dxG + (0f + ((bxG - 0f) * axG));
                float fb = dxB + (0f + ((bxB - 0f) * axB));
                float fa = Math.Max(sfA, 0f);

                if (fr == 0f && fg == 0f && fb == 0f)
                {
                    _combinerRgbZeroFragments++;
                    if (fa > 0f)
                    {
                        _combinerRgbZeroAlphaFragments++;
                    }
                }

                if (zclipActive)
                {
                    float depth = (w1 * a.Z) + (w2 * b.Z) + (w0 * c.Z);
                    float zvalue = depth * zclipRange;
                    if (zvalue < zclipMin || zvalue > zclipMax)
                    {
                        if (zclipCull)
                        {
                            ZclipCulledPixels++;
                            continue;
                        }

                        ZclipClampedPixels++;
                    }
                }

                uint address = colorBase + (y * pitch) + (x * 4u);
                WritePixel(address, 4, PackPixel(4, fr, fg, fb, fa));
                DrawnPixels++;
            }
        }

        return pixels;
    }

    private void FillTrianglePsh(
        PshVertex a, PshVertex b, PshVertex c,
        uint clipX, uint clipY, uint clipW, uint clipH, uint bytes, uint pitch,
        float[][] consts)
    {
        using GpuWriteBatch batch = new(_memoryFast);
        long diagPixels = 0;
        float area = ((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y));
        if (area == 0f)
        {
            return;
        }

        if (ShouldCull(area))
        {
            TrianglesCulled++;
            return;
        }

        uint zetaBytes = ZetaBytesPerPixel();
        uint zetaPitch = (_surfacePitch >> 16) & 0xFFFFu;
        _colorSurfaceBase = SurfaceBase(_dmaColor);
        _zetaSurfaceBase = SurfaceBase(_dmaZeta);
        PrepareFragmentConstants(zetaBytes);

        uint x0 = (uint)Math.Max(0f, Math.Min(Math.Min(a.X, b.X), c.X));
        uint x1 = (uint)Math.Min((float)clipX + clipW - 1, Math.Max(Math.Max(a.X, b.X), c.X));
        uint y0 = (uint)Math.Max(0f, Math.Min(Math.Min(a.Y, b.Y), c.Y));
        uint y1 = (uint)Math.Min((float)clipY + clipH - 1, Math.Max(Math.Max(a.Y, b.Y), c.Y));
        if (x1 < x0 || y1 < y0)
        {
            return;
        }

        IPgraphBackend? backend = Backend;
        if (backend is not null)
        {
            PgraphDrawInfo backendInfo = BuildDrawInfo(a, b, c, clipX, clipY, clipW, clipH, bytes, zetaBytes, combinerActive: true);
            if (backend.ReplaceMode)
            {
                if (backend.TryDraw(backendInfo))
                {
                    // Replacement mode: the backend owns the pixels for this
                    // draw; the software rasterizer is skipped entirely.
                    return;
                }
            }
            else
            {
                backend.Draw(backendInfo);
            }
        }

        float invArea = 1f / area;
        float invAw = a.W != 0f ? 1f / a.W : 0f;
        float invBw = b.W != 0f ? 1f / b.W : 0f;
        float invCw = c.W != 0f ? 1f / c.W : 0f;
        bool perspective = invAw != 0f && invBw != 0f && invCw != 0f && (a.W != b.W || b.W != c.W);

        // Reused per-fill scratch (fully re-initialized below; bit-identical
        // to the previous per-triangle allocations).
        float[] diffuse = _pshDiffuse;
        float[] specular = _pshSpecular;
        float[] fog = _pshFog;
        float[][] texels = _pshTexels;
        float[][] coords = _pshCoords;
        float[] stageDots = _pshStageDots;
        int[] texModes = _pshTexModes;
        foreach (float[] texel in texels)
        {
            SetTexelNone(texel);
        }

        fog[0] = _fogColor[0];
        fog[1] = _fogColor[1];
        fog[2] = _fogColor[2];
        texModes[0] = _combiner.DecodeTexMode(0);
        texModes[1] = _combiner.DecodeTexMode(1);
        texModes[2] = _combiner.DecodeTexMode(2);
        texModes[3] = _combiner.DecodeTexMode(3);
        bool anyClipPlane = false;
        bool anyStageDotReader = false;
        for (int i = 0; i < PshCombiner.TextureSlots; i++)
        {
            int mode = texModes[i];
            anyClipPlane |= mode == PshCombiner.TexModeClipPlane;
            anyStageDotReader |= mode == PshCombiner.TexModeDotSt || mode == PshCombiner.TexModeDotStr3D;
        }

        // Lean interpolation for kind-1 modulate fills whose stages 1-3 are
        // unbound: the modulate combiner consumes only the interpolated
        // diffuse, the stage-0 texcoords and depth, so the specular, stages
        // 1-3 texcoords and fog-factor interpolations (16 of 24 vectors) are
        // dead and skipped. All conditions are per-fill; the values that are
        // computed stay operation-for-operation identical.
        bool leanInterp = _combinerFastKind == 1 &&
                          texModes[1] == PshCombiner.TexModeNone &&
                          texModes[2] == PshCombiner.TexModeNone &&
                          texModes[3] == PshCombiner.TexModeNone;

        long rasterStart = RasterTimingEnabled ? Stopwatch.GetTimestamp() : 0L;

        // Compiled fill for the dominant modulate shape: when selected it owns
        // every pixel and the generic loop below runs zero iterations. Both
        // sampler shapes are served: the nearest tap and the TEXFILTER tent
        // (a convolution stays on the generic loop).
        bool compiledModulate = leanInterp &&
                                !DiagnosticGenericFill &&
                                _samplerPlans[0].Convolution == 0 &&
                                texModes[0] == PshCombiner.TexModeProject2D &&
                                bytes == 4 &&
                                !_depthTestEnable &&
                                !_stencilTestEnable &&
                                _colorMask == ColorMaskAll &&
                                _blendFastSrcAlpha &&
                                _memory is not null &&
                                _samplerPlans[0].FastKind != 0 &&
                                (_textureControl0[0] & TextureControl0Enable) != 0 &&
                                !IsTextureCubemap(0) &&
                                !IsDepthTextureFormat(_samplerPlans[0].Color);
        if (compiledModulate)
        {
            diagPixels = FillTrianglePshModulate(
                a, b, c, x0, x1, y0, y1,
                invArea, perspective, invAw, invBw, invCw,
                pitch, in _samplerPlans[0],
                _alphaTestEnable, _alphaFunc, _alphaRefScaled,
                _colorSurfaceBase + PresentedColorOffset,
                _zclipActive, _zclipCull, _zclipRange, _zclipMin, _zclipMax,
                _samplerPlans[0].Bilinear);
            _diagCompiledFills++;
            _diagCompiledPixels += diagPixels;
            if (RasterTimingEnabled)
            {
                _rasterCompiledTicks += Stopwatch.GetTimestamp() - rasterStart;
            }
        }
        else if (_combinerFastKind == 1 && RasterTimingEnabled)
        {
            int reason = 0;
            if (texModes[0] != PshCombiner.TexModeProject2D)
            {
                reason |= 1;
            }

            if (!leanInterp)
            {
                reason |= 2;
            }

            if (bytes != 4)
            {
                reason |= 4;
            }

            if (_depthTestEnable)
            {
                reason |= 8;
            }

            if (_stencilTestEnable)
            {
                reason |= 0x10;
            }

            if (_colorMask != ColorMaskAll)
            {
                reason |= 0x20;
            }

            if (!_blendFastSrcAlpha)
            {
                reason |= 0x40;
            }

            if (_memory is null)
            {
                reason |= 0x80;
            }

            if (_samplerPlans[0].FastKind == 0)
            {
                reason |= 0x100;
                ref readonly SamplerPlan rp = ref _samplerPlans[0];
                uint bpt = TextureFormatBytesPerTexel(rp.Color);
                if (bpt is not (2 or 4))
                {
                    reason |= 0x400;
                }

                if (rp.Width <= 0 || rp.Height <= 0)
                {
                    reason |= 0x800;
                }

                if (rp.UAddr == (int)TextureAddressBorder || rp.VAddr == (int)TextureAddressBorder)
                {
                    reason |= 0x1000;
                }

                if (!rp.Linear && !rp.MortonMasks)
                {
                    reason |= 0x2000;
                }

                if (FastWrapKind(rp.WrapModeU, rp.WrapMaskU, rp.MirrorMaskU) < 0 ||
                    FastWrapKind(rp.WrapModeV, rp.WrapMaskV, rp.MirrorMaskV) < 0)
                {
                    reason |= 0x4000;
                }
            }

            if ((_textureControl0[0] & TextureControl0Enable) == 0)
            {
                reason |= 0x200;
            }

            _diagRejects.TryGetValue(reason, out long rejectCount);
            _diagRejects[reason] = rejectCount + 1;
        }

        // Compiled fill for the k2/k3 lighting shapes: stages 1-3 must resolve
        // to the constant SetTexelNone texel with no counter updates (an
        // unconfigured cube/volume texture), which the generic resolve only
        // proves per pixel. Anything else keeps the generic loop.
        bool compiledLighting = false;
        bool lightingSampleStage0 = false;
        int lightingReason = 0x100000;
        System.Array.Clear(_lightTexels);
        if (bytes != 4)
        {
            lightingReason |= 1;
        }

        if (zetaBytes != 0)
        {
            lightingReason |= 2;
        }

        if (_colorMask != ColorMaskAll)
        {
            lightingReason |= 4;
        }

        if (_blendEnable)
        {
            lightingReason |= 8;
        }

        if (_alphaTestEnable)
        {
            lightingReason |= 0x10;
        }

        if (_memory is null)
        {
            lightingReason |= 0x20;
        }

        if (!compiledModulate && !DiagnosticGenericFill && _combinerFastKind is 2 or 3 &&
            bytes == 4 && zetaBytes == 0 && _colorMask == ColorMaskAll &&
            !_blendEnable && !_alphaTestEnable && _memory is not null &&
            !anyClipPlane && !anyStageDotReader)
        {
            compiledLighting = true;
            if (texModes[0] == PshCombiner.TexModeProject2D)
            {
                lightingSampleStage0 = _samplerPlans[0].FastKind != 0 &&
                                       _samplerPlans[0].Convolution == 0 &&
                                       (_textureControl0[0] & TextureControl0Enable) != 0 &&
                                       !IsTextureCubemap(0) &&
                                       !IsDepthTextureFormat(_samplerPlans[0].Color);
                compiledLighting = lightingSampleStage0;
            }
            else if (texModes[0] != PshCombiner.TexModeNone)
            {
                compiledLighting = false;
                lightingReason |= 0x40;
            }
            else if (texModes[0] == PshCombiner.TexModeProject2D)
            {
                lightingReason |= 0x80;
            }

            // The compiled loop drops the combine stage's -1..1 clamps
            // (they are provable no-ops when each multiplier stays inside
            // -1..1, since max(r,0) is always in [0,1]). Any other constant
            // set keeps the generic loop, whose clamps are exact.
            if (compiledLighting)
            {
                int combineStage = _combinerFastKind == 2 ? 6 : 5;
                float[] c0c = consts[combineStage * 2];
                float[] c1c = consts[(combineStage * 2) + 1];
                compiledLighting = Math.Abs(c0c[0]) <= 1f && Math.Abs(c0c[1]) <= 1f && Math.Abs(c0c[2]) <= 1f &&
                                   Math.Abs(c1c[0]) <= 1f && Math.Abs(c1c[1]) <= 1f && Math.Abs(c1c[2]) <= 1f;
                if (!compiledLighting)
                {
                    lightingReason |= 0x4000_0000;
                }
            }

            for (int i = 1; compiledLighting && i < PshCombiner.TextureSlots; i++)
            {
                int mode = texModes[i];
                if (mode == PshCombiner.TexModeNone)
                {
                    continue;
                }

                bool rectEmpty = (_textureImageRect[i] >> 16) == 0 ||
                                 (_textureImageRect[i] & 0xFFFFu) == 0;
                if (mode == PshCombiner.TexModeCubemap)
                {
                    // Cube mode with cube enabled resolves through
                    // TrySampleTextureCube, which returns before any counter
                    // when the face extent is unconfigured: SetTexelNone.
                    compiledLighting = rectEmpty && IsTextureCubemap(i);
                    if (!compiledLighting)
                    {
                        lightingReason |= 0x200 << (i * 2);
                    }
                }
                else if (mode == PshCombiner.TexModeProject3D)
                {
                    uint stageColor = (_textureFormat[i] >> 8) & 0x7Fu;
                    if (IsDepthTextureFormat(stageColor))
                    {
                        // A depth-format stage short-circuits in
                        // TryResolveShadow; only the two unshadowed functions
                        // are constant and counter-free.
                        if (_shadowDepthFunc == ShadowDepthFuncNever)
                        {
                            SetConstTexel(i, 0f, 0f, 0f);
                            compiledLighting = true;
                        }
                        else if (_shadowDepthFunc == ShadowDepthFuncAlways)
                        {
                            SetConstTexel(i, 1f, 1f, 1f);
                            compiledLighting = true;
                        }
                        else
                        {
                            compiledLighting = false;
                            lightingReason |= 0x2000 << (i * 2);
                        }
                    }
                    else
                    {
                        compiledLighting = rectEmpty && TextureDimensionality(i) == 3;
                        if (!compiledLighting)
                        {
                            lightingReason |= 0x4000 << (i * 2);
                        }
                    }
                }
                else
                {
                    compiledLighting = false;
                    lightingReason |= 0x8000 << (i * 2);
                }
            }
        }

        if (RasterTimingEnabled && !compiledLighting && _combinerFastKind is 2 or 3)
        {
            _diagRejects.TryGetValue(lightingReason, out long lightingRejectCount);
            _diagRejects[lightingReason] = lightingRejectCount + 1;
        }

        if (compiledLighting)
        {
            diagPixels = FillTrianglePshLighting(
                a, b, c, x0, x1, y0, y1,
                invArea, perspective, invAw, invBw, invCw,
                pitch, _combinerFastKind, consts, _lightTexels,
                lightingSampleStage0, in _samplerPlans[0], lightingSampleStage0 && _samplerPlans[0].Bilinear,
                _zclipActive, _zclipCull, _zclipRange, _zclipMin, _zclipMax,
                _colorSurfaceBase + PresentedColorOffset);
            _diagCompiledFills++;
            _diagCompiledPixels += diagPixels;
            if (RasterTimingEnabled)
            {
                _rasterCompiledTicks += Stopwatch.GetTimestamp() - rasterStart;
            }
        }

        for (uint y = y0; !compiledModulate && !compiledLighting && y <= y1; y++)
        {
            for (uint x = x0; x <= x1; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float w0 = (((b.X - a.X) * (py - a.Y)) - ((b.Y - a.Y) * (px - a.X))) * invArea;
                float w1 = (((c.X - b.X) * (py - b.Y)) - ((c.Y - b.Y) * (px - b.X))) * invArea;
                float w2 = 1f - w0 - w1;
                bool inside = (w0 >= 0f && w1 >= 0f && w2 >= 0f) ||
                              (w0 <= 0f && w1 <= 0f && w2 <= 0f);
                if (!inside)
                {
                    continue;
                }

                float p0 = w0;
                float p1 = w1;
                float p2 = w2;
                if (perspective)
                {
                    float s0 = w0 * invCw;
                    float s1 = w1 * invAw;
                    float s2 = w2 * invBw;
                    float sum = s0 + s1 + s2;
                    if (float.IsFinite(sum) && sum != 0f)
                    {
                        float invSum = 1f / sum;
                        p0 = s0 * invSum;
                        p1 = s1 * invSum;
                        p2 = s2 * invSum;
                    }
                }

                diagPixels++;
                diffuse[0] = (p1 * a.DiffuseR) + (p2 * b.DiffuseR) + (p0 * c.DiffuseR);
                diffuse[1] = (p1 * a.DiffuseG) + (p2 * b.DiffuseG) + (p0 * c.DiffuseG);
                diffuse[2] = (p1 * a.DiffuseB) + (p2 * b.DiffuseB) + (p0 * c.DiffuseB);
                diffuse[3] = (p1 * a.DiffuseA) + (p2 * b.DiffuseA) + (p0 * c.DiffuseA);
                float tu0 = (p1 * a.U0) + (p2 * b.U0) + (p0 * c.U0);
                float tv0 = (p1 * a.V0) + (p2 * b.V0) + (p0 * c.V0);
                float ts0 = (p1 * a.S0) + (p2 * b.S0) + (p0 * c.S0);
                float tw0 = (p1 * a.T0W) + (p2 * b.T0W) + (p0 * c.T0W);
                coords[0][0] = tu0;
                coords[0][1] = tv0;
                coords[0][2] = ts0;
                coords[0][3] = tw0;
                float fogFactor = 0f;
                if (!leanInterp)
                {
                    // Full interpolation: specular, stages 1-3 texcoords and
                    // the fog factor are only consumed by the generic/lighting
                    // combiner paths. The lean path (kind-1 modulate fills
                    // with stages 1-3 unbound) skips the dead 16 of 24
                    // vectors; the values it does compute are operation-for-
                    // operation identical (same order, same operands).
                    specular[0] = (p1 * a.SpecR) + (p2 * b.SpecR) + (p0 * c.SpecR);
                    specular[1] = (p1 * a.SpecG) + (p2 * b.SpecG) + (p0 * c.SpecG);
                    specular[2] = (p1 * a.SpecB) + (p2 * b.SpecB) + (p0 * c.SpecB);
                    specular[3] = (p1 * a.SpecA) + (p2 * b.SpecA) + (p0 * c.SpecA);
                    float tu1 = (p1 * a.U1) + (p2 * b.U1) + (p0 * c.U1);
                    float tv1 = (p1 * a.V1) + (p2 * b.V1) + (p0 * c.V1);
                    float ts1 = (p1 * a.S1) + (p2 * b.S1) + (p0 * c.S1);
                    float tw1 = (p1 * a.T1W) + (p2 * b.T1W) + (p0 * c.T1W);
                    float tu2 = (p1 * a.U2) + (p2 * b.U2) + (p0 * c.U2);
                    float tv2 = (p1 * a.V2) + (p2 * b.V2) + (p0 * c.V2);
                    float ts2 = (p1 * a.S2) + (p2 * b.S2) + (p0 * c.S2);
                    float tw2 = (p1 * a.T2W) + (p2 * b.T2W) + (p0 * c.T2W);
                    float tu3 = (p1 * a.U3) + (p2 * b.U3) + (p0 * c.U3);
                    float tv3 = (p1 * a.V3) + (p2 * b.V3) + (p0 * c.V3);
                    float ts3 = (p1 * a.S3) + (p2 * b.S3) + (p0 * c.S3);
                    float tw3 = (p1 * a.T3W) + (p2 * b.T3W) + (p0 * c.T3W);
                    coords[1][0] = tu1;
                    coords[1][1] = tv1;
                    coords[1][2] = ts1;
                    coords[1][3] = tw1;
                    coords[2][0] = tu2;
                    coords[2][1] = tv2;
                    coords[2][2] = ts2;
                    coords[2][3] = tw2;
                    coords[3][0] = tu3;
                    coords[3][1] = tv3;
                    coords[3][2] = ts3;
                    coords[3][3] = tw3;
                    fogFactor = (p1 * a.Fog) + (p2 * b.Fog) + (p0 * c.Fog);
                }

                float depth = (w1 * a.Z) + (w2 * b.Z) + (w0 * c.Z);

                if (anyClipPlane)
                {
                    bool clipped = false;
                    for (int i = 0; i < PshCombiner.TextureSlots; i++)
                    {
                        if (texModes[i] == PshCombiner.TexModeClipPlane && ClipPlaneDiscards(i, coords[i]))
                        {
                            clipped = true;
                            break;
                        }
                    }

                    if (clipped)
                    {
                        continue;
                    }
                }

                bool sampled = false;
                if (anyStageDotReader)
                {
                    Array.Clear(stageDots);
                }

                for (int i = 0; i < PshCombiner.TextureSlots; i++)
                {
                    if (texModes[i] == PshCombiner.TexModeNone)
                    {
                        continue;
                    }

                    if (ResolveTexel(i, texModes[i], coords, texels, stageDots))
                    {
                        sampled = true;
                    }
                }

                float fr;
                float fg;
                float fb;
                float fa;
                if (_combinerFastKind == 1)
                {
                    PshCombiner.EvaluateModulateT0V0(diffuse, texels, out fr, out fg, out fb, out fa);
                }
                else if (_combinerFastKind is 2 or 3)
                {
                    PshCombiner.EvaluateLightingSpecialized(_combinerFastKind, diffuse, specular, texels, consts,
                        out fr, out fg, out fb, out fa);
                }
                else if (_fogEnable)
                {
                    fog[3] = Math.Clamp(fogFactor, 0f, 1f);
                    _combiner.EvaluatePlanned(diffuse, specular, fog, texels, consts, out fr, out fg, out fb, out fa);
                }
                else
                {
                    _combiner.EvaluatePlanned(diffuse, specular, CombinerFogConst, texels, consts, out fr, out fg,
                        out fb, out fa);
                }

                if (sampled)
                {
                    TexturedPixels++;
                }

                if (fr == 0f && fg == 0f && fb == 0f)
                {
                    _combinerRgbZeroFragments++;
                    if (fa > 0f)
                    {
                        _combinerRgbZeroAlphaFragments++;
                    }
                }

                ProcessFragment(x, y, bytes, pitch, fr, fg, fb, fa, depth, zetaBytes, zetaPitch);
            }
        }

        if (RasterTimingEnabled)
        {
            _rasterPshTicks += Stopwatch.GetTimestamp() - rasterStart;
            _rasterPshCalls++;
            if (_combinerFastKind is >= 1 and <= 3)
            {
                _diagFastKindFills[_combinerFastKind]++;
            }

            if (leanInterp)
            {
                _diagLeanFills++;
            }

            // Diagnostic: accumulate pixels per combiner configuration (per fill).
            if (diagPixels > 0)
            {
                (long Draws, long Pixels) entry = CombinerConfigStats.TryGetValue(_diagCombinerConfig, out var existing)
                    ? existing
                    : (0L, 0L);
                CombinerConfigStats[_diagCombinerConfig] = (entry.Draws + 1, entry.Pixels + diagPixels);
            }
        }
    }

    private static Dictionary<uint, long> Snapshot(long[] counters)
    {
        var result = new Dictionary<uint, long>();
        for (int i = 0; i < counters.Length; i++)
        {
            if (counters[i] != 0)
            {
                result[(uint)i] = counters[i];
            }
        }

        return result;
    }

    private static Dictionary<int, long> SnapshotByIndex(long[] counters)
    {
        var result = new Dictionary<int, long>();
        for (int i = 0; i < counters.Length; i++)
        {
            if (counters[i] != 0)
            {
                result[i] = counters[i];
            }
        }

        return result;
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
