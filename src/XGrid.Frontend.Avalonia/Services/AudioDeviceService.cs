using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Serilog;

namespace XGrid.Frontend.Avalonia.Services;

/// <summary>One host playback device the shell can offer.</summary>
/// <param name="Id">Stable id (a DirectSound GUID string on Windows).</param>
/// <param name="Name">Friendly name shown in the settings page.</param>
public sealed record AudioDeviceInfo(string Id, string Name);

/// <summary>
/// Enumerates the host's playback devices for the settings page. DirectSound is
/// the only backend this shell can enumerate natively; the other QEMU backends
/// take a free-form device name from the user.
/// </summary>
public static class AudioDeviceService
{
    private delegate bool EnumerateCallback(
        IntPtr guid,
        IntPtr description,
        IntPtr module,
        IntPtr context);

    [DllImport("dsound.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
    private static extern int DirectSoundEnumerateW(EnumerateCallback callback, IntPtr context);

    /// <summary>Returns the DirectSound playback devices, or an empty list.</summary>
    public static IReadOnlyList<AudioDeviceInfo> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<AudioDeviceInfo>();
        }

        var devices = new List<AudioDeviceInfo>();
        try
        {
            EnumerateCallback callback = (guid, description, _, _) =>
            {
                if (guid == IntPtr.Zero)
                {
                    return true;
                }

                Guid id = Marshal.PtrToStructure<Guid>(guid);
                if (id == Guid.Empty)
                {
                    // The primary/default entry is represented by the
                    // "System default" choice in the UI.
                    return true;
                }

                string name = description == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUni(description) ?? string.Empty;
                if (name.Length == 0)
                {
                    name = id.ToString("B");
                }

                devices.Add(new AudioDeviceInfo(id.ToString("B"), name));
                return true;
            };

            _ = DirectSoundEnumerateW(callback, IntPtr.Zero);
        }
        catch (Exception exception) when (exception is DllNotFoundException
                                              or EntryPointNotFoundException
                                              or BadImageFormatException)
        {
            Log.Debug(exception, "DirectSound devices could not be enumerated");
        }

        devices.Sort(static (left, right) =>
            string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        return devices;
    }
}
