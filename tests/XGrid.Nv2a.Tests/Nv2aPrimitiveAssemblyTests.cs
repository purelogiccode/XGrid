using System;
using System.Collections.Generic;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// XFP2: primitive assembly differential. Drives SET_BEGIN_END, DRAW_ARRAYS
/// (NV097's packed start/count), consecutive-range coalescing and the
/// draw-command boundary through the real pusher and compares the counters and
/// pixels with the reference semantics.
/// </summary>
public sealed class Nv2aPrimitiveAssemblyTests
{
    private const uint Base = 0xFD000000;
    private const uint BeginEndTriangles = 5u;
    private const uint End = 0u;

    [Fact]
    public void DrawArraysDrawsTheRequestedVertexCount()
    {
        // Four vertices with TRIANGLES = one triangle; the count field is
        // count - 1 (NV097 packs the draw as start | (count - 1) << 24).
        (Nv2AMmio one, ArrayMemory oneMemory) = CreateChannel();
        WriteVertices(oneMemory, 4);
        Kick(one, oneMemory, PushFrame(one, oneMemory, 0, DrawArrays(start: 0, count: 3)));

        (Nv2AMmio two, ArrayMemory twoMemory) = CreateChannel();
        WriteVertices(twoMemory, 6);
        Kick(two, twoMemory, PushFrame(two, twoMemory, 0, DrawArrays(start: 0, count: 6)));

        Assert.Equal(1UL, one.DrawsExecuted);
        Assert.Equal(1UL, two.DrawsExecuted);
        Assert.True(one.DrawnPixels > 0);
        Assert.True(two.DrawnPixels > one.DrawnPixels);
    }

    [Fact]
    public void ConsecutiveDrawArraysRangesAreCoalesced()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteVertices(memory, 6);
        Kick(nv2a, memory, PushFrame(
            nv2a, memory, 0, DrawArrays(start: 0, count: 3), DrawArrays(start: 3, count: 3)));

        Assert.Equal(1, nv2a.DrawArrayRangesSubmitted);
        Assert.Equal(1UL, nv2a.DrawsExecuted);
        Assert.True(nv2a.DrawnPixels > 0);
    }

    [Fact]
    public void NonConsecutiveDrawArraysRangesStaySeparate()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteVertices(memory, 8);
        Kick(nv2a, memory, PushFrame(
            nv2a, memory, 0, DrawArrays(start: 0, count: 3), DrawArrays(start: 6, count: 3)));

        Assert.Equal(2, nv2a.DrawArrayRangesSubmitted);
        Assert.Equal(1UL, nv2a.DrawsExecuted);
    }

    [Fact]
    public void EachBeginEndPairIsItsOwnDrawCommand()
    {
        (Nv2AMmio nv2a, ArrayMemory memory) = CreateChannel();
        WriteVertices(memory, 3);
        uint address = PushFrame(nv2a, memory, 0, DrawArrays(start: 0, count: 3));
        address = PushFrame(nv2a, memory, address, DrawArrays(start: 0, count: 3));
        Kick(nv2a, memory, address);

        Assert.Equal(2UL, nv2a.DrawsExecuted);
        Assert.True(nv2a.DrawnPixels > 0);
    }

    private static uint DrawArrays(int start, int count)
    {
        return (uint)(start & 0x00FFFFFF) | ((uint)(count - 1) << 24);
    }

    private static (Nv2AMmio Nv2a, ArrayMemory Memory) CreateChannel()
    {
        var memory = new ArrayMemory(0x20000);
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

    private static void Kick(Nv2AMmio nv2A, ArrayMemory memory, uint endAddress)
    {
        memory.WritePhysical(endAddress, 0x00001001u);
        nv2A.Write(Base + 0x800040u, 4, endAddress + 4);
    }

    /// <summary>Writes axis-aligned triangles at 0x2000 (F32x4 x/y/z/w), two pixels apart.</summary>
    private static void WriteVertices(ArrayMemory memory, int count)
    {
        float[][] offsets = [[2f, 2f], [10f, 2f], [2f, 10f]];
        for (int i = 0; i < count; i++)
        {
            int group = i / 3;
            uint at = 0x2000u + ((uint)i * 16u);
            memory.WritePhysical(at, BitConverter.SingleToUInt32Bits(offsets[i % 3][0] + (group * 12f)));
            memory.WritePhysical(at + 4u, BitConverter.SingleToUInt32Bits(offsets[i % 3][1]));
            memory.WritePhysical(at + 8u, 0u);
            memory.WritePhysical(at + 12u, BitConverter.SingleToUInt32Bits(1f));
        }
    }

    /// <summary>Appends one BEGIN/draw list/END frame; returns the next pushbuffer address.</summary>
    private static uint PushFrame(Nv2AMmio nv2A, ArrayMemory memory, uint address, params uint[] drawParameters)
    {
        var pushes = new List<(uint Method, uint Parameter)>
        {
            // 16x16 A8R8G8B8 surface at 0x3000 (pitch 0x40).
            (0x0208u, 0x00000104u),
            (0x020Cu, 0x00000040u),
            (0x0210u, 0x00003000u),
            (0x0200u, 0x00100000u),
            (0x0204u, 0x00100000u),
            (0x1720u, 0x00002000u),
            (0x1760u, 0x00001042u),
            // Position program: oPos = v0.
            (0x1E9Cu, 0u),
            (0x0B00u, 0u),
            (0x0B04u, 0x0020001Bu),
            (0x0B08u, 0x0836006Cu),
            (0x0B0Cu, 0x0000F800u),
            (0x0B10u, 0u),
            (0x0B14u, 0x0020001Bu),
            (0x0B18u, 0x0C36006Cu),
            (0x0B1Cu, 0x0000F819u),
            (0x1EA4u, 0u),
            (0x0B80u, 0x3F800000u),
            (0x0B84u, 0x3F000000u),
            (0x0B88u, 0x3E800000u),
            (0x0B8Cu, 0x3F800000u),
            (0x17FCu, BeginEndTriangles)
        };
        foreach (uint draw in drawParameters)
        {
            pushes.Add((0x1810u, draw));
        }

        pushes.Add((0x17FCu, End));
        foreach ((uint method, uint parameter) in pushes)
        {
            memory.WritePhysical(address, (1u << 18) | method);
            memory.WritePhysical(address + 4, parameter);
            address += 8;
        }

        return address;
    }

    private sealed class ArrayMemory(int size = 0x10000) : IGpuMemory
    {
        private readonly byte[] _data = new byte[size];

        public void WritePhysical(uint address, uint value)
        {
            int at = (int)address;
            _data[at] = (byte)value;
            _data[at + 1] = (byte)(value >> 8);
            _data[at + 2] = (byte)(value >> 16);
            _data[at + 3] = (byte)(value >> 24);
        }

        public uint ReadPhysical(uint address)
        {
            int at = (int)address;
            return (uint)(_data[at] | (_data[at + 1] << 8) | (_data[at + 2] << 16) | (_data[at + 3] << 24));
        }
    }
}
