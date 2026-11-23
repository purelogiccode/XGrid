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
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? logPath,
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

    private readonly IntPtr _library;
    private readonly StartDelegate _start;
    private readonly StopDelegate _requestStop;
    private readonly IntDelegate _isRunning;
    private readonly IntDelegate _exitCode;
    private readonly IntDelegate _join;
    private readonly VersionDelegate _version;
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
    /// <param name="logPath">File for the core's own stdout/stderr, or null.</param>
    /// <param name="displaySink">Shared-section name for presented frames.</param>
    /// <param name="inputSink">Shared-section name for port 1.</param>
    /// <param name="extraArguments">Additional command-line arguments.</param>
    /// <returns>0 on success; a negative ABI status otherwise.</returns>
    public int Start(
        string configPath,
        string? logPath,
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

            int status = _start(configPath, logPath, displaySink, inputSink, argv, extraArguments.Count);
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
}
