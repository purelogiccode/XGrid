namespace XGrid.Core.Devices;

/// <summary>An Xbox hardware device that can register itself with the machine.</summary>
public interface IXboxDevice
{
    /// <summary>Gets the device name used in traces and diagnostics.</summary>
    string Name { get; }

    /// <summary>Returns the device to its power-on state.</summary>
    void Reset();
}
