using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace XGrid.Core.Xemu;

/// <summary>
/// Hosts the core inside the shell's own process. The machine runs on the
/// core's thread; the shell keeps presenting the shared-section frames and
/// publishing the pad exactly as it does for a separate process.
/// </summary>
public sealed class XemuInProcessRuntime : IXemuCoreRuntime
{
    private readonly XemuLaunchSettings _settings;
    private readonly XemuInProcessCore _core;
    private readonly string _configPath;
    private long _startedAt;
    private long _stoppedAt;
    private int? _exitCode;

    /// <summary>Creates a runtime for one launch.</summary>
    /// <param name="settings">Validated launch settings.</param>
    /// <param name="core">Loaded core DLL.</param>
    public XemuInProcessRuntime(XemuLaunchSettings settings, XemuInProcessCore core)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(core);
        _settings = settings;
        _core = core;
        _configPath = XemuCoreProcess.WriteConfiguration(settings);
        SaveStates = new InProcessSaveStateService(core);
        Playback = core.SupportsPlayback ? new InProcessPlayback(core) : null;
    }

    /// <summary>Gets the core version string.</summary>
    public string Version => _core.Version;

    /// <summary>Gets the generated configuration path.</summary>
    public string ConfigPath => _configPath;

    /// <inheritdoc />
    public bool IsRunning => _core.IsRunning;

    /// <inheritdoc />
    public int ExitCode
    {
        get
        {
            if (_exitCode is { } cached)
            {
                return cached;
            }

            if (_core.IsRunning)
            {
                return 0;
            }

            // The run loop has returned; joining releases the thread handle.
            _exitCode = _core.Join();
            _stoppedAt = Stopwatch.GetTimestamp();
            return _exitCode.Value;
        }
    }

    /// <inheritdoc />
    public TimeSpan Uptime
    {
        get
        {
            if (_startedAt == 0)
            {
                return TimeSpan.Zero;
            }

            long end = _stoppedAt != 0 ? _stoppedAt : Stopwatch.GetTimestamp();
            return Stopwatch.GetElapsedTime(_startedAt, end);
        }
    }

    /// <inheritdoc />
    public int ProcessId => Environment.ProcessId;

    /// <inheritdoc />
    public bool IsInProcess => true;

    /// <inheritdoc />
    public ISaveStateService SaveStates { get; }

    /// <inheritdoc />
    public ICorePlayback? Playback { get; }

    /// <inheritdoc />
    public void Start()
    {
        IReadOnlyList<string> extra = XemuCoreProcess.BuildExtraArguments(_settings);
        int status = _core.Start(
            _configPath,
            _settings.DisplaySinkName,
            _settings.InputSinkName,
            extra);
        if (status != 0)
        {
            throw new XemuConfigurationException(status switch
            {
                -1 => "The in-process core already ran in this shell; restart the shell to power on again.",
                -2 => "The in-process core rejected the configuration path.",
                -3 => "The in-process core rejected the command line.",
                _ => $"The in-process core refused to start (status {status}).",
            });
        }

        _startedAt = Stopwatch.GetTimestamp();
        _stoppedAt = 0;
        _exitCode = null;
    }

    /// <inheritdoc />
    public void RequestStop()
    {
        _core.RequestStop();
    }

    /// <inheritdoc />
    public void Kill()
    {
        // A thread cannot be terminated safely, so the request is repeated and
        // the session reports that the machine did not stop.
        _core.RequestStop();
        if (!_core.IsRunning)
        {
            _ = ExitCode;
        }
    }

    /// <inheritdoc />
    public string ReadLogTail(int maxChars)
    {
        // The in-process core shares the shell's streams and keeps no log file
        // of its own; the shell's activity log carries the session messages.
        _ = maxChars;
        return string.Empty;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _core.Dispose();
    }
}
