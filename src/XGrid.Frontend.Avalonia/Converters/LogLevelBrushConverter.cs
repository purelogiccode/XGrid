using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.Converters;

/// <summary>Maps a <see cref="LogLevel"/> to its display brush.</summary>
public sealed class LogLevelBrushConverter : IValueConverter
{
    /// <summary>Shared converter instance for XAML static references.</summary>
    public static LogLevelBrushConverter Instance { get; } = new();

    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#7FB3D8"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F2B84B"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF6B7D"));
    private static readonly IBrush DebugBrush = new SolidColorBrush(Color.Parse("#8A9DB3"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            LogLevel.Warning => WarningBrush,
            LogLevel.Error => ErrorBrush,
            LogLevel.Debug => DebugBrush,
            _ => InfoBrush,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
