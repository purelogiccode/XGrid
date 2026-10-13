namespace XGrid.Cpu;

/// <summary>
/// Optional device-side read-effect model the idle-skipper consults for
/// non-RAM reads in a candidate body (research-notes §119). Contract: a
/// classified register read must be side-effect free (no write-on-clear/FIFO
/// pop) and its value must change only as device time advances (via
/// <see cref="IIdleSkipHost.AdvanceIdle"/>); <see cref="TryGetReadEffect"/> must
/// be a pure query of that state. Registers that do not meet the contract must
/// return false so the conservative V1 rejection applies.
/// </summary>
public interface IIdleSkipReadModel
{
    /// <summary>
    /// Classifies the register at the physical address: false = unknown /
    /// unsafe (skip rejected); true = side-effect-free with the supplied
    /// <paramref name="effect"/> (stable forever or a fresh change deadline).
    /// </summary>
    bool TryGetReadEffect(uint physicalAddress, out IdleSkipReadEffect effect);
}
