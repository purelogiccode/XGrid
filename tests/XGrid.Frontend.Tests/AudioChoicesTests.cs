using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Core.Xemu;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins the backend list the settings page offers from a core manifest.</summary>
public sealed class AudioChoicesTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "xgrid-audio-tests", Guid.NewGuid().ToString("N"));

    public AudioChoicesTests()
    {
        _ = Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Keys_UsesTheAudioDriversTheCoreReports()
    {
        File.WriteAllText(
            Path.Combine(_root, CoreCapabilities.FileName),
            """{"version":"0.8.139","renderers":["OPENGL"],"audioDrivers":["none","sdl","wav"]}""");
        CoreCapabilities? capabilities = CoreCapabilities.ReadFromDirectory(_root);

        IReadOnlyList<string> keys = AudioChoices.Keys(capabilities);

        Assert.Equal(
            [AudioBackends.Auto, AudioBackends.Sdl, AudioBackends.None, AudioBackends.Wav],
            keys);
    }

    [Fact]
    public void Keys_AddsLinuxBackendsWithoutDuplicatingTheNullSink()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(_root, CoreCapabilities.FileName),
            """{"audioDrivers":["pa","alsa","jack","pipewire","oss","sndio","none","wav"]}""");
        CoreCapabilities? capabilities = CoreCapabilities.ReadFromDirectory(_root);

        IReadOnlyList<string> keys = AudioChoices.Keys(capabilities);

        Assert.Equal(AudioBackends.Auto, keys[0]);
        Assert.Contains(AudioBackends.PulseAudio, keys, StringComparer.Ordinal);
        Assert.Contains(AudioBackends.Alsa, keys, StringComparer.Ordinal);
        Assert.Contains(AudioBackends.Jack, keys, StringComparer.Ordinal);
        Assert.Contains(AudioBackends.PipeWire, keys, StringComparer.Ordinal);
        Assert.Contains(AudioBackends.Oss, keys, StringComparer.Ordinal);
        Assert.Contains(AudioBackends.Sndio, keys, StringComparer.Ordinal);
        Assert.Single(keys, key => string.Equals(key, AudioBackends.None, StringComparison.Ordinal));
        Assert.Single(keys, key => string.Equals(key, AudioBackends.Wav, StringComparison.Ordinal));
    }

    [Fact]
    public void Labels_NameTheNullSinkAndTheCommonBackends()
    {
        Assert.Equal("Auto (recommended)", AudioChoices.Label(AudioBackends.Auto));
        Assert.Equal("DirectSound", AudioChoices.Label(AudioBackends.DirectSound));
        Assert.Equal("PulseAudio", AudioChoices.Label(AudioBackends.PulseAudio));
        Assert.Equal("Null (no output)", AudioChoices.Label(AudioBackends.None));
        Assert.Equal("WAV capture", AudioChoices.Label(AudioBackends.Wav));
    }

    [Theory]
    [InlineData(AudioBackends.DirectSound, "device")]
    [InlineData(AudioBackends.PulseAudio, "out.name")]
    [InlineData(AudioBackends.PipeWire, "out.name")]
    [InlineData(AudioBackends.Alsa, "out.dev")]
    [InlineData(AudioBackends.Oss, "out.dev")]
    [InlineData(AudioBackends.Sndio, "dev")]
    [InlineData(AudioBackends.Jack, "out.server-name")]
    [InlineData(AudioBackends.None, null)]
    [InlineData(AudioBackends.Wav, null)]
    public void DeviceOptionKey_NamesTheBackendDeviceOption(string backend, string? expected)
    {
        Assert.Equal(expected, AudioBackends.DeviceOptionKey(backend));
    }

    [Fact]
    public void DeviceHint_ExplainsTheNullSink()
    {
        Assert.Contains("discards", AudioChoices.DeviceHint(AudioBackends.None), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DirectSound", AudioChoices.DeviceHint(AudioBackends.DirectSound), StringComparison.Ordinal);
        Assert.Contains("sink", AudioChoices.DeviceHint(AudioBackends.PulseAudio), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
