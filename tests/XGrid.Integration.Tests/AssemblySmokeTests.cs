using System.Reflection;
using Xunit;

namespace XGrid.Integration.Tests;

public sealed class AssemblySmokeTests
{
    [Fact]
    public void HostAssembly_IsReferenced()
    {
        Assert.Equal("XGrid.Host", Assembly.Load("XGrid.Host").GetName().Name);
    }
}
