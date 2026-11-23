using System;
using System.Runtime.InteropServices;

namespace XGrid.Input;

/// <summary>
/// Reads the first connected XInput controller and exposes it as an Xbox pad
/// report. Windows-only; on other platforms, or when no XInput runtime is
/// installed, <see cref="IsAvailable"/> is <c>false</c> and <see cref="TryRead"/>
/// always fails, so callers need no platform checks.
/// </summary>
/// <remarks>
/// Polling is cheap but it is not thread-safe by design: construct and poll the
/// reader on one thread (the UI thread). The XInput runtime is loaded lazily
/// from <c>xinput1_4.dll</c> with fallbacks to <c>xinput1_3.dll</c> and
/// <c>xinput9_1_0.dll</c>, because which one exists depends on the Windows
/// version. Nothing here throws when the platform cannot provide it.
/// </remarks>
public sealed class XInputGamepadReader : IDisposable
{
    private const uint ErrorSuccess = 0;
    private const int MaxUsers = 4;

    private delegate uint XInputGetState(uint userIndex, out XInputState state);

    private readonly XInputGetState? _getState;
    private IntPtr _library;
    private bool _disposed;

    /// <summary>Creates a reader, loading the XInput runtime if the platform has one.</summary>
    public XInputGamepadReader()
    {
        (_getState, _library) = Load();
    }

    /// <summary>Gets whether an XInput runtime was loaded.</summary>
    public bool IsAvailable => _getState is not null && _library != IntPtr.Zero;

    /// <summary>Gets the user slot of the last connected controller, or -1.</summary>
    public int UserIndex { get; private set; } = -1;

    /// <summary>
    /// Polls for controller state.
    /// </summary>
    /// <param name="pad">Receives the translated pad report when a controller is connected.</param>
    /// <param name="options">Bindings and axis options, or null for defaults.</param>
    /// <returns><c>true</c> when a controller reported state, <c>false</c> otherwise.</returns>
    public bool TryRead(out HostPad pad, GamepadMappingOptions? options = null)
    {
        pad = HostPad.Neutral;
        if (_disposed || _getState is null)
        {
            return false;
        }

        // Stick to the first slot that answered: re-scanning every poll would
        // make two connected controllers fight over slot order.
        if (UserIndex >= 0 && TryPoll(UserIndex, out pad, options))
        {
            return true;
        }

        for (int user = 0; user < MaxUsers; user++)
        {
            if (!TryPoll(user, out pad, options))
            {
                continue;
            }

            UserIndex = user;
            return true;
        }

        UserIndex = -1;
        pad = HostPad.Neutral;
        return false;
    }

    private bool TryPoll(int user, out HostPad pad, GamepadMappingOptions? options)
    {
        pad = HostPad.Neutral;
        if (_getState is null)
        {
            return false;
        }

        try
        {
            if (_getState((uint)user, out XInputState state) != ErrorSuccess)
            {
                return false;
            }

            pad = XInputPadMapping.Map(
                state.Gamepad.Buttons,
                state.Gamepad.LeftTrigger,
                state.Gamepad.RightTrigger,
                state.Gamepad.LeftThumbX,
                state.Gamepad.LeftThumbY,
                state.Gamepad.RightThumbX,
                state.Gamepad.RightThumbY,
                options);
            return true;
        }
        catch (DllNotFoundException)
        {
            // The runtime disappeared between load and poll; treat as absent.
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static (XInputGetState? GetState, IntPtr Library) Load()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (null, IntPtr.Zero);
        }

        string[] candidates = ["xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll"];
        foreach (string name in candidates)
        {
            if (!NativeLibrary.TryLoad(name, out IntPtr library))
            {
                continue;
            }

            if (NativeLibrary.TryGetExport(library, "XInputGetState", out IntPtr export))
            {
                return (Marshal.GetDelegateForFunctionPointer<XInputGetState>(export), library);
            }

            NativeLibrary.Free(library);
        }

        return (null, IntPtr.Zero);
    }

    /// <summary>Releases the loaded XInput runtime handle.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }
}
