using XGrid.Cpu.Tcg;
using XGrid.Memory;

namespace XGrid.Cpu.Tests;

/// <summary>The IR engine's guest-memory view over the machine's flattened bus.</summary>
internal sealed class TcgTestMemory(AddressSpace bus) : ITcgMemory
{
    /// <summary>Gets the underlying bus.</summary>
    public AddressSpace Bus { get; } = bus;

    public byte Read8(uint address)
    {
        return Bus.Read8(address);
    }

    public ushort Read16(uint address)
    {
        return (ushort)(Bus.Read8(address) | (Bus.Read8(address + 1) << 8));
    }

    public uint Read32(uint address)
    {
        return (uint)(Bus.Read8(address)
            | (Bus.Read8(address + 1) << 8)
            | (Bus.Read8(address + 2) << 16)
            | (Bus.Read8(address + 3) << 24));
    }

    public void Write8(uint address, byte value)
    {
        Bus.Write8(address, value);
    }

    public void Write16(uint address, ushort value)
    {
        Bus.Write8(address, (byte)value);
        Bus.Write8(address + 1, (byte)(value >> 8));
    }

    public void Write32(uint address, uint value)
    {
        Bus.Write8(address, (byte)value);
        Bus.Write8(address + 1, (byte)(value >> 8));
        Bus.Write8(address + 2, (byte)(value >> 16));
        Bus.Write8(address + 3, (byte)(value >> 24));
    }
}
