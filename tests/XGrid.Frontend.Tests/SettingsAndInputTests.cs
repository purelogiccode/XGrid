using System;
using System.Collections.Generic;
using Avalonia.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Input;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins the keyboard-name parser used by settings and key capture.</summary>
public sealed class KeyboardKeysTests
{
    [Theory]
    [InlineData("A", Key.A)]
    [InlineData("a", Key.A)]
    [InlineData(" F1 ", Key.F1)]
    [InlineData("Backspace", Key.Back)]
    [InlineData("back", Key.Back)]
    [InlineData("D0", Key.D0)]
    public void TryParse_AcceptsStoredNames(string text, Key expected)
    {
        Assert.True(KeyboardKeys.TryParse(text, out Key key));
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-key")]
    [InlineData("None")]
    [InlineData("9999")]
    public void TryParse_RejectsMissingOrUnknownNames(string? text)
    {
        Assert.False(KeyboardKeys.TryParse(text, out Key key));
        Assert.Equal(Key.None, key);
    }

    [Fact]
    public void ParseList_TrimsDropsUnknownAndDeduplicates()
    {
        IReadOnlyList<Key> keys = KeyboardKeys.ParseList("A, b, A, ,not-a-key, Space");

        Assert.Equal([Key.A, Key.B, Key.Space], keys);
    }

    [Fact]
    public void ParseList_ReturnsEmptyForBlankText()
    {
        Assert.Empty(KeyboardKeys.ParseList(null));
        Assert.Empty(KeyboardKeys.ParseList(" "));
    }

    [Fact]
    public void NormalizeList_RemovesReservedKeys()
    {
        Assert.Equal("A,B", KeyboardKeys.NormalizeList("A, Space, b, A"));
    }

    [Fact]
    public void NormalizeList_KeepsTheCanonicalBackspaceName()
    {
        Assert.Equal("Backspace", KeyboardKeys.NormalizeList("Back"));
    }

    [Fact]
    public void Describe_FormatsMultipleKeysOrUnbound()
    {
        Assert.Equal("B, A", KeyboardKeys.Describe("b,A"));
        Assert.Equal("Unbound", KeyboardKeys.Describe(""));
        Assert.Equal("Unbound", KeyboardKeys.Describe("Space"));
    }

    [Fact]
    public void Reserved_ContainsOnlyTheShellShortcut()
    {
        Assert.True(KeyboardKeys.IsReserved(Key.Space));
        Assert.False(KeyboardKeys.IsReserved(Key.Enter));
        Assert.Single(KeyboardKeys.Reserved);
    }

    [Fact]
    public void Format_UsesTheFriendlyBackspaceName()
    {
        Assert.Equal("Backspace", KeyboardKeys.Format(Key.Back));
        Assert.Equal("Return", KeyboardKeys.Format(Key.Enter));
    }
}

/// <summary>Pins how persisted input settings project onto the runtime mapping.</summary>
public sealed class InputMappingTests
{
    [Fact]
    public void Create_UsesTheStoredGamepadBindingsAndOptions()
    {
        var settings = new InputSettings
        {
            Gamepad = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(PadAction.A)] = nameof(GamepadSource.B),
            },
            LeftDeadZonePercent = 10,
            RightDeadZonePercent = 20,
            TriggerThresholdPercent = 30,
            InvertLeftY = true,
            InvertRightY = true,
            SwapSticks = true,
        };

        GamepadMappingOptions options = InputMapping.Create(settings);

        Assert.Equal(GamepadSource.B, options.Bindings[PadAction.A]);
        Assert.Equal(GamepadSource.B, options.Bindings[PadAction.B]);
        Assert.Equal(10, options.LeftDeadZonePercent);
        Assert.Equal(20, options.RightDeadZonePercent);
        Assert.Equal(30, options.TriggerThresholdPercent);
        Assert.True(options.InvertLeftY);
        Assert.True(options.InvertRightY);
        Assert.True(options.SwapSticks);
    }

    [Fact]
    public void Create_FallsBackForUnknownOrMissingSources()
    {
        var settings = new InputSettings
        {
            Gamepad = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(PadAction.A)] = "NotASource",
            },
        };

        GamepadMappingOptions options = InputMapping.Create(settings);

        Assert.Equal(GamepadMappingOptions.DefaultBindings[PadAction.A], options.Bindings[PadAction.A]);
        Assert.Equal(GamepadMappingOptions.DefaultBindings[PadAction.B], options.Bindings[PadAction.B]);
    }

    [Fact]
    public void TryMapKey_ReturnsTheFirstActionForAKey()
    {
        var settings = new InputSettings
        {
            Keyboard = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(PadAction.A)] = "Q",
                [nameof(PadAction.B)] = "Q",
            },
        };

        Assert.True(InputMapping.TryMapKey(settings, Key.Q, out PadAction action));
        Assert.Equal(PadAction.A, action);
        Assert.False(InputMapping.TryMapKey(settings, Key.Space, out _));
        Assert.False(InputMapping.TryMapKey(settings, Key.Z, out _));
    }

    [Fact]
    public void MergeKeyboard_PressesEveryBoundAction()
    {
        var settings = new InputSettings
        {
            Keyboard = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(PadAction.A)] = "Q",
                [nameof(PadAction.LeftTrigger)] = "T",
                [nameof(PadAction.DpadUp)] = "Up",
            },
        };
        var neutral = new HostPad(0, new byte[8], new short[4]);

        HostPad merged = InputMapping.MergeKeyboard(neutral, settings, [Key.Q, Key.T, Key.Up]);

        Assert.Equal(PadActions.DigitalMask(PadAction.DpadUp), (ushort)(merged.Buttons & PadActions.DigitalMask(PadAction.DpadUp)));
        Assert.Equal(255, merged.Analog[PadActions.AnalogIndex(PadAction.A)]);
        Assert.Equal(255, merged.Analog[PadActions.AnalogIndex(PadAction.LeftTrigger)]);
    }

    [Fact]
    public void MergeKeyboard_DoesNotMutateTheInputReport()
    {
        var settings = new InputSettings();
        byte[] analog = [1, 2, 3, 4, 5, 6, 7, 8];
        short[] sticks = [10, 20, 30, 40];
        var neutral = new HostPad(0, analog, sticks);

        HostPad merged = InputMapping.MergeKeyboard(neutral, settings, [Key.A]);

        Assert.NotSame(analog, merged.Analog);
        Assert.NotSame(sticks, merged.Sticks);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], analog);
        Assert.Equal([10, 20, 30, 40], sticks);
    }

    [Fact]
    public void MergeKeyboard_IgnoresReservedAndUnknownKeys()
    {
        var settings = new InputSettings();
        var neutral = new HostPad(0, new byte[8], new short[4]);

        HostPad merged = InputMapping.MergeKeyboard(neutral, settings, [Key.Space, Key.F24]);

        Assert.Equal(0, merged.Buttons);
        Assert.All(merged.Analog, value => Assert.Equal(0, value));
    }

    [Fact]
    public void PublicHelpers_RejectMissingSettings()
    {
        var settings = new InputSettings();
        var pad = new HostPad(0, new byte[8], new short[4]);

        Assert.Throws<ArgumentNullException>(() => InputMapping.Create(null!));
        Assert.Throws<ArgumentNullException>(() => InputMapping.TryMapKey(null!, Key.A, out _));
        Assert.Throws<ArgumentNullException>(() => InputMapping.MergeKeyboard(pad, null!, [Key.A]));
        Assert.Throws<ArgumentNullException>(
            () => InputMapping.MergeKeyboard(pad, settings, null!));
    }
}

/// <summary>Pins the graphics normalization used for settings loaded from disk.</summary>
public sealed class GraphicsSettingsTests
{
    [Fact]
    public void DefaultsTargetOpenGlAtNativeSurface()
    {
        var settings = new GraphicsSettings();

        Assert.Equal(GraphicsSettings.RendererOpenGl, settings.Renderer);
        Assert.Equal(1, settings.SurfaceScale);
        Assert.Equal(GraphicsSettings.FilterLinear, settings.Filtering);
        Assert.Equal("1280x960", settings.StartupSize);
        Assert.Equal("auto", settings.AspectRatio);
        Assert.Equal("scale", settings.Fit);
        Assert.Equal("hdtv", settings.AvPack);
    }

    [Theory]
    [InlineData("vulkan", GraphicsSettings.RendererVulkan)]
    [InlineData("NULL", GraphicsSettings.RendererNull)]
    [InlineData("opengl", GraphicsSettings.RendererOpenGl)]
    [InlineData("d3d11", GraphicsSettings.RendererDirect3D11)]
    [InlineData("direct3d", GraphicsSettings.RendererOpenGl)]
    [InlineData(null, GraphicsSettings.RendererOpenGl)]
    public void Normalize_RepairsTheRenderer(string? value, string expected)
    {
        var settings = new GraphicsSettings { Renderer = value! };

        settings.Normalize();

        Assert.Equal(expected, settings.Renderer);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(9, 8)]
    public void Normalize_ClampsTheSurfaceScale(int value, int expected)
    {
        var settings = new GraphicsSettings { SurfaceScale = value };

        settings.Normalize();

        Assert.Equal(expected, settings.SurfaceScale);
    }

    [Theory]
    [InlineData("nearest", "nearest")]
    [InlineData("NEAREST", "nearest")]
    [InlineData("bilinear", "linear")]
    [InlineData(null, "linear")]
    public void Normalize_RepairsTheTextureFiltering(string? value, string expected)
    {
        var settings = new GraphicsSettings { Filtering = value! };

        settings.Normalize();

        Assert.Equal(expected, settings.Filtering);
    }

    [Theory]
    [InlineData("native", "native")]
    [InlineData("4x3", "4x3")]
    [InlineData("16:9", "auto")]
    [InlineData("wide", "auto")]
    [InlineData(null, "auto")]
    public void Normalize_RepairsTheAspectRatio(string? value, string expected)
    {
        var settings = new GraphicsSettings { AspectRatio = value! };

        settings.Normalize();

        Assert.Equal(expected, settings.AspectRatio);
    }

    [Theory]
    [InlineData("center", "center")]
    [InlineData("stretch", "stretch")]
    [InlineData("scale", "scale")]
    [InlineData("fill", "scale")]
    public void Normalize_RepairsTheFit(string? value, string expected)
    {
        var settings = new GraphicsSettings { Fit = value! };

        settings.Normalize();

        Assert.Equal(expected, settings.Fit);
    }

    [Fact]
    public void Normalize_RepairsTheStartupSizeAndAvPack()
    {
        var settings = new GraphicsSettings
        {
            StartupSize = "1024x768",
            AvPack = "component",
        };

        settings.Normalize();

        Assert.Equal("1280x960", settings.StartupSize);
        Assert.Equal("hdtv", settings.AvPack);
    }

    [Fact]
    public void Normalize_AcceptsEveryPublishedStartupSizeAndAvPack()
    {
        foreach (string size in GraphicsSettings.StartupSizes)
        {
            var settings = new GraphicsSettings { StartupSize = size };
            settings.Normalize();
            Assert.Equal(size, settings.StartupSize);
        }

        foreach (string pack in GraphicsSettings.AvPacks)
        {
            var settings = new GraphicsSettings { AvPack = pack };
            settings.Normalize();
            Assert.Equal(pack, settings.AvPack);
        }
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var settings = new GraphicsSettings { SurfaceScale = 3, VSync = true };
        GraphicsSettings copy = settings.Clone();
        copy.SurfaceScale = 7;

        Assert.Equal(3, settings.SurfaceScale);
        Assert.True(copy.VSync);
    }
}

/// <summary>Pins the audio backend normalization.</summary>
public sealed class AudioSettingsTests
{
    [Fact]
    public void DefaultsUseAutoBackendAndHrtf()
    {
        var settings = new AudioSettings();

        Assert.Equal(AudioSettings.BackendAuto, settings.Backend);
        Assert.True(settings.UseDsp);
        Assert.True(settings.Hrtf);
        Assert.Equal(1.0, settings.VolumeLimit);
        Assert.Equal(0, settings.VoiceWorkers);
    }

    [Theory]
    [InlineData("none", AudioSettings.BackendNone)]
    [InlineData("WAV", AudioSettings.BackendWav)]
    [InlineData("dsound", AudioSettings.BackendAuto)]
    [InlineData("wasapi", AudioSettings.BackendAuto)]
    [InlineData(null, AudioSettings.BackendAuto)]
    public void Normalize_RepairsTheBackend(string? value, string expected)
    {
        var settings = new AudioSettings { Backend = value! };

        settings.Normalize();

        if (OperatingSystem.IsWindows()
            && string.Equals(expected, AudioSettings.BackendAuto, StringComparison.Ordinal)
            && string.Equals(value, "dsound", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Equal(AudioSettings.BackendDirectSound, settings.Backend);
        }
        else
        {
            Assert.Equal(expected, settings.Backend);
        }
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(0.35, 0.35)]
    public void Normalize_ClampsTheVolume(double value, double expected)
    {
        var settings = new AudioSettings { VolumeLimit = value };

        settings.Normalize();

        Assert.Equal(expected, settings.VolumeLimit);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(17, 16)]
    [InlineData(4, 4)]
    public void Normalize_ClampsTheVoiceWorkers(int value, int expected)
    {
        var settings = new AudioSettings { VoiceWorkers = value };

        settings.Normalize();

        Assert.Equal(expected, settings.VoiceWorkers);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var settings = new AudioSettings { UseDspJit = true, VoiceWorkers = 4 };
        AudioSettings copy = settings.Clone();
        copy.VoiceWorkers = 8;

        Assert.Equal(4, settings.VoiceWorkers);
        Assert.True(copy.UseDspJit);
    }
}
