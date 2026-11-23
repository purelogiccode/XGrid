using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>One selectable accent swatch.</summary>
/// <remarks>Creates an option around a palette entry.</remarks>
public sealed partial class AccentOption(AccentPalette palette) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Gets the palette entry.</summary>
    private AccentPalette Palette { get; } = palette;

    /// <summary>Gets the swatch fill.</summary>
    public IBrush Brush => Palette.Brush;

    /// <summary>Gets the display label.</summary>
    public string Label => Palette.Label;

    /// <summary>Gets the selection key.</summary>
    public string Key => Palette.Key;
}

/// <summary>Identifies a navigation destination.</summary>
public enum PageKind
{
    /// <summary>Console home and live display.</summary>
    Home,

    /// <summary>Game library.</summary>
    Library,

    /// <summary>Emulator settings.</summary>
    Settings,

    /// <summary>Activity log.</summary>
    Logs,

    /// <summary>About and environment information.</summary>
    About,
}

/// <summary>A sidebar destination plus its page view model.</summary>
/// <remarks>Creates a navigation item.</remarks>
public sealed partial class NavigationItem(PageKind kind, string title, Geometry icon, PageViewModel page) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Gets the destination kind.</summary>
    public PageKind Kind { get; } = kind;

    /// <summary>Gets the navigation label.</summary>
    public string Title { get; } = title;

    /// <summary>Gets the icon geometry.</summary>
    public Geometry Icon { get; } = icon;

    /// <summary>Gets the destination view model.</summary>
    public PageViewModel Page { get; } = page;
}

/// <summary>Base class for a navigable page view model.</summary>
/// <remarks>Creates a page bound to the application shell.</remarks>
public abstract partial class PageViewModel(MainWindowViewModel shell) : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Gets the application shell.</summary>
    public MainWindowViewModel Shell { get; } = shell;
}
