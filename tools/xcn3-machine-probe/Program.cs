using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using XGrid.Host;
using XGrid.Memory;

namespace XGrid.Xcn3MachineProbe
{
    /// <summary>
    /// XCN3 machine probe: runs the <b>full</b> XboxMachine on the native CPU.
    /// Guest RAM is the shared section, every AddressSpace region is a window
    /// (RAM aliases are QEMU region aliases, flash/ROM images are direct
    /// memory, devices are live) and the whole 64 KB I/O space is one live
    /// window. The C# device model is the authority for every access.
    /// </summary>
    internal static unsafe class Program
    {
        private const uint RamSize = 64 * 1024 * 1024;
        private const uint ShadowSize = 32 * 1024 * 1024;
        private const uint CtlOffset = RamSize + ShadowSize;
        private const int RingCapacity = 1024;
        private const int MaxWindows = 24;
        private const uint SectionSize = CtlOffset + 0x250 + ShadowSize +
            (uint)(RingCapacity * 24) + 0x100;

        private const uint Magic = 0x58474348u;
        private const uint Version = 5u;
        private const uint WinIo = 0x1;
        private const uint WinLive = 0x2;
        private const uint WinDirect = 0x4;
        private const uint WinRamAlias = 0x8;

        private const int OffMagic = 0;
        private const int OffVersion = 4;
        private const int OffReady = 8;
        private const int OffQuit = 12;
        private const int OffStatus = 16;
        private const int OffRunBudget = 20;
        private const int OffExecuted = 24;
        private const int OffRc = 32;
        private const int OffRingCount = 36;
        private const int OffRingTail = 580;
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
        private const int OffRamSize = 568;
        private const int OffShadowSize = 572;
        private const int OffRingCapacity = 576;
        private const int OffDropped = 584;
        private const int OffShadow = 0x250;
        private const int OffRing = 0x250 + (int)ShadowSize;
        private const int OffRegs = OffRing + (RingCapacity * 24);

        private static long s_reads;
        private static long s_writes;
        private static long s_irqs;
        private static long s_serviceTicks;
        private static long s_advanceTicks;
        private static long s_drainTicks;
        private static long s_spinTicks;
        private static byte[] s_bios = [];
        private static bool s_haltLogged;
        private static bool s_panicLogged;

        private static uint Read32(byte* p) => Volatile.Read(ref *(uint*)p);

        private static void Write32(byte* p, uint v) => Volatile.Write(ref *(uint*)p, v);

        private static ulong Read64(byte* p) => Volatile.Read(ref *(ulong*)p);

        private static void Write64(byte* p, ulong v) => Volatile.Write(ref *(ulong*)p, v);

        private static int Main(string[] args)
        {
            string root = FindRoot();
            string biosPath = args.Length > 0 ? args[0] : Path.Combine(root, "data", "xemu", "Complex_4627.bin");
            string romPath = args.Length > 1 ? args[1] : Path.Combine(root, "data", "xemu", "mcpx_1.0.bin");
            string hddPath = args.Length > 2 ? args[2] : Path.Combine(root, "data", "xemu", "xbox_hdd.qcow2");
            string eepromPath = args.Length > 3 ? args[3] : Path.Combine(root, "data", "xemu", "eeprom.bin");
            string dvdPath = args.Length > 6 ? args[6] : "";
            uint budget = args.Length > 4 ? uint.Parse(args[4]) : 2_000_000_000;
            uint slice = args.Length > 5 ? uint.Parse(args[5]) : 1_000_000;

            byte[] bios = File.ReadAllBytes(biosPath);
            byte[] rom = File.ReadAllBytes(romPath);
            byte[] eeprom = File.ReadAllBytes(eepromPath);

            string host = Environment.GetEnvironmentVariable("XGRIDCPU_HOST")
                ?? Path.Combine(root, "native", "xgridcpu", "xgridcpu-host.exe");

            string name = "xgrid-machine-" + Guid.NewGuid().ToString("N");
            using MemoryMappedFile mmf = MemoryMappedFile.CreateNew(name, SectionSize);
            using MemoryMappedViewAccessor view = mmf.CreateViewAccessor(
                0, SectionSize, MemoryMappedFileAccess.ReadWrite);
            byte* basePtr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            try
            {
                basePtr += view.PointerOffset;
                byte* ctl = basePtr + CtlOffset;
                s_bios = bios;

                // The machine's RAM is the section.
                XboxMachine machine = new XboxMachine(RamSize, (IntPtr)basePtr);
                machine.LoadBios(bios, rom);
                machine.LoadEeprom(eeprom);
                if (File.Exists(hddPath))
                {
                    machine.AttachHdd(hddPath);
                }
                if (dvdPath.Length != 0 && File.Exists(dvdPath))
                {
                    machine.AttachDvd(dvdPath);
                }

                Write32(ctl + OffMagic, Magic);
                Write32(ctl + OffVersion, Version);
                Write32(ctl + OffRamSize, RamSize);
                Write32(ctl + OffShadowSize, ShadowSize);
                Write32(ctl + OffRingCapacity, RingCapacity);

                int windows = BuildWindows(ctl, basePtr + CtlOffset + OffShadow, machine);
                Console.WriteLine($"machine: windows={windows} regions={machine.Memory.Regions.Count} " +
                    $"hdd={(machine.HddOverlay is not null ? "attached" : "none")}");

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
                        Console.Error.Write(hostLog.ToString());
                        return 1;
                    }
                    Thread.Sleep(10);
                }

                Write32(ctl + OffReset, 1);
                var sw = Stopwatch.StartNew();
                uint left = budget;
                int slices = 0;
                long lastDropped = 0;
                while (left > 0)
                {
                    uint chunk = Math.Min(left, slice);
                    Write32(ctl + OffRunBudget, chunk);
                    Write32(ctl + OffStatus, 1);
                    deadline = DateTime.UtcNow.AddSeconds(600);
                    while (true)
                    {
                        if (Read32(ctl + OffIoState) == 1)
                        {
                            Service(ctl, machine);
                            continue;
                        }
                        if (Read32(ctl + OffStatus) == 2)
                        {
                            DrainRing(ctl, machine);
                            break;
                        }
                        if (Read32(ctl + OffRingCount) != Read32(ctl + OffRingTail))
                        {
                            long drainStart = Stopwatch.GetTimestamp();
                            DrainRing(ctl, machine);
                            s_drainTicks += Stopwatch.GetTimestamp() - drainStart;
                            continue;
                        }
                        if (process.HasExited || DateTime.UtcNow > deadline)
                        {
                            Console.Error.WriteLine("host run failed");
                            Console.Error.Write(hostLog.ToString());
                            return 1;
                        }
                        long spinStart = Stopwatch.GetTimestamp();
                        Thread.SpinWait(32);
                        s_spinTicks += Stopwatch.GetTimestamp() - spinStart;
                    }
                    left -= chunk;
                    slices++;
                    ulong ran = Read64(ctl + OffExecuted);
                    if (!s_panicLogged)
                    {
                        uint nowEip = Read32(ctl + OffRegs + 32);
                        if ((nowEip & 0xFFFFF000u) == 0x80015000u)
                        {
                            s_panicLogged = true;
                            byte* code = basePtr + ((nowEip & 0x0FFFFFFFu) - 16);
                            Console.WriteLine($"panic: eip=0x{nowEip:X8} " +
                                $"eax=0x{Read32(ctl + OffRegs):X8} " +
                                $"ecx=0x{Read32(ctl + OffRegs + 4):X8} " +
                                $"edx=0x{Read32(ctl + OffRegs + 8):X8} " +
                                $"ebx=0x{Read32(ctl + OffRegs + 12):X8} " +
                                $"esi=0x{Read32(ctl + OffRegs + 24):X8} " +
                                $"edi=0x{Read32(ctl + OffRegs + 28):X8} " +
                                $"eflags=0x{Read32(ctl + OffRegs + 36):X8}");
                            System.Text.StringBuilder hx = new System.Text.StringBuilder();
                            for (int i = 0; i < 32; i++)
                            {
                                hx.Append(code[i].ToString("X2")).Append(' ');
                            }
                            Console.WriteLine($"panic: bytes[0x{(nowEip & 0x0FFFFFFFu) - 16:X}]={hx}");
                        }
                    }
                    // A halted CPU executes nothing; let device time pass so a
                    // timer interrupt can wake it (QEMU's main loop does the
                    // same with the next timer deadline).
                    if (ran == 0 && !s_haltLogged)
                    {
                        s_haltLogged = true;
                        byte* at = basePtr + (Read32(ctl + OffRegs + 32) & 0x0FFFFFFF);
                        Console.WriteLine($"halt: eip=0x{Read32(ctl + OffRegs + 32):X8} " +
                            $"bytes={at[0]:X2} {at[1]:X2} {at[2]:X2} {at[3]:X2} " +
                            $"eflags=0x{Read32(ctl + OffRegs + 36):X8} rc={Read32(ctl + OffRc)}");
                    }
                    long advanceStart = Stopwatch.GetTimestamp();
                    machine.Advance(ran != 0 ? ran : chunk);
                    s_advanceTicks += Stopwatch.GetTimestamp() - advanceStart;
                    if (machine.Bus.Pic.Pending)
                    {
                        Write32(ctl + OffIrqPending, 1);
                    }
                    long dropped = (long)Read64(ctl + OffDropped);
                    if ((slices % 10) == 0 || left == 0)
                    {
                        lastDropped = dropped;
                        Console.WriteLine($"slice {slices}: t={sw.Elapsed.TotalSeconds:F1}s " +
                            $"executed={Read64(ctl + OffExecuted)} " +
                            $"eip=0x{Read32(ctl + OffRegs + 32):X8} " +
                            $"io={s_reads}/{s_writes} irqs={s_irqs} dropped={dropped} " +
                            $"nv2a draws={machine.Nv2A.DrawsExecuted} frames={machine.Nv2A.FramesPresented} " +
                            $"ide={machine.Ide.AccessCount} " +
                            $"drain={s_drainTicks * 1e3 / Stopwatch.Frequency:F0}ms " +
                            $"spin={s_spinTicks * 1e3 / Stopwatch.Frequency:F0}ms " +
                            $"svc={s_serviceTicks * 1e3 / Stopwatch.Frequency:F0}ms");
                    }
                }
                sw.Stop();

                Console.WriteLine($"final: eip=0x{Read32(ctl + OffRegs + 32):X8} " +
                    $"cs=0x{Read32(ctl + OffRegs + 40) & 0xFFFF:X4} " +
                    $"io reads={s_reads} writes={s_writes} irqs={s_irqs} " +
                    $"dropped={Read64(ctl + OffDropped)}");
                Console.WriteLine($"nv2a: draws={machine.Nv2A.DrawsExecuted} " +
                    $"methods={machine.Nv2A.MethodTotal} clears={machine.Nv2A.ClearsExecuted} " +
                    $"frames={machine.Nv2A.FramesPresented} stalls={machine.Nv2A.FlipStalls}");
                Console.WriteLine($"ide: accesses={machine.Ide.AccessCount} " +
                    $"busmaster={machine.Ide.BusMasterStarts}");
                Console.WriteLine($"boot: wall={sw.Elapsed.TotalSeconds:F3} s " +
                    $"= {budget / sw.Elapsed.TotalSeconds / 1e6:F1} Msteps/s " +
                    $"(service={s_serviceTicks * 1e3 / Stopwatch.Frequency:F0} ms " +
                    $"drain={s_drainTicks * 1e3 / Stopwatch.Frequency:F0} ms " +
                    $"spin={s_spinTicks * 1e3 / Stopwatch.Frequency:F0} ms " +
                    $"advance={s_advanceTicks * 1e3 / Stopwatch.Frequency:F0} ms)");

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

        private static int BuildWindows(byte* ctl, byte* shadow, XboxMachine machine)
        {
            uint shadowUsed = 0;
            int index = 0;

            foreach (AddressRegion region in machine.Memory.Regions)
            {
                if (index >= MaxWindows)
                {
                    break;
                }

                uint flags;
                uint shadowOffset = 0;
                int priority = region.Priority;

                if (region.Device is RamAliasDevice)
                {
                    flags = WinRamAlias;
                }
                else if (region.Device is IImmutableImageDevice image)
                {
                    ReadOnlyMemory<byte> bytes = image.Image;
                    if (bytes.Length == 0 || region.Size % (uint)bytes.Length != 0)
                    {
                        continue;
                    }
                    shadowOffset = shadowUsed;
                    shadowUsed += region.Size;
                    for (uint offset = 0; offset < region.Size; offset += (uint)bytes.Length)
                    {
                        bytes.Span.CopyTo(new Span<byte>(shadow + shadowOffset + offset, bytes.Length));
                    }
                    // The flash window keeps precedence over the write-combined
                    // alias at the same base; the overlay wins over both.
                    flags = WinDirect;
                    if (priority == 0)
                    {
                        priority = 1;
                    }
                    else
                    {
                        priority++;
                    }
                }
                else
                {
                    flags = WinLive;
                }

                WriteWindow(ctl, index, region.BaseAddress, region.Size, flags,
                    shadowOffset, priority);
                Console.WriteLine($"  window[{index}] {region.Name} base=0x{region.BaseAddress:X8} " +
                    $"size=0x{region.Size:X} flags={flags} shadow=0x{shadowOffset:X} prio={priority}");
                index++;
            }

            WriteWindow(ctl, index, 0, 0x10000, WinIo | WinLive, 0, 0);
            index++;
            Write32(ctl + OffNWindows, (uint)index);
            Console.WriteLine($"shadow: used=0x{shadowUsed:X} of 0x{ShadowSize:X}");
            return index;
        }

        private static void Service(byte* ctl, XboxMachine machine)
        {
            long start = Stopwatch.GetTimestamp();
            DrainRing(ctl, machine);
            uint flags = Read32(ctl + OffIoFlags);
            if ((flags & 0x80000000u) != 0)
            {
                int vector = machine.Bus.Pic.Acknowledge();
                if (vector >= 0)
                {
                    s_irqs++;
                }
                Write64(ctl + OffIoValue, vector < 0 ? 0xFFFFFFFFu : (uint)vector);
                Write32(ctl + OffIoState, 2);
                return;
            }

            uint address = Read32(ctl + OffIoAddr);
            uint size = Read32(ctl + OffIoSize);
            uint value;
            if ((flags & WinIo) != 0)
            {
                ushort port = (ushort)(address & 0xFFFF);
                value = size switch
                {
                    1 => machine.Bus.Io.Read8(port),
                    2 => machine.Bus.Io.Read16(port),
                    _ => machine.Bus.Io.Read32(port),
                };
            }
            else
            {
                value = size switch
                {
                    1 => machine.Memory.Read8(address),
                    2 => machine.Memory.Read16(address),
                    _ => machine.Memory.Read32(address),
                };
            }
            s_reads++;
            s_serviceTicks += Stopwatch.GetTimestamp() - start;
            Write64(ctl + OffIoValue, value);
            Write32(ctl + OffIoState, 2);
        }

        private static void DrainRing(byte* ctl, XboxMachine machine)
        {
            uint head = Read32(ctl + OffRingCount);
            uint tail = Read32(ctl + OffRingTail);
            while (head != tail)
            {
                byte* e = ctl + OffRing + (int)(head * 24);
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
                            machine.Bus.Io.Write8(port, (byte)value);
                            break;
                        case 2:
                            machine.Bus.Io.Write16(port, (ushort)value);
                            break;
                        default:
                            machine.Bus.Io.Write32(port, (uint)value);
                            break;
                    }
                }
                else
                {
                    switch (size)
                    {
                        case 1:
                            machine.Memory.Write8(address, (byte)value);
                            break;
                        case 2:
                            machine.Memory.Write16(address, (ushort)value);
                            break;
                        case 8:
                            machine.Memory.Write64(address, value);
                            break;
                        default:
                            machine.Memory.Write32(address, (uint)value);
                            break;
                    }
                }
                s_writes++;
                head = (head + 1) % (uint)RingCapacity;
                Write32(ctl + OffRingCount, head);
            }
        }

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
