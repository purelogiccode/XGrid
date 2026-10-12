using System.Collections.Generic;
using System.IO;
using System.Text;
using XGrid.Core;
using XGrid.Host;
using Xunit;

namespace XGrid.Integration.Tests;

public sealed class SnapshotRegistrationTests
{
    private static List<string> ReadSectionTags(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        Assert.Equal("XGS1", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        reader.ReadUInt32();
        int count = reader.ReadInt32();
        var tags = new List<string>();
        for (int i = 0; i < count; i++)
        {
            tags.Add(reader.ReadString());
            ulong length = reader.ReadUInt64();
            reader.BaseStream.Seek((long)length, SeekOrigin.Current);
        }

        return tags;
    }

    [Fact]
    public void SnapshotFile_ListsAllStatefulDeviceSections()
    {
        var machine = new XboxMachine();
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            machine.SaveState(path);
            List<string> tags = ReadSectionTags(path);
            Assert.Equal(
                new[] { "CPU", "RAM", "BUS", "IDE", "NV2A", "ACI", "APU", "NET", "USB0", "USB1", "MACH" },
                tags);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SnapshotDevices_AllImplementContract()
    {
        var machine = new XboxMachine();
        ISnapshotable[] devices =
        {
            machine.Bus, machine.Ide, machine.Nv2A, machine.Aci,
            machine.Apu, machine.Net, machine.Usb0, machine.Usb1,
        };

        foreach (ISnapshotable device in devices)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            device.SaveState(writer);
            writer.Flush();
            Assert.True(stream.Length > 0);
        }
    }
}
