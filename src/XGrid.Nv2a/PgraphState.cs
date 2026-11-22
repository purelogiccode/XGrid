using System;

namespace XGrid.Nv2a;

/// <summary>
/// The PGRAPH register file the NV097 methods latch (XFP1). This first section
/// owns the four texture slots (xemu <c>pgraph.h</c>'s texture context) and the
/// bump-environment matrices; it is the state type <see cref="Nv2AMmio"/> owns.
/// The arrays are the same storage the device already used, so the decode, the
/// snapshot field order and every gate anchor are unchanged by the move.
/// </summary>
internal sealed class PgraphTextureState
{
    /// <summary>The number of texture slots (xemu's four texture contexts).</summary>
    public const int Slots = 4;

    /// <summary>The number of bump-environment stages (slots 1-3).</summary>
    public const int BumpStages = Slots - 1;

    /// <summary>Gets the per-slot texture base offset (NV097_SET_TEXTURE_OFFSET).</summary>
    public uint[] Offset { get; } = new uint[Slots];

    /// <summary>Gets the per-slot texture format word (NV097_SET_TEXTURE_FORMAT).</summary>
    public uint[] Format { get; } = new uint[Slots];

    /// <summary>Gets the per-slot texture address/control word (NV097_SET_TEXTURE_ADDRESS).</summary>
    public uint[] Address { get; } = new uint[Slots];

    /// <summary>Gets the per-slot texture control 0 word.</summary>
    public uint[] Control0 { get; } = new uint[Slots];

    /// <summary>Gets the per-slot texture control 1 word.</summary>
    public uint[] Control1 { get; } = new uint[Slots];

    /// <summary>Gets the per-slot texture filter word (NV097_SET_TEXTURE_FILTER).</summary>
    public uint[] Filter { get; } = new uint[Slots];

    /// <summary>Gets the per-slot texture image rectangle.</summary>
    public uint[] ImageRect { get; } = new uint[Slots];

    /// <summary>Gets the per-slot border color.</summary>
    public uint[] BorderColor { get; } = new uint[Slots];

    /// <summary>Gets the per-slot palette word (DMA select, length, offset).</summary>
    public uint[] Palette { get; } = new uint[Slots];

    /// <summary>Gets the bump-environment matrix per stage (slots 1-3).</summary>
    public float[][] BumpMatrices { get; } = new float[BumpStages][];

    /// <summary>Gets the bump-environment scale per stage.</summary>
    public float[] BumpScales { get; } = new float[BumpStages];

    /// <summary>Gets the bump-environment offset per stage.</summary>
    public float[] BumpOffsets { get; } = new float[BumpStages];

    /// <summary>Clears every texture register and bump state (device reset).</summary>
    public void Clear()
    {
        Array.Clear(Offset);
        Array.Clear(Format);
        Array.Clear(Address);
        Array.Clear(Control0);
        Array.Clear(Control1);
        Array.Clear(Filter);
        Array.Clear(ImageRect);
        Array.Clear(BorderColor);
        Array.Clear(Palette);
        foreach (float[] matrix in BumpMatrices)
        {
            if (matrix is not null)
            {
                Array.Clear(matrix);
            }
        }

        Array.Clear(BumpScales);
        Array.Clear(BumpOffsets);
    }
}
