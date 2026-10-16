using System;
using System.Collections.Generic;
using XGrid.Cpu;
using XGrid.Host;
using Xunit;

namespace XGrid.Integration.Tests;

public sealed class SevenEosQuirkTests
{
    private const uint Title007 = 0x4541000Du;
    private const uint StrmMagic = 0x4D525453u;
    private const uint StrmVa = 0x80B6C800u;
    private const uint Mpch = 0x6843504Du;
    private const uint Consumed = 0xFFFFFFFEu;

    [Fact]
    public void IsParked_ExactSignature_ReturnsTrue()
    {
        Assert.True(SevenEosQuirk.IsParked(
            Title007, StrmMagic, StrmVa, 0, 2, true, Mpch, true, Consumed));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x4541000Eu)]
    [InlineData(0xFFFFFFFFu)]
    public void IsParked_WrongTitle_ReturnsFalse(uint titleId)
    {
        Assert.False(SevenEosQuirk.IsParked(
            titleId, StrmMagic, StrmVa, 0, 2, true, Mpch, true, Consumed));
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(0x11E74u)]
    public void IsParked_NonZeroRemaining_ReturnsFalse(uint remaining)
    {
        Assert.False(SevenEosQuirk.IsParked(
            Title007, StrmMagic, StrmVa, remaining, 2, true, Mpch, true, Consumed));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    public void IsParked_NonTwoState_ReturnsFalse(uint state)
    {
        Assert.False(SevenEosQuirk.IsParked(
            Title007, StrmMagic, StrmVa, 0, state, true, Mpch, true, Consumed));
    }

    [Fact]
    public void IsParked_StagedMissing_ReturnsFalse()
    {
        Assert.False(SevenEosQuirk.IsParked(
            Title007, StrmMagic, StrmVa, 0, 2, false, 0, true, Consumed));
    }

    [Fact]
    public void IsParked_StagedNotMpch_ReturnsFalse()
    {
        Assert.False(SevenEosQuirk.IsParked(
            Title007, StrmMagic, StrmVa, 0, 2, true, Consumed, true, Consumed));
    }

    [Fact]
    public void IsParked_CurrentLinkedMpch_ReturnsFalse()
    {
        // Queue already delivers an MPCh chunk: not the parked unlinked case.
        Assert.False(SevenEosQuirk.IsParked(
            Title007, StrmMagic, StrmVa, 0, 2, true, Mpch, true, Mpch));
    }

    [Fact]
    public void IsParked_CurrentMissing_ReturnsFalse()
    {
        Assert.False(SevenEosQuirk.IsParked(
            Title007, StrmMagic, StrmVa, 0, 2, true, Mpch, false, 0));
    }

    [Fact]
    public void IsParked_WrongMagicOrHandle_ReturnsFalse()
    {
        Assert.False(SevenEosQuirk.IsParked(
            Title007, 0u, StrmVa, 0, 2, true, Mpch, true, Consumed));
        Assert.False(SevenEosQuirk.IsParked(
            Title007, StrmMagic, 0x80B6C801u, 0, 2, true, Mpch, true, Consumed));
    }

    [Fact]
    public void TryApply_ExactSignature_WritesStateZero()
    {
        var memory = ParkedMemory();

        uint writtenVa = 0;
        uint writtenValue = 0xFFFFFFFFu;

        Assert.True(SevenEosQuirk.TryApply(Read, Write));
        Assert.Equal(StrmVa + 0x44u, writtenVa);
        Assert.Equal(0u, writtenValue);
        return;

        uint? Read(uint va)
        {
            return memory.TryGetValue(va, out uint value) ? value : null;
        }

        void Write(uint va, uint value)
        {
            writtenVa = va;
            writtenValue = value;
        }
    }

    [Fact]
    public void TryApply_WrongTitle_DoesNotWrite()
    {
        var memory = ParkedMemory();
        memory[0x10180u] = 0u;
        bool wrote = false;

        Assert.False(SevenEosQuirk.TryApply(Read, (va, value) => wrote = true));
        Assert.False(wrote);
        return;

        uint? Read(uint va)
        {
            return memory.TryGetValue(va, out uint value) ? value : null;
        }
    }

    [Fact]
    public void TryApply_UnmappedTitle_DoesNotFire()
    {
        var memory = ParkedMemory();
        memory.Remove(0x10118u);
        bool wrote = false;

        Assert.False(SevenEosQuirk.TryApply(Read, (va, value) => wrote = true));
        Assert.False(wrote);
        return;

        uint? Read(uint va)
        {
            return memory.TryGetValue(va, out uint value) ? value : null;
        }
    }

    [Fact]
    public void Machine_DisabledQuirk_NeverFires()
    {
        var machine = new XboxMachine();
        machine.SevenEosQuirkEnabled = false;

        Assert.False(machine.CheckSevenEosQuirk());
        Assert.Equal(0uL, machine.SevenEosQuirkFires);
        Assert.Equal(0uL, machine.SevenEosQuirkChecks);
    }

    [Fact]
    public void Machine_PagingDisabled_NeverFires()
    {
        var machine = new XboxMachine();
        Assert.True(machine.SevenEosQuirkEnabled);

        Assert.False(machine.CheckSevenEosQuirk());
        Assert.Equal(0uL, machine.SevenEosQuirkFires);
    }

    [Fact]
    public void Machine_ParkedSignature_FiresAfterStabilityDelay()
    {
        var machine = new XboxMachine();
        SetupParkedMachine(machine);
        machine.Cpu.Context.Tsc = 100_000_000uL;

        // First observation arms the stability tracker but does not fire.
        Assert.False(machine.CheckSevenEosQuirk());
        Assert.Equal(0uL, machine.SevenEosQuirkFires);

        // Still within the stability window: no fire.
        machine.Cpu.Context.Tsc += SevenEosQuirk.StabilityThresholdSteps - 1;
        Assert.False(machine.CheckSevenEosQuirk());
        Assert.Equal(0uL, machine.SevenEosQuirkFires);

        // Past the threshold: fires exactly once and clears state to 0.
        machine.Cpu.Context.Tsc += 1;
        Assert.True(machine.CheckSevenEosQuirk());
        Assert.Equal(1uL, machine.SevenEosQuirkFires);
        Assert.Equal(0u, ReadVa(machine, StrmVa + 0x44u));

        // Signature no longer holds (state 0): no refire.
        machine.Cpu.Context.Tsc += SevenEosQuirk.StabilityThresholdSteps + 1;
        Assert.False(machine.CheckSevenEosQuirk());
        Assert.Equal(1uL, machine.SevenEosQuirkFires);
    }

    [Fact]
    public void Machine_WrongTitle_NeverFires()
    {
        var machine = new XboxMachine();
        SetupParkedMachine(machine);
        WriteVa(machine, 0x10180u, 0u);
        machine.Cpu.Context.Tsc = 200_000_000uL;

        for (int i = 0; i < 4; i++)
        {
            machine.Cpu.Context.Tsc += SevenEosQuirk.StabilityThresholdSteps;
            Assert.False(machine.CheckSevenEosQuirk());
        }

        Assert.Equal(0uL, machine.SevenEosQuirkFires);
    }

    [Fact]
    public void Machine_TransientPark_ResetsStability()
    {
        var machine = new XboxMachine();
        SetupParkedMachine(machine);
        machine.Cpu.Context.Tsc = 300_000_000uL;

        Assert.False(machine.CheckSevenEosQuirk());

        // Signature breaks before the threshold (transient starve resolves).
        WriteVa(machine, StrmVa + 0x44u, 1u);
        machine.Cpu.Context.Tsc += SevenEosQuirk.StabilityThresholdSteps;
        Assert.False(machine.CheckSevenEosQuirk());

        // Re-arms but needs a full fresh stability window.
        WriteVa(machine, StrmVa + 0x44u, 2u);
        machine.Cpu.Context.Tsc += 1;
        Assert.False(machine.CheckSevenEosQuirk());
        Assert.Equal(0uL, machine.SevenEosQuirkFires);
    }

    private static Dictionary<uint, uint> ParkedMemory()
    {
        return new Dictionary<uint, uint>
        {
            [0x10118u] = 0x10178u,
            [0x10180u] = Title007,
            [StrmVa] = StrmMagic,
            [0x80B6CBE8u] = StrmVa,
            [0x80B6CBE8u + 0x08u] = 0u,
            [StrmVa + 0x44u] = 2u,
            [StrmVa + 0x60u] = 0x80B6FD30u,
            [0x80B6FD30u] = Mpch,
            [0x80B6CBE8u + 0x0Cu] = 0x80B6CC48u,
            [0x80B6CC48u] = Consumed,
        };
    }

    private const uint PageDirectory = 0xF000u;
    private const uint PageTableLow = 0x2000u;
    private const uint PageTableHigh = 0x3000u;

    private static void Map4K(XboxMachine machine, uint virtualAddress, uint physicalAddress)
    {
        uint pdeAddress = PageDirectory + ((virtualAddress >> 22) & 0x3FFu) * 4u;
        uint tableBase = (virtualAddress >> 22) == (0x80B6C800u >> 22) || (virtualAddress >> 22) == (0x80B6FD30u >> 22)
            ? PageTableHigh
            : PageTableLow;
        // Both high VAs share one directory entry; low VAs share another.
        if ((virtualAddress >> 22) == 0u)
        {
            tableBase = PageTableLow;
        }

        machine.Memory.Write32(pdeAddress, (tableBase & 0xFFFFF000u) | 3u);
        uint pteAddress = tableBase + ((virtualAddress >> 12) & 0x3FFu) * 4u;
        machine.Memory.Write32(pteAddress, (physicalAddress & 0xFFFFF000u) | 3u);
    }

    private static void SetupParkedMachine(XboxMachine machine)
    {
        machine.Cpu.Context.Cr0 = ControlRegisterBits.Cr0ProtectedMode | ControlRegisterBits.Cr0Paging;
        machine.Cpu.Context.Cr3 = PageDirectory;
        machine.Cpu.Mmu.Flush();

        // Low page: XBE header + cert + TitleId.
        Map4K(machine, 0x10118u, 0x10000u);
        // High page holding STRM + handle + current entry.
        Map4K(machine, StrmVa, 0x20000u);
        // High page holding the staged MPCh buffer.
        Map4K(machine, 0x80B6FD30u, 0x22000u);
        machine.Cpu.Mmu.Flush();

        WriteVa(machine, 0x10118u, 0x10178u);
        WriteVa(machine, 0x10180u, Title007);
        WriteVa(machine, StrmVa, StrmMagic);
        WriteVa(machine, 0x80B6CBE8u, StrmVa);
        WriteVa(machine, 0x80B6CBE8u + 0x08u, 0u);
        WriteVa(machine, StrmVa + 0x44u, 2u);
        WriteVa(machine, StrmVa + 0x60u, 0x80B6FD30u);
        WriteVa(machine, 0x80B6FD30u, Mpch);
        WriteVa(machine, 0x80B6CBE8u + 0x0Cu, 0x80B6CC48u);
        WriteVa(machine, 0x80B6CC48u, Consumed);
    }

    private static uint ReadVa(XboxMachine machine, uint virtualAddress)
    {
        uint physical = machine.Cpu.Mmu.Translate(
            virtualAddress, isWrite: false, isInstruction: false,
            out bool faulted, out _);
        if (faulted)
        {
            throw new InvalidOperationException($"VA 0x{virtualAddress:X8} unmapped in test setup.");
        }

        return machine.Memory.Read32(physical);
    }

    private static void WriteVa(XboxMachine machine, uint virtualAddress, uint value)
    {
        uint physical = machine.Cpu.Mmu.Translate(
            virtualAddress, isWrite: true, isInstruction: false,
            out bool faulted, out _);
        if (faulted)
        {
            throw new InvalidOperationException($"VA 0x{virtualAddress:X8} unmapped in test setup.");
        }

        machine.Memory.Write32(physical, value);
    }
}
