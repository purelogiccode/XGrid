using XGrid.Bus.Io;
using XGrid.Bus.Timers;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class Pit8254Tests
{
    [Fact]
    public void ConfigureAndLatchReturnsReload()
    {
        var io = new IoSpace();
        var pit = new Pit8254(io);

        io.Write8(0x43, 0x34);
        io.Write8(0x40, 0x00);
        io.Write8(0x40, 0x10);

        io.Write8(0x43, 0x00);

        Assert.Equal(0x00u, io.Read8(0x40));
        Assert.Equal(0x10u, io.Read8(0x40));
    }

    [Fact]
    public void AdvancePulsesOutputOnReload()
    {
        var io = new IoSpace();
        var pit = new Pit8254(io);
        int pulses = 0;
        pit.Output += channel =>
        {
            if (channel == 0)
            {
                pulses++;
            }
        };

        io.Write8(0x43, 0x34);
        io.Write8(0x40, 10);
        io.Write8(0x40, 0x00);

        pit.Advance(10);

        Assert.Equal(1, pulses);
    }

    [Fact]
    public void Mode0IsOneShot()
    {
        var io = new IoSpace();
        var pit = new Pit8254(io);
        int pulses = 0;
        pit.Output += _ => pulses++;

        io.Write8(0x43, 0x30);
        io.Write8(0x40, 5);
        io.Write8(0x40, 0x00);

        pit.Advance(10);
        pit.Advance(10);

        Assert.Equal(1, pulses);
    }

    [Fact]
    public void CounterDecrements()
    {
        var io = new IoSpace();
        var pit = new Pit8254(io);

        io.Write8(0x43, 0x34);
        io.Write8(0x40, 0x00);
        io.Write8(0x40, 0x10);

        pit.Advance(0x100);

        io.Write8(0x43, 0x00);
        Assert.Equal(0x00u, io.Read8(0x40));
        Assert.Equal(0x0Fu, io.Read8(0x40));
    }

    [Fact]
    public void ResetStopsOutput()
    {
        var io = new IoSpace();
        var pit = new Pit8254(io);
        int pulses = 0;
        pit.Output += _ => pulses++;

        io.Write8(0x43, 0x34);
        io.Write8(0x40, 5);
        io.Write8(0x40, 0x00);
        pit.Reset();

        pit.Advance(100);

        Assert.Equal(0, pulses);
    }
}
