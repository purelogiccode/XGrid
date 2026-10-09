using System.Reflection;
using Xunit;

namespace XGrid.Memory.Tests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void MemoryAssembly_IsReferenced()
    {
        Assert.Equal("XGrid.Memory", Assembly.Load("XGrid.Memory").GetName().Name);
    }
}
