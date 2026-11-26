using System;
using System.Globalization;
using System.Text;

namespace XGrid.Core.Xemu;

/// <summary>
/// Generates the <c>xemu.toml</c> the core is launched with. The output is the
/// whole configuration (never a delta over a user file), so a launch cannot
/// inherit state from a previous one.
/// </summary>
/// <remarks>
/// Key names and value types are taken from the core's own configuration
/// specification (<c>config_spec.yml</c>) and were verified against the
/// 0.8.139 binary: <c>sys.mem_limit</c> is the string enum <c>'64'</c> /
/// <c>'128'</c> (an integer is rejected as the wrong type), and
/// <c>mem_limit</c> + 1 multiplied by 64 is the guest RAM in MB.
/// </remarks>
public static class XemuTomlWriter
{
    /// <summary>Builds the complete configuration document for a launch.</summary>
    /// <param name="settings">Validated launch settings.</param>
    /// <returns>The TOML text, newline-terminated.</returns>
    public static string Build(XemuLaunchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var text = new StringBuilder(1024);
        text.Append("[general]\n");
        // The welcome overlay disables autostart; the shell owns configuration.
        text.Append("show_welcome = false\n");
        text.Append("skip_boot_anim = ").Append(Bool(settings.SkipBootAnimation)).Append('\n');
        AppendPath(text, "screenshot_dir", settings.ScreenshotDirectory);
        AppendPath(text, "shader_cache_dir", settings.ShaderCacheDirectory);
        text.Append('\n');

        // No update check: a launch must never touch the network for this.
        text.Append("[general.updates]\n");
        text.Append("check = false\n");
        text.Append('\n');

        text.Append("[display]\n");
        text.Append("renderer = '").Append(settings.Renderer).Append("'\n");
        text.Append("filtering = '").Append(settings.Filtering).Append("'\n");
        text.Append('\n');

        text.Append("[display.quality]\n");
        text.Append("surface_scale = ").Append(settings.SurfaceScale.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append('\n');

        text.Append("[display.window]\n");
        text.Append("startup_size = '").Append(settings.StartupSize).Append("'\n");
        text.Append("last_width = ").Append(settings.WindowWidth.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("last_height = ").Append(settings.WindowHeight.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("vsync = ").Append(Bool(settings.VSync)).Append('\n');
        text.Append("fullscreen_on_startup = ").Append(Bool(settings.FullscreenOnStartup)).Append('\n');
        text.Append("fullscreen_exclusive = ").Append(Bool(settings.FullscreenExclusive)).Append('\n');
        text.Append('\n');

        text.Append("[display.ui]\n");
        text.Append("show_menubar = ").Append(Bool(settings.ShowMenuBar)).Append('\n');
        // The shell surfaces its own messages; core toasts would draw inside
        // the embedded display rectangle.
        text.Append("show_notifications = false\n");
        text.Append("fit = '").Append(settings.Fit).Append("'\n");
        text.Append("aspect_ratio = '").Append(settings.AspectRatio).Append("'\n");
        text.Append('\n');

        // Bind the core's keyboard controller to port 1 when the shell supplies
        // the pad. The core only binds a controller whose device id matches the
        // port's binding (`port1`), and the shell's sink then replaces that
        // controller's state; `port1_driver` stays at its Duke default.
        if (!string.IsNullOrWhiteSpace(settings.InputSinkName))
        {
            text.Append("[input.bindings]\n");
            text.Append("port1 = 'keyboard'\n");
            text.Append('\n');
        }

        text.Append("[audio]\n");
        text.Append("use_dsp = ").Append(Bool(settings.AudioDsp)).Append('\n');
        text.Append("use_dsp_jit = ").Append(Bool(settings.AudioDspJit)).Append('\n');
        text.Append("hrtf = ").Append(Bool(settings.Hrtf)).Append('\n');
        text.Append("volume_limit = ")
            .Append(settings.VolumeLimit.ToString("0.###", CultureInfo.InvariantCulture))
            .Append('\n');
        text.Append('\n');

        text.Append("[audio.vp]\n");
        text.Append("num_workers = ").Append(settings.VoiceWorkers.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append('\n');

        text.Append("[sys]\n");
        text.Append("default_machine = '").Append(settings.Machine).Append("'\n");
        text.Append("mem_limit = '").Append(settings.RamMb == 128 ? "128" : "64").Append("'\n");
        text.Append("avpack = '").Append(settings.AvPack).Append("'\n");
        text.Append('\n');

        text.Append("[sys.files]\n");
        AppendPath(text, "bootrom_path", settings.BootRomPath);
        AppendPath(text, "flashrom_path", settings.FlashRomPath);
        AppendPath(text, "eeprom_path", settings.EepromPath);
        AppendPath(text, "hdd_path", settings.HddPath);
        AppendPath(text, "dvd_path", settings.DvdPath);
        if (settings.IsChihiro)
        {
            text.Append('\n');
            text.Append("[chihiro.roms]\n");
            AppendPath(text, "bios_path", settings.ChihiroBiosPath);
            AppendPath(text, "mediaboard_path", settings.ChihiroMediaboardPath);
            AppendPath(text, "ic10_path", settings.ChihiroIc10Path);
            AppendPath(text, "ic11_path", settings.ChihiroIc11Path);
            AppendPath(text, "pc20_path", settings.ChihiroPc20Path);
            if (!string.IsNullOrWhiteSpace(settings.ChihiroNetFirmwarePath))
            {
                AppendPath(text, "net_firmware_path", settings.ChihiroNetFirmwarePath);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Renders a TOML string. Literal (single-quoted) form is preferred because
    /// Windows paths contain backslashes, which a basic string would require us
    /// to escape; values that cannot be literal use the basic form.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <returns>A TOML string token.</returns>
    internal static string String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.AsSpan().IndexOfAny("'\n\r\0") < 0)
        {
            return "'" + value + "'";
        }

        var text = new StringBuilder(value.Length + 8);
        text.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    text.Append("\\\\");
                    break;
                case '"':
                    text.Append("\\\"");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                default:
                    if (c < 0x20 || c == 0x7F)
                    {
                        text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        text.Append('"');
        return text.ToString();
    }

    private static void AppendPath(StringBuilder text, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        text.Append(key).Append(" = ").Append(String(value)).Append('\n');
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }
}
