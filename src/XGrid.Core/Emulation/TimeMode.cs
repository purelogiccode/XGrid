namespace XGrid.Core.Emulation;

/// <summary>Selects how emulated time advances.</summary>
public enum TimeMode
{
    /// <summary>Time advances only by explicit fixed steps. Required for reproducible tests.</summary>
    Deterministic = 0,

    /// <summary>Time is driven by host wall-clock pacing (used by the frontend/CLI).</summary>
    HostTime = 1,
}
