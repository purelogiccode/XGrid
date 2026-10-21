using System;
using System.Globalization;
using System.IO;
using System.Threading;
using XGrid.Bus.Net;
using XGrid.Frontend.Avalonia.Models;
using XGrid.Host;

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
    private long _nextProgressLog;
    private ushort _padButtons;
    private byte[] _padAnalog = new byte[8];
    private short[] _padSticks = new short[4];
    private bool _padDirty;

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

        EmulatorSettings snapshot = settings.Clone();
        Interlocked.Exchange(ref _steps, 0);
        Interlocked.Exchange(ref _frames, 0);
        _lastPresentedFrame = 0;
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
                $"Console powered on · {settings.RamMb} MB RAM · {DescribeCpu(settings)}"));

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
            machine?.HddOverlay?.Dispose();
            machine?.Dvd?.Dispose();
            socketBackend?.Dispose();
            _running = false;
            _paused = false;
            Stopped?.Invoke(this, new SessionStoppedEventArgs(faulted, reason, Steps));
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
        machine.Reset();
        ConfigureCpu(machine, settings);
        return machine;
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
        if (presented == _lastPresentedFrame)
        {
            return;
        }

        _lastPresentedFrame = presented;
        Interlocked.Exchange(ref _frames, unchecked((long)presented));
        if (machine.Nv2A.TryCopyPresentedFrame(out uint width, out uint height, out byte[] bgra)
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
