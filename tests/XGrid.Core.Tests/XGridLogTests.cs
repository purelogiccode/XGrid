using XGrid.Core.Diagnostics;
using Xunit;

namespace XGrid.Core.Tests;

public sealed class XGridLogTests
{
    [Fact]
    public void IsEnabled_DefaultSink_ReturnsFalse()
    {
        Assert.False(XGridLog.IsEnabled(LogCategory.Cpu, LogLevel.Error));
    }

    [Fact]
    public void SetSink_RoutesEventsToSink()
    {
        var sink = new RecordingSink();
        XGridLog.SetSink(sink);
        try
        {
            Assert.True(XGridLog.IsEnabled(LogCategory.Nv2a, LogLevel.Warning));
            XGridLog.Write(LogCategory.Nv2a, LogLevel.Warning, "test");
            Assert.Equal(1, sink.Count);
            Assert.Equal(LogCategory.Nv2a, sink.LastCategory);
            Assert.Equal(LogLevel.Warning, sink.LastLevel);
            Assert.Equal("test", sink.LastMessage);
        }
        finally
        {
            XGridLog.SetSink(null);
        }
    }

    private sealed class RecordingSink : ILogSink
    {
        public int Count { get; private set; }

        public LogCategory LastCategory { get; private set; }

        public LogLevel LastLevel { get; private set; }

        public string? LastMessage { get; private set; }

        public bool IsEnabled(LogCategory category, LogLevel level) => true;

        public void Write(LogCategory category, LogLevel level, string message)
        {
            Count++;
            LastCategory = category;
            LastLevel = level;
            LastMessage = message;
        }
    }
}
