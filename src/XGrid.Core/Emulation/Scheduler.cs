using System;
using System.Collections.Generic;
using XGrid.Core.Devices;

namespace XGrid.Core.Emulation;

/// <summary>
/// Advances registered devices in deterministic registration order. Coarse cycle ticking is used
/// initially; event-driven scheduling replaces it in a later phase where useful.
/// </summary>
public sealed class Scheduler
{
    private readonly List<IEmulatedDevice> _devices = [];

    /// <summary>Gets the registered devices in execution order.</summary>
    public IReadOnlyList<IEmulatedDevice> Devices => _devices;

    /// <summary>Gets the number of registered devices.</summary>
    public int DeviceCount => _devices.Count;

    /// <summary>Registers a device. Order of registration defines tick order.</summary>
    public void Register(IEmulatedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _devices.Add(device);
    }

    /// <summary>Removes all registered devices.</summary>
    public void Clear()
    {
        _devices.Clear();
    }

    /// <summary>Resets every registered device in registration order.</summary>
    public void Reset()
    {
        for (int i = 0; i < _devices.Count; i++)
        {
            _devices[i].Reset();
        }
    }

    /// <summary>Ticks every registered device in registration order.</summary>
    public void Tick(ulong cycles)
    {
        for (int i = 0; i < _devices.Count; i++)
        {
            _devices[i].Tick(cycles);
        }
    }
}
