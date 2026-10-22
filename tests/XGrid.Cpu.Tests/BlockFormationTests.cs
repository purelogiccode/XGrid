using Xunit;

namespace XGrid.Cpu.Tests;

// Slice 5 (§167): Cli/Sti continue blocks (straight-line IF writes,
// still fallback-interpreted). Pins the boundary decision plus the
// functional IF toggle under an enabled block cache.
public sealed class BlockFormationTests
{
    private static DecodedInstruction Decode(params byte[] code)
    {
        return Decoder.Decode(code);
    }

    [Fact]
    public void Cli_DoesNotTerminateBlock()
    {
        Assert.Equal(Mnemonic.Cli, Decode(0xFA).Mnemonic);
        Assert.False(DecodedBlockCache.Terminates(Decode(0xFA)));
    }

    [Fact]
    public void Sti_DoesNotTerminateBlock()
    {
        Assert.Equal(Mnemonic.Sti, Decode(0xFB).Mnemonic);
        Assert.False(DecodedBlockCache.Terminates(Decode(0xFB)));
    }

    [Fact]
    public void RetAndHlt_StillTerminateBlock()
    {
        Assert.True(DecodedBlockCache.Terminates(Decode(0xC3)));
        Assert.True(DecodedBlockCache.Terminates(Decode(0xF4)));
    }

    [Fact]
    public void CliStiSequence_TogglesIf_WithBlocksEnabled()
    {
        XboxCpu cpu = CpuHarness.Create(0x90, 0xFA, 0x90, 0xFB, 0x90);
        cpu.DecodeCacheEnabled = true;
        cpu.BlockExecutionEnabled = true;
        cpu.Context.Eflags |= FlagBits.If;
        cpu.Execute(5);

        Assert.True((cpu.Context.Eflags & FlagBits.If) != 0);
        Assert.Equal(CpuHarness.CodeAddress + 5, cpu.Context.Eip);
    }

    [Fact]
    public void Cli_ClearsIf_WithBlocksEnabled()
    {
        XboxCpu cpu = CpuHarness.Create(0xFA);
        cpu.DecodeCacheEnabled = true;
        cpu.BlockExecutionEnabled = true;
        cpu.Context.Eflags |= FlagBits.If;
        cpu.Execute(1);

        Assert.True((cpu.Context.Eflags & FlagBits.If) == 0);
    }
}
