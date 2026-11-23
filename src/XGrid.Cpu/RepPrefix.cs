namespace XGrid.Cpu;

/// <summary>REP-family prefix recorded on string instructions.</summary>
public enum RepPrefix
{
    /// <summary>No repeat prefix.</summary>
    None = 0,

    /// <summary>F3 — REP/REPE.</summary>
    Rep = 1,

    /// <summary>F2 — REPNE.</summary>
    Repne = 2,
}
