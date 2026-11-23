using System;
using System.IO;

namespace XGrid.Core.Xemu;

/// <summary>
/// Locates the core executable and its default firmware images. The packaged
/// layout puts the core beside the application (<c>core/xemu/xemu.exe</c>);
/// the development layout keeps the prebuilt core under <c>data/xemu</c> in
/// the repository root, so a walk up the directory tree finds it.
/// </summary>
public static class XemuPaths
{
    /// <summary>Environment variable that overrides the core executable path.</summary>
    public const string CorePathVariable = "XGRID_XEMU_CORE";

    /// <summary>Relative path of the packaged core.</summary>
    public static string PackagedCoreRelativePath
    {
        get { return Path.Combine("core", "xemu", OperatingSystem.IsWindows() ? "xemu.exe" : "xemu"); }
    }

    /// <summary>Relative path of the development core (repository layout).</summary>
    public static string DevelopmentCoreRelativePath
    {
        get { return Path.Combine("data", "xemu", OperatingSystem.IsWindows() ? "xemu.exe" : "xemu"); }
    }

    /// <summary>
    /// Returns the core executable to launch: the explicit path when it exists,
    /// then the environment override, then the packaged path, then the
    /// development path found by walking up from the application and the
    /// current directory.
    /// </summary>
    /// <param name="explicitPath">A path the user configured, or null.</param>
    /// <returns>The absolute path, or null when no core was found.</returns>
    public static string? FindCore(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(CorePathVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        string packaged = Path.Combine(AppContext.BaseDirectory, PackagedCoreRelativePath);
        if (File.Exists(packaged))
        {
            return Path.GetFullPath(packaged);
        }

        return FindUpwards(AppContext.BaseDirectory, DevelopmentCoreRelativePath)
               ?? FindUpwards(Environment.CurrentDirectory, DevelopmentCoreRelativePath);
    }

    /// <summary>
    /// Returns the directory that holds the development firmware images
    /// (<c>data/xemu</c> with the MCPX ROM, flash ROM, EEPROM and HDD), or null.
    /// </summary>
    /// <param name="corePath">Optional known core path; its directory wins.</param>
    /// <returns>The absolute directory, or null.</returns>
    public static string? FindFirmwareDirectory(string? corePath = null)
    {
        if (!string.IsNullOrWhiteSpace(corePath))
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(corePath));
            if (directory is not null && File.Exists(Path.Combine(directory, "mcpx_1.0.bin")))
            {
                return directory;
            }
        }

        return FindUpwards(AppContext.BaseDirectory, Path.Combine("data", "xemu"))
               ?? FindUpwards(Environment.CurrentDirectory, Path.Combine("data", "xemu"));
    }

    private static string? FindUpwards(string? start, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            directory = directory.Parent;
        }

        return null;
    }
}
