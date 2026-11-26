using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

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
public sealed partial class XemuCoreProcess : IXemuCoreRuntime
{
    private const int SigTerm = 15;

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

    /// <summary>
    /// Validates the settings, creates the working directory and writes the
    /// generated configuration. Shared by the process and in-process hosts.
    /// </summary>
    /// <param name="settings">Launch settings.</param>
    /// <returns>The configuration path.</returns>
    public static string WriteConfiguration(XemuLaunchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.EnsureValid();
        Directory.CreateDirectory(settings.WorkingDirectory);
        string path = Path.Combine(settings.WorkingDirectory, "xemu.toml");
        File.WriteAllText(path, XemuTomlWriter.Build(settings));
        return path;
    }

    /// <summary>
    /// Returns the core arguments that are not part of the configuration file.
    /// Shared by the process and in-process hosts.
    /// </summary>
    /// <param name="settings">Launch settings.</param>
    public static System.Collections.Generic.IReadOnlyList<string> BuildExtraArguments(XemuLaunchSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var arguments = new System.Collections.Generic.List<string>();
        switch (settings.AudioBackend)
        {
            case AudioBackends.None:
                arguments.Add("-audio");
                arguments.Add(AudioBackends.None);
                break;
            case AudioBackends.Wav:
                arguments.Add("-audio");
                arguments.Add("wav,path=" + settings.AudioWavPath);
                break;
            case AudioBackends.Auto:
                break;
            default:
                string audio = settings.AudioBackend;
                string? deviceOption = AudioBackends.DeviceOptionKey(audio);
                if (!string.IsNullOrWhiteSpace(settings.AudioDevice) && deviceOption is not null)
                {
                    audio += "," + deviceOption + "=" + settings.AudioDevice;
                }

                arguments.Add("-audio");
                arguments.Add(audio);
                break;
        }

        AddChihiroLinkArguments(arguments, settings);
        return arguments;
    }

    /// <summary>
    /// Bridges the Chihiro network board's hub 0 to the linked cabinets over
    /// TCP. The host listens on port + each other cabinet's number; a client
    /// connects to the host on port + its own number. QEMU's socket netdev
    /// frames the Ethernet, and hubport puts each endpoint on the netboard's
    /// hub.
    /// </summary>
    private static void AddChihiroLinkArguments(
        System.Collections.Generic.List<string> arguments,
        XemuLaunchSettings settings)
    {
        if (!settings.ChihiroLinkEnabled)
        {
            return;
        }

        if (settings.ChihiroLinkCabinet == 1)
        {
            for (int cabinet = 2; cabinet <= settings.ChihiroLinkCabinets; cabinet++)
            {
                int port = settings.ChihiroLinkPort + cabinet;
                arguments.Add("-netdev");
                arguments.Add($"socket,id=chihiro-link{cabinet},listen=:{port}");
                arguments.Add("-netdev");
                arguments.Add($"hubport,id=chihiro-hub{cabinet},hubid=0,netdev=chihiro-link{cabinet}");
            }

            return;
        }

        int ownPort = settings.ChihiroLinkPort + settings.ChihiroLinkCabinet;
        arguments.Add("-netdev");
        arguments.Add(
            $"socket,id=chihiro-link,connect={settings.ChihiroLinkHost}:{ownPort}");
        arguments.Add("-netdev");
        arguments.Add("hubport,id=chihiro-hub,hubid=0,netdev=chihiro-link");
    }

    /// <summary>Asks the core to power off through its own shutdown path.</summary>
    public void RequestStop()
    {
        RequestClose();
    }

    /// <inheritdoc />
    public bool TrySetPresentationFilter(string filter)
    {
        // The separate process receives the filter at launch; there is no live
        // channel back into it.
        return false;
    }

    /// <inheritdoc />
    public bool TrySetFpsOverlay(bool enabled)
    {
        return false;
    }

    /// <inheritdoc />
    public bool TryRequestScreenshot()
    {
        // The separate process has no live ABI; the shell falls back to its
        // window capture.
        return false;
    }

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
            catch (InvalidOperationException exception)
            {
                Log.Debug(exception, "The core process ended while its running state was read");
                return false;
            }
        }
    }

    /// <summary>Gets the core process id, or 0 when it was never started.</summary>
    public int ProcessId => _process?.Id ?? 0;

    /// <summary>Gets a value indicating whether the core shares the shell's process.</summary>
    public bool IsInProcess => false;

    /// <summary>
    /// Gets the core's save-state controls. A separate process exposes no ABI,
    /// so the shell can only save states with the in-process core.
    /// </summary>
    public ISaveStateService? SaveStates => null;

    /// <summary>
    /// Gets the core's pause/step controls. A separate process exposes no ABI,
    /// so pause and frame step need the in-process core.
    /// </summary>
    public ICorePlayback? Playback => null;

    /// <summary>
    /// Gets the core's disc-change controls. A separate process exposes no ABI,
    /// so changing a disc without a restart needs the in-process core.
    /// </summary>
    public ICoreDiscs? Discs => null;

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
            catch (InvalidOperationException exception)
            {
                Log.Debug(exception, "The core exit code is not available yet");
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

        _ = WriteConfiguration(_settings);

        string arguments = "-config_path " + Quote(ConfigPath);
        foreach (string argument in BuildExtraArguments(_settings))
        {
            arguments += " " + Quote(argument);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.CorePath,
            Arguments = arguments,
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

        // The core owns filtering and the overlay. The separate-process path
        // passes the initial selection through the environment; a live change
        // needs the in-process ABI.
        startInfo.Environment["XGRID_PRESENT_FILTER"] = _settings.PresentationFilter;
        startInfo.Environment["XGRID_FPS_OVERLAY"] = _settings.FpsOverlay ? "1" : "0";

        // Tell the core the shell will reparent its window: create it hidden
        // and give it the title the locator matches, so it never flashes as a
        // second top-level window before the embed completes.
        startInfo.Environment["XGRID_EMBED"] =
            string.IsNullOrEmpty(_settings.DisplaySinkName) && XemuWindowEmbedder.IsSupported
                ? "1"
                : "0";

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
            Log.Error(exception, "The core could not be started");
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
        if (!XemuWindowEmbedder.IsSupported)
        {
            return IntPtr.Zero;
        }

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
        if (!XemuWindowEmbedder.IsSupported)
        {
            return IntPtr.Zero;
        }

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
        if (XemuWindowEmbedder.IsSupported)
        {
            // A headless launch never embeds, so the window may not be cached
            // yet. Find it now: a close request is the core's power-off path,
            // and skipping it lets the session kill the process instead.
            IntPtr window = _window;
            if (window == IntPtr.Zero || !Win32Window.IsWindow(window))
            {
                window = TryFindWindow();
                if (window == IntPtr.Zero)
                {
                    window = XemuWindowLocator.FindAnyWindow(ProcessId);
                }

                _window = window;
            }

            if (window != IntPtr.Zero)
            {
                _ = Win32Window.PostMessageW(window, Win32Window.WmClose, IntPtr.Zero, IntPtr.Zero);
            }

            return;
        }

        // The core owns its window off Windows, so there is no host window
        // handle to close. QEMU handles SIGTERM as a shutdown request, which is
        // the core's own power-off path and flushes storage.
        Process? process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                _ = KillUnix(process.Id, SigTerm);
            }
        }
        catch (InvalidOperationException exception)
        {
            Log.Debug(exception, "The core exited before the stop signal was sent");
        }
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int KillUnix(int processId, int signal);

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
        catch (InvalidOperationException exception)
        {
            Log.Debug(exception, "The core was already gone when it was killed");
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
        catch (InvalidOperationException exception)
        {
            Log.Debug(exception, "The core exited between the stop checks");
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
        catch (InvalidOperationException exception)
        {
            Log.Debug(exception, "The core exit code could not be recorded yet");
        }
    }

    /// <summary>Reads the tail of the core log for diagnostics.</summary>
    /// <param name="maxChars">Maximum characters to return.</param>
    /// <returns>The log tail, or an empty string when no log exists yet.</returns>
    public string ReadLogTail(int maxChars)
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return string.Empty;
            }

            using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, stream.Length - maxChars);
            _ = stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException exception)
        {
            Log.Debug(exception, "Could not read the core output stream");
            return string.Empty;
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Debug(exception, "The core output stream is not accessible");
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
