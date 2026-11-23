namespace XGrid.Core.Devices;

/// <summary>External interrupt controller acknowledged by the CPU when interrupts are enabled.</summary>
public interface IInterruptSource
{
    /// <summary>Returns the interrupt vector to dispatch, or -1 when no interrupt is pending.</summary>
    int Acknowledge();

    /// <summary>
    /// Gets the address of a pinned 0/1 word that is non-zero when
    /// <see cref="Acknowledge"/> would return a vector, or 0 when the source
    /// cannot expose a cheap flag. Emitted fast-mode code polls this word at a
    /// block boundary instead of making a managed call; a false positive is
    /// harmless (the caller falls back to <see cref="Acknowledge"/>), a false
    /// negative would delay a delivery and is therefore never cached.
    /// </summary>
    nint PendingFlagAddress => 0;
}
