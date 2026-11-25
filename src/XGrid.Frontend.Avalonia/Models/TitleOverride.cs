using System;
using System.Collections.Generic;
using XGrid.Core.Xemu;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>
/// Per-game overrides applied on top of the global configuration when a disc or
/// XBE image is launched. A null member inherits the global setting, so an
/// override only has to carry what the title needs.
/// </summary>
/// <remarks>
/// The key is the launched image path (ISO, CHD or XBE) or an XBE certificate
/// title id written as <c>title:XXXXXXXX</c>.
/// </remarks>
public sealed class TitleOverride
{
    /// <summary>Gets or sets the per-title guest RAM size in megabytes.</summary>
    public int? RamMb { get; set; }

    /// <summary>Gets or sets the per-title core renderer (<c>OPENGL</c>, <c>VULKAN</c> or <c>NULL</c>).</summary>
    public string? Renderer { get; set; }

    /// <summary>Gets or sets the per-title core texture filtering (<c>linear</c> or <c>nearest</c>).</summary>
    public string? Filtering { get; set; }

    /// <summary>Gets or sets the per-title presentation filter.</summary>
    public string? Filter { get; set; }

    /// <summary>Creates an independent copy of this override.</summary>
    public TitleOverride Clone()
    {
        return new TitleOverride
        {
            RamMb = RamMb,
            Renderer = Renderer,
            Filtering = Filtering,
            Filter = Filter,
        };
    }

    /// <summary>Returns true when the override carries no value at all.</summary>
    public bool IsEmpty => RamMb is null && Renderer is null && Filtering is null && Filter is null;

    /// <summary>Drops values this build does not implement, leaving them inherited.</summary>
    public void Normalize()
    {
        RamMb = RamMb is 64 or 128 ? RamMb : null;

        string? renderer = Renderer?.Trim().ToUpperInvariant();
        Renderer = renderer is GraphicsSettings.RendererOpenGl
            or GraphicsSettings.RendererVulkan
            or GraphicsSettings.RendererNull
            ? renderer
            : null;

        string? filtering = Filtering?.Trim().ToLowerInvariant();
        Filtering = filtering is GraphicsSettings.FilterLinear or GraphicsSettings.FilterNearest
            ? filtering
            : null;

        string? filter = Filter?.Trim().ToLowerInvariant();
        Filter = null;
        foreach ((string key, _) in XemuPresentationFilter.All)
        {
            if (string.Equals(key, filter, StringComparison.Ordinal))
            {
                Filter = key;
            }
        }
    }

    /// <summary>Builds a one-line summary for the settings list.</summary>
    public string Describe()
    {
        var parts = new List<string>(4);
        if (RamMb is not null)
        {
            parts.Add(RamMb.Value + " MB");
        }

        if (Renderer is not null)
        {
            parts.Add(Renderer switch
            {
                GraphicsSettings.RendererOpenGl => "OpenGL",
                GraphicsSettings.RendererVulkan => "Vulkan",
                _ => "Null renderer",
            });
        }

        if (Filtering is not null)
        {
            parts.Add(Filtering + " texture filtering");
        }

        if (Filter is not null)
        {
            parts.Add(FilterLabel(Filter));
        }

        return parts.Count == 0 ? "inherit everything" : string.Join(", ", parts);
    }

    private static string FilterLabel(string key)
    {
        foreach ((string candidate, string label) in XemuPresentationFilter.All)
        {
            if (string.Equals(candidate, key, StringComparison.Ordinal))
            {
                return label;
            }
        }

        return key;
    }
}
