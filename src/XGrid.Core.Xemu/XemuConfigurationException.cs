using System;
using System.Collections.Generic;

namespace XGrid.Core.Xemu;

/// <summary>
/// Reports that the xemu core could not be configured or started. The message
/// lists every problem at once so the shell can show one actionable dialog.
/// </summary>
public sealed class XemuConfigurationException : Exception
{
    /// <summary>Creates an exception with no message.</summary>
    public XemuConfigurationException()
    {
        Errors = [];
    }

    /// <summary>Creates an exception for one problem.</summary>
    /// <param name="message">What is wrong.</param>
    public XemuConfigurationException(string message)
        : base(message)
    {
        Errors = [message];
    }

    /// <summary>Creates an exception for several problems at once.</summary>
    /// <param name="errors">The individual problems.</param>
    public XemuConfigurationException(IReadOnlyList<string> errors)
        : base(Join(errors))
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors;
    }

    /// <summary>Creates an exception that wraps a launch failure.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="innerException">The underlying failure.</param>
    public XemuConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    /// <summary>Gets the individual problems, in validation order.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string Join(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 1 ? errors[0] : string.Join(Environment.NewLine, errors);
    }
}
