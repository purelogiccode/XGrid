using System;
using XGrid.Core.Configuration;
using XGrid.Core.Diagnostics;

namespace XGrid.Core.Emulation;

/// <summary>
/// Owns emulated time and device scheduling. This type must not depend on any frontend.
/// </summary>
public sealed class Emulator : IEmulator
{
    /// <summary>Creates an emulator for the given configuration.</summary>
    /// <param name="configuration">Validated configuration.</param>
    /// <param name="options">Optional runtime options; defaults to deterministic mode.</param>
    public Emulator(EmulatorConfiguration configuration, EmulatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Configuration = configuration.Validate();
        Options = options ?? EmulatorOptions.Default;
        Options.Validate();
        Clock = new EmulatedClock(Options.TimeMode);
        Scheduler = new Scheduler();
    }

    /// <inheritdoc />
    public EmulatorState State { get; private set; } = EmulatorState.Created;

    /// <summary>Gets the validated configuration.</summary>
    public EmulatorConfiguration Configuration { get; }

    /// <summary>Gets the runtime options.</summary>
    public EmulatorOptions Options { get; }

    /// <summary>Gets the emulated clock.</summary>
    public EmulatedClock Clock { get; }

    /// <summary>Gets the device scheduler.</summary>
    public Scheduler Scheduler { get; }

    /// <inheritdoc />
    public void Initialize()
    {
        if (State != EmulatorState.Created)
        {
            throw InvalidTransition(nameof(Initialize));
        }

        State = EmulatorState.Initialized;
        Log(LogLevel.Information, "Emulator initialized");
    }

    /// <inheritdoc />
    public void Reset()
    {
        if (State is not (EmulatorState.Initialized
            or EmulatorState.Running
            or EmulatorState.Paused
            or EmulatorState.Stopped))
        {
            throw InvalidTransition(nameof(Reset));
        }

        Clock.Reset();
        Scheduler.Reset();
        State = EmulatorState.Initialized;
        Log(LogLevel.Information, "Emulator reset");
    }

    /// <inheritdoc />
    public void Start()
    {
        if (State is not (EmulatorState.Initialized or EmulatorState.Stopped))
        {
            throw InvalidTransition(nameof(Start));
        }

        State = EmulatorState.Running;
        Log(LogLevel.Information, "Emulator started");
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (State != EmulatorState.Running)
        {
            throw InvalidTransition(nameof(Pause));
        }

        State = EmulatorState.Paused;
        Log(LogLevel.Information, "Emulator paused");
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (State != EmulatorState.Paused)
        {
            throw InvalidTransition(nameof(Resume));
        }

        State = EmulatorState.Running;
        Log(LogLevel.Information, "Emulator resumed");
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (State is not (EmulatorState.Initialized or EmulatorState.Running or EmulatorState.Paused))
        {
            throw InvalidTransition(nameof(Stop));
        }

        State = EmulatorState.Stopped;
        Log(LogLevel.Information, "Emulator stopped");
    }

    /// <summary>Advances emulated time by the given number of cycles and ticks all devices.</summary>
    /// <returns>The total number of cycles elapsed since the last reset.</returns>
    /// <exception cref="InvalidOperationException">The emulator is not running.</exception>
    public ulong Advance(ulong cycles)
    {
        if (State != EmulatorState.Running)
        {
            throw InvalidTransition(nameof(Advance));
        }

        Clock.Advance(cycles);
        Scheduler.Tick(cycles);
        return Clock.Cycles;
    }

    /// <summary>Advances emulated time by one configured deterministic step.</summary>
    /// <returns>The total number of cycles elapsed since the last reset.</returns>
    public ulong AdvanceStep()
    {
        return Advance(Options.CyclesPerStep);
    }

    private InvalidOperationException InvalidTransition(string operation)
    {
        return new InvalidOperationException($"Operation '{operation}' is not valid in state {State}.");
    }

    private static void Log(LogLevel level, string message)
    {
        if (XGridLog.IsEnabled(LogCategory.Core, level))
        {
            XGridLog.Write(LogCategory.Core, level, message);
        }
    }
}
