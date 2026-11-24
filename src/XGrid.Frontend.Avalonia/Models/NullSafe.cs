namespace XGrid.Frontend.Avalonia.Models;

/// <summary>
/// Null-tolerant conversions for values loaded from disk. A JSON file can carry
/// <c>null</c> for a property whose annotation is non-null, so the normalizers
/// coerce through here instead of dereferencing.
/// </summary>
internal static class NullSafe
{
    /// <summary>Returns the value, or an empty string when it is null.</summary>
    /// <param name="value">Value from the settings file.</param>
    /// <returns>The value, never null.</returns>
    public static string Text(string? value)
    {
        return value ?? string.Empty;
    }

    /// <summary>Returns the value, or the fallback when it is null.</summary>
    /// <typeparam name="T">Reference type.</typeparam>
    /// <param name="value">Value from the settings file.</param>
    /// <param name="fallback">Value to use when <paramref name="value"/> is null.</param>
    /// <returns>The value, never null.</returns>
    public static T Value<T>(T? value, T fallback)
        where T : class
    {
        return value ?? fallback;
    }
}
