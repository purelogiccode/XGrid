using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

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
    private readonly string _logPath;
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
        _logPath = Path.Combine(settings.WorkingDirectory, "xgrid-core.log");
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
    public void Start()
    {
        IReadOnlyList<string> extra = XemuCoreProcess.BuildExtraArguments(_settings);
        int status = _core.Start(
            _configPath,
            _logPath,
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
        try
        {
            using var stream = new FileStream(
                _logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            int take = (int)Math.Min(length, Math.Max(0, maxChars));
            stream.Seek(-take, SeekOrigin.End);
            byte[] buffer = new byte[take];
            int read = stream.Read(buffer, 0, take);
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _core.Dispose();
    }
}
