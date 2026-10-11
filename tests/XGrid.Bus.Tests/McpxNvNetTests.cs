using XGrid.Bus.Net;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class McpxNvNetTests
{
    private const uint Base = McpxNvNetDevice.BaseAddress;

    private const uint IrqStatus = 0x000;
    private const uint IrqMask = 0x004;
    private const uint TxStatus = 0x088;
    private const uint RxStatus = 0x098;
    private const uint MacAddrA = 0x0A8;
    private const uint MacAddrB = 0x0AC;
    private const uint SetupReg5 = 0x130;
    private const uint TxRxControl = 0x144;
    private const uint AdapterControl = 0x188;
    private const uint MdioAddr = 0x190;
    private const uint MdioData = 0x194;

    [Fact]
    public void ResetDefaults_AreIdleWithLinkUp()
    {
        var net = new McpxNvNetDevice();

        Assert.Equal(0u, net.Read(Base + TxStatus, 4));
        Assert.Equal(0u, net.Read(Base + RxStatus, 4));
        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
        Assert.NotEqual(0u, net.Read(Base + TxRxControl, 4) & (1u << 3));
        Assert.NotEqual(0u, net.Read(Base + AdapterControl, 4) & (1u << 2));
    }

    [Fact]
    public void BusyWaitLoop_ExitsImmediately()
    {
        var net = new McpxNvNetDevice();

        // Mirrors the guest TX/RX-busy wait: with idle status it never stalls.
        Assert.Equal(0u, net.Read(Base + TxStatus, 1) & 1u);
        Assert.Equal(0u, net.Read(Base + RxStatus, 1) & 1u);
    }

    [Fact]
    public void ControlIdle_StaysSet_AcrossWrites()
    {
        var net = new McpxNvNetDevice();

        net.Write(Base + TxRxControl, 4, 0x4);

        Assert.Equal(0xCu, net.Read(Base + TxRxControl, 4));
    }

    [Fact]
    public void ZeroWrite_SetsSetupReg5Bit31()
    {
        var net = new McpxNvNetDevice();

        net.Write(Base + TxRxControl, 4, 0);

        Assert.NotEqual(0u, net.Read(Base + SetupReg5, 4) & (1u << 31));
    }

    [Fact]
    public void Bit1Write_ClearsIrqStatus()
    {
        var net = new McpxNvNetDevice();

        net.Write(Base + TxRxControl, 4, 0x2);

        Assert.Equal(0u, net.Read(Base + IrqStatus, 4));
    }

    [Fact]
    public void MacDefault_MatchesQemu()
    {
        var net = new McpxNvNetDevice();

        Assert.Equal(0x12005452u, net.Read(Base + MacAddrA, 4));
        Assert.Equal(0x00005634u, net.Read(Base + MacAddrB, 4));

        net.Write(Base + MacAddrA, 4, 0xAABBCCDD);

        Assert.Equal(0xAABBCCDDu, net.Read(Base + MacAddrA, 4));
    }

    [Fact]
    public void MdioRead_CompletesInstantly_WithLinkUpBmsr()
    {
        var net = new McpxNvNetDevice();

        // Select PHY register 1 (BMSR), read op: INUSE must read clear.
        net.Write(Base + MdioAddr, 4, 0x1);
        Assert.Equal(0u, net.Read(Base + MdioAddr, 4) & (1u << 15));

        uint bmsr = net.Read(Base + MdioData, 4);
        Assert.NotEqual(0u, bmsr & 0x04u);
        Assert.NotEqual(0u, bmsr & 0x20u);
    }

    [Fact]
    public void MdioWrite_StoresPhyRegister()
    {
        var net = new McpxNvNetDevice();

        net.Write(Base + MdioData, 4, 0x1234);
        net.Write(Base + MdioAddr, 4, 0x400 | 0x4);
        net.Write(Base + MdioAddr, 4, 0x4);

        Assert.Equal(0x1234u, net.Read(Base + MdioData, 4));
    }

    [Fact]
    public void ByteReads_ReturnCorrectByte()
    {
        var net = new McpxNvNetDevice();

        net.Write(Base + IrqMask, 4, 0xAABBCCDD);

        Assert.Equal(0xDDu, net.Read(Base + IrqMask, 1));
        Assert.Equal(0xCCu, net.Read(Base + IrqMask + 1, 1));
        Assert.Equal(0xBBu, net.Read(Base + IrqMask + 2, 1));
        Assert.Equal(0xAAu, net.Read(Base + IrqMask + 3, 1));
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        var net = new McpxNvNetDevice();

        net.Write(Base + IrqMask, 4, 0xFFFFFFFF);
        net.Write(Base + AdapterControl, 4, 0);
        net.Reset();

        Assert.Equal(0u, net.Read(Base + IrqMask, 4));
        Assert.NotEqual(0u, net.Read(Base + AdapterControl, 4) & (1u << 2));
        Assert.NotEqual(0u, net.Read(Base + TxRxControl, 4) & (1u << 3));
    }

    [Fact]
    public void RegisterHistograms_CountAlignedAccesses()
    {
        var net = new McpxNvNetDevice();

        net.Read(Base + TxStatus, 4);
        net.Read(Base + TxStatus + 1, 1);
        net.Write(Base + TxRxControl, 4, 0x4);

        Assert.Equal(2, net.RegReadCounts[TxStatus]);
        Assert.Equal(1, net.RegWriteCounts[TxRxControl]);
    }

    [Fact]
    public void Reset_ClearsRegisterHistograms()
    {
        var net = new McpxNvNetDevice();

        net.Read(Base + TxStatus, 4);
        net.Reset();

        Assert.Empty(net.RegReadCounts);
        Assert.Empty(net.RegWriteCounts);
    }
}
