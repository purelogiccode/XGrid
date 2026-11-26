using System.IO;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>
/// Sega Chihiro cabinet configuration. A Chihiro is an Xbox with a media
/// board, a DIMM and a baseboard, so it uses its own flash image and the
/// media-board firmware instead of the Xbox hard disk.
/// </summary>
public sealed class ChihiroSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether a launch uses the Chihiro
    /// machine. A netboot image (<c>.bin</c>) selects it for that launch even
    /// when this is false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the folder holding the Chihiro firmware. Empty uses the
    /// folder beside the Xbox BIOS image.
    /// </summary>
    /// <remarks>
    /// Expected files: <c>chihiro_xbox_bios.bin</c>,
    /// <c>fpr21042_m29w160et.bin</c>, <c>ic10_g24lc64.bin</c>,
    /// <c>ic11_24lc024.bin</c> and <c>pc20_g24lc64.bin</c>.
    /// <c>ver1305.bin</c> is optional but required by network titles such as
    /// MJ2 and MJ3, which check its version.
    /// </remarks>
    public string FirmwareDirectory { get; set; } = string.Empty;

    /// <summary>The Chihiro flash ROM file name.</summary>
    public const string BiosFileName = "chihiro_xbox_bios.bin";

    /// <summary>The media-board flash file name.</summary>
    public const string MediaboardFileName = "fpr21042_m29w160et.bin";

    /// <summary>The QC EEPROM file name.</summary>
    public const string Ic10FileName = "ic10_g24lc64.bin";

    /// <summary>The baseboard EEPROM file name.</summary>
    public const string Ic11FileName = "ic11_24lc024.bin";

    /// <summary>The SC EEPROM file name.</summary>
    public const string Pc20FileName = "pc20_g24lc64.bin";

    /// <summary>The optional network-board firmware file name.</summary>
    public const string NetFirmwareFileName = "ver1305.bin";

    /// <summary>Gets the firmware folder to use, given the Xbox BIOS path.</summary>
    /// <param name="biosPath">The configured Xbox BIOS path, if any.</param>
    /// <returns>An absolute folder path, or an empty string.</returns>
    public string ResolveDirectory(string biosPath)
    {
        if (!string.IsNullOrWhiteSpace(FirmwareDirectory))
        {
            return FirmwareDirectory.Trim();
        }

        return string.IsNullOrWhiteSpace(biosPath)
            ? string.Empty
            : Path.GetDirectoryName(biosPath) ?? string.Empty;
    }

    /// <summary>Creates an independent copy of these settings.</summary>
    /// <returns>The copy.</returns>
    public ChihiroSettings Clone()
    {
        return new ChihiroSettings
        {
            Enabled = Enabled,
            FirmwareDirectory = FirmwareDirectory,
        };
    }

    /// <summary>Clamps and repairs values loaded from disk.</summary>
    public void Normalize()
    {
        FirmwareDirectory = (FirmwareDirectory ?? string.Empty).Trim();
    }
}
