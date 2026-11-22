using System.Runtime.InteropServices;

namespace XGrid.Cpu.Tcg;

/// <summary>The managed TCG IR opcodes (phase B of the CPU engine port, P1S3).</summary>
public enum TcgOpKind
{
    /// <summary>Writes an immediate into a temp.</summary>
    MovImmediate,

    /// <summary>Reads a guest general-purpose register into a temp.</summary>
    ReadRegister,

    /// <summary>Writes a temp into a guest general-purpose register.</summary>
    WriteRegister,

    /// <summary>Adds two temps and computes the arithmetic flags.</summary>
    Add,

    /// <summary>Subtracts two temps and computes the arithmetic flags.</summary>
    Sub,

    /// <summary>Bitwise AND of two temps, with the logical flags.</summary>
    And,

    /// <summary>Bitwise OR of two temps, with the logical flags.</summary>
    Or,

    /// <summary>Bitwise XOR of two temps, with the logical flags.</summary>
    Xor,

    /// <summary>Subtracts without storing (CMP).</summary>
    Cmp,

    /// <summary>Increments a temp by one (preserves CF, like INC).</summary>
    Inc,

    /// <summary>Decrements a temp by one (preserves CF, like DEC).</summary>
    Dec,

    /// <summary>Sets EIP unconditionally and ends the block.</summary>
    SetEip,

    /// <summary>Sets EIP when the condition holds and ends the block; otherwise falls through.</summary>
    ConditionalSetEip,

    /// <summary>Ends the block.</summary>
    Exit,
}

/// <summary>One managed TCG IR operation.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct TcgOp
{
    /// <summary>Gets the opcode.</summary>
    public TcgOpKind Kind { get; init; }

    /// <summary>Gets the destination temp index.</summary>
    public int Dest { get; init; }

    /// <summary>Gets the left temp index.</summary>
    public int Left { get; init; }

    /// <summary>Gets the right temp index.</summary>
    public int Right { get; init; }

    /// <summary>Gets the immediate or target EIP.</summary>
    public uint Immediate { get; init; }

    /// <summary>Gets the guest register index (GPR operations).</summary>
    public int GuestRegister { get; init; }

    /// <summary>Gets the condition (conditional branch).</summary>
    public ConditionCode Condition { get; init; }
}
