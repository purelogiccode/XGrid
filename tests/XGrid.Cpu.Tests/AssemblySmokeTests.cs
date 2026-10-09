using System.Reflection;
using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void CpuAssembly_IsReferenced()
    {
        Assert.Equal("XGrid.Cpu", Assembly.Load("XGrid.Cpu").GetName().Name);
    }
}
