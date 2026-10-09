namespace XGrid.Core.Emulation;

/// <summary>Lifecycle states of the emulator.</summary>
public enum EmulatorState
{
    /// <summary>The emulator has been constructed but not initialized.</summary>
    Created = 0,

    /// <summary>Hardware and configuration have been initialized.</summary>
    Initialized = 1,

    /// <summary>The emulation loop is executing.</summary>
    Running = 2,

    /// <summary>Execution is suspended and can be resumed.</summary>
    Paused = 3,

    /// <summary>The emulator has stopped and can be reset or disposed.</summary>
    Stopped = 4,

    /// <summary>An unrecoverable emulator error occurred.</summary>
    Faulted = 5,
}
