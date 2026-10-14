using System.IO;
using Xunit;

namespace XGrid.Memory.Tests;

/// <summary>
/// Present-upload dirty-bit choke tests (research notes §132 (a*)): the bit
/// lives in PhysicalMemory.Write8/16/32/64 (the single writer path that also
/// covers the 0x80000000 alias / RAM mirror / 0xF0000000 WC view), watched
/// spans are armed by the GL backend, and the GPU-writer-identity
/// suppression counter exempts the NV2A twin's own adapter writes.
/// </summary>
public sealed class PresentDirtyWatchTests
{
    private const uint SpanBase = 0x3000u;
    private const uint SpanLength = 64u;

    [Fact]
    public void Disarmed_WritesNeverDirty()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);

        memory.Write32(0x4000, 0x11223344);
        Assert.False(memory.PresentDirty);

        memory.DisarmPresentWatch();
        memory.ClearPresentDirty();
        memory.Write32(SpanBase, 0x55667788);
        Assert.False(memory.PresentDirty);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void WriteWidths_InsideWatchedSpan_Dirty(int width)
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);

        switch (width)
        {
            case 1:
                memory.Write8(SpanBase + 8, 0xAB);
                break;
            case 2:
                memory.Write16(SpanBase + 8, 0xBEEF);
                break;
            case 4:
                memory.Write32(SpanBase + 8, 0xDEADBEEF);
                break;
            default:
                memory.Write64(SpanBase + 8, 0x0123456789ABCDEF);
                break;
        }

        Assert.True(memory.PresentDirty);
    }

    [Fact]
    public void WriteOutsideWatchedSpan_DoesNotDirty()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);

        memory.Write32(SpanBase - 4, 0x11111111);
        memory.Write32(SpanBase + SpanLength, 0x22222222);
        memory.Write8(SpanBase - 1, 0x33);

        Assert.False(memory.PresentDirty);
    }

    [Fact]
    public void StraddlingWrite_OverlappingSpanEdge_Dirties()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);

        memory.Write32(SpanBase - 2, 0x44556677);
        Assert.True(memory.PresentDirty);

        memory.ClearPresentDirty();
        memory.Write32(SpanBase + SpanLength - 2, 0x8899AABB);
        Assert.True(memory.PresentDirty);
    }

    [Fact]
    public void SuppressedWrite_DoesNotDirty_UnsuppressedAfterEnd_Dirties()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);

        memory.BeginGpuWriteSuppression();
        memory.Write32(SpanBase, 0x01020304);
        memory.Write64(SpanBase + 8, 0x05060708090A0B0C);
        Assert.False(memory.PresentDirty);

        memory.EndGpuWriteSuppression();
        memory.Write32(SpanBase + 16, 0x0D0E0F10);
        Assert.True(memory.PresentDirty);
    }

    [Fact]
    public void NestedSuppression_OnlyOutermostEnd_Resumes()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);

        memory.BeginGpuWriteSuppression();
        memory.BeginGpuWriteSuppression();
        memory.EndGpuWriteSuppression();
        memory.Write32(SpanBase, 0x11223344);
        Assert.False(memory.PresentDirty);

        memory.EndGpuWriteSuppression();
        memory.Write32(SpanBase, 0x55667788);
        Assert.True(memory.PresentDirty);
    }

    [Fact]
    public void Arm_IsIdempotent_AndDoesNotClearDirty()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);
        memory.ArmPresentWatch(SpanBase, SpanLength);
        memory.Write32(SpanBase, 0x11223344);
        Assert.True(memory.PresentDirty);

        // Re-arming the same span at the next flip must not launder dirt.
        memory.ArmPresentWatch(SpanBase, SpanLength);
        Assert.True(memory.PresentDirty);

        memory.ClearPresentDirty();
        Assert.False(memory.PresentDirty);
    }

    [Fact]
    public void Disarm_DoesNotClearDirty()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);
        memory.Write32(SpanBase, 0x11223344);
        Assert.True(memory.PresentDirty);

        memory.DisarmPresentWatch();
        Assert.True(memory.PresentDirty);
    }

    [Fact]
    public void ZeroLengthArm_IsNoOp()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, 0);
        memory.Write32(SpanBase, 0x11223344);
        Assert.False(memory.PresentDirty);
    }

    [Fact]
    public void MultipleSpans_AllWatched()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(0x1000, 0x100);
        memory.ArmPresentWatch(0x8000, 0x100);

        memory.Write32(0x8040, 1);
        Assert.True(memory.PresentDirty);

        memory.ClearPresentDirty();
        memory.Write32(0x1040, 2);
        Assert.True(memory.PresentDirty);

        memory.ClearPresentDirty();
        memory.Write32(0x4000, 3);
        Assert.False(memory.PresentDirty);
    }

    [Fact]
    public void SpanOverflow_RoundRobinEvictsOldest()
    {
        var memory = new PhysicalMemory(0x20000);
        for (uint i = 0; i < 4; i++)
        {
            memory.ArmPresentWatch(0x1000 + (i * 0x1000), 0x100);
        }

        // Fifth span evicts the oldest slot (0x1000): writes there no
        // longer dirty (the documented §135 under-watch corner).
        memory.ArmPresentWatch(0x5000, 0x100);
        memory.Write32(0x1040, 1);
        Assert.False(memory.PresentDirty);

        // The newest spans stay watched.
        memory.Write32(0x5040, 2);
        Assert.True(memory.PresentDirty);

        memory.ClearPresentDirty();
        memory.Write32(0x4040, 3);
        Assert.True(memory.PresentDirty);
    }

    [Fact]
    public void LoadState_WhileArmed_MarksDirty()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);
        memory.Write32(SpanBase, 0x11223344);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            memory.SaveState(writer);
        }

        memory.ClearPresentDirty();
        memory.Write32(SpanBase + 32, 0x55667788);
        memory.ClearPresentDirty();
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        memory.LoadState(reader);

        Assert.True(memory.PresentDirty);
    }

    [Fact]
    public void LoadState_WhileDisarmed_DoesNotMarkDirty()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.Write32(SpanBase, 0x11223344);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            memory.SaveState(writer);
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        memory.LoadState(reader);

        // Not armed = no live FBO = the empty-FBO upload path is the
        // fail-closed mechanism; pre-arm loads must not poison the bit.
        Assert.False(memory.PresentDirty);
    }

    [Fact]
    public void Clear_WhileArmed_MarksDirty()
    {
        var memory = new PhysicalMemory(0x10000);
        memory.ArmPresentWatch(SpanBase, SpanLength);
        memory.Clear();

        Assert.True(memory.PresentDirty);
    }

    [Fact]
    public void RamAliasWrite_MapsToFlatOffset_Dirties()
    {
        // §132 finding ii: an AddressSpace-level watch misses the 0x80000000
        // alias; the choke lives in PhysicalMemory where the alias lands on
        // the same flat offset.
        var memory = new PhysicalMemory(0x10000);
        var alias = new RamAliasDevice(memory, 0x80000000u);
        memory.ArmPresentWatch(SpanBase, SpanLength);

        alias.Write(0x80000000u + SpanBase + 12, 4, 0xCAFEBABE);

        Assert.True(memory.PresentDirty);
        Assert.Equal(0xCAFEBABEu, memory.Read32(SpanBase + 12));
    }

    [Fact]
    public void MarkPresentDirty_And_ClearPresentDirty_RoundTrip()
    {
        var memory = new PhysicalMemory(0x1000);
        memory.MarkPresentDirty();
        Assert.True(memory.PresentDirty);
        memory.ClearPresentDirty();
        Assert.False(memory.PresentDirty);
    }
}
