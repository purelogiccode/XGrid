using System;

namespace XGrid.Core.Xemu;

/// <summary>
/// Playback control for the in-process core. The core applies the request on
/// its own main loop, so this type only maps the ABI status codes into the
/// shell's messages.
/// </summary>
public sealed class InProcessPlayback : ICorePlayback
{
    private readonly XemuInProcessCore _core;

    /// <summary>Creates the service for one loaded core.</summary>
    /// <param name="core">In-process core.</param>
    public InProcessPlayback(XemuInProcessCore core)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
    }

    /// <inheritdoc />
    public bool TrySetPaused(bool paused, out string problem)
    {
        int status = _core.SetPaused(paused);
        problem = status switch
        {
            0 => string.Empty,
            -1 => "The emulator is not running.",
            _ => $"The emulator refused the pause request (status {status}).",
        };
        return status == 0;
    }

    /// <inheritdoc />
    public bool TryStepFrame(out string problem)
    {
        int status = _core.StepFrame();
        problem = status switch
        {
            0 => string.Empty,
            -1 => "The emulator is not running.",
            -2 => "Pause the emulator before stepping.",
            -3 => "The emulator is still stepping.",
            _ => $"The emulator refused the frame step (status {status}).",
        };
        return status == 0;
    }

    /// <inheritdoc />
    public bool TrySetSpeed(int multiplier, out string problem)
    {
        int status = _core.SetSpeed(multiplier);
        problem = status switch
        {
            0 => string.Empty,
            -1 => "The emulator is not running.",
            -2 => "Fast-forward runs from 1x to 8x.",
            _ => $"The emulator refused the speed request (status {status}).",
        };
        return status == 0;
    }
}
