using System;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class DecodedBlockCacheTests
{
    [Fact]
    public void Invalidation_DiscardsActiveCursorBeforeNextMember()
    {
        var blocks = new DecodedBlockCache();
        var decodes = new DecodeCache { Blocks = blocks };
        Fill(decodes, 0x1000, 0x90);
        Fill(decodes, 0x1001, 0x90);
        Fill(decodes, 0x1002, 0xC3);
        Assert.True(blocks.TryNext(decodes, 0x1000, 32, 32, out _));
        Assert.Equal(1UL, blocks.Dispatches);
        decodes.DropPhysicalPage(1);
        Assert.False(blocks.TryNext(decodes, 0x1001, 32, 32, out _));
        Assert.Equal(0, blocks.Count);
        Assert.Equal(1UL, blocks.Invalidations);

        Fill(decodes, 0x1000, 0x90);
        Fill(decodes, 0x1001, 0x90);
        Assert.True(blocks.TryNext(decodes, 0x1000, 32, 32, out _));
        decodes.Flush();
        decodes.Flush(); // Empty-table flush must be harmless too.
        Assert.False(blocks.TryNext(decodes, 0x1001, 32, 32, out _));
        Assert.Equal(2UL, blocks.Invalidations);
    }

    [Fact]
    public void Formation_UsesOnlyExistingSinglePageWindows_AndEndsAtBranch()
    {
        var blocks = new DecodedBlockCache();
        var decodes = new DecodeCache { Blocks = blocks };
        Fill(decodes, 0x1000, 0x90);
        Fill(decodes, 0x1001, 0xC3);
        Fill(decodes, 0x1002, 0x90);
        Assert.True(blocks.TryNext(decodes, 0x1000, 32, 32, out _));
        Assert.True(blocks.TryNext(decodes, 0x1001, 32, 32, out _));
        Assert.Equal(1UL, blocks.Dispatches);
        Assert.Equal(1UL, blocks.Completed);
        Assert.True(blocks.TryNext(decodes, 0x1002, 32, 32, out _));
        Assert.Equal(2UL, blocks.Dispatches);
        Assert.False(blocks.TryNext(decodes, 0x1003, 32, 32, out _));

        Fill(decodes, 0x1FF1, 0x90); // Last full 15-byte window.
        Fill(decodes, 0x1FF2, 0x90); // Defensively rejected even if inserted directly.
        Assert.True(blocks.TryNext(decodes, 0x1FF1, 32, 32, out _));
        Assert.False(blocks.TryNext(decodes, 0x1FF2, 32, 32, out _));
    }

    [Fact]
    public void Cap_BoundsArraysAndInvalidatesCursor()
    {
        var blocks = new DecodedBlockCache();
        var decodes = new DecodeCache { Blocks = blocks };
        for (uint i = 0; i <= DecodedBlockCache.MaxBlocks; i++)
        {
            uint address = (i + 1) * 32;
            Fill(decodes, address, 0x90);
            Fill(decodes, address + 1, 0xC3);
            Assert.True(blocks.TryNext(decodes, address, 32, 32, out _));
        }

        Assert.Equal(DecodedBlockCache.MaxBlocks, blocks.PeakCount);
        Assert.Equal(1UL, blocks.CapFlushes);
        Assert.Equal(1, blocks.Count);
        Assert.True(blocks.TryNext(decodes, (DecodedBlockCache.MaxBlocks + 1u) * 32 + 1, 32, 32,
            out DecodeCacheEntry last));
        Assert.Equal(Mnemonic.Ret, last.Instruction.Mnemonic);
    }

    private static void Fill(DecodeCache cache, uint linear, byte opcode)
    {
        Span<byte> bytes = stackalloc byte[15];
        bytes.Clear();
        bytes[0] = opcode;
        cache.Insert(linear, Decoder.Decode(bytes, 32, 32), linear, 0, 0, bytes, 32, 32);
    }
}
