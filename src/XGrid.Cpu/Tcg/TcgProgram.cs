using System;
using System.Collections.Generic;

namespace XGrid.Cpu.Tcg;

/// <summary>An immutable managed TCG program for one guest instruction or block.</summary>
public sealed class TcgProgram
{
    internal TcgProgram(TcgOp[] ops, int tempCount, uint startEip, int byteLength)
    {
        Ops = ops;
        TempCount = tempCount;
        StartEip = startEip;
        ByteLength = byteLength;
    }

    /// <summary>Gets the operations in execution order.</summary>
    public IReadOnlyList<TcgOp> Ops { get; }

    /// <summary>Gets the number of temps the program uses.</summary>
    public int TempCount { get; }

    /// <summary>Gets the guest EIP the program starts at.</summary>
    public uint StartEip { get; }

    /// <summary>Gets the number of guest bytes the program covers.</summary>
    public int ByteLength { get; }
}

/// <summary>Builds a managed TCG program (the IR side of the P1S3 port).</summary>
public sealed class TcgBuilder
{
    private readonly List<TcgOp> _ops = [];
    private int _tempCount;
    private int _byteLength;

    /// <summary>Initializes a builder for a program starting at the given guest EIP.</summary>
    /// <param name="startEip">The guest EIP the program starts at.</param>
    public TcgBuilder(uint startEip)
    {
        StartEip = startEip;
    }

    /// <summary>Gets the guest EIP the program starts at.</summary>
    public uint StartEip { get; }

    /// <summary>Gets the number of temps allocated so far.</summary>
    public int TempCount => _tempCount;

    /// <summary>Allocates one temp.</summary>
    /// <returns>The temp index.</returns>
    public int NewTemp()
    {
        return _tempCount++;
    }

    /// <summary>Records the guest instruction length covered by the program.</summary>
    /// <param name="length">Instruction length in bytes.</param>
    public void NoteLength(int length)
    {
        _byteLength += length;
    }

    /// <summary>Emits a temp = immediate (masked by size).</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="value">Immediate value.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void MovImmediate(int dest, uint value, int size = 4)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.MovImmediate, Dest = dest, Immediate = value, Size = size });
    }

    /// <summary>Emits temp = guest register (32-bit).</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="register">Guest register.</param>
    public void ReadRegister(int dest, Gpr register)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.ReadRegister, Dest = dest, GuestRegister = (int)register, Size = 4 });
    }

    /// <summary>Emits temp = guest register for an 8/16/32-bit operand.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="registerIndex">Guest register index (0-7; 8-bit 4-7 are AH..BH).</param>
    /// <param name="size">Operand size in bytes.</param>
    public void ReadRegister(int dest, int registerIndex, int size)
    {
        TcgOpKind kind = size switch
        {
            1 => TcgOpKind.ReadRegister8,
            2 => TcgOpKind.ReadRegister16,
            _ => TcgOpKind.ReadRegister,
        };
        _ops.Add(new TcgOp { Kind = kind, Dest = dest, GuestRegister = registerIndex, Size = size });
    }

    /// <summary>Emits guest register = temp (32-bit).</summary>
    /// <param name="register">Guest register.</param>
    /// <param name="source">Source temp.</param>
    public void WriteRegister(Gpr register, int source)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.WriteRegister, GuestRegister = (int)register, Left = source, Size = 4 });
    }

    /// <summary>Emits guest register = temp for an 8/16/32-bit operand.</summary>
    /// <param name="registerIndex">Guest register index (0-7; 8-bit 4-7 are AH..BH).</param>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void WriteRegister(int registerIndex, int source, int size)
    {
        TcgOpKind kind = size switch
        {
            1 => TcgOpKind.WriteRegister8,
            2 => TcgOpKind.WriteRegister16,
            _ => TcgOpKind.WriteRegister,
        };
        _ops.Add(new TcgOp { Kind = kind, GuestRegister = registerIndex, Left = source, Size = size });
    }

    /// <summary>Emits a binary ALU operation.</summary>
    /// <param name="kind">The opcode (Add/Sub/And/Or/Xor).</param>
    /// <param name="dest">Destination temp.</param>
    /// <param name="left">Left temp.</param>
    /// <param name="right">Right temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Alu(TcgOpKind kind, int dest, int left, int right, int size = 4)
    {
        _ops.Add(new TcgOp { Kind = kind, Dest = dest, Left = left, Right = right, Size = size });
    }

    /// <summary>Emits a compare (subtract without storing).</summary>
    /// <param name="left">Left temp.</param>
    /// <param name="right">Right temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Cmp(int left, int right, int size = 4)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Cmp, Left = left, Right = right, Size = size });
    }

    /// <summary>Emits an increment (CF preserved).</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Inc(int dest, int source, int size = 4)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Inc, Dest = dest, Left = source, Size = size });
    }

    /// <summary>Emits a decrement (CF preserved).</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Dec(int dest, int source, int size = 4)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Dec, Dest = dest, Left = source, Size = size });
    }

    /// <summary>Emits a single shift/rotate.</summary>
    /// <param name="kind">The shift opcode.</param>
    /// <param name="dest">Destination temp.</param>
    /// <param name="value">Value temp.</param>
    /// <param name="count">Count temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Shift(TcgOpKind kind, int dest, int value, int count, int size)
    {
        _ops.Add(new TcgOp { Kind = kind, Dest = dest, Left = value, Right = count, Size = size });
    }

    /// <summary>Emits a double-precision shift (SHLD/SHRD).</summary>
    /// <param name="kind">Shld or Shrd.</param>
    /// <param name="dest">Destination temp.</param>
    /// <param name="value">Destination value temp.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="count">The immediate count.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void DoubleShift(TcgOpKind kind, int dest, int value, int source, uint count, int size)
    {
        _ops.Add(new TcgOp
        {
            Kind = kind,
            Dest = dest,
            Left = value,
            Right = source,
            Immediate = count,
            Size = size,
        });
    }

    /// <summary>Emits a negation.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Neg(int dest, int source, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Neg, Dest = dest, Left = source, Size = size });
    }

    /// <summary>Emits a bitwise complement (no flags).</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Complement(int dest, int source, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Not, Dest = dest, Left = source, Size = size });
    }

    /// <summary>Emits a zero/sign extension.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="sourceSize">Source size in bytes (1 or 2).</param>
    /// <param name="isSigned">True for MOVSX, false for MOVZX.</param>
    public void MovExtend(int dest, int source, int sourceSize, bool isSigned)
    {
        TcgOpKind kind = (sourceSize, isSigned) switch
        {
            (1, false) => TcgOpKind.Movzx8,
            (1, true) => TcgOpKind.Movsx8,
            (_, false) => TcgOpKind.Movzx16,
            _ => TcgOpKind.Movsx16,
        };
        _ops.Add(new TcgOp { Kind = kind, Dest = dest, Left = source, Size = sourceSize });
    }

    /// <summary>Emits SAHF (EFLAGS from AH).</summary>
    public void Sahf()
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Sahf });
    }

    /// <summary>Emits LAHF (AH from EFLAGS).</summary>
    public void Lahf()
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Lahf });
    }

    /// <summary>Emits an unsigned multiply of EAX by a temp (writes EDX:EAX).</summary>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Mul(int source, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Mul, Left = source, Size = size });
    }

    /// <summary>Emits a signed one-operand multiply of EAX by a temp.</summary>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Imul1(int source, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Imul1, Left = source, Size = size });
    }

    /// <summary>Emits a signed two-operand multiply.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="left">Left temp.</param>
    /// <param name="right">Right temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Imul2(int dest, int left, int right, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Imul2, Dest = dest, Left = left, Right = right, Size = size });
    }

    /// <summary>Emits a signed three-operand multiply.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="left">Source temp.</param>
    /// <param name="immediate">The immediate factor.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Imul3(int dest, int left, uint immediate, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Imul3, Dest = dest, Left = left, Immediate = immediate, Size = size });
    }

    /// <summary>Emits an unsigned divide of EDX:EAX by a temp.</summary>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Div(int source, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Div, Left = source, Size = size });
    }

    /// <summary>Emits a signed divide of EDX:EAX by a temp.</summary>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Idiv(int source, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Idiv, Left = source, Size = size });
    }

    /// <summary>Emits a stack push.</summary>
    /// <param name="value">Value temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Push(int value, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Push, Left = value, Size = size });
    }

    /// <summary>Emits a stack pop.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Pop(int dest, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Pop, Dest = dest, Size = size });
    }

    /// <summary>Emits a bit test/modify (BT/BTS/BTR/BTC).</summary>
    /// <param name="result">Result temp (register form).</param>
    /// <param name="valueOrAddress">Value temp or address temp.</param>
    /// <param name="bitIndex">Bit index temp.</param>
    /// <param name="operation">0=test, 1=set, 2=reset, 3=complement.</param>
    /// <param name="size">Operand size in bytes.</param>
    /// <param name="isMemory">True for the memory (bit-string) form.</param>
    public void BitTest(int result, int valueOrAddress, int bitIndex, int operation, int size, bool isMemory)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.BitTest,
            Dest = result,
            Left = valueOrAddress,
            Right = bitIndex,
            Immediate = (uint)operation,
            Size = size,
            IsMemory = isMemory,
        });
    }

    /// <summary>Emits a compare-and-exchange.</summary>
    /// <param name="destination">Destination register index, or address temp for memory.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    /// <param name="isMemory">True when the destination is memory.</param>
    public void Cmpxchg(int destination, int source, int size, bool isMemory)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Cmpxchg,
            Left = isMemory ? destination : 0,
            GuestRegister = isMemory ? -1 : destination,
            Right = source,
            Size = size,
            IsMemory = isMemory,
        });
    }

    /// <summary>Emits a compare-and-exchange of eight bytes.</summary>
    /// <param name="address">Address temp.</param>
    public void Cmpxchg8B(int address)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Cmpxchg8B, Left = address, IsMemory = true });
    }

    /// <summary>Emits an exchange-and-add.</summary>
    /// <param name="valueOrAddress">Destination value temp (register) or address temp.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="sourceRegister">Source register index (receives the old destination).</param>
    /// <param name="size">Operand size in bytes.</param>
    /// <param name="isMemory">True when the destination is memory.</param>
    public void Xadd(int valueOrAddress, int source, int sourceRegister, int size, bool isMemory)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Xadd,
            Left = valueOrAddress,
            Right = source,
            GuestRegister = sourceRegister,
            Size = size,
            IsMemory = isMemory,
        });
    }

    /// <summary>Emits a string operation with optional REP.</summary>
    /// <param name="operation">0=MOVS, 1=CMPS, 2=STOS, 3=LODS, 4=SCAS.</param>
    /// <param name="size">Element size in bytes.</param>
    /// <param name="rep">0=none, 1=REP, 2=REPNE.</param>
    /// <param name="address16">True for a 16-bit address size.</param>
    public void StringOp(int operation, int size, int rep, bool address16)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.StringOp,
            Immediate = (uint)operation,
            Size = size,
            GuestRegister = rep,
            Address16 = address16,
        });
    }

    /// <summary>Emits an effective-address computation (QEMU's address shape).</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="memory">The decoded memory operand.</param>
    /// <param name="segment">The segment used for the base.</param>
    public void Lea(int dest, in MemoryOperand memory, SegmentRegister segment)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Lea,
            Dest = dest,
            GuestRegister = memory.HasBase ? (int)memory.Base : -1,
            GuestRegister2 = memory.HasIndex ? (int)memory.Index : -1,
            Scale = memory.HasIndex ? memory.Scale : 1,
            Immediate = memory.Displacement,
            Segment = (int)segment,
            HasBase = memory.HasBase,
            HasIndex = memory.HasIndex,
            Address16 = memory.Is16Bit,
        });
    }

    /// <summary>Emits a load from a temp address.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="address">Address temp.</param>
    /// <param name="size">Access size in bytes.</param>
    public void Load(int dest, int address, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Load, Dest = dest, Left = address, Size = size });
    }

    /// <summary>Emits a store to a temp address.</summary>
    /// <param name="address">Address temp.</param>
    /// <param name="value">Value temp.</param>
    /// <param name="size">Access size in bytes.</param>
    public void Store(int address, int value, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Store, Left = address, Right = value, Size = size });
    }

    /// <summary>Emits an unconditional EIP set and ends the block.</summary>
    /// <param name="target">Guest EIP.</param>
    public void SetEip(uint target)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.SetEip, Immediate = target });
    }

    /// <summary>Emits a conditional EIP set; a taken condition ends the block.</summary>
    /// <param name="target">Guest EIP.</param>
    /// <param name="condition">The condition code.</param>
    public void ConditionalSetEip(uint target, ConditionCode condition)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.ConditionalSetEip, Immediate = target, Condition = condition });
    }

    /// <summary>Emits the end-of-block marker.</summary>
    public void Exit()
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Exit });
    }

    /// <summary>Builds the immutable program.</summary>
    /// <returns>The program.</returns>
    public TcgProgram Build()
    {
        return new TcgProgram(_ops.ToArray(), Math.Max(1, _tempCount), StartEip, _byteLength);
    }
}
