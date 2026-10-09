using Xunit;

namespace XGrid.Nv2a.Tests;

public sealed class PfifoPusherTests
{
    private const uint Base = Nv2aMmio.BaseAddress;

    private sealed class ArrayMemory : IGpuMemory
    {
        private readonly byte[] _data;

        public ArrayMemory(int size = 0x10000)
        {
            _data = new byte[size];
        }

        public void WritePhysical(uint address, uint value)
        {
            _data[address] = (byte)value;
            _data[address + 1] = (byte)(value >> 8);
            _data[address + 2] = (byte)(value >> 16);
            _data[address + 3] = (byte)(value >> 24);
        }

        public uint ReadPhysical(uint address)
        {
            return (uint)(_data[address] | (_data[address + 1] << 8) | (_data[address + 2] << 16) | (_data[address + 3] << 24));
        }
    }

    private static (Nv2aMmio Nv2a, ArrayMemory Memory) CreateChannel()
    {
        var memory = new ArrayMemory();
        var nv2a = new Nv2aMmio(64u * 1024 * 1024, "NV2A", memory);
        nv2a.Write(Base + 0x2504u, 4, 1u);
        nv2a.Write(Base + 0x3200u, 4, 1u);
        nv2a.Write(Base + 0x3204u, 4, 0x100u);
        nv2a.Write(Base + 0x3220u, 4, 1u);
        nv2a.Write(Base + 0x322Cu, 4, 0x111Cu);
        nv2a.Write(Base + 0x3250u, 4, 1u);
        nv2a.Write(Base + 0x400720u, 4, 1u);
        nv2a.Write(Base + 0x7111C0u, 4, 0x00000002u);
        nv2a.Write(Base + 0x7111C4u, 4, 0x0000FFFFu);
        nv2a.Write(Base + 0x7111C8u, 4, 0x00000000u);
        return (nv2a, memory);
    }

    [Fact]
    public void IdleRing_JumpToPut_AdvancesGet()
    {
        (Nv2aMmio nv2a, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x0000u, 0x00001001u);

        nv2a.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0x00001000u, nv2a.Read(Base + 0x800044u, 4));
        Assert.True(nv2a.PusherWordsConsumed > 0);
        Assert.False(nv2a.PusherCapped);
    }

    [Fact]
    public void IncreasingMethod_DispatchesToSink()
    {
        (Nv2aMmio nv2a, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x0000u, 0x00040100u);
        memory.WritePhysical(0x0004u, 0xDEADBEEFu);
        memory.WritePhysical(0x0008u, 0x00001001u);

        nv2a.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0x00001000u, nv2a.Read(Base + 0x800044u, 4));
        Assert.True(nv2a.MethodCounts.TryGetValue(0x100u, out int count) && count == 1);
        Assert.True(nv2a.MethodParams.TryGetValue(0x100u, out uint param) && param == 0xDEADBEEFu);
    }

    [Fact]
    public void ObjectBind_ResolvesRamhtInstance()
    {
        (Nv2aMmio nv2a, ArrayMemory memory) = CreateChannel();
        nv2a.Write(Base + 0x2210u, 4, 0x00000100u);
        nv2a.Write(Base + 0x710000u + 0x42u * 8u, 4, 0x00000042u);
        nv2a.Write(Base + 0x710000u + 0x42u * 8u + 4u, 4, 0x80010120u);
        memory.WritePhysical(0x0000u, 0x00040000u);
        memory.WritePhysical(0x0004u, 0x00000042u);
        memory.WritePhysical(0x0008u, 0x00001001u);

        nv2a.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0x00001000u, nv2a.Read(Base + 0x800044u, 4));
        Assert.True(nv2a.MethodCounts.TryGetValue(0u, out int count) && count == 1);
    }

    [Fact]
    public void ReservedCommand_SuspendsWithError()
    {
        (Nv2aMmio nv2a, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x0000u, 0xDEADBEEFu);

        nv2a.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(4u, nv2a.Read(Base + 0x800044u, 4));
        Assert.False(nv2a.PusherCapped);
    }

    [Fact]
    public void WithoutMemory_KickLeavesGetUntouched()
    {
        var nv2a = new Nv2aMmio();
        nv2a.Write(Base + 0x2504u, 4, 1u);
        nv2a.Write(Base + 0x3200u, 4, 1u);
        nv2a.Write(Base + 0x3204u, 4, 0x100u);
        nv2a.Write(Base + 0x3220u, 4, 1u);
        nv2a.Write(Base + 0x400720u, 4, 1u);

        nv2a.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0u, nv2a.Read(Base + 0x800044u, 4));
        Assert.Equal(0u, nv2a.PusherWordsConsumed);
    }

    [Fact]
    public void PfifoIntr_WriteClearsPendingBits()
    {
        var nv2a = new Nv2aMmio();

        nv2a.Write(Base + 0x2140u, 4, 0x1111111u);
        nv2a.Write(Base + 0x2100u, 4, 0xFFFFFFFFu);

        Assert.Equal(0x1111111u, nv2a.Read(Base + 0x2140u, 4));
        Assert.Equal(0u, nv2a.Read(Base + 0x2100u, 4));
        Assert.Equal(0x10u, nv2a.Read(Base + 0x2400u, 4));
    }

    [Fact]
    public void ChannelSetup_ProgramsCache1Registers()
    {
        (Nv2aMmio nv2a, ArrayMemory _) = CreateChannel();

        Assert.Equal(1u, nv2a.Read(Base + 0x2504u, 4));
        Assert.Equal(1u, nv2a.Read(Base + 0x3200u, 4));
        Assert.Equal(0x100u, nv2a.Read(Base + 0x3204u, 4));
        Assert.Equal(1u, nv2a.Read(Base + 0x3220u, 4));
        Assert.Equal(0x111Cu, nv2a.Read(Base + 0x322Cu, 4));
        Assert.Equal(1u, nv2a.Read(Base + 0x3250u, 4));
        Assert.Equal(1u, nv2a.Read(Base + 0x400720u, 4));
        Assert.Equal(0x00000002u, nv2a.Read(Base + 0x7111C0u, 4));
        Assert.Equal(0x0000FFFFu, nv2a.Read(Base + 0x7111C4u, 4));
    }

    [Fact]
    public void PraminOffset_RetainsWrittenWord()
    {
        var nv2a = new Nv2aMmio();

        nv2a.Write(Base + 0x700000u, 4, 0x11111111u);
        nv2a.Write(Base + 0x700100u, 4, 0x22222222u);
        nv2a.Write(Base + 0x701000u, 4, 0x33333333u);
        nv2a.Write(Base + 0x710000u, 4, 0x44444444u);
        nv2a.Write(Base + 0x7111C0u, 4, 0x55555555u);

        Assert.Equal(0x11111111u, nv2a.Read(Base + 0x700000u, 4));
        Assert.Equal(0x22222222u, nv2a.Read(Base + 0x700100u, 4));
        Assert.Equal(0x33333333u, nv2a.Read(Base + 0x701000u, 4));
        Assert.Equal(0x44444444u, nv2a.Read(Base + 0x710000u, 4));
        Assert.Equal(0x55555555u, nv2a.Read(Base + 0x7111C0u, 4));
    }

    [Fact]
    public void SurfaceClear_FillsColorAndZetaRects()
    {
        (Nv2aMmio nv2a, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x0000u, 0x00100208u);
        memory.WritePhysical(0x0004u, 0x00000128u);
        memory.WritePhysical(0x0008u, 0x00100010u);
        memory.WritePhysical(0x000Cu, 0x00001000u);
        memory.WritePhysical(0x0010u, 0x00002000u);
        memory.WritePhysical(0x0014u, 0x00080200u);
        memory.WritePhysical(0x0018u, 0x00040000u);
        memory.WritePhysical(0x001Cu, 0x00040000u);
        memory.WritePhysical(0x0020u, 0x00081D98u);
        memory.WritePhysical(0x0024u, 0x00030000u);
        memory.WritePhysical(0x0028u, 0x00030000u);
        memory.WritePhysical(0x002Cu, 0x00141D8Cu);
        memory.WritePhysical(0x0030u, 0x00FFFFFFu);
        memory.WritePhysical(0x0034u, 0xFF112233u);
        memory.WritePhysical(0x0038u, 0x000000F3u);
        memory.WritePhysical(0x003Cu, 0x00001001u);

        nv2a.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(1u, nv2a.ClearsExecuted);
        Assert.Equal(0x00001000u, nv2a.PresentedColorOffset);
        Assert.Equal(0x10u, nv2a.PresentedColorPitch);
        Assert.Equal(4u, nv2a.PresentedWidth);
        Assert.Equal(4u, nv2a.PresentedHeight);
        Assert.Equal(0xFF112233u, memory.ReadPhysical(0x00001000u));
        Assert.Equal(0xFF112233u, memory.ReadPhysical(0x0000100Cu));
        Assert.Equal(0xFF112233u, memory.ReadPhysical(0x00001030u));
        Assert.Equal(0x00FFFFFFu, memory.ReadPhysical(0x00002000u));
        Assert.Equal(0x00FFFFFFu, memory.ReadPhysical(0x00002030u));
        Assert.True(nv2a.MethodCounts.TryGetValue(0x1D94u, out int count) && count == 1);
    }

    [Fact]
    public void SurfaceClear_SixteenBitColor_MergesWords()
    {
        (Nv2aMmio nv2a, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x0000u, 0x00100208u);
        memory.WritePhysical(0x0004u, 0x00000103u);
        memory.WritePhysical(0x0008u, 0x00000008u);
        memory.WritePhysical(0x000Cu, 0x00001002u);
        memory.WritePhysical(0x0010u, 0x00002002u);
        memory.WritePhysical(0x0014u, 0x00080200u);
        memory.WritePhysical(0x0018u, 0x00020000u);
        memory.WritePhysical(0x001Cu, 0x00020000u);
        memory.WritePhysical(0x0020u, 0x00081D98u);
        memory.WritePhysical(0x0024u, 0x00010000u);
        memory.WritePhysical(0x0028u, 0x00010000u);
        memory.WritePhysical(0x002Cu, 0x000C1D8Cu);
        memory.WritePhysical(0x0030u, 0x00000000u);
        memory.WritePhysical(0x0034u, 0x0000ABCDu);
        memory.WritePhysical(0x0038u, 0x000000F0u);
        memory.WritePhysical(0x003Cu, 0x00001001u);

        nv2a.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(1u, nv2a.ClearsExecuted);
        Assert.Equal(0xABCD0000u, memory.ReadPhysical(0x00001000u));
        Assert.Equal(0x0000ABCDu, memory.ReadPhysical(0x00001004u));
    }

    [Fact]
    public void Vblank_AssertsIrqWhenEnabled()
    {
        var nv2a = new Nv2aMmio();
        bool asserted = false;
        nv2a.InterruptChanged += level => asserted = level;
        nv2a.Write(Base + 0x600140u, 4, 0x00000001u);
        nv2a.Write(Base + 0x0140u, 4, 0x01000000u);

        nv2a.Advance(Nv2aMmio.CoreClockHz / 60);

        Assert.True(asserted);
        Assert.True(nv2a.InterruptAsserted);
        Assert.Equal(1u, nv2a.Read(Base + 0x600100u, 4));
        Assert.Equal(0x01000000u, nv2a.Read(Base + 0x0100u, 4));
    }

    [Fact]
    public void Vblank_StaysSilentWhenDisabled()
    {
        var nv2a = new Nv2aMmio();
        bool asserted = false;
        nv2a.InterruptChanged += level => asserted = level;

        nv2a.Advance(Nv2aMmio.CoreClockHz / 60);

        Assert.False(asserted);
        Assert.Equal(1u, nv2a.Read(Base + 0x600100u, 4));
    }

    [Fact]
    public void Vblank_ClearDeassertsIrq()
    {
        var nv2a = new Nv2aMmio();
        bool asserted = false;
        nv2a.InterruptChanged += level => asserted = level;
        nv2a.Write(Base + 0x600140u, 4, 0x00000001u);
        nv2a.Write(Base + 0x0140u, 4, 0x01000000u);
        nv2a.Advance(Nv2aMmio.CoreClockHz / 60);
        Assert.True(asserted);

        nv2a.Write(Base + 0x600100u, 4, 0x00000001u);

        Assert.False(asserted);
        Assert.False(nv2a.InterruptAsserted);
    }

    [Fact]
    public void ContextSwitchBind_RaisesPgraphIrq()
    {
        (Nv2aMmio nv2a, ArrayMemory memory) = CreateChannel();
        bool asserted = false;
        nv2a.InterruptChanged += level => asserted = level;
        nv2a.Write(Base + 0x400140u, 4, 0x00001000u);
        nv2a.Write(Base + 0x0140u, 4, 0x00001000u);
        nv2a.Write(Base + 0x2210u, 4, 0x00000100u);
        nv2a.Write(Base + 0x710000u + 0x42u * 8u, 4, 0x00000042u);
        nv2a.Write(Base + 0x710000u + 0x42u * 8u + 4u, 4, 0x80010120u);
        memory.WritePhysical(0x0000u, 0x00040000u);
        memory.WritePhysical(0x0004u, 0x00000042u);
        memory.WritePhysical(0x0008u, 0x00001001u);

        nv2a.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.True(asserted);
        Assert.Equal(0x00001000u, nv2a.Read(Base + 0x400100u, 4));
    }

    [Fact]
    public void InlineTriangle_RasterizesGouraudFill()
    {
        (Nv2aMmio nv2a, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x0000u, 0x00100208u);
        memory.WritePhysical(0x0004u, 0x00000128u);
        memory.WritePhysical(0x0008u, 0x00000100u);
        memory.WritePhysical(0x000Cu, 0x00001000u);
        memory.WritePhysical(0x0010u, 0x00002000u);
        memory.WritePhysical(0x0014u, 0x00080200u);
        memory.WritePhysical(0x0018u, 0x00400000u);
        memory.WritePhysical(0x001Cu, 0x00400000u);
        memory.WritePhysical(0x0020u, 0x00081760u);
        memory.WritePhysical(0x0024u, 0x00000022u);
        memory.WritePhysical(0x0028u, 0x00000042u);
        memory.WritePhysical(0x002Cu, 0x400417FCu);
        memory.WritePhysical(0x0030u, 0x00000005u);
        memory.WritePhysical(0x0034u, 0x40481818u);
        memory.WritePhysical(0x0038u, 0x41200000u);
        memory.WritePhysical(0x003Cu, 0x41200000u);
        memory.WritePhysical(0x0040u, 0x3F800000u);
        memory.WritePhysical(0x0044u, 0x00000000u);
        memory.WritePhysical(0x0048u, 0x00000000u);
        memory.WritePhysical(0x004Cu, 0x3F800000u);
        memory.WritePhysical(0x0050u, 0x42480000u);
        memory.WritePhysical(0x0054u, 0x41200000u);
        memory.WritePhysical(0x0058u, 0x00000000u);
        memory.WritePhysical(0x005Cu, 0x3F800000u);
        memory.WritePhysical(0x0060u, 0x00000000u);
        memory.WritePhysical(0x0064u, 0x3F800000u);
        memory.WritePhysical(0x0068u, 0x41200000u);
        memory.WritePhysical(0x006Cu, 0x42480000u);
        memory.WritePhysical(0x0070u, 0x00000000u);
        memory.WritePhysical(0x0074u, 0x00000000u);
        memory.WritePhysical(0x0078u, 0x3F800000u);
        memory.WritePhysical(0x007Cu, 0x3F800000u);
        memory.WritePhysical(0x0080u, 0x400417FCu);
        memory.WritePhysical(0x0084u, 0x00000000u);
        memory.WritePhysical(0x0088u, 0x00001001u);

        nv2a.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0u, nv2a.PusherError);
        Assert.Equal(0u, nv2a.SkippedDraws);
        Assert.Equal(1u, nv2a.DrawsExecuted);
        Assert.Equal(0xFF794343u, memory.ReadPhysical(0x00002450u));
        Assert.Equal(0u, memory.ReadPhysical(0x00001000u));
    }
}

