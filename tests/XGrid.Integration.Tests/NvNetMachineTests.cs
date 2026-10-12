using XGrid.Bus.Net;
using XGrid.Host;
using Xunit;

namespace XGrid.Integration.Tests;

public sealed class NvNetMachineTests
{
    private const uint NetBase = McpxNvNetDevice.BaseAddress;

    [Fact]
    public void TxThroughGuestRam_ReachesBackendAndPicIrq4()
    {
        var machine = new XboxMachine();
        const uint descBase = 0x10000;
        const uint bufAddr = 0x20000;
        machine.Memory.Write32(descBase, bufAddr);
        machine.Memory.Write16(descBase + 4, 1);
        machine.Memory.Write16(descBase + 6, 0x8001);
        machine.Memory.Write8(bufAddr, 0xAA);
        machine.Memory.Write8(bufAddr + 1, 0xBB);

        machine.Net.Write(NetBase + 0x100, 4, descBase);
        machine.Net.Write(NetBase + 0x108, 4, 0);
        machine.Net.Write(NetBase + 0x134, 4, descBase);
        machine.Net.Write(NetBase + 0x084, 4, 1);
        machine.Net.Write(NetBase + 0x004, 4, 0x10);
        machine.Net.Write(NetBase + 0x144, 4, 1);

        var backend = Assert.IsType<LoopbackNetworkBackend>(machine.Net.Backend);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, backend.SentLog[0]);
        Assert.True(machine.Net.InterruptAsserted);
        Assert.NotEqual(0, machine.Bus.Pic.MasterRequest & (1 << 4));

        machine.Net.Write(NetBase + 0x000, 4, 0x10);

        Assert.False(machine.Net.InterruptAsserted);
        Assert.Equal(0, machine.Bus.Pic.MasterRequest & (1 << 4));
    }

    [Fact]
    public void RxThroughGuestRam_DeliversBackendFrameAndPicIrq4()
    {
        var machine = new XboxMachine();
        const uint descBase = 0x10000;
        const uint bufAddr = 0x20000;
        machine.Memory.Write32(descBase, bufAddr);
        machine.Memory.Write16(descBase + 4, 63);
        machine.Memory.Write16(descBase + 6, 0x8000);
        machine.Net.Write(NetBase + 0x104, 4, descBase);
        machine.Net.Write(NetBase + 0x108, 4, 0);
        machine.Net.Write(NetBase + 0x138, 4, descBase);
        machine.Net.Write(NetBase + 0x094, 4, 1);
        machine.Net.Write(NetBase + 0x004, 4, 0x02);

        var backend = Assert.IsType<LoopbackNetworkBackend>(machine.Net.Backend);
        byte[] frame = new byte[16];
        for (int i = 0; i < 6; i++)
        {
            frame[i] = 0xFF;
        }

        frame[14] = 0xAA;
        frame[15] = 0xBB;
        backend.SendPacket(frame);

        machine.Advance(1000);

        Assert.Equal(1, machine.Net.RxPackets);
        Assert.Equal(0xFF, machine.Memory.Read8(bufAddr));
        Assert.Equal(0xAA, machine.Memory.Read8(bufAddr + 14));
        Assert.Equal(0xBB, machine.Memory.Read8(bufAddr + 15));
        Assert.True(machine.Net.InterruptAsserted);
        Assert.NotEqual(0, machine.Bus.Pic.MasterRequest & (1 << 4));

        machine.Net.Write(NetBase + 0x000, 4, 0x02);

        Assert.False(machine.Net.InterruptAsserted);
        Assert.Equal(0, machine.Bus.Pic.MasterRequest & (1 << 4));
    }
}
