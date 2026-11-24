using System;
using System.Collections.Generic;

namespace XGrid.Frontend.Avalonia;

/// <summary>
/// The command-line switches the shell handles itself before Avalonia starts.
/// Everything else is passed through unchanged.
/// </summary>
/// <param name="Autostart">True for <c>--autostart</c>.</param>
/// <param name="Fullscreen">True for <c>-fullscreen</c>, <c>--fullscreen</c> or <c>-f</c>.</param>
/// <param name="Remaining">Arguments Avalonia and the platform should see.</param>
public sealed record LaunchArguments(bool Autostart, bool Fullscreen, IReadOnlyList<string> Remaining)
{
    /// <summary>Parses the process arguments, accepting the switches in any case.</summary>
    /// <param name="arguments">The command line, not including the executable.</param>
    /// <returns>The recognized switches and the remaining arguments.</returns>
    public static LaunchArguments Parse(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        bool autostart = false;
        bool fullscreen = false;
        var remaining = new List<string>();
        foreach (string argument in arguments)
        {
            if (string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase))
            {
                autostart = true;
            }
            else if (IsFullscreen(argument))
            {
                fullscreen = true;
            }
            else
            {
                remaining.Add(argument);
            }
        }

        return new LaunchArguments(autostart, fullscreen, remaining);
    }

    private static bool IsFullscreen(string argument)
    {
        return string.Equals(argument, "-fullscreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "--fullscreen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "-f", StringComparison.OrdinalIgnoreCase);
    }
}
