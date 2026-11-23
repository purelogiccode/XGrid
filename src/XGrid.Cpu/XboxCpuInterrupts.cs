using XGrid.Core.Devices;

namespace XGrid.Cpu;

/// <summary>
/// Interrupt polling: the CPU asks the controller for a vector once per
/// instruction in the interpreter paths (<c>Step</c>, the spec-block runner)
/// and once per block in the JIT paths. A <see cref="XGrid.Core.Devices.IInterruptSource"/>
/// that exposes a pinned 0/1 pending word lets that poll become a single load:
/// the controller maintains the word on every state change (line raise, mask
/// write, EOI), so a zero reading means <c>Acknowledge</c> would return -1 and
/// the call - which mutates the controller - has nothing to do.
/// </summary>
public sealed unsafe partial class XboxCpu
{
    private IInterruptSource? _pollSource;
    private uint* _pollPendingWord;

    /// <summary>
    /// Acknowledges a pending interrupt, or returns -1. Uses the controller's
    /// pinned pending word when it has one, so the common no-interrupt case
    /// costs a load instead of a virtual call into the device model.
    /// </summary>
    private int PollInterrupt()
    {
        IInterruptSource? source = InterruptSource;
        if (source is null)
        {
            return -1;
        }

        if (!ReferenceEquals(source, _pollSource))
        {
            _pollSource = source;
            _pollPendingWord = (uint*)source.PendingFlagAddress;
        }

        if (_pollPendingWord is not null && *_pollPendingWord == 0)
        {
            return -1;
        }

        return source.Acknowledge();
    }
}
