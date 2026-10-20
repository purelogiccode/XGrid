using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>User-facing emulator configuration, persisted as JSON.</summary>
public sealed class EmulatorSettings
{
    /// <summary>Gets or sets the MCPX BIOS image path (required to power on).</summary>
    public string BiosPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional 512-byte MCPX boot ROM path.</summary>
    public string McpxRomPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional 256-byte EEPROM image path.</summary>
    public string EepromPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional hard-disk image path.</summary>
    public string HddPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the disc image inserted at power-on (ISO/XISO or CHD).</summary>
    public string DvdPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the guest RAM size in megabytes (64 retail, 128 debug kit).</summary>
    public int RamMb { get; set; } = 64;

    /// <summary>Gets or sets a value indicating whether the fast-mode JIT engine is enabled.</summary>
    public bool FastMode { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the raw x86-64 block emitter is used.</summary>
    public bool RawBlocks { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether idle-spin fast-forwarding is enabled.</summary>
    public bool SpinSkip { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the decode cache is enabled.</summary>
    public bool DecodeCache { get; set; } = true;

    /// <summary>Gets or sets the JIT build threshold (visits before a block is compiled).</summary>
    public int JitThreshold { get; set; } = 8;

    /// <summary>Gets or sets a value indicating whether the 007 end-of-stream quirk is enabled.</summary>
    public bool EosQuirk { get; set; } = true;

    /// <summary>Gets or sets the NVNET backend ("loopback" or "socket").</summary>
    public string NetworkBackend { get; set; } = "loopback";

    /// <summary>Gets or sets a value indicating whether the emulated network link is up.</summary>
    public bool NetworkLinkUp { get; set; } = true;

    /// <summary>Gets or sets the accent palette key.</summary>
    public string Accent { get; set; } = "Emerald";

    /// <summary>Gets or sets the folders scanned by the game library.</summary>
    public List<string> LibraryFolders { get; set; } = new();

    /// <summary>Gets or sets recently launched disc images (newest first).</summary>
    public List<string> RecentGames { get; set; } = new();

    /// <summary>Creates an independent copy of these settings.</summary>
    public EmulatorSettings Clone()
    {
        return new EmulatorSettings
        {
            BiosPath = BiosPath,
            McpxRomPath = McpxRomPath,
            EepromPath = EepromPath,
            HddPath = HddPath,
            DvdPath = DvdPath,
            RamMb = RamMb,
            FastMode = FastMode,
            RawBlocks = RawBlocks,
            SpinSkip = SpinSkip,
            DecodeCache = DecodeCache,
            JitThreshold = JitThreshold,
            EosQuirk = EosQuirk,
            NetworkBackend = NetworkBackend,
            NetworkLinkUp = NetworkLinkUp,
            Accent = Accent,
            LibraryFolders = new List<string>(LibraryFolders),
            RecentGames = new List<string>(RecentGames),
        };
    }

    /// <summary>Clamps and repairs values loaded from disk.</summary>
    public void Normalize()
    {
        RamMb = RamMb is 128 ? 128 : 64;
        JitThreshold = Math.Clamp(JitThreshold, 1, 4096);
        NetworkBackend = string.Equals(NetworkBackend, "socket", StringComparison.Ordinal)
            ? "socket"
            : "loopback";
        if (string.IsNullOrWhiteSpace(Accent))
        {
            Accent = "Emerald";
        }

        LibraryFolders ??= new List<string>();
        RecentGames ??= new List<string>();
        LibraryFolders.RemoveAll(string.IsNullOrWhiteSpace);
        RecentGames.RemoveAll(string.IsNullOrWhiteSpace);
    }

    /// <summary>Returns true when the required firmware image is present on disk.</summary>
    [JsonIgnore]
    public bool IsBiosReady => !string.IsNullOrWhiteSpace(BiosPath) && File.Exists(BiosPath);

    /// <summary>Returns the number of configured library folders that still exist.</summary>
    [JsonIgnore]
    public int ExistingLibraryFolderCount
    {
        get
        {
            int count = 0;
            foreach (string folder in LibraryFolders)
            {
                if (Directory.Exists(folder))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
