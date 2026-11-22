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

    /// <summary>Emits a temp = immediate.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="value">Immediate value.</param>
    public void MovImmediate(int dest, uint value)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.MovImmediate, Dest = dest, Immediate = value });
    }

    /// <summary>Emits temp = guest register.</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="register">Guest register.</param>
    public void ReadRegister(int dest, Gpr register)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.ReadRegister, Dest = dest, GuestRegister = (int)register });
    }

    /// <summary>Emits guest register = temp.</summary>
    /// <param name="register">Guest register.</param>
    /// <param name="source">Source temp.</param>
    public void WriteRegister(Gpr register, int source)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.WriteRegister, GuestRegister = (int)register, Left = source });
    }

    /// <summary>Emits a binary ALU operation.</summary>
    /// <param name="kind">The opcode (Add/Sub/And/Or/Xor).</param>
    /// <param name="dest">Destination temp.</param>
    /// <param name="left">Left temp.</param>
    /// <param name="right">Right temp.</param>
    public void Alu(TcgOpKind kind, int dest, int left, int right)
    {
        _ops.Add(new TcgOp { Kind = kind, Dest = dest, Left = left, Right = right });
    }

    /// <summary>Emits a compare (subtract without storing).</summary>
    /// <param name="left">Left temp.</param>
    /// <param name="right">Right temp.</param>
    public void Cmp(int left, int right)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Cmp, Left = left, Right = right });
    }

    /// <summary>Emits an increment (CF preserved).</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="source">Source temp.</param>
    public void Inc(int dest, int source)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Inc, Dest = dest, Left = source });
    }

    /// <summary>Emits a decrement (CF preserved).</summary>
    /// <param name="dest">Destination temp.</param>
    /// <param name="source">Source temp.</param>
    public void Dec(int dest, int source)
    {
        _ops.Add(new TcgOp { Kind = TcgOpKind.Dec, Dest = dest, Left = source });
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
