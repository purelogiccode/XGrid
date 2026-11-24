using System;
using System.IO;
using System.Text.Json;
using Serilog;
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
        : this(ResolveDirectory())
    {
    }

    /// <summary>Initializes a store rooted at an explicit folder (tests and tools).</summary>
    /// <param name="directoryPath">Folder that holds <c>settings.json</c>.</param>
    internal SettingsStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        DirectoryPath = directoryPath;
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
    private string DirectoryPath { get; }

    /// <summary>Gets the settings file path.</summary>
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");

    /// <summary>
    /// Gets a value indicating whether <c>settings.json</c> existed when
    /// <see cref="Load"/> ran. A missing file is a first run.
    /// </summary>
    public bool ExistedAtLoad { get; private set; }

    /// <summary>Loads settings, returning defaults when the file is missing or unreadable.</summary>
    public EmulatorSettings Load()
    {
        ExistedAtLoad = File.Exists(FilePath);
        if (!ExistedAtLoad)
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
        catch (JsonException exception)
        {
            Log.Warning(exception, "The settings file is not valid JSON; defaults are used: {Path}", FilePath);
            BackupCorruptFile();
            return new EmulatorSettings();
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "The settings file could not be read; defaults are used: {Path}", FilePath);
            return new EmulatorSettings();
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "The settings file is not accessible; defaults are used: {Path}", FilePath);
            return new EmulatorSettings();
        }
    }

    /// <summary>Writes settings to disk, creating the folder when needed.</summary>
    public void Save(EmulatorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        Directory.CreateDirectory(DirectoryPath);
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));

        // Replace the live file only after the new body is on disk, so a crash
        // or a full disk cannot truncate the user's settings.
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private void BackupCorruptFile()
    {
        try
        {
            File.Move(FilePath, FilePath + ".bak", overwrite: true);
        }
        catch (IOException exception)
        {
            Log.Debug(exception, "The corrupt settings file could not be backed up");
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Debug(exception, "The corrupt settings file could not be backed up");
        }
    }
}
