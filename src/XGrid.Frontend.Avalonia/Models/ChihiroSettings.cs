using System;
using System.IO;
using XGrid.Core.Xemu;

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

    /// <summary>
    /// Gets or sets the linked-cabinet setup. The core relays the network
    /// board's Ethernet between XGrid instances running the same game.
    /// </summary>
    public ChihiroLinkSettings Link { get; set; } = new();

    /// <summary>
    /// Gets or sets the cabinet key bindings. Values are SDL scancodes; the
    /// Settings input tab edits them.
    /// </summary>
    public ChihiroJvsSettings Jvs { get; set; } = new();

    /// <summary>
    /// Gets or sets player 2's gun and card bindings for a two-player cabinet.
    /// </summary>
    public ChihiroJvsP2Settings JvsP2 { get; set; } = new();

    /// <summary>
    /// Gets or sets the card-reader files and whether the readers answer.
    /// </summary>
    public ChihiroCardReaderSettings CardReader { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the cabinet is on free play.
    /// Start begins a game without inserting a coin.
    /// </summary>
    public bool FreePlay { get; set; }

    /// <summary>
    /// Gets or sets the cabinet region: <c>auto</c>, <c>jp</c>, <c>us</c> or
    /// <c>ex</c>. A game that stops with Error 05 may need another region.
    /// </summary>
    public string Region { get; set; } = "ex";

    /// <summary>
    /// Gets or sets the media-board DIMM size: <c>auto</c>, <c>128</c>,
    /// <c>256</c>, <c>512</c> or <c>1024</c> MB.
    /// </summary>
    public string DimmSize { get; set; } = "auto";

    /// <summary>
    /// Gets or sets the media-board type: <c>auto</c>, <c>type1</c> or
    /// <c>type3</c>.
    /// </summary>
    public string BoardType { get; set; } = "auto";

    /// <summary>
    /// Gets or sets a value indicating whether the drive board's force
    /// feedback is sent to the first gamepad as rumble.
    /// </summary>
    public bool ForceFeedback { get; set; }

    /// <summary>Gets or sets the force-feedback strength, 0 to 200 percent.</summary>
    public int FfbStrength { get; set; } = 100;

    /// <summary>Gets or sets a value indicating whether the wheel pulls the other way.</summary>
    public bool FfbInvert { get; set; }

    /// <summary>Gets or sets the wheel's damping share, 0 to 100 percent.</summary>
    public int WheelWeight { get; set; } = 40;

    /// <summary>Gets or sets a value indicating whether the wheel centers itself.</summary>
    public bool WheelAutocenter { get; set; } = true;

    /// <summary>Gets or sets the self-centering strength, 0 to 100 percent.</summary>
    public int WheelAutocenterStrength { get; set; } = 50;

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
            Link = Link.Clone(),
            Jvs = Jvs.Clone(),
            JvsP2 = (JvsP2 ?? new ChihiroJvsP2Settings()).Clone(),
            CardReader = (CardReader ?? new ChihiroCardReaderSettings()).Clone(),
            FreePlay = FreePlay,
            Region = Region,
            DimmSize = DimmSize,
            BoardType = BoardType,
            ForceFeedback = ForceFeedback,
            FfbStrength = FfbStrength,
            FfbInvert = FfbInvert,
            WheelWeight = WheelWeight,
            WheelAutocenter = WheelAutocenter,
            WheelAutocenterStrength = WheelAutocenterStrength,
        };
    }

    /// <summary>Clamps and repairs values loaded from disk.</summary>
    public void Normalize()
    {
        FirmwareDirectory = (FirmwareDirectory ?? string.Empty).Trim();
        Link ??= new ChihiroLinkSettings();
        Link.Normalize();
        Jvs ??= new ChihiroJvsSettings();
        Jvs.Normalize();
        JvsP2 ??= new ChihiroJvsP2Settings();
        JvsP2.Normalize();
        CardReader ??= new ChihiroCardReaderSettings();
        CardReader.Normalize();
        Region = Region is "auto" or "jp" or "us" or "ex" ? Region : "ex";
        DimmSize = DimmSize is "128" or "256" or "512" or "1024" or "auto" ? DimmSize : "auto";
        BoardType = BoardType is "type1" or "type3" or "auto" ? BoardType : "auto";
        FfbStrength = Math.Clamp(FfbStrength, 0, 200);
        WheelWeight = Math.Clamp(WheelWeight, 0, 100);
        WheelAutocenterStrength = Math.Clamp(WheelAutocenterStrength, 0, 100);
    }
}

/// <summary>
/// Linked-cabinet settings for titles that support two to four connected
/// cabinets. Cabinet 1 is the host; the others connect to its address.
/// </summary>
public sealed class ChihiroLinkSettings
{
    /// <summary>Gets or sets a value indicating whether the cabinet link runs.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the number of linked cabinets (2 to 4).</summary>
    public int Cabinets { get; set; } = 2;

    /// <summary>Gets or sets this cabinet's number (1 to <see cref="Cabinets"/>).</summary>
    public int Cabinet { get; set; } = 1;

    /// <summary>
    /// Gets or sets the host cabinet's address. Empty on cabinet 1; the other
    /// cabinets use the host's LAN address.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the relay's base TCP port.</summary>
    public int Port { get; set; } = 9100;

    /// <summary>Creates an independent copy of these settings.</summary>
    /// <returns>The copy.</returns>
    public ChihiroLinkSettings Clone()
    {
        return new ChihiroLinkSettings
        {
            Enabled = Enabled,
            Cabinets = Cabinets,
            Cabinet = Cabinet,
            Host = Host,
            Port = Port,
        };
    }

    /// <summary>Clamps and repairs values loaded from disk.</summary>
    public void Normalize()
    {
        Cabinets = Math.Clamp(Cabinets, 2, 4);
        Cabinet = Math.Clamp(Cabinet, 1, Cabinets);
        Host = (Host ?? string.Empty).Trim();
        if (Port is < 1 or > 65535)
        {
            Port = 9100;
        }
    }
}
