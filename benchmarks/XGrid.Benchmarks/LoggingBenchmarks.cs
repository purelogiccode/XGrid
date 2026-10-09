using BenchmarkDotNet.Attributes;
using XGrid.Core.Diagnostics;

namespace XGrid.Benchmarks;

[MemoryDiagnoser]
public class LoggingBenchmarks
{
    [Benchmark]
    public static bool IsEnabled_DefaultSink()
    {
        return XGridLog.IsEnabled(LogCategory.Cpu, LogLevel.Trace);
    }
}
