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

    [Fact]
    public void Capture_RejectsMissingIdentity()
    {
        Assert.Throws<System.ArgumentException>(() => EnvironmentSnapshot.Capture(" ", "1.0.0"));
        Assert.Throws<System.ArgumentException>(() => EnvironmentSnapshot.Capture("XGrid", " "));
    }

    [Fact]
    public void ToText_IncludesEveryRequiredSection()
    {
        var snapshot = new EnvironmentSnapshot
        {
            Date = new System.DateTimeOffset(2026, 11, 24, 12, 34, 56, System.TimeSpan.FromHours(-3)),
            ApplicationName = "XGrid",
            ApplicationVersion = "1.2.3",
            OsVersion = "Microsoft Windows NT 10.0",
            Architecture = "X64",
            Bitness = "64-bit process, 64-bit OS",
            PlatformVersion = "Windows 10.0",
            ProcessorCount = 12,
            BaseDirectory = "/app/",
            TempPath = "/tmp/",
        };

        string text = snapshot.ToText();

        Assert.Contains("=== Environment Details ===", text, System.StringComparison.Ordinal);
        Assert.Contains("Date: 2026-11-24 12:34:56 -03:00", text, System.StringComparison.Ordinal);
        Assert.Contains("Application Name: XGrid", text, System.StringComparison.Ordinal);
        Assert.Contains("Application Version: 1.2.3", text, System.StringComparison.Ordinal);
        Assert.Contains("OS Version: Microsoft Windows NT 10.0", text, System.StringComparison.Ordinal);
        Assert.Contains("Architecture: X64", text, System.StringComparison.Ordinal);
        Assert.Contains("Bitness: 64-bit process, 64-bit OS", text, System.StringComparison.Ordinal);
        Assert.Contains("Windows, Linux or MacOsX Version: Windows 10.0", text, System.StringComparison.Ordinal);
        Assert.Contains("Processor Count: 12", text, System.StringComparison.Ordinal);
        Assert.Contains("Base Directory: /app/", text, System.StringComparison.Ordinal);
        Assert.Contains("Temp Path: /tmp/", text, System.StringComparison.Ordinal);
    }
}
