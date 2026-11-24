using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace XGrid.Core.Xemu;

/// <summary>
/// The in-process core: <c>xgrid_core.dll</c> loaded into the shell's own
/// process. Frames and pad state use the same shared sections as the
/// out-of-process core, so the session's presentation path does not change.
/// </summary>
/// <remarks>
/// QEMU keeps global state that is initialised once, so a process hosts one
/// machine: <see cref="Start"/> is accepted once and the library stays loaded
/// for the life of the shell. Loading a DLL (not the EXE) is deliberate - the
/// Windows loader initialises a DLL's CRT, which an EXE loaded with
/// <c>LoadLibrary</c> does not do.
/// </remarks>
public sealed class XemuInProcessCore : IDisposable
{
    private const string LibraryFileName = "xgrid_core.dll";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StartDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string configPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? displaySink,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? inputSink,
        IntPtr extraArgv,
        int extraArgc);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StopDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IntDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr VersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NameRequestDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PauseDelegate(int paused);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NoArgIntDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NoArgStringDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr IndexedStringDelegate(int index);

    private readonly IntPtr _library;
    private readonly StartDelegate _start;
    private readonly StopDelegate _requestStop;
    private readonly IntDelegate _isRunning;
    private readonly IntDelegate _exitCode;
    private readonly IntDelegate _join;
    private readonly VersionDelegate _version;
    private readonly NameRequestDelegate? _snapshotSave;
    private readonly NameRequestDelegate? _snapshotLoad;
    private readonly NameRequestDelegate? _snapshotDelete;
    private readonly NoArgIntDelegate? _snapshotRefresh;
    private readonly NoArgIntDelegate? _snapshotStatus;
    private readonly NoArgIntDelegate? _snapshotCount;
    private readonly NoArgStringDelegate? _snapshotError;
    private readonly IndexedStringDelegate? _snapshotName;
    private readonly PauseDelegate? _setPaused;
    private readonly NoArgIntDelegate? _frameStep;
    private bool _started;
    private bool _disposed;

    private XemuInProcessCore(string libraryPath)
    {
        _library = NativeLibrary.Load(libraryPath);
        try
        {
            _start = Get<StartDelegate>("xgrid_core_start");
            _requestStop = Get<StopDelegate>("xgrid_core_request_stop");
            _isRunning = Get<IntDelegate>("xgrid_core_is_running");
            _exitCode = Get<IntDelegate>("xgrid_core_exit_code");
            _join = Get<IntDelegate>("xgrid_core_join");
            _version = Get<VersionDelegate>("xgrid_core_version");

            // Save states are optional: a core built before the ABI existed
            // still runs, and the shell reports that it cannot save.
            _snapshotSave = TryGet<NameRequestDelegate>("xgrid_core_snapshot_save");
            _snapshotLoad = TryGet<NameRequestDelegate>("xgrid_core_snapshot_load");
            _snapshotDelete = TryGet<NameRequestDelegate>("xgrid_core_snapshot_delete");
            _snapshotRefresh = TryGet<NoArgIntDelegate>("xgrid_core_snapshot_refresh");
            _snapshotStatus = TryGet<NoArgIntDelegate>("xgrid_core_snapshot_status");
            _snapshotCount = TryGet<NoArgIntDelegate>("xgrid_core_snapshot_count");
            _snapshotError = TryGet<NoArgStringDelegate>("xgrid_core_snapshot_error");
            _snapshotName = TryGet<IndexedStringDelegate>("xgrid_core_snapshot_name");

            // Playback is optional for the same reason: an older core still
            // boots, the shell just has no pause control for it.
            _setPaused = TryGet<PauseDelegate>("xgrid_core_set_paused");
            _frameStep = TryGet<NoArgIntDelegate>("xgrid_core_frame_step");
        }
        catch
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
            throw;
        }
    }

    /// <summary>Gets the core version string (for example <c>0.8.139</c>).</summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the machine is running.</summary>
    public bool IsRunning => !_disposed && _isRunning() != 0;

    /// <summary>Gets the run's exit status (0 before it finishes).</summary>
    public int ExitCode => _disposed ? -1 : _exitCode();

    /// <summary>Gets the core DLL that is loaded, or an empty string.</summary>
    public string LibraryPath { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the core can save and load states.</summary>
    public bool SupportsSaveStates =>
        _snapshotSave is not null && _snapshotLoad is not null && _snapshotDelete is not null
        && _snapshotStatus is not null && _snapshotError is not null;

    /// <summary>Gets a value indicating whether the core can pause and step.</summary>
    public bool SupportsPlayback => _setPaused is not null && _frameStep is not null;

    /// <summary>
    /// Loads the core DLL when it exists beside the executable, returning null
    /// when it is absent or does not export the ABI.
    /// </summary>
    /// <param name="coreExecutablePath">Path to the core executable.</param>
    /// <param name="libraryPath">Resolved DLL path.</param>
    public static XemuInProcessCore? TryLoad(string coreExecutablePath, out string libraryPath)
    {
        libraryPath = string.Empty;
        if (string.IsNullOrWhiteSpace(coreExecutablePath))
        {
            return null;
        }

        string? directory = Path.GetDirectoryName(coreExecutablePath);
        if (directory is null)
        {
            return null;
        }

        string candidate = Path.Combine(directory, LibraryFileName);
        if (!File.Exists(candidate))
        {
            return null;
        }

        try
        {
            var core = new XemuInProcessCore(candidate)
            {
                LibraryPath = candidate,
            };
            core.Version = Marshal.PtrToStringUTF8(core._version()) ?? string.Empty;
            libraryPath = candidate;
            return core;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts the machine on the core's own thread.
    /// </summary>
    /// <param name="configPath">Generated xemu settings file.</param>
    /// <param name="displaySink">Shared-section name for presented frames.</param>
    /// <param name="inputSink">Shared-section name for port 1.</param>
    /// <param name="extraArguments">Additional command-line arguments.</param>
    /// <returns>0 on success; a negative ABI status otherwise.</returns>
    public int Start(
        string configPath,
        string? displaySink,
        string? inputSink,
        IReadOnlyList<string> extraArguments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("The in-process core was already started.");
        }

        IntPtr argv = IntPtr.Zero;
        IntPtr[]? strings = null;
        try
        {
            if (extraArguments.Count > 0)
            {
                strings = new IntPtr[extraArguments.Count];
                argv = Marshal.AllocHGlobal(IntPtr.Size * extraArguments.Count);
                for (int i = 0; i < extraArguments.Count; i++)
                {
                    strings[i] = Marshal.StringToCoTaskMemUTF8(extraArguments[i]);
                    Marshal.WriteIntPtr(argv, i * IntPtr.Size, strings[i]);
                }
            }

            int status = _start(configPath, displaySink, inputSink, argv, extraArguments.Count);
            _started = status == 0;
            return status;
        }
        finally
        {
            if (strings is not null)
            {
                foreach (IntPtr value in strings)
                {
                    Marshal.FreeCoTaskMem(value);
                }
            }

            if (argv != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(argv);
            }
        }
    }

    /// <summary>Asks the machine to power off.</summary>
    public void RequestStop()
    {
        if (!_disposed && _started)
        {
            _requestStop();
        }
    }

    /// <summary>
    /// Joins the stopped run thread and returns its exit status, or -2 while
    /// the machine is still running.
    /// </summary>
    public int Join()
    {
        return _disposed ? -1 : _join();
    }

    /// <summary>Queues a pause (true) or resume (false); 0 accepted, negative refused.</summary>
    /// <param name="paused">True to pause, false to resume.</param>
    public int SetPaused(bool paused)
    {
        return _disposed || _setPaused is null ? -1 : _setPaused(paused ? 1 : 0);
    }

    /// <summary>Queues one frame step while paused (0 accepted, negative refused).</summary>
    public int StepFrame()
    {
        return _disposed || _frameStep is null ? -1 : _frameStep();
    }

    /// <summary>Queues a snapshot save (0 accepted, negative refused).</summary>
    /// <param name="name">Snapshot name; an existing one is replaced.</param>
    public int SnapshotSave(string name)
    {
        return _disposed || _snapshotSave is null ? -1 : _snapshotSave(name);
    }

    /// <summary>Queues a snapshot load (0 accepted, negative refused).</summary>
    /// <param name="name">Snapshot name.</param>
    public int SnapshotLoad(string name)
    {
        return _disposed || _snapshotLoad is null ? -1 : _snapshotLoad(name);
    }

    /// <summary>Queues a snapshot delete (0 accepted, negative refused).</summary>
    /// <param name="name">Snapshot name.</param>
    public int SnapshotDelete(string name)
    {
        return _disposed || _snapshotDelete is null ? -1 : _snapshotDelete(name);
    }

    /// <summary>Queues a refresh of the snapshot list (0 accepted).</summary>
    public int SnapshotRefresh()
    {
        return _disposed || _snapshotRefresh is null ? -1 : _snapshotRefresh();
    }

    /// <summary>Gets the last request's state.</summary>
    public XemuSaveStateStatus SnapshotStatus
    {
        get
        {
            if (_disposed || _snapshotStatus is null)
            {
                return XemuSaveStateStatus.Idle;
            }

            int status = _snapshotStatus();
            return Enum.IsDefined(typeof(XemuSaveStateStatus), status)
                ? (XemuSaveStateStatus)status
                : XemuSaveStateStatus.Failed;
        }
    }

    /// <summary>Gets the last request's error text, or an empty string.</summary>
    public string SnapshotError
    {
        get
        {
            if (_disposed || _snapshotError is null)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUTF8(_snapshotError()) ?? string.Empty;
        }
    }

    /// <summary>Gets the snapshot names the last refresh found.</summary>
    public IReadOnlyList<string> SnapshotNames
    {
        get
        {
            if (_disposed || _snapshotCount is null || _snapshotName is null)
            {
                return Array.Empty<string>();
            }

            int count = _snapshotCount();
            if (count <= 0)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                string? name = Marshal.PtrToStringUTF8(_snapshotName(i));
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }
    }

    /// <summary>Releases the wrapper. The module stays loaded for the process.</summary>
    public void Dispose()
    {
        _disposed = true;
    }

    private T Get<T>(string name)
        where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    }

    private T? TryGet<T>(string name)
        where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_library, name, out IntPtr address) || address == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
}
