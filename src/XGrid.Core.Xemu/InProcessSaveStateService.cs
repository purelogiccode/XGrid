using System;
using System.Collections.Generic;

namespace XGrid.Core.Xemu;

/// <summary>
/// Save states for the in-process core. Every call goes straight through the
/// ABI; the core queues the work on its own main loop and this type only maps
/// the status codes into the shell's vocabulary.
/// </summary>
public sealed class InProcessSaveStateService : ISaveStateService
{
    private readonly XemuInProcessCore _core;

    /// <summary>Creates the service for one loaded core.</summary>
    /// <param name="core">In-process core.</param>
    public InProcessSaveStateService(XemuInProcessCore core)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
    }

    /// <inheritdoc />
    public XemuSaveStateStatus Status => _core.SnapshotStatus;

    /// <inheritdoc />
    public string Error => _core.SnapshotError;

    /// <inheritdoc />
    public IReadOnlyList<string> Names => _core.SnapshotNames;

    /// <inheritdoc />
    public bool TrySave(string name, out string problem)
    {
        return Describe(_core.SnapshotSave(name), out problem);
    }

    /// <inheritdoc />
    public bool TryLoad(string name, out string problem)
    {
        return Describe(_core.SnapshotLoad(name), out problem);
    }

    /// <inheritdoc />
    public bool TryDelete(string name, out string problem)
    {
        return Describe(_core.SnapshotDelete(name), out problem);
    }

    /// <inheritdoc />
    public bool TryRefresh(out string problem)
    {
        return Describe(_core.SnapshotRefresh(), out problem);
    }

    private static bool Describe(int status, out string problem)
    {
        problem = status switch
        {
            0 => string.Empty,
            -1 => "The emulator is not running.",
            -2 => "That save-state name cannot be used.",
            -3 => "The emulator is still working on the previous save state.",
            _ => $"The emulator refused the save-state request (status {status}).",
        };
        return status == 0;
    }
}
