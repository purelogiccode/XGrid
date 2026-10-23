// SPDX-License-Identifier: GPL-2.0-or-later
// Part of the XGrid DSP56300 port; see docs/porting-policy.md.

using System;

namespace XGrid.Audio.Dsp;

/// <summary>The base class for DSP56300 execution faults.</summary>
public abstract class DspException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DspException"/> class.</summary>
    protected DspException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspException"/> class.</summary>
    /// <param name="message">The message.</param>
    protected DspException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    protected DspException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the program counter at the fault.</summary>
    public uint Pc { get; protected init; }

    /// <summary>Gets the instruction word at the fault.</summary>
    public uint Instruction { get; protected init; }
}

/// <summary>Raised when the decoder has no entry for an instruction word.</summary>
public sealed class DspUndefinedInstructionException : DspException
{
    /// <summary>Initializes a new instance of the <see cref="DspUndefinedInstructionException"/> class.</summary>
    public DspUndefinedInstructionException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspUndefinedInstructionException"/> class.</summary>
    /// <param name="message">The message.</param>
    public DspUndefinedInstructionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspUndefinedInstructionException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DspUndefinedInstructionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspUndefinedInstructionException"/> class.</summary>
    /// <param name="pc">The program counter.</param>
    /// <param name="instruction">The instruction word.</param>
    public DspUndefinedInstructionException(uint pc, uint instruction)
        : base($"DSP undefined instruction 0x{instruction:X6} at P:0x{pc:X4}")
    {
        Pc = pc;
        Instruction = instruction;
    }
}

/// <summary>Raised by the <c>ill</c> instruction when exception debugging is on.</summary>
public sealed class DspIllegalInstructionException : DspException
{
    /// <summary>Initializes a new instance of the <see cref="DspIllegalInstructionException"/> class.</summary>
    public DspIllegalInstructionException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspIllegalInstructionException"/> class.</summary>
    /// <param name="message">The message.</param>
    public DspIllegalInstructionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspIllegalInstructionException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DspIllegalInstructionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspIllegalInstructionException"/> class.</summary>
    /// <param name="pc">The program counter.</param>
    /// <param name="instruction">The instruction word.</param>
    public DspIllegalInstructionException(uint pc, uint instruction)
        : base($"DSP illegal instruction 0x{instruction:X6} at P:0x{pc:X4}")
    {
        Pc = pc;
        Instruction = instruction;
    }
}

/// <summary>Raised on a stack overflow or underflow when exception debugging is on.</summary>
public sealed class DspStackException : DspException
{
    /// <summary>Initializes a new instance of the <see cref="DspStackException"/> class.</summary>
    public DspStackException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspStackException"/> class.</summary>
    /// <param name="message">The message.</param>
    public DspStackException(string message)
        : base($"DSP stack error: {message}")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DspStackException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DspStackException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
