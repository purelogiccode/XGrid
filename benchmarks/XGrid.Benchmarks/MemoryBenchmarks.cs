using BenchmarkDotNet.Attributes;
using XGrid.Memory;

namespace XGrid.Benchmarks;

[MemoryDiagnoser]
public class MemoryBenchmarks
{
    private AddressSpace _space = null!;

    [GlobalSetup]
    public void Setup()
    {
        _space = new AddressSpace(new PhysicalMemory());
    }

    [Benchmark]
    public uint RamRead32()
    {
        return _space.Read32(0x00100000);
    }

    [Benchmark]
    public uint RamRead64()
    {
        return (uint)_space.Read64(0x00100000);
    }

    [Benchmark]
    public uint RamWrite32()
    {
        _space.Write32(0x00100000, 0x12345678);
        return 0x12345678;
    }

    [Benchmark]
    public uint RamSpanAccess()
    {
        _space.GetSpan(0x00100000, 4)[0] = 0x5A;
        return _space.GetSpan(0x00100000, 4)[0];
    }
}
