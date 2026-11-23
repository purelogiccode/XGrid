namespace XGrid.Core.Emulation;

/// <summary>Controls the emulator lifecycle. Implementations must not depend on a frontend.</summary>
public interface IEmulator
{
    /// <summary>Gets the current lifecycle state.</summary>
    EmulatorState State { get; }

    /// <summary>Initializes emulated hardware and configuration.</summary>
    void Initialize();

    /// <summary>Returns the machine to its power-on state.</summary>
    void Reset();

    /// <summary>Starts or resumes execution.</summary>
    void Start();

    /// <summary>Suspends execution without discarding state.</summary>
    void Pause();

    /// <summary>Resumes execution after a pause.</summary>
    void Resume();

    /// <summary>Stops execution and releases the emulation loop.</summary>
    void Stop();
}
