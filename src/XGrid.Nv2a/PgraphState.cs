using System;

namespace XGrid.Nv2a;

/// <summary>
/// The PGRAPH state type the NV097 methods latch (XFP1). It owns the texture
/// register file, the transform program and constant contexts, and the register
/// combiner; <see cref="Nv2AMmio"/> keeps the decode and reaches the same
/// storage through delegating properties, so behavior and the snapshot field
/// order are unchanged. Later sections add the viewport/clip and surface state.
/// </summary>
internal sealed class PgraphState
{
    /// <summary>Initializes the state with the reference's fixed row counts.</summary>
    /// <param name="transformProgramLength">The transform-program row count.</param>
    /// <param name="transformConstantCount">The transform-constant row count.</param>
    public PgraphState(int transformProgramLength, int transformConstantCount)
    {
        TransformProgram = new uint[transformProgramLength][];
        for (int i = 0; i < TransformProgram.Length; i++)
        {
            TransformProgram[i] = new uint[4];
        }

        TransformConstants = new uint[transformConstantCount][];
        for (int i = 0; i < TransformConstants.Length; i++)
        {
            TransformConstants[i] = new uint[4];
        }
    }

    /// <summary>Gets the four texture slots and the bump-environment state.</summary>
    public PgraphTextureState Textures { get; } = new();

    /// <summary>Gets the transform-program rows (four dwords each).</summary>
    public uint[][] TransformProgram { get; }

    /// <summary>Gets the transform-constant rows (four dwords each).</summary>
    public uint[][] TransformConstants { get; }

    /// <summary>Gets the fragment register combiner.</summary>
    public PshCombiner Combiner { get; } = new();

    // ReSharper disable UnassignedField.Compiler
    // Nv2AMmio writes these six through ref-returning aliases
    // (`private ref uint _surfaceFormat => ref _state.SurfaceFormat;`), which is
    // how the ported register file keeps one storage location per NV097 method.
    // The analyzer does not follow a write through a ref return, so it reports
    // them as never assigned. They are assigned on the method path and read by
    // the draw/clear decode.

    /// <summary>The surface format word (NV097_SET_SURFACE_FORMAT).</summary>
    public uint SurfaceFormat;

    /// <summary>The surface pitch word (NV097_SET_SURFACE_PITCH).</summary>
    public uint SurfacePitch;

    /// <summary>The surface clip horizontal word (x/y packed).</summary>
    public uint SurfaceClipH;

    /// <summary>The surface clip vertical word (y/height packed).</summary>
    public uint SurfaceClipV;

    /// <summary>The colour surface base offset latched from the DMA context.</summary>
    public uint ColorSurfaceBase;

    /// <summary>The zeta surface base offset latched from the DMA context.</summary>
    public uint ZetaSurfaceBase;

    // ReSharper restore UnassignedField.Compiler
}

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
