namespace XGrid.Core.Xemu;

/// <summary>
/// Chihiro card-reader configuration. Each slot names a card image used by
/// the emulated reader; an empty slot makes the core create a blank card the
/// first time a game asks for one.
/// </summary>
public sealed class ChihiroCardReaderSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the cabinet's card readers
    /// answer the game.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets Ghost Squad's player-1 HW210 card file.</summary>
    public string Hw210Slot1 { get; set; } = string.Empty;

    /// <summary>Gets or sets Ghost Squad's player-2 HW210 card file.</summary>
    public string Hw210Slot2 { get; set; } = string.Empty;

    /// <summary>Gets or sets Gundam B.O.S.'s HW210 card file.</summary>
    public string Hw210Gundam { get; set; } = string.Empty;

    /// <summary>Gets or sets Maximum Tune 1's CRP-1231 card file.</summary>
    public string Crp1231Mt1 { get; set; } = string.Empty;

    /// <summary>Gets or sets Maximum Tune 2's CRP-1231 card file.</summary>
    public string Crp1231Mt2 { get; set; } = string.Empty;

    /// <summary>Creates an independent copy of these settings.</summary>
    /// <returns>The copy.</returns>
    public ChihiroCardReaderSettings Clone()
    {
        return (ChihiroCardReaderSettings)MemberwiseClone();
    }

    /// <summary>Trims paths loaded from disk.</summary>
    public void Normalize()
    {
        Hw210Slot1 = (Hw210Slot1 ?? string.Empty).Trim();
        Hw210Slot2 = (Hw210Slot2 ?? string.Empty).Trim();
        Hw210Gundam = (Hw210Gundam ?? string.Empty).Trim();
        Crp1231Mt1 = (Crp1231Mt1 ?? string.Empty).Trim();
        Crp1231Mt2 = (Crp1231Mt2 ?? string.Empty).Trim();
    }
}
