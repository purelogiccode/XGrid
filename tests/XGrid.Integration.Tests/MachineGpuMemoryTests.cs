using XGrid.Host;
using XGrid.Memory;
using XGrid.Nv2a;
using Xunit;

namespace XGrid.Integration.Tests;

/// <summary>
/// The machine publishes the GPU memory adapter it gives the NV2A, so a host
/// renderer (the CLI's GL backend, the Avalonia frontend) reads and watches the
/// same guest memory the device writes instead of wrapping the address space a
/// second time (decisions §220).
/// </summary>
public sealed class MachineGpuMemoryTests
{
    [Fact]
    public void GpuMemoryReadsAndWritesGuestRam()
    {
        var machine = new XboxMachine();

        machine.GpuMemory.WritePhysical(0x2000, 0x12345678);

        Assert.Equal(0x12345678u, machine.GpuMemory.ReadPhysical(0x2000));
        Assert.Equal(0x12345678u, machine.Memory.Read32(0x2000));
    }

    [Fact]
    public void GpuMemoryIsTheMachinesOwnAdapterNotASecondWrapper()
    {
        var machine = new XboxMachine();

        // Only the machine's adapter carries the rasterizer fast lane and the
        // present-dirty watch; a caller-built wrapper would expose neither.
        Assert.IsAssignableFrom<IGpuMemoryFast>(machine.GpuMemory);
        Assert.IsAssignableFrom<IPresentDirtyWatch>(machine.GpuMemory);

        bool ramView = ((IGpuMemoryRamView)machine.GpuMemory).TryGetRamBytes(out byte[] bytes, out uint size);
        Assert.True(ramView);
        Assert.Same(machine.Ram.RawBytes, bytes);
        Assert.Equal(machine.Ram.Size, size);
    }

    [Fact]
    public void GpuMemoryWritesDoNotDirtyThePresentedSpanButGuestWritesDo()
    {
        var machine = new XboxMachine();
        var watch = (IPresentDirtyWatch)machine.GpuMemory;
        watch.ArmPresentWatch(0x4000, 4);
        watch.ClearPresentDirty();

        // The renderer writing its own surface is not a guest store: it must
        // not arm the present upload (§132 finding i).
        machine.GpuMemory.WritePhysical(0x4000, 0x11111111);
        Assert.False(watch.PresentDirty);

        machine.Memory.Write32(0x4000, 0x22222222);
        Assert.True(watch.PresentDirty);
    }
}
