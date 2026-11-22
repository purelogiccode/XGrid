using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace XGrid.Xcn1HostProbe
{
    /// <summary>
    /// XCN1 native-host probe. The CPU and the MMIO ring live in a native
    /// process; this client owns the shared RAM and the read shadow, drains
    /// the write ring between slices and never calls the DLL from .NET.
    /// </summary>
    internal static unsafe class Program
    {
        private const int RamSize = 1024 * 1024;
        private const int SectionSize = RamSize + 0x2000;
        private const uint Magic = 0x58474348u;
        private const uint Version = 1u;

        // Control-block offsets (see native/xgridcpu/host.c).
        private const int OffMagic = 0;
        private const int OffVersion = 4;
        private const int OffReady = 8;
        private const int OffQuit = 12;
        private const int OffStatus = 16;
        private const int OffRunBudget = 20;
        private const int OffExecuted = 24;
        private const int OffRc = 32;
        private const int OffRingCount = 36;
        private const int OffShadow = 64;
        private const int OffRing = 4160;
        private const int OffRegs = 5696;

        private static uint Read32(byte* p) => Volatile.Read(ref *(uint*)p);
        private static void Write32(byte* p, uint v) => Volatile.Write(ref *(uint*)p, v);
        private static ulong Read64(byte* p) => Volatile.Read(ref *(ulong*)p);

        private static int Main()
        {
            // Real-mode code at 0x1000:
            //   mov dword ptr [0x2000], 0x12345678
            //   mov eax, [0xF0000000]      ; MMIO read from the shared shadow
            //   mov [0xF0000004], eax      ; MMIO write -> ring
            //   mov ecx, 10
            // loop:
            //   dec ecx
            //   jnz loop
            //   jmp $                      ; the step budget stops the guest
            ReadOnlySpan<byte> code = new byte[]
            {
                0x66, 0xC7, 0x06, 0x00, 0x20, 0x78, 0x56, 0x34, 0x12,
                0x67, 0x66, 0xA1, 0x00, 0x00, 0x00, 0xF0,
                0x67, 0x66, 0xA3, 0x04, 0x00, 0x00, 0xF0,
                0x66, 0xB9, 0x0A, 0x00, 0x00, 0x00,
                0x66, 0x49,
                0x75, 0xFC,
                0xEB, 0xFE,
            };

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
                byte* ctl = basePtr + RamSize;

                Write32(ctl + OffMagic, Magic);
                Write32(ctl + OffVersion, Version);
                code.CopyTo(new Span<byte>(basePtr + 0x1000, code.Length));

                // Device read shadow: 0xF0000000 -> 0xCAFEBABE.
                *(uint*)(ctl + OffShadow) = 0xCAFEBABEu;
                Write32(ctl + OffRunBudget, 100_000);

                var psi = new ProcessStartInfo(host, name)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using Process process = Process.Start(psi)
                    ?? throw new InvalidOperationException("host did not start");

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

                Write32(ctl + OffStatus, 1);
                deadline = DateTime.UtcNow.AddSeconds(30);
                while (Read32(ctl + OffStatus) != 2)
                {
                    if (process.HasExited)
                    {
                        Console.Error.WriteLine($"host exited during run: {process.ExitCode}");
                        return 1;
                    }
                    if (DateTime.UtcNow > deadline)
                    {
                        Console.Error.WriteLine("host run timeout");
                        return 1;
                    }
                    Thread.Sleep(1);
                }

                ulong executed = Read64(ctl + OffExecuted);
                int rc = (int)Read32(ctl + OffRc);
                uint ringCount = Read32(ctl + OffRingCount);
                uint ramWord = Read32(basePtr + 0x2000);
                uint eax = Read32(ctl + OffRegs);
                uint ecx = Read32(ctl + OffRegs + 4);
                uint eip = Read32(ctl + OffRegs + 32);

                uint writeGpa = 0;
                uint writeSize = 0;
                ulong writeValue = 0;
                if (ringCount >= 1)
                {
                    byte* entry = ctl + OffRing;
                    writeGpa = Read32(entry);
                    writeSize = Read32(entry + 4);
                    writeValue = Read64(entry + 16);
                }

                Console.WriteLine($"host: executed={executed} rc={rc} ring={ringCount} " +
                    $"write[0x{writeGpa:X8}/{writeSize}]=0x{writeValue:X8}");
                Console.WriteLine($"host: ram[0x2000]=0x{ramWord:X8} eax=0x{eax:X8} " +
                    $"ecx=0x{ecx:X8} eip=0x{eip:X8}");

                Write32(ctl + OffRunBudget, 100_000_000);
                var sw = Stopwatch.StartNew();
                Write32(ctl + OffStatus, 1);
                while (Read32(ctl + OffStatus) != 2)
                {
                    if (process.HasExited)
                    {
                        Console.Error.WriteLine($"host exited during perf: {process.ExitCode}");
                        return 1;
                    }
                    Thread.Sleep(1);
                }
                sw.Stop();
                ulong perfSteps = Read64(ctl + OffExecuted);
                double mps = perfSteps / sw.Elapsed.TotalSeconds / 1e6;
                Console.WriteLine($"perf: steps={perfSteps} wall={sw.Elapsed.TotalSeconds:F3} s = {mps:F1} Msteps/s");

                Write32(ctl + OffQuit, 1);
                Write32(ctl + OffStatus, 1);
                if (!process.WaitForExit(5000))
                {
                    process.Kill(true);
                }

                if (executed != 100_000u || rc != 0 || ringCount != 1 ||
                    writeGpa != 0xF0000004u || writeSize != 4 ||
                    writeValue != 0xCAFEBABEul || ramWord != 0x12345678u ||
                    eax != 0xCAFEBABEu || ecx != 0 || eip != 0x1021u ||
                    perfSteps != 100_000_000u)
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
    }
}
