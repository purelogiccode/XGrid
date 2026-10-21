namespace XGrid.Frontend.Avalonia.Models;

/// <summary>
/// Per-game overrides applied on top of the global configuration when a disc or
/// XBE image is launched. A null member inherits the global setting, so an
/// override only has to carry what the title needs.
/// </summary>
/// <remarks>
/// The key is the launched image path (ISO, CHD or XBE) as recorded by the
/// library/recent list. Title-id keying needs the XBE read out of the disc in
/// the core layer and is the documented V2 of this feature.
/// </remarks>
public sealed class TitleOverride
{
    /// <summary>Gets or sets the per-title fixed-function vertex stage switch (<c>--ffp</c>).</summary>
    public bool? FixedFunctionVertex { get; set; }

    /// <summary>Gets or sets the per-title texture filter mode ("legacy" or "registers").</summary>
    public string? TextureFilter { get; set; }

    /// <summary>Gets or sets the per-title renderer ("software" or "gl"; null inherits).</summary>
    public string? GpuRenderer { get; set; }

    /// <summary>Gets or sets the per-title guest RAM size in megabytes.</summary>
    public int? RamMb { get; set; }

    /// <summary>Creates an independent copy of this override.</summary>
    public TitleOverride Clone()
    {
        return new TitleOverride
        {
            FixedFunctionVertex = FixedFunctionVertex,
            TextureFilter = TextureFilter,
            GpuRenderer = GpuRenderer,
            RamMb = RamMb,
        };
    }

    /// <summary>Returns true when the override carries no value at all.</summary>
    public bool IsEmpty => FixedFunctionVertex is null
        && string.IsNullOrEmpty(TextureFilter)
        && string.IsNullOrEmpty(GpuRenderer)
        && RamMb is null;

    /// <summary>Builds a one-line summary for the settings list.</summary>
    public string Describe()
    {
        var parts = new System.Collections.Generic.List<string>(4);
        if (FixedFunctionVertex == true)
        {
            parts.Add("fixed-function vertex");
        }
        else if (FixedFunctionVertex == false)
        {
            parts.Add("resident vertex program");
        }

        if (!string.IsNullOrEmpty(TextureFilter))
        {
            parts.Add("filter: " + TextureFilter);
        }

        if (!string.IsNullOrEmpty(GpuRenderer))
        {
            parts.Add("renderer: " + GpuRenderer);
        }

        if (RamMb is not null)
        {
            parts.Add(RamMb.Value + " MB");
        }

        return parts.Count == 0 ? "inherit everything" : string.Join(", ", parts);
    }
}
