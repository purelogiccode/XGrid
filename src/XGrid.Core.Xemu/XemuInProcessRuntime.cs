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
    private IntPtr _window;

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
        Discs = core.SupportsDiscChange ? new InProcessDiscs(core) : null;
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
    public ICoreDiscs? Discs { get; }

    /// <inheritdoc />
    public void Start()
    {
        IReadOnlyList<string> extra = XemuCoreProcess.BuildExtraArguments(_settings);
        if (string.IsNullOrEmpty(_settings.DisplaySinkName) && XemuWindowEmbedder.IsSupported)
        {
            // Best effort: ask the core to create its window hidden. The DLL's
            // CRT can snapshot the environment at load time, so the shell also
            // finds the window by its SDL class and hides it before reparenting.
            Environment.SetEnvironmentVariable("XGRID_EMBED", "1");
        }

        int status = _core.Start(
            _configPath,
            _settings.DisplaySinkName,
            _settings.InputSinkName,
            extra);
        if (status != 0)
        {
            throw new XemuConfigurationException(status switch
            {
                -1 => "The in-process core can run one machine per shell. Restart XGrid to use in-process features, or switch Core hosting to Separate process.",
                -2 => "The in-process core rejected the configuration path.",
                -3 => "The in-process core rejected the command line.",
                _ => $"The in-process core refused to start (status {status}).",
            });
        }

        _startedAt = Stopwatch.GetTimestamp();
        _stoppedAt = 0;
        _exitCode = null;
    }

    /// <summary>
    /// Returns the core's own window if it exists yet, without blocking. The
    /// window is created on the core's thread a second or two after start.
    /// </summary>
    /// <returns>The window handle, or <see cref="IntPtr.Zero"/>.</returns>
    /// <remarks>
    /// Only the core's own window is considered: it is matched by the
    /// <c>XGridCore</c> title or the <c>SDL_app</c> class. A process-wide search
    /// could return the shell's own window and embed it into itself.
    /// </remarks>
    public IntPtr TryFindWindow()
    {
        if (!XemuWindowEmbedder.IsSupported || !_core.IsRunning)
        {
            return IntPtr.Zero;
        }

        if (_window != IntPtr.Zero && !Win32Window.IsWindow(_window))
        {
            _window = IntPtr.Zero;
        }

        if (_window == IntPtr.Zero)
        {
            _window = XemuWindowLocator.FindTitled(Environment.ProcessId, "XGridCore");
        }

        if (_window == IntPtr.Zero)
        {
            // The core inside this process is a normal SDL app until the shell
            // embeds it. Filter by class: a process-wide search would also see
            // the Avalonia main window and could embed the shell into itself.
            _window = XemuWindowLocator.FindClass(Environment.ProcessId, "SDL_app");
            if (_window != IntPtr.Zero)
            {
                // Hide it before the reparent so the embed never flashes a
                // second top-level window over the shell.
                _ = Win32Window.ShowWindow(_window, Win32Window.SwHide);
            }
        }

        return _window;
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
