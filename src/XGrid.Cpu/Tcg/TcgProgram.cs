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

    /// <summary>Emits a double shift by a temp count (CL form).</summary>
    /// <param name="kind">SHLD or SHRD.</param>
    /// <param name="dest">Destination temp.</param>
    /// <param name="value">Destination value temp.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="count">Count temp (the low byte is used).</param>
    /// <param name="size">Operand size in bytes.</param>
    public void DoubleShiftTemp(TcgOpKind kind, int dest, int value, int source, int count, int size)
    {
        _ops.Add(new TcgOp
        {
            Kind = kind,
            Dest = dest,
            Left = value,
            Right = source,
            GuestRegister2 = count,
            On = true,
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
    /// <param name="address16">True for a 16-bit stack pointer.</param>
    public void Push(int value, int size, bool address16 = false)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Push, Left = value, Size = size, Address16 = address16 });
    }

    /// <summary>Emits a stack pop.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    /// <param name="address16">True for a 16-bit stack pointer.</param>
    public void Pop(int dest, int size, bool address16 = false)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Pop, Dest = dest, Size = size, Address16 = address16 });
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

    /// <summary>Emits an MMX register read (low 32 or all 64 bits) into a temp.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="mmx">MMX register index.</param>
    /// <param name="size">4 for MOVD, 8 for MOVQ and packed sources.</param>
    public void MmxToTemp(int dest, int mmx, int size)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.ToTemp,
            Dest = dest,
            GuestRegister = mmx,
            Size = size,
        });
    }

    /// <summary>Emits an MMX register write from a temp.</summary>
    /// <param name="mmx">MMX register index.</param>
    /// <param name="source">Source temp.</param>
    /// <param name="size">4 to zero-extend, 8 for the full register.</param>
    public void MmxFromTemp(int mmx, int source, int size)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.FromTemp,
            Left = source,
            GuestRegister = mmx,
            Size = size,
        });
    }

    /// <summary>Emits a 4- or 8-byte guest-memory load into a temp.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="address">Address temp.</param>
    /// <param name="size">Access size in bytes (4 or 8).</param>
    public void MmxLoad(int dest, int address, int size)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.Load,
            Dest = dest,
            Left = address,
            Size = size,
        });
    }

    /// <summary>Emits a 4- or 8-byte guest-memory store from a temp.</summary>
    /// <param name="address">Address temp.</param>
    /// <param name="value">Value temp.</param>
    /// <param name="size">Access size in bytes (4 or 8).</param>
    public void MmxStore(int address, int value, int size)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.Store,
            Left = address,
            Right = value,
            Size = size,
        });
    }

    /// <summary>Emits a packed binary MMX operation.</summary>
    /// <param name="mnemonic">The packed operation.</param>
    /// <param name="mmx">Destination MMX register index.</param>
    /// <param name="source">Source value temp.</param>
    public void MmxBinary(Mnemonic mnemonic, int mmx, int source)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.Binary,
            Mnemonic = mnemonic,
            GuestRegister = mmx,
            Left = source,
            Size = 8,
        });
    }

    /// <summary>Emits a packed MMX shift.</summary>
    /// <param name="mnemonic">The shift operation.</param>
    /// <param name="mmx">Destination MMX register index.</param>
    /// <param name="count">Count temp (the low byte is used).</param>
    public void MmxShift(Mnemonic mnemonic, int mmx, int count)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.Shift,
            Mnemonic = mnemonic,
            GuestRegister = mmx,
            Left = count,
            Size = 8,
        });
    }

    /// <summary>Emits a masked byte store (MASKMOVQ).</summary>
    /// <param name="data">Data MMX register index.</param>
    /// <param name="mask">Mask MMX register index.</param>
    public void MmxMaskmovq(int data, int mask)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.Maskmovq,
            GuestRegister = data,
            Left = mask,
            Size = 8,
        });
    }

    /// <summary>Emits a PINSRW.</summary>
    /// <param name="mmx">Destination MMX register index.</param>
    /// <param name="value">Source value temp.</param>
    /// <param name="lane">Word lane (0-3).</param>
    public void MmxPinsrw(int mmx, int value, uint lane)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.Pinsrw,
            GuestRegister = mmx,
            Left = value,
            Control = lane,
            Size = 8,
        });
    }

    /// <summary>Emits a PEXTRW.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="mmx">Source MMX register index.</param>
    /// <param name="lane">Word lane (0-3).</param>
    public void MmxPextrw(int dest, int mmx, uint lane)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.Pextrw,
            Dest = dest,
            GuestRegister = mmx,
            Control = lane,
        });
    }

    /// <summary>Emits a PMOVMSKB.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="mmx">Source MMX register index.</param>
    public void MmxPmovmskb(int dest, int mmx)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.Pmovmskb,
            Dest = dest,
            GuestRegister = mmx,
        });
    }

    /// <summary>Emits a PSHUFW.</summary>
    /// <param name="mmx">Destination MMX register index.</param>
    /// <param name="source">Source value temp.</param>
    /// <param name="control">The two-bit lane selectors.</param>
    public void MmxPshufw(int mmx, int source, uint control)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Mmx,
            MmxOp = TcgMmxOp.Pshufw,
            GuestRegister = mmx,
            Left = source,
            Control = control,
            Size = 8,
        });
    }

    /// <summary>Emits an x87 instruction for the shared helper.</summary>
    /// <param name="instruction">The decoded x87 instruction.</param>
    public void Fpu(in DecodedInstruction instruction)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Fpu, Instruction = instruction });
    }

    /// <summary>Emits a read of the x87 status word into a temp.</summary>
    /// <param name="dest">Destination temp.</param>
    public void ReadFpuStatus(int dest)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.ReadFpuStatus, Dest = dest, Size = 2 });
    }

    /// <summary>Emits an SSE instruction for the shared helper.</summary>
    /// <param name="instruction">The decoded SSE instruction.</param>
    public void Sse(in DecodedInstruction instruction)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Sse, Instruction = instruction });
    }

    /// <summary>Emits EMMS (the x87/MMX tag word becomes empty).</summary>
    public void MmxEmms()
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Mmx, MmxOp = TcgMmxOp.Emms });
    }

    /// <summary>Emits an add-with-carry.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="left">Left temp.</param>
    /// <param name="right">Right temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Adc(int dest, int left, int right, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Adc, Dest = dest, Left = left, Right = right, Size = size });
    }

    /// <summary>Emits a subtract-with-borrow.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="left">Left temp.</param>
    /// <param name="right">Right temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void Sbb(int dest, int left, int right, int size)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Sbb, Dest = dest, Left = left, Right = right, Size = size });
    }

    /// <summary>Emits a SETcc result (0 or 1) into a temp.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="condition">The condition code.</param>
    public void SetCondition(int dest, ConditionCode condition)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.SetCondition, Dest = dest, Condition = condition, Size = 1 });
    }

    /// <summary>Emits a CMOVcc (destination temp keeps its value unless the condition holds).</summary>
    /// <param name="dest">Destination temp (already loaded with the old value).</param>
    /// <param name="source">Source temp.</param>
    /// <param name="condition">The condition code.</param>
    /// <param name="size">Operand size in bytes.</param>
    public void ConditionalMove(int dest, int source, ConditionCode condition, int size)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.ConditionalMove,
            Dest = dest,
            Right = source,
            Condition = condition,
            Size = size,
        });
    }

    /// <summary>Emits a byte swap of the low 32 bits.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="source">Source temp.</param>
    public void Bswap(int dest, int source)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Bswap, Dest = dest, Left = source, Size = 4 });
    }

    /// <summary>Emits a bit scan (BSF/BSR).</summary>
    /// <param name="dest">Destination temp (its old value survives a zero source).</param>
    /// <param name="source">Source temp.</param>
    /// <param name="size">Operand size in bytes.</param>
    /// <param name="reverse">True for BSR.</param>
    public void BitScan(int dest, int source, int size, bool reverse)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.BitScan,
            Dest = dest,
            Left = source,
            Size = size,
            On = reverse,
        });
    }

    /// <summary>Emits an ARPL on a temp (ZF set when adjusted).</summary>
    /// <param name="dest">Destination temp, updated in place.</param>
    /// <param name="source">Source temp.</param>
    public void Arpl(int dest, int source)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Arpl, Dest = dest, Right = source, Size = 2 });
    }

    /// <summary>Emits an ARPL on a memory word (stored only when adjusted).</summary>
    /// <param name="address">Address temp.</param>
    /// <param name="source">Source temp.</param>
    public void ArplMemory(int address, int source)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Arpl,
            Left = address,
            Right = source,
            Size = 2,
            IsMemory = true,
        });
    }

    /// <summary>Emits CBW (0), CWDE (1), CWD (2) or CDQ (3).</summary>
    /// <param name="kind">The extension kind.</param>
    public void SignExtend(int kind)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.SignExtend, Immediate = (uint)kind });
    }

    /// <summary>Emits XLAT.</summary>
    /// <param name="segment">The segment register index.</param>
    /// <param name="address16">True for a 16-bit address size.</param>
    public void Xlat(int segment, bool address16)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Xlat,
            Segment = segment,
            Address16 = address16,
        });
    }

    /// <summary>Emits a flag set or clear.</summary>
    /// <param name="mask">The EFLAGS bit.</param>
    /// <param name="on">True to set the bit, false to clear it.</param>
    public void FlagOp(uint mask, bool on)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.FlagOp, Immediate = mask, On = on });
    }

    /// <summary>Emits a flag complement (CMC).</summary>
    /// <param name="mask">The EFLAGS bit.</param>
    public void ToggleFlag(uint mask)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.FlagOp, Immediate = mask, Size = 2 });
    }

    /// <summary>Emits a read of EFLAGS (reserved bit set) into a temp.</summary>
    /// <param name="dest">Destination temp.</param>
    public void ReadFlags(int dest)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.ReadFlags, Dest = dest, Size = 4 });
    }

    /// <summary>Emits a write of EFLAGS from a temp.</summary>
    /// <param name="source">Source temp.</param>
    public void WriteFlags(int source)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.WriteFlags, Left = source, Size = 4 });
    }

    /// <summary>Emits LOOP (0), LOOPE (1), LOOPNE (2) or JECXZ (3).</summary>
    /// <param name="kind">The loop kind.</param>
    /// <param name="target">Target EIP.</param>
    /// <param name="address16">True for a 16-bit address size (CX).</param>
    public void Loop(int kind, uint target, bool address16)
    {
        _ops.Add(new TcgOp
        {
            Kind = TcgOpKind.Loop,
            GuestRegister = kind,
            Immediate = target,
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
