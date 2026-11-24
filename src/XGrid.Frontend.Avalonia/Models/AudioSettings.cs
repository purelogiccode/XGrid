using System;

namespace XGrid.Frontend.Avalonia.Models;

/// <summary>Audio options written into the xemu core configuration.</summary>
public sealed class AudioSettings
{
    /// <summary>Backend key: let the core choose.</summary>
    public const string BackendAuto = "auto";

    /// <summary>Backend key: DirectSound (Windows builds).</summary>
    public const string BackendDirectSound = "dsound";

    /// <summary>Backend key: no host audio output.</summary>
    public const string BackendNone = "none";

    /// <summary>Backend key: capture to a WAV file in the core working directory.</summary>
    public const string BackendWav = "wav";

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
        };
    }

    /// <summary>Repairs values loaded from disk.</summary>
    public void Normalize()
    {
        Backend = NullSafe.Text(Backend).ToLowerInvariant() switch
        {
            BackendDirectSound when OperatingSystem.IsWindows() => BackendDirectSound,
            BackendNone => BackendNone,
            BackendWav => BackendWav,
            _ => BackendAuto,
        };
        VolumeLimit = Math.Clamp(VolumeLimit, 0.0, 1.0);
        VoiceWorkers = Math.Clamp(VoiceWorkers, 0, 16);
    }
}
