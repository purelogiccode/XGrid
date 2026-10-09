using System;

namespace XGrid.Cpu;

/// <summary>Architectural CPU state (plan section 8.1).</summary>
public sealed class XboxContext
{
    private readonly uint[] _registers = new uint[8];
    private readonly SegmentState[] _segments = new SegmentState[6];
    private readonly uint[] _debugRegisters = new uint[8];
    private readonly uint[] _xmm = new uint[32];

    /// <summary>Gets or sets the instruction pointer.</summary>
    public uint Eip { get; set; }

    /// <summary>Gets or sets the flags register.</summary>
    public uint Eflags { get; set; } = FlagBits.Reserved1;

    /// <summary>Gets or sets CR0.</summary>
    public uint Cr0 { get; set; }

    /// <summary>Gets or sets CR2.</summary>
    public uint Cr2 { get; set; }

    /// <summary>Gets or sets CR3.</summary>
    public uint Cr3 { get; set; }

    /// <summary>Gets or sets CR4.</summary>
    public uint Cr4 { get; set; }

    /// <summary>Gets or sets whether the CPU is halted.</summary>
    public bool Halted { get; set; }

    /// <summary>Gets or sets whether the current code segment defaults to 32-bit (CS.D).</summary>
    public bool CodeSegment32 { get; set; }

    /// <summary>Gets or sets the time-stamp counter.</summary>
    public ulong Tsc { get; set; }

    /// <summary>Gets the x87/MMX floating-point state.</summary>
    public FpuState Fpu { get; } = new();

    /// <summary>Gets or sets the SSE control/status register.</summary>
    public uint Mxcsr { get; set; } = 0x1F80;

    /// <summary>Gets the global descriptor table register.</summary>
    public DescriptorTableRegister Gdtr { get; } = new();

    /// <summary>Gets the interrupt descriptor table register.</summary>
    public DescriptorTableRegister Idtr { get; } = new();

    /// <summary>Gets or sets the task register selector.</summary>
    public ushort Tr { get; set; }

    /// <summary>Gets or sets the local descriptor table register selector.</summary>
    public ushort Ldtr { get; set; }

    /// <summary>Gets whether protected mode is enabled (CR0.PE).</summary>
    public bool IsProtectedMode => (Cr0 & 1u) != 0;

    /// <summary>Gets or sets EAX.</summary>
    public uint Eax
    {
        get => ReadRegister(Gpr.Eax);
        set => WriteRegister(Gpr.Eax, value);
    }

    /// <summary>Gets or sets ECX.</summary>
    public uint Ecx
    {
        get => ReadRegister(Gpr.Ecx);
        set => WriteRegister(Gpr.Ecx, value);
    }

    /// <summary>Gets or sets EDX.</summary>
    public uint Edx
    {
        get => ReadRegister(Gpr.Edx);
        set => WriteRegister(Gpr.Edx, value);
    }

    /// <summary>Gets or sets EBX.</summary>
    public uint Ebx
    {
        get => ReadRegister(Gpr.Ebx);
        set => WriteRegister(Gpr.Ebx, value);
    }

    /// <summary>Gets or sets ESP.</summary>
    public uint Esp
    {
        get => ReadRegister(Gpr.Esp);
        set => WriteRegister(Gpr.Esp, value);
    }

    /// <summary>Gets or sets EBP.</summary>
    public uint Ebp
    {
        get => ReadRegister(Gpr.Ebp);
        set => WriteRegister(Gpr.Ebp, value);
    }

    /// <summary>Gets or sets ESI.</summary>
    public uint Esi
    {
        get => ReadRegister(Gpr.Esi);
        set => WriteRegister(Gpr.Esi, value);
    }

    /// <summary>Gets or sets EDI.</summary>
    public uint Edi
    {
        get => ReadRegister(Gpr.Edi);
        set => WriteRegister(Gpr.Edi, value);
    }

    /// <summary>Gets the default operand size (16-bit in real mode, CS.D-driven in protected mode).</summary>
    public int DefaultOperandSize => IsProtectedMode && CodeSegment32 ? 32 : 16;

    /// <summary>Gets the default address size (16-bit in real mode, CS.D-driven in protected mode).</summary>
    public int DefaultAddressSize => IsProtectedMode && CodeSegment32 ? 32 : 16;

    /// <summary>Returns the CPU to its power-on state (reset vector at 0xFFFFFFF0).</summary>
    public void Reset()
    {
        Array.Clear(_registers);
        Array.Clear(_debugRegisters);
        Array.Clear(_segments);
        Eip = 0xFFF0;
        Eflags = FlagBits.Reserved1;
        Cr0 = 0;
        Cr2 = 0;
        Cr3 = 0;
        Cr4 = 0;
        Halted = false;
        CodeSegment32 = false;
        Tsc = 0;
        Mxcsr = 0x1F80;
        Fpu.Init();
        Array.Clear(_xmm);

        SetSegment(SegmentRegister.Cs, 0xF000, 0xFFFF0000);
    }

    /// <summary>Reads a 32-bit element of an XMM register (element 0 is the low dword).</summary>
    public uint ReadXmmElement(int register, int element)
    {
        return _xmm[((register & 7) * 4) + element];
    }

    /// <summary>Writes a 32-bit element of an XMM register.</summary>
    public void WriteXmmElement(int register, int element, uint value)
    {
        _xmm[((register & 7) * 4) + element] = value;
    }

    /// <summary>Reads the low or high 64-bit half of an XMM register.</summary>
    public ulong ReadXmm64(int register, int half)
    {
        int index = ((register & 7) * 4) + (half * 2);
        return _xmm[index] | ((ulong)_xmm[index + 1] << 32);
    }

    /// <summary>Writes the low or high 64-bit half of an XMM register.</summary>
    public void WriteXmm64(int register, int half, ulong value)
    {
        int index = ((register & 7) * 4) + (half * 2);
        _xmm[index] = (uint)value;
        _xmm[index + 1] = (uint)(value >> 32);
    }

    /// <summary>Reads a 128-bit XMM register as two 64-bit halves.</summary>
    public (ulong Low, ulong High) ReadXmm128(int register)
    {
        return (ReadXmm64(register, 0), ReadXmm64(register, 1));
    }

    /// <summary>Writes a 128-bit XMM register as two 64-bit halves.</summary>
    public void WriteXmm128(int register, ulong low, ulong high)
    {
        WriteXmm64(register, 0, low);
        WriteXmm64(register, 1, high);
    }

    /// <summary>Reads an MMX register (low 64 bits of the aliased x87 register).</summary>
    public ulong ReadMmx(int index)
    {
        return Fpu.GetMmxRegister(index);
    }

    /// <summary>Writes an MMX register (aliases the low 64 bits of the x87 register).</summary>
    public void WriteMmx(int index, ulong value)
    {
        Fpu.SetMmxRegister(index, value);
    }

    /// <summary>Reads a 32-bit general-purpose register.</summary>
    public uint ReadRegister(Gpr register)
    {
        return _registers[(int)register];
    }

    /// <summary>Writes a 32-bit general-purpose register.</summary>
    public void WriteRegister(Gpr register, uint value)
    {
        _registers[(int)register] = value;
    }

    /// <summary>Reads the low 16 bits of a general-purpose register.</summary>
    public ushort ReadRegister16(Gpr register)
    {
        return (ushort)_registers[(int)register];
    }

    /// <summary>Writes the low 16 bits of a general-purpose register.</summary>
    public void WriteRegister16(Gpr register, ushort value)
    {
        _registers[(int)register] = (_registers[(int)register] & 0xFFFF0000u) | value;
    }

    /// <summary>Reads an 8-bit register by ModRM encoding (0-3 low bytes, 4-7 high bytes).</summary>
    public byte ReadRegister8(int index)
    {
        int register = index & 3;
        uint value = _registers[register];
        return (index & 4) != 0 ? (byte)(value >> 8) : (byte)value;
    }

    /// <summary>Writes an 8-bit register by ModRM encoding (0-3 low bytes, 4-7 high bytes).</summary>
    public void WriteRegister8(int index, byte value)
    {
        int register = index & 3;
        uint current = _registers[register];
        _registers[register] = (index & 4) != 0
            ? (current & 0xFFFF00FFu) | ((uint)value << 8)
            : (current & 0xFFFFFF00u) | value;
    }

    /// <summary>Reads a debug register.</summary>
    public uint ReadDebugRegister(int index)
    {
        return _debugRegisters[index];
    }

    /// <summary>Writes a debug register.</summary>
    public void WriteDebugRegister(int index, uint value)
    {
        _debugRegisters[index] = value;
    }

    /// <summary>Gets a segment selector.</summary>
    public ushort GetSegmentSelector(SegmentRegister segment)
    {
        return _segments[(int)segment].Selector;
    }

    /// <summary>Gets a segment base address.</summary>
    public uint GetSegmentBase(SegmentRegister segment)
    {
        return _segments[(int)segment].Base;
    }

    /// <summary>Sets a segment selector and base.</summary>
    public void SetSegment(SegmentRegister segment, ushort selector, uint baseAddress)
    {
        _segments[(int)segment] = new SegmentState(selector, baseAddress);
    }

    private readonly record struct SegmentState(ushort Selector, uint Base);
}
