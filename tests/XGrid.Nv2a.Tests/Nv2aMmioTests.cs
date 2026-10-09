using Xunit;

namespace XGrid.Nv2a.Tests;

public sealed class Nv2aMmioTests
{
    private const uint Base = Nv2aMmio.BaseAddress;

    [Fact]
    public void PmcBoot0_ReportsNv2aStepping()
    {
        var nv2a = new Nv2aMmio();

        Assert.Equal(0x02A000A3u, nv2a.Read(Base + 0x000000u, 4));
    }

    [Fact]
    public void Pbus_MirrorsPciIdAndClass()
    {
        var nv2a = new Nv2aMmio();

        Assert.Equal(0x02A010DEu, nv2a.Read(Base + 0x001000u, 4));
        Assert.Equal(0x030000A1u, nv2a.Read(Base + 0x001008u, 4));
    }

    [Fact]
    public void Pfb_ReportsRamSizeAndCfgDefault()
    {
        var nv2a = new Nv2aMmio(64u * 1024 * 1024);

        Assert.Equal(64u * 1024 * 1024, nv2a.Read(Base + 0x10020Cu, 4));
        Assert.Equal(0x03070103u, nv2a.Read(Base + 0x100200u, 4));
        Assert.Equal(0u, nv2a.Read(Base + 0x10030Cu, 4));
    }

    [Fact]
    public void PfifoCache1Status_HasLowMarkAfterReset()
    {
        var nv2a = new Nv2aMmio();

        Assert.Equal(0x10u, nv2a.Read(Base + 0x003214u, 4));
    }

    [Fact]
    public void Pramdac_ReportsResetClocksAndLockedPlls()
    {
        var nv2a = new Nv2aMmio();

        Assert.Equal(0x00011C01u, nv2a.Read(Base + 0x680500u, 4));
        Assert.Equal(0u, nv2a.Read(Base + 0x680504u, 4));
        Assert.Equal(0x0003C20Du, nv2a.Read(Base + 0x680508u, 4));
        Assert.Equal(0xD8000000u, nv2a.Read(Base + 0x680514u, 4));
    }

    [Fact]
    public void PtimerTime_AdvancesWithCoreTicks()
    {
        var nv2a = new Nv2aMmio();
        uint before = nv2a.Read(Base + 0x009400u, 4);

        nv2a.Advance(1_000_000);

        Assert.NotEqual(before, nv2a.Read(Base + 0x009400u, 4));
    }

    [Fact]
    public void PcrtcRaster_IncrementsOnEachRead()
    {
        var nv2a = new Nv2aMmio();

        uint first = nv2a.Read(Base + 0x600808u, 4);
        uint second = nv2a.Read(Base + 0x600808u, 4);

        Assert.Equal(first + 1, second);
    }

    [Fact]
    public void PcrtcStart_MasksTo27Bits()
    {
        var nv2a = new Nv2aMmio();

        nv2a.Write(Base + 0x600800u, 4, 0xFFFFFFFFu);

        Assert.Equal(0x07FFFFFFu, nv2a.Read(Base + 0x600800u, 4));
    }

    [Fact]
    public void PmcIntr_WriteClearsPendingBits()
    {
        var nv2a = new Nv2aMmio();
        nv2a.Write(Base + 0x000140u, 4, 0xFFFFFFFFu);

        Assert.Equal(0u, nv2a.Read(Base + 0x000100u, 4));
    }

    [Fact]
    public void Pramin_RetainsWrittenWords()
    {
        var nv2a = new Nv2aMmio();

        nv2a.Write(Base + 0x700000u, 4, 0x12345678u);

        Assert.Equal(0x12345678u, nv2a.Read(Base + 0x700000u, 4));
    }

    [Fact]
    public void SubWordAccesses_ShiftWithinAlignedWord()
    {
        var nv2a = new Nv2aMmio();

        Assert.Equal(0xA3u, nv2a.Read(Base + 0x000000u, 1));
        Assert.Equal(0x00A3u, nv2a.Read(Base + 0x000000u, 2));
        Assert.Equal(0x02A0u, nv2a.Read(Base + 0x000002u, 2));
    }

    [Fact]
    public void StubBlocks_ReadZero()
    {
        var nv2a = new Nv2aMmio();

        Assert.Equal(0u, nv2a.Read(Base + 0x007000u, 4));
        Assert.Equal(0u, nv2a.Read(Base + 0x101000u, 4));
    }

    [Fact]
    public void UnmappedGapOffsets_ReadZeroAndIgnoreWrites()
    {
        var nv2a = new Nv2aMmio();

        nv2a.Write(Base + 0x200000u, 4, 0xDEADBEEFu);

        Assert.Equal(0u, nv2a.Read(Base + 0x200000u, 4));
        Assert.Equal(0u, nv2a.Read(Base + 0x500000u, 4));
    }

    [Fact]
    public void RegisterAccesses_AreCountedPerBlock()
    {
        var nv2a = new Nv2aMmio();

        nv2a.Read(Base + 0x000000u, 4);
        nv2a.Write(Base + 0x680600u, 4, 1u);

        Assert.Equal(1, nv2a.ReadCounts[0x000000u]);
        Assert.Equal(1, nv2a.WriteCounts[0x680000u]);
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        var nv2a = new Nv2aMmio();
        nv2a.Write(Base + 0x680500u, 4, 0xDEADBEEFu);
        nv2a.Write(Base + 0x600800u, 4, 0x1000u);

        nv2a.Reset();

        Assert.Equal(0x00011C01u, nv2a.Read(Base + 0x680500u, 4));
        Assert.Equal(0u, nv2a.Read(Base + 0x600800u, 4));
        Assert.Equal(0x10u, nv2a.Read(Base + 0x003214u, 4));
    }
}
