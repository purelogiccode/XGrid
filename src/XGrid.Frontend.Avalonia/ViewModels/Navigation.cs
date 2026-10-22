using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using XGrid.Frontend.Avalonia.Services;

namespace XGrid.Frontend.Avalonia.ViewModels;

/// <summary>One selectable accent swatch.</summary>
public sealed partial class AccentOption : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Creates an option around a palette entry.</summary>
    public AccentOption(AccentPalette palette)
    {
        Palette = palette;
    }

    /// <summary>Gets the palette entry.</summary>
    public AccentPalette Palette { get; }

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
public sealed class NavigationItem : ObservableObject
{
    /// <summary>Creates a navigation item.</summary>
    public NavigationItem(PageKind kind, string title, string subtitle, Geometry icon, PageViewModel page)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Icon = icon;
        Page = page;
    }

    /// <summary>Gets the destination kind.</summary>
    public PageKind Kind { get; }

    /// <summary>Gets the navigation label.</summary>
    public string Title { get; }

    /// <summary>Gets the short description.</summary>
    public string Subtitle { get; }

    /// <summary>Gets the icon geometry.</summary>
    public Geometry Icon { get; }

    /// <summary>Gets the destination view model.</summary>
    public PageViewModel Page { get; }
}

/// <summary>Base class for a navigable page view model.</summary>
public abstract class PageViewModel : ObservableObject
{
    /// <summary>Creates a page bound to the application shell.</summary>
    protected PageViewModel(MainWindowViewModel shell)
    {
        Shell = shell;
    }

    /// <summary>Gets the application shell.</summary>
    public MainWindowViewModel Shell { get; }
}
