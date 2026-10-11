using System;
using System.Collections.Generic;
using XGrid.Bus.Usb;
using Xunit;

namespace XGrid.Bus.Tests;

public sealed class OhciUsbTests
{
    private const uint Base = 0xFED00000;

    private const uint HcControl = 0x04;
    private const uint HcCommandStatus = 0x08;
    private const uint HcInterruptStatus = 0x0C;
    private const uint HcInterruptEnable = 0x10;
    private const uint HcHcca = 0x18;
    private const uint HcFmNumber = 0x3C;
    private const uint HcRhDescriptorA = 0x48;
    private const uint HcRhStatus = 0x50;
    private const uint HcRhPortStatus1 = 0x54;

    private const uint UsbOperational = 0x80;
    private const uint UsbSuspend = 0xc0;
    private const uint StatusHcr = 0x1;
    private const uint IntrSf = 0x4;
    private const uint IntrRhsc = 0x40;
    private const uint IntrMie = 0x80000000;
    private const uint RhsLpsc = 0x00010000;
    private const uint PortCcs = 0x1;
    private const uint PortPes = 0x2;
    private const uint PortPps = 0x100;
    private const uint PortCsc = 0x10000;

    private sealed class FakeUsbMemory : IUsbMemory
    {
        private readonly Dictionary<uint, byte> _ram = new();

        public List<(uint Address, ushort Value)> Writes16 { get; } = new();

        public List<(uint Address, uint Value)> Writes32 { get; } = new();

        public byte Read8(uint address)
        {
            return _ram.TryGetValue(address, out byte value) ? value : (byte)0;
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
            Writes16.Add((address, value));
            Write8(address, (byte)value);
            Write8(address + 1, (byte)(value >> 8));
        }

        public void Write32(uint address, uint value)
        {
            Writes32.Add((address, value));
            for (int i = 0; i < 4; i++)
            {
                Write8(address + (uint)i, (byte)(value >> (i * 8)));
            }
        }

        public void WriteBlock(uint address, byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                Write8(address + (uint)i, data[i]);
            }
        }

        public byte[] ReadBlock(uint address, int length)
        {
            byte[] result = new byte[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = Read8(address + (uint)i);
            }

            return result;
        }
    }

    [Fact]
    public void RevisionAndRootHubDescriptor_MatchXemu()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        Assert.Equal(0x10u, ohci.Read(Base, 4));
        Assert.Equal(0x204u, ohci.Read(Base + HcRhDescriptorA, 4));
    }

    [Fact]
    public void EmptyPorts_ReportPowerAndNoConnect()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        uint status = ohci.Read(Base + HcRhPortStatus1, 4);

        Assert.Equal(PortPps, status & PortPps);
        Assert.Equal(0u, status & PortCcs);
    }

    [Fact]
    public void HubPowerUp_SetsPpsOnAllPorts()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        ohci.Write(Base + HcRhStatus, 4, RhsLpsc);

        Assert.Equal(PortPps, ohci.Read(Base + HcRhPortStatus1, 4) & PortPps);
        Assert.Equal(PortPps, ohci.Read(Base + HcRhPortStatus1 + 4, 4) & PortPps);
    }

    [Fact]
    public void PortEnableWrite_WithNoDevice_SetsConnectStatusChange()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());
        bool asserted = false;
        ohci.InterruptChanged += a => asserted = a;
        ohci.Write(Base + HcInterruptEnable, 4, IntrMie | IntrRhsc);

        ohci.Write(Base + HcRhPortStatus1, 4, PortPes);

        uint status = ohci.Read(Base + HcRhPortStatus1, 4);
        Assert.NotEqual(0u, status & PortCsc);
        Assert.Equal(0u, status & PortPes);
        Assert.NotEqual(0u, ohci.Read(Base + HcInterruptStatus, 4) & IntrRhsc);
        Assert.True(asserted);
    }

    [Fact]
    public void OperationalFrames_RaiseSofAndIncrementFrameNumber()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());
        bool asserted = false;
        ohci.InterruptChanged += a => asserted = a;
        ohci.Write(Base + HcInterruptEnable, 4, IntrMie | IntrSf);
        ohci.Write(Base + HcControl, 4, UsbOperational);

        ohci.Advance(733334);

        Assert.NotEqual(0u, ohci.Read(Base + HcInterruptStatus, 4) & IntrSf);
        Assert.Equal(1u, ohci.Read(Base + HcFmNumber, 4));
        Assert.True(asserted);

        ohci.Write(Base + HcInterruptStatus, 4, IntrSf);

        Assert.False(asserted);
    }

    [Fact]
    public void SuspendedController_DoesNotAdvanceFrames()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        ohci.Advance(10_000_000);

        Assert.Equal(0u, ohci.Read(Base + HcFmNumber, 4));
    }

    [Fact]
    public void FrameBoundary_WritesHccaFrameAndDone()
    {
        var memory = new FakeUsbMemory();
        var ohci = new OhciUsbController(Base, memory);
        ohci.Write(Base + HcHcca, 4, 0x10000000);
        ohci.Write(Base + HcControl, 4, UsbOperational);

        ohci.Advance(733334);

        Assert.Contains((0x10000080u, (ushort)1), memory.Writes16);
        Assert.Contains((0x10000082u, (ushort)0), memory.Writes16);
        Assert.Contains((0x10000084u, 0u), memory.Writes32);
    }

    [Fact]
    public void HostControllerReset_ClearsStatusButKeepsRootHub()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());
        ohci.Write(Base + HcInterruptEnable, 4, IntrMie | IntrSf);
        ohci.Write(Base + HcControl, 4, UsbOperational);
        ohci.Advance(733334);

        ohci.Write(Base + HcCommandStatus, 4, StatusHcr);

        Assert.Equal(0u, ohci.Read(Base + HcCommandStatus, 4));
        Assert.Equal(0u, ohci.Read(Base + HcInterruptStatus, 4));
        Assert.Equal(UsbSuspend, ohci.Read(Base + HcControl, 4) & 0xc0);
        Assert.Equal(0x204u, ohci.Read(Base + HcRhDescriptorA, 4));
    }

    [Fact]
    public void UnalignedAccess_ReturnsSentinelAndIsIgnored()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        Assert.Equal(0xFFFFFFFFu, ohci.Read(Base + 2, 4));

        ohci.Write(Base + 2, 4, 0x1234);
        Assert.Equal(0x10u, ohci.Read(Base, 4));
    }

    [Fact]
    public void PortStatusAccessor_MirrorsEmptyPorts()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        for (int i = 0; i < OhciUsbController.PortCount; i++)
        {
            Assert.Equal(0u, ohci.GetPortStatus(i));
        }

        Assert.Equal(0u, ohci.GetPortStatus(-1));
        Assert.Equal(0u, ohci.GetPortStatus(OhciUsbController.PortCount));
    }

    [Fact]
    public void AttachGamepad_ReportsConnectWithoutPowerWrites()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory())
        {
            Function = new XidGamepad(),
        };

        uint status = ohci.Read(Base + HcRhPortStatus1, 4);

        Assert.NotEqual(0u, status & PortCcs);
        Assert.NotEqual(0u, status & PortCsc);
    }

    [Fact]
    public void Port0PowerUp_WithGamepad_ReportsConnect()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory())
        {
            Function = new XidGamepad(),
        };

        ohci.Write(Base + HcRhStatus, 4, RhsLpsc);

        Assert.NotEqual(0u, ohci.GetPortStatus(0) & PortCcs);
        Assert.Equal(0u, ohci.GetPortStatus(1) & PortCcs);
    }

    [Fact]
    public void PortReset_WithGamepad_EnablesPort()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory())
        {
            Function = new XidGamepad(),
        };
        ohci.Write(Base + HcRhStatus, 4, RhsLpsc);

        ohci.Write(Base + HcRhPortStatus1, 4, 0x10);

        uint status = ohci.Read(Base + HcRhPortStatus1, 4);
        Assert.NotEqual(0u, status & PortPes);
        Assert.NotEqual(0u, status & 0x100000u);
    }

    [Fact]
    public void ControlGetDeviceDescriptor_RetiresWithWriteback()
    {
        var memory = new FakeUsbMemory();
        var ohci = new OhciUsbController(Base, memory)
        {
            Function = new XidGamepad(),
        };
        StartControl(ohci);

        byte[] data = RunControlTransfer(
            memory, ohci, 0,
            new byte[] { 0x80, 0x06, 0x00, 0x01, 0x00, 0x00, 0x12, 0x00 }, null, 0x12);

        Assert.Equal(0x12, data[0]);
        Assert.Equal(0x01, data[1]);
        Assert.Equal(0x5E, data[8]);
        Assert.Equal(0x04, data[9]);
        Assert.Equal(0x02, data[10]);
        Assert.Equal(0x02, data[11]);
        Assert.Equal(3, ohci.ControlTransfers);
        Assert.NotEqual(0u, ohci.Read(Base + 0x30, 4));
        Assert.NotEqual(0u, ohci.Read(Base + HcInterruptStatus, 4) & 0x2u);
        Assert.Contains(ohci.TransferLog, e => e.Contains(" ctl in ", StringComparison.Ordinal));
    }

    [Fact]
    public void ControlSetAddress_AssignsFunctionAddress()
    {
        var memory = new FakeUsbMemory();
        var ohci = new OhciUsbController(Base, memory)
        {
            Function = new XidGamepad(),
        };
        StartControl(ohci);

        RunControlTransfer(
            memory, ohci, 0,
            new byte[] { 0x00, 0x05, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00 }, null, 0);

        Assert.Equal(2u, ohci.DeviceAddress);

        RunControlTransfer(
            memory, ohci, 2,
            new byte[] { 0x00, 0x09, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 }, null, 0);

        Assert.True(((XidGamepad)ohci.Function!).Configured);
    }

    [Fact]
    public void PortReset_ResetsAttachedFunction()
    {
        var memory = new FakeUsbMemory();
        var ohci = new OhciUsbController(Base, memory)
        {
            Function = new XidGamepad(),
        };
        StartControl(ohci);
        RunControlTransfer(
            memory, ohci, 0,
            new byte[] { 0x00, 0x05, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00 }, null, 0);
        RunControlTransfer(
            memory, ohci, 2,
            new byte[] { 0x00, 0x09, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 }, null, 0);
        Assert.True(((XidGamepad)ohci.Function!).Configured);

        ohci.Write(Base + HcRhPortStatus1, 4, 0x10);

        Assert.Equal(0u, ohci.DeviceAddress);
        Assert.False(((XidGamepad)ohci.Function!).Configured);
    }

    [Fact]
    public void RegisterHistograms_CountOffsets()
    {
        var ohci = new OhciUsbController(Base, new FakeUsbMemory());

        ohci.Read(Base, 4);
        ohci.Read(Base, 4);
        ohci.Write(Base + HcControl, 4, UsbOperational);

        Assert.Equal(2, ohci.RegReadCounts[0]);
        Assert.Equal(1, ohci.RegWriteCounts[HcControl]);
    }

    [Fact]
    public void ShortInWithoutRounding_RetiresSuccessfully()
    {
        var memory = new FakeUsbMemory();
        var ohci = new OhciUsbController(Base, memory)
        {
            Function = new XidGamepad(),
        };
        StartControl(ohci);
        memory.WriteBlock(0x1000, EdBytes(64u << 16, 0x1100, 0x1400));
        memory.WriteBlock(0x2000, new byte[] { 0x80, 0x06, 0x00, 0x01, 0x00, 0x00, 0x40, 0x00 });
        memory.WriteBlock(0x1100, TdBytes(0, 0x2000, 0x2007, 0x1120));
        memory.WriteBlock(0x1120, TdBytes(2u << 19, 0x2010, 0x204F, 0x1140));
        memory.WriteBlock(0x1140, TdBytes(1u << 19, 0, 0, 0x1400));
        ohci.Write(Base + 0x20, 4, 0x1000);
        ohci.Write(Base + HcCommandStatus, 4, 0x02);

        ohci.Advance(733334 * 3);

        Assert.Equal(0u, memory.Read32(0x1120) >> 28);
        Assert.Equal(0x12, memory.Read8(0x2010));
    }

    [Fact]
    public void ChunkedConfigRead_CompletesWithEmptyTail()
    {
        var memory = new FakeUsbMemory();
        var ohci = new OhciUsbController(Base, memory)
        {
            Function = new XidGamepad(),
        };
        StartControl(ohci);
        memory.WriteBlock(0x1000, EdBytes(64u << 16, 0x1100, 0x1400));
        memory.WriteBlock(0x2000, new byte[] { 0x80, 0x06, 0x00, 0x02, 0x00, 0x00, 0x50, 0x00 });
        memory.WriteBlock(0x1100, TdBytes(0, 0x2000, 0x2007, 0x1120));
        memory.WriteBlock(0x1120, TdBytes(2u << 19, 0x2010, 0x204F, 0x1130));
        memory.WriteBlock(0x1130, TdBytes((2u << 19) | (1u << 18), 0x2050, 0x205F, 0x1140));
        memory.WriteBlock(0x1140, TdBytes(1u << 19, 0, 0, 0x1400));
        ohci.Write(Base + 0x20, 4, 0x1000);
        ohci.Write(Base + HcCommandStatus, 4, 0x02);

        ohci.Advance(733334 * 4);

        Assert.Equal(0u, memory.Read32(0x1120) >> 28);
        Assert.Equal(0u, memory.Read32(0x1130) >> 28);
        Assert.Equal(0x09, memory.Read8(0x2010));
        Assert.Equal(4, ohci.ControlTransfers);
    }

    [Fact]
    public void ControlBadRequest_StallsAndHaltsEndpoint()
    {
        var memory = new FakeUsbMemory();
        var ohci = new OhciUsbController(Base, memory)
        {
            Function = new XidGamepad(),
        };
        StartControl(ohci);
        memory.WriteBlock(0x1000, EdBytes(0, 0x1100, 0x1120));
        memory.WriteBlock(0x1100, TdBytes(0, 0x2000, 0x2007, 0x1120));
        memory.WriteBlock(0x2000, new byte[] { 0xC0, 0x06, 0x00, 0xFF, 0x00, 0x00, 0x08, 0x00 });
        ohci.Write(Base + 0x20, 4, 0x1000);
        ohci.Write(Base + HcCommandStatus, 4, 0x02);

        ohci.Advance(733334 * 3);

        Assert.Equal(4u, memory.Read32(0x1100) >> 28);
        Assert.NotEqual(0u, memory.Read32(0x1008) & 1u);
        // A halted endpoint idles the list, so the fill flag clears again.
        Assert.Equal(0u, ohci.Read(Base + HcCommandStatus, 4) & 0x02u);
    }

    [Fact]
    public void PeriodicInterruptIn_DeliversNeutralReport()
    {
        var memory = new FakeUsbMemory();
        var ohci = new OhciUsbController(Base, memory)
        {
            Function = new XidGamepad(),
        };
        memory.Write32(0x3000 + 4, 0x1500);
        memory.WriteBlock(0x1500, EdBytes((2u << 11) | (2u << 7) | (32u << 16), 0x1600, 0x1400));
        memory.WriteBlock(0x1600, TdBytes(0, 0x2100, 0x2113, 0x1400));
        ohci.Write(Base + HcHcca, 4, 0x3000);
        ohci.Write(Base + HcControl, 4, UsbOperational | 0x04);

        ohci.Advance(733334 * 3);

        byte[] report = memory.ReadBlock(0x2100, 20);
        Assert.Equal(0, report[0]);
        Assert.Equal(20, report[1]);
        Assert.Equal(new byte[] { 0, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, report);
        Assert.Equal(1, ohci.InterruptTransfers);
        Assert.NotEqual(0u, ohci.Read(Base + HcInterruptStatus, 4) & 0x2u);
    }

    private static void StartControl(OhciUsbController ohci)
    {
        ohci.Write(Base + HcControl, 4, UsbOperational | 0x10);
    }

    private static byte[] EdBytes(uint flags, uint head, uint tail)
    {
        byte[] ed = new byte[16];
        WriteWord(ed, 0, flags);
        WriteWord(ed, 4, tail);
        WriteWord(ed, 8, head);
        return ed;
    }

    private static byte[] TdBytes(uint flags, uint cbp, uint be, uint next)
    {
        byte[] td = new byte[16];
        WriteWord(td, 0, flags);
        WriteWord(td, 4, cbp);
        WriteWord(td, 8, next);
        WriteWord(td, 12, be);
        return td;
    }

    private static void WriteWord(byte[] buffer, int offset, uint value)
    {
        for (int i = 0; i < 4; i++)
        {
            buffer[offset + i] = (byte)(value >> (i * 8));
        }
    }

    private static byte[] RunControlTransfer(
        FakeUsbMemory memory, OhciUsbController ohci, byte address,
        byte[] setup, byte[]? outData, int inLength)
    {
        memory.WriteBlock(0x1000, EdBytes(address | (64u << 16), 0x1100, 0x1400));
        memory.WriteBlock(0x2000, setup);
        memory.WriteBlock(0x1100, TdBytes(0, 0x2000, 0x2007, outData is null && inLength == 0 ? 0x1140u : 0x1120u));
        uint next = 0x1140;
        int statusDir;
        if (inLength > 0)
        {
            memory.WriteBlock(0x1120, TdBytes((2u << 19) | (1u << 18), 0x2010, 0x2010 + (uint)inLength - 1, 0x1140));
            next = 0x1140;
            statusDir = 1;
        }
        else if (outData is not null)
        {
            memory.WriteBlock(0x2010, outData);
            memory.WriteBlock(0x1120, TdBytes(1u << 19, 0x2010, 0x2010 + (uint)outData.Length - 1, 0x1140));
            next = 0x1140;
            statusDir = 2;
        }
        else
        {
            statusDir = 2;
        }

        memory.WriteBlock(next, TdBytes((uint)statusDir << 19, 0, 0, 0x1400));
        ohci.Write(Base + 0x20, 4, 0x1000);
        ohci.Write(Base + HcCommandStatus, 4, 0x02);

        ohci.Advance(733334 * 3);

        Assert.Equal(0u, memory.Read32(0x1100) >> 28);
        Assert.Equal(0x1400u, memory.Read32(0x1008) & 0xFFFFFFF0u);
        return inLength > 0 ? memory.ReadBlock(0x2010, inLength) : Array.Empty<byte>();
    }
}
