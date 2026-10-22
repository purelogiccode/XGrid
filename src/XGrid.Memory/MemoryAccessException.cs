using System;

namespace XGrid.Memory;

/// <summary>Thrown for host-side invalid memory accesses (developer errors, not guest faults).</summary>
public sealed class MemoryAccessException : Exception
{
    /// <summary>Creates an exception with the given message.</summary>
    public MemoryAccessException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and inner exception.</summary>
    // ReSharper disable once UnusedMember.Global
    public MemoryAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with the default message.</summary>
    // ReSharper disable once UnusedMember.Global
    public MemoryAccessException()
    {
    }
}
