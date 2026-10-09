namespace XGrid.Core.Emulation;

/// <summary>Runtime options that are not persisted in the configuration file.</summary>
public sealed class EmulatorOptions
{
    /// <summary>Default options: deterministic, one cycle per step.</summary>
    public static EmulatorOptions Default { get; } = new();

    /// <summary>Gets how emulated time advances.</summary>
    public TimeMode TimeMode { get; init; } = TimeMode.Deterministic;

    /// <summary>Gets the number of cycles in one deterministic step. Must be greater than zero.</summary>
    public ulong CyclesPerStep { get; init; } = 1;

    /// <summary>Gets whether the emulator runs in deterministic mode.</summary>
    public bool IsDeterministic => TimeMode == TimeMode.Deterministic;

    internal void Validate()
    {
        if (CyclesPerStep == 0)
        {
            throw new ConfigurationException("cpu.cyclesPerStep must be greater than zero.");
        }
    }
}
