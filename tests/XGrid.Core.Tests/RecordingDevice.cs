using System.Collections.Generic;
using XGrid.Core.Devices;

namespace XGrid.Core.Tests;

internal sealed class RecordingDevice : IEmulatedDevice
{
    private readonly List<string> _log;

    public RecordingDevice(string name, List<string> log)
    {
        Name = name;
        _log = log;
    }

    public string Name { get; }

    public int ResetCount { get; private set; }

    public int TickCount { get; private set; }

    public ulong TotalCycles { get; private set; }

    public void Reset()
    {
        ResetCount++;
        _log.Add($"{Name}:reset");
    }

    public void Tick(ulong cycles)
    {
        TickCount++;
        TotalCycles += cycles;
        _log.Add($"{Name}:tick:{cycles}");
    }
}
