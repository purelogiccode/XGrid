using System;
using System.IO;
using System.Runtime.InteropServices;

namespace XGrid.Diagnostics;

/// <summary>Captures the host details every bug report must include.</summary>
public sealed record EnvironmentSnapshot
{
    /// <summary>Gets the capture time.</summary>
    public DateTimeOffset Date { get; init; }

    /// <summary>Gets the application name.</summary>
    public string ApplicationName { get; init; } = "XGrid";

    /// <summary>Gets the application version.</summary>
    public string ApplicationVersion { get; init; } = "0.0.0";

    /// <summary>Gets the operating-system description.</summary>
    public string OsVersion { get; init; } = string.Empty;

    /// <summary>Gets the process architecture.</summary>
    public string Architecture { get; init; } = string.Empty;

    /// <summary>Gets the process and operating-system bitness.</summary>
    public string Bitness { get; init; } = string.Empty;

    /// <summary>Gets the platform name and version.</summary>
    public string PlatformVersion { get; init; } = string.Empty;

    /// <summary>Gets the logical processor count.</summary>
    public int ProcessorCount { get; init; }

    /// <summary>Gets the application base directory.</summary>
    public string BaseDirectory { get; init; } = string.Empty;

    /// <summary>Gets the temporary directory.</summary>
    public string TempPath { get; init; } = string.Empty;

    /// <summary>Captures the current process environment.</summary>
    /// <param name="applicationName">The application name.</param>
    /// <param name="applicationVersion">The application version.</param>
    /// <returns>A populated snapshot.</returns>
    public static EnvironmentSnapshot Capture(string applicationName, string applicationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);

        return new EnvironmentSnapshot
        {
            Date = DateTimeOffset.Now,
            ApplicationName = applicationName,
            ApplicationVersion = applicationVersion,
            OsVersion = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Bitness = FormattableString.Invariant(
                $"{BitnessOf(Environment.Is64BitProcess)} process, {BitnessOf(Environment.Is64BitOperatingSystem)} OS"),
            PlatformVersion = PlatformName() + " " + Environment.OSVersion.Version,
            ProcessorCount = Environment.ProcessorCount,
            BaseDirectory = AppContext.BaseDirectory,
            TempPath = Path.GetTempPath(),
        };
    }

    /// <summary>Formats the environment block used by bug reports.</summary>
    /// <returns>A multi-line environment section.</returns>
    public string ToText()
    {
        return string.Join(
            Environment.NewLine,
            "=== Environment Details ===",
            "Date: " + Date.ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture),
            "Application Name: " + ApplicationName,
            "Application Version: " + ApplicationVersion,
            "OS Version: " + OsVersion,
            "Architecture: " + Architecture,
            "Bitness: " + Bitness,
            "Windows, Linux or MacOsX Version: " + PlatformVersion,
            "Processor Count: " + ProcessorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Base Directory: " + BaseDirectory,
            "Temp Path: " + TempPath);
    }

    private static string PlatformName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        return "Unknown";
    }

    private static string BitnessOf(bool is64Bit)
    {
        return is64Bit ? "64-bit" : "32-bit";
    }
}
