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

    /// <summary>Address of the FPU push-single entry (Fld m32).</summary>
    public static readonly ulong FpuPushSingleAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, void>)&FpuPushSingle;

    /// <summary>Address of the FPU single-bits entry (Fst/Fstp m32).</summary>
    public static readonly ulong FpuSingleBitsAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint>)&FpuSingleBits;

    /// <summary>Address of the FPU pop entry (Fstp half).</summary>
    public static readonly ulong FpuPopAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, void>)&FpuPop;

    /// <summary>Address of the FPU register-push entry (Fld st(i)).</summary>
    public static readonly ulong FpuPushRegisterAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, void>)&FpuPushRegister;

    /// <summary>Address of the FPU register-store entry (Fst st(i)).</summary>
    public static readonly ulong FpuStoreRegisterAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, void>)&FpuStoreRegister;

    /// <summary>Address of the FPU register-store-and-pop entry (Fstp st(i)).</summary>
    public static readonly ulong FpuStoreRegisterPopAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, void>)&FpuStoreRegisterPop;

    /// <summary>Address of the FPU integer-push entry (Fild m32).</summary>
    public static readonly ulong FpuPushIntegerAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, void>)&FpuPushInteger;

    /// <summary>Address of the FPU integer-bits entry (Fist/Fistp m32).</summary>
    public static readonly ulong FpuIntegerBitsAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint>)&FpuIntegerBits;

    /// <summary>Address of the MMX register read entry (movq source).</summary>
    public static readonly ulong MmxReadAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, ulong>)&MmxRead;

    /// <summary>Address of the MMX register write entry (movq/movntq destination).</summary>
    public static readonly ulong MmxWriteAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, ulong, void>)&MmxWrite;

    /// <summary>Address of the x87 register-arithmetic entry.</summary>
    public static readonly ulong FpuArithRegAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int, void>)&FpuArithReg;

    /// <summary>Address of the x87 popping register-arithmetic entry.</summary>
    public static readonly ulong FpuArithPopAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, void>)&FpuArithPop;

    /// <summary>Address of the x87 memory-arithmetic entry.</summary>
    public static readonly ulong FpuArithMemAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, ulong, void>)&FpuArithMem;

    /// <summary>Address of the x87 exchange entry (Fxch).</summary>
    public static readonly ulong FpuExchangeAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, void>)&FpuExchange;

    /// <summary>Address of the x87 constant-one push entry (Fld1).</summary>
    public static readonly ulong FpuPushOneAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, void>)&FpuPushOne;

    /// <summary>Address of the x87 sign-change entry (Fchs).</summary>
    public static readonly ulong FpuChangeSignAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, void>)&FpuChangeSign;

    /// <summary>Address of the x87 register-compare entry (Fcom st(i)).</summary>
    public static readonly ulong FpuCompareRegAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int, void>)&FpuCompareReg;

    /// <summary>Address of the x87 memory-compare entry (Fcom m32/m64).</summary>
    public static readonly ulong FpuCompareMemAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, ulong, void>)&FpuCompareMem;

    /// <summary>Address of the scalar-SSE arithmetic register entry.</summary>
    public static readonly ulong SseArithRegAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int, void>)&SseArithReg;

    /// <summary>Address of the scalar-SSE arithmetic memory entry.</summary>
    public static readonly ulong SseArithMemAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, uint, void>)&SseArithMem;

    /// <summary>Address of the scalar-SSE unary entry.</summary>
    public static readonly ulong SseUnaryRegAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int, void>)&SseUnaryReg;

    /// <summary>Address of the Movss register entry.</summary>
    public static readonly ulong SseMovssRegAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, int, void>)&SseMovssReg;

    /// <summary>Address of the Movss load entry.</summary>
    public static readonly ulong SseMovssMemAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, uint, void>)&SseMovssMem;

    /// <summary>Address of the Movss store entry.</summary>
    public static readonly ulong SseMovssStoreAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, uint>)&SseMovssStore;

    /// <summary>Address of the Cvttss2Si memory conversion entry.</summary>
    public static readonly ulong SseCvtAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, uint, int>)&SseCvt;

    /// <summary>Address of the Cvttss2Si register conversion entry.</summary>
    public static readonly ulong SseCvtRegAddress =
        (ulong)(nint)(delegate* unmanaged<IntPtr, int, int>)&SseCvtReg;

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

            if (cpu.RawShapeTally)
            {
                cpu.NoteRawHelperShape(
                    instruction.Mnemonic,
                    instruction.Operand1.Kind,
                    instruction.Operand2.Kind,
                    instruction.Operand1.Size);
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
    private static void FpuPushSingle(IntPtr cpuPointer, uint bits)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuPushSingle(bits);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static uint FpuSingleBits(IntPtr cpuPointer)
    {
        try
        {
            return XboxCpu.RawFromHandle(cpuPointer).JitFpuSingleBits();
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuPop(IntPtr cpuPointer)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuPop();
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuPushRegister(IntPtr cpuPointer, int index)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuPushRegister(index);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuStoreRegister(IntPtr cpuPointer, int index)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuStoreRegister(index);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuStoreRegisterPop(IntPtr cpuPointer, int index)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuStoreRegisterPop(index);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuPushInteger(IntPtr cpuPointer, uint bits)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuPushInteger(bits);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static uint FpuIntegerBits(IntPtr cpuPointer)
    {
        try
        {
            return XboxCpu.RawFromHandle(cpuPointer).JitFpuIntegerBits();
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static ulong MmxRead(IntPtr cpuPointer, int index)
    {
        try
        {
            return XboxCpu.RawFromHandle(cpuPointer).JitMmxRead(index);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void MmxWrite(IntPtr cpuPointer, int index, ulong value)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitMmxWrite(index, value);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuArithReg(IntPtr cpuPointer, uint descriptor, int sourceIndex)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuArithReg(descriptor, sourceIndex);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuArithPop(IntPtr cpuPointer, uint descriptor)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuArithPop(descriptor);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuArithMem(IntPtr cpuPointer, uint descriptor, ulong bits)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuArithMem(descriptor, bits);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuExchange(IntPtr cpuPointer, int index)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuExchange(index);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuPushOne(IntPtr cpuPointer)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuPushOne();
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuChangeSign(IntPtr cpuPointer)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuChangeSign();
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuCompareReg(IntPtr cpuPointer, uint descriptor, int index)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuCompareReg(descriptor, index);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void FpuCompareMem(IntPtr cpuPointer, uint descriptor, ulong bits)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitFpuCompareMem(descriptor, bits);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void SseArithReg(IntPtr cpuPointer, uint descriptor, int sourceIndex)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitSseArithReg(descriptor, sourceIndex);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void SseArithMem(IntPtr cpuPointer, uint descriptor, uint bits)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitSseArithMem(descriptor, bits);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void SseUnaryReg(IntPtr cpuPointer, uint descriptor, int sourceIndex)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitSseUnaryReg(descriptor, sourceIndex);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void SseMovssReg(IntPtr cpuPointer, int destination, int sourceIndex)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitSseMovssReg(destination, sourceIndex);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static void SseMovssMem(IntPtr cpuPointer, int destination, uint bits)
    {
        try
        {
            XboxCpu.RawFromHandle(cpuPointer).JitSseMovssMem(destination, bits);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
        }
    }

    [UnmanagedCallersOnly]
    private static uint SseMovssStore(IntPtr cpuPointer, int sourceIndex)
    {
        try
        {
            return XboxCpu.RawFromHandle(cpuPointer).JitSseMovssStore(sourceIndex);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static int SseCvt(IntPtr cpuPointer, uint bits)
    {
        try
        {
            return XboxCpu.RawFromHandle(cpuPointer).JitSseCvt(bits);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static int SseCvtReg(IntPtr cpuPointer, int sourceIndex)
    {
        try
        {
            return XboxCpu.RawFromHandle(cpuPointer).JitSseCvtReg(sourceIndex);
        }
        catch (Exception ex)
        {
            Capture(cpuPointer, ex);
            return 0;
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
