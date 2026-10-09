using Xunit;

namespace XGrid.Cpu.Tests;

public sealed class AluOpsTests
{
    [Fact]
    public void ParityEven_IsCorrect()
    {
        Assert.True(AluOps.ParityEven(0x00));
        Assert.False(AluOps.ParityEven(0x01));
        Assert.True(AluOps.ParityEven(0x03));
        Assert.False(AluOps.ParityEven(0x07));
        Assert.True(AluOps.ParityEven(0xFF));
    }

    [Fact]
    public void Add_ComputesCarryAndZero()
    {
        uint flags = AluOps.Add(0xFFFFFFFF, 1, 0, 32, 0, out uint result);

        Assert.Equal(0u, result);
        Assert.True((flags & FlagBits.Cf) != 0);
        Assert.True((flags & FlagBits.Zf) != 0);
        Assert.True((flags & FlagBits.Af) != 0);
        Assert.True((flags & FlagBits.Pf) != 0);
    }

    [Fact]
    public void Add_ComputesSignedOverflow()
    {
        uint flags = AluOps.Add(0x7FFFFFFF, 1, 0, 32, 0, out uint result);

        Assert.Equal(0x80000000u, result);
        Assert.True((flags & FlagBits.Of) != 0);
        Assert.True((flags & FlagBits.Sf) != 0);
        Assert.False((flags & FlagBits.Cf) != 0);
    }

    [Fact]
    public void Add_WithCarry_UsesCarryIn()
    {
        uint flags = AluOps.Add(1, 1, 1, 32, 0, out uint result);

        Assert.Equal(3u, result);
        Assert.False((flags & FlagBits.Cf) != 0);
    }

    [Fact]
    public void Sub_ComputesBorrow()
    {
        uint flags = AluOps.Sub(0, 1, 0, 32, 0, out uint result);

        Assert.Equal(0xFFFFFFFFu, result);
        Assert.True((flags & FlagBits.Cf) != 0);
        Assert.True((flags & FlagBits.Sf) != 0);
        Assert.True((flags & FlagBits.Af) != 0);
    }

    [Fact]
    public void Sub_ComputesSignedOverflow()
    {
        uint flags = AluOps.Sub(0x80000000, 1, 0, 32, 0, out uint result);

        Assert.Equal(0x7FFFFFFFu, result);
        Assert.True((flags & FlagBits.Of) != 0);
        Assert.False((flags & FlagBits.Sf) != 0);
    }

    [Fact]
    public void Logical_ClearsCarryAndOverflow()
    {
        uint flags = AluOps.Logical(0, 32, FlagBits.Cf | FlagBits.Of | FlagBits.Af);

        Assert.False((flags & FlagBits.Cf) != 0);
        Assert.False((flags & FlagBits.Of) != 0);
        Assert.False((flags & FlagBits.Af) != 0);
        Assert.True((flags & FlagBits.Zf) != 0);
        Assert.True((flags & FlagBits.Pf) != 0);
    }

    [Fact]
    public void Inc_PreservesCarry()
    {
        uint flags = AluOps.Inc(0, 32, FlagBits.Cf, out uint result);

        Assert.Equal(1u, result);
        Assert.True((flags & FlagBits.Cf) != 0);
        Assert.False((flags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void Inc_ComputesOverflow()
    {
        uint flags = AluOps.Inc(0x7FFFFFFF, 32, 0, out uint result);

        Assert.Equal(0x80000000u, result);
        Assert.True((flags & FlagBits.Of) != 0);
    }

    [Fact]
    public void Dec_PreservesCarry()
    {
        uint flags = AluOps.Dec(1, 32, FlagBits.Cf, out uint result);

        Assert.Equal(0u, result);
        Assert.True((flags & FlagBits.Cf) != 0);
        Assert.True((flags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void Neg_SetsCarryForNonZero()
    {
        uint flags = AluOps.Neg(1, 32, 0, out uint result);

        Assert.Equal(0xFFFFFFFFu, result);
        Assert.True((flags & FlagBits.Cf) != 0);
    }

    [Fact]
    public void Neg_Zero_ClearsCarry()
    {
        uint flags = AluOps.Neg(0, 32, 0, out uint result);

        Assert.Equal(0u, result);
        Assert.False((flags & FlagBits.Cf) != 0);
        Assert.True((flags & FlagBits.Zf) != 0);
    }

    [Fact]
    public void EightBit_Add_ComputesCarry()
    {
        uint flags = AluOps.Add(0xFF, 1, 0, 8, 0, out uint result);

        Assert.Equal(0u, result);
        Assert.True((flags & FlagBits.Cf) != 0);
        Assert.True((flags & FlagBits.Zf) != 0);
    }
}
