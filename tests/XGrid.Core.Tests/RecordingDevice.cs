using System.Collections.Generic;
using XGrid.Core.Devices;

namespace XGrid.Core.Tests;

internal sealed class RecordingDevice(string name, List<string> log) : IEmulatedDevice
{

    private string Name { get; } = name;

    public int ResetCount { get; private set; }

    public int TickCount { get; private set; }

    public ulong TotalCycles { get; private set; }

    public void Reset()
    {
        ResetCount++;
        log.Add($"{Name}:reset");
    }

    public void Tick(ulong cycles)
    {
        TickCount++;
        TotalCycles += cycles;
        log.Add($"{Name}:tick:{cycles}");
    }
}
