using System;
using XGrid.Bus.Io;
using XGrid.Bus.Pci;

namespace XGrid.Storage;

/// <summary>Xbox IDE controller (nForce 10DE:01BC at PCI 0:9.0, IRQ 14) with one primary channel.</summary>
public sealed class IdeController : PciDevice
{
    private readonly AtaDevice _primaryMaster = new();

    /// <summary>Creates the controller and defines its PCI header.</summary>
    public IdeController()
        : base("nForce IDE", 9, 0, 0x10DE, 0x01BC, 0xB1, 0x01, 0x01, 0x80)
    {
        DefineBar(0, 8, isIo: true);
        DefineBar(1, 4, isIo: true);
        DefineBar(2, 8, isIo: true);
        DefineBar(3, 4, isIo: true);
        DefineBar(4, 16, isIo: true);
        _primaryMaster.Interrupt += RaiseInterrupt;
    }

    /// <summary>Gets the primary master drive.</summary>
    public AtaDevice PrimaryMaster => _primaryMaster;

    /// <summary>Attaches media to the primary master drive.</summary>
    public void AttachPrimaryMaster(IDiskImage disk)
    {
        _primaryMaster.Attach(disk);
    }

    /// <summary>Registers the legacy compatibility ports (0x1F0 and 0x3F6).</summary>
    public void RegisterPorts(IoSpace io)
    {
        ArgumentNullException.ThrowIfNull(io);
        io.Register(0x1F0, 8, new TaskfileWindow(_primaryMaster));
        io.Register(0x3F6, 2, new ControlWindow(_primaryMaster));
    }

    /// <inheritdoc />
    public override void Reset()
    {
        base.Reset();
        _primaryMaster.Reset();
    }

    private sealed class TaskfileWindow : IIoPortDevice
    {
        private readonly AtaDevice _device;
        private readonly string _name = "IDE taskfile";

        public TaskfileWindow(AtaDevice device)
        {
            _device = device;
        }

        public string Name => _name;

        public uint Read(ushort offset, int size)
        {
            return _device.ReadTaskfile(offset, size);
        }

        public void Write(ushort offset, int size, uint value)
        {
            _device.WriteTaskfile(offset, size, value);
        }
    }

    private sealed class ControlWindow : IIoPortDevice
    {
        private readonly AtaDevice _device;
        private readonly string _name = "IDE control";

        public ControlWindow(AtaDevice device)
        {
            _device = device;
        }

        public string Name => _name;

        public uint Read(ushort offset, int size)
        {
            return _device.ReadControl();
        }

        public void Write(ushort offset, int size, uint value)
        {
            _device.WriteControl(value);
        }
    }
}
