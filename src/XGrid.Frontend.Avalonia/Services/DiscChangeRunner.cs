using System;
using XGrid.Core.Xemu;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>Which disc operation the shell queued.</summary>
public enum DiscChangeOperation
{
    /// <summary>No request was made.</summary>
    None = 0,

    /// <summary>Inserting a disc image.</summary>
    Load = 1,

    /// <summary>Ejecting the inserted disc.</summary>
    Eject = 2,
}

/// <summary>The outcome of one disc request the shell waited on.</summary>
public sealed class DiscChangeResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="succeeded">True when the core finished the request.</param>
    /// <param name="operation">The queued operation.</param>
    /// <param name="path">The image that was inserted (empty for an eject).</param>
    /// <param name="message">What to log and toast.</param>
    public DiscChangeResult(bool succeeded, DiscChangeOperation operation, string path, string message)
    {
        Succeeded = succeeded;
        Operation = operation;
        Path = path;
        Message = message;
    }

    /// <summary>Gets a value indicating whether the core finished the request.</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the operation that was queued.</summary>
    public DiscChangeOperation Operation { get; }

    /// <summary>Gets the inserted image path, or an empty string.</summary>
    public string Path { get; }

    /// <summary>Gets the message that describes what happened.</summary>
    public string Message { get; }
}

/// <summary>
/// Turns the core's one-at-a-time disc requests into a shell operation: a
/// single outstanding request and one completion message. The core changes the
/// block backend on its own main loop, so the shell queues the operation and
/// waits by polling.
/// </summary>
public sealed class DiscChangeRunner
{
    private readonly ICoreDiscs? _discs;
    private readonly Func<long> _clock;
    private readonly long _timeoutMs;
    private bool _pending;
    private long _deadline;
    private DiscChangeOperation _operation;
    private string _path = string.Empty;

    /// <summary>Creates a runner over one core's disc-change controls.</summary>
    /// <param name="discs">Core service, or null when the hosting mode has none.</param>
    /// <param name="timeoutMs">How long one request may take before it is abandoned.</param>
    /// <param name="clock">Millisecond clock (tests pass their own).</param>
    public DiscChangeRunner(ICoreDiscs? discs, long timeoutMs = 30_000, Func<long>? clock = null)
    {
        _discs = discs;
        _timeoutMs = timeoutMs;
        _clock = clock ?? (static () => Environment.TickCount64);
    }

    /// <summary>Gets a value indicating whether the core can change discs.</summary>
    public bool IsSupported => _discs is not null;

    /// <summary>Gets a value indicating whether a request is still running.</summary>
    public bool IsBusy => _pending;

    /// <summary>Gets the operation of the running (or last) request.</summary>
    public DiscChangeOperation Operation => _operation;

    /// <summary>Gets the image path of the running (or last) request.</summary>
    public string Path => _path;

    /// <summary>Queues an insertion of one disc image.</summary>
    /// <param name="path">ISO/XISO, CHD or ZArchive image.</param>
    /// <param name="problem">Why the request was refused, if it was.</param>
    public bool TryLoad(string path, out string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return TryStart(DiscChangeOperation.Load, path, out problem);
    }

    /// <summary>Queues an eject.</summary>
    /// <param name="problem">Why the request was refused, if it was.</param>
    public bool TryEject(out string problem)
    {
        return TryStart(DiscChangeOperation.Eject, string.Empty, out problem);
    }

    /// <summary>
    /// Reports the finished request, or null while it is still running. A
    /// request reports once; later calls return null.
    /// </summary>
    public DiscChangeResult? Poll()
    {
        if (!_pending || _discs is null)
        {
            return null;
        }

        XemuDiscStatus status = _discs.Status;
        if (status == XemuDiscStatus.Pending)
        {
            if (_clock() <= _deadline)
            {
                return null;
            }

            _pending = false;
            return new DiscChangeResult(
                false, _operation, _path, "The emulator did not answer the disc change.");
        }

        _pending = false;
        if (status == XemuDiscStatus.Failed)
        {
            string error = _discs.Error;
            return new DiscChangeResult(
                false,
                _operation,
                _path,
                string.IsNullOrWhiteSpace(error) ? "The emulator could not change the disc." : error);
        }

        return new DiscChangeResult(true, _operation, _path, DescribeSuccess());
    }

    private bool TryStart(DiscChangeOperation operation, string path, out string problem)
    {
        problem = string.Empty;
        if (_discs is null)
        {
            problem = "Changing discs needs the in-process emulator core.";
            return false;
        }

        if (_pending)
        {
            problem = "A disc change is still in progress.";
            return false;
        }

        bool accepted = operation == DiscChangeOperation.Load
            ? _discs.TryLoad(path, out problem)
            : _discs.TryEject(out problem);
        if (!accepted)
        {
            return false;
        }

        _pending = true;
        _operation = operation;
        _path = path;
        _deadline = _clock() + _timeoutMs;
        return true;
    }

    private string DescribeSuccess()
    {
        return _operation switch
        {
            DiscChangeOperation.Load =>
                "Inserted " + System.IO.Path.GetFileName(_path) + ".",
            DiscChangeOperation.Eject => "Ejected the disc.",
            _ => "Disc changed.",
        };
    }
}
