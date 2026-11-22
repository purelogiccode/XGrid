using System;

namespace XGrid.Cpu.Tcg;

/// <summary>
/// The MMX register file the IR interpreter (P1S3) and the host code generator
/// (P1S4) operate on. The hardware aliases MMX onto the x87 register stack:
/// <c>mm0..7</c> are the low 64 bits of <c>ST(Top+i)</c> and an MMX write marks
/// the physical tag valid (<see cref="FpuState"/>'s rule), while EMMS empties
/// every tag.
/// </summary>
public sealed class TcgMmxState
{
    private readonly ulong[] _physical = new ulong[8];

    /// <summary>Initializes a new instance of the <see cref="TcgMmxState"/> class (all tags empty).</summary>
    public TcgMmxState()
    {
        TagWord = 0xFFFF;
    }

    /// <summary>Gets or sets the x87 top-of-stack index.</summary>
    public int Top { get; set; }

    /// <summary>Gets or sets the physical x87 tag word (two bits per register).</summary>
    public ushort TagWord { get; set; }

    /// <summary>Reads a logical MMX register (<c>ST(Top+index)</c>'s significand).</summary>
    /// <param name="index">The MMX register index (0-7).</param>
    /// <returns>The 64-bit value.</returns>
    public ulong Read(int index)
    {
        return _physical[(Top + index) & 7];
    }

    /// <summary>Writes a logical MMX register and marks its physical tag valid.</summary>
    /// <param name="index">The MMX register index (0-7).</param>
    /// <param name="value">The 64-bit value.</param>
    public void Write(int index, ulong value)
    {
        int physical = (Top + index) & 7;
        _physical[physical] = value;
        TagWord = (ushort)(TagWord & ~(3 << (physical * 2)));
    }

    /// <summary>Empties every tag (EMMS).</summary>
    public void Empty()
    {
        TagWord = 0xFFFF;
    }

    /// <summary>Builds the state from the real CPU's x87 stack.</summary>
    /// <param name="fpu">The FPU state.</param>
    /// <returns>A state seeded with the physical registers and tags.</returns>
    public static TcgMmxState FromFpu(FpuState fpu)
    {
        ArgumentNullException.ThrowIfNull(fpu);
        var state = new TcgMmxState { Top = fpu.Top, TagWord = fpu.TagWord };
        for (int i = 0; i < 8; i++)
        {
            state._physical[i] = fpu.GetPhysicalRegister(i).Significand;
        }

        return state;
    }
}
