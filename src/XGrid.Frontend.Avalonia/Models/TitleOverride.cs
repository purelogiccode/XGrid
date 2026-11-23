using System.Collections.Generic;

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

    /// <summary>Creates an independent copy of this override.</summary>
    public TitleOverride Clone()
    {
        return new TitleOverride
        {
            RamMb = RamMb,
        };
    }

    /// <summary>Returns true when the override carries no value at all.</summary>
    public bool IsEmpty => RamMb is null;

    /// <summary>Builds a one-line summary for the settings list.</summary>
    public string Describe()
    {
        var parts = new List<string>(1);
        if (RamMb is not null)
        {
            parts.Add(RamMb.Value + " MB");
        }

        return parts.Count == 0 ? "inherit everything" : string.Join(", ", parts);
    }
}
