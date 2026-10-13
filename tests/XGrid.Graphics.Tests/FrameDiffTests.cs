using System;
using Xunit;

namespace XGrid.Graphics.Tests;

/// <summary>Golden-frame diff policy tests (design doc §3.4).</summary>
public sealed class FrameDiffTests
{
    [Fact]
    public void IdenticalFrames_Pass()
    {
        byte[] frame = FlatFrame(4, 4, 0x11, 0x22, 0x33, 0xFF);
        FrameDiff.Result result = FrameDiff.Compare(frame, frame, 4, 4);
        Assert.True(result.Passed);
        Assert.Equal(0, result.DiffPixels);
        Assert.Equal(0, result.Violations);
        Assert.Equal(0, result.MaxChannelDelta);
    }

    [Fact]
    public void FlatRegionByteExact_Required()
    {
        byte[] reference = FlatFrame(8, 8, 0x10, 0x20, 0x30, 0xFF);
        byte[] test = (byte[])reference.Clone();
        test[(3 * 8 * 4) + (4 * 4)] = 0x11; // single interior pixel, delta 1, flat region
        FrameDiff.Result result = FrameDiff.Compare(reference, test, 8, 8);
        Assert.Equal(1, result.DiffPixels);
        Assert.False(result.Passed);
        Assert.Equal(1, result.Violations);
    }

    [Fact]
    public void EdgeBandDeltaWithin2_Passes()
    {
        // Vertical edge at x=4: left half dark, right half bright in BOTH
        // frames; a ±1 band pixel may shift by up to 2 per channel.
        byte[] reference = TwoToneFrame(8, 8, edgeX: 4, left: 0x20, right: 0xC0);
        byte[] test = (byte[])reference.Clone();
        int p = ((2 * 8) + 4) * 4; // pixel at the edge
        test[p] = (byte)(reference[p] + 2);
        test[p + 1] = (byte)(reference[p + 1] - 2);
        FrameDiff.Result result = FrameDiff.Compare(reference, test, 8, 8);
        Assert.Equal(1, result.DiffPixels);
        Assert.Equal(1, result.EdgeBandPixels);
        Assert.True(result.Passed, result.ToString());
        Assert.Equal(2, result.MaxChannelDelta);
    }

    [Fact]
    public void EdgeBandDeltaAbove2_Fails()
    {
        byte[] reference = TwoToneFrame(8, 8, edgeX: 4, left: 0x20, right: 0xC0);
        byte[] test = (byte[])reference.Clone();
        int p = ((2 * 8) + 4) * 4;
        test[p] = (byte)(reference[p] + 3);
        FrameDiff.Result result = FrameDiff.Compare(reference, test, 8, 8);
        Assert.False(result.Passed);
        Assert.Equal(1, result.Violations);
        Assert.Equal(3, result.MaxChannelDelta);
    }

    [Fact]
    public void OutsideEdgeBand_AnyDeltaFails()
    {
        byte[] reference = TwoToneFrame(8, 8, edgeX: 4, left: 0x20, right: 0xC0);
        byte[] test = (byte[])reference.Clone();
        int p = ((2 * 8) + 0) * 4; // deep inside the flat left half
        test[p] = 0x21;
        FrameDiff.Result result = FrameDiff.Compare(reference, test, 8, 8);
        Assert.False(result.Passed);
        Assert.Equal(1, result.Violations);
        Assert.Equal(0, result.EdgeBandPixels);
    }

    [Fact]
    public void SizeMismatch_Throws()
    {
        byte[] reference = FlatFrame(4, 4, 1, 2, 3, 4);
        byte[] shortFrame = new byte[16];
        Assert.Throws<ArgumentException>(() => FrameDiff.Compare(reference, shortFrame, 4, 4));
    }

    private static byte[] FlatFrame(int width, int height, byte r, byte g, byte b, byte a)
    {
        var frame = new byte[width * height * 4];
        for (int i = 0; i < frame.Length; i += 4)
        {
            frame[i] = b;
            frame[i + 1] = g;
            frame[i + 2] = r;
            frame[i + 3] = a;
        }

        return frame;
    }

    private static byte[] TwoToneFrame(int width, int height, int edgeX, byte left, byte right)
    {
        var frame = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte v = x < edgeX ? left : right;
                int p = ((y * width) + x) * 4;
                frame[p] = v;
                frame[p + 1] = v;
                frame[p + 2] = v;
                frame[p + 3] = 0xFF;
            }
        }

        return frame;
    }
}
