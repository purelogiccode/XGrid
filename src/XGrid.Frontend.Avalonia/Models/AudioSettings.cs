using System;
using XGrid.Core.Xemu;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>Audio options written into the xemu core configuration.</summary>
public sealed class AudioSettings
{
    /// <summary>Backend key: let the core choose.</summary>
    public const string BackendAuto = AudioBackends.Auto;

    /// <summary>Backend key: DirectSound (Windows builds).</summary>
    public const string BackendDirectSound = AudioBackends.DirectSound;

    /// <summary>Backend key: no host audio output.</summary>
    public const string BackendNone = AudioBackends.None;

    /// <summary>Backend key: capture to a WAV file in the core working directory.</summary>
    public const string BackendWav = AudioBackends.Wav;

    /// <summary>Gets or sets a value indicating whether the guest DSP microcode runs.</summary>
    public bool UseDsp { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the DSP JIT is used.</summary>
    public bool UseDspJit { get; set; }

    /// <summary>Gets or sets a value indicating whether HRTF filtering is applied.</summary>
    public bool Hrtf { get; set; } = true;

    /// <summary>Gets or sets the output volume limit (0-1).</summary>
    public double VolumeLimit { get; set; } = 1.0;

    /// <summary>Gets or sets the voice-processor worker count (0 = auto).</summary>
    public int VoiceWorkers { get; set; }

    /// <summary>Gets or sets the host audio backend key.</summary>
    public string Backend { get; set; } = BackendAuto;

    /// <summary>
    /// Gets or sets the backend-specific output device. DirectSound uses a
    /// device GUID; PulseAudio, PipeWire, ALSA, OSS, sndio and JACK use the
    /// backend's own device or server name. Empty follows the system default.
    /// </summary>
    public string Device { get; set; } = string.Empty;

    /// <summary>Creates an independent copy of these settings.</summary>
    public AudioSettings Clone()
    {
        return new AudioSettings
        {
            UseDsp = UseDsp,
            UseDspJit = UseDspJit,
            Hrtf = Hrtf,
            VolumeLimit = VolumeLimit,
            VoiceWorkers = VoiceWorkers,
            Backend = Backend,
            Device = Device,
        };
    }

    /// <summary>Repairs values loaded from disk.</summary>
    public void Normalize()
    {
        string backend = NullSafe.Text(Backend).Trim().ToLowerInvariant();
        Backend = AudioBackends.IsKnown(backend) && AudioBackends.IsAvailableOnThisHost(backend)
            ? backend
            : BackendAuto;

        string device = NullSafe.Text(Device).Trim();
        // A comma starts the next QEMU audio option and a newline would split
        // the argument; neither can appear in a device id.
        Device = device.AsSpan().IndexOfAny(",\r\n") >= 0 ? string.Empty : device;
        VolumeLimit = Math.Clamp(VolumeLimit, 0.0, 1.0);
        VoiceWorkers = Math.Clamp(VoiceWorkers, 0, 16);
    }
}
