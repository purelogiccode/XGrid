using System.IO;
using XGrid.Core.Snapshots;
using Xunit;

namespace XGrid.Nv2a.Tests;

public sealed class Nv2ASnapshotTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private sealed class ArrayMemory : IGpuMemory
    {
        private readonly byte[] _data = new byte[0x10000];

        public void WritePhysical(uint address, uint value)
        {
            _data[address] = (byte)value;
            _data[address + 1] = (byte)(value >> 8);
            _data[address + 2] = (byte)(value >> 16);
            _data[address + 3] = (byte)(value >> 24);
        }

        public uint ReadPhysical(uint address)
        {
            return (uint)(_data[address] | (_data[address + 1] << 8) | (_data[address + 2] << 16) |
                          (_data[address + 3] << 24));
        }
    }

    private static byte[] Save(ISnapshotable device)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        device.SaveState(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Load(ISnapshotable device, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        device.LoadState(reader);
    }

    private static (Nv2AMmio Nv2a, ArrayMemory Memory) CreateChannel()
    {
        var memory = new ArrayMemory();
        var nv2A = new Nv2AMmio(64u * 1024 * 1024, "NV2A", memory);
        nv2A.Write(Base + 0x2504u, 4, 1u);
        nv2A.Write(Base + 0x3200u, 4, 1u);
        nv2A.Write(Base + 0x3204u, 4, 0x100u);
        nv2A.Write(Base + 0x3220u, 4, 1u);
        nv2A.Write(Base + 0x322Cu, 4, 0x111Cu);
        nv2A.Write(Base + 0x3250u, 4, 1u);
        nv2A.Write(Base + 0x400720u, 4, 1u);
        nv2A.Write(Base + 0x7111C0u, 4, 0x00000002u);
        nv2A.Write(Base + 0x7111C4u, 4, 0x0000FFFFu);
        nv2A.Write(Base + 0x7111C8u, 4, 0x00000000u);
        return (nv2A, memory);
    }

    private static void PushMethods(Nv2AMmio nv2A, ArrayMemory memory, params (uint Method, uint Parameter)[] methods)
    {
        uint address = 0;
        foreach ((uint method, uint parameter) in methods)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        memory.WritePhysical(address, 0x00001001u);
        nv2A.Write(Base + 0x800040u, 4, address + 4);
    }

    private static void Exercise(Nv2AMmio nv2A, ArrayMemory memory)
    {
        PushMethods(
            nv2A, memory,
            (0x200u, 0x028001E0u),
            (0x204u, 0x01E001E0u),
            (0x208u, 0x00000005u),
            (0x20Cu, 0x00000A00u),
            (0x210u, 0x00324000u),
            (0x214u, 0x00300000u),
            (0x300u, 1u),
            (0x304u, 1u),
            (0x30Cu, 1u),
            (0x32Cu, 1u),
            (0x33Cu, 0x000001D1u),
            (0x340u, 0x00000080u),
            (0x344u, 0x00000302u),
            (0x348u, 0x00000303u),
            (0x350u, 0x00000802u),
            (0x354u, 0x00000201u),
            (0x358u, 0x01010101u),
            (0x35Cu, 1u),
            (0x364u, 0x00000207u),
            (0x368u, 0x0000002Au),
            (0x120u, 0x00000002u),
            (0x124u, 0x00000003u),
            (0x128u, 0x00000002u),
            (0x29Cu, 1u),
            (0x2A4u, 1u),
            (0x2A8u, 0x00FF8040u),
            (0x290u, 1u << 12),
            (0x394u, 0x3F800000u),
            (0x398u, 0x7F800000u),
            (0x1D8Cu, 0x00FFFFFFu),
            (0x1D90u, 0x00FF00FFu),
            (0x1D98u, 0x028001E0u),
            (0x1E60u, 0x00000002u),
            (0x1E70u, 0x00008421u),
            (0x1E74u, 0x00000123u),
            (0x1E78u, 0x00100000u),
            (0x17F8u, 0x00010001u),
            (0x1E9Cu, 0x00000004u),
            (0x0B00u, 0x11111111u),
            (0x0B04u, 0x22222222u),
            (0x1EA4u, 0x00000002u),
            (0x0B80u, 0x3F800000u),
            (0x1EA0u, 0x00000000u),
            (0x1760u, 0x00000112u),
            (0x1720u, 0x00100000u),
            (0x1B00u, 0x00100040u),
            (0x1B04u, 0x00000605u),
            (0x0260u, 0x00001234u),
            (0x0AC0u, 0x00005678u),
            (0x0A60u, 0x00ABCDEFu),
            (0x09C0u, 0x3F800000u));
        nv2A.Write(Base + 0x700010u, 4, 0xDEADBEEFu);
        nv2A.Write(Base + 0x000140u, 4, 0xFFFFFFFFu);
        nv2A.Advance(1000000);
        nv2A.Read(Base + 0x009400u, 4);
        nv2A.Read(Base + 0x600808u, 4);
    }

    [Fact]
    public void Nv2a_RoundTrip()
    {
        var first = CreateChannel();
        Exercise(first.Nv2a, first.Memory);
        byte[] baseline = Save(first.Nv2a);
        Assert.True(first.Nv2a.MethodCounts.Count > 0);

        var second = CreateChannel();
        Load(second.Nv2a, baseline);
        Assert.Equal(baseline, Save(second.Nv2a));
        Assert.Equal(first.Nv2a.DrawsExecuted, second.Nv2a.DrawsExecuted);
        Assert.Equal(first.Nv2a.FlipWriteDebug, second.Nv2a.FlipWriteDebug);

        Exercise(second.Nv2a, second.Memory);
        Load(second.Nv2a, baseline);
        Assert.Equal(baseline, Save(second.Nv2a));
    }

    [Fact]
    public void Combiner_RoundTrip()
    {
        var first = new PshCombiner();
        ExerciseCombiner(first);
        byte[] baseline = Save(first);

        var second = new PshCombiner();
        Load(second, baseline);
        Assert.Equal(baseline, Save(second));
        Assert.Equal(first.Control, second.Control);
        Assert.Equal(first.Final1, second.Final1);
        return;

        static void ExerciseCombiner(PshCombiner combiner)
        {
            combiner.RgbInputs[0] = 0x00001234;
            combiner.AlphaInputs[2] = 0x00005678;
            combiner.RgbOutputs[1] = 0x00009ABC;
            combiner.AlphaOutputs[3] = 0x0000DEF0;
            combiner.Factor0[0] = 0x00ABCDEF;
            combiner.Factor1[4] = 0x00123456;
            combiner.SpecFogFactor[1] = 0x00777777;
            combiner.Control = 0x00000003;
            combiner.StageProgram = 0x00008421;
            combiner.DotRgbMapping = 0x00000123;
            combiner.OtherStageInput = 0x00100000;
            combiner.Final0 = 0x12345678;
            combiner.Final1 = 0x9ABCDEF0;
        }
    }
}
