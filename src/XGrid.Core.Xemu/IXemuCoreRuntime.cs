using System;

namespace XGrid.Core.Xemu;

/// <summary>
/// One running core, hosted in the shell's process or in its own. The session
/// drives a runtime and never branches on the hosting mode itself.
/// </summary>
public interface IXemuCoreRuntime : IDisposable
{
    /// <summary>Gets a value indicating whether the machine is alive.</summary>
    bool IsRunning { get; }

    /// <summary>Gets the exit code once the machine has stopped.</summary>
    int ExitCode { get; }

    /// <summary>Gets how long the machine has been running.</summary>
    TimeSpan Uptime { get; }

    /// <summary>Gets the operating-system process id that hosts the machine.</summary>
    int ProcessId { get; }

    /// <summary>Gets a value indicating whether the machine shares the shell's process.</summary>
    bool IsInProcess { get; }

    /// <summary>Starts the machine.</summary>
    void Start();

    /// <summary>Asks the guest to power off through its own shutdown path.</summary>
    void RequestStop();

    /// <summary>Terminates a machine that ignored <see cref="RequestStop"/>.</summary>
    void Kill();

    /// <summary>Reads the tail of the core's own log.</summary>
    /// <param name="maxChars">Maximum number of characters to return.</param>
    string ReadLogTail(int maxChars);
}
