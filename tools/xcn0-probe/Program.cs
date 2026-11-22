using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace XGrid.Xcn0Probe
{
    /// <summary>
    /// XCN0 acceptance probe: drives libxgridcpu from managed code.
    ///
    /// This probe covers the vCPU, guest RAM, the architectural registers and
    /// the instruction budget. MMIO under the .NET-hosted DLL is deferred to
    /// XCN1: a managed callback fails at the callback and the native shadow
    /// ring fails before the first ring access (0xC0000409), while the same
    /// ring code passes as a native executable (native/xgridcpu/ring_test.c).
    /// </summary>
    internal static unsafe class Program
    {
        private const string Library = "xgridcpu";

        [StructLayout(LayoutKind.Sequential)]
        private struct XgcRegs
        {
            public uint Eax;
            public uint Ecx;
            public uint Edx;
            public uint Ebx;
            public uint Esp;
            public uint Ebp;
            public uint Esi;
            public uint Edi;
            public uint Eip;
            public uint Eflags;
            public ushort Cs;
            public ushort Ds;
            public ushort Es;
            public ushort Fs;
            public ushort Gs;
            public ushort Ss;
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int xgc_init();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr xgc_create();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void xgc_destroy(IntPtr cpu);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int xgc_map_ram(IntPtr cpu, uint gpa, uint size, IntPtr host);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int xgc_run(IntPtr cpu, uint maxInsns, out ulong executed);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void xgc_get_regs(IntPtr cpu, out XgcRegs regs);

        private static int Main()
        {
            string dll = Environment.GetEnvironmentVariable("XGRIDCPU_DLL")
                ?? Path.Combine(AppContext.BaseDirectory, "xgridcpu.dll");
            NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name, assembly, path) =>
            {
                return name == Library ? NativeLibrary.Load(dll) : IntPtr.Zero;
            });

            if (!NativeLibrary.TryLoad(dll, out _))
            {
                Console.Error.WriteLine($"xgridcpu.dll not found: {dll}");
                return 2;
            }

            // Real-mode code at 0x1000:
            //   mov dword ptr [0x2000], 0x12345678
            //   mov eax, [0xF0000000]      ; a RAM window in this leg
            //   mov [0xF0000004], eax
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

            if (xgc_init() != 0)
            {
                Console.Error.WriteLine("xgc_init failed");
                return 1;
            }

            byte* ram = (byte*)NativeMemory.AlignedAlloc(1024 * 1024, 4096);
            byte* deviceWindow = (byte*)NativeMemory.AlignedAlloc(0x1000, 4096);
            try
            {
                new Span<byte>(ram, 1024 * 1024).Fill(0x90);
                new Span<byte>(deviceWindow, 0x1000).Fill(0x90);
                code.CopyTo(new Span<byte>(ram + 0x1000, code.Length));

                IntPtr cpu = xgc_create();
                if (cpu == IntPtr.Zero)
                {
                    Console.Error.WriteLine("xgc_create failed");
                    return 1;
                }

                try
                {
                    if (xgc_map_ram(cpu, 0, 1024 * 1024, (IntPtr)ram) != 0)
                    {
                        Console.Error.WriteLine("xgc_map_ram failed");
                        return 1;
                    }
                    if (xgc_map_ram(cpu, 0xF0000000u, 0x1000, (IntPtr)deviceWindow) != 0)
                    {
                        Console.Error.WriteLine("xgc_map_ram (device window) failed");
                        return 1;
                    }

                    int rc = xgc_run(cpu, 100_000, out ulong executed);
                    xgc_get_regs(cpu, out XgcRegs regs);

                    uint ramWord = *(uint*)(ram + 0x2000);
                    uint deviceWord = *(uint*)(deviceWindow + 4);
                    Console.WriteLine($"run rc={rc} executed={executed}");
                    Console.WriteLine($"ram[0x2000]=0x{ramWord:X8} (want 0x12345678)");
                    Console.WriteLine($"device[0xF0000004]=0x{deviceWord:X8} (want 0x90909090)");
                    Console.WriteLine($"eax=0x{regs.Eax:X8} ecx=0x{regs.Ecx:X8} " +
                        $"eip=0x{regs.Eip:X8} eflags=0x{regs.Eflags:X8}");

                    if (ramWord != 0x12345678u || executed != 100_000u ||
                        deviceWord != 0x90909090u ||
                        regs.Eax != 0x90909090u || regs.Ecx != 0 || regs.Eip != 0x1021u)
                    {
                        Console.Error.WriteLine("XCN0 CSHARP SPIKE FAIL");
                        return 1;
                    }

                    Console.WriteLine("XCN0 CSHARP SPIKE PASS (vCPU/RAM/step; MMIO host boundary is XCN1)");

                    var sw = Stopwatch.StartNew();
                    int prc = xgc_run(cpu, 100_000_000, out ulong perfSteps);
                    sw.Stop();
                    double mps = perfSteps / sw.Elapsed.TotalSeconds / 1e6;
                    Console.WriteLine($"perf: rc={prc} steps={perfSteps} wall={sw.Elapsed.TotalSeconds:F3} s = {mps:F1} Msteps/s");
                    return 0;
                }
                finally
                {
                    xgc_destroy(cpu);
                }
            }
            finally
            {
                NativeMemory.AlignedFree(deviceWindow);
                NativeMemory.AlignedFree(ram);
            }
        }
    }
}
