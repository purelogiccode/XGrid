namespace XGrid.Cpu;

/// <summary>General-purpose registers (32-bit views).</summary>
public enum Gpr
{
    /// <summary>Accumulator register.</summary>
    Eax = 0,

    /// <summary>Count register.</summary>
    Ecx = 1,

    /// <summary>Data register.</summary>
    Edx = 2,

    /// <summary>Base register.</summary>
    Ebx = 3,

    /// <summary>Stack pointer.</summary>
    Esp = 4,

    /// <summary>Base (frame) pointer.</summary>
    Ebp = 5,

    /// <summary>Source index.</summary>
    Esi = 6,

    /// <summary>Destination index.</summary>
    Edi = 7,
}
