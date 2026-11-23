using System;
using System.IO;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Persists the 24C02 EEPROM image between runs in the frontend's data folder.
/// </summary>
/// <remarks>
/// The console keeps its user settings (video mode, language, region, audio
/// flags) in this 256-byte serial EEPROM and the guest rewrites it through the
/// SMBus at runtime. The configured firmware image is treated as read-only
/// seed data: the first run copies it into <c>eeprom.bin</c> here, later runs
/// load that copy, and "Reset EEPROM state" deletes it so the next boot re-seeds
/// from the configured image.
/// </remarks>
public sealed class EepromStateStore
{
    /// <summary>The image size persisted by the device.</summary>
    public const int ImageSize = 256;

    /// <summary>Creates a store rooted at an explicit folder (tests use a temp folder).</summary>
    /// <param name="directoryPath">Folder that holds the persisted image.</param>
    public EepromStateStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        DirectoryPath = directoryPath;
    }

    /// <summary>Gets the folder that holds the persisted image.</summary>
    private string DirectoryPath { get; }

    /// <summary>Gets the persisted image path.</summary>
    public string FilePath => Path.Combine(DirectoryPath, "eeprom.bin");

    /// <summary>Returns true when a persisted image exists.</summary>
    public bool Exists => File.Exists(FilePath);

    /// <summary>Loads the persisted image, or returns false when it is absent or unusable.</summary>
    /// <param name="image">Receives exactly <see cref="ImageSize"/> bytes.</param>
    public bool TryLoad(out byte[] image)
    {
        image = [];
        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            byte[] raw = File.ReadAllBytes(FilePath);
            if (raw.Length < ImageSize)
            {
                return false;
            }

            image = new byte[ImageSize];
            Array.Copy(raw, image, ImageSize);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Writes the live image, creating the folder when needed.</summary>
    /// <param name="image">Buffer of at least <see cref="ImageSize"/> bytes.</param>
    public bool TrySave(ReadOnlySpan<byte> image)
    {
        if (image.Length < ImageSize)
        {
            throw new ArgumentException("the image must be 256 bytes", nameof(image));
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllBytes(FilePath, image[..ImageSize].ToArray());
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Deletes the persisted image so the next boot re-seeds from the configured firmware image.</summary>
    public bool TryReset()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
