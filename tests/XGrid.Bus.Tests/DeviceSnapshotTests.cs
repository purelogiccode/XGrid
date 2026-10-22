using System;
using System.Collections.Generic;
using System.IO;
using XGrid.Bus.Aci;
using XGrid.Bus.Interrupts;
using XGrid.Bus.Io;
using XGrid.Bus.Net;
using XGrid.Bus.Pci;
using XGrid.Bus.Smbus;
using XGrid.Bus.Timers;
using XGrid.Bus.Usb;
using XGrid.Core.Snapshots;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class DeviceSnapshotTests
{
    private static byte[] Save(ISnapshotable device)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        device.SaveState(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void Load(ISnapshotable device, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        device.LoadState(reader);
    }

    private static void AssertRoundTrip(Func<ISnapshotable> create, Action<ISnapshotable> exercise)
    {
        ISnapshotable first = create();
        exercise(first);
        byte[] baseline = Save(first);

        ISnapshotable second = create();
        Load(second, baseline);
        byte[] restored = Save(second);
        Assert.Equal(baseline, restored);

        exercise(second);
        Load(second, baseline);
        byte[] reloaded = Save(second);
        Assert.Equal(baseline, reloaded);
    }

    private sealed class FakeUsbMemory : IUsbMemory
    {
        private readonly Dictionary<uint, byte> _ram = [];

        public byte Read8(uint address)
        {
            return _ram.GetValueOrDefault(address, (byte)0);
        }

        public ushort Read16(uint address)
        {
            return (ushort)(Read8(address) | (Read8(address + 1) << 8));
        }

        public uint Read32(uint address)
        {
            return (uint)(Read8(address) | (Read8(address + 1) << 8) | (Read8(address + 2) << 16) |
                          (Read8(address + 3) << 24));
        }

        public void Write8(uint address, byte value)
        {
            _ram[address] = value;
        }

        public void Write16(uint address, ushort value)
        {
            Write8(address, (byte)value);
            Write8(address + 1, (byte)(value >> 8));
        }

        public void Write32(uint address, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                Write8(address + (uint)i, (byte)(value >> (i * 8)));
            }
        }
    }

    [Fact]
    public void Pit_RoundTrip()
    {
        AssertRoundTrip(
            () => new Pit8254(new IoSpace()),
            device =>
            {
                var pit = (Pit8254)device;
                pit.Write(3, 8, 0x36);
                pit.Write(0, 8, 0xFF);
                pit.Write(0, 8, 0xFF);
                pit.Write(3, 8, 0x76);
                pit.Write(1, 8, 0x12);
                pit.Advance(5000);
                pit.Read(0, 8);
            });
    }

    [Fact]
    public void Pic_RoundTrip()
    {
        AssertRoundTrip(
            () => new Pic8259(new IoSpace()),
            device =>
            {
                var pic = (Pic8259)device;
                pic.SetIrq(0, true);
                pic.SetIrq(5, true);
                pic.SetIrq(14, true);
                pic.Acknowledge();
                pic.SetIrq(5, false);
            });
    }

    [Fact]
    public void Rtc_RoundTrip()
    {
        AssertRoundTrip(
            () => new Rtc146818(new IoSpace()),
            device =>
            {
                var rtc = (Rtc146818)device;
                rtc.Write(0, 8, 0x0B);
                rtc.Write(1, 8, 0x42);
                rtc.Advance(70000);
                rtc.Read(0, 8);
            });
    }

    [Fact]
    public void PciBus_RoundTrip()
    {
        AssertRoundTrip(
            () =>
            {
                var io = new IoSpace();
                var bus = new PciBus(io);
                bus.Register(new PciDevice("Test NIC", 4, 0, 0x10DE, 0x01C3, 0xB1, 0x02, 0x00, 0x00));
                return bus;
            },
            device =>
            {
                var bus = (PciBus)device;
                uint address = PciBus.MakeConfigAddress(4, 0, 0x04);
                bus.WriteConfig(address, 2, 0x0007);
                bus.WriteConfig(address, 1, 0x09);
                bus.ReadConfig(address, 4);
            });
    }

    [Fact]
    public void Lpc_RoundTrip()
    {
        AssertRoundTrip(
            () =>
            {
                var io = new IoSpace();
                var bus = new PciBus(io);
                return new LpcController(io, bus);
            },
            device =>
            {
                var lpc = (LpcController)device;
                lpc.Pm1Enable = 0x0101;
                lpc.AdvancePmTimer(123456);
                lpc.WriteConfig(0x6C, 4, 0x0E065492);
            });
    }

    [Fact]
    public void Smbus_RoundTrip()
    {
        AssertRoundTrip(
            () =>
            {
                var io = new IoSpace();
                var bus = new PciBus(io);
                var smbus = new Amd756Smbus(io, bus);
                smbus.Attach(new SmcDevice());
                smbus.Attach(new Eeprom24C02());
                return smbus;
            },
            device =>
            {
                var smbus = (Amd756Smbus)device;
                smbus.Write(0x04, 8, 0xA9);
                smbus.Write(0x08, 8, 0x01);
                smbus.Write(0x02, 8, 0x0B);
                smbus.Read(0x00, 8);
            });
    }

    [Fact]
    public void Smc_RoundTrip()
    {
        AssertRoundTrip(
            () => new SmcDevice(),
            device =>
            {
                var smc = (SmcDevice)device;
                smc.TrayState = 0x40;
                smc.AvPack = 0x02;
                smc.WriteByte(0x1B, 0x07);
                smc.TryReadByte(0x01, out _);
                smc.TryReceiveByte(out _);
            });
    }

    [Fact]
    public void Eeprom_RoundTrip()
    {
        AssertRoundTrip(
            () => new Eeprom24C02([0x01, 0x02, 0x03]),
            device =>
            {
                var eeprom = (Eeprom24C02)device;
                eeprom.WriteByte(0x10, 0xAB);
                eeprom.TryReadWord(0x10, out _);
            });
    }

    [Fact]
    public void Encoder_RoundTrip()
    {
        AssertRoundTrip(
            () => new ConexantCx25871(),
            device =>
            {
                var encoder = (ConexantCx25871)device;
                encoder.WriteByte(0x20, 0x77);
                encoder.TryReadByte(0x20, out _);
            });
    }

    [Fact]
    public void XboxBus_RoundTrip()
    {
        AssertRoundTrip(
            () => new XboxBus(),
            device =>
            {
                var bus = (XboxBus)device;
                bus.Advance(1000000);
                bus.Pic.SetIrq(0, true);
                bus.Lpc.AdvancePmTimer(999);
                bus.Smc.TrayState = 0x40;
            });
    }

    [Fact]
    public void Aci_RoundTrip()
    {
        AssertRoundTrip(
            () => new McpxAciDevice(),
            device =>
            {
                var aci = (McpxAciDevice)device;
                aci.Write(McpxAciDevice.BaseAddress + 0x02, 2, 0x1234);
                aci.Write(McpxAciDevice.BaseAddress + 0x134, 1, 0x01);
                aci.Write(McpxAciDevice.BaseAddress + 0x12C, 4, 0x00000003);
                aci.Write(McpxAciDevice.BaseAddress + 0x100, 4, 0xDEADBEEFu);
                aci.Read(McpxAciDevice.BaseAddress + 0x02, 2);
            });
    }

    [Fact]
    public void NvNet_RoundTrip_Loopback()
    {
        AssertRoundTrip(
            () => new McpxNvNetDevice(),
            device =>
            {
                var net = (McpxNvNetDevice)device;
                net.Write(McpxNvNetDevice.BaseAddress + 0x004, 4, 0x0000007E);
                net.Write(McpxNvNetDevice.BaseAddress + 0x100, 4, 0x00100000);
                net.Write(McpxNvNetDevice.BaseAddress + 0x104, 4, 0x00200000);
                net.Write(McpxNvNetDevice.BaseAddress + 0x108, 4, 0x00030003);
                net.Write(McpxNvNetDevice.BaseAddress + 0x144, 4, 0x00000001);
                net.SetLinkUp(false);
                net.Advance(1000);
                net.Read(McpxNvNetDevice.BaseAddress + 0x000, 4);
            });
    }

    [Fact]
    public void NvNet_SocketBackend_SaveFailsLoudly()
    {
        var net = new McpxNvNetDevice { Backend = new SocketNetworkBackend() };
        try
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            Assert.Throws<InvalidDataException>(() => net.SaveState(writer));
        }
        finally
        {
            ((SocketNetworkBackend)net.Backend).Dispose();
        }
    }

    [Fact]
    public void NvNet_SocketBackend_LoadFailsLoudly()
    {
        var loopback = new McpxNvNetDevice();
        byte[] baseline = Save(loopback);
        var socket = new McpxNvNetDevice { Backend = new SocketNetworkBackend() };
        try
        {
            Assert.Throws<InvalidDataException>(() => Load(socket, baseline));
        }
        finally
        {
            ((SocketNetworkBackend)socket.Backend).Dispose();
        }
    }

    [Fact]
    public void Gamepad_RoundTrip()
    {
        AssertRoundTrip(
            () => new XidGamepad(),
            device =>
            {
                var pad = (XidGamepad)device;
                pad.SetInput(0x0003, [1, 2, 3, 4, 5, 6, 7, 8], [100, -100, 50, -50]);
                pad.HandleSetup(new UsbSetupPacket(0x00, 0x05, 0x0002, 0, 0), out _);
                pad.CompleteStatusStage();
            });
    }

    [Fact]
    public void Hub_RoundTrip()
    {
        AssertRoundTrip(
            () =>
            {
                var hub = new UsbHub(3);
                hub.Attach(0, new XidGamepad());
                return hub;
            },
            device =>
            {
                var hub = (UsbHub)device;
                ((XidGamepad)hub.GetPortDevice(0)!).SetInput(0x0010, new byte[8], new short[4]);
                hub.HandleSetup(new UsbSetupPacket(0x00, 0x05, 0x0003, 0, 0), out _);
                hub.CompleteStatusStage();
            });
    }

    [Fact]
    public void Ohci_RoundTrip()
    {
        AssertRoundTrip(
            () =>
            {
                var controller = new OhciUsbController(0xFED00000, new FakeUsbMemory());
                var hub = new UsbHub(4);
                var child = new UsbHub(3);
                child.Attach(0, new XidGamepad());
                hub.Attach(2, child);
                controller.Function = hub;
                return controller;
            },
            device =>
            {
                var controller = (OhciUsbController)device;
                controller.Write(0xFED00000 + 0x04, 4, 0x80);
                controller.Write(0xFED00000 + 0x18, 4, 0x00FFF000);
                controller.Write(0xFED00000 + 0x20, 4, 0x00FF0000);
                controller.Advance(XboxBus.CpuClockHz);
                controller.Read(0xFED00000 + 0x0C, 4);
            });
    }
}
