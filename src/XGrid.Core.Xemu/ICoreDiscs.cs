namespace XGrid.Core.Xemu;

/// <summary>The state of the core's disc request slot.</summary>
public enum XemuDiscStatus
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
/// Disc change as the core exposes it. The request runs on the core's own main
/// loop, so the shell queues it and polls the status instead of blocking.
/// </summary>
public interface ICoreDiscs
{
    /// <summary>Gets the last request's state.</summary>
    XemuDiscStatus Status { get; }

    /// <summary>Gets the last request's error text, or an empty string.</summary>
    string Error { get; }

    /// <summary>Queues a change of the inserted disc.</summary>
    /// <param name="path">ISO/XISO, CHD or ZArchive image.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    bool TryLoad(string path, out string problem);

    /// <summary>Queues an eject, leaving the drive connected and empty.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    /// <returns>True when the core accepted the request.</returns>
    bool TryEject(out string problem);
}
