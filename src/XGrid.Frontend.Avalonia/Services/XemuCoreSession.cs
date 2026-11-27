using System;
using System.Threading;
using XGrid.Core.Xemu;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Reports that the core process ended.</summary>
/// <param name="faulted">True when the core exited with a non-zero code.</param>
/// <param name="message">Why it ended, for the shell log.</param>
/// <param name="exitCode">The process exit code.</param>
public sealed class XemuCoreStoppedEventArgs(bool faulted, string message, int exitCode) : EventArgs
{
    /// <summary>Gets a value indicating whether the core ended unexpectedly.</summary>
    public bool Faulted { get; } = faulted;

    /// <summary>Gets the human-readable reason.</summary>
    public string Message { get; } = message;

    /// <summary>Gets the core's exit code.</summary>
    public int ExitCode { get; } = exitCode;
}

/// <summary>
/// Owns one xemu core launch for the shell: the process, its generated
/// configuration and the embedded display. All members are called on the UI
/// thread; the only background work is a 500 ms liveness poll.
/// </summary>
public sealed class XemuCoreSession : IDisposable
{
    private readonly Lock _gate = new();
    private IXemuCoreRuntime? _runtime;
    private XemuWindowEmbedder? _embedder;
    private XemuInputSink? _input;
    private Timer? _monitor;
    private IntPtr _displayHost;
    private int _displayWidth;
    private int _displayHeight;
    private bool _displayVisible = true;
    private bool _takeFocus;
    private long _stopDeadline;
    private bool _stopping;
    private bool _stopDeadlineReported;
    private bool _disposed;

    /// <summary>Creates a session that hosts one core launch.</summary>
    public XemuCoreSession()
    {
    }

    /// <summary>Raised for each diagnostic line the shell should log.</summary>
    public event EventHandler<string>? Log;

    /// <summary>Raised when the core process ends, by request or on its own.</summary>
    public event EventHandler<XemuCoreStoppedEventArgs>? Stopped;

    /// <summary>Gets a value indicating whether the core process is alive.</summary>
    public bool IsRunning => _runtime?.IsRunning == true;

    /// <summary>Gets a value indicating whether the core shares the shell's process.</summary>
    public bool IsInProcess => _runtime?.IsInProcess == true;

    /// <summary>Gets a value indicating whether the core window is embedded in the shell.</summary>
    public bool IsEmbedded => _embedder?.IsEmbedded == true;

    /// <summary>
    /// Gets the core's save-state controls, or null when the running core
    /// cannot reach them (a separate process today).
    /// </summary>
    public ISaveStateService? SaveStates => _runtime?.SaveStates;

    /// <summary>
    /// Gets the core's pause/step controls, or null when the running core
    /// cannot reach them (a separate process or an older build).
    /// </summary>
    public ICorePlayback? Playback => _runtime?.Playback;

    /// <summary>
    /// Gets the core's disc-change controls, or null when the running core
    /// cannot reach them (a separate process or an older build).
    /// </summary>
    public ICoreDiscs? Discs => _runtime?.Discs;

    /// <summary>Gets the process id that hosts the core, or 0.</summary>
    public int ProcessId => _runtime?.ProcessId ?? 0;

    /// <summary>Gets how long the core has been running.</summary>
    public TimeSpan Uptime => _runtime?.Uptime ?? TimeSpan.Zero;

    /// <summary>
    /// Reports the shell control that owns the display area. Calling it before
    /// a launch is fine: the window is embedded as soon as both exist.
    /// </summary>
    /// <param name="host">Native host window handle.</param>
    /// <param name="width">Display width in physical pixels.</param>
    /// <param name="height">Display height in physical pixels.</param>
    public void AttachDisplay(IntPtr host, int width, int height)
    {
        if (_disposed)
        {
            return;
        }

        _displayHost = host;
        // The control publishes before its first layout pass (1x1); keep the
        // last meaningful size until the real one arrives.
        if (width >= 64 && height >= 64)
        {
            _displayWidth = width;
            _displayHeight = height;
        }

        FrontendTrace.Write($"display attached hwnd=0x{host.ToInt64():X} {width}x{height}");
        if (_embedder is { IsEmbedded: true } embedder && embedder.IsEmbeddedIn(host))
        {
            embedder.Resize(_displayWidth, _displayHeight);
        }
        else
        {
            // The page host is new (the old one was destroyed), so the core
            // window must be moved into it rather than merely resized.
            _embedder?.Detach();
            TryEmbed();
        }
    }

    /// <summary>Forwards one pad state to the core.</summary>
    /// <param name="port">Xbox port index, 0 to 3.</param>
    /// <param name="buttons">xemu XID button mask.</param>
    /// <param name="lx">Left stick X.</param>
    /// <param name="ly">Left stick Y.</param>
    /// <param name="rx">Right stick X.</param>
    /// <param name="ry">Right stick Y.</param>
    /// <param name="lt">Left trigger 0..32767.</param>
    /// <param name="rt">Right trigger 0..32767.</param>
    public void SetPad(int port, uint buttons, int lx, int ly, int rx, int ry, int lt, int rt)
    {
        _input?.Write(port, buttons, lx, ly, rx, ry, lt, rt);
    }

    /// <summary>Shows or hides the core picture without stopping the core.</summary>
    /// <param name="visible">True on the console page, false on any other page.</param>
    public void SetDisplayVisible(bool visible)
    {
        _displayVisible = visible;
        _embedder?.SetVisible(visible);
    }

    /// <summary>Reports that the display control is gone (page change, closed window).</summary>
    public void DetachDisplay()
    {
        _displayHost = IntPtr.Zero;
        _embedder?.Detach();
    }

    /// <summary>
    /// Starts the core. The display is embedded when a host handle is attached;
    /// until then the core runs with its own window off-screen.
    /// </summary>
    /// <param name="launch">Validated launch settings.</param>
    public void Start(XemuLaunchSettings launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runtime is not null)
        {
            throw new InvalidOperationException("The core is already running.");
        }

        _takeFocus = launch.IsChihiro;

        // The core owns the picture. The shell never creates a frame reader;
        // filters and the overlay are selected through the core ABI/env.
        if (XemuInputSink.IsSupported && !string.IsNullOrWhiteSpace(launch.InputSinkName))
        {
            _input = new XemuInputSink(launch.InputSinkName);
            // Publish neutral states immediately so the core never reads garbage.
            _input.WriteNeutral();
            Log?.Invoke(this, "Four player pads are published to the core.");
        }

        IXemuCoreRuntime runtime;
        if (string.Equals(launch.HostMode, "in-process", StringComparison.OrdinalIgnoreCase))
        {
            XemuInProcessRuntime? inProcess = StartInProcess(launch);
            if (inProcess is not null)
            {
                runtime = inProcess;
            }
            else
            {
                runtime = StartProcess(launch);
            }
        }
        else
        {
            runtime = StartProcess(launch);
        }

        lock (_gate)
        {
            _runtime = runtime;
            _stopping = false;
            _stopDeadlineReported = false;
        }

        Log?.Invoke(this, "The core owns the picture; the shell does not read frames.");

        _monitor = new Timer(_ => Poll(), null, 250, 250);
        TryEmbed();
    }

    /// <summary>Sets the presentation pace for a running core (1 is real time).</summary>
    /// <param name="multiplier">Speed multiplier from 1 to 8.</param>
    /// <returns>True when the running core accepted the change.</returns>
    public bool TrySetSpeed(int multiplier)
    {
        return _runtime?.TrySetSpeed(multiplier) == true;
    }

    /// <summary>Selects the core-side presentation filter for a running core.</summary>
    /// <param name="filter">Filter key (nearest, bilinear, scanline, crt).</param>
    /// <returns>True when the running core accepted the change.</returns>
    public bool TrySetPresentationFilter(string filter)
    {
        return _runtime?.TrySetPresentationFilter(filter) == true;
    }

    /// <summary>Turns the core-drawn frame-rate overlay on or off.</summary>
    /// <param name="enabled">True to draw the overlay.</param>
    /// <returns>True when the running core accepted the change.</returns>
    public bool TrySetFpsOverlay(bool enabled)
    {
        return _runtime?.TrySetFpsOverlay(enabled) == true;
    }

    /// <summary>Caps presented frames per second (0 is unlimited).</summary>
    /// <param name="fps">Frame-rate limit.</param>
    /// <returns>True when the running core accepted the change.</returns>
    public bool TrySetFrameRateLimit(int fps)
    {
        return _runtime?.TrySetFrameRateLimit(fps) == true;
    }

    /// <summary>Enables or disables dropping missed presentation deadlines.</summary>
    /// <param name="enabled">True to skip missed frames.</param>
    /// <returns>True when the running core accepted the change.</returns>
    public bool TrySetFrameSkip(bool enabled)
    {
        return _runtime?.TrySetFrameSkip(enabled) == true;
    }

    /// <summary>Queues a core-side screenshot of the running picture.</summary>
    /// <returns>True when the running core accepted the request.</returns>
    public bool TryRequestScreenshot()
    {
        return _runtime?.TryRequestScreenshot() == true;
    }

    private XemuInProcessRuntime? StartInProcess(XemuLaunchSettings launch)
    {
        XemuInProcessCore? core = XemuInProcessCore.TryLoad(launch.CorePath, out string libraryPath);
        if (core is null)
        {
            Log?.Invoke(this, "The in-process core library is not available; starting a separate core process.");
            return null;
        }

        try
        {
            var runtime = new XemuInProcessRuntime(launch, core);
            runtime.Start();
            Log?.Invoke(this, $"Emulator started in-process ({libraryPath}).");
            return runtime;
        }
        catch (XemuConfigurationException exception)
        {
            core.Dispose();
            Serilog.Log.Error(exception, "The in-process core did not start");
            Log?.Invoke(this, "The in-process core did not start: " + exception.Message
                              + " Starting a separate core process for this session.");
            return null;
        }
    }

    private XemuCoreProcess StartProcess(XemuLaunchSettings launch)
    {
        var process = new XemuCoreProcess(launch);
        process.Start();
        Log?.Invoke(this, $"Emulator started (pid {process.ProcessId}).");
        return process;
    }

    /// <summary>
    /// Asks the core to power off. The close request is the core's own
    /// shutdown path; the liveness poll reports the stop and terminates the
    /// process if it does not exit within eight seconds.
    /// </summary>
    public void Stop()
    {
        IXemuCoreRuntime? runtime = _runtime;
        if (runtime is null)
        {
            return;
        }

        lock (_gate)
        {
            _stopping = true;
            _stopDeadline = Environment.TickCount64 + 8000;
            _stopDeadlineReported = false;
        }

        // A paused machine cannot process the shutdown path; resume first.
        _ = runtime.Playback?.TrySetPaused(false, out _);
        runtime.RequestStop();
    }

    /// <summary>Writes the core's log tail into the shell log.</summary>
    public void LogTail()
    {
        IXemuCoreRuntime? runtime = _runtime;
        string tail = runtime?.ReadLogTail(2048) ?? string.Empty;
        if (tail.Length == 0)
        {
            return;
        }

        foreach (string line in tail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Log?.Invoke(this, "[core] " + line);
        }
    }

    /// <summary>Stops the core and releases the process and display handles.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IXemuCoreRuntime? runtime = _runtime;
        if (runtime is not null)
        {
            lock (_gate)
            {
                _stopping = true;
            }

            // Dispose is the one place a blocking wait is correct: the core
            // must not outlive the shell window. A paused machine cannot run
            // the shutdown path, so resume it first.
            _ = runtime.Playback?.TrySetPaused(false, out _);
            runtime.RequestStop();
            long deadline = Environment.TickCount64 + 10000;
            while (runtime.IsRunning && Environment.TickCount64 < deadline)
            {
                Thread.Sleep(50);
            }

            if (runtime.IsRunning)
            {
                runtime.Kill();
                long killDeadline = Environment.TickCount64 + 2000;
                while (runtime.IsRunning && Environment.TickCount64 < killDeadline)
                {
                    Thread.Sleep(50);
                }
            }

            bool stopped = !runtime.IsRunning;
            Finish(runtime);
            if (!stopped)
            {
                // Never reparent a live GL window: the driver can fault while
                // its swapchain is still in use. The process is exiting.
                Serilog.Log.Warning("The in-process core did not stop; leaving its display attached until process exit");
                _monitor?.Dispose();
                _monitor = null;
                _input?.Dispose();
                _input = null;
                _runtime?.Dispose();
                _runtime = null;
                return;
            }
        }

        _monitor?.Dispose();
        _monitor = null;
        _input?.Dispose();
        _input = null;
        _embedder?.Dispose();
        _embedder = null;
        _runtime?.Dispose();
        _runtime = null;
    }

    private void TryEmbed()
    {
        if (!XemuWindowEmbedder.IsSupported)
        {
            return;
        }

        lock (_gate)
        {
            if (_displayHost == IntPtr.Zero || _embedder?.IsEmbedded == true)
            {
                return;
            }

            XemuCoreProcess? process = _runtime as XemuCoreProcess;
            XemuInProcessRuntime? inProcess = _runtime as XemuInProcessRuntime;
            if (process is null && inProcess is null)
            {
                // The display host exists before the console is powered on.
                return;
            }

            // The window appears a second or two after the core starts; the
            // 250 ms poll retries, so the UI thread never blocks on it. The
            // in-process host shares this process, so only its own core window
            // is eligible for embedding.
            IntPtr window = process is not null
                ? process.TryFindWindow()
                : inProcess!.TryFindWindow();
            if (window == IntPtr.Zero)
            {
                int processId = process?.ProcessId ?? Environment.ProcessId;
                FrontendTrace.Write($"core window not found yet (pid {processId})");
                return;
            }

            FrontendTrace.Write($"embed check host=0x{_displayHost.ToInt64():X} child=0x{window.ToInt64():X}");

            try
            {
                _embedder ??= new XemuWindowEmbedder();
                _embedder.TakeFocus = _takeFocus;
                _embedder.Embed(_displayHost, window, _displayWidth, _displayHeight);
                // The embed may arrive while the user is on another page.
                _embedder.SetVisible(_displayVisible);
                FrontendTrace.Write($"core window 0x{window.ToInt64():X} embedded in 0x{_displayHost.ToInt64():X}");
                Log?.Invoke(this, "Emulator display attached.");
            }
            catch (XemuConfigurationException exception)
            {
                Serilog.Log.Warning(exception, "The core display could not be attached");
                FrontendTrace.Write("embed failed: " + exception.Message);
                Log?.Invoke(this, "Could not attach the core display: " + exception.Message);
            }
        }
    }

    private void Poll()
    {
        try
        {
            IXemuCoreRuntime? runtime = _runtime;
            if (runtime is null)
            {
                return;
            }

            if (runtime.IsRunning)
            {
                if (_stopping && Environment.TickCount64 > _stopDeadline)
                {
                    if (!_stopDeadlineReported)
                    {
                        _stopDeadlineReported = true;
                        Log?.Invoke(this, runtime.IsInProcess
                            ? "The in-process core did not stop in time; it is still shutting down."
                            : "The core did not close in time; terminating it.");
                        runtime.Kill();
                    }

                    return;
                }

                TryEmbed();
                return;
            }

            Finish(runtime);
        }
        catch (ObjectDisposedException exception)
        {
            Serilog.Log.Debug(exception, "The core session was disposed while the start timer was queued");
        }
    }

    private void Finish(IXemuCoreRuntime runtime)
    {
        int exitCode;
        bool requested;
        lock (_gate)
        {
            if (_runtime != runtime)
            {
                return;
            }

            _runtime = null;
            requested = _stopping;
            exitCode = runtime.ExitCode;
            _monitor?.Dispose();
            _monitor = null;
            if (!runtime.IsRunning)
            {
                _embedder?.Detach();
            }
        }

        bool faulted = !requested && exitCode != 0;
        string message = requested
            ? $"The emulator stopped after {runtime.Uptime.TotalSeconds:F1} s."
            : faulted
                ? $"The emulator exited unexpectedly (code {exitCode})."
                : "The emulator powered off.";

        LogTail();
        Stopped?.Invoke(this, new XemuCoreStoppedEventArgs(faulted, message, exitCode));
        runtime.Dispose();
    }
}
