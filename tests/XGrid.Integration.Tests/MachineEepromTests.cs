using System;
using XGrid.Bus.Smbus;
using XGrid.Host;
using Xunit;

namespace XGrid.Integration.Tests;

/// <summary>
/// Pins the host-level EEPROM image path used by the CLI (<c>--eeprom-out</c>)
/// and the desktop frontend's persistent state: the seed is loaded into the
/// SMBus device and the live image (seed + guest writes) can be copied out.
/// </summary>
public sealed class MachineEepromTests
{
    [Fact]
    public void CopyEepromReturnsTheSeededImage()
    {
        var machine = new XboxMachine();
        byte[] seed = new byte[Eeprom24C02.Size];
        for (int i = 0; i < seed.Length; i++)
        {
            seed[i] = (byte)(0x40 + (i & 0x3F));
        }

        machine.LoadEeprom(seed);
        byte[] image = new byte[Eeprom24C02.Size];
        machine.CopyEeprom(image);

        Assert.Equal(seed, image);
    }

    [Fact]
    public void CopyEepromSeesGuestWrites()
    {
        var machine = new XboxMachine();
        machine.LoadEeprom(new byte[Eeprom24C02.Size]);

        // The guest reaches the same device through the SMBus; a write there is
        // exactly what the console's settings menu performs.
        machine.Bus.Eeprom.WriteByte(0x08, 0x5A);

        byte[] image = new byte[Eeprom24C02.Size];
        machine.CopyEeprom(image);

        Assert.Equal(0x5A, image[0x08]);
        Assert.Equal(0x00, image[0x09]);
    }

    [Fact]
    public void CopyEepromRejectsAShortBuffer()
    {
        var machine = new XboxMachine();

        Assert.Throws<ArgumentException>(() => machine.CopyEeprom(new byte[Eeprom24C02.Size - 1]));
    }
}
