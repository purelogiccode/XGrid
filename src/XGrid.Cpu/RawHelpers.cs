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
/// unmanaged and permits the reverse call.</para>
///
/// <para><b>Fault contract.</b> An exception must never unwind out of one of
/// these entries: the frame it would have to cross is hand-emitted machine code
/// with no unwind data, so CoreCLR cannot walk it and delivery of the fault
/// depends on stack layout luck (it aborts the process with
/// <c>0x80131506</c> / <c>0xE0434352</c> when the luck runs out). Each entry
/// therefore catches, records the exception on the CPU
/// (<c>RawCaptureFault</c>, which also sets the emitted code's fault word) and
/// returns; the emitted code sees the word, exits with the fault sentinel, and
/// <c>RunJitBlock</c> rethrows the original exception in managed code after
/// <c>RawExit</c> published the context - exactly the IL runner's observable
/// behavior.</para>
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

    /// <summary>Address of the post-member interrupt poll for the emitted code.</summary>
    public static readonly ulong PollInterruptAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, IntPtr, uint, int>)&PollInterrupt;

    /// <summary>Address of the device read fast path for the emitted code.</summary>
    public static readonly ulong ReadPhysicalAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int, uint>)&ReadPhysical;

    /// <summary>Address of the device write fast path for the emitted code.</summary>
    public static readonly ulong WritePhysicalAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int, uint, void>)&WritePhysical;

    /// <summary>Address of the decode-cache store note for the emitted code.</summary>
    public static readonly ulong NoteStoreAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int, void>)&NoteStore;

    [UnmanagedCallersOnly]
    private static void NoteStore(IntPtr cpuPointer, uint physical, int sizeBits)
    {
        try
        {
            XboxCpu cpu = XboxCpu.RawFromHandle(cpuPointer);
            cpu.RawNoteStorePhysical(physical, sizeBits);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void ExecuteSpec(IntPtr cpuPointer, IntPtr entriesPointer, int index, IntPtr statePointer)
    {
        try
        {
            XboxCpu cpu = XboxCpu.RawFromHandle(cpuPointer);
            DecodeCacheEntry[] entries = Unsafe.As<IntPtr, DecodeCacheEntry[]>(ref entriesPointer);
            ref readonly DecodedInstruction instruction = ref entries[index].Instruction;

            // The raw block keeps Eip/Eflags/Tsc in its scratch block while the
            // handler runs against the context: publish the member's boundary
            // state forward (handlers read Eip for return addresses and relative
            // targets and Eflags for string direction) and copy the result back.
            RawState* state = (RawState*)statePointer;
            cpu.Context.Eip = (uint)*state->Eip;
            cpu.Context.Eflags = *state->Eflags;
            cpu.Context.Tsc = *state->Tsc;

            XboxCpu.SpecHandler? handler = XboxCpu.ResolveSpec(in instruction);
            if (XboxCpu.RawTraceEnabled)
            {
                XboxCpu.RawTrace($"  spec idx={index} {instruction.Mnemonic} handler={handler is not null}");
            }

            if (handler is not null)
            {
                handler(cpu, in instruction);
            }
            else
            {
                cpu.RawExecuteInstruction(in instruction);
            }

            *state->Eip = (uint)cpu.Context.Eip;
            *state->Eflags = cpu.Context.Eflags;
            *state->Tsc = cpu.Context.Tsc;
        }
        catch (Exception ex)
        {
            // The context was published forward before the handler ran, so the
            // member's boundary state is already exact (the IL runner's
            // contract for a faulting member).
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static uint ReadMemory(IntPtr cpuPointer, uint address, int size)
    {
        try
        {
            return XboxCpu.RawFromHandle(cpuPointer).RawReadMemory(address, size);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void WriteMemory(IntPtr cpuPointer, uint address, int size, uint value)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).RawWriteMemory(address, size, value);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    /// <summary>
    /// Fast-mode post-member poll (STI/POPF/string/port members): acknowledges
    /// at the member boundary; a delivered vector is recorded on the CPU for
    /// <c>RunJitBlock</c> to transfer, exactly like the IL runner's post-poll.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int PollInterrupt(IntPtr cpuPointer, IntPtr statePointer, uint ctrl)
    {
        try
        {
            _ = statePointer;
            XboxCpu cpu = XboxCpu.RawFromHandle(cpuPointer);
            int result = cpu.RawPollInterrupt();
            if (XboxCpu.RawTraceEnabled)
            {
                XboxCpu.RawTrace($"  poll ctrl={ctrl:X} -> {result}");
            }

            return result;
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static uint ReadPhysical(IntPtr cpuPointer, uint physical, int size)
    {
        try
        {
            return XboxCpu.RawFromHandle(cpuPointer).RawReadPhysicalJit(physical, size);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void WritePhysical(IntPtr cpuPointer, uint physical, int size, uint value)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).RawWritePhysicalJit(physical, size, value);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    /// <summary>Records a caught helper exception on the CPU (never rethrows here).</summary>
    private static void Capture(IntPtr cpuPointer, Exception exception) =>
        XboxCpu.RawFromHandleOrNull(cpuPointer)?.RawCaptureFault(exception);
}
