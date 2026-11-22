using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace XGrid.Xcn1HostProbe
{
    /// <summary>
    /// XCN1 native-host probe. The CPU lives in a native process; this client
    /// owns the shared RAM, the read shadow, the write ring and the live-read
    /// service, and never calls the DLL from .NET.
    /// </summary>
    internal static unsafe class Program
    {
        private const int RamSize = 1024 * 1024;
        private const int ShadowSize = 0x10000;
        private const int CtlOffset = RamSize;
        private const int SectionSize = CtlOffset + 0x250 + ShadowSize + (RingCapacity * 24) + 0x100;

        private const uint Magic = 0x58474348u;
        private const uint Version = 5u;
        private const int RingCapacity = 1024;

        // Control-block offsets, relative to the control area (CtlOffset).
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
        private const int OffRingCapacity = 576;
        private const int OffRamSize = 568;
        private const int OffShadowSize = 572;
        private const int OffShadow = 0x250;
        private const int OffRing = 0x250 + ShadowSize;
        private const int OffRegs = OffRing + (RingCapacity * 24);

        private const uint WinIo = 0x1;
        private const uint WinLive = 0x2;
        private const uint IoRequest = 1;
        private const uint IoDone = 2;
        private const uint IoError = 0x80000000u;

        private readonly struct Write
        {
            public Write(uint address, uint size, uint flags, ulong value)
            {
                Address = address;
                Size = size;
                Flags = flags;
                Value = value;
            }

            public uint Address { get; }

            public uint Size { get; }

            public uint Flags { get; }

            public ulong Value { get; }
        }

        private static readonly List<Write> Writes = new List<Write>();
        private static int s_liveReads;
        private static int s_portReads;
        private static long s_serviceTicks;
        private static int s_serviceCount;

        private static uint Read32(byte* p) => Volatile.Read(ref *(uint*)p);

        private static void Write32(byte* p, uint v) => Volatile.Write(ref *(uint*)p, v);

        private static ulong Read64(byte* p) => Volatile.Read(ref *(ulong*)p);

        private static void Write64(byte* p, ulong v) => Volatile.Write(ref *(ulong*)p, v);

        private static int Main()
        {
            string host = Environment.GetEnvironmentVariable("XGRIDCPU_HOST")
                ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..",
                    "native", "xgridcpu", "xgridcpu-host.exe"));
            if (!File.Exists(host))
            {
                Console.Error.WriteLine($"native host not found: {host}");
                return 2;
            }

            string name = "xgrid-host-" + Guid.NewGuid().ToString("N");
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
                WriteWindow(ctl, 0, 0xF0000000u, 0x1000, 0, 0, 0);
                WriteWindow(ctl, 1, 0xF1000000u, 0x1000, WinLive, 0, 0);
                WriteWindow(ctl, 2, 0x60u, 8, WinIo | WinLive, 0, 0);
                Write32(ctl + OffNWindows, 3);

                // Shadow-only read window: 0xF0000000 -> 0xCAFEBABE.
                *(uint*)(ctl + OffShadow) = 0xCAFEBABEu;

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
                        return 1;
                    }
                    if (DateTime.UtcNow > deadline)
                    {
                        Console.Error.WriteLine("host ready timeout");
                        return 1;
                    }
                    Thread.Sleep(10);
                }

                // Real-mode program: shadow read, shadow write, two live MMIO
                // reads, a live MMIO write, a live port read and a port write.
                ReadOnlySpan<byte> code = new byte[]
                {
                    0x67, 0x66, 0xC7, 0x05, 0x00, 0x20, 0x00, 0x00, 0x78, 0x56, 0x34, 0x12,
                    0x67, 0x66, 0xA1, 0x00, 0x00, 0x00, 0xF0,
                    0x67, 0x66, 0xA3, 0x04, 0x20, 0x00, 0x00,
                    0x67, 0x66, 0x89, 0x05, 0x04, 0x00, 0x00, 0xF0,
                    0x67, 0x66, 0x8B, 0x1D, 0x00, 0x00, 0x00, 0xF1,
                    0x67, 0x66, 0x89, 0x1D, 0x08, 0x20, 0x00, 0x00,
                    0x67, 0x66, 0x8B, 0x1D, 0x00, 0x00, 0x00, 0xF1,
                    0x67, 0x66, 0x89, 0x1D, 0x0C, 0x20, 0x00, 0x00,
                    0x67, 0x66, 0x89, 0x1D, 0x04, 0x00, 0x00, 0xF1,
                    0x66, 0xE5, 0x60,
                    0x67, 0x66, 0xA3, 0x10, 0x20, 0x00, 0x00,
                    0x66, 0xE7, 0x60,
                    0x66, 0xB9, 0x0A, 0x00, 0x00, 0x00,
                    0x66, 0x49,
                    0x75, 0xFC,
                    0xEB, 0xFE,
                };
                code.CopyTo(new Span<byte>(basePtr + 0x1000, code.Length));

                Write32(ctl + OffRunBudget, 100_000);
                RunSlice(ctl, process);

                uint ramWord = Read32(basePtr + 0x2000);
                uint shadowWord = Read32(basePtr + 0x2004);
                uint live1 = Read32(basePtr + 0x2008);
                uint live2 = Read32(basePtr + 0x200C);
                uint portWord = Read32(basePtr + 0x2010);
                uint eax = Read32(ctl + OffRegs);
                uint ecx = Read32(ctl + OffRegs + 4);
                int rc = (int)Read32(ctl + OffRc);

                Console.WriteLine($"run: rc={rc} ring={Writes.Count} ram=0x{ramWord:X8} " +
                    $"shadow=0x{shadowWord:X8} live={live1},{live2} port=0x{portWord:X8} " +
                    $"eax=0x{eax:X8} ecx=0x{ecx:X8}");
                for (int i = 0; i < Writes.Count; i++)
                {
                    Write w = Writes[i];
                    Console.WriteLine($"  write[{i}] addr=0x{w.Address:X8} size={w.Size} " +
                        $"io={w.Flags & WinIo} value=0x{w.Value:X8}");
                }

                bool ok = rc == 0
                    && ramWord == 0x12345678u
                    && shadowWord == 0xCAFEBABEu
                    && live1 == 1 && live2 == 2
                    && portWord == 0x1234u
                    && s_liveReads == 2 && s_portReads == 1
                    && eax == 0x1234u && ecx == 0
                    && Writes.Count == 3
                    && Writes[0].Address == 0xF0000004u && Writes[0].Flags == 0
                    && Writes[0].Value == 0xCAFEBABEul
                    && Writes[1].Address == 0xF1000004u && Writes[1].Flags == 0
                    && Writes[1].Value == 2
                    && Writes[2].Address == 0x60u && Writes[2].Flags == WinIo
                    && Writes[2].Value == 0x1234ul;

                // IRQ phase: real-mode IVT 0x20 -> 0x3000:0, sti, wait for a flag.
                ReadOnlySpan<byte> irqCode = new byte[]
                {
                    0xC7, 0x06, 0x80, 0x00, 0x00, 0x30,
                    0xC7, 0x06, 0x82, 0x00, 0x00, 0x00,
                    0xFB,
                    0x80, 0x3E, 0x04, 0x20, 0x01,
                    0x75, 0xF9,
                    0xEB, 0xFE,
                };
                ReadOnlySpan<byte> irqHandler = new byte[]
                {
                    0xC6, 0x06, 0x04, 0x20, 0x01,
                    0xCF,
                };
                irqCode.CopyTo(new Span<byte>(basePtr + 0x1000, irqCode.Length));
                irqHandler.CopyTo(new Span<byte>(basePtr + 0x3000, irqHandler.Length));
                *(uint*)(basePtr + 0x2004) = 0;

                Write32(ctl + OffReset, 2);
                Write32(ctl + OffRunBudget, 10_000);
                RunSlice(ctl, process);

                Write32(ctl + OffIrqVector, 0x20);
                Write32(ctl + OffIrqPending, 1);
                Write32(ctl + OffRunBudget, 100_000);
                RunSlice(ctl, process);

                byte irqFlag = *(basePtr + 0x2004);
                Console.WriteLine($"irq: flag={irqFlag}");
                ok &= irqFlag == 1;

                // Live-read latency: 100k device reads, each a round trip
                // across the process boundary.
                ReadOnlySpan<byte> liveCode = new byte[]
                {
                    0x66, 0xB9, 0xA0, 0x86, 0x01, 0x00,
                    0x67, 0x66, 0x8B, 0x1D, 0x00, 0x00, 0x00, 0xF1,
                    0x66, 0x49,
                    0x75, 0xF4,
                    0xEB, 0xFE,
                };
                liveCode.CopyTo(new Span<byte>(basePtr + 0x1000, liveCode.Length));
                Write32(ctl + OffReset, 2);
                Write32(ctl + OffRunBudget, 400_000);
                int beforeLive = s_liveReads;
                s_serviceTicks = 0;
                s_serviceCount = 0;
                var liveWatch = Stopwatch.StartNew();
                RunSlice(ctl, process);
                liveWatch.Stop();
                int liveCount = s_liveReads - beforeLive;
                double nsPerRead = liveCount == 0
                    ? 0
                    : liveWatch.Elapsed.TotalMilliseconds * 1e6 / liveCount;
                Console.WriteLine($"live: reads={liveCount} " +
                    $"wall={liveWatch.Elapsed.TotalMilliseconds:F1} ms = " +
                    $"{nsPerRead:F0} ns/read; service=" +
                    $"{s_serviceTicks * 1e9 / Stopwatch.Frequency / Math.Max(1, s_serviceCount):F0} ns/answer");
                ok &= liveCount == 100_000;

                // Shadow-read control: the same loop against a shadow window,
                // so the QEMU MMIO path cost is separated from the round trip.
                ReadOnlySpan<byte> shadowCode = new byte[]
                {
                    0x66, 0xB9, 0xA0, 0x86, 0x01, 0x00,
                    0x67, 0x66, 0x8B, 0x1D, 0x00, 0x00, 0x00, 0xF0,
                    0x66, 0x49,
                    0x75, 0xF4,
                    0xEB, 0xFE,
                };
                shadowCode.CopyTo(new Span<byte>(basePtr + 0x1000, shadowCode.Length));
                Write32(ctl + OffReset, 2);
                Write32(ctl + OffRunBudget, 400_000);
                var shadowWatch = Stopwatch.StartNew();
                int beforeShadow = s_liveReads + s_portReads;
                RunSlice(ctl, process);
                shadowWatch.Stop();
                double nsShadow = shadowWatch.Elapsed.TotalMilliseconds * 1e6 / 100_000;
                Console.WriteLine($"shadow: reads=100000 " +
                    $"wall={shadowWatch.Elapsed.TotalMilliseconds:F1} ms = " +
                    $"{nsShadow:F0} ns/read");
                ok &= s_liveReads + s_portReads == beforeShadow;

                // RAM-read control: the same loop with a normal memory load.
                ReadOnlySpan<byte> ramCode = new byte[]
                {
                    0x66, 0xB9, 0xA0, 0x86, 0x01, 0x00,
                    0x67, 0x66, 0x8B, 0x1D, 0x00, 0x20, 0x00, 0x00,
                    0x66, 0x49,
                    0x75, 0xF4,
                    0xEB, 0xFE,
                };
                ramCode.CopyTo(new Span<byte>(basePtr + 0x1000, ramCode.Length));
                Write32(ctl + OffReset, 2);
                Write32(ctl + OffRunBudget, 400_000);
                var ramWatch = Stopwatch.StartNew();
                RunSlice(ctl, process);
                ramWatch.Stop();
                double nsRam = ramWatch.Elapsed.TotalMilliseconds * 1e6 / 100_000;
                Console.WriteLine($"ram: reads=100000 " +
                    $"wall={ramWatch.Elapsed.TotalMilliseconds:F1} ms = " +
                    $"{nsRam:F1} ns/read");

                // MMIO write control: queued writes, no round trip.
                ReadOnlySpan<byte> writeCode = new byte[]
                {
                    0x66, 0xB9, 0xA0, 0x86, 0x01, 0x00,
                    0x67, 0x66, 0x89, 0x05, 0x04, 0x00, 0x00, 0xF0,
                    0x66, 0x49,
                    0x75, 0xF4,
                    0xEB, 0xFE,
                };
                writeCode.CopyTo(new Span<byte>(basePtr + 0x1000, writeCode.Length));
                Write32(ctl + OffReset, 2);
                Write32(ctl + OffRunBudget, 400_000);
                var writeWatch = Stopwatch.StartNew();
                RunSlice(ctl, process);
                writeWatch.Stop();
                double nsWrite = writeWatch.Elapsed.TotalMilliseconds * 1e6 / 100_000;
                Console.WriteLine($"write: 100000 " +
                    $"wall={writeWatch.Elapsed.TotalMilliseconds:F1} ms = " +
                    $"{nsWrite:F0} ns/write");

                // Perf phase: a tight self-loop at 0x1000.
                *(uint*)(basePtr + 0x1000) = 0xFEEB;
                Write32(ctl + OffReset, 2);
                Write32(ctl + OffRunBudget, 100_000_000);
                var sw = Stopwatch.StartNew();
                RunSlice(ctl, process);
                sw.Stop();
                ulong perfSteps = Read64(ctl + OffExecuted);
                double mps = perfSteps / sw.Elapsed.TotalSeconds / 1e6;
                Console.WriteLine($"perf: steps={perfSteps} wall={sw.Elapsed.TotalSeconds:F3} s " +
                    $"= {mps:F1} Msteps/s");
                ok &= perfSteps == 100_000_000u;

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

                if (!ok)
                {
                    Console.Error.WriteLine("XCN1 NATIVE HOST FAIL");
                    return 1;
                }

                Console.WriteLine("XCN1 NATIVE HOST PASS");
                return 0;
            }
            finally
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
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

        private static void RunSlice(byte* ctl, Process process)
        {
            Write32(ctl + OffStatus, 1);
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            int spins = 0;
            while (true)
            {
                if (Read32(ctl + OffIoState) == IoRequest)
                {
                    long start = Stopwatch.GetTimestamp();
                    DrainRing(ctl);
                    AnswerRead(ctl);
                    s_serviceTicks += Stopwatch.GetTimestamp() - start;
                    s_serviceCount++;
                    continue;
                }
                if (Read32(ctl + OffStatus) == IoDone)
                {
                    DrainRing(ctl);
                    break;
                }
                if (Read32(ctl + OffRingCount) != Read32(ctl + OffRingTail))
                {
                    DrainRing(ctl);
                    continue;
                }
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"host exited during run: {process.ExitCode}");
                }
                if ((++spins & 0xFFF) == 0 && DateTime.UtcNow > deadline)
                {
                    throw new InvalidOperationException("host run timeout");
                }
                Thread.SpinWait(32);
            }
        }

        private static void DrainRing(byte* ctl)
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
                Writes.Add(new Write(address, size, flags, value));
                head = (head + 1) % (uint)RingCapacity;
                Write32(ctl + OffRingCount, head);
            }
        }

        private static void AnswerRead(byte* ctl)
        {
            uint address = Read32(ctl + OffIoAddr);
            uint size = Read32(ctl + OffIoSize);
            uint flags = Read32(ctl + OffIoFlags);
            if ((flags & 0x80000000u) != 0)
            {
                // Interrupt acknowledge: the probe only queues one vector.
                Write64(ctl + OffIoValue, Read32(ctl + OffIrqVector));
                Write32(ctl + OffIoState, IoDone);
                return;
            }
            ulong value;
            if ((flags & WinIo) != 0)
            {
                uint port = address & 0xFF;
                s_portReads++;
                value = port == 0x60 ? 0x1234u : 0xFFFFFFFFu;
                if (size < 2)
                {
                    value &= 0xFF;
                }
            }
            else
            {
                s_liveReads++;
                value = address == 0xF1000000u ? (uint)s_liveReads : 0u;
            }
            Write64(ctl + OffIoValue, value);
            Write32(ctl + OffIoFlags, flags & ~IoError);
            Write32(ctl + OffIoState, IoDone);
        }
    }
}

