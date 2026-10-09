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

    /// <summary>Gets the interrupt vector.</summary>
    public int Vector { get; }

    /// <summary>Gets the faulting instruction pointer.</summary>
    public uint Eip { get; }
}
