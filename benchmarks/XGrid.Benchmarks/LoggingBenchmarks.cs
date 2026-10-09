using BenchmarkDotNet.Attributes;
using XGrid.Core.Diagnostics;

namespace XGrid.Benchmarks;

[MemoryDiagnoser]
public class LoggingBenchmarks
{
    private readonly LogLevel _level = LogLevel.Trace;

    [Benchmark]
    public bool IsEnabled_DefaultSink()
    {
        return XGridLog.IsEnabled(LogCategory.Cpu, _level);
    }
}
