using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace XGrid.Xcn0Probe
{
    /// <summary>
    /// XCN0 acceptance probe: drives libxgridcpu from managed code with a
    /// real-mode guest, one MMIO region and a bounded run.
    /// </summary>
    internal static unsafe class Program
    {
        private const string Library = "xgridcpu";

        private static long s_mmioReads;
        private static long s_mmioWrites;
        private static ulong s_mmioLast;
        private static uint s_mmioLastAddress;

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
        private static extern int xgc_map_mmio(IntPtr cpu, uint gpa, uint size,
            IntPtr read, IntPtr write, IntPtr opaque);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int xgc_run(IntPtr cpu, uint maxInsns, out ulong executed);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void xgc_get_regs(IntPtr cpu, out XgcRegs regs);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static ulong OnMmioRead(IntPtr opaque, uint gpa, uint size)
        {
            _ = opaque;
            _ = gpa;
            _ = size;
            s_mmioReads++;
            return 0xCAFEBABEul;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnMmioWrite(IntPtr opaque, uint gpa, uint size, ulong value)
        {
            _ = opaque;
            _ = size;
            s_mmioWrites++;
            s_mmioLast = value;
            s_mmioLastAddress = gpa;
        }

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
            //   mov eax, [0xF0000000]
            //   mov [0xF0000004], eax
            //   mov ecx, 1000000
            // loop:
            //   dec ecx
            //   jnz loop
            //   hlt
            ReadOnlySpan<byte> code = new byte[]
            {
                0x66, 0xC7, 0x06, 0x00, 0x20, 0x78, 0x56, 0x34, 0x12,
                0x67, 0x66, 0xA1, 0x00, 0x00, 0x00, 0xF0,
                0x67, 0x66, 0xA3, 0x04, 0x00, 0x00, 0xF0,
                0x66, 0xB9, 0x40, 0x42, 0x0F, 0x00,
                0x66, 0x49,
                0x75, 0xFC,
                0xF4,
            };

            if (xgc_init() != 0)
            {
                Console.Error.WriteLine("xgc_init failed");
                return 1;
            }

            byte* ram = (byte*)NativeMemory.AlignedAlloc(1024 * 1024, 4096);
            try
            {
                new Span<byte>(ram, 1024 * 1024).Fill(0x90);
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

                    IntPtr read = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, uint, uint, ulong>)&OnMmioRead;
                    IntPtr write = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, uint, uint, ulong, void>)&OnMmioWrite;
                    if (xgc_map_mmio(cpu, 0xF0000000u, 0x1000, read, write, IntPtr.Zero) != 0)
                    {
                        Console.Error.WriteLine("xgc_map_mmio failed");
                        return 1;
                    }

                    int rc = xgc_run(cpu, 3_000_000, out ulong executed);
                    xgc_get_regs(cpu, out XgcRegs regs);

                    uint ramWord = *(uint*)(ram + 0x2000);
                    Console.WriteLine($"run rc={rc} executed={executed}");
                    Console.WriteLine($"ram[0x2000]=0x{ramWord:X8} (want 0x12345678)");
                    Console.WriteLine($"mmio reads={s_mmioReads} writes={s_mmioWrites} " +
                        $"last=0x{s_mmioLast:X} addr=0x{s_mmioLastAddress:X8}");
                    Console.WriteLine($"eax=0x{regs.Eax:X8} ecx=0x{regs.Ecx:X8} " +
                        $"eip=0x{regs.Eip:X8} eflags=0x{regs.Eflags:X8}");

                    if (ramWord != 0x12345678u ||
                        s_mmioReads != 1 || s_mmioWrites != 1 ||
                        s_mmioLast != 0xCAFEBABEul || s_mmioLastAddress != 0xF0000004u ||
                        regs.Eax != 0xCAFEBABEu || regs.Ecx != 0)
                    {
                        Console.Error.WriteLine("XCN0 CSHARP SPIKE FAIL");
                        return 1;
                    }

                    Console.WriteLine("XCN0 CSHARP SPIKE PASS");
                    return 0;
                }
                finally
                {
                    xgc_destroy(cpu);
                }
            }
            finally
            {
                NativeMemory.AlignedFree(ram);
            }
        }
    }
}
