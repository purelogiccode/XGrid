using System;
using Xunit;

namespace XGrid.Memory.Tests;

public sealed class AddressSpaceTests
{
    private static AddressSpace CreateSpace(uint ramSize = 64u * 1024 * 1024)
    {
        return new AddressSpace(new PhysicalMemory(ramSize));
    }

    [Fact]
    public void UnmappedAccesses_AreCountedPerMegabyteBucket()
    {
        AddressSpace space = CreateSpace();

        space.Read32(0xFD000000);
        space.Read32(0xFD000010);
        space.Write32(0xFE800000, 1);

        Assert.Equal(2, space.UnmappedAccessCounts[0xFD0]);
        Assert.Equal(1, space.UnmappedAccessCounts[0xFE8]);
    }

    [Fact]
    public void WatchRange_ReportsWritesInsideRangeOnly()
    {
        AddressSpace space = CreateSpace();
        uint hitAddress = 0;
        space.WatchStart = 0xFD000000;
        space.WatchEnd = 0xFD000100;
        space.WatchHit += (address, _, _) => { hitAddress = address; };

        space.Write32(0xFD000010, 1);
        space.Write32(0xFE000000, 1);

        Assert.Equal(0xFD000010u, hitAddress);
    }

    [Fact]
    public void Ram_ReadWrite_UsesFastPath()
    {
        AddressSpace space = CreateSpace();

        space.Write32(0x100, 0xCAFEBABE);

        Assert.Equal(0xCAFEBABEu, space.Read32(0x100));
        Assert.True(space.IsRam(0x100));
    }

    [Fact]
    public void Ram_Read64_UsesFastPath()
    {
        AddressSpace space = CreateSpace();

        space.Write64(0x200, 0x0123456789ABCDEF);

        Assert.Equal(0x0123456789ABCDEFUL, space.Read64(0x200));
    }

    [Fact]
    public void MmioRegion_RoutesAbsoluteAddressAndSize()
    {
        AddressSpace space = CreateSpace();
        var device = new RecordingMmioDevice { ReadValue = 0x12345678 };
        space.AddRegion("usb0", 0xFED00000, 0x1000, device);

        uint value = space.Read32(0xFED00010);
        space.Write32(0xFED00014, 0xAABBCCDD);

        Assert.Equal(0x12345678u, value);
        Assert.Equal(2, device.Accesses.Count);
        Assert.True(device.Accesses[0].IsRead);
        Assert.Equal(0xFED00010u, device.Accesses[0].Address);
        Assert.Equal(4, device.Accesses[0].Size);
        Assert.False(device.Accesses[1].IsRead);
        Assert.Equal(0xFED00014u, device.Accesses[1].Address);
        Assert.Equal(0xAABBCCDDu, device.Accesses[1].Value);
    }

    [Fact]
    public void MmioRegion_SupportsByteAndWordSizes()
    {
        AddressSpace space = CreateSpace();
        var device = new RecordingMmioDevice { ReadValue = 0x0000FF00 };
        space.AddRegion("dev", 0xFD000000, 0x100, device);

        byte b = space.Read8(0xFD000001);
        ushort w = space.Read16(0xFD000002);
        space.Write8(0xFD000003, 0x42);

        Assert.Equal(0x00, b);
        Assert.Equal(0xFF00, w);
        Assert.Equal(1, device.Accesses[0].Size);
        Assert.Equal(2, device.Accesses[1].Size);
        Assert.Equal(1, device.Accesses[2].Size);
    }

    [Fact]
    public void Unmapped_Read_ReturnsSentinel()
    {
        AddressSpace space = CreateSpace();

        Assert.Equal(0xFF, space.Read8(0xFD000000));
        Assert.Equal(0xFFFF, space.Read16(0xFD000000));
        Assert.Equal(0xFFFFFFFFu, space.Read32(0xFD000000));
    }

    [Fact]
    public void Unmapped_Write_IsIgnored()
    {
        AddressSpace space = CreateSpace();

        space.Write32(0xFD000000, 0x12345678);

        Assert.Equal(0xFFFFFFFFu, space.Read32(0xFD000000));
    }

    [Fact]
    public void ThrowPolicy_UnmappedAccess_Throws()
    {
        AddressSpace space = CreateSpace();
        space.UnmappedPolicy = UnmappedAccessPolicy.Throw;

        Assert.Throws<MemoryAccessException>(() => space.Read32(0xFD000000));
        Assert.Throws<MemoryAccessException>(() => space.Write32(0xFD000000, 1));
    }

    [Fact]
    public void OverlappingRegions_HighestPriorityWins()
    {
        AddressSpace space = CreateSpace();
        var low = new RecordingMmioDevice { ReadValue = 0x11111111 };
        var high = new RecordingMmioDevice { ReadValue = 0x22222222 };
        space.AddRegion("low", 0xFD000000, 0x1000, low, priority: 0);
        space.AddRegion("high", 0xFD000000, 0x1000, high, priority: 1);

        Assert.Equal(0x22222222u, space.Read32(0xFD000000));
    }

    [Fact]
    public void OverlappingRegions_EqualPriority_LatestWins()
    {
        AddressSpace space = CreateSpace();
        var first = new RecordingMmioDevice { ReadValue = 0x11111111 };
        var second = new RecordingMmioDevice { ReadValue = 0x22222222 };
        space.AddRegion("first", 0xFD000000, 0x1000, first);
        space.AddRegion("second", 0xFD000000, 0x1000, second);

        Assert.Equal(0x22222222u, space.Read32(0xFD000000));
    }

    [Fact]
    public void TryGetRegion_Unmapped_ReturnsFalse()
    {
        AddressSpace space = CreateSpace();

        Assert.False(space.TryGetRegion(0xFD000000, out _));
    }

    [Fact]
    public void TryGetRegion_Mapped_ReturnsRegion()
    {
        AddressSpace space = CreateSpace();
        space.AddRegion("nv2a", 0xFD000000, 0x1000, new RecordingMmioDevice());

        Assert.True(space.TryGetRegion(0xFD000500, out AddressRegion? region));
        Assert.NotNull(region);
        Assert.Equal("nv2a", region.Name);
    }

    [Fact]
    public void Regions_AreExposedInRegistrationOrder()
    {
        AddressSpace space = CreateSpace();
        space.AddRegion("a", 0xFD000000, 0x1000, new RecordingMmioDevice());
        space.AddRegion("b", 0xFE000000, 0x1000, new RecordingMmioDevice());

        Assert.Equal(2, space.Regions.Count);
        Assert.Equal("a", space.Regions[0].Name);
        Assert.Equal("b", space.Regions[1].Name);
    }

    [Fact]
    public void GetSpan_OutsideRam_Throws()
    {
        AddressSpace space = CreateSpace();

        Assert.Throws<MemoryAccessException>(() => space.GetSpan(0xFD000000, 4).Length.ToString());
    }

    [Fact]
    public void Read64_Mmio_ComposesTwo32BitReads()
    {
        AddressSpace space = CreateSpace();
        var device = new RecordingMmioDevice { ReadValue = 0x00000002 };
        space.AddRegion("dev", 0xFD000000, 0x1000, device);

        ulong value = space.Read64(0xFD000000);

        Assert.Equal(0x0000000200000002UL, value);
        Assert.Equal(2, device.Accesses.Count);
        Assert.Equal(0xFD000000u, device.Accesses[0].Address);
        Assert.Equal(0xFD000004u, device.Accesses[1].Address);
    }

    [Fact]
    public void Read64_CrossingRamBoundary_UsesSentinelForUnmappedHalf()
    {
        AddressSpace space = CreateSpace(ramSize: 8);
        space.Write32(4, 0x11223344);

        ulong value = space.Read64(4);

        Assert.Equal(0xFFFFFFFF11223344UL, value);
    }

    [Fact]
    public void Region_Contains_HandlesBounds()
    {
        var region = new AddressRegion("test", 0x1000, 0x100, new RecordingMmioDevice(), 0);

        Assert.False(region.Contains(0x0FFF));
        Assert.True(region.Contains(0x1000));
        Assert.True(region.Contains(0x10FF));
        Assert.False(region.Contains(0x1100));
    }

    [Fact]
    public void Region_ZeroSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AddressRegion("test", 0, 0, new RecordingMmioDevice(), 0));
    }

    [Fact]
    public void Region_NullDevice_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AddressRegion("test", 0, 0x10, null!, 0));
    }
}
