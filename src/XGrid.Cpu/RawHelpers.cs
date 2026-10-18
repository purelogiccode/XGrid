using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>
/// Entry points the C3 raw blocks call for members they do not encode natively.
///
/// <para>These are reverse-P/Invoke stubs (delegate function pointers) rather
/// than <c>[UnmanagedCallersOnly]</c>: the raw function itself is entered from
/// managed code through a function pointer, so the runtime does not consider
/// its calls unmanaged, and <c>[UnmanagedCallersOnly]</c> targets would reject
/// them. The stub establishes the transition; exceptions (page faults,
/// unsupported instructions) propagate back through the native frame to
/// <c>RunJitBlock</c> exactly like the IL runner's.</para>
/// </summary>
internal static unsafe class RawHelpers
{
    /// <summary>Runs one member through the resolved spec handler or interpreter body.</summary>
    public delegate void ExecuteSpecCallback(IntPtr cpuPointer, IntPtr entriesPointer, int index);

    /// <summary>Reads guest memory through the exact interpreter path.</summary>
    public delegate uint ReadMemoryCallback(IntPtr cpuPointer, uint address, int size);

    /// <summary>Writes guest memory through the exact interpreter path.</summary>
    public delegate void WriteMemoryCallback(IntPtr cpuPointer, uint address, int size, uint value);

    private static readonly ExecuteSpecCallback ExecuteSpecTarget = ExecuteSpec;
    private static readonly ReadMemoryCallback ReadMemoryTarget = ReadMemory;
    private static readonly WriteMemoryCallback WriteMemoryTarget = WriteMemory;

    /// <summary>Address of the spec-handler trampoline for the emitted code.</summary>
    public static readonly ulong ExecuteSpecAddress =
        (ulong)(nint)Marshal.GetFunctionPointerForDelegate(ExecuteSpecTarget);

    /// <summary>Address of the memory-read trampoline for the emitted code.</summary>
    public static readonly ulong ReadMemoryAddress =
        (ulong)(nint)Marshal.GetFunctionPointerForDelegate(ReadMemoryTarget);

    /// <summary>Address of the memory-write trampoline for the emitted code.</summary>
    public static readonly ulong WriteMemoryAddress =
        (ulong)(nint)Marshal.GetFunctionPointerForDelegate(WriteMemoryTarget);

    private static void ExecuteSpec(IntPtr cpuPointer, IntPtr entriesPointer, int index)
    {
        XboxCpu cpu = Unsafe.As<IntPtr, XboxCpu>(ref cpuPointer);
        DecodeCacheEntry[] entries = Unsafe.As<IntPtr, DecodeCacheEntry[]>(ref entriesPointer);
        ref readonly DecodedInstruction instruction = ref entries[index].Instruction;
        XboxCpu.SpecHandler? handler = XboxCpu.ResolveSpec(in instruction);
        if (handler is not null)
        {
            handler(cpu, in instruction);
        }
        else
        {
            cpu.RawExecuteInstruction(in instruction);
        }
    }

    private static uint ReadMemory(IntPtr cpuPointer, uint address, int size)
    {
        XboxCpu cpu = Unsafe.As<IntPtr, XboxCpu>(ref cpuPointer);
        return cpu.RawReadMemory(address, size);
    }

    private static void WriteMemory(IntPtr cpuPointer, uint address, int size, uint value)
    {
        XboxCpu cpu = Unsafe.As<IntPtr, XboxCpu>(ref cpuPointer);
        cpu.RawWriteMemory(address, size, value);
    }
}
