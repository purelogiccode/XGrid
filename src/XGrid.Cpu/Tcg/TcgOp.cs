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

    /// <summary>Adds two temps plus the carry flag.</summary>
    Adc,

    /// <summary>Subtracts two temps and computes the flags for the operand size.</summary>
    Sub,

    /// <summary>Subtracts two temps plus the borrow flag.</summary>
    Sbb,

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

    /// <summary>Logical shift left.</summary>
    Shl,

    /// <summary>Logical shift right.</summary>
    Shr,

    /// <summary>Arithmetic shift right.</summary>
    Sar,

    /// <summary>Rotate left.</summary>
    Rol,

    /// <summary>Rotate right.</summary>
    Ror,

    /// <summary>Rotate left through carry.</summary>
    Rcl,

    /// <summary>Rotate right through carry.</summary>
    Rcr,

    /// <summary>Double-precision shift left (SHLD).</summary>
    Shld,

    /// <summary>Double-precision shift right (SHRD).</summary>
    Shrd,

    /// <summary>Two's-complement negation.</summary>
    Neg,

    /// <summary>Bitwise complement (no flags).</summary>
    Not,

    /// <summary>Zero-extends a byte.</summary>
    Movzx8,

    /// <summary>Zero-extends a word.</summary>
    Movzx16,

    /// <summary>Sign-extends a byte.</summary>
    Movsx8,

    /// <summary>Sign-extends a word.</summary>
    Movsx16,

    /// <summary>Loads EFLAGS from AH (SAHF).</summary>
    Sahf,

    /// <summary>Stores AH from EFLAGS (LAHF).</summary>
    Lahf,

    /// <summary>Unsigned multiply, one-operand form (EDX:EAX = EAX * source).</summary>
    Mul,

    /// <summary>Signed multiply, one-operand form (EDX:EAX = EAX * source).</summary>
    Imul1,

    /// <summary>Signed multiply, two-operand form (dest = dest * source).</summary>
    Imul2,

    /// <summary>Signed multiply, three-operand form (dest = source * immediate).</summary>
    Imul3,

    /// <summary>Unsigned divide (EDX:EAX / source).</summary>
    Div,

    /// <summary>Signed divide (EDX:EAX / source).</summary>
    Idiv,

    /// <summary>Pushes a temp on the stack.</summary>
    Push,

    /// <summary>Pops a temp from the stack.</summary>
    Pop,

    /// <summary>Bit test (BT/BTS/BTR/BTC).</summary>
    BitTest,

    /// <summary>Compare and exchange (CMPXCHG).</summary>
    Cmpxchg,

    /// <summary>Compare and exchange eight bytes (CMPXCHG8B).</summary>
    Cmpxchg8B,

    /// <summary>Exchange and add (XADD).</summary>
    Xadd,

    /// <summary>String operation with optional REP (MOVS/CMPS/STOS/LODS/SCAS).</summary>
    StringOp,

    /// <summary>An MMX operation (packed integer, move, shift or mask).</summary>
    Mmx,

    /// <summary>An x87 instruction (<see cref="TcgFpu"/> helper).</summary>
    Fpu,

    /// <summary>Reads the x87 status word into a temp (FNSTSW AX).</summary>
    ReadFpuStatus,

    /// <summary>An SSE instruction (<see cref="TcgSse"/> helper).</summary>
    Sse,

    /// <summary>Writes the condition result (0/1) into a temp (SETcc).</summary>
    SetCondition,

    /// <summary>Moves the source into the destination temp when the condition holds (CMOVcc).</summary>
    ConditionalMove,

    /// <summary>Byte-swaps the low 32 bits.</summary>
    Bswap,

    /// <summary>Bit scan forward/backward with the reference ZF rule.</summary>
    BitScan,

    /// <summary>Adjusts a selector's RPL (ARPL).</summary>
    Arpl,

    /// <summary>CBW/CWDE/CWD/CDQ.</summary>
    SignExtend,

    /// <summary>XLAT (AL = [segment:(E)BX+AL]).</summary>
    Xlat,

    /// <summary>Sets or clears an EFLAGS bit.</summary>
    FlagOp,

    /// <summary>Reads EFLAGS (with the reserved bit set) into a temp.</summary>
    ReadFlags,

    /// <summary>Writes EFLAGS from a temp (POPF).</summary>
    WriteFlags,

    /// <summary>LOOP/LOOPE/LOOPNE/JECXZ.</summary>
    Loop,

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

/// <summary>The MMX sub-operations carried by <see cref="TcgOpKind.Mmx"/>.</summary>
public enum TcgMmxOp
{
    /// <summary>Copies an MMX register (or its low 32 bits) into a temp.</summary>
    ToTemp,

    /// <summary>Writes a temp into an MMX register (low 32 or all 64 bits).</summary>
    FromTemp,

    /// <summary>Loads 32 or 64 bits from guest memory into a temp.</summary>
    Load,

    /// <summary>Stores 32 or 64 bits from a temp into guest memory.</summary>
    Store,

    /// <summary>A packed binary operation (<c>ComputeMmx</c> semantics).</summary>
    Binary,

    /// <summary>A packed shift by a temp count.</summary>
    Shift,

    /// <summary>Masked byte store through ES:EDI.</summary>
    Maskmovq,

    /// <summary>Inserts a word lane.</summary>
    Pinsrw,

    /// <summary>Extracts a word lane.</summary>
    Pextrw,

    /// <summary>Packs the MMX sign bits into a GPR.</summary>
    Pmovmskb,

    /// <summary>Shuffles four words by an immediate control.</summary>
    Pshufw,

    /// <summary>Empties the x87/MMX tag word.</summary>
    Emms,
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

    /// <summary>Gets whether the destination operand is memory.</summary>
    public bool IsMemory { get; init; }

    /// <summary>Gets the condition (conditional branch).</summary>
    public ConditionCode Condition { get; init; }

    /// <summary>Gets the MMX sub-operation (<see cref="TcgOpKind.Mmx"/>).</summary>
    public TcgMmxOp MmxOp { get; init; }

    /// <summary>Gets the MMX control byte (shuffle, word lane).</summary>
    public uint Control { get; init; }

    /// <summary>Gets the MMX mnemonic (packed binary/shift semantics).</summary>
    public Mnemonic Mnemonic { get; init; }

    /// <summary>Gets whether a flag op sets (true) or clears (false) its bit.</summary>
    public bool On { get; init; }

    /// <summary>Gets the decoded instruction an x87 helper op executes.</summary>
    public DecodedInstruction Instruction { get; init; }
}
