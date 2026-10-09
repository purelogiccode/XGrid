namespace XGrid.Core.Devices;

/// <summary>External interrupt controller acknowledged by the CPU when interrupts are enabled.</summary>
public interface IInterruptSource
{
    /// <summary>Returns the interrupt vector to dispatch, or -1 when no interrupt is pending.</summary>
    int Acknowledge();
}
