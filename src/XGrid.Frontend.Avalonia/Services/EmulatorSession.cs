using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using XGrid.Audio;
using XGrid.Bus.Net;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Host;
using XGrid.Nv2a;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Describes why an emulation run ended.</summary>
/// <remarks>Creates the event payload.</remarks>
public sealed class SessionStoppedEventArgs(bool faulted, string message, long steps) : EventArgs
{

    /// <summary>Gets a value indicating whether the run ended on an exception.</summary>
    public bool Faulted { get; } = faulted;

    /// <summary>Gets the human-readable stop reason.</summary>
    public string Message { get; } = message;

    /// <summary>Gets the number of guest instructions executed.</summary>
    public long Steps { get; } = steps;
}

/// <summary>
/// Owns the in-process <see cref="XboxMachine"/> on a dedicated emulation thread and
/// publishes presented frames, progress counters, and log lines to the UI.
/// </summary>
public sealed class EmulatorSession : IDisposable
{
    private const int BatchInstructions = 65536;
    private const long ProgressLogInterval = 250_000_000;

    // Frame stepping (pause + advance): a step keeps the pause gate closed and
    // releases exactly the requested work. A frame step is budgeted, because a
    // guest can idle for millions of steps between flips.
    private const long FrameStepFallbackInstructions = 8_000_000;

    // The display is a live scanout, not an event: guests that render straight
    // into the framebuffer never flip, so the picture is sampled on a timer as
    // well as on flips (~30 Hz, the panel refresh the UI can use).
    private static readonly long DisplayCaptureInterval = Stopwatch.Frequency / 30;

    private readonly Lock _padLock = new();
    private readonly Lock _requestLock = new();
    private readonly Queue<StateRequest> _requests = new();
    private readonly ManualResetEventSlim _runGate = new(initialState: true);

    private CancellationTokenSource? _cancellation;
    private Thread? _thread;
    private XboxMachine? _machine;
    private WasapiAudioBackend? _audioBackend;
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
    private readonly byte[] _padAnalog = new byte[8];
    private readonly short[] _padSticks = new short[4];
    private bool _padDirty;
    private long _stepBudget;
    private long _stepTargetFrame;

    // Title identity (V2 per-title overrides): resolved from the launched image
    // once per run so the override dictionary can key by XBE title id instead
    // of a file path, and so the UI can show what was launched.

    // Renderer state. The GPU backend (when the profile selects OpenGL) is
    // created, used and disposed on the emulation thread: a WGL context is
    // current on the thread that made it, and the emulation thread is the only
    // one allowed to touch guest or backend state. The display source itself is
    // decided by Nv2AMmio.TryCopyDisplayFrame (backend readback for a surface
    // the backend rendered, the RAM scanout for a direct-framebuffer program's
    // own buffer), so the session keeps no renderer flag of its own.

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
    private uint? TitleId { get; set; }

    /// <summary>Gets the XBE certificate title name of the running disc (empty when unknown).</summary>
    private string TitleName { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether a per-title override was applied to this run.</summary>
    private bool TitleOverrideApplied { get; set; }

    /// <summary>
    /// Resolves the XBE title id of an image (ISO/XISO or loose XBE; a CHD has
    /// no reader yet and reports false) and returns it.
    /// </summary>
    /// <param name="imagePath">Disc image or executable path.</param>
    /// <param name="titleId">Parsed XBE title id on success.</param>
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
        TitleId = null;
        TitleName = string.Empty;
        if (!string.IsNullOrWhiteSpace(discPath) && DiscTitleId.TryRead(discPath, out uint titleId, out string name))
        {
            TitleId = titleId;
            TitleName = name;
        }

        EmulatorSettings snapshot = settings.WithTitleOverride(discPath, TitleId);
        TitleOverrideApplied = settings.FindTitleOverride(discPath, TitleId) is not null;
        Interlocked.Exchange(ref _steps, 0);
        Interlocked.Exchange(ref _frames, 0);
        _lastPresentedFrame = 0;
        _lastDisplayCapture = 0;
        _nextProgressLog = ProgressLogInterval;
        _paused = false;
        _running = true;
        Interlocked.Exchange(ref _stepBudget, 0);
        Interlocked.Exchange(ref _stepTargetFrame, 0);
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

        Interlocked.Exchange(ref _stepBudget, 0);
        Interlocked.Exchange(ref _stepTargetFrame, 0);
        _paused = false;
        _runGate.Set();
    }

    /// <summary>
    /// Advances a paused console by one presented frame (or by
    /// <paramref name="maxInstructions"/> when the guest presents nothing in
    /// that budget, e.g. while it idles). The console stays paused.
    /// </summary>
    /// <param name="maxInstructions">Upper bound on instructions for the step.</param>
    /// <returns>True when the step was accepted (the console is running and paused).</returns>
    public bool StepFrame(long maxInstructions = FrameStepFallbackInstructions)
    {
        if (!_running || !_paused)
        {
            return false;
        }

        if (maxInstructions <= 0)
        {
            maxInstructions = FrameStepFallbackInstructions;
        }

        Interlocked.Exchange(ref _stepTargetFrame, Frames + 1);
        Interlocked.Exchange(ref _stepBudget, maxInstructions);
        _runGate.Set();
        return true;
    }

    /// <summary>
    /// Advances a paused console by an instruction count (a finer-grained step
    /// than <see cref="StepFrame"/>). The console stays paused.
    /// </summary>
    /// <param name="instructions">Instruction budget.</param>
    /// <returns>True when the step was accepted.</returns>
    public bool StepInstructions(long instructions)
    {
        if (!_running || !_paused || instructions <= 0)
        {
            return false;
        }

        Interlocked.Exchange(ref _stepTargetFrame, 0);
        Interlocked.Add(ref _stepBudget, instructions);
        _runGate.Set();
        return true;
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
        RunOnEmulationThread(machine => machine.SaveState(path));
    }

    /// <summary>
    /// Loads a snapshot while the emulation thread is parked between batches.
    /// </summary>
    /// <param name="path">Source <c>.xgs</c> path.</param>
    /// <exception cref="InvalidOperationException">The console is not running.</exception>
    public void LoadState(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RunOnEmulationThread(machine =>
        {
            machine.LoadState(path);

            // The restored frame is not the frame this session presented last;
            // reset the baseline so the next present is delivered to the UI.
            _lastPresentedFrame = 0;
        });
    }

    /// <summary>
    /// Consumes the executed instructions from the step budget and re-closes
    /// the pause gate when the step is done (frame reached or budget spent).
    /// Only meaningful while paused; a running session ignores the budget.
    /// </summary>
    private void CompleteStep(int executed)
    {
        if (!_paused)
        {
            return;
        }

        long remaining = Interlocked.Add(ref _stepBudget, -executed);
        long target = Interlocked.Read(ref _stepTargetFrame);
        bool frameReached = target > 0 && Frames >= target;
        if (remaining > 0 && !frameReached)
        {
            return;
        }

        if (frameReached)
        {
            Interlocked.Exchange(ref _stepTargetFrame, 0);
            Interlocked.Exchange(ref _stepBudget, 0);
            Log?.Invoke(this, FormattableString.Invariant($"Stepped to frame {Frames:N0} · EIP 0x{Eip:X8}"));
        }
        else
        {
            // Clear only the spent budget: a step requested while this batch
            // ran must keep its own budget.
            Interlocked.CompareExchange(ref _stepBudget, 0, remaining);
            Log?.Invoke(this, FormattableString.Invariant(
                $"Stepped {executed:N0} instructions (frame-step budget exhausted at EIP 0x{Eip:X8})."));
        }

        if (Interlocked.Read(ref _stepBudget) == 0)
        {
            _runGate.Reset();
        }
    }

    /// <summary>
    /// Runs a machine operation on the emulation thread, between batches.
    /// </summary>
    /// <remarks>
    /// Device state is not the only thing with thread affinity: an attached GPU
    /// backend owns a GL context that is current on the emulation thread only,
    /// and a snapshot load resets backend state (<see cref="Nv2AMmio.LoadState"/>).
    /// Marshalling instead of running on the UI thread keeps every GL call on
    /// its owning thread and still observes a
    /// quiescent machine, because the loop serves requests with no batch in
    /// flight.
    /// </remarks>
    private void RunOnEmulationThread(Action<XboxMachine> work)
    {
        if (!_running || Volatile.Read(ref _machine) is null)
        {
            throw new InvalidOperationException("The console is not running.");
        }

        using var request = new StateRequest(work);
        lock (_requestLock)
        {
            _requests.Enqueue(request);
        }

        if (!request.Done.Wait(TimeSpan.FromSeconds(60)))
        {
            throw new InvalidOperationException("The console did not reach a quiescent point in time.");
        }

        if (request.Error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(request.Error).Throw();
        }
    }

    /// <summary>Serves queued snapshot requests on the emulation thread.</summary>
    private void ServeRequests(XboxMachine machine)
    {
        while (true)
        {
            StateRequest? request;
            lock (_requestLock)
            {
                request = _requests.Count > 0 ? _requests.Dequeue() : null;
            }

            if (request is null)
            {
                return;
            }

            try
            {
                request.Work(machine);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The caller sees the failure; the session must keep running.
                request.Complete(exception);
                continue;
            }

            request.Complete(error: null);
        }
    }

    /// <summary>Releases requests that can no longer be served (the loop is exiting).</summary>
    private void FailPendingRequests()
    {
        while (true)
        {
            StateRequest? request;
            lock (_requestLock)
            {
                request = _requests.Count > 0 ? _requests.Dequeue() : null;
            }

            if (request is null)
            {
                return;
            }

            request.Complete(new InvalidOperationException("The console stopped before the request was served."));
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
        IPgraphBackend? gpuBackend = null;
        SocketNetworkBackend? socketBackend = null;
        bool faulted = false;
        string reason = "Stopped";
        try
        {
            machine = CreateMachine(settings, discPath, ref socketBackend);
            Volatile.Write(ref _machine, machine);
            Log?.Invoke(this, FormattableString.Invariant(
                $"Console powered on · {settings.RamMb} MB RAM · {DescribeCpu(settings)} \u00B7 {DescribeGraphics(settings)}"));
            gpuBackend = AttachRenderer(machine, settings);
            if (TitleId is { } runningTitleId)
            {
                string suffix = TitleOverrideApplied ? " (per-title override applied)" : string.Empty;
                string name = TitleName.Length == 0 ? string.Empty : " \u00B7 " + TitleName;
                Log?.Invoke(this, FormattableString.Invariant(
                    $"Title {runningTitleId:X8}{name}{suffix}"));
            }

            while (!token.IsCancellationRequested && !machine.Cpu.Context.Halted)
            {
                // Snapshot requests run here, on the thread that owns the
                // devices (and the GL context), with no batch in flight.
                ServeRequests(machine);
                long stepBudget = Interlocked.Read(ref _stepBudget);
                bool stepping = _paused && stepBudget != 0;
                if (_paused && !stepping)
                {
                    // A timed gate wait keeps the loop able to serve a save or
                    // load request while the console is paused.
                    _runGate.Wait(TimeSpan.FromMilliseconds(50), token);
                    continue;
                }

                ApplyPad(machine);
                int batch = BatchInstructions;
                if (stepping && stepBudget < batch)
                {
                    batch = (int)stepBudget;
                }

                int executed = machine.Run(batch);
                if (executed > 0)
                {
                    Interlocked.Add(ref _steps, executed);
                    Volatile.Write(ref _eip, unchecked((int)machine.Cpu.Context.Eip));
                }

                CaptureFrame(machine);
                CompleteStep(executed);
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
            // Refuse new requests first, then release the ones still queued.
            _running = false;
            Volatile.Write(ref _machine, null);
            FailPendingRequests();
            DetachRenderer(machine, gpuBackend);
            _audioBackend?.Dispose();
            _audioBackend = null;
            PersistEeprom(machine); machine?.HddOverlay?.Dispose();
            machine?.Dvd?.Dispose();
            socketBackend?.Dispose();
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
            ? []
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

        // Audio: the APU DSP/VP runs the guest microcode and mixer; the live
        // WASAPI sink is the product output. A sink that cannot open (no
        // device, non-Windows) logs and stays silent - audio is never a
        // guest-visible fault.
        machine.Apu.ApuDspCore = settings.ApuDsp;
        if (settings.ApuDsp)
        {
            if (string.Equals(settings.AudioOutput, EmulatorSettings.AudioOutputLive, StringComparison.Ordinal))
            {
                try
                {
                    _audioBackend = new WasapiAudioBackend();
                    machine.Apu.AudioBackend = _audioBackend;
                    Log?.Invoke(this, "Audio: live WASAPI sink (48 kHz, 16-bit stereo).");
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Log?.Invoke(this, "Audio: live sink unavailable - " + exception.Message);
                }
            }
            else
            {
                Log?.Invoke(this, "Audio: APU DSP running with no host sink.");
            }
        }

        // NV2A fidelity switches (both opt-in at the CLI): the fixed-function
        // vertex stage replaces the resident program for mode-0 draws, and the
        // texture filter mode honors the TEXFILTER registers instead of the
        // legacy nearest sampler.
        machine.Nv2A.FixedFunctionTransform = settings.FixedFunctionVertex;
        machine.Nv2A.TextureFilterRegisters =
            string.Equals(settings.TextureFilter, "registers", StringComparison.Ordinal);

        // The per-method histogram is two dictionary updates per PGRAPH method
        // (~18M per 1B dashboard steps) and no frontend surface reads it; the
        // CLI drops it for the same reason (§204, measured there as a 6.2%
        // software-mode win). Guest state is untouched. The method total and
        // the method stream still run.
        machine.Nv2A.CollectMethodHistogram = false;

        machine.Reset();
        ConfigureCpu(machine, settings);
        return machine;
    }

    /// <summary>
    /// Attaches the renderer the profile asks for, on the emulation thread
    /// (the thread that owns the WGL context for its whole lifetime).
    /// Returns null when the session stays on the software rasterizer - either
    /// because that is the configured renderer or because the GPU path is
    /// unavailable on this host, which is logged with its reason rather than
    /// failing the boot. Never a mix: the decision is made once per power-on.
    /// </summary>
    private IPgraphBackend? AttachRenderer(XboxMachine machine, EmulatorSettings settings)
    {
        bool attached = GraphicsBackendHost.TryAttach(machine, settings, out string detail);
        Log?.Invoke(this, "Renderer: " + detail);
        if (!attached)
        {
            return null;
        }

        return machine.Nv2A.Backend;
    }

    /// <summary>
    /// Detaches and disposes the GPU backend on the emulation thread (the
    /// context is current there). Runs before the machine's devices are
    /// disposed, and never from the UI thread.
    /// </summary>
    private void DetachRenderer(XboxMachine? machine, IPgraphBackend? backend)
    {
        if (backend is null)
        {
            return;
        }

        try
        {
            GraphicsBackendHost.Detach(machine);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            // Teardown is best-effort: a context that is already gone must not
            // mask the session's real stop reason.
            Log?.Invoke(this, "Renderer: OpenGL teardown failed (" + exception.Message + ").");
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
        string renderer = EmulatorSettings.IsGpuRenderer(settings.Renderer) ? "OpenGL renderer" : "software renderer";
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

        // Display source. `TryCopyDisplayFrame` is the one decision point: with
        // a GPU backend attached it serves the backend's per-flip readback for a
        // surface the backend rendered, and the RAM scanout for a buffer the
        // backend never rendered (a direct-framebuffer program's output, which
        // exists only in RAM). Without a backend it is the display controller's
        // scanout, falling back to the flipped surface.
        bool captured = machine.Nv2A.TryCopyDisplayFrame(out uint width, out uint height, out byte[] bgra);

        if (captured && width > 0 && height > 0 && bgra.Length >= width * height * 4)
        {
            // The software scanout hands out a fresh buffer per capture, but a
            // backend readback is the backend's own buffer: the next flip
            // overwrites it while the UI thread may still be copying this frame.
            // Isolating it here (on the emulation thread, between flips) keeps
            // the renderer switch from tearing the picture.
            if (machine.Nv2A.Backend is not null)
            {
                bgra = (byte[])bgra.Clone();
            }

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

    /// <summary>One machine operation queued for the emulation thread.</summary>
    private sealed class StateRequest(Action<XboxMachine> work) : IDisposable
    {
        public Action<XboxMachine> Work { get; } = work;

        public ManualResetEventSlim Done { get; } = new(initialState: false);

        public Exception? Error { get; private set; }

        /// <summary>Signals the caller, tolerating a caller that already gave up.</summary>
        public void Complete(Exception? error)
        {
            Error = error;
            try
            {
                Done.Set();
            }
            catch (ObjectDisposedException)
            {
                // The waiting caller timed out and disposed the handle.
            }
        }

        public void Dispose()
        {
            Done.Dispose();
        }
    }
}
