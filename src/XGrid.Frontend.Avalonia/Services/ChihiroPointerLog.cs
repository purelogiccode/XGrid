using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>One pointer device reported in the core log.</summary>
/// <param name="Index">The core's stable slot number.</param>
/// <param name="Name">The device name used to pin it to a player.</param>
/// <param name="Kind">Absolute, relative, or empty when the core does not say.</param>
public sealed record ChihiroPointerDevice(int Index, string Name, string Kind)
{
    /// <summary>Gets the device as a readable drop-down label.</summary>
    public string Label
    {
        get
        {
            string head = Index.ToString(CultureInfo.InvariantCulture) + ": " + DisplayName;
            return Kind.Length == 0 ? head : head + " (" + Kind + ")";
        }
    }

    private string DisplayName
    {
        get
        {
            if (!Name.StartsWith("\\\\?\\HID#", StringComparison.Ordinal))
            {
                return Name;
            }

            string[] parts = Name.Split('#');
            return parts.Length >= 2 ? "HID " + parts[1] : Name;
        }
    }
}

/// <summary>
/// Reads the per-device pointer lines the core writes to <c>xemu.log</c>. The
/// core owns device enumeration on every platform; Settings only presents the
/// names so a gun can be pinned instead of the built-in pointer.
/// </summary>
public static class ChihiroPointerLog
{
    private const string Prefix = @"^(?:\[[^\]]*\]\[[^\]]*\]\s*)?";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex LinuxPattern = new(
        Prefix + @"linux pointer (?<index>\d+): (?<name>.*) \(/dev/input/event\d+\) (?<kind>absolute|relative)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        RegexTimeout);
    private static readonly Regex MacPattern = new(
        Prefix + @"mac pointer (?<index>\d+): (?<name>.*) \((?<kind>absolute|relative)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        RegexTimeout);
    private static readonly Regex WindowsPattern = new(
        Prefix + @"raw pointer (?<index>\d+): (?<name>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        RegexTimeout);

    /// <summary>Reads the pointer devices from a core log.</summary>
    /// <param name="path">Path to <c>xemu.log</c>.</param>
    /// <returns>Devices sorted by the core's slot number.</returns>
    public static IReadOnlyList<ChihiroPointerDevice> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        var devices = new Dictionary<int, ChihiroPointerDevice>();
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                ChihiroPointerDevice? device = Parse(line);
                if (device is not null)
                {
                    devices[device.Index] = device;
                }
            }
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return [.. devices.Values.OrderBy(device => device.Index)];
    }

    private static ChihiroPointerDevice? Parse(string line)
    {
        Match match = LinuxPattern.Match(line);
        if (match.Success)
        {
            return Device(match, "index", "name", "kind");
        }

        match = MacPattern.Match(line);
        if (match.Success)
        {
            return Device(match, "index", "name", "kind");
        }

        match = WindowsPattern.Match(line);
        return match.Success ? Device(match, "index", "name", null) : null;
    }

    private static ChihiroPointerDevice? Device(
        Match match, string indexGroup, string nameGroup, string? kindGroup)
    {
        if (!int.TryParse(
                match.Groups[indexGroup].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int index))
        {
            return null;
        }

        string name = match.Groups[nameGroup].Value.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        string kind = kindGroup is null ? string.Empty : match.Groups[kindGroup].Value;
        return new ChihiroPointerDevice(index, name, kind);
    }
}
