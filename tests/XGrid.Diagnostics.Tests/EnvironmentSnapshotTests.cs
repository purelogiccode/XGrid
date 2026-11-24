using Xunit;

namespace XGrid.Diagnostics.Tests;

public sealed class EnvironmentSnapshotTests
{
    [Fact]
    public void Capture_ReportsIdentityAndHostDetails()
    {
        EnvironmentSnapshot snapshot = EnvironmentSnapshot.Capture("XGrid.Tests", "9.8.7");

        Assert.Equal("XGrid.Tests", snapshot.ApplicationName);
        Assert.Equal("9.8.7", snapshot.ApplicationVersion);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.OsVersion));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Architecture));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Bitness));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.PlatformVersion));
        Assert.True(snapshot.ProcessorCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.BaseDirectory));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.TempPath));
    }
}
