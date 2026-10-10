using XGrid.Bus.Aci;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class McpxAciTests
{
    private const uint Base = McpxAciDevice.BaseAddress;

    // NABM register offsets relative to the window base.
    private const uint NamLimit = 0x100;
    private const uint PoBdbar = NamLimit + 0x10;
    private const uint PoLvi = NamLimit + 0x15;
    private const uint PoSr = NamLimit + 0x16;
    private const uint PoCr = NamLimit + 0x1B;
    private const uint GlobCnt = NamLimit + 0x2C;
    private const uint GlobSta = NamLimit + 0x30;
    private const uint Cas = NamLimit + 0x34;
    private const uint SoBdbar = NamLimit + 0x70;
    private const uint SoSr = NamLimit + 0x76;
    private const uint SoCr = NamLimit + 0x7B;

    [Fact]
    public void GlobStaRead_IncludesS0CrBit()
    {
        var aci = new McpxAciDevice();

        uint value = aci.Read(Base + GlobSta, 4);

        Assert.Equal(1u << 8, value & (1u << 8));
    }

    [Fact]
    public void CasRead_ReturnsZeroThenLocks_UntilNamAccess()
    {
        var aci = new McpxAciDevice();

        Assert.Equal(0u, aci.Read(Base + Cas, 1));
        Assert.Equal(1u, aci.Read(Base + Cas, 1));

        aci.Read(Base + 0x26, 2);

        Assert.Equal(0u, aci.Read(Base + Cas, 1));
    }

    [Fact]
    public void NamWrite_ReleasesCas()
    {
        var aci = new McpxAciDevice();

        aci.Read(Base + Cas, 1);
        Assert.Equal(1u, aci.Read(Base + Cas, 1));

        aci.Write(Base + 0x26, 2, 0);

        Assert.Equal(0u, aci.Read(Base + Cas, 1));
    }

    [Fact]
    public void GlobCnt_ColdAndWarmResetRequestsAreIgnored()
    {
        var aci = new McpxAciDevice();

        aci.Write(Base + GlobCnt, 4, 0x2);
        Assert.Equal(0u, aci.Read(Base + GlobCnt, 4));
        aci.Write(Base + GlobCnt, 4, 0x4);
        Assert.Equal(0u, aci.Read(Base + GlobCnt, 4));

        aci.Write(Base + GlobCnt, 4, 0x1);
        Assert.Equal(1u, aci.Read(Base + GlobCnt, 4));
    }

    [Fact]
    public void NamPowerdownWrite_KeepsLowNibble()
    {
        var aci = new McpxAciDevice();

        Assert.Equal(0x000Fu, aci.Read(Base + 0x26, 2));

        aci.Write(Base + 0x26, 2, 0xFFFF);

        Assert.Equal(0x7FFFu, aci.Read(Base + 0x26, 2));
    }

    [Fact]
    public void NamVolumeWrites_AreMasked()
    {
        var aci = new McpxAciDevice();

        aci.Write(Base + 0x02, 2, 0xFFFF);
        aci.Write(Base + 0x18, 2, 0xFFFF);
        aci.Write(Base + 0x1C, 2, 0xFFFF);

        Assert.Equal(0xBF3Fu, aci.Read(Base + 0x02, 2));
        Assert.Equal(0x9F1Fu, aci.Read(Base + 0x18, 2));
        Assert.Equal(0x8F0Fu, aci.Read(Base + 0x1C, 2));
    }

    [Fact]
    public void NamReset_RestoresCodecDefaults()
    {
        var aci = new McpxAciDevice();

        aci.Write(Base + 0x02, 2, 0x0000);
        Assert.Equal(0x0000u, aci.Read(Base + 0x02, 2));

        aci.Write(Base + 0x00, 2, 0x0000);

        Assert.Equal(0x8000u, aci.Read(Base + 0x02, 2));
        Assert.Equal(0x8384u, aci.Read(Base + 0x7C, 2));
        Assert.Equal(0x7600u, aci.Read(Base + 0x7E, 2));
        Assert.Equal(0x0809u, aci.Read(Base + 0x28, 2));
        Assert.Equal(0xBB80u, aci.Read(Base + 0x2C, 2));
        Assert.Equal(0x000Fu, aci.Read(Base + 0x26, 2));
    }

    [Fact]
    public void VendorIdRegisters_AreReadOnly()
    {
        var aci = new McpxAciDevice();

        aci.Write(Base + 0x7C, 2, 0x1234);
        aci.Write(Base + 0x7E, 2, 0x5678);

        Assert.Equal(0x8384u, aci.Read(Base + 0x7C, 2));
        Assert.Equal(0x7600u, aci.Read(Base + 0x7E, 2));
    }

    [Fact]
    public void BdbAR_MasksLowTwoBits()
    {
        var aci = new McpxAciDevice();

        aci.Write(Base + PoBdbar, 4, 0x00123457);

        Assert.Equal(0x00123454u, aci.Read(Base + PoBdbar, 4));
    }

    [Fact]
    public void PoControlReset_ResetsBlockToDch()
    {
        var aci = new McpxAciDevice();

        aci.Write(Base + PoBdbar, 4, 0x1000);
        aci.Write(Base + PoLvi, 1, 0x15);
        aci.Write(Base + PoCr, 1, 0x01);
        Assert.Equal(0u, aci.Read(Base + PoSr, 2) & 1u);

        aci.Write(Base + PoCr, 1, 0x02);

        Assert.Equal(0u, aci.Read(Base + PoBdbar, 4));
        Assert.Equal(0u, aci.Read(Base + PoLvi, 1));
        Assert.Equal(1u, aci.Read(Base + PoSr, 2) & 1u);
    }

    [Fact]
    public void SrWrite_CannotSetReadOnlyBits()
    {
        var aci = new McpxAciDevice();

        aci.Write(Base + PoSr, 2, 0x0003);

        Assert.Equal(1u, aci.Read(Base + PoSr, 2));
    }

    [Fact]
    public void SoBlock_UsesOffset0x70()
    {
        var aci = new McpxAciDevice();

        aci.Write(Base + SoBdbar, 4, 0x2000);
        Assert.Equal(0x2000u, aci.Read(Base + SoBdbar, 4));

        aci.Write(Base + SoCr, 1, 0x02);

        Assert.Equal(0u, aci.Read(Base + SoBdbar, 4));
        Assert.Equal(1u, aci.Read(Base + SoSr, 2) & 1u);
    }

    [Fact]
    public void BootInitSequence_PassesCasAndGlobStaPolls()
    {
        // Mirrors the kernel's ACI init: GLOB_CNT bit1 request, GLOB_STA bit8 poll,
        // CAS poll, codec volume/powerdown writes, then PO/SO control resets.
        var aci = new McpxAciDevice();

        uint globCnt = aci.Read(Base + GlobCnt, 4);
        Assert.Equal(0u, globCnt & 0x2);
        aci.Write(Base + GlobCnt, 4, globCnt | 0x2);

        Assert.NotEqual(0u, aci.Read(Base + GlobSta, 4) & 0x100u);

        Assert.Equal(0u, aci.Read(Base + Cas, 1));
        aci.Write(Base + 0x26, 2, 0);

        aci.Write(Base + PoCr, 1, 0x02);
        aci.Write(Base + SoCr, 1, 0x02);

        Assert.Equal(0u, aci.Read(Base + Cas, 1));
        aci.Write(Base + 0x02, 2, 0);
        Assert.Equal(0u, aci.Read(Base + Cas, 1));
        aci.Write(Base + 0x18, 2, 0x808);
        Assert.Equal(0x0808u, aci.Read(Base + 0x18, 2));
    }
}
