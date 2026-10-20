using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>A selectable accent color and its derived shades.</summary>
public sealed class AccentPalette
{
    /// <summary>Creates a palette entry.</summary>
    public AccentPalette(string key, string label, Color color)
    {
        Key = key;
        Label = label;
        Color = color;
        Hover = Mix(color, Colors.White, 0.24);
        Soft = Color.FromArgb(38, color.R, color.G, color.B);
    }

    /// <summary>Gets the stable settings key.</summary>
    public string Key { get; }

    /// <summary>Gets the display label.</summary>
    public string Label { get; }

    /// <summary>Gets the primary accent color.</summary>
    public Color Color { get; }

    /// <summary>Gets the pointer-over shade.</summary>
    public Color Hover { get; }

    /// <summary>Gets the translucent selection fill.</summary>
    public Color Soft { get; }

    /// <summary>Gets the swatch brush.</summary>
    public IBrush Brush => new SolidColorBrush(Color);

    /// <summary>The built-in palettes, in display order.</summary>
    public static IReadOnlyList<AccentPalette> All { get; } = new[]
    {
        new AccentPalette("Emerald", "Emerald", Color.Parse("#57E389")),
        new AccentPalette("Aqua", "Aqua", Color.Parse("#4CD7F2")),
        new AccentPalette("Violet", "Violet", Color.Parse("#A78BFA")),
        new AccentPalette("Amber", "Amber", Color.Parse("#F5B84C")),
        new AccentPalette("Crimson", "Crimson", Color.Parse("#FF6B81")),
    };

    /// <summary>Resolves a palette key, falling back to Emerald.</summary>
    public static AccentPalette Find(string? key)
    {
        foreach (AccentPalette palette in All)
        {
            if (string.Equals(palette.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return palette;
            }
        }

        return All[0];
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
        return Color.FromArgb(
            Lerp(from.A, to.A, amount),
            Lerp(from.R, to.R, amount),
            Lerp(from.G, to.G, amount),
            Lerp(from.B, to.B, amount));
    }
}

/// <summary>Applies the selected accent palette to application resources at runtime.</summary>
public static class AccentService
{
    /// <summary>Applies a palette to the current Avalonia application.</summary>
    public static void Apply(AccentPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (Application.Current is not { } application)
        {
            return;
        }

        Color color = palette.Color;
        application.Resources["XgAccentColor"] = color;
        application.Resources["XgAccentBrush"] = new SolidColorBrush(color);
        application.Resources["XgAccentHoverBrush"] = new SolidColorBrush(palette.Hover);
        application.Resources["XgAccentPressedBrush"] = new SolidColorBrush(Mix(color, Colors.Black, 0.28));
        application.Resources["XgAccentSoftBrush"] = new SolidColorBrush(palette.Soft);
        application.Resources["XgAccentTextBrush"] = new SolidColorBrush(Mix(color, Colors.Black, 0.78));

        application.Resources["SystemAccentColor"] = color;
        application.Resources["SystemAccentColorLight1"] = Mix(color, Colors.White, 0.12);
        application.Resources["SystemAccentColorLight2"] = Mix(color, Colors.White, 0.24);
        application.Resources["SystemAccentColorLight3"] = Mix(color, Colors.White, 0.36);
        application.Resources["SystemAccentColorDark1"] = Mix(color, Colors.Black, 0.12);
        application.Resources["SystemAccentColorDark2"] = Mix(color, Colors.Black, 0.24);
        application.Resources["SystemAccentColorDark3"] = Mix(color, Colors.Black, 0.36);
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
        return Color.FromArgb(
            Lerp(from.A, to.A, amount),
            Lerp(from.R, to.R, amount),
            Lerp(from.G, to.G, amount),
            Lerp(from.B, to.B, amount));
    }
}
