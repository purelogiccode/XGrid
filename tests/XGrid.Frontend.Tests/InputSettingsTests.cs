using System;
using System.Text.Json;
using Avalonia.Input;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Frontend.Avalonia.Services;
using XGrid.Input;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins the persisted keyboard/gamepad configuration and its projection.</summary>
public sealed class InputSettingsTests
{
    [Fact]
    public void Defaults_CoverEveryActionAndKeepTheAlternateAKey()
    {
        var settings = new InputSettings();

        Assert.Equal("A,Space", settings.Keyboard[nameof(PadAction.A)]);
        Assert.Equal("RightShoulder", settings.Gamepad[nameof(PadAction.White)]);
        Assert.Equal("LeftShoulder", settings.Gamepad[nameof(PadAction.Black)]);
        foreach (PadAction action in PadActions.All)
        {
            Assert.True(settings.Keyboard.ContainsKey(action.ToString()));
            Assert.True(settings.Gamepad.ContainsKey(action.ToString()));
        }
    }

    [Fact]
    public void Normalize_DropsUnknownActionsAndRepairsInvalidValues()
    {
        var settings = new InputSettings
        {
            Keyboard = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = "A, not-a-key, Space",
                ["NotAnAction"] = "A",
            },
            Gamepad = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = "NotASource",
            },
            LeftDeadZonePercent = -5,
            RightDeadZonePercent = 500,
            TriggerThresholdPercent = 500,
        };

        settings.Normalize();

        Assert.Equal("A,Space", settings.Keyboard[nameof(PadAction.A)]);
        Assert.False(settings.Keyboard.ContainsKey("NotAnAction"));
        Assert.Equal("A", settings.Gamepad[nameof(PadAction.A)]);
        Assert.Equal(0, settings.LeftDeadZonePercent);
        Assert.Equal(100, settings.RightDeadZonePercent);
        Assert.Equal(100, settings.TriggerThresholdPercent);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var settings = new InputSettings();
        InputSettings copy = settings.Clone();
        copy.Keyboard[nameof(PadAction.A)] = "Q";

        Assert.Equal("A,Space", settings.Keyboard[nameof(PadAction.A)]);
    }

    [Fact]
    public void PlayerGamepads_DefaultToTheMatchingSlotAndCloneIndependently()
    {
        var settings = new InputSettings();
        Assert.Equal([0, 1, 2, 3], settings.PlayerGamepads);

        InputSettings copy = settings.Clone();
        copy.PlayerGamepads[0] = -1;

        Assert.Equal(0, settings.PlayerGamepads[0]);
        Assert.Equal(-1, copy.PlayerGamepads[0]);
    }

    [Fact]
    public void Normalize_RepairsThePlayerAssignments()
    {
        var settings = new InputSettings { PlayerGamepads = [7, -2, 2, 3] };

        settings.Normalize();

        Assert.Equal([-1, -1, 2, 3], settings.PlayerGamepads);
    }

    [Fact]
    public void Normalize_FillsAMissingPlayerAssignment()
    {
        var settings = new InputSettings { PlayerGamepads = [1] };

        settings.Normalize();

        Assert.Equal([1, 1, 2, 3], settings.PlayerGamepads);
    }

    [Fact]
    public void KeyboardKeys_ParseFormatAndAliasBackspace()
    {
        Assert.True(KeyboardKeys.TryParse("Backspace", out Key back));
        Assert.Equal(Key.Back, back);
        Assert.Equal("Backspace", KeyboardKeys.Format(Key.Back));
        Assert.Equal("A,Space", KeyboardKeys.NormalizeList("A, Space, A"));
        Assert.Equal("Unbound", KeyboardKeys.Describe("not-a-key"));
    }

    [Fact]
    public void TryMapKey_MapsTheDefaultKeyboard()
    {
        var settings = new InputSettings();

        Assert.True(InputMapping.TryMapKey(settings, Key.A, out PadAction a));
        Assert.Equal(PadAction.A, a);
        Assert.True(InputMapping.TryMapKey(settings, Key.Space, out PadAction space));
        Assert.Equal(PadAction.A, space);
        Assert.True(InputMapping.TryMapKey(settings, Key.S, out PadAction b));
        Assert.Equal(PadAction.B, b);
        Assert.False(InputMapping.TryMapKey(settings, Key.P, out _));
    }

    [Fact]
    public void MergeKeyboard_OverlaysTheGamepadReport()
    {
        var settings = new InputSettings();
        HostPad gamepad = XInputPadMapping.Map(0, 0, 0, 10000, 0, 0, 0);

        HostPad merged = InputMapping.MergeKeyboard(gamepad, settings, [Key.A, Key.Up]);

        Assert.Equal(0xFF, merged.Analog[0]);
        Assert.Equal(XInputPadMapping.DpadUp, merged.Buttons);
        Assert.Equal(10000, merged.Sticks[0]);
    }

    [Fact]
    public void Create_MapsTheGamepadOptions()
    {
        var settings = new InputSettings
        {
            LeftDeadZonePercent = 10,
            RightDeadZonePercent = 20,
            TriggerThresholdPercent = 30,
            InvertLeftY = true,
            SwapSticks = true,
        };
        settings.Gamepad[nameof(PadAction.A)] = nameof(GamepadSource.RightTrigger);

        GamepadMappingOptions options = InputMapping.Create(settings);

        Assert.Equal(10, options.LeftDeadZonePercent);
        Assert.Equal(20, options.RightDeadZonePercent);
        Assert.Equal(30, options.TriggerThresholdPercent);
        Assert.True(options.InvertLeftY);
        Assert.True(options.SwapSticks);
        Assert.Equal(GamepadSource.RightTrigger, options.Bindings[PadAction.A]);
    }

    [Fact]
    public void GraphicsDefaults_NormalizeUnknownValues()
    {
        var graphics = new GraphicsSettings
        {
            Renderer = "directx",
            Filtering = "anisotropic",
            StartupSize = "800x600",
            AspectRatio = "21x9",
            Fit = "zoom",
            AvPack = "magnavox",
            SurfaceScale = 99,
        };

        graphics.Normalize();

        Assert.Equal(GraphicsSettings.RendererOpenGl, graphics.Renderer);
        Assert.Equal(GraphicsSettings.FilterLinear, graphics.Filtering);
        Assert.Equal("1280x960", graphics.StartupSize);
        Assert.Equal("auto", graphics.AspectRatio);
        Assert.Equal("scale", graphics.Fit);
        Assert.Equal("hdtv", graphics.AvPack);
        Assert.Equal(8, graphics.SurfaceScale);
    }

    [Fact]
    public void AudioDefaults_ClampAndRepair()
    {
        var audio = new AudioSettings
        {
            Backend = "asio",
            VolumeLimit = 4.0,
            VoiceWorkers = 99,
        };

        audio.Normalize();

        Assert.Equal(AudioSettings.BackendAuto, audio.Backend);
        Assert.Equal(1.0, audio.VolumeLimit);
        Assert.Equal(16, audio.VoiceWorkers);
    }

    [Fact]
    public void JsonRoundTrip_PreservesNestedSections()
    {
        var settings = new EmulatorSettings();
        settings.Input.Keyboard[nameof(PadAction.A)] = "Q";
        settings.Input.LeftDeadZonePercent = 10;
        settings.Graphics.Renderer = GraphicsSettings.RendererVulkan;
        settings.Graphics.SurfaceScale = 4;
        settings.Graphics.Filtering = GraphicsSettings.FilterNearest;
        settings.Audio.Backend = AudioSettings.BackendNone;
        settings.Audio.VolumeLimit = 0.5;

        string json = JsonSerializer.Serialize(settings);
        EmulatorSettings? loaded = JsonSerializer.Deserialize<EmulatorSettings>(json);
        Assert.NotNull(loaded);
        loaded.Normalize();

        Assert.Equal("Q", loaded.Input.Keyboard[nameof(PadAction.A)]);
        Assert.Equal(10, loaded.Input.LeftDeadZonePercent);
        Assert.Equal(GraphicsSettings.RendererVulkan, loaded.Graphics.Renderer);
        Assert.Equal(4, loaded.Graphics.SurfaceScale);
        Assert.Equal(GraphicsSettings.FilterNearest, loaded.Graphics.Filtering);
        Assert.Equal(AudioSettings.BackendNone, loaded.Audio.Backend);
        Assert.Equal(0.5, loaded.Audio.VolumeLimit);
    }

    [Fact]
    public void EmulatorSettings_CloneCopiesNestedSections()
    {
        var settings = new EmulatorSettings();
        settings.Input.Keyboard[nameof(PadAction.A)] = "Q";
        settings.Graphics.Renderer = GraphicsSettings.RendererVulkan;
        settings.Audio.VolumeLimit = 0.5;

        EmulatorSettings copy = settings.Clone();
        copy.Input.Keyboard[nameof(PadAction.A)] = "W";
        copy.Graphics.Renderer = GraphicsSettings.RendererNull;
        copy.Audio.VolumeLimit = 0.25;

        Assert.Equal("Q", settings.Input.Keyboard[nameof(PadAction.A)]);
        Assert.Equal(GraphicsSettings.RendererVulkan, settings.Graphics.Renderer);
        Assert.Equal(0.5, settings.Audio.VolumeLimit);
    }
}

