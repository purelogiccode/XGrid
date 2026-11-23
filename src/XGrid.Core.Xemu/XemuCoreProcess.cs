using System;
using System.Diagnostics;
using System.IO;

namespace XGrid.Core.Xemu;

/// <summary>
/// Runs one xemu core process for the shell: writes its configuration, starts
/// it, waits for its window, and shuts it down gracefully (a window close is
/// the core's power-off request).
/// </summary>
/// <remarks>
/// The core is the emulator: QEMU TCG, the NV2A and its OpenGL renderer, the
/// MCPX/APU/ACI/IDE/USB devices, audio and input. XGrid owns the shell around
/// it, which is why this type does not emulate anything.
/// </remarks>
public sealed class XemuCoreProcess : IDisposable
{
    private readonly XemuLaunchSettings _settings;
    private Process? _process;
    private IntPtr _window;
    private long _startedAt;
    private long _stoppedAt;
    private int? _exitCode;
    private bool _disposed;

    /// <summary>Creates a host for one core launch.</summary>
    /// <param name="settings">Validated launch settings.</param>
    public XemuCoreProcess(XemuLaunchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        ConfigPath = Path.Combine(settings.WorkingDirectory, "xemu.toml");
        LogPath = Path.Combine(settings.WorkingDirectory, "xemu.log");
    }

    /// <summary>Gets the generated core configuration path.</summary>
    public string ConfigPath { get; }

    /// <summary>Gets the core's log path (<c>xemu.log</c> in its working directory).</summary>
    public string LogPath { get; }

    /// <summary>Gets a value indicating whether the core process is alive.</summary>
    public bool IsRunning
    {
        get
        {
            Process? process = _process;
            if (process is null)
            {
                return false;
            }

            try
            {
                return !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>Gets the core process id, or 0 when it was never started.</summary>
    public int ProcessId => _process?.Id ?? 0;

    /// <summary>Gets the process exit code once it has exited, or -1 before then.</summary>
    public int ExitCode
    {
        get
        {
            if (_exitCode is { } code)
            {
                return code;
            }

            // The event can lag a direct wait; read the handle when it is final.
            Process? process = _process;
            if (process is null)
            {
                return -1;
            }

            try
            {
                return process.HasExited ? process.ExitCode : -1;
            }
            catch (InvalidOperationException)
            {
                return -1;
            }
        }
    }

    /// <summary>Gets how long the core has been running.</summary>
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

    /// <summary>
    /// Writes the configuration and starts the core. The process is started
    /// detached from any shell window; call <see cref="WaitForWindow"/> before
    /// embedding it.
    /// </summary>
    /// <exception cref="XemuConfigurationException">The settings are unusable or the process could not start.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is not null)
        {
            throw new InvalidOperationException("The core process was already started.");
        }

        _settings.EnsureValid();
        Directory.CreateDirectory(_settings.WorkingDirectory);
        File.WriteAllText(ConfigPath, XemuTomlWriter.Build(_settings));

        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.CorePath,
            Arguments = "-config_path " + Quote(ConfigPath),
            WorkingDirectory = _settings.WorkingDirectory,
            UseShellExecute = false,
        };

        if (!string.IsNullOrWhiteSpace(_settings.DisplaySinkName))
        {
            startInfo.Environment["XGRID_DISPLAY_SHM"] = _settings.DisplaySinkName;
        }

        if (!string.IsNullOrWhiteSpace(_settings.InputSinkName))
        {
            startInfo.Environment["XGRID_INPUT_SHM"] = _settings.InputSinkName;
        }

        try
        {
            _process = Process.Start(startInfo)
                       ?? throw new XemuConfigurationException("The core process did not start.");
            _startedAt = Stopwatch.GetTimestamp();
            _stoppedAt = 0;
            _exitCode = null;
            Process started = _process;
            // Record the code from the exit event: a Process handle can be
            // released between a wait and a later read, and the shell reports
            // the code in its log.
            started.EnableRaisingEvents = true;
            started.Exited += (_, _) => RecordExit(started);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new XemuConfigurationException("The core could not be started: " + exception.Message, exception);
        }
    }

    /// <summary>
    /// Returns the core's window if it exists yet, without blocking. The window
    /// is created on the core's own thread a second or two after start, so a
    /// shell polls this instead of waiting on its UI thread.
    /// </summary>
    /// <returns>The window handle, or <see cref="IntPtr.Zero"/>.</returns>
    public IntPtr TryFindWindow()
    {
        if (_window != IntPtr.Zero && !Win32Window.IsWindow(_window))
        {
            // The core recreated its window during start-up; look again.
            _window = IntPtr.Zero;
        }

        if (_window == IntPtr.Zero && IsRunning)
        {
            // The embedded core opens a hidden window titled XGridCore; fall
            // back to the visible-window search for an unpatched core.
            _window = XemuWindowLocator.FindTitled(ProcessId, "XGridCore");
            if (_window == IntPtr.Zero)
            {
                _window = XemuWindowLocator.Find(ProcessId);
            }
        }

        return _window;
    }

    /// <summary>Waits for the core's main window to be created.</summary>
    /// <param name="timeout">How long to wait (30 s is generous for a cold start).</param>
    /// <returns>The window handle, or <see cref="IntPtr.Zero"/> on timeout.</returns>
    public IntPtr WaitForWindow(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_window != IntPtr.Zero)
        {
            return _window;
        }

        if (!IsRunning)
        {
            return IntPtr.Zero;
        }

        _window = XemuWindowLocator.WaitForMainWindow(ProcessId, timeout, () => !IsRunning);
        return _window;
    }

    /// <summary>
    /// Asks the core to power off by closing its window, without waiting. The
    /// caller observes the exit through <see cref="IsRunning"/> or the shell's
    /// liveness poll.
    /// </summary>
    public void RequestClose()
    {
        if (_window != IntPtr.Zero && Win32Window.IsWindow(_window))
        {
            _ = Win32Window.PostMessageW(_window, Win32Window.WmClose, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Terminates the core process immediately (after a failed graceful stop).</summary>
    public void Kill()
    {
        Process? process = _process;
        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            RecordExit(process);
        }
    }

    /// <summary>
    /// Shuts the core down. A window close is the core's own power-off path
    /// (it flushes storage and saves its settings), so the process is asked to
    /// close first and only killed when it does not exit in time.
    /// </summary>
    /// <param name="grace">How long to wait for a graceful exit.</param>
    /// <returns>True when the process exited within the grace period.</returns>
    public bool Stop(TimeSpan grace)
    {
        Process? process = _process;
        if (process is null)
        {
            return true;
        }

        if (process.HasExited)
        {
            RecordExit(process);
            return true;
        }

        RequestClose();

        try
        {
            if (process.WaitForExit(grace))
            {
                RecordExit(process);
                _stoppedAt = Stopwatch.GetTimestamp();
                return true;
            }

            process.Kill(entireProcessTree: true);
            bool killed = process.WaitForExit(TimeSpan.FromSeconds(5));
            RecordExit(process);
            _stoppedAt = Stopwatch.GetTimestamp();
            return killed;
        }
        catch (InvalidOperationException)
        {
            // Exited between the checks: nothing left to stop.
            RecordExit(process);
            _stoppedAt = Stopwatch.GetTimestamp();
            return true;
        }
    }

    private void RecordExit(Process process)
    {
        _stoppedAt = _stoppedAt == 0 ? Stopwatch.GetTimestamp() : _stoppedAt;
        if (_exitCode is not null)
        {
            return;
        }

        try
        {
            _exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // Not exited yet, or the handle is gone; a later read can retry.
        }
    }

    /// <summary>Reads the tail of the core log for diagnostics.</summary>
    /// <param name="maxCharacters">Maximum characters to return.</param>
    /// <returns>The log tail, or an empty string when no log exists yet.</returns>
    public string ReadLogTail(int maxCharacters = 16 * 1024)
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return string.Empty;
            }

            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, stream.Length - maxCharacters);
            _ = stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
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

    /// <summary>Stops the core (if running) and releases the process handle.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = Stop(TimeSpan.FromSeconds(5));
        _process?.Dispose();
        _process = null;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
