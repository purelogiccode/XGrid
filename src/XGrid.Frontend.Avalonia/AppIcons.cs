using Avalonia.Media;

namespace XGrid.Frontend.Avalonia;

/// <summary>Fill geometries for the UI icon set (Material Design 24x24 paths).</summary>
public static class AppIcons
{
    /// <summary>Home glyph.</summary>
    public static Geometry Home { get; } = Geometry.Parse(
        "M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z");

    /// <summary>Library grid glyph.</summary>
    public static Geometry Library { get; } = Geometry.Parse(
        "M4 4h6v6H4zm10 0h6v6h-6zM4 14h6v6H4zm10 0h6v6h-6z");

    /// <summary>Settings gear glyph.</summary>
    public static Geometry Settings { get; } = Geometry.Parse(
        "M19.14 12.94a7.07 7.07 0 0 0 0-1.88l2.03-1.58a.5.5 0 0 0 .12-.64l-1.92-3.32" +
        "a.5.5 0 0 0-.61-.22l-2.39 1a7.3 7.3 0 0 0-1.62-.94l-.36-2.54a.5.5 0 0 0-.5-.42" +
        "H10.12a.5.5 0 0 0-.5.42l-.36 2.54a7.3 7.3 0 0 0-1.62.94l-2.39-1a.5.5 0 0 0-.61.22" +
        "L2.72 8.84a.5.5 0 0 0 .12.64l2.03 1.58a7.07 7.07 0 0 0 0 1.88l-2.03 1.58a.5.5 0 0 0-.12.64" +
        "l1.92 3.32a.5.5 0 0 0 .61.22l2.39-1a7.3 7.3 0 0 0 1.62.94l.36 2.54a.5.5 0 0 0 .5.42h4.16" +
        "a.5.5 0 0 0 .5-.42l.36-2.54a7.3 7.3 0 0 0 1.62-.94l2.39 1a.5.5 0 0 0 .61-.22l1.92-3.32" +
        "a.5.5 0 0 0-.12-.64zM12 15.6A3.6 3.6 0 1 1 15.6 12 3.6 3.6 0 0 1 12 15.6z");

    /// <summary>Activity log glyph.</summary>
    public static Geometry Logs { get; } = Geometry.Parse(
        "M3 5h10a2 2 0 0 1 0 4H3a2 2 0 0 1 0-4zm0 6h16a2 2 0 0 1 0 4H3a2 2 0 0 1 0-4z" +
        "m0 6h7a2 2 0 0 1 0 4H3a2 2 0 0 1 0-4z");

    /// <summary>Information glyph.</summary>
    public static Geometry Info { get; } = Geometry.Parse(
        "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z" +
        "m1 15h-2v-6h2v6zm0-8h-2V7h2v2z");

    /// <summary>Power glyph.</summary>
    public static Geometry Power { get; } = Geometry.Parse(
        "M13 3h-2v10h2V3zm4.83 2.17-1.42 1.42C17.99 7.86 19 9.81 19 12c0 3.87-3.13 7-7 7" +
        "s-7-3.13-7-7c0-2.19 1.01-4.14 2.59-5.41L6.17 5.17C4.23 6.82 3 9.26 3 12c0 4.97 4.03 9 9 9" +
        "s9-4.03 9-9c0-2.74-1.23-5.18-3.17-6.83z");

    /// <summary>Play glyph.</summary>
    public static Geometry Play { get; } = Geometry.Parse("M8 5v14l11-7z");

    /// <summary>Pause glyph.</summary>
    public static Geometry Pause { get; } = Geometry.Parse("M6 5h4v14H6zm8 0h4v14h-4z");

    /// <summary>Step-one-frame glyph.</summary>
    public static Geometry StepForward { get; } = Geometry.Parse(
        "M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z");

    /// <summary>Stop glyph.</summary>
    public static Geometry Stop { get; } = Geometry.Parse("M6 6h12v12H6z");

    /// <summary>Folder glyph.</summary>
    public static Geometry Folder { get; } = Geometry.Parse(
        "M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z");

    /// <summary>Disc glyph.</summary>
    public static Geometry Disc { get; } = Geometry.Parse(
        "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 14.5" +
        "c-2.49 0-4.5-2.01-4.5-4.5S9.51 7.5 12 7.5s4.5 2.01 4.5 4.5-2.01 4.5-4.5 4.5z" +
        "m0-5.5c-.55 0-1 .45-1 1s.45 1 1 1 1-.45 1-1-.45-1-1-1z");

    /// <summary>Refresh glyph.</summary>
    public static Geometry Refresh { get; } = Geometry.Parse(
        "M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8" +
        "c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6" +
        "c1.66 0 3.14.69 4.22 1.78L13 11h7V4z");

    /// <summary>Save glyph.</summary>
    public static Geometry Save { get; } = Geometry.Parse(
        "M17 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16" +
        "c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z");

    /// <summary>Copy glyph.</summary>
    public static Geometry Copy { get; } = Geometry.Parse(
        "M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11" +
        "c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z");

    /// <summary>Controller glyph.</summary>
    // ReSharper disable once UnusedMember.Global
    public static Geometry Controller { get; } = Geometry.Parse(
        "M21.58 16.09l-1.09-7.66C20.21 6.46 18.52 5 16.53 5H7.47C5.48 5 3.79 6.46 3.51 8.43" +
        "l-1.09 7.66C2.2 17.63 3.39 19 4.94 19c.68 0 1.32-.27 1.8-.75L9 16h6l2.25 2.25" +
        "c.48.48 1.13.75 1.8.75 1.56 0 2.75-1.37 2.53-2.91zM11 11H9v2H8v-2H6v-1h2V8h1v2h2v1z" +
        "m4-1c-.55 0-1-.45-1-1s.45-1 1-1 1 .45 1 1-.45 1-1 1zm2 3c-.55 0-1-.45-1-1s.45-1 1-1" +
        " 1 .45 1 1-.45 1-1 1z");

    /// <summary>Warning glyph.</summary>
    public static Geometry Warning { get; } = Geometry.Parse(
        "M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z");

    /// <summary>Check glyph.</summary>
    public static Geometry Check { get; } = Geometry.Parse(
        "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z");

    /// <summary>Heart glyph used by the optional donation banner.</summary>
    public static Geometry Heart { get; } = Geometry.Parse(
        "M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3" +
        "c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5" +
        "c0 3.78-3.4 6.86-8.55 11.54L12 21.35z");

    /// <summary>Close glyph.</summary>
    public static Geometry Close { get; } = Geometry.Parse(
        "M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z");

    /// <summary>Keyboard glyph.</summary>
    public static Geometry Keyboard { get; } = Geometry.Parse(
        "M20 5H4c-1.1 0-1.99.9-1.99 2L2 17c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2z" +
        "m-9 3h2v2h-2V8zm0 3h2v2h-2v-2zM8 8h2v2H8V8zm0 3h2v2H8v-2zm-1 2H5v-2h2v2zm0-3H5V8h2v2z" +
        "m9 7H8v-2h8v2zm0-4h-2v-2h2v2zm0-3h-2V8h2v2zm3 3h-2v-2h2v2zm0-3h-2V8h2v2z");

    /// <summary>Enter-fullscreen glyph.</summary>
    public static Geometry Fullscreen { get; } = Geometry.Parse(
        "M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z");

    /// <summary>Fast-forward glyph.</summary>
    public static Geometry FastForward { get; } = Geometry.Parse(
        "M4 18l8.5-6L4 6v12zm9-12v12l8.5-6L13 6z");

    /// <summary>List glyph.</summary>
    public static Geometry List { get; } = Geometry.Parse(
        "M3 13h2v-2H3v2zm0 4h2v-2H3v2zm0-8h2V7H3v2zm4 4h14v-2H7v2zm0 4h14v-2H7v2zM7 7v2h14V7H7z");
}
