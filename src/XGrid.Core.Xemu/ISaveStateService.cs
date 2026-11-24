using System.Collections.Generic;

namespace XGrid.Core.Xemu;

/// <summary>What a queued save-state request is doing.</summary>
public enum XemuSaveStateOperation
{
    /// <summary>No request was made.</summary>
    None = 0,

    /// <summary>Writing the machine's state to a snapshot.</summary>
    Save = 1,

    /// <summary>Restoring the machine from a snapshot.</summary>
    Load = 2,

    /// <summary>Removing a snapshot.</summary>
    Delete = 3,

    /// <summary>Re-reading the snapshot names.</summary>
    Refresh = 4,
}

/// <summary>The state of the core's save-state request slot.</summary>
public enum XemuSaveStateStatus
{
    /// <summary>Nothing was requested yet.</summary>
    Idle = 0,

    /// <summary>The core accepted a request and has not finished it.</summary>
    Pending = 1,

    /// <summary>The last request finished successfully.</summary>
    Completed = 2,

    /// <summary>The last request failed; see the error text.</summary>
    Failed = 3,
}

/// <summary>
/// Save states as the core exposes them. A request only queues work: xemu
/// writes the machine's state as an internal snapshot of the hard disk image,
/// which only the core's own main loop may do. Callers start a request and poll
/// its status.
/// </summary>
/// <remarks>
/// A request is accepted only while the previous one has finished, so
/// <see cref="Status"/> and <see cref="Names"/> always describe one operation.
/// </remarks>
public interface ISaveStateService
{
    /// <summary>Gets the last request's state.</summary>
    XemuSaveStateStatus Status { get; }

    /// <summary>Gets the last request's error text, or an empty string.</summary>
    string Error { get; }

    /// <summary>Gets the snapshot names the last refresh found.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Queues a snapshot save.</summary>
    /// <param name="name">Snapshot name; an existing one is replaced.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    bool TrySave(string name, out string problem);

    /// <summary>Queues a snapshot load.</summary>
    /// <param name="name">Snapshot name.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    bool TryLoad(string name, out string problem);

    /// <summary>Queues a snapshot delete.</summary>
    /// <param name="name">Snapshot name.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    bool TryDelete(string name, out string problem);

    /// <summary>Queues a refresh of <see cref="Names"/>.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    bool TryRefresh(out string problem);
}
