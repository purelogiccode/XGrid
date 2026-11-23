using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Parses and formats the Avalonia key names stored in input settings.</summary>
public static class KeyboardKeys
{
    /// <summary>Parses a comma-separated key list, ignoring unknown or duplicate entries.</summary>
    /// <param name="text">Stored key list.</param>
    public static IReadOnlyList<Key> ParseList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var keys = new List<Key>();
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParse(part, out Key key) && !keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>Parses one stored key name.</summary>
    /// <param name="text">Stored key name.</param>
    /// <param name="key">Parsed key.</param>
    public static bool TryParse(string? text, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string name = text.Trim();
        if (string.Equals(name, "Backspace", StringComparison.OrdinalIgnoreCase))
        {
            key = Key.Back;
            return true;
        }

        return Enum.TryParse(name, ignoreCase: true, out key)
            && key != Key.None
            && Enum.IsDefined(key);
    }

    /// <summary>Formats one key for storage and display.</summary>
    /// <param name="key">Avalonia key.</param>
    public static string Format(Key key)
    {
        return key == Key.Back ? "Backspace" : key.ToString();
    }

    /// <summary>Canonicalizes a stored key list (valid names only, comma-separated).</summary>
    /// <param name="text">Stored key list.</param>
    public static string NormalizeList(string? text)
    {
        return string.Join(",", ParseList(text).Select(Format).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Returns the display text for a stored key list.</summary>
    /// <param name="text">Stored key list.</param>
    public static string Describe(string? text)
    {
        IReadOnlyList<Key> keys = ParseList(text);
        return keys.Count == 0 ? "Unbound" : string.Join(", ", keys.Select(Format));
    }
}
