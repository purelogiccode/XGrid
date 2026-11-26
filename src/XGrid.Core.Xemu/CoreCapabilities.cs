using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace XGrid.Core.Xemu;

/// <summary>
/// What one emulator build can do, read from the <c>xgrid-core.json</c>
/// manifest that <c>scripts/build-native-xemu.ps1</c> writes beside the core.
/// The shell offers only the renderers a build can actually start; a core
/// without a manifest reports nothing (<c>null</c>), and the shell then leaves
/// the configured choice alone.
/// </summary>
public sealed class CoreCapabilities
{
    /// <summary>File name of the manifest beside the core executable.</summary>
    public const string FileName = "xgrid-core.json";

    /// <summary>Renderer key the manifest and the shell share for OpenGL.</summary>
    public const string RendererOpenGl = "OPENGL";

    /// <summary>Renderer key for the Vulkan backend.</summary>
    public const string RendererVulkan = "VULKAN";

    /// <summary>Renderer key for the Direct3D 11 backend.</summary>
    public const string RendererDirect3D11 = "D3D11";

    /// <summary>Renderer key for the headless null backend.</summary>
    public const string RendererNull = "NULL";

    private CoreCapabilities(string version, IReadOnlyList<string> renderers, IReadOnlyList<string> audioDrivers)
    {
        Version = version;
        Renderers = renderers;
        AudioDrivers = audioDrivers;
        SupportsVulkan = renderers.Contains(RendererVulkan, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the emulator version the manifest describes.</summary>
    public string Version { get; }

    /// <summary>Gets the renderer keys the build compiled in.</summary>
    public IReadOnlyList<string> Renderers { get; }

    /// <summary>Gets the audio driver keys the build registered.</summary>
    public IReadOnlyList<string> AudioDrivers { get; }

    /// <summary>Gets a value indicating whether the build has the Vulkan renderer.</summary>
    public bool SupportsVulkan { get; }

    /// <summary>Returns whether the build lists one renderer key.</summary>
    /// <param name="renderer">Renderer key, such as <c>VULKAN</c>.</param>
    /// <returns><see langword="true"/> when the key is listed.</returns>
    public bool Has(string renderer)
    {
        return Renderers.Contains(renderer, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the manifest beside a core executable.
    /// </summary>
    /// <param name="corePath">Core executable path, or null.</param>
    /// <returns>The capabilities, or null when there is no readable manifest.</returns>
    public static CoreCapabilities? Read(string? corePath)
    {
        if (string.IsNullOrWhiteSpace(corePath))
        {
            return null;
        }

        return ReadFromDirectory(Path.GetDirectoryName(Path.GetFullPath(corePath)));
    }

    /// <summary>
    /// Reads the manifest from a directory.
    /// </summary>
    /// <param name="directory">Directory that holds the core, or null.</param>
    /// <returns>The capabilities, or null when there is no readable manifest.</returns>
    public static CoreCapabilities? ReadFromDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string version = root.TryGetProperty("version", out JsonElement versionElement)
                && versionElement.ValueKind == JsonValueKind.String
                    ? versionElement.GetString() ?? string.Empty
                    : string.Empty;

            var renderers = new List<string>();
            if (root.TryGetProperty("renderers", out JsonElement rendererElement)
                && rendererElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in rendererElement.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } key)
                    {
                        renderers.Add(key.ToUpperInvariant());
                    }
                }
            }

            var audioDrivers = new List<string>();
            if (root.TryGetProperty("audioDrivers", out JsonElement audioElement)
                && audioElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in audioElement.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } key)
                    {
                        audioDrivers.Add(key.ToLowerInvariant());
                    }
                }
            }

            return new CoreCapabilities(version, renderers, audioDrivers);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A malformed manifest must never stop the shell from starting; it
            // means "this build does not report anything", the same as no file.
            Log.Debug(exception, "The core manifest could not be read");
            return null;
        }
    }
}
