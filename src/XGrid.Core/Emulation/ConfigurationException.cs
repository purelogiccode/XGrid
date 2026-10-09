using System;

namespace XGrid.Core.Emulation;

/// <summary>Thrown when emulator configuration or options are invalid.</summary>
public sealed class ConfigurationException : Exception
{
    /// <summary>Creates an exception with the given message.</summary>
    public ConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with the given message and inner exception.</summary>
    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
