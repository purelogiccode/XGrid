using System;

namespace XGrid.Cpu;

/// <summary>Thrown when the interpreter encounters an opcode it does not implement yet.</summary>
public sealed class UnsupportedInstructionException : Exception
{
    /// <summary>Creates an exception for the given address and opcode.</summary>
    public UnsupportedInstructionException(uint eip, byte opcode)
        : base($"Unsupported instruction 0x{opcode:X2} at EIP=0x{eip:X8}.")
    {
        Eip = eip;
        Opcode = opcode;
    }

    public UnsupportedInstructionException()
    {
    }

    public UnsupportedInstructionException(string? message) : base(message)
    {
    }

    public UnsupportedInstructionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>Gets the instruction pointer where the opcode was encountered.</summary>
    public uint Eip { get; }

    /// <summary>Gets the first opcode byte.</summary>
    public byte Opcode { get; }
}
