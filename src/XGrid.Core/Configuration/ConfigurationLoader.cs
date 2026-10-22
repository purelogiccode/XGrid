using System;
using System.IO;
using System.Text.Json;
using XGrid.Core.Emulation;

namespace XGrid.Core.Configuration;

/// <summary>Loads and saves <see cref="EmulatorConfiguration"/> as JSON (AOT-safe).</summary>
public static class ConfigurationLoader
{
    /// <summary>Parses configuration from a JSON string.</summary>
    /// <exception cref="ConfigurationException">The JSON is malformed or empty.</exception>
    public static EmulatorConfiguration Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            EmulatorConfiguration? configuration =
                JsonSerializer.Deserialize(json, EmulatorConfigurationJsonContext.Default.EmulatorConfiguration);

            return configuration is null
                ? throw new ConfigurationException("Configuration JSON did not contain an object.")
                : configuration.Validate();
        }
        catch (JsonException exception)
        {
            throw new ConfigurationException("Configuration JSON is invalid.", exception);
        }
    }

    /// <summary>Loads configuration from a file.</summary>
    public static EmulatorConfiguration LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    /// <summary>Serializes configuration to a JSON string.</summary>
    public static string Save(EmulatorConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return JsonSerializer.Serialize(
            configuration,
            EmulatorConfigurationJsonContext.Default.EmulatorConfiguration);
    }

    /// <summary>Writes configuration to a file.</summary>
    public static void SaveFile(string path, EmulatorConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configuration);
        File.WriteAllText(path, Save(configuration));
    }
}
