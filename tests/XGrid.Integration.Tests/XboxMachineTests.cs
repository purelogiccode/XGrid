using System;
using XGrid.Bus.Pci;
using XGrid.Host;
using XGrid.Memory;
using Xunit;

namespace XGrid.Integration.Tests;

public sealed class XboxMachineTests
{
    private const uint ResetVector = 0xFFFFFFF0;

    [Fact]
    public void BiosIsVisibleAtFlashWindowAndAlias()
    {
        XboxMachine machine = CreateLoadedMachine();

        Assert.Equal(0x90, machine.Memory.Read8(XboxAddressMap.FlashBase));
        Assert.Equal(0x90, machine.Memory.Read8(XboxAddressMap.FlashBase + 0x100));
        Assert.Equal(0x90, machine.Memory.Read8(XboxAddressMap.FlashMirrorBase));
        Assert.Equal(0x90, machine.Memory.Read8(XboxAddressMap.FlashMirrorBase + 0x100));
    }

    [Fact]
    public void BiosIsMirroredEveryFlashSize()
    {
        var machine = new XboxMachine();
        byte[] bios = FakeBios();
        bios[0] = 0x11;
        bios[0x100] = 0x22;
        machine.LoadBios(bios, FakeMcpxRom());

        Assert.Equal(0x11, machine.Memory.Read8(XboxAddressMap.FlashMirrorBase));
        Assert.Equal(0x11, machine.Memory.Read8(XboxAddressMap.FlashMirrorBase + 0x40000));
        Assert.Equal(0x22, machine.Memory.Read8(XboxAddressMap.FlashMirrorBase + 0x40100));
    }

    [Fact]
    public void FlashWindowIsReadOnly()
    {
        XboxMachine machine = CreateLoadedMachine();

        machine.Memory.Write8(XboxAddressMap.FlashBase, 0x00);

        Assert.Equal(0x90, machine.Memory.Read8(XboxAddressMap.FlashBase));
    }

    [Fact]
    public void McpxOverlayCoversResetVector()
    {
        XboxMachine machine = CreateLoadedMachine();

        Assert.Equal(0xB8, machine.Memory.Read8(ResetVector));
        Assert.True(machine.Bus.RomOverlayEnabled);
    }

    [Fact]
    public void ResetVectorExecutesMcpxCodeInRealMode()
    {
        XboxMachine machine = CreateLoadedMachine();

        machine.Reset();
        int executed = machine.Run(10);

        // The reset vector runs in real mode, so the fake ROM uses 16-bit encoding.
        Assert.Equal(0x5678u, machine.Cpu.Context.Eax & 0xFFFF);
        Assert.True(machine.Cpu.Context.Halted);
        Assert.Equal(2, executed);
    }

    [Fact]
    public void OverlayDisableRevealsBiosAtResetVector()
    {
        XboxMachine machine = CreateLoadedMachine();

        machine.Bus.Pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x80), 1, 2);

        Assert.False(machine.Bus.RomOverlayEnabled);
        Assert.Equal(0x90, machine.Memory.Read8(ResetVector));
    }

    [Fact]
    public void RamMirrorAndAliasMapToRam()
    {
        XboxMachine machine = CreateLoadedMachine();
        machine.Ram.Clear();

        machine.Memory.Write32(0x100, 0xAABBCCDD);

        Assert.Equal(0xAABBCCDDu, machine.Memory.Read32(XboxAddressMap.RamMirrorBase + 0x100));
        Assert.Equal(0xAABBCCDDu, machine.Memory.Read32(XboxMachine.ContiguousAliasBase + 0x100));
    }

    [Fact]
    public void RamMirrorWrapsWithinRam()
    {
        XboxMachine machine = CreateLoadedMachine();

        machine.Memory.Write32(0x0C000000 + 0x200, 0x11223344);

        Assert.Equal(0x11223344u, machine.Ram.Read32(0x200));
    }

    [Fact]
    public void WriteCombinedAliasMirrorsRam()
    {
        XboxMachine machine = CreateLoadedMachine();
        machine.Ram.Clear();

        machine.Memory.Write32(XboxAddressMap.WriteCombinedBase + 0x34A2000, 0xAABBCCDD);

        Assert.Equal(0xAABBCCDDu, machine.Ram.Read32(0x34A2000));
        Assert.Equal(0xAABBCCDDu, machine.Memory.Read32(0x34A2000));
        Assert.Equal(0xAABBCCDDu, machine.Memory.Read32(XboxAddressMap.WriteCombinedBase + 0x34A2000));
    }

    [Fact]
    public void FlashWindowKeepsPrecedenceOverWriteCombinedAlias()
    {
        XboxMachine machine = CreateLoadedMachine();

        Assert.Equal(0x90, machine.Memory.Read8(XboxAddressMap.FlashBase));
        machine.Memory.Write8(XboxAddressMap.FlashBase, 0x00);

        Assert.Equal(0x90, machine.Memory.Read8(XboxAddressMap.FlashBase));
    }

    [Fact]
    public void ResetRestoresOverlayAfterDisable()
    {
        XboxMachine machine = CreateLoadedMachine();
        machine.Bus.Pci.WriteConfig(PciBus.MakeConfigAddress(1, 0, 0x80), 1, 2);

        machine.Reset();

        Assert.True(machine.Bus.RomOverlayEnabled);
        Assert.Equal(0xB8, machine.Memory.Read8(ResetVector));
    }

    [Fact]
    public void OversizedBiosIsRejected()
    {
        XboxMachine machine = new();
        byte[] tooBig = new byte[XboxAddressMap.FlashSize + 1];

        Assert.Throws<ArgumentException>(() => machine.LoadBios(tooBig));
    }

    private static XboxMachine CreateLoadedMachine()
    {
        var machine = new XboxMachine();
        machine.LoadBios(FakeBios(), FakeMcpxRom());
        return machine;
    }

    private static byte[] FakeBios(int size = 256 * 1024)
    {
        byte[] bios = new byte[size];
        Array.Fill(bios, (byte)0x90);
        return bios;
    }

    private static byte[] FakeMcpxRom()
    {
        byte[] rom = new byte[512];
        Array.Fill(rom, (byte)0x90);

        // Reset vector sits at ROM offset 0x1F0: mov ax, 0x5678; hlt (real mode)
        byte[] code = { 0xB8, 0x78, 0x56, 0xF4 };
        code.CopyTo(rom, 0x1F0);
        return rom;
    }
}
