namespace XGrid.Core.Diagnostics;

/// <summary>Logging severity levels (plan section 33).</summary>
public enum LogLevel
{
    /// <summary>Finest-grained diagnostic events.</summary>
    Trace = 0,

    /// <summary>Diagnostic events useful during development.</summary>
    Debug = 1,

    /// <summary>General operational events.</summary>
    Information = 2,

    /// <summary>Abnormal events that do not stop execution.</summary>
    Warning = 3,

    /// <summary>Failures that prevent an operation from completing.</summary>
    Error = 4,

    /// <summary>Failures that prevent the emulator from continuing.</summary>
    // ReSharper disable once UnusedMember.Global
    Critical = 5,
}
