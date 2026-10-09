using System;

namespace XGrid.Cpu;

/// <summary>x87 floating-point register file and status/control state (plan section 8.1).</summary>
public sealed class FpuState
{
    /// <summary>Default control word after FNINIT.</summary>
    public const ushort DefaultControlWord = 0x037F;

    /// <summary>Tag value for a valid (normal) register.</summary>
    public const int TagValid = 0;

    /// <summary>Tag value for a zero register.</summary>
    public const int TagZero = 1;

    /// <summary>Tag value for a special (NaN/infinity/denormal/unnormal) register.</summary>
    public const int TagSpecial = 2;

    /// <summary>Tag value for an empty register.</summary>
    public const int TagEmpty = 3;

    private readonly Fpu80[] _registers = new Fpu80[8];

    /// <summary>Gets or sets the control word.</summary>
    public ushort ControlWord { get; set; } = DefaultControlWord;

    /// <summary>Gets or sets the status word.</summary>
    public ushort StatusWord { get; set; }

    /// <summary>Gets or sets the tag word.</summary>
    public ushort TagWord { get; set; } = 0xFFFF;

    /// <summary>Gets or sets the stack top pointer (status word bits 11-13).</summary>
    public int Top
    {
        get => (StatusWord >> 11) & 7;
        set => StatusWord = (ushort)((StatusWord & ~0x3800) | ((value & 7) << 11));
    }

    /// <summary>Gets the rounding mode from the control word (0 nearest, 1 down, 2 up, 3 truncate).</summary>
    public int RoundingMode => (ControlWord >> 10) & 3;

    /// <summary>Gets the precision-control field from the control word.</summary>
    public int PrecisionControl => (ControlWord >> 8) & 3;

    /// <summary>Returns the register at stack position ST(index).</summary>
    public Fpu80 GetRegister(int index)
    {
        return _registers[(Top + index) & 7];
    }

    /// <summary>Writes the register at stack position ST(index).</summary>
    public void SetRegister(int index, Fpu80 value)
    {
        int physical = (Top + index) & 7;
        _registers[physical] = value;
        SetTag(physical, ClassifyTag(value));
    }

    /// <summary>Pushes a value onto the register stack.</summary>
    public void Push(Fpu80 value)
    {
        Top = (Top - 1) & 7;
        _registers[Top] = value;
        SetTag(Top, ClassifyTag(value));
    }

    /// <summary>Pops the top register and marks it empty.</summary>
    public Fpu80 Pop()
    {
        Fpu80 value = _registers[Top];
        SetTag(Top, TagEmpty);
        Top = (Top + 1) & 7;
        return value;
    }

    /// <summary>Marks ST(index) empty without changing the stack pointer.</summary>
    public void Free(int index)
    {
        SetTag((Top + index) & 7, TagEmpty);
    }

    /// <summary>Exchanges ST(0) and ST(index).</summary>
    public void Exchange(int index)
    {
        int physical = (Top + index) & 7;
        (_registers[Top], _registers[physical]) = (_registers[physical], _registers[Top]);

        int topTag = GetTag(Top);
        SetTag(Top, GetTag(physical));
        SetTag(physical, topTag);
    }

    /// <summary>Returns the CPU to its initial floating-point state.</summary>
    public void Init()
    {
        ControlWord = DefaultControlWord;
        StatusWord = 0;
        TagWord = 0xFFFF;
        Array.Clear(_registers);
    }

    /// <summary>Clears all exception flags (FNCLEX).</summary>
    public void ClearExceptions()
    {
        StatusWord &= 0x7F00;
    }

    /// <summary>Sets an exception flag (bit 0 IE, 1 DE, 2 ZE, 3 OE, 4 UE, 5 PE, 6 SF).</summary>
    public void SetException(int bit)
    {
        StatusWord |= (ushort)(1 << bit);
    }

    /// <summary>Gets whether an unmasked exception is pending (would trigger #MF on FWAIT).</summary>
    public bool HasUnmaskedException
    {
        get
        {
            ushort exceptions = (ushort)(StatusWord & 0x3F);
            ushort unmasked = (ushort)(~ControlWord & 0x3F);
            return (exceptions & unmasked) != 0;
        }
    }

    /// <summary>Sets the condition-code flags C3/C2/C0.</summary>
    public void SetConditionCodes(bool c3, bool c2, bool c0)
    {
        const ushort mask = 0x4500;
        ushort value = 0;
        if (c3)
        {
            value |= 0x4000;
        }

        if (c2)
        {
            value |= 0x0400;
        }

        if (c0)
        {
            value |= 0x0100;
        }

        StatusWord = (ushort)((StatusWord & ~mask) | value);
    }

    /// <summary>Sets the C1 condition flag (bit 9).</summary>
    public void SetC1(bool value)
    {
        if (value)
        {
            StatusWord |= 0x0200;
        }
        else
        {
            StatusWord &= unchecked((ushort)~0x0200);
        }
    }

    /// <summary>Sets the C0 condition flag (bit 8).</summary>
    public void SetC0(bool value)
    {
        if (value)
        {
            StatusWord |= 0x0100;
        }
        else
        {
            StatusWord &= unchecked((ushort)~0x0100);
        }
    }

    /// <summary>Writes the raw register at physical index (used by state restore).</summary>
    public void SetPhysicalRegister(int physicalIndex, Fpu80 value)
    {
        _registers[physicalIndex & 7] = value;
    }

    /// <summary>Writes an MMX register (aliases the low 64 bits of ST(index)) and tags it valid.</summary>
    public void SetMmxRegister(int index, ulong value)
    {
        int physical = (Top + index) & 7;
        _registers[physical] = new Fpu80(0x403E, value);
        SetTag(physical, TagValid);
    }

    /// <summary>Reads the low 64 bits of ST(index) for MMX use.</summary>
    public ulong GetMmxRegister(int index)
    {
        return _registers[(Top + index) & 7].Significand;
    }

    /// <summary>Marks every register empty (EMMS).</summary>
    public void EmptyAll()
    {
        TagWord = 0xFFFF;
    }

    /// <summary>Reads the raw register at physical index.</summary>
    public Fpu80 GetPhysicalRegister(int physicalIndex)
    {
        return _registers[physicalIndex & 7];
    }

    private int GetTag(int physicalIndex)
    {
        return (TagWord >> (physicalIndex * 2)) & 3;
    }

    private void SetTag(int physicalIndex, int tag)
    {
        int shift = (physicalIndex & 7) * 2;
        TagWord = (ushort)((TagWord & ~(3 << shift)) | ((tag & 3) << shift));
    }

    private static int ClassifyTag(Fpu80 value)
    {
        if (value.IsZero)
        {
            return TagZero;
        }

        if (!value.IsFinite || value.Exponent == 0 || value.IsUnnormal)
        {
            return TagSpecial;
        }

        return TagValid;
    }
}
