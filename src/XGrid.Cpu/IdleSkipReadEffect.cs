using System;
using System.Runtime.InteropServices;

namespace XGrid.Cpu;

/// <summary>
/// Device-supplied read-effect classification for one physical-address read in
/// an idle-skip candidate body (research-notes §119). <see cref="Stable"/> means
/// the register's value never changes, so the read may span the whole skip;
/// otherwise <see cref="StepsUntilChange"/> is the number of device-advance
/// cycles until the value's next change from now, and the skipper clamps every
/// chunk so the skip lands exactly at (never past) that step.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct IdleSkipReadEffect
{
    private IdleSkipReadEffect(bool isStable, long stepsUntilChange)
    {
        IsStable = isStable;
        StepsUntilChange = stepsUntilChange;
    }

    /// <summary>Gets a classification for a value that never changes.</summary>
    public static IdleSkipReadEffect Stable => new(isStable: true, stepsUntilChange: 0);

    /// <summary>Gets a classification for a value that changes in the given number of cycles.</summary>
    public static IdleSkipReadEffect ChangeIn(long steps)
    {
        return new IdleSkipReadEffect(isStable: false, stepsUntilChange: Math.Max(0, steps));
    }

    /// <summary>Gets whether the register's value is provably constant (spans the skip).</summary>
    public bool IsStable { get; }

    /// <summary>Gets the cycles until the value's next change (only when not <see cref="IsStable"/>).</summary>
    public long StepsUntilChange { get; }
}
