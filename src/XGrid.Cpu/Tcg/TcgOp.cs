using System.Runtime.InteropServices;

namespace XGrid.Cpu.Tcg;

/// <summary>The managed TCG IR opcodes (phase B of the CPU engine port, P1S3).</summary>
public enum TcgOpKind
{
    /// <summary>Writes an immediate (masked by size) into a temp.</summary>
    MovImmediate,

    /// <summary>Reads a 32-bit guest register into a temp.</summary>
    ReadRegister,

    /// <summary>Reads an 8-bit guest register (AL..BH) into a temp.</summary>
    ReadRegister8,

    /// <summary>Reads a 16-bit guest register into a temp.</summary>
    ReadRegister16,

    /// <summary>Writes a temp into a 32-bit guest register.</summary>
    WriteRegister,

    /// <summary>Writes the low byte of a temp into an 8-bit guest register.</summary>
    WriteRegister8,

    /// <summary>Writes the low 16 bits of a temp into a 16-bit guest register.</summary>
    WriteRegister16,

    /// <summary>Adds two temps and computes the flags for the operand size.</summary>
    Add,

    /// <summary>Subtracts two temps and computes the flags for the operand size.</summary>
    Sub,

    /// <summary>Bitwise AND of two temps, with the logical flags.</summary>
    And,

    /// <summary>Bitwise OR of two temps, with the logical flags.</summary>
    Or,

    /// <summary>Bitwise XOR of two temps, with the logical flags.</summary>
    Xor,

    /// <summary>Subtracts without storing (CMP).</summary>
    Cmp,

    /// <summary>Increments by one (CF preserved).</summary>
    Inc,

    /// <summary>Decrements by one (CF preserved).</summary>
    Dec,

    /// <summary>Computes a guest effective address from the memory operand shape.</summary>
    Lea,

    /// <summary>Loads 1/2/4 bytes from a temp address.</summary>
    Load,

    /// <summary>Stores 1/2/4 bytes to a temp address.</summary>
    Store,

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

    /// <summary>Gets the immediate, displacement or target EIP.</summary>
    public uint Immediate { get; init; }

    /// <summary>Gets the guest register index (GPR operations).</summary>
    public int GuestRegister { get; init; }

    /// <summary>Gets the index guest register (LEA).</summary>
    public int GuestRegister2 { get; init; }

    /// <summary>Gets the index scale (LEA).</summary>
    public int Scale { get; init; }

    /// <summary>Gets the operand size in bytes (1, 2 or 4).</summary>
    public int Size { get; init; }

    /// <summary>Gets the segment register index (LEA).</summary>
    public int Segment { get; init; }

    /// <summary>Gets whether the LEA has a base register.</summary>
    public bool HasBase { get; init; }

    /// <summary>Gets whether the LEA has an index register.</summary>
    public bool HasIndex { get; init; }

    /// <summary>Gets whether the address is 16-bit (LEA).</summary>
    public bool Address16 { get; init; }

    /// <summary>Gets the condition (conditional branch).</summary>
    public ConditionCode Condition { get; init; }
}
