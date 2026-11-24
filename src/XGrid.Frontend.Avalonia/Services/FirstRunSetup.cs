using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// The mapping between the first-run wizard's choices and the persisted
/// settings. Kept free of UI types so the rules are testable: a path that is
/// entered must exist, blank values keep the current setting, and a library
/// folder is added once.
/// </summary>
public static class FirstRunSetup
{
    /// <summary>Returns the first blocking problem, or null when the choices are usable.</summary>
    /// <param name="biosPath">Chosen BIOS image, or blank to configure later.</param>
    /// <param name="eepromPath">Chosen EEPROM image, or blank to configure later.</param>
    /// <param name="hddPath">Chosen hard-disk image, or blank to configure later.</param>
    public static string? Validate(string? biosPath, string? eepromPath, string? hddPath)
    {
        return Check(biosPath, "The BIOS image was not found.")
               ?? Check(eepromPath, "The EEPROM image was not found.")
               ?? Check(hddPath, "The hard-disk image was not found.");
    }

    /// <summary>Writes the wizard's choices into the settings.</summary>
    /// <param name="settings">Settings to update.</param>
    /// <param name="biosPath">Chosen BIOS image, or blank to keep the current one.</param>
    /// <param name="eepromPath">Chosen EEPROM image, or blank to keep the current one.</param>
    /// <param name="hddPath">Chosen hard-disk image, or blank to keep the current one.</param>
    /// <param name="libraryFolder">Folder to add to the Library, or blank.</param>
    public static void Apply(
        EmulatorSettings settings,
        string? biosPath,
        string? eepromPath,
        string? hddPath,
        string? libraryFolder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!string.IsNullOrWhiteSpace(biosPath))
        {
            settings.BiosPath = biosPath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(eepromPath))
        {
            settings.EepromPath = eepromPath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(hddPath))
        {
            settings.HddPath = hddPath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(libraryFolder))
        {
            settings.LibraryFolders = NullSafe.Value(settings.LibraryFolders, new List<string>());
            string folder = libraryFolder.Trim();
            if (!settings.LibraryFolders.Exists(
                    existing => string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase)))
            {
                settings.LibraryFolders.Add(folder);
            }
        }
    }

    private static string? Check(string? path, string problem)
    {
        return string.IsNullOrWhiteSpace(path) || File.Exists(path.Trim()) ? null : problem;
    }
}
