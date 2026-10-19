using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>
/// Entry points the C3 raw blocks call for members they do not encode natively.
///
/// <para>The raw runner is entered through a delegate over its function pointer
/// (a real managed-to-native transition), so these entries can be plain
/// <c>[UnmanagedCallersOnly]</c> methods: the runtime sees the raw frame as
/// unmanaged and permits the reverse call. Exceptions (page faults,
/// unsupported instructions) propagate back through the native frame to
/// <c>RunJitBlock</c> exactly like the IL runner's.</para>
/// </summary>
internal static unsafe class RawHelpers
{
    /// <summary>Address of the spec-handler entry for the emitted code.</summary>
    public static readonly ulong ExecuteSpecAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, void>)&ExecuteSpec;

    /// <summary>Address of the memory-read entry for the emitted code.</summary>
    public static readonly ulong ReadMemoryAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int, uint>)&ReadMemory;

    /// <summary>Address of the memory-write entry for the emitted code.</summary>
    public static readonly ulong WriteMemoryAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int, uint, void>)&WriteMemory;

    [UnmanagedCallersOnly]
    private static void ExecuteSpec(IntPtr cpuPointer, IntPtr entriesPointer, int index, IntPtr statePointer)
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

        // The handler runs against the context, but the raw block keeps Eip,
        // Eflags and Tsc in its own scratch block: publish them back or the
        // reload would replay the values from before the call.
        RawState* state = (RawState*)statePointer;
        *state->Eip = (uint)cpu.Context.Eip;
        *state->Eflags = cpu.Context.Eflags;
        *state->Tsc = cpu.Context.Tsc;
    }


    [UnmanagedCallersOnly]
    private static uint ReadMemory(IntPtr cpuPointer, uint address, int size)
    {
        XboxCpu cpu = Unsafe.As<IntPtr, XboxCpu>(ref cpuPointer);
        return cpu.RawReadMemory(address, size);
    }

    [UnmanagedCallersOnly]
    private static void WriteMemory(IntPtr cpuPointer, uint address, int size, uint value)
    {
        XboxCpu cpu = Unsafe.As<IntPtr, XboxCpu>(ref cpuPointer);
        cpu.RawWriteMemory(address, size, value);
    }
}
