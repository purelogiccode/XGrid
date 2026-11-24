using System;
using System.Globalization;

namespace XGrid.Diagnostics;

/// <summary>Compares simple dotted product versions.</summary>
public static class VersionComparison
{
    /// <summary>Returns whether <paramref name="candidate"/> is a newer release than <paramref name="current"/>.</summary>
    /// <param name="candidate">The remote tag or version.</param>
    /// <param name="current">The running product version.</param>
    /// <returns><see langword="true"/> only when both versions parse and the candidate is greater.</returns>
    public static bool IsNewer(string? candidate, string? current)
    {
        if (!TryParse(candidate, out Version? remote) || !TryParse(current, out Version? local))
        {
            return false;
        }

        return remote > local;
    }

    /// <summary>Parses a tag such as <c>v1.2.3</c> or <c>1.2.3-beta</c>.</summary>
    /// <param name="text">The tag text.</param>
    /// <param name="version">The parsed version.</param>
    /// <returns><see langword="true"/> when the numeric core is valid.</returns>
    public static bool TryParse(string? text, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        int metadata = value.IndexOf('+', StringComparison.Ordinal);
        if (metadata >= 0)
        {
            value = value[..metadata];
        }

        int prerelease = value.IndexOf('-', StringComparison.Ordinal);
        if (prerelease >= 0)
        {
            value = value[..prerelease];
        }

        string[] parts = value.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        Span<int> numbers = stackalloc int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                || number < 0)
            {
                return false;
            }

            numbers[i] = number;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }
}
