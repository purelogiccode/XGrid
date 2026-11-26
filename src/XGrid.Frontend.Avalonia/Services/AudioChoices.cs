using System;
using System.Collections.Generic;
using XGrid.Core.Xemu;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>
/// Turns the core's reported audio drivers into the backend choices the
/// settings page offers. A driver the binary did not register would only make
/// the core exit at launch, so it is not offered.
/// </summary>
public static class AudioChoices
{
    /// <summary>Returns the backend keys to offer, best first.</summary>
    /// <param name="capabilities">Core capabilities, or null when unknown.</param>
    /// <returns>The keys, always ending with the null sink and WAV capture.</returns>
    public static IReadOnlyList<string> Keys(CoreCapabilities? capabilities)
    {
        var keys = new List<string> { AudioBackends.Auto };
        if (capabilities is { AudioDrivers.Count: > 0 })
        {
            foreach (string driver in capabilities.AudioDrivers)
            {
                string key = driver.Trim().ToLowerInvariant();
                if (!AudioBackends.IsKnown(key)
                    || !AudioBackends.IsAvailableOnThisHost(key)
                    || key is AudioBackends.Auto or AudioBackends.None or AudioBackends.Wav
                    || keys.Contains(key))
                {
                    continue;
                }

                keys.Add(key);
            }
        }
        else
        {
            // No manifest: offer the host's likely drivers. The core will
            // reject a driver it was not built with.
            if (OperatingSystem.IsWindows())
            {
                keys.Add(AudioBackends.DirectSound);
            }
            else if (OperatingSystem.IsMacOS())
            {
                keys.Add(AudioBackends.CoreAudio);
            }
            else
            {
                keys.Add(AudioBackends.PulseAudio);
                keys.Add(AudioBackends.Alsa);
                keys.Add(AudioBackends.Jack);
                keys.Add(AudioBackends.PipeWire);
                keys.Add(AudioBackends.Oss);
                keys.Add(AudioBackends.Sndio);
            }
        }

        keys.Add(AudioBackends.None);
        keys.Add(AudioBackends.Wav);
        return keys;
    }

    /// <summary>Returns the label for a backend key.</summary>
    public static string Label(string key)
    {
        return key switch
        {
            AudioBackends.Auto => "Auto (recommended)",
            AudioBackends.DirectSound => "DirectSound",
            AudioBackends.CoreAudio => "Core Audio",
            AudioBackends.PulseAudio => "PulseAudio",
            AudioBackends.Alsa => "ALSA",
            AudioBackends.Jack => "JACK",
            AudioBackends.PipeWire => "PipeWire",
            AudioBackends.Oss => "OSS",
            AudioBackends.Sndio => "sndio",
            AudioBackends.Sdl => "SDL",
            AudioBackends.None => "Null (no output)",
            AudioBackends.Wav => "WAV capture",
            _ => key,
        };
    }

    /// <summary>Returns the hint under the backend row.</summary>
    public static string Hint(CoreCapabilities? capabilities)
    {
        string supported = capabilities is { AudioDrivers.Count: > 0 }
            ? "This emulator supports: "
              + string.Join(", ", LabelList(capabilities.AudioDrivers))
              + "."
            : "This emulator does not report its audio backends; the platform defaults are offered.";
        return supported
               + " Null discards host audio; headless runs use it automatically.";
    }

    /// <summary>Returns the hint under the device row for a backend.</summary>
    public static string DeviceHint(string? backend)
    {
        return backend?.ToLowerInvariant() switch
        {
            AudioBackends.DirectSound => "Choose the DirectSound output device. System default follows Windows.",
            AudioBackends.PulseAudio or AudioBackends.PipeWire =>
                "Optional sink name. Empty follows the system default.",
            AudioBackends.Alsa or AudioBackends.Oss or AudioBackends.Sndio =>
                "Optional device name. Empty follows the system default.",
            AudioBackends.Jack => "Optional JACK server name. Empty uses the default server.",
            AudioBackends.None => "The null sink discards audio and has no output device.",
            AudioBackends.Wav => "WAV capture writes audio.wav in the core working directory.",
            _ => "This backend uses the system default device.",
        };
    }

    private static IEnumerable<string> LabelList(IReadOnlyList<string> drivers)
    {
        foreach (string driver in drivers)
        {
            yield return Label(driver);
        }
    }
}
