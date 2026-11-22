using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using XGrid.Bus;
using XGrid.Bus.Smbus;

namespace XGrid.Xcn2SmcProbe
{
    /// <summary>
    /// XCN2 machine seam probe: runs the MCPX 1BL in the native host with the
    /// real C# AMD756 SMBus controller and SMC attached. The whole 64 KB I/O
    /// space is one live window, so the guest's in/out instructions reach
    /// the C# I/O space (and the PCI config ports reach the PCI bus, which
    /// programs the SMBus BAR the 1BL uses).
    /// </summary>
    internal static unsafe class Program
    {
        private const uint RamSize = 64 * 1024 * 1024;
        private const uint FlashSize = 1024 * 1024;
        private const uint MirrorSize = 16 * 1024 * 1024;
        private const uint ShadowFlash = 0;
        private const uint ShadowMirror = FlashSize;
        private const uint ShadowOverlay = ShadowMirror + MirrorSize;
        private const uint ShadowSize = ShadowOverlay + FlashSize;
        private const uint CtlOffset = RamSize;
        private const uint SectionSize = CtlOffset + 0x250 + ShadowSize + (RingCapacity * 24) + 0x100;

        private const uint Magic = 0x58474348u;
        private const uint Version = 5u;
        private const int RingCapacity = 1024;
        private const uint WinIo = 0x1;
        private const uint WinLive = 0x2;
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

        private static readonly XboxBus Bus = new XboxBus();
        private static long s_portReads;
        private static long s_portWrites;
        private static long s_irqAcks;

        private static uint Read32(byte* p) => Volatile.Read(ref *(uint*)p);

        private static void Write32(byte* p, uint v) => Volatile.Write(ref *(uint*)p, v);

        private static ulong Read64(byte* p) => Volatile.Read(ref *(ulong*)p);

        private static void Write64(byte* p, ulong v) => Volatile.Write(ref *(ulong*)p, v);

        private static int Main(string[] args)
        {
            string root = FindRoot();
            string biosPath = args.Length > 0
                ? args[0]
                : Path.Combine(root, "data", "xemu", "Complex_4627.bin");
            string romPath = args.Length > 1
                ? args[1]
                : Path.Combine(root, "data", "xemu", "mcpx_1.0.bin");
            uint budget = args.Length > 2 ? uint.Parse(args[2]) : 200_000_000;

            byte[] bios = File.ReadAllBytes(biosPath);
            byte[] rom = File.ReadAllBytes(romPath);
            byte[] overlay = new byte[FlashSize];
            bios.CopyTo(overlay, 0);
            rom.CopyTo(overlay, overlay.Length - rom.Length);

            string host = Environment.GetEnvironmentVariable("XGRIDCPU_HOST")
                ?? Path.Combine(root, "native", "xgridcpu", "xgridcpu-host.exe");

            string name = "xgrid-smc-" + Guid.NewGuid().ToString("N");
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
                WriteWindow(ctl, 3, 0x0000u, 0x10000u, WinIo | WinLive, 0, 0);
                Write32(ctl + OffNWindows, 4);

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
                    if (process.HasExited || DateTime.UtcNow > deadline)
                    {
                        Console.Error.WriteLine("host did not become ready");
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
                    uint chunk = Math.Min(left, 5_000_000u);
                    Write32(ctl + OffRunBudget, chunk);
                    Write32(ctl + OffStatus, 1);
                    deadline = DateTime.UtcNow.AddSeconds(300);
                    while (true)
                    {
                        if (Read32(ctl + OffIoState) == 1)
                        {
                            Service(ctl);
                            continue;
                        }
                        if (Read32(ctl + OffStatus) == 2)
                        {
                            DrainRing(ctl);
                            break;
                        }
                        if (process.HasExited || DateTime.UtcNow > deadline)
                        {
                            Console.Error.WriteLine("host run failed");
                            Console.Error.Write(hostLog.ToString());
                            return 1;
                        }
                        Thread.SpinWait(32);
                    }
                    left -= chunk;
                    slices++;
                    // Device time and interrupts follow the executed count
                    // (1 instruction = 1 CPU cycle at 733 MHz).
                    ulong ran = Read64(ctl + OffExecuted);
                    Bus.Advance(ran);
                    if (Bus.Pic.Pending)
                    {
                        Write32(ctl + OffIrqPending, 1);
                    }
                    if ((slices % 8) == 0 || left == 0)
                    {
                        Console.WriteLine($"slice {slices}: executed={Read64(ctl + OffExecuted)} " +
                            $"eip=0x{Read32(ctl + OffRegs + 32):X8} " +
                            $"cs=0x{Read32(ctl + OffRegs + 40) & 0xFFFF:X4} " +
                            $"tsc={Read32(ctl + OffRegs + 56):X8}{Read32(ctl + OffRegs + 52):X8} " +
                            $"io={s_portReads}/{s_portWrites} smb={Bus.Smbus.TransactionCount}");
                    }
                }
                sw.Stop();

                Console.WriteLine($"final: eip=0x{Read32(ctl + OffRegs + 32):X8} " +
                    $"cs=0x{Read32(ctl + OffRegs + 40) & 0xFFFF:X4} " +
                    $"eax=0x{Read32(ctl + OffRegs):X8} " +
                    $"io reads={s_portReads} writes={s_portWrites} irqs={s_irqAcks}");
                Console.WriteLine($"smc: smbus transactions={Bus.Smbus.TransactionCount} " +
                    $"last addr=0x{Bus.Smbus.LastSlaveAddress:X2} " +
                    $"cmd=0x{Bus.Smbus.LastCommand:X2} ok={Bus.Smbus.LastOk}");
                Console.WriteLine($"boot: wall={sw.Elapsed.TotalSeconds:F3} s " +
                    $"= {budget / sw.Elapsed.TotalSeconds / 1e6:F1} Msteps/s");

                byte* at = basePtr + 0x400930;
                System.Text.StringBuilder hex = new System.Text.StringBuilder();
                for (int i = 0; i < 32; i++)
                {
                    hex.Append(at[i].ToString("X2"));
                    hex.Append(' ');
                }
                Console.WriteLine($"ram[0x400930]={hex}");
                int loaded = 0;
                for (int i = 0; i < 0x10000; i++)
                {
                    if (basePtr[0x90000 + i] != 0)
                    {
                        loaded++;
                    }
                }
                Console.WriteLine($"ram[0x90000] non-zero bytes={loaded}");

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

        private static void Service(byte* ctl)
        {
            DrainRing(ctl);
            uint flags = Read32(ctl + OffIoFlags);
            if ((flags & 0x80000000u) != 0)
            {
                int vector = Bus.Pic.Acknowledge();
                if (vector >= 0)
                {
                    s_irqAcks++;
                }
                Write64(ctl + OffIoValue, vector < 0 ? 0xFFFFFFFFu : (uint)vector);
                Write32(ctl + OffIoState, 2);
                return;
            }
            uint address = Read32(ctl + OffIoAddr);
            uint size = Read32(ctl + OffIoSize);
            ushort port = (ushort)(address & 0xFFFF);
            uint value = size switch
            {
                1 => Bus.Io.Read8(port),
                2 => Bus.Io.Read16(port),
                _ => Bus.Io.Read32(port),
            };
            s_portReads++;
            Write64(ctl + OffIoValue, value);
            Write32(ctl + OffIoFlags, 0);
            Write32(ctl + OffIoState, 2);
        }

        private static void DrainRing(byte* ctl)
        {
            uint count = Read32(ctl + OffRingCount);
            for (uint i = 0; i < count && i < RingCapacity; i++)
            {
                byte* e = ctl + OffRing + (int)(i * 24);
                uint address = Read32(e);
                uint size = Read32(e + 4);
                uint flags = Read32(e + 12);
                ulong value = Read64(e + 16);
                if ((flags & WinIo) != 0)
                {
                    ushort port = (ushort)(address & 0xFFFF);
                    switch (size)
                    {
                        case 1:
                            Bus.Io.Write8(port, (byte)value);
                            break;
                        case 2:
                            Bus.Io.Write16(port, (ushort)value);
                            break;
                        default:
                            Bus.Io.Write32(port, (uint)value);
                            break;
                    }
                    s_portWrites++;
                }
            }
            Write32(ctl + OffRingCount, 0);
        }

        private static void Write32(byte* ctl, int offset, uint value) => Write32(ctl + offset, value);

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
