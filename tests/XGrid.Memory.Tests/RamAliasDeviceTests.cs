using System;
using Xunit;

namespace XGrid.Memory.Tests;

public sealed class RamAliasDeviceTests
{
    [Fact]
    public void Alias_MirrorsRam()
    {
        var ram = new PhysicalMemory(4096);
        var alias = new RamAliasDevice(ram, 0x0C000000);
        ram.Write32(0x100, 0xCAFEBABE);

        Assert.Equal(0xCAFEBABEu, alias.Read(0x0C000100, 4));

        alias.Write(0x0C000200, 4, 0xDEADBEEF);

        Assert.Equal(0xDEADBEEFu, ram.Read32(0x200));
    }

    [Fact]
    public void Alias_WrapsAtRamSize()
    {
        var ram = new PhysicalMemory(1024);
        var alias = new RamAliasDevice(ram, 0x0C000000);
        ram.Write8(0, 0x5A);

        Assert.Equal(0x5Au, alias.Read(0x0C000000u + 1024u, 1));
    }

    [Fact]
    public void Alias_SupportsAllWidths()
    {
        var ram = new PhysicalMemory(1024);
        var alias = new RamAliasDevice(ram, 0x0C000000);

        alias.Write(0x0C000000, 1, 0xAB);
        alias.Write(0x0C000002, 2, 0xBEEF);
        alias.Write(0x0C000004, 4, 0xDEADBEEF);

        Assert.Equal(0xABu, alias.Read(0x0C000000, 1));
        Assert.Equal(0xBEEFu, alias.Read(0x0C000002, 2));
        Assert.Equal(0xDEADBEEFu, alias.Read(0x0C000004, 4));
    }

    [Fact]
    public void Alias_UnsupportedSize_Throws()
    {
        var ram = new PhysicalMemory(1024);
        var alias = new RamAliasDevice(ram, 0x0C000000);

        Assert.Throws<ArgumentOutOfRangeException>(() => alias.Read(0x0C000000, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => alias.Write(0x0C000000, 3, 0));
    }

    [Fact]
    public void Alias_NonPowerOfTwoRam_Throws()
    {
        var ram = new PhysicalMemory(1000);

        Assert.Throws<ArgumentException>(() => new RamAliasDevice(ram, 0x0C000000));
    }

    [Fact]
    public void Alias_NullRam_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RamAliasDevice(null!, 0x0C000000));
    }

    [Fact]
    public void Alias_RegisteredInAddressSpace_RoutesThroughRegion()
    {
        var ram = new PhysicalMemory(1024);
        var space = new AddressSpace(ram);
        space.AddRegion("ram-mirror", 0x0C000000, 0x800, new RamAliasDevice(ram, 0x0C000000));
        space.Write32(0x40, 0x11223344);

        Assert.Equal(0x11223344u, space.Read32(0x0C000040));
    }
}
