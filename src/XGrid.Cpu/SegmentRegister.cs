namespace XGrid.Cpu;

/// <summary>Segment registers.</summary>
public enum SegmentRegister
{
    /// <summary>Extra segment.</summary>
    Es = 0,
    /// <summary>Code segment.</summary>
    Cs = 1,
    /// <summary>Stack segment.</summary>
    Ss = 2,
    /// <summary>Data segment.</summary>
    Ds = 3,
    /// <summary>General-purpose FS segment.</summary>
    Fs = 4,
    /// <summary>General-purpose GS segment.</summary>
    Gs = 5,
}
