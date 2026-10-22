using System;

namespace XGrid.Nv2a;

/// <summary>
/// Present-upload pixel builder (no GL calls): converts the guest-RAM color
/// surface at a flip record's address into bottom-up RGBA bytes ready for a
/// <c>glTexImage2D</c> full-frame replace. The walk mirrors
/// <see cref="Nv2AMmio.TryCopyPresentedFrame"/> exactly (guest row stride =
/// pitch, A8R8G8B8 dword <c>0xAARRGGBB</c>), including xemu's pitch-repack
/// rule (reference <c>gl/surface.c</c> <c>pgraph_gl_upload_surface_data</c>:
/// rows are repacked to <c>width * bytes_per_pixel</c> when the guest pitch
/// is wider — facts only). Row order is guest order (row 0 first): the
/// backend's FBO convention puts guest y=0 at the FBO bottom and texture
/// row 0 uploads to the texture bottom, so no vertical flip is needed for
/// the readback path to serve guest row 0 first.
/// </summary>
public static class PresentUpload
{
    private const uint ColorBytes32 = 4u;
    private const uint MaxPresentDimension = 4096u;

    /// <summary>
    /// Builds the upload buffer for one presented surface.
    /// </summary>
    /// <returns>
    /// False when the record cannot describe an uploadable surface (non-4-byte
    /// format, zero/oversize dimensions, zero pitch, pitch narrower than a row,
    /// or a row span that wraps the 32-bit address space). No exception for
    /// guest-state reasons; production <see cref="IGpuMemory"/> reads are total
    /// (unmapped addresses return benign values), so unlike the software path
    /// there is no RAM-size check — the caller passes no size and this helper
    /// takes none.
    /// </returns>
    public static bool TryBuildUploadPixels(
        IGpuMemory memory,
        uint surfaceAddress,
        uint width,
        uint height,
        uint pitch,
        uint colorBytes,
        out byte[]? rgba)
    {
        ArgumentNullException.ThrowIfNull(memory);
        rgba = null;
        if (colorBytes != ColorBytes32)
        {
            return false;
        }

        if (width == 0 || height == 0 || width > MaxPresentDimension || height > MaxPresentDimension)
        {
            return false;
        }

        if (pitch == 0)
        {
            return false;
        }

        ulong rowBytes = (ulong)width * colorBytes;
        if (pitch < rowBytes)
        {
            return false;
        }

        ulong count = (ulong)width * height;

        // The whole walk must stay inside the 32-bit address space (same
        // overflow discipline as the software copy's RamSize guard, minus the
        // size itself, which this seam does not carry).
        ulong span = ((ulong)height - 1u) * pitch;
        if (span > uint.MaxValue || surfaceAddress + span + (rowBytes - 1u) > uint.MaxValue)
        {
            return false;
        }

        byte[] pixels = new byte[count * 4];
        for (uint y = 0; y < height; y++)
        {
            uint row = surfaceAddress + (uint)((ulong)y * pitch);
            for (uint x = 0; x < width; x++)
            {
                uint pixel = memory.ReadPhysical(row + (x * colorBytes));
                int at = (int)(((ulong)y * width * 4u) + (x * 4u));
                pixels[at] = (byte)(pixel >> 16);
                pixels[at + 1] = (byte)(pixel >> 8);
                pixels[at + 2] = (byte)pixel;
                pixels[at + 3] = (byte)(pixel >> 24);
            }
        }

        rgba = pixels;
        return true;
    }
}
