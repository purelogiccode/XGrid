using System;
using System.Collections.Generic;

namespace XGrid.Core.Xemu;

/// <summary>
/// The QEMU audio driver keys the shell understands and the key that selects a
/// device inside each driver's options. The keys are the same ones the core
/// prints from <c>-audio-drivers</c>.
/// </summary>
public static class AudioBackends
{
    /// <summary>Let the core use its first compiled driver.</summary>
    public const string Auto = "auto";

    /// <summary>QEMU's null sink: discard host audio.</summary>
    public const string None = "none";

    /// <summary>Capture to a WAV file.</summary>
    public const string Wav = "wav";

    /// <summary>DirectSound (Windows).</summary>
    public const string DirectSound = "dsound";

    /// <summary>Core Audio (macOS).</summary>
    public const string CoreAudio = "coreaudio";

    /// <summary>PulseAudio.</summary>
    public const string PulseAudio = "pa";

    /// <summary>ALSA.</summary>
    public const string Alsa = "alsa";

    /// <summary>JACK.</summary>
    public const string Jack = "jack";

    /// <summary>PipeWire.</summary>
    public const string PipeWire = "pipewire";

    /// <summary>OSS.</summary>
    public const string Oss = "oss";

    /// <summary>sndio.</summary>
    public const string Sndio = "sndio";

    /// <summary>SDL audio.</summary>
    public const string Sdl = "sdl";

    private static readonly string[] KnownKeys =
    [
        Auto,
        None,
        Wav,
        DirectSound,
        CoreAudio,
        PulseAudio,
        Alsa,
        Jack,
        PipeWire,
        Oss,
        Sndio,
        Sdl,
    ];

    /// <summary>Gets every backend key the shell can name.</summary>
    public static IReadOnlyList<string> Known => KnownKeys;

    /// <summary>Returns true when <paramref name="key"/> is a known backend key.</summary>
    public static bool IsKnown(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        foreach (string known in KnownKeys)
        {
            if (string.Equals(known, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when the backend can run on this host (a Windows settings
    /// file moved to Linux normalizes DirectSound away, for example).
    /// </summary>
    public static bool IsAvailableOnThisHost(string key)
    {
        return key.ToLowerInvariant() switch
        {
            DirectSound => OperatingSystem.IsWindows(),
            CoreAudio => OperatingSystem.IsMacOS(),
            PulseAudio or Alsa or Jack or PipeWire or Oss or Sndio => OperatingSystem.IsLinux()
                || OperatingSystem.IsFreeBSD(),
            _ => true,
        };
    }

    /// <summary>
    /// Returns the QEMU key-value option that selects this backend's device, or
    /// null when the backend only uses the system default.
    /// </summary>
    public static string? DeviceOptionKey(string? backend)
    {
        return backend?.ToLowerInvariant() switch
        {
            DirectSound => "device",
            PulseAudio => "out.name",
            PipeWire => "out.name",
            Alsa => "out.dev",
            Oss => "out.dev",
            Sndio => "dev",
            Jack => "out.server-name",
            _ => null,
        };
    }

    /// <summary>Returns true when a device string applies to this backend.</summary>
    public static bool SupportsDevice(string? backend)
    {
        return DeviceOptionKey(backend) is not null;
    }
}
