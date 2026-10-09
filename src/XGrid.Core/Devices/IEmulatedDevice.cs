namespace XGrid.Core.Devices;

/// <summary>A device scheduled by the emulator clock.</summary>
public interface IEmulatedDevice
{
    /// <summary>Returns the device to its power-on state.</summary>
    void Reset();

    /// <summary>Advances the device by the given number of emulated cycles.</summary>
    void Tick(ulong cycles);
}
