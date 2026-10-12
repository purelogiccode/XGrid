using System.IO;
using XGrid.Core;
using XGrid.Host;
using Xunit;

namespace XGrid.Integration.Tests;

public sealed class SnapshotRegistrationTests
{
    [Fact]
    public void SnapshotDevices_ListsAllStatefulDevices()
    {
        var machine = new XboxMachine();

        Assert.Equal(8, machine.SnapshotDevices.Count);
        Assert.Same(machine.Bus, machine.SnapshotDevices[0]);
        Assert.Same(machine.Ide, machine.SnapshotDevices[1]);
        Assert.Same(machine.Nv2A, machine.SnapshotDevices[2]);
        Assert.Same(machine.Aci, machine.SnapshotDevices[3]);
        Assert.Same(machine.Apu, machine.SnapshotDevices[4]);
        Assert.Same(machine.Net, machine.SnapshotDevices[5]);
        Assert.Same(machine.Usb0, machine.SnapshotDevices[6]);
        Assert.Same(machine.Usb1, machine.SnapshotDevices[7]);
    }

    [Fact]
    public void SnapshotDevices_AllImplementContract()
    {
        var machine = new XboxMachine();

        foreach (ISnapshotable device in machine.SnapshotDevices)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            device.SaveState(writer);
            writer.Flush();
            Assert.True(stream.Length > 0);
        }
    }
}
