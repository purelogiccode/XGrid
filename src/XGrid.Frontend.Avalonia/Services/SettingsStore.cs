using System;
using System.IO;
using System.Text.Json;
using XGrid.Frontend.Avalonia.Models;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Loads and saves <see cref="EmulatorSettings"/> in the per-user app-data folder.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Initializes a store rooted at the platform application-data folder.</summary>
    public SettingsStore()
    {
        DirectoryPath = ResolveDirectory();
    }

    /// <summary>Resolves the per-user data folder (honors <c>XGRID_FRONTEND_DATA_DIR</c>).</summary>
    public static string ResolveDirectory()
    {
        string? overrideDirectory = Environment.GetEnvironmentVariable("XGRID_FRONTEND_DATA_DIR");
        return string.IsNullOrWhiteSpace(overrideDirectory)
            ? DefaultDirectory()
            : overrideDirectory;
    }

    private static string DefaultDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "XGrid");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XGrid");
    }

    /// <summary>Gets the settings folder.</summary>
    public string DirectoryPath { get; }

    /// <summary>Gets the settings file path.</summary>
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");

    /// <summary>Loads settings, returning defaults when the file is missing or unreadable.</summary>
    public EmulatorSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new EmulatorSettings();
        }

        try
        {
            string json = File.ReadAllText(FilePath);
            EmulatorSettings? settings = JsonSerializer.Deserialize<EmulatorSettings>(json, JsonOptions);
            if (settings is null)
            {
                return new EmulatorSettings();
            }

            settings.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            BackupCorruptFile();
            return new EmulatorSettings();
        }
        catch (IOException)
        {
            return new EmulatorSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new EmulatorSettings();
        }
    }

    /// <summary>Writes settings to disk, creating the folder when needed.</summary>
    public void Save(EmulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private void BackupCorruptFile()
    {
        try
        {
            File.Move(FilePath, FilePath + ".bak", overwrite: true);
        }
        catch (IOException)
        {
            // A failed backup must never block startup.
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only profile: start with defaults.
        }
    }
}
