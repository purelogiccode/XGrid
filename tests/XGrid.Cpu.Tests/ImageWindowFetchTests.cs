using XGrid.Memory;
using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// §237: instruction fetches from immutable image windows (flash/BIOS, its
/// mirror, the MCPX ROM overlay) are cached and blocked. The decode cache
/// treats them as a class of their own — no store invalidation is possible, a
/// store into the window drops them conservatively, and the host drops them
/// when the image state moves.
/// </summary>
public sealed class ImageWindowFetchTests
{
    private const uint RomBase = 0xF0000000;
    private const uint RomSize = 0x00010000;

    // inc eax; inc eax; jmp back to the first inc (3 instructions per pass).
    private static readonly byte[] IncLoop = [0x40, 0x40, 0xEB, 0xFC];

    // mov eax,[0x2000]; dec ecx; jnz back to the head; hlt.
    private static readonly byte[] RamReadLoop = [0xA1, 0x00, 0x20, 0x00, 0x00, 0x49, 0x75, 0xF8, 0xF4];

    // mov eax,0x11111111; mov [0xF0000010],eax; hlt — a store into the image window.
    private static readonly byte[] StoreIntoImage = [0xB8, 0x11, 0x11, 0x11, 0x11, 0xA3, 0x10, 0x00, 0x00, 0xF0, 0xF4];

    [Fact]
    public void ImageWindow_IsClassifiedAndAdvertisedAsAnImmutableWindow()
    {
        var memory = new PhysicalMemory(0x10000);
        var bus = new AddressSpace(memory, UnmappedAccessPolicy.Throw);
        bus.AddRegion("ROM", RomBase, RomSize, new RomDevice(RomBase, new byte[RomSize]));

        Assert.True(bus.TryGetImmutableImageWindow(RomBase + 0x123, out uint windowBase, out uint windowSize));
        Assert.Equal(RomBase, windowBase);
        Assert.Equal(RomSize, windowSize);

        // Base RAM, an unmapped address and a non-image device window are not
        // image-classified (the store path must stay untouched for them).
        Assert.False(bus.TryGetImmutableImageWindow(0x1000, out _, out _));
        Assert.False(bus.TryGetImmutableImageWindow(0x80000000, out _, out _));

        var register = new TestMmioDevice();
        bus.AddRegion("MMIO", 0xFD000000, 0x1000, register);
        Assert.False(bus.TryGetImmutableImageWindow(0xFD000004, out _, out _));
    }

    [Fact]
    public void ImageWindow_CodeIsFetchedAtMostOncePerInstruction()
    {
        XboxCpu cpu = CreateImageCpu(IncLoop);
        cpu.DecodeCacheEnabled = true;

        cpu.Execute(300);

        Assert.Equal(200u, cpu.Context.Eax);
        Assert.Equal(0UL, cpu.DecodeCacheSkippedNonRam);
        Assert.True(cpu.DecodeCacheImageFills >= 3, $"imageFills={cpu.DecodeCacheImageFills}");
        Assert.True(cpu.DecodeCacheHits > 250, $"hits={cpu.DecodeCacheHits}");
        Assert.Equal(0UL, cpu.DecodeCacheVerifyFailures);

        // Three instruction heads live in the window, so the image cache holds
        // exactly those (not one entry per executed instruction).
        Assert.Equal(3, cpu.DecodeCacheImageEntries);
    }

    [Fact]
    public void ImageWindow_VerifiedDecodeStaysExactAcrossAHostFlush()
    {
        XboxCpu cpu = CreateImageCpu(RamReadLoop);
        cpu.DecodeCacheEnabled = true;
        cpu.DecodeCacheVerify = true;
        cpu.Ram().Write32(0x2000, 0xDEADBEEF);
        cpu.Context.Ecx = 4;

        cpu.Execute(200);
        Assert.True(cpu.Context.Halted);
        Assert.Equal(0xDEADBEEFu, cpu.Context.Eax);
        Assert.Equal(0UL, cpu.DecodeCacheVerifyFailures);

        // The host API is what the overlay-disable and snapshot-load sites
        // call: the next fetch re-fills from the bus and stays exact.
        cpu.InvalidateDecodeCache();
        cpu.Context.Ecx = 3;
        cpu.Context.Eip = 0;
        cpu.Context.Halted = false;
        cpu.Execute(200);

        Assert.True(cpu.Context.Halted);
        Assert.Equal(0xDEADBEEFu, cpu.Context.Eax);
        Assert.Equal(0UL, cpu.DecodeCacheVerifyFailures);
        Assert.True(cpu.DecodeCacheHits > 0);
    }

    [Fact]
    public void ImageWindow_AGuestStoreIntoTheWindowDropsTheCachedDecodes()
    {
        XboxCpu cpu = CreateImageCpu(StoreIntoImage);
        cpu.DecodeCacheEnabled = true;
        cpu.Execute(1);
        Assert.Equal(1, cpu.DecodeCacheImageEntries);

        cpu.Execute(1);

        // The store cannot change the image bytes (the device ignores it), but
        // the cache drops the window's entries conservatively.
        Assert.Equal(1UL, cpu.DecodeCacheImageFlushes);
        Assert.Equal(0, cpu.DecodeCacheImageEntries);
        Assert.Equal(0UL, cpu.DecodeCacheVerifyFailures);

        // The dropped head is simply re-decoded from the bus.
        cpu.Execute(1);
        Assert.Equal(1, cpu.DecodeCacheImageEntries);
    }

    [Fact]
    public void ImageWindow_CodeRunsThroughJitBlocks_AndMatchesTheInterpreter()
    {
        XboxCpu plain = CreateImageCpu(RamReadLoop);
        plain.Ram().Write32(0x2000, 0x01020304);
        plain.Context.Ecx = 5;
        XboxCpu jit = CreateImageCpu(RamReadLoop);
        jit.Ram().Write32(0x2000, 0x01020304);
        jit.Context.Ecx = 5;
        EnableJit(jit);

        plain.Execute(600);
        jit.Execute(600);

        Assert.True(plain.Context.Halted);
        Assert.True(jit.Context.Halted);
        Assert.Equal(plain.Context.Eip, jit.Context.Eip);
        Assert.Equal(plain.Context.Ecx, jit.Context.Ecx);
        Assert.Equal(plain.Context.Eax, jit.Context.Eax);
        Assert.Equal(plain.Context.Tsc, jit.Context.Tsc);
        Assert.Equal(0UL, jit.DecodeCacheSkippedNonRam);
        Assert.True(jit.JitBlocksBuilt > 0, "no block formed over the image window");
        Assert.True(jit.JitServedTotal > 10, $"served={jit.JitServedTotal}");
    }

    [Fact]
    public void JitModeChange_ReformsTheBlocksInsteadOfYieldingAtTheHeadForever()
    {
        // 16-bit defaults form the head's block; flipping CS.D makes the same
        // linear head 32-bit. The cached block's decode defaults no longer
        // match, so RunJitBlock must drop it and re-form (a stale block that is
        // merely skipped strands the head on the member path for good).
        XboxCpu cpu = CreateImageCpu([0x90, 0x90, 0xEB, 0xFC]);
        cpu.Context.CodeSegment32 = false;
        Assert.Equal(16, cpu.Context.DefaultOperandSize);
        EnableJit(cpu);

        cpu.Execute(200);
        ulong served16 = cpu.JitServedTotal;
        Assert.True(served16 > 100, $"served={served16}");
        Assert.True(cpu.JitBlocksBuilt > 0);

        cpu.Context.CodeSegment32 = true;
        Assert.Equal(32, cpu.Context.DefaultOperandSize);

        cpu.Execute(400);

        Assert.True(
            cpu.JitServedTotal >= served16 + 300,
            $"16-bit served={served16}, total={cpu.JitServedTotal}: the head did not re-form");
        Assert.True(cpu.JitBlocksBuilt >= 2, $"blocks={cpu.JitBlocksBuilt}");
        Assert.Equal(32, cpu.Context.DefaultOperandSize);
    }

    private static void EnableJit(XboxCpu cpu)
    {
        cpu.DecodeCacheEnabled = true;
        cpu.SpecExecutionEnabled = true;
        cpu.JitSpecSlice = int.MaxValue;
        cpu.JitBuildThreshold = 1;
        cpu.JitEnabled = true;
        cpu.JitRawBlocks = true;
    }

    /// <summary>
    /// Builds a CPU whose CS base points at <see cref="RomBase"/> and whose
    /// image window holds <paramref name="code"/>; data/stack segments stay at
    /// base 0 so the guest can read and write base RAM.
    /// </summary>
    private static XboxCpu CreateImageCpu(byte[] code)
    {
        var memory = new PhysicalMemory(0x10000);
        var bus = new AddressSpace(memory, UnmappedAccessPolicy.Throw);
        var image = new byte[RomSize];
        code.CopyTo(image, 0);
        bus.AddRegion("ROM", RomBase, RomSize, new RomDevice(RomBase, image));

        var cpu = new XboxCpu(bus);
        cpu.Reset();
        cpu.Context.Cr0 = 1;
        cpu.Context.CodeSegment32 = true;
        cpu.Context.SetSegment(SegmentRegister.Cs, 0x08, RomBase);
        cpu.Context.SetSegment(SegmentRegister.Ds, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Ss, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Es, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Fs, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Gs, 0x10, 0);
        cpu.Context.Eflags = FlagBits.Reserved1;
        cpu.Context.Esp = 0x8000;
        cpu.Context.Eip = 0;
        return cpu;
    }

    private sealed class TestMmioDevice : IMmioDevice
    {
        public uint Read(uint address, int size)
        {
            return 0;
        }

        public void Write(uint address, int size, uint value)
        {
        }
    }
}
