using Xunit;

namespace XGrid.Nv2a.Tests;

public sealed class PfifoPusherTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private sealed class ArrayMemory(int size = 0x10000) : IGpuMemory
    {
        private readonly byte[] _data = new byte[size];

        public byte[] Bytes => _data;

        public void WritePhysical(uint address, uint value)
        {
            _data[address] = (byte)value;
            _data[address + 1] = (byte)(value >> 8);
            _data[address + 2] = (byte)(value >> 16);
            _data[address + 3] = (byte)(value >> 24);
        }

        public uint ReadPhysical(uint address)
        {
            return (uint)(_data[address] | (_data[address + 1] << 8) | (_data[address + 2] << 16) |
                          (_data[address + 3] << 24));
        }
    }

    private static (Nv2AMmio Nv2a, ArrayMemory Memory) CreateChannel()
    {
        var memory = new ArrayMemory();
        var nv2A = new Nv2AMmio(64u * 1024 * 1024, "NV2A", memory);
        nv2A.Write(Base + 0x2504u, 4, 1u);
        nv2A.Write(Base + 0x3200u, 4, 1u);
        nv2A.Write(Base + 0x3204u, 4, 0x100u);
        nv2A.Write(Base + 0x3220u, 4, 1u);
        nv2A.Write(Base + 0x322Cu, 4, 0x111Cu);
        nv2A.Write(Base + 0x3250u, 4, 1u);
        nv2A.Write(Base + 0x400720u, 4, 1u);
        nv2A.Write(Base + 0x7111C0u, 4, 0x00000002u);
        nv2A.Write(Base + 0x7111C4u, 4, 0x0000FFFFu);
        nv2A.Write(Base + 0x7111C8u, 4, 0x00000000u);
        return (nv2A, memory);
    }

    /// <summary>
    /// The production adapter's fast lanes: the RAM array view plus the batched
    /// direct word write the clear fast path replaces (§239).
    /// </summary>
    private sealed class FastArrayMemory(int size = 0x10000) : IGpuMemory, IGpuMemoryFast
    {
        private readonly byte[] _data = new byte[size];
        private bool _batch;

        public byte[] Bytes => _data;

        public long FastWrites { get; private set; }

        public void BeginGpuBatch()
        {
            _batch = true;
        }

        public void EndGpuBatch()
        {
            _batch = false;
        }

        public bool TryReadPhysical(uint address, out uint value)
        {
            if (address < (uint)_data.Length && address <= (uint)_data.Length - 4u)
            {
                value = ReadPhysical(address);
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryWritePhysical(uint address, uint value)
        {
            if (!_batch || address > (uint)_data.Length - 4u)
            {
                return false;
            }

            WritePhysical(address, value);
            FastWrites++;
            return true;
        }

        public bool TryGetRamBytes(out byte[] bytes, out uint size)
        {
            bytes = _data;
            size = (uint)_data.Length;
            return true;
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
            return (uint)(_data[address] | (_data[address + 1] << 8) | (_data[address + 2] << 16) |
                          (_data[address + 3] << 24));
        }
    }

    private static (Nv2AMmio Nv2a, FastArrayMemory Memory) CreateFastChannel()
    {
        var memory = new FastArrayMemory();
        var nv2A = new Nv2AMmio(64u * 1024 * 1024, "NV2A", memory);
        nv2A.Write(Base + 0x2504u, 4, 1u);
        nv2A.Write(Base + 0x3200u, 4, 1u);
        nv2A.Write(Base + 0x3204u, 4, 0x100u);
        nv2A.Write(Base + 0x3220u, 4, 1u);
        nv2A.Write(Base + 0x322Cu, 4, 0x111Cu);
        nv2A.Write(Base + 0x3250u, 4, 1u);
        nv2A.Write(Base + 0x400720u, 4, 1u);
        nv2A.Write(Base + 0x7111C0u, 4, 0x00000002u);
        nv2A.Write(Base + 0x7111C4u, 4, 0x0000FFFFu);
        nv2A.Write(Base + 0x7111C8u, 4, 0x00000000u);
        return (nv2A, memory);
    }

    /// <summary>
    /// §239 clear-fill fast lane: a 32-bit surface clear must write exactly the
    /// bytes the per-pixel <c>WriteGpu</c> loop wrote - proven by running the
    /// same clear through a fast adapter (span fill) and a plain one (per-pixel
    /// loop) and comparing the whole RAM image. The unaligned-pitch case pins
    /// that the fast lane declines instead of filling at the wrong addresses.
    /// </summary>
    [Theory]
    [InlineData(0x00000100u, 0x00000200u, 0x40u)]
    [InlineData(0x00000100u, 0x00000200u, 0x42u)]
    public void SurfaceClear_FastFillLane_MatchesPerPixelFill(uint colorOffset, uint zetaOffset, uint pitch)
    {
        (Nv2AMmio plain, ArrayMemory plainMemory) = CreateChannel();
        (Nv2AMmio fast, FastArrayMemory fastMemory) = CreateFastChannel();

        foreach ((Nv2AMmio nv2A, IGpuMemory memory) in new (Nv2AMmio, IGpuMemory)[]
                 {
                     (plain, plainMemory), (fast, fastMemory),
                 })
        {
            memory.WritePhysical(0x0000u, 0x00100208u);
            memory.WritePhysical(0x0004u, pitch);
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
            memory.WritePhysical(0x003Cu, colorOffset);
            nv2A.Write(Base + 0x800040u, 4, zetaOffset);
            Assert.Equal(1u, nv2A.ClearsExecuted);
        }

        Assert.Equal(plainMemory.Bytes, fastMemory.Bytes);
    }

    [Fact]
    public void IdleRing_JumpToPut_AdvancesGet()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x0000u, 0x00001001u);

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0x00001000u, nv2A.Read(Base + 0x800044u, 4));
        Assert.True(nv2A.PusherWordsConsumed > 0);
        Assert.False(nv2A.PusherCapped);
    }

    [Fact]
    public void IncreasingMethod_DispatchesToSink()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x0000u, 0x00040100u);
        memory.WritePhysical(0x0004u, 0x00000000u);
        memory.WritePhysical(0x0008u, 0x00001001u);

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0x00001000u, nv2A.Read(Base + 0x800044u, 4));
        Assert.True(nv2A.MethodCounts.TryGetValue(0x100u, out int count) && count == 1);
        Assert.True(nv2A.MethodParams.TryGetValue(0x100u, out uint param) && param == 0u);
    }

    [Fact]
    public void NoOperationWithParameter_StallsPusherUntilPgraphIntrAck()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.Write(Base + 0x400140u, 4, 0x00100000u);
        nv2A.Write(Base + 0x0140u, 4, 0x00000001u);
        memory.WritePhysical(0x0000u, 0x00040100u);
        memory.WritePhysical(0x0004u, 0x0000031Cu);
        memory.WritePhysical(0x0008u, 0x00001001u);

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        // The NO_OPERATION word is consumed, then the pusher waits for the
        // guest to acknowledge the PGRAPH notification (xemu waiting_for_nop).
        Assert.Equal(0x00000008u, nv2A.Read(Base + 0x800044u, 4));
        Assert.Equal(0x00100000u, nv2A.Read(Base + 0x400100u, 4));

        nv2A.Write(Base + 0x400100u, 4, 0x00100000u);

        // The engine resumes on the next tick (guest acked before reading the
        // subprogram parameters), then the jump-to-put word advances GET to PUT.
        nv2A.Advance(1);

        Assert.Equal(0x00001000u, nv2A.Read(Base + 0x800044u, 4));
    }

    [Fact]
    public void ClearSurface_AppliesColorDmaContextBase()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();

        // RAMHT at RAMIN 0x10000; color context handle 9 -> instance 0x11150
        // with base 0x8000 and limit 0xFFFF.
        nv2A.Write(Base + 0x2210u, 4, 0x00000100u);
        nv2A.Write(Base + 0x711150u, 4, 0x0000003Du);
        nv2A.Write(Base + 0x711154u, 4, 0x0000FFFFu);
        nv2A.Write(Base + 0x711158u, 4, 0x00008003u);
        nv2A.Write(Base + 0x710000u + 9u * 8u, 4, 0x00000009u);
        nv2A.Write(Base + 0x710000u + 9u * 8u + 4u, 4, 0x80001115u);

        uint[] words =
        [
            0x00040194u, 0x00000009u,
            0x000C020Cu, 0x01000100u, 0x00000000u, 0x00000000u,
            0x00080200u, 0x00400040u, 0x00400040u,
            0x00040208u, 0x00000128u,
            0x00081D98u, 0x003F0000u, 0x003F0000u,
            0x000C1D8Cu, 0x00000000u, 0x11223344u, 0x000000F0u,
        ];
        for (int i = 0; i < words.Length; i++)
        {
            memory.WritePhysical((uint)(i * 4), words[i]);
        }

        nv2A.Write(Base + 0x800040u, 4, (uint)(words.Length * 4));

        Assert.Equal(0u, nv2A.PusherError);
        Assert.Equal(1u, nv2A.ClearsExecuted);
        Assert.Equal(0x11223344u, memory.ReadPhysical(0x8000u));
        Assert.Equal(0x11223344u, memory.ReadPhysical(0x8000u + 63u * 0x100u + 63u * 4u));
        Assert.Equal(0u, memory.ReadPhysical(0xC000u));

        // The clear-value methods alias the outer PGRAPH registers that the
        // guest's PB_SETOUTER subprogram reads back (PARAMETER_A/B).
        Assert.Equal(0x00000000u, nv2A.Read(Base + 0x400000u + 0x1A88u, 4));
        Assert.Equal(0x11223344u, nv2A.Read(Base + 0x400000u + 0x186Cu, 4));
    }

    [Fact]
    public void ObjectBind_ResolvesRamhtInstance()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.Write(Base + 0x2210u, 4, 0x00000100u);
        nv2A.Write(Base + 0x710000u + 0x42u * 8u, 4, 0x00000042u);
        nv2A.Write(Base + 0x710000u + 0x42u * 8u + 4u, 4, 0x80010120u);
        memory.WritePhysical(0x0000u, 0x00040000u);
        memory.WritePhysical(0x0004u, 0x00000042u);
        memory.WritePhysical(0x0008u, 0x00001001u);

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0x00001000u, nv2A.Read(Base + 0x800044u, 4));
        Assert.True(nv2A.MethodCounts.TryGetValue(0u, out int count) && count == 1);
    }

    [Fact]
    public void ReservedCommand_SuspendsWithError()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        memory.WritePhysical(0x0000u, 0xDEADBEEFu);

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(4u, nv2A.Read(Base + 0x800044u, 4));
        Assert.False(nv2A.PusherCapped);
    }

    [Fact]
    public void WithoutMemory_KickLeavesGetUntouched()
    {
        var nv2A = new Nv2AMmio();
        nv2A.Write(Base + 0x2504u, 4, 1u);
        nv2A.Write(Base + 0x3200u, 4, 1u);
        nv2A.Write(Base + 0x3204u, 4, 0x100u);
        nv2A.Write(Base + 0x3220u, 4, 1u);
        nv2A.Write(Base + 0x400720u, 4, 1u);

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0u, nv2A.Read(Base + 0x800044u, 4));
        Assert.Equal(0u, nv2A.PusherWordsConsumed);
    }

    [Fact]
    public void PfifoIntr_WriteClearsPendingBits()
    {
        var nv2A = new Nv2AMmio();

        nv2A.Write(Base + 0x2140u, 4, 0x1111111u);
        nv2A.Write(Base + 0x2100u, 4, 0xFFFFFFFFu);

        Assert.Equal(0x1111111u, nv2A.Read(Base + 0x2140u, 4));
        Assert.Equal(0u, nv2A.Read(Base + 0x2100u, 4));
        Assert.Equal(0x10u, nv2A.Read(Base + 0x2400u, 4));
    }

    [Fact]
    public void ChannelSetup_ProgramsCache1Registers()
    {
        (Nv2AMmio nv2A, ArrayMemory _) = CreateChannel();

        Assert.Equal(1u, nv2A.Read(Base + 0x2504u, 4));
        Assert.Equal(1u, nv2A.Read(Base + 0x3200u, 4));
        Assert.Equal(0x100u, nv2A.Read(Base + 0x3204u, 4));
        Assert.Equal(1u, nv2A.Read(Base + 0x3220u, 4));
        Assert.Equal(0x111Cu, nv2A.Read(Base + 0x322Cu, 4));
        Assert.Equal(1u, nv2A.Read(Base + 0x3250u, 4));
        Assert.Equal(1u, nv2A.Read(Base + 0x400720u, 4));
        Assert.Equal(0x00000002u, nv2A.Read(Base + 0x7111C0u, 4));
        Assert.Equal(0x0000FFFFu, nv2A.Read(Base + 0x7111C4u, 4));
    }

    [Fact]
    public void PraminOffset_RetainsWrittenWord()
    {
        var nv2A = new Nv2AMmio();

        nv2A.Write(Base + 0x700000u, 4, 0x11111111u);
        nv2A.Write(Base + 0x700100u, 4, 0x22222222u);
        nv2A.Write(Base + 0x701000u, 4, 0x33333333u);
        nv2A.Write(Base + 0x710000u, 4, 0x44444444u);
        nv2A.Write(Base + 0x7111C0u, 4, 0x55555555u);

        Assert.Equal(0x11111111u, nv2A.Read(Base + 0x700000u, 4));
        Assert.Equal(0x22222222u, nv2A.Read(Base + 0x700100u, 4));
        Assert.Equal(0x33333333u, nv2A.Read(Base + 0x701000u, 4));
        Assert.Equal(0x44444444u, nv2A.Read(Base + 0x710000u, 4));
        Assert.Equal(0x55555555u, nv2A.Read(Base + 0x7111C0u, 4));
    }

    [Fact]
    public void SurfaceClear_FillsColorAndZetaRects()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
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

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(1u, nv2A.ClearsExecuted);
        Assert.Equal(0x00001000u, nv2A.PresentedColorOffset);
        Assert.Equal(0x10u, nv2A.PresentedColorPitch);
        Assert.Equal(4u, nv2A.PresentedWidth);
        Assert.Equal(4u, nv2A.PresentedHeight);
        Assert.Equal(0xFF112233u, memory.ReadPhysical(0x00001000u));
        Assert.Equal(0xFF112233u, memory.ReadPhysical(0x0000100Cu));
        Assert.Equal(0xFF112233u, memory.ReadPhysical(0x00001030u));
        Assert.Equal(0x00FFFFFFu, memory.ReadPhysical(0x00002000u));
        Assert.Equal(0x00FFFFFFu, memory.ReadPhysical(0x00002030u));
        Assert.True(nv2A.MethodCounts.TryGetValue(0x1D94u, out int count) && count == 1);
    }

    [Fact]
    public void SurfaceClear_SixteenBitColor_MergesWords()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
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

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(1u, nv2A.ClearsExecuted);
        Assert.Equal(0xABCD0000u, memory.ReadPhysical(0x00001000u));
        Assert.Equal(0x0000ABCDu, memory.ReadPhysical(0x00001004u));
    }

    [Fact]
    public void Vblank_AssertsIrqWhenEnabled()
    {
        var nv2A = new Nv2AMmio();
        bool asserted = false;
        nv2A.InterruptChanged += level => asserted = level;
        nv2A.Write(Base + 0x600140u, 4, 0x00000001u);
        nv2A.Write(Base + 0x0140u, 4, 0x01000000u);

        nv2A.Advance(Nv2AMmio.CoreClockHz / 60);

        Assert.True(asserted);
        Assert.True(nv2A.InterruptAsserted);
        Assert.Equal(1u, nv2A.Read(Base + 0x600100u, 4));
        Assert.Equal(0x01000000u, nv2A.Read(Base + 0x0100u, 4));
    }

    [Fact]
    public void Vblank_StaysSilentWhenDisabled()
    {
        var nv2A = new Nv2AMmio();
        bool asserted = false;
        nv2A.InterruptChanged += level => asserted = level;

        nv2A.Advance(Nv2AMmio.CoreClockHz / 60);

        Assert.False(asserted);
        Assert.Equal(1u, nv2A.Read(Base + 0x600100u, 4));
    }

    [Fact]
    public void Vblank_ClearDeassertsIrq()
    {
        var nv2A = new Nv2AMmio();
        bool asserted = false;
        nv2A.InterruptChanged += level => asserted = level;
        nv2A.Write(Base + 0x600140u, 4, 0x00000001u);
        nv2A.Write(Base + 0x0140u, 4, 0x01000000u);
        nv2A.Advance(Nv2AMmio.CoreClockHz / 60);
        Assert.True(asserted);

        nv2A.Write(Base + 0x600100u, 4, 0x00000001u);

        Assert.False(asserted);
        Assert.False(nv2A.InterruptAsserted);
    }

    [Fact]
    public void ContextSwitchBind_RaisesPgraphIrq()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        bool asserted = false;
        nv2A.InterruptChanged += level => asserted = level;
        nv2A.Write(Base + 0x400140u, 4, 0x00001000u);
        nv2A.Write(Base + 0x0140u, 4, 0x00001000u);
        nv2A.Write(Base + 0x2210u, 4, 0x00000100u);
        nv2A.Write(Base + 0x710000u + 0x42u * 8u, 4, 0x00000042u);
        nv2A.Write(Base + 0x710000u + 0x42u * 8u + 4u, 4, 0x80010120u);
        memory.WritePhysical(0x0000u, 0x00040000u);
        memory.WritePhysical(0x0004u, 0x00000042u);
        memory.WritePhysical(0x0008u, 0x00001001u);

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.True(asserted);
        Assert.Equal(0x00001000u, nv2A.Read(Base + 0x400100u, 4));
    }

    [Fact]
    public void SemaphoreRelease_WritesValueToDmaTarget()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        nv2A.Write(Base + 0x2210u, 4, 0x00000100u);
        nv2A.Write(Base + 0x710000u + 0x40u, 4, 0x00000008u);
        nv2A.Write(Base + 0x710000u + 0x44u, 4, 0x8000111Bu);
        nv2A.Write(Base + 0x7111B0u, 4, 0x0000003Du);
        nv2A.Write(Base + 0x7111B4u, 4, 0x00000020u);
        nv2A.Write(Base + 0x7111B8u, 4, 0x00008000u);
        memory.WritePhysical(0x0000u, 0x000401A4u);
        memory.WritePhysical(0x0004u, 0x00000008u);
        memory.WritePhysical(0x0008u, 0x00041D6Cu);
        memory.WritePhysical(0x000Cu, 0x00000004u);
        memory.WritePhysical(0x0010u, 0x00041D70u);
        memory.WritePhysical(0x0014u, 0x0000CAFEu);
        memory.WritePhysical(0x0018u, 0x00001001u);

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0u, nv2A.PusherError);
        Assert.True(nv2A.MethodCounts.ContainsKey(0x1A4u));
        Assert.True(nv2A.MethodCounts.ContainsKey(0x1D6Cu));
        Assert.True(nv2A.MethodCounts.ContainsKey(0x1D70u));
        Assert.Equal(1, nv2A.SemaphoreReleases);
        Assert.Equal(0x0000CAFEu, memory.ReadPhysical(0x00008004u));
    }

    [Fact]
    public void NoOperation_RaisesPgraphNotification()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        bool asserted = false;
        nv2A.InterruptChanged += level => asserted = level;
        nv2A.Write(Base + 0x400140u, 4, 0x00100000u);
        nv2A.Write(Base + 0x0140u, 4, 0x00000001u);
        memory.WritePhysical(0x0000u, 0x00040100u);
        memory.WritePhysical(0x0004u, 0x0000031Cu);
        memory.WritePhysical(0x0008u, 0x00001001u);

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.True(asserted);
        Assert.True(nv2A.InterruptAsserted);
        Assert.Equal(0x00100000u, nv2A.Read(Base + 0x400100u, 4));
        Assert.Equal(1u, nv2A.Read(Base + 0x400108u, 4));
        Assert.Equal(0x00000100u, nv2A.Read(Base + 0x400704u, 4));
        Assert.Equal(0x0000031Cu, nv2A.Read(Base + 0x400708u, 4));
    }

    [Fact]
    public void InlineTriangle_RasterizesGouraudFill()
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
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

        nv2A.Write(Base + 0x800040u, 4, 0x00001000u);

        Assert.Equal(0u, nv2A.PusherError);
        Assert.Equal(0u, nv2A.SkippedDraws);
        Assert.Equal(1u, nv2A.DrawsExecuted);
        Assert.Equal(0xFF794343u, memory.ReadPhysical(0x00002450u));
        Assert.Equal(0u, memory.ReadPhysical(0x00001000u));
    }
}
