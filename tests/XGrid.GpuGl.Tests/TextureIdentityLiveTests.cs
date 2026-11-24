using System;
using System.Globalization;
using XGrid.Memory;
using XGrid.Nv2a;
using Xunit;
using Xunit.Abstractions;

namespace XGrid.GpuGl.Tests;

/// <summary>
/// XFP4 texture-cache content identity: the reference keys a binding on the
/// whole <c>TextureShape</c> and hashes the whole guest range, re-uploading
/// only when the content actually changed. The backend now uses a
/// page-generation write stamp (<see cref="IGuestWriteStamp"/>) as the dirty
/// test and a whole-range FNV hash as the identity. The old 16-texel
/// fingerprint only sampled the anti-diagonal of a 4x4 texture
/// ((1,3), (2,0), (3,1), (0,2)), so a change to texel (0,0) went unnoticed.
/// </summary>
public sealed class TextureIdentityLiveTests(ITestOutputHelper output)
{
    private const uint TextureBase = 0x4000u;

    // 4x4 A8R8G8B8, swizzled (log2 2 x 2).
    private const uint TextureFormat = (0x06u << 8) | (2u << 20) | (2u << 24);

    private sealed class StampMemory : IGpuMemory, IGuestWriteStamp
    {
        private readonly byte[] _data = new byte[0x10000];
        private ulong _stamp = 1;

        public void WritePhysical(uint address, uint value)
        {
            _data[address] = (byte)value;
            _data[address + 1] = (byte)(value >> 8);
            _data[address + 2] = (byte)(value >> 16);
            _data[address + 3] = (byte)(value >> 24);
            _stamp++;
        }

        public uint ReadPhysical(uint address)
        {
            return (uint)(_data[address] | (_data[address + 1] << 8) | (_data[address + 2] << 16) |
                          (_data[address + 3] << 24));
        }

        public ulong GetWriteStamp(uint address, uint length)
        {
            return _stamp;
        }

        public void NoteDirectWrite(uint address, uint length)
        {
            _stamp++;
        }
    }

    private static PgraphVertex Vertex(float x, float y, float u, float v)
    {
        return new PgraphVertex(
            x, y, 0.5f, 1f,
            1f, 1f, 1f, 1f,
            0f, 0f, 0f, 0f,
            u, v, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            0f, 0f, 0f,
            1f, 1f, 1f, 1f,
            0f);
    }

    private static PgraphFragmentState Fragment()
    {
        return new PgraphFragmentState(
            alphaTestEnable: false, alphaFunc: 0u, alphaRef: 0u,
            blendEnable: false, blendSrcFactor: 1u, blendDstFactor: 0u, blendColor: 0u, blendEquation: 0u,
            depthTestEnable: false, depthFunc: 1u, depthMask: false,
            colorMask: 0x01010101u,
            stencilTestEnable: false, stencilWriteMask: 0u, stencilFunc: 1u, stencilRef: 0u,
            stencilReadMask: 0u, stencilOpFail: 0x1E00u, stencilOpZfail: 0x1E00u, stencilOpZpass: 0x1E00u,
            zclipActive: false, zclipMin: 0f, zclipMax: 1f, zclipCull: false);
    }

    private static PgraphDrawInfo Draw()
    {
        var slot = new PgraphTextureSlot(
            offset: TextureBase,
            format: TextureFormat,
            address: 0x00010101u,
            control0: 1u << 30,
            control1: 0u,
            imageRect: 0x00040004u);
        return new PgraphDrawInfo(
            Vertex(0f, 0f, 0f, 0f), Vertex(16f, 0f, 1f, 0f), Vertex(0f, 16f, 0f, 1f),
            0u, 0u, 16u, 16u,
            colorBytes: 4u, zetaBytes: 0u,
            combinerActive: false,
            Fragment(),
            slot, slot, slot, slot,
            new PshCombiner(),
            fogEnable: false, fogR: 0f, fogG: 0f, fogB: 0f, fogA: 0f,
            shaderClipMode: 0u,
            zFormatFloat: false,
            colorSurfaceAddress: 0u,
            colorPitch: 0u);
    }

    private static int Uploads(string summary)
    {
        int at = summary.IndexOf("uploads=", StringComparison.Ordinal);
        Assert.True(at >= 0, summary);
        at += "uploads=".Length;
        int end = at;
        while (end < summary.Length && char.IsAsciiDigit(summary[end]))
        {
            end++;
        }

        return int.Parse(summary[at..end], CultureInfo.InvariantCulture);
    }

    private bool TryCreateBackend(StampMemory memory, out IPgraphBackend backend)
    {
        bool created = GlBackendFactory.TryCreate(memory, out IPgraphBackend? createdBackend, out string why);
        if (!created || createdBackend is null)
        {
            output.WriteLine("no GL context (" + why + "); live row not exercised.");
            backend = null!;
            return false;
        }

        backend = createdBackend;
        return true;
    }

    private static void SeedTexture(StampMemory memory)
    {
        for (uint word = 0; word < 16u; word++)
        {
            memory.WritePhysical(TextureBase + (word * 4u), 0xFF000000u | word);
        }
    }

    [Fact]
    public void TextureContentChangeOffTheOldSampleGrid_Reuploads()
    {
        var memory = new StampMemory();
        if (!TryCreateBackend(memory, out IPgraphBackend backend))
        {
            return;
        }

        try
        {
            SeedTexture(memory);
            Assert.True(backend.TryDraw(Draw()), "draw declined: " + backend.DebugSummary);
            int first = Uploads(backend.DebugSummary);
            Assert.True(first >= 1, backend.DebugSummary);

            // Unchanged content: the stamp did not move, so the binding is
            // served without a hash or an upload.
            Assert.True(backend.TryDraw(Draw()), "second draw declined");
            Assert.Equal(first, Uploads(backend.DebugSummary));

            // Change ONLY texel (0,0), which the old 16-sample fingerprint
            // never sampled on a 4x4 texture. The write bumps the page
            // generation, the hash changes, the entry re-uploads.
            memory.WritePhysical(TextureBase, 0xFF00FF00u);
            Assert.True(backend.TryDraw(Draw()), "third draw declined");
            Assert.Equal(first + 1, Uploads(backend.DebugSummary));
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void UnrelatedWrite_HashesButDoesNotReupload()
    {
        var memory = new StampMemory();
        if (!TryCreateBackend(memory, out IPgraphBackend backend))
        {
            return;
        }

        try
        {
            SeedTexture(memory);
            Assert.True(backend.TryDraw(Draw()), "draw declined");
            int first = Uploads(backend.DebugSummary);

            // A write elsewhere bumps the stamp (the dirty test cannot tell
            // which pages), so the range is re-hashed - and the hash says the
            // texture is unchanged, so no upload happens.
            memory.WritePhysical(0x8000u, 0xDEADBEEFu);
            Assert.True(backend.TryDraw(Draw()), "second draw declined");
            Assert.Equal(first, Uploads(backend.DebugSummary));
        }
        finally
        {
            (backend as IDisposable)?.Dispose();
        }
    }
}
