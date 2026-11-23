using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using XGrid.Core.Xemu;

namespace XGrid.XemuEmbedSpike
{
    /// <summary>
    /// A0 spike: prove the core can run inside a host window with no per-frame
    /// copying. Creates a plain Win32 host window, starts xemu, reparents the
    /// core window into it, runs for a fixed time, captures what is on screen
    /// and shuts the core down through its own close request.
    /// </summary>
    internal static partial class Program
    {
        private static int Main(string[] args)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("The embedding spike is Windows-only.");
                return 2;
            }

            Options options;
            try
            {
                options = Options.Parse(args);
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }

            string? core = XemuPaths.FindCore(options.CorePath);
            string? firmware = XemuPaths.FindFirmwareDirectory(core);
            if (core is null || firmware is null)
            {
                Console.Error.WriteLine("xemu core or firmware directory not found.");
                return 2;
            }

            var settings = new XemuLaunchSettings
            {
                CorePath = core,
                BootRomPath = Path.Combine(firmware, "mcpx_1.0.bin"),
                FlashRomPath = Path.Combine(firmware, "Complex_4627.bin"),
                EepromPath = Path.Combine(firmware, "eeprom.bin"),
                HddPath = Path.Combine(firmware, "xbox_hdd.qcow2"),
                DvdPath = options.IsoPath,
                WindowWidth = options.Width,
                WindowHeight = options.Height,
                WorkingDirectory = options.WorkingDirectory,
            };

            IReadOnlyList<string> errors = settings.Validate();
            if (errors.Count > 0)
            {
                foreach (string error in errors)
                {
                    Console.Error.WriteLine(error);
                }

                return 2;
            }

            Console.WriteLine($"core: {core}");
            Console.WriteLine($"firmware: {firmware}");
            Console.WriteLine($"disc: {options.IsoPath}");

            using var host = new HostWindow(options.Width, options.Height);
            Console.WriteLine($"host: hwnd=0x{host.Handle:X}");

            using var coreProcess = new XemuCoreProcess(settings);
            using var embedder = new XemuWindowEmbedder();

            coreProcess.Start();
            IntPtr child = coreProcess.WaitForWindow(TimeSpan.FromSeconds(30));
            if (child == IntPtr.Zero)
            {
                Console.Error.WriteLine("The core did not create a window.");
                Console.Error.WriteLine(coreProcess.ReadLogTail());
                return 3;
            }

            Console.WriteLine($"core: pid={coreProcess.ProcessId} hwnd=0x{child:X}");
            embedder.Embed(host.Handle, child, host.ClientWidth, host.ClientHeight);
            Console.WriteLine($"embed: isEmbedded={embedder.IsEmbedded}");

            var clock = Stopwatch.StartNew();
            TimeSpan deadline = TimeSpan.FromSeconds(options.Seconds);
            while (clock.Elapsed < deadline && coreProcess.IsRunning)
            {
                host.Pump(embedder);
                Thread.Sleep(4);
            }

            Console.WriteLine(
                $"run: {clock.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s, "
                + $"coreRunning={coreProcess.IsRunning}");

            FrameStatistics stats = HostWindow.Capture(embedder, options.ScreenshotPath);
            Console.WriteLine(
                $"shot: {options.ScreenshotPath} {stats.Width}x{stats.Height} "
                + $"colours={stats.Colours} "
                + $"black={stats.BlackShare.ToString("P1", CultureInfo.InvariantCulture)} "
                + $"mean=/{(int)stats.MeanR},{(int)stats.MeanG},{(int)stats.MeanB}");

            bool graceful = coreProcess.Stop(TimeSpan.FromSeconds(10));
            Console.WriteLine(
                $"stop: graceful={graceful} exit={coreProcess.ExitCode} "
                + $"uptime={coreProcess.Uptime.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s");
            host.Destroy();

            string logTail = coreProcess.ReadLogTail(2048);
            if (logTail.Length > 0)
            {
                Console.WriteLine("--- core log tail ---");
                Console.WriteLine(logTail);
            }

            bool ok = stats.Colours > 16;
            Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL (the captured frame is blank)");
            return ok ? 0 : 4;
        }

        /// <summary>Command line for the spike.</summary>
        private sealed class Options
        {
            internal string IsoPath { get; private set; } = string.Empty;

            internal string? CorePath { get; private set; }

            internal int Seconds { get; private set; } = 45;

            internal int Width { get; private set; } = 1280;

            internal int Height { get; private set; } = 720;

            internal string ScreenshotPath { get; private set; } = "xemu-embed-spike.bmp";

            internal string WorkingDirectory { get; private set; } = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XGrid", "xemu");

            internal static Options Parse(string[] args)
            {
                var options = new Options();
                for (int i = 0; i < args.Length; i++)
                {
                    string name = args[i];
                    string Next()
                    {
                        if (i + 1 >= args.Length)
                        {
                            throw new ArgumentException($"Missing value for {name}.");
                        }

                        return args[++i];
                    }

                    switch (name)
                    {
                        case "--iso":
                            options.IsoPath = Path.GetFullPath(Next());
                            break;
                        case "--core":
                            options.CorePath = Path.GetFullPath(Next());
                            break;
                        case "--seconds":
                            options.Seconds = int.Parse(Next(), CultureInfo.InvariantCulture);
                            break;
                        case "--shot":
                            options.ScreenshotPath = Path.GetFullPath(Next());
                            break;
                        case "--work":
                            options.WorkingDirectory = Path.GetFullPath(Next());
                            break;
                        default:
                            throw new ArgumentException($"Unknown argument '{name}'.");
                    }
                }

                if (options.IsoPath.Length == 0)
                {
                    throw new ArgumentException("--iso <disc image> is required.");
                }

                return options;
            }
        }

        /// <summary>A minimal Win32 host window whose client area the core fills.</summary>
        private sealed unsafe partial class HostWindow : IDisposable
        {
            private const string HostClassName = "XGridXemuSpikeHost";
            private const uint WmDestroy = 0x0002;
            private const int SwShow = 5;

            private IntPtr _handle;
            private bool _classRegistered;

            internal HostWindow(int width, int height)
            {
                IntPtr instance = Native.GetModuleHandleW(IntPtr.Zero);
                IntPtr className = Marshal.StringToHGlobalUni(HostClassName);
                try
                {
                    var windowClass = new Native.WindowClassEx
                    {
                        Size = (uint)sizeof(Native.WindowClassEx),
                        Style = 0x0002 | 0x0001,
                        Instance = instance,
                        ClassName = className,
                        WindowProcedure = &WindowProc,
                    };
                    if (Native.RegisterClassExW(&windowClass) == 0)
                    {
                        throw new InvalidOperationException(
                            "RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
                    }

                    _classRegistered = true;
                }
                finally
                {
                    Marshal.FreeHGlobal(className);
                }

                _handle = Native.CreateWindowExW(
                    0,
                    HostClassName,
                    "XGrid - xemu embedding spike",
                    0x00CF0000,
                    unchecked((int)0x80000000),
                    unchecked((int)0x80000000),
                    width,
                    height,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    instance,
                    IntPtr.Zero);
                if (_handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
                }

                _ = Native.ShowWindow(_handle, SwShow);
                _ = Native.UpdateWindow(_handle);
                ReadClientSize();
            }

            internal IntPtr Handle => _handle;

            internal int ClientWidth { get; private set; }

            internal int ClientHeight { get; private set; }

            internal void Pump(XemuWindowEmbedder embedder)
            {
                while (Native.PeekMessageW(out Native.Message message, IntPtr.Zero, 0, 0, 1))
                {
                    _ = Native.TranslateMessage(ref message);
                    _ = Native.DispatchMessageW(ref message);
                }

                ReadClientSize();
                embedder.Resize(ClientWidth, ClientHeight);
            }

            internal static FrameStatistics Capture(XemuWindowEmbedder embedder, string path)
            {
                IntPtr child = embedder.ChildHandle;
                if (child == IntPtr.Zero || !Native.GetWindowRect(child, out Native.Rect rect))
                {
                    return default;
                }

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0)
                {
                    return default;
                }

                FrameStatistics best = default;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    using var bitmap = new ScreenBitmap(width, height);
                    if (!Native.PrintWindow(child, bitmap.Dc, 2) || bitmap.ColourCount() <= 2)
                    {
                        bitmap.CopyFromScreen(rect, width, height);
                    }

                    FrameStatistics stats = bitmap.Statistics();
                    bitmap.SaveBmp(path);
                    if (stats.Colours > 16)
                    {
                        return stats;
                    }

                    best = stats;
                    Thread.Sleep(400);
                }

                return best;
            }

            internal void Destroy()
            {
                if (_handle != IntPtr.Zero)
                {
                    _ = Native.DestroyWindow(_handle);
                    _handle = IntPtr.Zero;
                }
            }

            public void Dispose()
            {
                Destroy();
                if (_classRegistered)
                {
                    _ = Native.UnregisterClassW(HostClassName, Native.GetModuleHandleW(IntPtr.Zero));
                    _classRegistered = false;
                }
            }

            private void ReadClientSize()
            {
                if (_handle != IntPtr.Zero && Native.GetClientRect(_handle, out Native.Rect rect))
                {
                    ClientWidth = Math.Max(1, rect.Right - rect.Left);
                    ClientHeight = Math.Max(1, rect.Bottom - rect.Top);
                }
            }

            [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
            private static nint WindowProc(IntPtr window, uint message, nint wParam, nint lParam)
            {
                if (message == WmDestroy)
                {
                    Native.PostQuitMessage(0);
                    return 0;
                }

                return Native.DefWindowProcW(window, message, wParam, lParam);
            }
        }

        /// <summary>Window, GDI and kernel user32 imports for the spike.</summary>
        private static unsafe partial class Native
        {
            internal const uint WsOverlappedWindow = 0x00CF0000;

            [StructLayout(LayoutKind.Sequential)]
            internal struct WindowClassEx
            {
                internal uint Size;
                internal uint Style;
                internal delegate* unmanaged[Stdcall]<IntPtr, uint, nint, nint, nint> WindowProcedure;
                internal int ClassExtra;
                internal int WindowExtra;
                internal IntPtr Instance;
                internal IntPtr Icon;
                internal IntPtr Cursor;
                internal IntPtr Background;
                internal IntPtr MenuName;
                internal IntPtr ClassName;
                internal IntPtr SmallIcon;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct Message
            {
                internal IntPtr Window;
                internal uint MessageId;
                internal nint WParam;
                internal nint LParam;
                internal uint Time;
                internal int PointX;
                internal int PointY;
                internal uint Private;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct Rect
            {
                internal int Left;
                internal int Top;
                internal int Right;
                internal int Bottom;
            }

            [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
            internal static partial ushort RegisterClassExW(WindowClassEx* windowClass);

            [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW",
                StringMarshalling = StringMarshalling.Utf16)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static partial bool UnregisterClassW(string className, IntPtr instance);

            [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true,
                StringMarshalling = StringMarshalling.Utf16)]
            internal static partial IntPtr CreateWindowExW(
                uint exStyle,
                string className,
                string windowName,
                uint style,
                int x,
                int y,
                int width,
                int height,
                IntPtr parent,
                IntPtr menu,
                IntPtr instance,
                IntPtr param);

            [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
            internal static partial nint DefWindowProcW(IntPtr window, uint message, nint wParam, nint lParam);

            [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static partial bool DestroyWindow(IntPtr window);

            [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static partial bool ShowWindow(IntPtr window, int command);

            [LibraryImport("user32.dll", EntryPoint = "UpdateWindow")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static partial bool UpdateWindow(IntPtr window);

            [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static partial bool PeekMessageW(
                out Message message, IntPtr window, uint min, uint max, uint remove);

            [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static partial bool TranslateMessage(ref Message message);

            [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
            internal static partial nint DispatchMessageW(ref Message message);

            [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
            internal static partial void PostQuitMessage(int exitCode);

            [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static partial bool GetClientRect(IntPtr window, out Rect rect);

            [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static partial bool GetWindowRect(IntPtr window, out Rect rect);

            [LibraryImport("user32.dll", EntryPoint = "PrintWindow")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static partial bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

            [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
            internal static partial IntPtr GetModuleHandleW(IntPtr module);
        }

        /// <summary>A 32-bit DIB section used for window capture and BMP export.</summary>
        private sealed unsafe partial class ScreenBitmap : IDisposable
        {
            private readonly int _width;
            private readonly int _height;
            private readonly IntPtr _dc;
            private readonly IntPtr _bitmap;
            private readonly IntPtr _previous;
            private readonly IntPtr _pixels;

            internal ScreenBitmap(int width, int height)
            {
                _width = width;
                _height = height;
                _dc = CreateCompatibleDc(IntPtr.Zero);
                var info = new BitmapInfo
                {
                    Header = new BitmapInfoHeader
                    {
                        Size = (uint)sizeof(BitmapInfoHeader),
                        Width = width,
                        Height = -height,
                        Planes = 1,
                        BitCount = 32,
                    },
                };
                _bitmap = CreateDibSection(_dc, &info, 0, out _pixels, IntPtr.Zero, 0);
                _previous = SelectObject(_dc, _bitmap);
            }

            internal IntPtr Dc => _dc;

            internal int ColourCount()
            {
                var seen = new HashSet<uint>();
                int count = _width * _height;
                for (int i = 0; i < count; i++)
                {
                    if (seen.Add(ReadPixel(i)) && seen.Count > 64)
                    {
                        return seen.Count;
                    }
                }

                return seen.Count;
            }

            internal void CopyFromScreen(Native.Rect rect, int width, int height)
            {
                IntPtr screen = GetDc(IntPtr.Zero);
                if (screen == IntPtr.Zero)
                {
                    return;
                }

                _ = BitBlt(_dc, 0, 0, width, height, screen, rect.Left, rect.Top, 0x00CC0020u | 0x40000000u);
                _ = ReleaseDc(IntPtr.Zero, screen);
            }

            internal FrameStatistics Statistics()
            {
                var seen = new HashSet<uint>();
                long r = 0;
                long g = 0;
                long b = 0;
                long black = 0;
                int count = _width * _height;
                for (int i = 0; i < count; i++)
                {
                    uint pixel = ReadPixel(i);
                    _ = seen.Add(pixel);
                    b += pixel & 0xFF;
                    g += (pixel >> 8) & 0xFF;
                    r += (pixel >> 16) & 0xFF;
                    if (pixel == 0)
                    {
                        black++;
                    }
                }

                return new FrameStatistics(
                    _width,
                    _height,
                    seen.Count,
                    count == 0 ? 0 : (double)black / count,
                    count == 0 ? 0 : (double)r / count,
                    count == 0 ? 0 : (double)g / count,
                    count == 0 ? 0 : (double)b / count);
            }

            internal void SaveBmp(string path)
            {
                int rowBytes = _width * 4;
                int imageBytes = rowBytes * _height;
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
                using var writer = new BinaryWriter(stream);
                writer.Write((byte)'B');
                writer.Write((byte)'M');
                writer.Write(14 + 40 + imageBytes);
                writer.Write(0);
                writer.Write(14 + 40);
                writer.Write(40);
                writer.Write(_width);
                writer.Write(-_height);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(0);
                writer.Write(imageBytes);
                writer.Write(2835);
                writer.Write(2835);
                writer.Write(0);
                writer.Write(0);
                for (int i = 0; i < _width * _height; i++)
                {
                    writer.Write(ReadPixel(i));
                }
            }

            public void Dispose()
            {
                _ = SelectObject(_dc, _previous);
                _ = DeleteObject(_bitmap);
                _ = DeleteDc(_dc);
            }

            private uint ReadPixel(int index)
            {
                return ((uint*)_pixels)[index] & 0x00FFFFFF;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct BitmapInfoHeader
            {
                internal uint Size;
                internal int Width;
                internal int Height;
                internal ushort Planes;
                internal ushort BitCount;
                internal uint Compression;
                internal uint SizeImage;
                internal int XPelsPerMeter;
                internal int YPelsPerMeter;
                internal uint ClrUsed;
                internal uint ClrImportant;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct BitmapInfo
            {
                internal BitmapInfoHeader Header;
                internal uint Colours;
            }

            [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
            private static partial IntPtr CreateCompatibleDc(IntPtr dc);

            [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
            private static partial IntPtr CreateDibSection(
                IntPtr dc, BitmapInfo* info, uint usage, out IntPtr bits, IntPtr section, uint offset);

            [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
            private static partial IntPtr SelectObject(IntPtr dc, IntPtr obj);

            [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool DeleteObject(IntPtr obj);

            [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool DeleteDc(IntPtr dc);

            [LibraryImport("gdi32.dll", EntryPoint = "BitBlt")]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static partial bool BitBlt(
                IntPtr destination,
                int x,
                int y,
                int width,
                int height,
                IntPtr source,
                int sourceX,
                int sourceY,
                uint rop);

            [LibraryImport("user32.dll", EntryPoint = "GetDC")]
            private static partial IntPtr GetDc(IntPtr window);

            [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
            private static partial int ReleaseDc(IntPtr window, IntPtr dc);
        }

        /// <summary>Summary of a captured frame: enough to tell a picture from a blank surface.</summary>
        private readonly record struct FrameStatistics(
            int Width, int Height, int Colours, double BlackShare, double MeanR, double MeanG, double MeanB);
    }
}
