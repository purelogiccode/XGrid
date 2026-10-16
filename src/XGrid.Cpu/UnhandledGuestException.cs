using System;

namespace XGrid.Cpu;

/// <summary>Thrown when a guest exception occurs and no interrupt table is installed.</summary>
public sealed class UnhandledGuestException : Exception
{
    /// <summary>Creates an exception for the given vector and address.</summary>
    public UnhandledGuestException(int vector, uint eip)
        : base($"Unhandled guest exception vector {vector} at EIP=0x{eip:X8}.")
    {
        Vector = vector;
        Eip = eip;
    }

    /// <summary>Initializes a new instance of the <see cref="UnhandledGuestException"/> class.</summary>
    public UnhandledGuestException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnhandledGuestException"/> class with a message.</summary>
    /// <param name="message">The message describing the error.</param>
    public UnhandledGuestException(string? message) : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnhandledGuestException"/> class with a message and inner exception.</summary>
    /// <param name="message">The message describing the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public UnhandledGuestException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>Gets the interrupt vector.</summary>
    public int Vector { get; }

    /// <summary>Gets the faulting instruction pointer.</summary>
    public uint Eip { get; }
}
