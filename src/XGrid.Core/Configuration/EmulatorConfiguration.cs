using XGrid.Core.Emulation;

namespace XGrid.Core.Configuration;

/// <summary>Root emulator configuration (plan section 34).</summary>
public sealed class EmulatorConfiguration
{
    /// <summary>Gets the firmware and storage configuration.</summary>
    public SystemConfiguration System { get; init; } = new();

    /// <summary>Gets the graphics configuration.</summary>
    public GraphicsConfiguration Graphics { get; init; } = new();

    /// <summary>Gets the CPU configuration.</summary>
    public CpuConfiguration Cpu { get; init; } = new();

    /// <summary>Gets the audio configuration.</summary>
    public AudioConfiguration Audio { get; init; } = new();

    /// <summary>Gets the input configuration.</summary>
    public InputConfiguration Input { get; init; } = new();

    /// <summary>Validates the configuration and returns it.</summary>
    /// <exception cref="ConfigurationException">A value is out of range or a section is null.</exception>
    public EmulatorConfiguration Validate()
    {
        if (System is null || Graphics is null || Cpu is null || Audio is null || Input is null)
        {
            throw new ConfigurationException("All configuration sections are required.");
        }

        return Graphics.ResolutionScale < 1 || Graphics.ResolutionScale > 16
            ? throw new ConfigurationException("graphics.resolutionScale must be between 1 and 16.")
            : string.IsNullOrWhiteSpace(Input.Backend) ? throw new ConfigurationException("input.backend must not be empty.") : this;
    }
}
