using System;
using System.IO;
using System.Text;
using XGrid.Host;
using Xunit;

namespace XGrid.Integration.Tests;

public sealed class MachineSnapshotTests
{
    [Fact]
    public void SaveRestore_ContinuationMatchesUninterrupted()
    {
        string first = Path.GetTempFileName();
        string second = Path.GetTempFileName();
        string resume = Path.GetTempFileName();
        try
        {
            XboxMachine reference = CreateLoadedMachine();
            Assert.Equal(2, reference.Run(2));
            reference.SaveState(first);

            XboxMachine probed = CreateLoadedMachine();
            Assert.Equal(1, probed.Run(1));
            probed.SaveState(resume);

            XboxMachine continued = CreateLoadedMachine();
            continued.LoadState(resume);
            Assert.Equal(1, continued.Run(1));
            continued.SaveState(second);

            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
            Assert.Equal(0x5678u, continued.Cpu.Context.Eax & 0xFFFF);
            Assert.True(continued.Cpu.Context.Halted);
            Assert.Equal(reference.Cpu.Context.Eip, continued.Cpu.Context.Eip);
            Assert.Equal(reference.Cpu.Context.Tsc, continued.Cpu.Context.Tsc);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            File.Delete(resume);
        }
    }

    [Fact]
    public void LoadState_BadMagic_Throws()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { (byte)'N', (byte)'O', (byte)'P', (byte)'E', 1, 0, 0, 0 });

            XboxMachine machine = CreateLoadedMachine();
            Assert.Throws<InvalidDataException>(() => machine.LoadState(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadState_MissingSection_Throws()
    {
        string path = Path.GetTempFileName();
        try
        {
            XboxMachine machine = CreateLoadedMachine();
            byte[] cpuPayload = CaptureSection(machine, "CPU");

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(Encoding.ASCII.GetBytes(XboxMachine.SnapshotMagic));
                writer.Write(XboxMachine.SnapshotFormatVersion);
                writer.Write(1);
                writer.Write("CPU");
                writer.Write((ulong)cpuPayload.Length);
                writer.Write(cpuPayload);
            }

            Assert.Throws<InvalidDataException>(() => machine.LoadState(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadState_UnknownSection_IsIgnored()
    {
        string path = Path.GetTempFileName();
        try
        {
            XboxMachine machine = CreateLoadedMachine();
            machine.Run(1);

            byte[] cpuPayload = CaptureSection(machine, "CPU");
            byte[] ramPayload = CaptureSection(machine, "RAM");
            byte[] machPayload = CaptureSection(machine, "MACH");

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(Encoding.ASCII.GetBytes(XboxMachine.SnapshotMagic));
                writer.Write(XboxMachine.SnapshotFormatVersion);
                writer.Write(4);
                WriteSection(writer, "CPU", cpuPayload);
                WriteSection(writer, "FUTURE-DEVICE", new byte[] { 1, 2, 3, 4 });
                WriteSection(writer, "RAM", ramPayload);
                WriteSection(writer, "MACH", machPayload);
            }

            XboxMachine restored = CreateLoadedMachine();
            restored.LoadState(path);

            Assert.Equal(machine.Cpu.Context.Eip, restored.Cpu.Context.Eip);
            Assert.Equal(machine.Cpu.Context.Eax, restored.Cpu.Context.Eax);
            Assert.Equal(machine.Cpu.Context.Tsc, restored.Cpu.Context.Tsc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RegisterSnapshotSection_DuplicateTag_Throws()
    {
        XboxMachine machine = CreateLoadedMachine();

        Assert.Throws<ArgumentException>(() => machine.RegisterSnapshotSection("CPU", machine.Cpu));
    }

    private static void WriteSection(BinaryWriter writer, string tag, byte[] payload)
    {
        writer.Write(tag);
        writer.Write((ulong)payload.Length);
        writer.Write(payload);
    }

    private static byte[] CaptureSection(XboxMachine machine, string tag)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            switch (tag)
            {
                case "CPU":
                    machine.Cpu.SaveState(writer);
                    break;
                case "RAM":
                    machine.Ram.SaveState(writer);
                    break;
                case "MACH":
                    // Round-trip through a full file to capture the core-owned
                    // machine scalars without reaching into private state.
                    string path = Path.GetTempFileName();
                    try
                    {
                        machine.SaveState(path);
                        return ExtractSection(path, tag);
                    }
                    finally
                    {
                        File.Delete(path);
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(tag), tag, "Unknown core section.");
            }
        }

        return stream.ToArray();
    }

    private static byte[] ExtractSection(string path, string wanted)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        Assert.Equal(XboxMachine.SnapshotMagic, Encoding.ASCII.GetString(reader.ReadBytes(4)));
        reader.ReadUInt32();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string tag = reader.ReadString();
            byte[] payload = reader.ReadBytes((int)reader.ReadUInt64());
            if (string.Equals(tag, wanted, StringComparison.Ordinal))
            {
                return payload;
            }
        }

        throw new InvalidOperationException($"Section '{wanted}' not found.");
    }

    private static XboxMachine CreateLoadedMachine()
    {
        var machine = new XboxMachine();
        byte[] bios = new byte[256 * 1024];
        Array.Fill(bios, (byte)0x90);
        byte[] rom = new byte[512];
        Array.Fill(rom, (byte)0x90);

        // Reset vector at ROM offset 0x1F0: mov ax, 0x5678; hlt (real mode).
        byte[] code = { 0xB8, 0x78, 0x56, 0xF4 };
        code.CopyTo(rom, 0x1F0);

        machine.LoadBios(bios, rom);
        machine.Reset();
        return machine;
    }
}
