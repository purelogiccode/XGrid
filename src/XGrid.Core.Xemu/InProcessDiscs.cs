using System;

namespace XGrid.Core.Xemu;

/// <summary>
/// Disc change for the in-process core. The core runs the block-layer
/// operation on its own main loop; this type only maps the status codes into
/// the shell's vocabulary.
/// </summary>
public sealed class InProcessDiscs : ICoreDiscs
{
    private readonly XemuInProcessCore _core;

    /// <summary>Creates the service for one loaded core.</summary>
    /// <param name="core">In-process core.</param>
    public InProcessDiscs(XemuInProcessCore core)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
    }

    /// <inheritdoc />
    public XemuDiscStatus Status
    {
        get
        {
            int status = _core.DiscStatus;
            return Enum.IsDefined(typeof(XemuDiscStatus), status)
                ? (XemuDiscStatus)status
                : XemuDiscStatus.Failed;
        }
    }

    /// <inheritdoc />
    public string Error => _core.DiscError;

    /// <inheritdoc />
    public bool TryLoad(string path, out string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Describe(_core.DiscLoad(path), out problem);
    }

    /// <inheritdoc />
    public bool TryEject(out string problem)
    {
        return Describe(_core.DiscEject(), out problem);
    }

    private static bool Describe(int status, out string problem)
    {
        problem = status switch
        {
            0 => string.Empty,
            -1 => "The emulator is not running.",
            -2 => "That disc image path cannot be used.",
            -3 => "A disc change is still in progress.",
            _ => $"The emulator refused the disc change (status {status}).",
        };
        return status == 0;
    }
}
