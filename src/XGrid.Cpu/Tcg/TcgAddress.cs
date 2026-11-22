using System;

namespace XGrid.Cpu.Tcg;

/// <summary>The x87/SSE effective-address helper shared by the IR helpers.</summary>
internal static class TcgAddress
{
    /// <summary>Computes the linear address of a memory operand.</summary>
    /// <param name="operand">The operand (must be memory).</param>
    /// <param name="gprs">The eight guest GPRs.</param>
    /// <param name="segmentBases">The six segment bases in <see cref="SegmentRegister"/> order.</param>
    /// <returns>The linear address.</returns>
    public static uint Of(Operand operand, ReadOnlySpan<uint> gprs, ReadOnlySpan<uint> segmentBases)
    {
        if (operand.Kind != OperandKind.Memory)
        {
            throw new InvalidOperationException("the operand is not memory");
        }

        MemoryOperand memory = operand.Memory;
        uint address = memory.Displacement;
        if (memory.HasBase)
        {
            uint value = gprs[(int)memory.Base];
            address += memory.Is16Bit ? value & 0xFFFFu : value;
        }

        if (memory.HasIndex)
        {
            uint value = gprs[(int)memory.Index];
            address += (memory.Is16Bit ? value & 0xFFFFu : value) * (uint)memory.Scale;
        }

        if (memory.Is16Bit)
        {
            address &= 0xFFFFu;
        }

        SegmentRegister segment = memory.SegmentOverride ?? DefaultSegment(memory);
        if ((int)segment < segmentBases.Length)
        {
            address += segmentBases[(int)segment];
        }

        return address;
    }

    private static SegmentRegister DefaultSegment(in MemoryOperand memory)
    {
        if (memory.Is16Bit)
        {
            return memory.HasBase && memory.Base == Gpr.Ebp ? SegmentRegister.Ss : SegmentRegister.Ds;
        }

        return memory.HasBase && (memory.Base == Gpr.Esp || memory.Base == Gpr.Ebp)
            ? SegmentRegister.Ss
            : SegmentRegister.Ds;
    }
}
