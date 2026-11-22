using Xunit;

namespace XGrid.Cpu.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> for the raw-emitter differentials. They need
/// <see cref="RawCodeMemory.IsSupported"/> - the emitter reserves executable
/// memory through <c>kernel32.VirtualAlloc</c>, so it only runs on Windows x64.
/// On any other host the IL engine is the contract and the raw path is an
/// optional accelerator, so the test is reported as skipped instead of failing
/// on "no raw blocks were built".
/// </summary>
public sealed class RawJitFactAttribute : FactAttribute
{
    public RawJitFactAttribute()
    {
        if (!RawCodeMemory.IsSupported)
        {
            Skip = "the raw emitter needs Windows x64 (executable memory via kernel32)";
        }
    }
}
