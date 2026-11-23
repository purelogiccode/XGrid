using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace XGrid.Nv2a.Tests;

/// <summary>
/// Pins the G1 texture-format slice: the reference's color-format table
/// (bytes/layout), the Y8/AY8/A8/A8Y8/Y16/R6G5B5 word decoders, the paletted
/// I8 lookup, the S3TC block decoder (differential against xemu 0.8.139
/// <c>s3tc.c</c>, vectors in <c>TestData/s3tc-vectors.txt</c>), the mip-chain
/// layout and the software sampler's new format lanes.
/// </summary>
public sealed class Nv2aTextureFormatTests
{
    private const uint Base = Nv2AMmio.BaseAddress;

    private sealed class ArrayMemory(int size = 0x10000) : IGpuMemory
    {
        private byte[] Bytes { get; } = new byte[size];

        public void WritePhysical(uint address, uint value)
        {
            Bytes[address] = (byte)value;
            Bytes[address + 1] = (byte)(value >> 8);
            Bytes[address + 2] = (byte)(value >> 16);
            Bytes[address + 3] = (byte)(value >> 24);
        }

        public uint ReadPhysical(uint address)
        {
            return (uint)(Bytes[address] | (Bytes[address + 1] << 8) | (Bytes[address + 2] << 16) |
                          (Bytes[address + 3] << 24));
        }

        public void WriteByte(uint address, byte value) => Bytes[address] = value;
    }

    /// <summary>
    /// XFP4: every texture format the reference declares is deliberately
    /// classified - word-decodable colour, paletted, compressed, YUV, or the
    /// depth-as-texture set that the sampler declines.
    /// </summary>
    [Fact]
    public void EveryReferenceTextureFormatIsClassified()
    {
        uint[] wordDecodable =
        [
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
            0x10, 0x11, 0x12, 0x13, 0x17,
            0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, 0x20,
            0x27, 0x28, 0x29, 0x32, 0x35,
            0x3A, 0x3B, 0x3C, 0x3F, 0x40, 0x41,
        ];
        uint[] paletted = [0x0B];
        uint[] compressed = [0x0C, 0x0E, 0x0F];
        uint[] yuv = [0x24, 0x25];
        uint[] depth = [0x2C, 0x2E, 0x2F, 0x30, 0x31];

        foreach (uint color in wordDecodable)
        {
            Assert.True(TextureFormats.IsWordDecodable(color), $"0x{color:X2} should be word-decodable");
        }

        foreach (uint color in paletted)
        {
            Assert.True(TextureFormats.IsPaletted(color), $"0x{color:X2} should be paletted");
        }

        foreach (uint color in compressed)
        {
            Assert.True(TextureFormats.IsCompressed(color), $"0x{color:X2} should be compressed");
        }

        foreach (uint color in yuv)
        {
            Assert.True(TextureFormats.IsYuv(color), $"0x{color:X2} should be YUV");
        }

        foreach (uint color in depth)
        {
            Assert.True(TextureFormats.IsDepth(color), $"0x{color:X2} should be a depth format");
            Assert.False(TextureFormats.IsWordDecodable(color), $"0x{color:X2} is a depth texture and must be declined");
            Assert.False(TextureFormats.IsYuv(color), $"0x{color:X2} is not YUV");
            Assert.False(TextureFormats.IsPaletted(color), $"0x{color:X2} is not paletted");
            Assert.False(TextureFormats.IsCompressed(color), $"0x{color:X2} is not compressed");
        }

        foreach (uint color in wordDecodable.Concat(paletted).Concat(compressed).Concat(yuv))
        {
            Assert.False(TextureFormats.IsDepth(color), $"0x{color:X2} is not a depth format");
        }

        Assert.Empty(wordDecodable.Intersect(paletted));
        Assert.Empty(wordDecodable.Intersect(compressed));
        Assert.Empty(wordDecodable.Intersect(yuv));
        Assert.Empty(paletted.Intersect(compressed));
    }

    [Fact]
    public void DepthTexelDecode_MatchesTheReferenceSampler()
    {
        // The Y16 formats return the normalized 16-bit depth and the shader's
        // 0xFFFF scale (the reference swizzle's green = 0).
        Assert.True(TextureFormats.TryDecodeDepthTexel(0x30u, 0x0000u, out float d, out float m));
        Assert.Equal(0f, d);
        Assert.Equal(65535f, m);
        Assert.True(TextureFormats.TryDecodeDepthTexel(0x2Cu, 0xFFFFu, out d, out m));
        Assert.Equal(1f, d);
        Assert.Equal(65535f, m);
        Assert.True(TextureFormats.TryDecodeDepthTexel(0x30u, 0x8000u, out d, out m));
        Assert.Equal(32768f / 65535f, d);

        // The X8/Y24 pair: the depth is the high 24 bits (the low byte is the
        // stencil) and the scale is 0xFFFFFF. The reference maps the FLOAT id
        // through the same fixed-point entry ("surface format hack").
        Assert.True(TextureFormats.TryDecodeDepthTexel(0x2Eu, 0x12345678u, out d, out m));
        Assert.Equal(0x123456u / 16777215f, d);
        Assert.Equal(16777215f, m);
        Assert.True(TextureFormats.TryDecodeDepthTexel(0x2Fu, 0x12345678u, out d, out m));
        Assert.Equal(0x123456u / 16777215f, d);
        Assert.Equal(16777215f, m);

        // LU_IMAGE_DEPTH_Y16_FLOAT: the GL upload converts the half float to
        // the normalized fixed-point depth, clamping outside [0,1].
        Assert.True(TextureFormats.TryDecodeDepthTexel(0x31u, 0x3800u, out d, out m));
        Assert.Equal(0.5f, d);
        Assert.Equal(511.9375f, m);
        Assert.True(TextureFormats.TryDecodeDepthTexel(0x31u, 0x4000u, out d, out m));
        Assert.Equal(1f, d);
        Assert.True(TextureFormats.TryDecodeDepthTexel(0x31u, 0xBC00u, out d, out m));
        Assert.Equal(0f, d);

        Assert.False(TextureFormats.TryDecodeDepthTexel(0x06u, 0xFFFFFFFFu, out _, out _));
        Assert.Equal(2u, TextureFormats.DepthBytesPerTexel(0x2Cu));
        Assert.Equal(2u, TextureFormats.DepthBytesPerTexel(0x30u));
        Assert.Equal(4u, TextureFormats.DepthBytesPerTexel(0x2Eu));
        Assert.Equal(4u, TextureFormats.DepthBytesPerTexel(0x2Fu));
        Assert.Equal(0u, TextureFormats.DepthBytesPerTexel(0x06u));
    }

    [Fact]
    public void ShadowDepthCompare_MatchesTheGlslOperators()
    {
        // LESS/EQUAL/LEQUAL/GREATER/NOTEQUAL/GEQUAL over a three-value matrix.
        Assert.True(TextureFormats.CompareShadowDepth(1u, 1f, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(1u, 2f, 2f));
        Assert.True(TextureFormats.CompareShadowDepth(2u, 2f, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(2u, 1f, 2f));
        Assert.True(TextureFormats.CompareShadowDepth(3u, 2f, 2f));
        Assert.True(TextureFormats.CompareShadowDepth(3u, 1f, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(3u, 3f, 2f));
        Assert.True(TextureFormats.CompareShadowDepth(4u, 3f, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(4u, 2f, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(5u, 2f, 2f));
        Assert.True(TextureFormats.CompareShadowDepth(5u, 1f, 2f));
        Assert.True(TextureFormats.CompareShadowDepth(6u, 2f, 2f));
        Assert.True(TextureFormats.CompareShadowDepth(6u, 3f, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(6u, 1f, 2f));

        // NaN follows GLSL: only != is true.
        float nan = float.NaN;
        Assert.False(TextureFormats.CompareShadowDepth(1u, nan, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(2u, nan, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(3u, nan, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(4u, nan, 2f));
        Assert.True(TextureFormats.CompareShadowDepth(5u, nan, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(6u, nan, 2f));

        // NEVER (0) and ALWAYS (7) are resolved before the compare.
        Assert.False(TextureFormats.CompareShadowDepth(0u, 1f, 2f));
        Assert.False(TextureFormats.CompareShadowDepth(7u, 1f, 2f));
    }

    [Theory]
    // The 8-bit family.
    [InlineData(0x00u, 1u, false)]
    [InlineData(0x01u, 1u, false)]
    [InlineData(0x0Bu, 1u, false)]
    [InlineData(0x13u, 1u, true)]
    [InlineData(0x19u, 1u, false)]
    [InlineData(0x1Bu, 1u, true)]
    [InlineData(0x1Fu, 1u, true)]
    // The 16-bit family the old table missed.
    [InlineData(0x1Au, 2u, false)]
    [InlineData(0x20u, 2u, true)]
    [InlineData(0x24u, 2u, true)]
    [InlineData(0x25u, 2u, true)]
    [InlineData(0x27u, 2u, false)]
    [InlineData(0x32u, 2u, false)]
    [InlineData(0x35u, 2u, true)]
    // The compressed family reports the decoded texel width (4) like the
    // reference's BasicColorFormatInfo, while IsCompressed routes the sampler
    // to the block path.
    [InlineData(0x0Cu, 4u, false)]
    [InlineData(0x0Eu, 4u, false)]
    [InlineData(0x0Fu, 4u, false)]
    public void ReferenceFormats_ReportTheirWidthAndLayout(uint color, uint bytes, bool linear)
    {
        Assert.Equal(bytes, Nv2AMmio.TextureFormatBytesPerTexel(color));
        Assert.Equal(linear, Nv2AMmio.IsLinearTextureFormat(color));
    }

    [Theory]
    [InlineData(0x0Cu)]
    [InlineData(0x0Eu)]
    [InlineData(0x0Fu)]
    public void CompressedFormats_AreCompressedAndNotWordDecodable(uint color)
    {
        Assert.True(TextureFormats.IsCompressed(color));
        Assert.False(TextureFormats.IsWordDecodable(color));
        Assert.Equal(4u, Nv2AMmio.TextureFormatBytesPerTexel(color));
    }

    [Fact]
    public void UnknownAndDepthFormats_KeepReportingNoTexelWidth()
    {
        // The slice must not turn the sampler into a catch-all: an id with no
        // defined layout keeps counting a format miss, and depth formats stay
        // on the shadow path (0x2F is asserted by the byte-order tests too).
        Assert.Equal(0u, Nv2AMmio.TextureFormatBytesPerTexel(0x3Du));
        Assert.Equal(0u, Nv2AMmio.TextureFormatBytesPerTexel(0x2Fu));
        Assert.Equal(0u, Nv2AMmio.TextureFormatBytesPerTexel(0x7Fu));
    }

    private static float U(byte value) => value / 255f;

    [Fact]
    public void Y8_RepeatsTheByteIntoRgbAndOpaqueAlpha()
    {
        Assert.True(TextureFormats.TryDecodeWord(0x00u, 0x80u, out float r, out float g, out float b, out float a));
        Assert.Equal(U(0x80), r);
        Assert.Equal(U(0x80), g);
        Assert.Equal(U(0x80), b);
        Assert.Equal(1f, a);

        // The LU twin is the same decode.
        Assert.True(TextureFormats.TryDecodeWord(0x13u, 0x80u, out float lr, out _, out _, out _));
        Assert.Equal(r, lr);
    }

    [Fact]
    public void Ay8_PutsTheByteInAllFourChannels()
    {
        Assert.True(TextureFormats.TryDecodeWord(0x01u, 0x40u, out float r, out float g, out float b, out float a));
        Assert.Equal(U(0x40), r);
        Assert.Equal(U(0x40), g);
        Assert.Equal(U(0x40), b);
        Assert.Equal(U(0x40), a);
    }

    [Fact]
    public void A8_IsWhiteWithTheByteAsAlpha()
    {
        Assert.True(TextureFormats.TryDecodeWord(0x19u, 0x20u, out float r, out float g, out float b, out float a));
        Assert.Equal(1f, r);
        Assert.Equal(1f, g);
        Assert.Equal(1f, b);
        Assert.Equal(U(0x20), a);
    }

    [Fact]
    public void A8Y8_ReadsLumaFromTheLowByteAndAlphaFromTheHighByte()
    {
        Assert.True(TextureFormats.TryDecodeWord(0x1Au, 0x1234u, out float r, out float g, out float b, out float a));
        Assert.Equal(U(0x34), r);
        Assert.Equal(U(0x34), g);
        Assert.Equal(U(0x34), b);
        Assert.Equal(U(0x12), a);
    }

    [Fact]
    public void Y16_ReadsGreenFromTheHighByteAndBlueFromTheLowByte()
    {
        Assert.True(TextureFormats.TryDecodeWord(0x32u, 0x1234u, out float r, out float g, out float b, out float a));
        Assert.Equal(1f, r);
        Assert.Equal(U(0x12), g);
        Assert.Equal(U(0x34), b);
        Assert.Equal(1f, a);
    }

    [Theory]
    // Hand-evaluated from pgraph_convert_texture_data's signed conversion:
    // v ^= 0x0210; R = (v & 0xFC00)>>10 * 0x7F/0x3F; G/B = (field * 0xFF/0x1F) - 0x80;
    // sampled as RGB8_SNORM (value / 127).
    [InlineData(0x0000u, 0f, 3f / 127f, 3f / 127f)]
    [InlineData(0xFFFFu, 1f, -5f / 127f, -5f / 127f)]
    [InlineData(0x7FFFu, 62f / 127f, -5f / 127f, -5f / 127f)]
    [InlineData(0x8000u, 64f / 127f, 3f / 127f, 3f / 127f)]
    public void R6G5B5_DecodesTheSignedConversion(uint word, float expectedR, float expectedG, float expectedB)
    {
        Assert.True(TextureFormats.TryDecodeWord(0x27u, word, out float r, out float g, out float b, out float a));
        Assert.Equal(expectedR, r);
        Assert.Equal(expectedG, g);
        Assert.Equal(expectedB, b);
        Assert.Equal(1f, a);
    }

    [Fact]
    public void OldFormats_AreUnchangedByTheNewTable()
    {
        // A8R8G8B8, A4R4G4B4 and the byte-order twin: the switch cases the
        // gates depend on must not move.
        Nv2AMmio.DecodeTextureTexel(0x06u, 0x11223344u, out float r, out float g, out float b, out float a);
        Assert.Equal(U(0x22), r);
        Assert.Equal(U(0x33), g);
        Assert.Equal(U(0x44), b);
        Assert.Equal(U(0x11), a);

        Nv2AMmio.DecodeTextureTexel(0x04u, 0x1234u, out r, out g, out b, out a);
        Assert.Equal(2f / 15f, r);
        Assert.Equal(3f / 15f, g);
        Assert.Equal(4f / 15f, b);
        Assert.Equal(1f / 15f, a);

        Nv2AMmio.DecodeTextureTexel(0x3Fu, 0x11223344u, out r, out g, out b, out a);
        Assert.Equal(U(0x44), r);
        Assert.Equal(U(0x33), g);
        Assert.Equal(U(0x22), b);
        Assert.Equal(U(0x11), a);
    }

    [Fact]
    public void PaletteEntry_DecodesAsAnA8R8G8B8Word()
    {
        TextureFormats.DecodePaletteEntry(5, 0x11223344u, out float r, out float g, out float b, out float a);
        Assert.Equal(U(0x22), r);
        Assert.Equal(U(0x33), g);
        Assert.Equal(U(0x44), b);
        Assert.Equal(U(0x11), a);
    }

    [Fact]
    public void Yuv_MatchesTheReferenceConversion()
    {
        // YUY2, even texel: c = Y - 16 = 65, d = U - 128 = -38, e = V - 128 = 112
        // -> R clips to 255, G to 0, B to 0.
        byte[] window = [0x51, 0x5A, 0x51, 0xF0];
        Assert.True(TextureFormats.TryDecodeYuv(0x24u, window, odd: false, out float r, out float g, out float b));
        Assert.Equal(1f, r);
        Assert.Equal(0f, g);
        Assert.Equal(0f, b);
    }

    [Fact]
    public void S3tcBlocks_MatchTheReferenceDecoder()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "s3tc-vectors.txt");
        int count = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, parts.Length);
            uint color = parts[0].StartsWith("dxt1", StringComparison.Ordinal)
                ? S3tcDecoder.Dxt1ColorFormat
                : parts[0].StartsWith("dxt3", StringComparison.Ordinal)
                    ? S3tcDecoder.Dxt3ColorFormat
                    : S3tcDecoder.Dxt5ColorFormat;
            byte[] block = Convert.FromHexString(parts[1]);
            byte[] expected = Convert.FromHexString(parts[2]);
            Assert.Equal(S3tcDecoder.BlockBytes(color), block.Length);
            Assert.Equal(64, expected.Length);

            byte[] actual = new byte[64];
            Assert.True(S3tcDecoder.TryDecodeBlock(color, block, actual), parts[0]);
            Assert.True(actual.AsSpan().SequenceEqual(expected),
                $"{parts[0]} block {parts[1]} differs: actual={Convert.ToHexString(actual)} " +
                $"expected={Convert.ToHexString(expected)}");

            // The single-texel entry point must be the same decode.
            for (int i = 0; i < 16; i++)
            {
                Assert.True(S3tcDecoder.TryDecodeTexel(color, block, i, out byte r, out byte g, out byte b,
                    out byte a));
                Assert.Equal(expected[(i * 4) + 0], r);
                Assert.Equal(expected[(i * 4) + 1], g);
                Assert.Equal(expected[(i * 4) + 2], b);
                Assert.Equal(expected[(i * 4) + 3], a);
            }

            count++;
        }

        Assert.Equal(16, count);
    }

    [Fact]
    public void MipChainLayout_MatchesTheReferenceLengthLoop()
    {
        // A8R8G8B8 256x256, nine levels: sum(w*h) = 87381 texels * 4.
        Assert.Equal(87381L * 4L, TextureFormats.ChainLength(0x06u, 256, 256, 9));
        Assert.Equal(65536L * 4L, TextureFormats.LevelOffset(0x06u, 256, 256, 1));
        Assert.Equal(4L, TextureFormats.LevelLength(0x06u, 1, 1));

        // DXT1: level 0 is 64x64 blocks * 8 bytes, level 8 is one 4x4 block.
        Assert.Equal(32768L, TextureFormats.LevelLength(0x0Cu, 256, 256));
        Assert.Equal(8192L, TextureFormats.LevelLength(0x0Cu, 128, 128));
        Assert.Equal(8L, TextureFormats.LevelLength(0x0Cu, 1, 1));
        Assert.Equal(43704L, TextureFormats.ChainLength(0x0Cu, 256, 256, 9));

        // The cube face is the chain rounded to the 128-byte alignment.
        Assert.Equal(349568L, TextureFormats.CubeFaceStride(0x06u, 256, 256, 9));
        Assert.Equal(43776L, TextureFormats.CubeFaceStride(0x0Cu, 256, 256, 9));
    }

    // ---- software sampler integration -------------------------------------

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

    private static uint F(float value) => BitConverter.SingleToUInt32Bits(value);

    private static uint[] Insn(int outAddress, bool final = false, int v = 0, int aMux = 2, int constIndex = 0)
    {
        uint t1 = (1u << 21) | ((uint)constIndex << 13) | ((uint)v << 9);
        t1 |= 0x1Bu;
        uint t2 = (uint)aMux << 26;
        t2 |= 0x1Bu << 17;
        t2 |= 1 << 11;
        t2 |= 0x1Bu << 2;
        uint t3 = 0xFu << 12;
        t3 |= 1u << 11;
        t3 |= (uint)outAddress << 3;
        if (final)
        {
            t3 |= 1u;
        }

        return [0u, t1, t2, t3];
    }

    private static void PushMethods(Nv2AMmio nv2A, ArrayMemory memory,
        params (uint Method, uint Parameter)[] methods)
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

    private static void MethodPush(List<(uint Method, uint Parameter)> pushes, uint[] instruction, uint loadBase)
    {
        for (int i = 0; i < 4; i++)
        {
            pushes.Add((loadBase + (uint)(i * 4), instruction[i]));
        }
    }

    /// <summary>
    /// Renders one textured draw with the kind-1 modulate shape (T0 * white
    /// diffuse, SrcAlpha/InvSrcAlpha/Add over a cleared surface) and returns the
    /// first texture state's sample evidence (address, raw texel word, sample
    /// and miss counts). The texture data is written so that every texel is the
    /// same, which keeps the assertion independent of the interpolated
    /// coordinate.
    /// </summary>
    private static (uint Address, uint Texel, long Samples, long Misses) RenderOneStage(uint formatWord,
        int width, int height, uint pitch, uint paletteWord, Action<ArrayMemory> writeTexture)
    {
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        writeTexture(memory);

        memory.WritePhysical(0x2000u, 0x00000000u);
        memory.WritePhysical(0x2004u, 0x00000000u);
        memory.WritePhysical(0x2008u, F(0.5f));
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41000000u);
        memory.WritePhysical(0x2014u, 0x00000000u);
        memory.WritePhysical(0x2018u, F(0.5f));
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x00000000u);
        memory.WritePhysical(0x2024u, 0x41000000u);
        memory.WritePhysical(0x2028u, F(0.5f));
        memory.WritePhysical(0x202Cu, 0x3F800000u);
        for (uint i = 0; i < 3; i++)
        {
            memory.WritePhysical(0x2200u + (i * 8u), F(0.5f));
            memory.WritePhysical(0x2204u + (i * 8u), F(0.5f));
        }

        for (uint i = 0; i < 4; i++)
        {
            memory.WritePhysical(0xB80u + (i * 4u), 0x3F800000u);
        }

        var pushes = new List<(uint Method, uint Parameter)>();
        pushes.Add((0x0208u, 0x00000104u));
        pushes.Add((0x020Cu, 0x00000040u));
        pushes.Add((0x0210u, 0x00003000u));
        pushes.Add((0x0200u, 0x00100000u));
        pushes.Add((0x0204u, 0x00100000u));
        pushes.Add((0x1720u, 0x00002000u));
        pushes.Add((0x1760u, 0x00001042u));
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, Insn(0, v: 0), 0x0B00u);
        MethodPush(pushes, Insn(3, aMux: 3, constIndex: 0), 0x0B10u);
        MethodPush(pushes, Insn(9, final: true, v: 1), 0x0B20u);
        pushes.Add((0x1B00u, 0x4000u));
        pushes.Add((0x1B04u, formatWord));
        pushes.Add((0x1B08u, 0x00010303u));
        pushes.Add((0x1B0Cu, 0x40000000u));
        pushes.Add((0x1B10u, pitch << 16));
        pushes.Add((0x1B1Cu, ((uint)width << 16) | (uint)height));
        if (paletteWord != 0u)
        {
            pushes.Add((0x1B20u, paletteWord));
        }

        pushes.Add((0x1E70u, 0x01u));
        pushes.Add((0x1E60u, 0x01u));
        pushes.Add((0x0AC0u, 0x08040000u));
        pushes.Add((0x0260u, 0x18140000u));
        pushes.Add((0x1E40u, 0x00000C00u));
        pushes.Add((0x0AA0u, 0x00000C00u));
        pushes.Add((0x0288u, 0x0000000Cu));
        pushes.Add((0x028Cu, 0x00001C80u));
        pushes.Add((0x0304u, 1u));
        pushes.Add((0x0344u, 0x0302u));
        pushes.Add((0x0348u, 0x0303u));
        pushes.Add((0x0350u, 0x8006u));
        pushes.Add((0x17FCu, 5u));
        pushes.Add((0x1800u, 0x00010000u));
        pushes.Add((0x1808u, 0x00000002u));
        pushes.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.True(nv2A.TexturedPixels > 0,
            $"the draw must sample the texture (drawn={nv2A.DrawnPixels} samples={nv2A.TextureSampleSuccesses} " +
            $"formats={string.Join(";", nv2A.TextureFormatAttempts)})");
        foreach ((string _, long samples, long _, long _, long misses, uint firstAddress, uint firstTexel, float _,
                     float _, float _, float _) in nv2A.TextureStateStats)
        {
            if (samples > 0)
            {
                return (firstAddress, firstTexel, samples, misses);
            }
        }

        throw new InvalidOperationException("no texture state recorded a sample");
    }

    [Fact]
    public void Sampler_DecodesTwoDifferentFormatsInOneDraw()
    {
        // Multistage: slot 0 is swizzled Y8 (every texel 0x40), slot 1 is
        // swizzled A8 (every texel 0x80). The combiner's two Project2D stages
        // must both sample, and each state records its own raw word.
        (Nv2AMmio nv2A, ArrayMemory memory) = CreateChannel();
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                uint offset = Nv2AMmio.SwizzleOffset(x, y, 4, 4);
                memory.WriteByte(0x4000u + offset, 0x40);
                memory.WriteByte(0x5000u + offset, 0x80);
            }
        }

        memory.WritePhysical(0x2000u, 0x00000000u);
        memory.WritePhysical(0x2004u, 0x00000000u);
        memory.WritePhysical(0x2008u, F(0.5f));
        memory.WritePhysical(0x200Cu, 0x3F800000u);
        memory.WritePhysical(0x2010u, 0x41000000u);
        memory.WritePhysical(0x2014u, 0x00000000u);
        memory.WritePhysical(0x2018u, F(0.5f));
        memory.WritePhysical(0x201Cu, 0x3F800000u);
        memory.WritePhysical(0x2020u, 0x00000000u);
        memory.WritePhysical(0x2024u, 0x41000000u);
        memory.WritePhysical(0x2028u, F(0.5f));
        memory.WritePhysical(0x202Cu, 0x3F800000u);
        for (uint i = 0; i < 3; i++)
        {
            memory.WritePhysical(0x2200u + (i * 8u), F(0.5f));
            memory.WritePhysical(0x2204u + (i * 8u), F(0.5f));
        }

        for (uint i = 0; i < 4; i++)
        {
            memory.WritePhysical(0xB80u + (i * 4u), 0x3F800000u);
        }

        var pushes = new List<(uint Method, uint Parameter)>();
        pushes.Add((0x0208u, 0x00000104u));
        pushes.Add((0x020Cu, 0x00000040u));
        pushes.Add((0x0210u, 0x00003000u));
        pushes.Add((0x0200u, 0x00100000u));
        pushes.Add((0x0204u, 0x00100000u));
        pushes.Add((0x1720u, 0x00002000u));
        pushes.Add((0x1760u, 0x00001042u));
        pushes.Add((0x1724u, 0x00002200u));
        pushes.Add((0x1764u, 0x00000822u));
        pushes.Add((0x1E9Cu, 0u));
        MethodPush(pushes, Insn(0, v: 0), 0x0B00u);
        MethodPush(pushes, Insn(3, aMux: 3, constIndex: 0), 0x0B10u);
        MethodPush(pushes, Insn(9, final: true, v: 1), 0x0B20u);
        pushes.Add((0x1B00u, 0x4000u));
        pushes.Add((0x1B04u, 0x00000000u | (2u << 20) | (2u << 24)));
        pushes.Add((0x1B08u, 0x00010303u));
        pushes.Add((0x1B0Cu, 0x40000000u));
        pushes.Add((0x1B10u, 16u << 16));
        pushes.Add((0x1B1Cu, 0x00040004u));
        pushes.Add((0x1B40u, 0x5000u));
        pushes.Add((0x1B44u, 0x00001900u | (2u << 20) | (2u << 24)));
        pushes.Add((0x1B48u, 0x00010303u));
        pushes.Add((0x1B4Cu, 0x40000000u));
        pushes.Add((0x1B50u, 16u << 16));
        pushes.Add((0x1B5Cu, 0x00040004u));
        pushes.Add((0x1E70u, 0x21u));
        pushes.Add((0x1E60u, 0x01u));
        pushes.Add((0x0AC0u, 0x08090000u));
        pushes.Add((0x0260u, 0x18140000u));
        pushes.Add((0x1E40u, 0x000000C0u));
        pushes.Add((0x0AA0u, 0x00000C00u));
        pushes.Add((0x0288u, 0x000C0C00u));
        pushes.Add((0x028Cu, 0x00002000u));
        pushes.Add((0x17FCu, 5u));
        pushes.Add((0x1800u, 0x00010000u));
        pushes.Add((0x1808u, 0x00000002u));
        pushes.Add((0x17FCu, 0u));
        PushMethods(nv2A, memory, [.. pushes]);

        Assert.True(nv2A.TextureSampleSuccesses > 0);
        Assert.True(nv2A.TextureFormatAttempts.TryGetValue(0x00u, out long y8Attempts) && y8Attempts > 0);
        Assert.True(nv2A.TextureFormatAttempts.TryGetValue(0x19u, out long a8Attempts) && a8Attempts > 0);
        Assert.False(nv2A.TextureFormatMisses.ContainsKey(0x00u));
        Assert.False(nv2A.TextureFormatMisses.ContainsKey(0x19u));
    }

    private static void WriteSwizzledByte(ArrayMemory memory, int x, int y, byte value)
    {
        uint address = Nv2AMmio.SwizzleOffset(x, y, 4, 4);
        memory.WriteByte(0x4000u + address, value);
    }

    [Fact]
    public void Sampler_DecodesY8FromOneBytePerTexel()
    {
        // Swizzled 4x4 Y8, every texel 0x80: the sampled word is the byte and
        // the decoded colour is (128/255, 128/255, 128/255, 1).
        var sample = RenderOneStage(0x00000000u | (2u << 20) | (2u << 24), 4, 4, 16u, 0u,
            memory =>
            {
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        WriteSwizzledByte(memory, x, y, 0x80);
                    }
                }
            });

        Assert.Equal(0x80u, sample.Texel);
        Assert.InRange(sample.Address, 0x4000u, 0x400Fu);
        Assert.Equal(0L, sample.Misses);
        Assert.True(sample.Samples > 0);
    }

    [Fact]
    public void Sampler_DecodesThePalettedI8Format()
    {
        // 4x4 I8, every index 10; palette entry 10 = A=FF, R=A0, G=50, B=0A.
        var sample = RenderOneStage(0x00000B00u | (2u << 20) | (2u << 24), 4, 4, 16u, 0x00006000u,
            memory =>
            {
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        WriteSwizzledByte(memory, x, y, 10);
                    }
                }

                for (uint i = 0; i < 16; i++)
                {
                    memory.WritePhysical(0x6000u + (i * 4u),
                        0xFF000000u | ((i * 16u) << 16) | ((i * 8u) << 8) | i);
                }
            });

        Assert.Equal(0xFFA0500Au, sample.Texel);
        Assert.InRange(sample.Address, 0x4000u, 0x400Fu);
        Assert.Equal(0L, sample.Misses);
    }

    [Fact]
    public void Sampler_DecodesADxt1Block()
    {
        // c0 = red, c1 = blue, every index 0 -> every texel is opaque red. The
        // recorded raw word is the decoded RGBA the block lane produced.
        var sample = RenderOneStage(0x00000C00u | (2u << 20) | (2u << 24), 4, 4, 16u, 0u,
            memory =>
            {
                byte[] block = Convert.FromHexString("00F81F0000000000");
                for (uint i = 0; i < block.Length; i += 4)
                {
                    memory.WritePhysical(0x4000u + i,
                        (uint)(block[i] | (block[i + 1] << 8) | (block[i + 2] << 16) | (block[i + 3] << 24)));
                }
            });

        Assert.Equal(0xFFFF0000u, sample.Texel);
        Assert.Equal(0x4000u, sample.Address);
        Assert.Equal(0L, sample.Misses);
    }

    [Fact]
    public void Sampler_DecodesALinearYuy2Texture()
    {
        // Linear 4x4 YUY2, pitch 8, every pair Y=81 U=90 V=240:
        // c = 65, d = -38, e = 112 -> (255, 0, 0). The raw word is the
        // four-byte window the conversion read.
        var sample = RenderOneStage(0x00002400u, 4, 4, 8u, 0u,
            memory =>
            {
                for (uint y = 0; y < 4; y++)
                {
                    memory.WriteByte(0x4000u + (y * 8u) + 0u, 0x51);
                    memory.WriteByte(0x4000u + (y * 8u) + 1u, 0x5A);
                    memory.WriteByte(0x4000u + (y * 8u) + 2u, 0x51);
                    memory.WriteByte(0x4000u + (y * 8u) + 3u, 0xF0);
                }
            });

        Assert.Equal(0xF0515A51u, sample.Texel);
        Assert.InRange(sample.Address, 0x4000u, 0x401Fu);
        Assert.Equal(0L, sample.Misses);
    }
}
