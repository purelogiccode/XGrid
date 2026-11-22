using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace XGrid.Xcn1BootProbe
{
    /// <summary>
    /// XCN1 boot probe: runs the real MCPX 1BL in the native host. Guest RAM,
    /// the flash image and the MCPX ROM overlay are direct windows into the
    /// shared section, so instruction fetch is plain memory. No device window
    /// is mapped yet; the run stops at the first access outside RAM/flash.
    /// </summary>
    internal static unsafe class Program
    {
        private const uint RamSize = 64 * 1024 * 1024;
        private const uint FlashSize = 1024 * 1024;
        private const uint MirrorSize = 16 * 1024 * 1024;
        private const uint ShadowSize = (16 * 1024 * 1024) + (2 * 1024 * 1024);
        private const uint ShadowFlash = 0;
        private const uint ShadowMirror = FlashSize;
        private const uint ShadowOverlay = ShadowMirror + MirrorSize;
        private const uint CtlOffset = RamSize;
        private const uint SectionSize = CtlOffset + 0x250 + ShadowSize + (RingCapacity * 24) + 0x100;

        private const uint Magic = 0x58474348u;
        private const uint Version = 5u;
        private const int RingCapacity = 1024;
        private const uint WinDirect = 0x4;

        private const int OffMagic = 0;
        private const int OffVersion = 4;
        private const int OffReady = 8;
        private const int OffQuit = 12;
        private const int OffStatus = 16;
        private const int OffRunBudget = 20;
        private const int OffExecuted = 24;
        private const int OffRc = 32;
        private const int OffRingCount = 36;
        private const int OffIrqPending = 40;
        private const int OffIrqVector = 44;
        private const int OffReset = 48;
        private const int OffNWindows = 52;
        private const int OffWindows = 56;
        private const int OffIoState = 536;
        private const int OffIoFlags = 540;
        private const int OffIoAddr = 544;
        private const int OffIoSize = 548;
        private const int OffIoValue = 552;
        private const int OffIoIcount = 560;
        private const int OffRingCapacity = 576;
        private const int OffRamSize = 568;
        private const int OffShadowSize = 572;
        private const int OffRing = 0x250 + (int)ShadowSize;
        private const int OffRegs = OffRing + (RingCapacity * 24);

        private static uint Read32(byte* p) => Volatile.Read(ref *(uint*)p);

        private static void Write32(byte* p, uint v) => Volatile.Write(ref *(uint*)p, v);

        private static ulong Read64(byte* p) => Volatile.Read(ref *(ulong*)p);

        private static int Main(string[] args)
        {
            string root = FindRoot();
            string biosPath = args.Length > 0
                ? args[0]
                : Path.Combine(root, "data", "xemu", "Complex_4627.bin");
            string romPath = args.Length > 1
                ? args[1]
                : Path.Combine(root, "data", "xemu", "mcpx_1.0.bin");
            uint budget = args.Length > 2 ? uint.Parse(args[2]) : 10_000_000;

            byte[] bios = File.ReadAllBytes(biosPath);
            byte[] rom = File.ReadAllBytes(romPath);
            if (bios.Length != FlashSize)
            {
                Console.Error.WriteLine($"BIOS must be {FlashSize} bytes, got {bios.Length}");
                return 2;
            }

            byte[] overlay = new byte[FlashSize];
            bios.CopyTo(overlay, 0);
            if (rom.Length > overlay.Length)
            {
                Console.Error.WriteLine("MCPX ROM larger than the flash image");
                return 2;
            }
            rom.CopyTo(overlay, overlay.Length - rom.Length);

            string host = Environment.GetEnvironmentVariable("XGRIDCPU_HOST")
                ?? Path.Combine(root, "native", "xgridcpu", "xgridcpu-host.exe");
            if (!File.Exists(host))
            {
                Console.Error.WriteLine($"native host not found: {host}");
                return 2;
            }

            string name = "xgrid-boot-" + Guid.NewGuid().ToString("N");
            using MemoryMappedFile mmf = MemoryMappedFile.CreateNew(name, SectionSize);
            using MemoryMappedViewAccessor view = mmf.CreateViewAccessor(
                0, SectionSize, MemoryMappedFileAccess.ReadWrite);
            byte* basePtr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            try
            {
                basePtr += view.PointerOffset;
                byte* ctl = basePtr + CtlOffset;

                Write32(ctl + OffMagic, Magic);
                Write32(ctl + OffVersion, Version);
                Write32(ctl + OffRamSize, RamSize);
                Write32(ctl + OffShadowSize, ShadowSize);
                Write32(ctl + OffRingCapacity, RingCapacity);
                WriteWindow(ctl, 0, 0xF0000000u, FlashSize, WinDirect, ShadowFlash, 0);
                WriteWindow(ctl, 1, 0xFF000000u, MirrorSize, WinDirect, ShadowMirror, 0);
                WriteWindow(ctl, 2, 0xFFF00000u, FlashSize, WinDirect, ShadowOverlay, 1);
                Write32(ctl + OffNWindows, 3);

                // Flash at 0xF0000000, the 16 MB alias at 0xFF000000 and the
                // overlay at 0xFFF00000 (flash + MCPX ROM at the top).
                bios.CopyTo(new Span<byte>(basePtr + CtlOffset + 0x250 + ShadowFlash,
                    bios.Length));
                for (int i = 0; i < 16; i++)
                {
                    bios.CopyTo(new Span<byte>(
                        basePtr + CtlOffset + 0x250 + ShadowMirror + (i * (int)FlashSize),
                        bios.Length));
                }
                overlay.CopyTo(new Span<byte>(
                    basePtr + CtlOffset + 0x250 + ShadowOverlay, overlay.Length));

                var psi = new ProcessStartInfo(host, $"{name} {CtlOffset}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };
                var hostLog = new System.Text.StringBuilder();
                using Process process = Process.Start(psi)
                    ?? throw new InvalidOperationException("host did not start");
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                    {
                        lock (hostLog)
                        {
                            hostLog.AppendLine(e.Data);
                        }
                    }
                };
                process.BeginErrorReadLine();

                DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                while (Read32(ctl + OffReady) == 0)
                {
                    if (process.HasExited)
                    {
                        Console.Error.WriteLine($"host exited early: {process.ExitCode}");
                        Console.Error.Write(hostLog.ToString());
                        return 1;
                    }
                    if (DateTime.UtcNow > deadline)
                    {
                        Console.Error.WriteLine("host ready timeout");
                        return 1;
                    }
                    Thread.Sleep(10);
                }

                Write32(ctl + OffReset, 1);
                var sw = Stopwatch.StartNew();
                uint left = budget;
                int slices = 0;
                while (left > 0)
                {
                    uint chunk = Math.Min(left, 1_000_000u);
                    Write32(ctl + OffRunBudget, chunk);
                    Write32(ctl + OffStatus, 1);
                    deadline = DateTime.UtcNow.AddSeconds(120);
                    while (Read32(ctl + OffStatus) != 2)
                    {
                        if (Read32(ctl + OffIoState) == 1)
                        {
                            Answer(ctl);
                            continue;
                        }
                        if (process.HasExited)
                        {
                            Console.Error.WriteLine($"host exited during run: {process.ExitCode}");
                            Console.Error.Write(hostLog.ToString());
                            return 1;
                        }
                        if (DateTime.UtcNow > deadline)
                        {
                            Console.Error.WriteLine("host run timeout");
                            return 1;
                        }
                        Thread.SpinWait(64);
                    }
                    left -= chunk;
                    slices++;
                    uint eip = Read32(ctl + OffRegs + 32);
                    uint eflags = Read32(ctl + OffRegs + 36);
                    int rc = (int)Read32(ctl + OffRc);
                    if ((slices % 10) == 0 || left == 0)
                    {
                        Console.WriteLine($"slice {slices}: executed={Read64(ctl + OffExecuted)} " +
                            $"rc={rc} eip=0x{eip:X8} eflags=0x{eflags:X8} " +
                            $"eax=0x{Read32(ctl + OffRegs):X8} ebx=0x{Read32(ctl + OffRegs + 12):X8} " +
                            $"esi=0x{Read32(ctl + OffRegs + 24):X8}");
                    }
                    if (Read64(ctl + OffExecuted) < chunk)
                    {
                        Console.WriteLine("run stopped early");
                        break;
                    }
                }
                sw.Stop();

                uint finalEip = Read32(ctl + OffRegs + 32);
                uint eax = Read32(ctl + OffRegs);
                uint ecx = Read32(ctl + OffRegs + 4);
                uint edx = Read32(ctl + OffRegs + 8);
                uint ebx = Read32(ctl + OffRegs + 12);
                uint esp = Read32(ctl + OffRegs + 16);
                uint ebp = Read32(ctl + OffRegs + 20);
                uint esi = Read32(ctl + OffRegs + 24);
                uint edi = Read32(ctl + OffRegs + 28);
                uint cs = Read32(ctl + OffRegs + 40) & 0xFFFF;
                uint ds = Read32(ctl + OffRegs + 42) & 0xFFFF;
                Console.WriteLine($"final: eip=0x{finalEip:X8} cs=0x{cs:X4} ds=0x{ds:X4} " +
                    $"eax=0x{eax:X8} ecx=0x{ecx:X8} edx=0x{edx:X8} ebx=0x{ebx:X8}");
                Console.WriteLine($"final: esp=0x{esp:X8} ebp=0x{ebp:X8} " +
                    $"esi=0x{esi:X8} edi=0x{edi:X8}");
                Console.WriteLine($"boot: wall={sw.Elapsed.TotalSeconds:F3} s " +
                    $"= {budget / sw.Elapsed.TotalSeconds / 1e6:F1} Msteps/s");

                Write32(ctl + OffQuit, 1);
                Write32(ctl + OffStatus, 1);
                if (!process.WaitForExit(5000))
                {
                    process.Kill(true);
                }
                if (hostLog.Length > 0)
                {
                    Console.Error.Write(hostLog.ToString());
                }
                return 0;
            }
            finally
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }

        private static void Answer(byte* ctl)
        {
            // No device windows are mapped in this probe; fail the read loudly
            // so the final EIP identifies the first unmapped access.
            Write64(ctl + OffIoValue, 0xFFFFFFFFFFFFFFFFul);
            Write32(ctl + OffIoState, 2);
        }

        private static void Write64(byte* p, ulong v) => Volatile.Write(ref *(ulong*)p, v);

        private static void WriteWindow(byte* ctl, int index, uint address, uint size,
            uint flags, uint shadow, int priority)
        {
            byte* w = ctl + OffWindows + (index * 20);
            Write32(w, address);
            Write32(w + 4, size);
            Write32(w + 8, flags);
            Write32(w + 12, shadow);
            Write32(w + 16, (uint)priority);
        }

        private static string FindRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "XGrid.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
