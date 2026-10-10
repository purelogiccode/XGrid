using System;
using System.IO;
using Xunit;

namespace XGrid.Graphics.Tests;

public sealed class DisplayFrameTests
{
    [Fact]
    public void Show_StoresDimensions()
    {
        var frame = new DisplayFrame();

        frame.Show(2u, 1u, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.True(frame.HasFrame);
        Assert.Equal(2u, frame.Width);
        Assert.Equal(1u, frame.Height);
        Assert.Equal(2u, frame.CountNonZeroPixels());
    }

    [Fact]
    public void Show_RejectsMismatchedData()
    {
        var frame = new DisplayFrame();

        Assert.Throws<ArgumentException>(() => frame.Show(2u, 2u, new byte[12]));
    }

    [Fact]
    public void SaveBmp_WritesBottomUpBitmap()
    {
        var frame = new DisplayFrame();
        frame.Show(2u, 2u, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bmp");

        try
        {
            frame.SaveBmp(path);

            byte[] file = File.ReadAllBytes(path);
            Assert.Equal(54 + 16, file.Length);
            Assert.Equal((byte)'B', file[0]);
            Assert.Equal((byte)'M', file[1]);
            Assert.Equal(2, BitConverter.ToInt32(file, 18));
            Assert.Equal(2, BitConverter.ToInt32(file, 22));
            Assert.Equal(
                new byte[] { 9, 10, 11, 12, 13, 14, 15, 16, 1, 2, 3, 4, 5, 6, 7, 8 },
                file[54..]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveBmp_WithoutFrameThrows()
    {
        var frame = new DisplayFrame();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bmp");

        Assert.Throws<InvalidOperationException>(() => frame.SaveBmp(path));
    }
}
