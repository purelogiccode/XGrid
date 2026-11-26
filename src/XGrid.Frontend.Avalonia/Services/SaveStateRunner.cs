using System;
using System.Collections.Generic;
using System.Globalization;
using XGrid.Core.Xemu;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>The outcome of one save-state request the shell waited on.</summary>
public sealed class SaveStateResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="succeeded">True when the core finished the request.</param>
    /// <param name="message">What to log and toast.</param>
    public SaveStateResult(bool succeeded, string message)
    {
        Succeeded = succeeded;
        Message = message;
    }

    /// <summary>Gets a value indicating whether the core finished the request.</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the message that describes what happened.</summary>
    public string Message { get; }
}

/// <summary>
/// Turns the core's one-at-a-time save-state requests into a shell operation:
/// named slots, a single outstanding request and one completion message. The
/// core writes a save state as an internal snapshot of the hard disk image, so
/// the work happens on the core's own loop and this type waits by polling.
/// </summary>
public sealed class SaveStateRunner
{
    /// <summary>The number of quick slots the shell offers.</summary>
    public const int SlotCount = 4;

    /// <summary>The snapshot name used for save-on-exit and resume-on-start.</summary>
    public const string SessionName = "xgrid-session";

    private readonly ISaveStateService? _service;
    private readonly Func<long> _clock;
    private readonly long _timeoutMs;
    private bool _pending;
    private long _deadline;
    private XemuSaveStateOperation _operation;
    private int _slot;
    private string _name = string.Empty;

    /// <summary>Creates a runner over one core's save-state service.</summary>
    /// <param name="service">Core service, or null when the hosting mode has none.</param>
    /// <param name="timeoutMs">How long one request may take before it is abandoned.</param>
    /// <param name="clock">Millisecond clock (tests pass their own).</param>
    public SaveStateRunner(ISaveStateService? service, long timeoutMs = 120_000, Func<long>? clock = null)
    {
        _service = service;
        _timeoutMs = timeoutMs;
        _clock = clock ?? (static () => Environment.TickCount64);
    }

    /// <summary>Gets a value indicating whether the core can save states.</summary>
    public bool IsSupported => _service is not null;

    /// <summary>Gets the snapshot names reported by the last refresh.</summary>
    public IReadOnlyList<string> Names => _service?.Names ?? [];

    /// <summary>Gets a value indicating whether a request is still running.</summary>
    public bool IsBusy => _pending;

    /// <summary>Gets the slot the running (or last) request used, or 0.</summary>
    public int Slot => _slot;

    /// <summary>Gets the snapshot name the running (or last) request used.</summary>
    public string Name => _name;

    /// <summary>Gets a value indicating whether the last request was for the session snapshot.</summary>
    public bool IsSession => string.Equals(_name, SessionName, StringComparison.Ordinal);

    /// <summary>Gets the running (or last) request's operation.</summary>
    public XemuSaveStateOperation Operation => _operation;

    /// <summary>Gets the snapshot name for a slot.</summary>
    /// <param name="slot">Slot number, 1 to <see cref="SlotCount"/>.</param>
    public static string SlotName(int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"xgrid-slot-{slot}");
    }

    /// <summary>Gets a value indicating whether a slot number is one the shell offers.</summary>
    /// <param name="slot">Slot number.</param>
    public static bool IsValidSlot(int slot)
    {
        return slot >= 1 && slot <= SlotCount;
    }

    /// <summary>Queues a save of one slot.</summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    public bool TrySave(int slot, out string problem)
    {
        if (!ValidateSlot(slot, out problem))
        {
            return false;
        }

        return TryStart(XemuSaveStateOperation.Save, slot, SlotName(slot), out problem);
    }

    /// <summary>Queues a load of one slot.</summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    public bool TryLoad(int slot, out string problem)
    {
        if (!ValidateSlot(slot, out problem))
        {
            return false;
        }

        return TryStart(XemuSaveStateOperation.Load, slot, SlotName(slot), out problem);
    }

    /// <summary>Queues a delete of one slot.</summary>
    /// <param name="slot">Slot number.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    public bool TryDelete(int slot, out string problem)
    {
        if (!ValidateSlot(slot, out problem))
        {
            return false;
        }

        return TryStart(XemuSaveStateOperation.Delete, slot, SlotName(slot), out problem);
    }

    /// <summary>Queues a save of the automatic session snapshot.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    public bool TrySaveSession(out string problem)
    {
        return TryStart(XemuSaveStateOperation.Save, 0, SessionName, out problem);
    }

    /// <summary>Queues a load of the automatic session snapshot.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    public bool TryLoadSession(out string problem)
    {
        return TryStart(XemuSaveStateOperation.Load, 0, SessionName, out problem);
    }

    /// <summary>Queues a delete of the automatic session snapshot.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    public bool TryDeleteSession(out string problem)
    {
        return TryStart(XemuSaveStateOperation.Delete, 0, SessionName, out problem);
    }

    /// <summary>Queues a re-read of the snapshot names.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    public bool TryRefresh(out string problem)
    {
        return TryStart(XemuSaveStateOperation.Refresh, 0, string.Empty, out problem);
    }

    /// <summary>
    /// Reports the finished request, or null while it is still running. A
    /// request reports once; later calls return null.
    /// </summary>
    public SaveStateResult? Poll()
    {
        if (!_pending || _service is null)
        {
            return null;
        }

        XemuSaveStateStatus status = _service.Status;
        if (status == XemuSaveStateStatus.Pending)
        {
            if (_clock() <= _deadline)
            {
                return null;
            }

            _pending = false;
            return new SaveStateResult(false, "The emulator did not answer the save-state request.");
        }

        _pending = false;
        if (status == XemuSaveStateStatus.Failed)
        {
            string error = _service.Error;
            return new SaveStateResult(
                false,
                string.IsNullOrWhiteSpace(error) ? "The emulator could not complete the save state." : error);
        }

        return new SaveStateResult(true, DescribeSuccess());
    }

    private static bool ValidateSlot(int slot, out string problem)
    {
        if (IsValidSlot(slot))
        {
            problem = string.Empty;
            return true;
        }

        problem = "Save states use slots 1 to "
                  + SlotCount.ToString(CultureInfo.InvariantCulture)
                  + ".";
        return false;
    }

    private bool TryStart(
        XemuSaveStateOperation operation,
        int slot,
        string name,
        out string problem)
    {
        problem = string.Empty;
        if (_service is null)
        {
            problem = "Save states need the in-process emulator core.";
            return false;
        }

        if (_pending)
        {
            problem = "The emulator is still working on the previous save state.";
            return false;
        }

        if (operation != XemuSaveStateOperation.Refresh && slot != 0 && !IsValidSlot(slot))
        {
            problem = "Save states use slots 1 to " + SlotCount.ToString(CultureInfo.InvariantCulture) + ".";
            return false;
        }

        bool accepted = operation switch
        {
            XemuSaveStateOperation.Save => _service.TrySave(name, out problem),
            XemuSaveStateOperation.Load => _service.TryLoad(name, out problem),
            XemuSaveStateOperation.Delete => _service.TryDelete(name, out problem),
            _ => _service.TryRefresh(out problem),
        };
        if (!accepted)
        {
            return false;
        }

        _pending = true;
        _operation = operation;
        _slot = slot;
        _name = name;
        _deadline = _clock() + _timeoutMs;
        return true;
    }

    private string DescribeSuccess()
    {
        if (_slot == 0)
        {
            return _operation switch
            {
                XemuSaveStateOperation.Save => "Saved the session state.",
                XemuSaveStateOperation.Load => "Loaded the saved session.",
                XemuSaveStateOperation.Delete => "Deleted the saved session.",
                _ => "Save states: " + (_service?.Names.Count ?? 0).ToString(CultureInfo.InvariantCulture) + " found.",
            };
        }

        string number = _slot.ToString(CultureInfo.InvariantCulture);
        return _operation switch
        {
            XemuSaveStateOperation.Save => "Saved state to slot " + number + ".",
            XemuSaveStateOperation.Load => "Loaded state from slot " + number + ".",
            XemuSaveStateOperation.Delete => "Deleted the state in slot " + number + ".",
            _ => "Save states: " + (_service?.Names.Count ?? 0).ToString(CultureInfo.InvariantCulture) + " found.",
        };
    }
}
