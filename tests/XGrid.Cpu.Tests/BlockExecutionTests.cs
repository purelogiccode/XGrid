using System.IO;
using System.Text;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class BlockExecutionTests
{
    private static readonly byte[] Loop = { 0x40, 0x83, 0xF0, 0x35, 0x48, 0x90, 0xEB, 0xF8 };

    [Fact]
    public void EveryStepAndBudget_MatchesDecodeCache_StateAndInstructionCounts()
    {
        XboxCpu plain = Create(false);
        XboxCpu block = Create(true);
        var plainProfile = new HotspotProfiler();
        var blockProfile = new HotspotProfiler();
        plain.HotspotObserver = plainProfile;
        block.HotspotObserver = blockProfile;
        for (int i = 0; i < 200; i++)
        {
            // Alternating the same APIs used by trace/break-log and machine batches.
            if ((i & 1) == 0)
            {
                plain.Step();
                block.Step();
            }
            else
            {
                Assert.Equal(plain.Execute(i), block.Execute(i));
            }

            Assert.Equal(State(plain), State(block));
            Assert.Equal(plain.DecodeCacheHits, block.DecodeCacheHits);
            Assert.Equal(plain.DecodeCount, block.DecodeCount);
            Assert.Equal(plainProfile.TotalInstructions, blockProfile.TotalInstructions);
        }

        Assert.True(block.BlockStatistics!.Instructions > 1000);
        Assert.True(block.BlockStatistics.Dispatches < block.BlockStatistics.Instructions / 2);
    }

    [Fact]
    public void Verify_RejectsStaleMemberBeforeExecution()
    {
        XboxCpu cpu = Create(true);
        cpu.BlockVerify = true;
        cpu.Execute(5); // Warm whole loop.
        cpu.Step(); // First member of active block.
        cpu.Ram().Write8(0x1001, 0x90); // Bypass hooks deliberately.
        cpu.Step();
        Assert.Equal(0x1002u, cpu.Context.Eip); // Fresh NOP, not stale XOR.
        Assert.Equal(1UL, cpu.BlockStatistics!.VerifyFailures);
    }

    [Fact]
    public void HostWriteAndSnapshotLoad_DiscardActiveMembers()
    {
        XboxCpu cpu = Create(true);
        cpu.Execute(6);
        byte[] state = State(cpu);
        cpu.Ram().Write8(0x1001, 0x90);
        cpu.InvalidateDecodeCachePage(0x1001);
        cpu.Step();
        Assert.Equal(0x1002u, cpu.Context.Eip);
        using var stream = new MemoryStream(state);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        cpu.LoadState(reader);
        Assert.Equal(0, cpu.BlockStatistics!.Count);
        cpu.Step();
        Assert.Equal(0x1002u, cpu.Context.Eip);
    }

    private static XboxCpu Create(bool block)
    {
        XboxCpu cpu = CpuHarness.Create(Loop);
        cpu.DecodeCacheEnabled = true;
        cpu.BlockExecutionEnabled = block;
        return cpu;
    }

    private static byte[] State(XboxCpu cpu)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            cpu.SaveState(writer);
        }

        return stream.ToArray();
    }
}
