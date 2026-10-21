using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using XGrid.Bus.Net;
using XGrid.Frontend.Avalonia.Models;
using XGrid.GpuGl;
using XGrid.Host;
using XGrid.Nv2a;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Describes why an emulation run ended.</summary>
public sealed class SessionStoppedEventArgs : EventArgs
{
    /// <summary>Creates the event payload.</summary>
    public SessionStoppedEventArgs(bool faulted, string message, long steps)
    {
        Faulted = faulted;
        Message = message;
        Steps = steps;
    }

    /// <summary>Gets a value indicating whether the run ended on an exception.</summary>
    public bool Faulted { get; }

    /// <summary>Gets the human-readable stop reason.</summary>
    public string Message { get; }

    /// <summary>Gets the number of guest instructions executed.</summary>
    public long Steps { get; }
}

/// <summary>
/// Owns the in-process <see cref="XboxMachine"/> on a dedicated emulation thread and
/// publishes presented frames, progress counters, and log lines to the UI.
/// </summary>
public sealed class EmulatorSession : IDisposable
{
    private const int BatchInstructions = 65536;
    private const long ProgressLogInterval = 250_000_000;

    // The display is a live scanout, not an event: guests that render straight
    // into the framebuffer never flip, so the picture is sampled on a timer as
    // well as on flips (~30 Hz, the panel refresh the UI can use).
    private static readonly long DisplayCaptureInterval = Stopwatch.Frequency / 30;

    private readonly Lock _padLock = new();
    private readonly Lock _machineLock = new();
    private readonly ManualResetEventSlim _runGate = new(initialState: true);

    private CancellationTokenSource? _cancellation;
    private Thread? _thread;
    private XboxMachine? _machine;
    private volatile bool _running;
    private volatile bool _paused;
    private volatile bool _disposed;
    private long _steps;
    private long _frames;
    private int _eip;
    private ulong _lastPresentedFrame;
    private long _lastDisplayCapture;
    private long _nextProgressLog;
    private ushort _padButtons;
    private byte[] _padAnalog = new byte[8];
    private short[] _padSticks = new short[4];
    private bool _padDirty;

    // Title identity (V2 per-title overrides): resolved from the launched image
    // once per run so the override dictionary can key by XBE title id instead
    // of a file path, and so the UI can show what was launched.
    private uint? _titleId;
    private string _titleName = string.Empty;
    private bool _titleOverrideApplied;

    /// <summary>Raised on the emulation thread when a new presented frame is available.</summary>
    public event EventHandler<FramePacket>? FrameReady;

    /// <summary>Raised on the emulation thread with a progress or error line.</summary>
    public event EventHandler<string>? Log;

    /// <summary>Raised on the emulation thread when a run ends.</summary>
    public event EventHandler<SessionStoppedEventArgs>? Stopped;

    /// <summary>Gets a value indicating whether a run is active.</summary>
    public bool IsRunning => _running;

    /// <summary>Gets a value indicating whether the run is paused.</summary>
    public bool IsPaused => _paused;

    /// <summary>Gets the number of guest instructions executed by the current run.</summary>
    public long Steps => Interlocked.Read(ref _steps);

    /// <summary>Gets the number of presented frames observed by the current run.</summary>
    public long Frames => Interlocked.Read(ref _frames);

    /// <summary>Gets the current guest instruction pointer.</summary>
    public uint Eip => unchecked((uint)Volatile.Read(ref _eip));

    /// <summary>Gets the XBE title id of the running disc (null when unknown, e.g. a CHD or the dashboard).</summary>
    public uint? TitleId => _titleId;

    /// <summary>Gets the XBE certificate title name of the running disc (empty when unknown).</summary>
    public string TitleName => _titleName;

    /// <summary>Gets a value indicating whether a per-title override was applied to this run.</summary>
    public bool TitleOverrideApplied => _titleOverrideApplied;

    /// <summary>
    /// Resolves the XBE title id of an image (ISO/XISO or loose XBE; a CHD has
    /// no reader yet and reports false) and returns it.
    /// </summary>
    /// <param name="imagePath">Disc image or executable path.</param>
    /// <param name="titleName">Certificate title name on success.</param>
    public static bool TryResolveTitle(string? imagePath, out uint titleId, out string titleName)
    {
        return DiscTitleId.TryRead(imagePath, out titleId, out titleName);
    }

    /// <summary>Starts a run with a snapshot of the supplied settings.</summary>
    /// <param name="settings">Configuration to boot with (cloned before the thread starts).</param>
    /// <param name="discPath">Optional disc image to insert, overriding the configured disc.</param>
    public void Start(EmulatorSettings settings, string? discPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_running)
        {
            throw new InvalidOperationException("The emulator is already running.");
        }

        if (string.IsNullOrWhiteSpace(settings.BiosPath) || !File.Exists(settings.BiosPath))
        {
            throw new InvalidOperationException("A BIOS image is required. Choose one on the Settings page.");
        }

        // V2 per-title overrides: the title id is resolved once here, so a
        // title keeps its configuration when the image is renamed, moved or
        // re-wrapped. Unresolvable images fall back to the path key.
        _titleId = null;
        _titleName = string.Empty;
        if (!string.IsNullOrWhiteSpace(discPath) && DiscTitleId.TryRead(discPath, out uint titleId, out string name))
        {
            _titleId = titleId;
            _titleName = name;
        }

        EmulatorSettings snapshot = settings.WithTitleOverride(discPath, _titleId);
        _titleOverrideApplied = settings.FindTitleOverride(discPath, _titleId) is not null;
        Interlocked.Exchange(ref _steps, 0);
        Interlocked.Exchange(ref _frames, 0);
        _lastPresentedFrame = 0;
        _lastDisplayCapture = 0;
        _nextProgressLog = ProgressLogInterval;
        _paused = false;
        _running = true;
        _runGate.Set();
        _cancellation = new CancellationTokenSource();

        var thread = new Thread(() => RunLoop(snapshot, discPath, _cancellation.Token))
        {
            IsBackground = true,
            Name = "XGrid emulation",
        };
        _thread = thread;
        thread.Start();
    }

    /// <summary>Pauses guest execution after the current batch.</summary>
    public void Pause()
    {
        if (!_running)
        {
            return;
        }

        _paused = true;
        _runGate.Reset();
    }

    /// <summary>Resumes a paused run.</summary>
    public void Resume()
    {
        if (!_running)
        {
            return;
        }

        _paused = false;
        _runGate.Set();
    }

    /// <summary>Requests a stop and waits briefly for the emulation thread to exit.</summary>
    public void Stop()
    {
        CancellationTokenSource? cancellation = _cancellation;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        _runGate.Set();
        _thread?.Join(TimeSpan.FromSeconds(5));
    }

    /// <summary>Updates the emulated player-1 pad; applied at the next batch boundary.</summary>
    public void SetPad(ushort buttons, ReadOnlySpan<byte> analogButtons, ReadOnlySpan<short> sticks)
    {
        lock (_padLock)
        {
            _padButtons = buttons;
            analogButtons.Slice(0, Math.Min(analogButtons.Length, _padAnalog.Length)).CopyTo(_padAnalog);
            sticks.Slice(0, Math.Min(sticks.Length, _padSticks.Length)).CopyTo(_padSticks);
            _padDirty = true;
        }
    }

    /// <summary>
    /// Saves a snapshot while the emulation thread is parked between batches.
    /// </summary>
    /// <param name="path">Destination <c>.xgs</c> path.</param>
    /// <exception cref="InvalidOperationException">The console is not running.</exception>
    public void SaveState(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        WithMachine(machine => machine.SaveState(path));
    }

    /// <summary>
    /// Loads a snapshot while the emulation thread is parked between batches.
    /// </summary>
    /// <param name="path">Source <c>.xgs</c> path.</param>
    /// <exception cref="InvalidOperationException">The console is not running.</exception>
    public void LoadState(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        WithMachine(machine =>
        {
            machine.LoadState(path);

            // The restored frame is not the frame this session presented last;
            // reset the baseline so the next present is delivered to the UI.
            _lastPresentedFrame = 0;
        });
    }

    private void WithMachine(Action<XboxMachine> action)
    {
        XboxMachine machine = Volatile.Read(ref _machine)
            ?? throw new InvalidOperationException("The console is not running.");
        lock (_machineLock)
        {
            action(machine);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _cancellation?.Dispose();
        _cancellation = null;
        _thread = null;
        _runGate.Dispose();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A UI boundary must never let an emulator fault tear down the desktop application.")]
    private void RunLoop(EmulatorSettings settings, string? discPath, CancellationToken token)
    {
        XboxMachine? machine = null;
        SocketNetworkBackend? socketBackend = null;
        bool faulted = false;
        string reason = "Stopped";
        try
        {
            machine = CreateMachine(settings, discPath, ref socketBackend);
            Volatile.Write(ref _machine, machine);
            Log?.Invoke(this, FormattableString.Invariant(
                $"Console powered on · {settings.RamMb} MB RAM · {DescribeCpu(settings)} \u00B7 {DescribeGraphics(settings)}"));
            if (_titleId is uint runningTitleId)
            {
                string suffix = _titleOverrideApplied ? " (per-title override applied)" : string.Empty;
                string name = _titleName.Length == 0 ? string.Empty : " \u00B7 " + _titleName;
                Log?.Invoke(this, FormattableString.Invariant(
                    $"Title {runningTitleId:X8}{name}{suffix}"));
            }

            while (!token.IsCancellationRequested && !machine.Cpu.Context.Halted)
            {
                if (_paused)
                {
                    _runGate.Wait(token);
                    continue;
                }

                ApplyPad(machine);
                int executed;
                lock (_machineLock)
                {
                    // Saves and loads take the same lock, so they always observe a
                    // paused, quiescent machine rather than a half-run batch.
                    executed = machine.Run(BatchInstructions);
                }
                if (executed > 0)
                {
                    Interlocked.Add(ref _steps, executed);
                    Volatile.Write(ref _eip, unchecked((int)machine.Cpu.Context.Eip));
                }

                CaptureFrame(machine);
                LogProgressIfDue();
                if (executed == 0)
                {
                    if (machine.Cpu.Context.Halted)
                    {
                        reason = "The guest halted.";
                        break;
                    }

                    // A zero-length batch without a halt would spin the UI loop;
                    // back off one scheduler tick until the next batch is possible.
                    Thread.Sleep(1);
                }
            }

            if (machine.Cpu.Context.Halted)
            {
                reason = "The guest halted.";
            }
        }
        catch (OperationCanceledException)
        {
            reason = "Stopped by the user.";
        }
        catch (Exception exception)
        {
            faulted = true;
            reason = exception.Message;
            Log?.Invoke(this, $"Emulation fault: {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _machine, null);
            PersistEeprom(machine);
            DisposeGpuBackend(machine);
            machine?.HddOverlay?.Dispose();
            machine?.Dvd?.Dispose();
            socketBackend?.Dispose();
            _running = false;
            _paused = false;
            Stopped?.Invoke(this, new SessionStoppedEventArgs(faulted, reason, Steps));
        }
    }

    /// <summary>
    /// Writes the live EEPROM image to the frontend data folder so the guest's
    /// video mode / language / region settings survive across runs.
    /// </summary>
    private void PersistEeprom(XboxMachine? machine)
    {
        if (machine is null)
        {
            return;
        }

        try
        {
            byte[] image = new byte[EepromStateStore.ImageSize];
            machine.CopyEeprom(image);
            var store = new EepromStateStore(SettingsStore.ResolveDirectory());
            if (!store.TrySave(image))
            {
                Log?.Invoke(this, "EEPROM state could not be saved (the data folder is not writable).");
            }
        }
        catch (InvalidOperationException exception)
        {
            Log?.Invoke(this, "EEPROM state not saved: " + exception.Message);
        }
    }

    private XboxMachine CreateMachine(EmulatorSettings settings, string? discPath, ref SocketNetworkBackend? socketBackend)
    {
        uint ramSize = checked((uint)settings.RamMb * 1024u * 1024u);
        var machine = new XboxMachine(ramSize);
        byte[] bios = File.ReadAllBytes(settings.BiosPath);
        byte[] mcpx = string.IsNullOrWhiteSpace(settings.McpxRomPath) || !File.Exists(settings.McpxRomPath)
            ? Array.Empty<byte>()
            : File.ReadAllBytes(settings.McpxRomPath);
        machine.LoadBios(bios, mcpx);
        string firmwareDetail = mcpx.Length > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes BIOS + {1:N0} bytes MCPX ROM", bios.Length, mcpx.Length)
            : string.Format(CultureInfo.InvariantCulture, "{0:N0} bytes BIOS (no MCPX ROM)", bios.Length);
        Log?.Invoke(this, "Firmware: " + firmwareDetail);

        if (!string.IsNullOrWhiteSpace(settings.EepromPath) && File.Exists(settings.EepromPath))
        {
            machine.LoadEeprom(File.ReadAllBytes(settings.EepromPath));
            Log?.Invoke(this, "EEPROM image loaded.");
        }

        // The frontend keeps the console's user settings in its own eeprom.bin:
        // the configured firmware image is only the seed. A persisted image
        // therefore wins over the seed so video mode/language changes survive.
        var eepromState = new EepromStateStore(SettingsStore.ResolveDirectory());
        if (eepromState.TryLoad(out byte[] persistedEeprom))
        {
            machine.LoadEeprom(persistedEeprom);
            Log?.Invoke(this, "EEPROM state restored from " + eepromState.FilePath);
        }

        if (!string.IsNullOrWhiteSpace(settings.HddPath) && File.Exists(settings.HddPath))
        {
            machine.AttachHdd(settings.HddPath);
            Log?.Invoke(this, $"Hard disk attached: {Path.GetFileName(settings.HddPath)} (copy-on-write overlay)");
        }

        string? insert = !string.IsNullOrWhiteSpace(discPath) && File.Exists(discPath)
            ? discPath
            : !string.IsNullOrWhiteSpace(settings.DvdPath) && File.Exists(settings.DvdPath)
                ? settings.DvdPath
                : null;
        if (insert is not null)
        {
            machine.AttachDvd(insert);
            Log?.Invoke(this, $"Disc inserted: {Path.GetFileName(insert)}");
        }

        if (string.Equals(settings.NetworkBackend, "socket", StringComparison.Ordinal))
        {
            var backend = new SocketNetworkBackend();
            socketBackend = backend;
            machine.Net.Backend = backend;
        }

        machine.Net.SetLinkUp(settings.NetworkLinkUp);
        machine.SevenEosQuirkEnabled = settings.EosQuirk;

        // NV2A fidelity switches (both opt-in at the CLI): the fixed-function
        // vertex stage replaces the resident program for mode-0 draws, and the
        // texture filter mode honors the TEXFILTER registers instead of the
        // legacy nearest sampler.
        machine.Nv2A.FixedFunctionTransform = settings.FixedFunctionVertex;
        machine.Nv2A.TextureFilterRegisters =
            string.Equals(settings.TextureFilter, "registers", StringComparison.Ordinal);

        machine.Reset();
        ConfigureCpu(machine, settings);
        AttachGpuBackend(machine, settings);
        return machine;
    }

    /// <summary>
    /// Attaches the host OpenGL backend when the configuration asks for it.
    /// </summary>
    /// <remarks>
    /// The backend is created on this thread (the emulation thread owns the WGL
    /// context for its whole lifetime) and renders offscreen into its own
    /// framebuffer, so nothing is embedded in the Avalonia visual tree: the
    /// display path (<see cref="Nv2AMmio.TryCopyDisplayFrame"/>) defers to the
    /// backend readback and the pixels still arrive as a
    /// <see cref="FramePacket"/>. Replacement mode is the only mode offered -
    /// mirror mode still runs the software rasterizer for every draw, so it
    /// buys no speed, and the two writers are never mixed. Any failure (a
    /// non-Windows host, no GL driver, a failed probe) falls back to the
    /// software rasterizer for the whole session rather than failing the run.
    /// </remarks>
    private void AttachGpuBackend(XboxMachine machine, EmulatorSettings settings)
    {
        if (!settings.UsesGpuBackend)
        {
            return;
        }

        if (GlBackendFactory.TryCreate(
                machine.GpuMemory,
                windowEnabled: false,
                replaceSoftware: true,
                frameReadback: true,
                out IPgraphBackend? backend,
                out string why))
        {
            machine.Nv2A.Backend = backend;
            Log?.Invoke(this, "GPU renderer: OpenGL replace mode · " + why);
            return;
        }

        Log?.Invoke(this, "GPU renderer: OpenGL unavailable (" + why + ") — using the software rasterizer");
    }

    /// <summary>
    /// Releases the host GPU backend (its hidden window, device context and GL
    /// objects) on the emulation thread that created it.
    /// </summary>
    private static void DisposeGpuBackend(XboxMachine? machine)
    {
        if (machine?.Nv2A.Backend is IPgraphBackend backend)
        {
            machine.Nv2A.Backend = null;
            if (backend is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static void ConfigureCpu(XboxMachine machine, EmulatorSettings settings)
    {
        machine.Cpu.DecodeCacheEnabled = settings.DecodeCache || settings.FastMode;
        if (!settings.FastMode)
        {
            return;
        }

        machine.Cpu.JitBuildThreshold = settings.JitThreshold;
        machine.Cpu.JitSpinSkip = settings.SpinSkip;
        machine.Cpu.JitRawBlocks = settings.RawBlocks;
        machine.Cpu.JitEnabled = true;
    }

    private static string DescribeCpu(EmulatorSettings settings)
    {
        if (!settings.FastMode)
        {
            return "accurate interpreter";
        }

        string emitter = settings.RawBlocks ? "raw block JIT" : "block JIT";
        return settings.SpinSkip ? emitter + " + idle-spin skip" : emitter;
    }

    private static string DescribeGraphics(EmulatorSettings settings)
    {
        string renderer = settings.UsesGpuBackend
            ? "OpenGL renderer (replace)"
            : "software rasterizer";
        string vertex = settings.FixedFunctionVertex
            ? "fixed-function vertex stage"
            : "resident vertex program";
        string filter = string.Equals(settings.TextureFilter, "registers", StringComparison.Ordinal)
            ? "TEXFILTER registers"
            : "legacy nearest sampler";
        return renderer + ", " + vertex + ", " + filter;
    }

    private void ApplyPad(XboxMachine machine)
    {
        ushort buttons;
        byte[] analog;
        short[] sticks;
        lock (_padLock)
        {
            if (!_padDirty)
            {
                return;
            }

            _padDirty = false;
            buttons = _padButtons;
            analog = (byte[])_padAnalog.Clone();
            sticks = (short[])_padSticks.Clone();
        }

        machine.Gamepad.SetInput(buttons, analog, sticks);
    }

    private void CaptureFrame(XboxMachine machine)
    {
        ulong presented = machine.Nv2A.FramesPresented;
        long now = Stopwatch.GetTimestamp();
        if (presented == _lastPresentedFrame && now - _lastDisplayCapture < DisplayCaptureInterval)
        {
            return;
        }

        _lastPresentedFrame = presented;
        _lastDisplayCapture = now;
        Interlocked.Exchange(ref _frames, unchecked((long)presented));
        if (machine.Nv2A.TryCopyDisplayFrame(out uint width, out uint height, out byte[] bgra)
            && width > 0 && height > 0 && bgra.Length >= width * height * 4)
        {
            FrameReady?.Invoke(this, new FramePacket(width, height, bgra));
        }
    }

    private void LogProgressIfDue()
    {
        long steps = Steps;
        if (steps < _nextProgressLog)
        {
            return;
        }

        _nextProgressLog += ProgressLogInterval;
        Log?.Invoke(this, FormattableString.Invariant(
            $"Executed {steps:N0} steps · EIP 0x{Eip:X8} · {Frames:N0} frames"));
    }
}
