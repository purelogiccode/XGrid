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
    private XemuCoreProcess? _process;
    private XemuWindowEmbedder? _embedder;
    private Timer? _monitor;
    private IntPtr _displayHost;
    private int _displayWidth;
    private int _displayHeight;
    private long _stopDeadline;
    private bool _stopping;
    private bool _disposed;

    /// <summary>Raised for each diagnostic line the shell should log.</summary>
    public event EventHandler<string>? Log;

    /// <summary>Raised when the core process ends, by request or on its own.</summary>
    public event EventHandler<XemuCoreStoppedEventArgs>? Stopped;

    /// <summary>Gets a value indicating whether the core process is alive.</summary>
    public bool IsRunning => _process?.IsRunning == true;

    /// <summary>Gets a value indicating whether the core window is embedded in the shell.</summary>
    public bool IsEmbedded => _embedder?.IsEmbedded == true;

    /// <summary>Gets the core process id, or 0.</summary>
    public int ProcessId => _process?.ProcessId ?? 0;

    /// <summary>Gets how long the core has been running.</summary>
    public TimeSpan Uptime => _process?.Uptime ?? TimeSpan.Zero;

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
        TryEmbed();
    }

    /// <summary>Reports a new display size; the embedded window follows.</summary>
    /// <param name="width">Display width in physical pixels.</param>
    /// <param name="height">Display height in physical pixels.</param>
    public void ResizeDisplay(int width, int height)
    {
        _displayWidth = Math.Max(1, width);
        _displayHeight = Math.Max(1, height);
        XemuWindowEmbedder? embedder = _embedder;
        if (embedder is not null && embedder.IsEmbedded)
        {
            embedder.Resize(_displayWidth, _displayHeight);
        }
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
        if (_process is not null)
        {
            throw new InvalidOperationException("The core is already running.");
        }

        var process = new XemuCoreProcess(launch);
        process.Start();
        lock (_gate)
        {
            _process = process;
            _stopping = false;
        }

        Log?.Invoke(this, $"xemu core started (pid {process.ProcessId}).");
        _monitor = new Timer(_ => Poll(), null, 250, 250);
        TryEmbed();
    }

    /// <summary>
    /// Asks the core to power off. The close request is the core's own
    /// shutdown path; the liveness poll reports the stop and terminates the
    /// process if it does not exit within eight seconds.
    /// </summary>
    public void Stop()
    {
        XemuCoreProcess? process = _process;
        if (process is null)
        {
            return;
        }

        lock (_gate)
        {
            _stopping = true;
            _stopDeadline = Environment.TickCount64 + 8000;
        }

        process.RequestClose();
    }

    /// <summary>Writes the core's log tail into the shell log.</summary>
    public void LogTail()
    {
        XemuCoreProcess? process = _process;
        string tail = process?.ReadLogTail(2048) ?? string.Empty;
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
        XemuCoreProcess? process = _process;
        if (process is not null)
        {
            lock (_gate)
            {
                _stopping = true;
            }

            // Dispose is the one place a blocking wait is correct: the process
            // must not outlive the shell window.
            _ = process.Stop(TimeSpan.FromSeconds(5));
            Finish(process);
        }

        _monitor?.Dispose();
        _monitor = null;
        _embedder?.Dispose();
        _embedder = null;
        _process?.Dispose();
        _process = null;
    }

    private void TryEmbed()
    {
        lock (_gate)
        {
            XemuCoreProcess? process = _process;
            if (process is null || _displayHost == IntPtr.Zero || _embedder?.IsEmbedded == true)
            {
                return;
            }

            // The window appears a second or two after the process starts; the
            // 250 ms poll retries, so the UI thread never blocks on it.
            IntPtr window = process.TryFindWindow();
            if (window == IntPtr.Zero)
            {
                FrontendTrace.Write($"core window not found yet (pid {process.ProcessId})");
                return;
            }

            try
            {
                _embedder ??= new XemuWindowEmbedder();
                _embedder.Embed(_displayHost, window, _displayWidth, _displayHeight);
                FrontendTrace.Write($"core window 0x{window.ToInt64():X} embedded in 0x{_displayHost.ToInt64():X}");
                Log?.Invoke(this, "xemu core display attached.");
            }
            catch (XemuConfigurationException exception)
            {
                FrontendTrace.Write("embed failed: " + exception.Message);
                Log?.Invoke(this, "Could not attach the core display: " + exception.Message);
            }
        }
    }

    private void Poll()
    {
        try
        {
            XemuCoreProcess? process = _process;
            if (process is null)
            {
                return;
            }

            if (process.IsRunning)
            {
                if (_stopping && Environment.TickCount64 > _stopDeadline)
                {
                    Log?.Invoke(this, "The core did not close in time; terminating it.");
                    process.Kill();
                    return;
                }

                TryEmbed();
                return;
            }

            Finish(process);
        }
        catch (ObjectDisposedException)
        {
            // The shell disposed the session while the timer was queued.
        }
    }

    private void Finish(XemuCoreProcess process)
    {
        int exitCode;
        bool requested;
        lock (_gate)
        {
            if (_process != process)
            {
                return;
            }

            _process = null;
            requested = _stopping;
            exitCode = process.ExitCode;
            _monitor?.Dispose();
            _monitor = null;
            _embedder?.Detach();
        }

        bool faulted = !requested && exitCode != 0;
        string message = requested
            ? $"The xemu core stopped after {process.Uptime.TotalSeconds:F1} s."
            : faulted
                ? $"The xemu core exited unexpectedly (code {exitCode})."
                : "The xemu core powered off.";

        LogTail();
        Stopped?.Invoke(this, new XemuCoreStoppedEventArgs(faulted, message, exitCode));
        process.Dispose();
    }
}
