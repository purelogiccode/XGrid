using System;
using XGrid.Audio;
using XGrid.Bus;
using XGrid.Bus.Aci;
using XGrid.Bus.Net;
using XGrid.Bus.Usb;
using XGrid.Cpu;
using XGrid.Memory;
using XGrid.Nv2a;
using XGrid.Storage;

namespace XGrid.Host;

/// <summary>The assembled Xbox machine: RAM, address map, bus, and CPU.</summary>
public sealed class XboxMachine
{
    /// <summary>Default retail RAM size (64 MB).</summary>
    public const uint DefaultRamSize = 64u * 1024 * 1024;

    /// <summary>Contiguous RAM alias used by the kernel physical map.</summary>
    public const uint ContiguousAliasBase = 0x80000000;

    private RomOverlayDevice? _overlay;
    private byte[]? _bios;
    private ulong _nv2ARemainder;

    /// <summary>Creates the machine with RAM, bus, and CPU wired together.</summary>
    public XboxMachine(uint ramSize = DefaultRamSize)
    {
        Ram = new PhysicalMemory(ramSize);
        Memory = new AddressSpace(Ram, UnmappedAccessPolicy.Sentinel);
        Bus = new XboxBus();
        Cpu = new XboxCpu(Memory, Bus.Io) { InterruptSource = Bus.Pic };
        Ide = new IdeController();
        Bus.Pci.Register(Ide);
        Ide.RegisterPorts(Bus.Io);
        Ide.DmaMemory = new AddressSpaceGuestMemory(Memory);
        Nv2A = new Nv2AMmio(ramSize, "NV2A", new PhysicalGpuMemory(Memory));
        Nv2A.InterruptChanged += OnNv2aInterrupt;
        Memory.AddRegion(
            "NV2A",
            XboxAddressMap.Nv2ABase,
            XboxAddressMap.Nv2ASize,
            Nv2A);

        Aci = new McpxAciDevice();
        Memory.AddRegion(
            "MCPX ACI",
            XboxAddressMap.AciBase,
            XboxAddressMap.AciSize,
            Aci);

        Apu = new McpxApuDevice(new PhysicalApuMemory(Memory));
        Apu.InterruptChanged += OnApuInterrupt;
        Memory.AddRegion(
            "MCPX APU",
            XboxAddressMap.ApuBase,
            XboxAddressMap.ApuSize,
            Apu);

        Net = new McpxNvNetDevice();
        Net.InterruptChanged += OnNetInterrupt;
        Net.DmaMemory = new PhysicalNvNetMemory(Memory);
        Memory.AddRegion(
            "MCPX NVNET",
            XboxAddressMap.NvNetBase,
            XboxAddressMap.NvNetSize,
            Net);

        Usb0 = new OhciUsbController(XboxAddressMap.Usb0Base, new PhysicalUsbMemory(Memory));
        Usb0.InterruptChanged += OnUsb0Interrupt;
        Gamepad = new XidGamepad();
        var controllerHub = new UsbHub(3);
        controllerHub.Attach(0, Gamepad);
        var daughterboard = new UsbHub(4);
        daughterboard.Attach(2, controllerHub);
        Usb0.Function = daughterboard;
        Memory.AddRegion("USB0", XboxAddressMap.Usb0Base, XboxAddressMap.UsbSize, Usb0);

        Usb1 = new OhciUsbController(XboxAddressMap.Usb1Base, new PhysicalUsbMemory(Memory));
        Usb1.InterruptChanged += OnUsb1Interrupt;
        Memory.AddRegion("USB1", XboxAddressMap.Usb1Base, XboxAddressMap.UsbSize, Usb1);

        Memory.AddRegion(
            "RAM mirror",
            XboxAddressMap.RamMirrorBase,
            XboxAddressMap.RamMirrorSize,
            new RamAliasDevice(Ram, XboxAddressMap.RamMirrorBase));
        Memory.AddRegion(
            "RAM alias",
            ContiguousAliasBase,
            ramSize,
            new RamAliasDevice(Ram, ContiguousAliasBase));

        // Tiled / write-combined RAM view ("Shadow RAM", 0xF0000000-0xF3FFFFFF).
        // Registered before LoadBios maps flash at the same base, so the flash
        // window keeps precedence over its first megabyte by tie order; the rest
        // of the range newly mirrors RAM (previously sentinel void).
        Memory.AddRegion(
            "RAM write-combined",
            XboxAddressMap.WriteCombinedBase,
            XboxAddressMap.WriteCombinedSize,
            new RamAliasDevice(Ram, XboxAddressMap.WriteCombinedBase));

        Bus.Lpc.RomOverlayDisabled += OnRomOverlayDisabled;
    }

    /// <summary>Gets the IDE controller.</summary>
    public IdeController Ide { get; }

    /// <summary>Gets the NV2A GPU register window.</summary>
    public Nv2AMmio Nv2A { get; }

    /// <summary>Gets the MCPX ACI (AC'97 audio) register window.</summary>
    public McpxAciDevice Aci { get; }

    /// <summary>Gets the MCPX APU register window.</summary>
    public McpxApuDevice Apu { get; }

    /// <summary>Gets the MCPX nvnet (Ethernet) register window.</summary>
    public McpxNvNetDevice Net { get; }

    /// <summary>Gets the OHCI USB0 register window.</summary>
    public OhciUsbController Usb0 { get; }

    /// <summary>Gets the player-1 XID gamepad behind the USB0 hub topology.</summary>
    public XidGamepad Gamepad { get; }

    /// <summary>Gets the OHCI USB1 register window.</summary>
    public OhciUsbController Usb1 { get; }

    /// <summary>Gets the physical RAM.</summary>
    public PhysicalMemory Ram { get; }

    /// <summary>Gets the physical address space.</summary>
    public AddressSpace Memory { get; }

    /// <summary>Gets the Xbox bus.</summary>
    public XboxBus Bus { get; }

    /// <summary>Gets the CPU.</summary>
    public XboxCpu Cpu { get; }

    /// <summary>Gets whether a BIOS image has been loaded.</summary>
    public bool BiosLoaded => _bios is not null;

    /// <summary>Gets the padded flash image size in bytes (0 when no BIOS is loaded).</summary>
    public uint BiosSize => (uint)(_bios?.Length ?? 0);

    /// <summary>Loads a BIOS image and optional 512-byte MCPX boot ROM, then maps flash and overlay.</summary>
    public void LoadBios(ReadOnlySpan<byte> bios, ReadOnlySpan<byte> mcpxRom = default)
    {
        if (bios.Length == 0 || bios.Length > XboxAddressMap.FlashSize)
        {
            throw new ArgumentException(
                $"BIOS size must be between 1 byte and {XboxAddressMap.FlashSize} bytes.",
                nameof(bios));
        }

        uint flashSize = RoundUpPowerOfTwo((uint)bios.Length);
        byte[] image = new byte[flashSize];
        Array.Fill(image, (byte)0xFF);
        bios.CopyTo(image);
        _bios = image;

        Memory.AddRegion(
            "Flash",
            XboxAddressMap.FlashBase,
            flashSize,
            new RomDevice(XboxAddressMap.FlashBase, image));

        var alias = new RomAliasDevice(XboxAddressMap.FlashMirrorBase, image);
        Memory.AddRegion(
            "Flash alias",
            XboxAddressMap.FlashMirrorBase,
            XboxAddressMap.FlashMirrorSize,
            alias);

        byte[] overlayImage = (byte[])image.Clone();
        if (!mcpxRom.IsEmpty)
        {
            int count = Math.Min(mcpxRom.Length, overlayImage.Length);
            mcpxRom[..count].CopyTo(overlayImage.AsSpan(overlayImage.Length - count));
        }

        uint overlayBase = (uint)(0x100000000UL - flashSize);
        _overlay = new RomOverlayDevice(overlayBase, overlayImage, alias);
        Memory.AddRegion("MCPX ROM overlay", overlayBase, flashSize, _overlay, priority: 1);
    }

    /// <summary>Loads an EEPROM image into the SMBus EEPROM device.</summary>
    public void LoadEeprom(ReadOnlySpan<byte> image)
    {
        Bus.AttachEeprom(image);
    }

    /// <summary>Attaches a raw or qcow2 HDD image to the primary master IDE drive.</summary>
    /// <remarks>Writes are captured by an in-memory overlay so the backing image is not modified.</remarks>
    public void AttachHdd(string path)
    {
        HddOverlay = new OverlayDiskImage(OpenDiskImage(path));
        Ide.AttachPrimaryMaster(HddOverlay);
    }

    /// <summary>Gets the HDD copy-on-write overlay, or null when no hard disk is attached.</summary>
    public OverlayDiskImage? HddOverlay { get; private set; }

    /// <summary>Attaches a DVD image to the ATAPI slave drive (read-only, no overlay).</summary>
    public void AttachDvd(string path)
    {
        Dvd = OpenDvdImage(path);
        Ide.AttachDvd(Dvd);
    }

    /// <summary>Opens an ISO/XISO or CHD DVD image by magic number.</summary>
    public static IDvdImage OpenDvdImage(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Span<byte> magic = stackalloc byte[8];
        using (System.IO.FileStream stream = System.IO.File.OpenRead(path))
        {
            stream.ReadExactly(magic);
        }

        return magic[0] == (byte)'M' && magic[1] == (byte)'C' && magic[2] == (byte)'o'
            && magic[3] == (byte)'m' && magic[4] == (byte)'p' && magic[5] == (byte)'r'
            && magic[6] == (byte)'H' && magic[7] == (byte)'D'
            ? ChdDvdImage.Open(path)
            : new IsoDvdImage(path);
    }

    /// <summary>Gets the attached DVD image, or null when no disc is present.</summary>
    public IDvdImage? Dvd { get; private set; }

    /// <summary>Opens a raw or qcow2 disk image by magic number.</summary>
    public static IDiskImage OpenDiskImage(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Span<byte> magic = stackalloc byte[4];
        using (System.IO.FileStream stream = System.IO.File.OpenRead(path))
        {
            stream.ReadExactly(magic);
        }

        return magic[0] == 0x51 && magic[1] == 0x46 && magic[2] == 0x49 && magic[3] == 0xFB
            ? new Qcow2DiskImage(path)
            : new RawDiskImage(path);
    }

    /// <summary>Returns the CPU and bus to the power-on state and re-enables the MCPX overlay.</summary>
    public void Reset()
    {
        Cpu.Reset();
        Bus.Reset();
        Nv2A.Reset();
        Aci.Reset();
        Apu.Reset();
        Net.Reset();
        _nv2ARemainder = 0;
        if (_overlay is not null)
        {
            _overlay.Enabled = true;
        }
    }

    /// <summary>Advances bus timers and the NV2A PTIMER by the given CPU cycles.</summary>
    public void Advance(ulong cycles)
    {
        Bus.Advance(cycles);
        Apu.Advance(cycles);
        Usb0.Advance(cycles);
        Usb1.Advance(cycles);
        _nv2ARemainder += cycles * Nv2AMmio.CoreClockHz;
        ulong ticks = _nv2ARemainder / XboxBus.CpuClockHz;
        _nv2ARemainder %= XboxBus.CpuClockHz;
        if (ticks != 0)
        {
            Nv2A.Advance(ticks);
        }
    }

    /// <summary>Runs up to the given number of instructions, advancing bus timers.</summary>
    public int Run(int maxInstructions)
    {
        const int batchSize = 65536;
        int executed = 0;
        while (executed < maxInstructions && !Cpu.Context.Halted)
        {
            int batch = Math.Min(maxInstructions - executed, batchSize);
            int done = Cpu.Execute(batch);
            executed += done;
            Advance((ulong)done);
            if (done < batch)
            {
                break;
            }
        }

        return executed;
    }

    private void OnRomOverlayDisabled()
    {
        if (_overlay is not null)
        {
            _overlay.Enabled = false;
        }
    }

    private void OnNv2aInterrupt(bool asserted)
    {
        if (asserted)
        {
            Bus.Nv2APci.RaiseInterrupt();
        }
        else
        {
            Bus.Nv2APci.LowerInterrupt();
        }
    }

    private void OnApuInterrupt(bool asserted)
    {
        if (asserted)
        {
            Bus.ApuPci.RaiseInterrupt();
        }
        else
        {
            Bus.ApuPci.LowerInterrupt();
        }
    }

    private void OnNetInterrupt(bool asserted)
    {
        if (asserted)
        {
            Bus.NvNetPci.RaiseInterrupt();
        }
        else
        {
            Bus.NvNetPci.LowerInterrupt();
        }
    }

    private void OnUsb0Interrupt(bool asserted)
    {
        if (asserted)
        {
            Bus.Usb0Pci.RaiseInterrupt();
        }
        else
        {
            Bus.Usb0Pci.LowerInterrupt();
        }
    }

    private void OnUsb1Interrupt(bool asserted)
    {
        if (asserted)
        {
            Bus.Usb1Pci.RaiseInterrupt();
        }
        else
        {
            Bus.Usb1Pci.LowerInterrupt();
        }
    }

    private sealed class PhysicalApuMemory : IApuMemory
    {
        private readonly AddressSpace _memory;

        public PhysicalApuMemory(AddressSpace memory)
        {
            _memory = memory;
        }

        public uint Read32(uint address)
        {
            return _memory.Read32(address);
        }

        public void Write32(uint address, uint value)
        {
            _memory.Write32(address, value);
        }

        public void Write8(uint address, byte value)
        {
            _memory.Write8(address, value);
        }
    }

    private sealed class PhysicalNvNetMemory : INvNetMemory
    {
        private readonly AddressSpace _memory;

        public PhysicalNvNetMemory(AddressSpace memory)
        {
            _memory = memory;
        }

        public byte ReadPhysical(uint address)
        {
            return _memory.Read8(address);
        }

        public void WritePhysical(uint address, byte value)
        {
            _memory.Write8(address, value);
        }
    }

    private sealed class PhysicalUsbMemory : IUsbMemory
    {
        private readonly AddressSpace _memory;

        public PhysicalUsbMemory(AddressSpace memory)
        {
            _memory = memory;
        }

        public byte Read8(uint address)
        {
            return _memory.Read8(address);
        }

        public ushort Read16(uint address)
        {
            return _memory.Read16(address);
        }

        public uint Read32(uint address)
        {
            return _memory.Read32(address);
        }

        public void Write8(uint address, byte value)
        {
            _memory.Write8(address, value);
        }

        public void Write16(uint address, ushort value)
        {
            _memory.Write16(address, value);
        }

        public void Write32(uint address, uint value)
        {
            _memory.Write32(address, value);
        }
    }

    private sealed class PhysicalGpuMemory : IGpuMemory
    {
        private readonly AddressSpace _memory;

        public PhysicalGpuMemory(AddressSpace memory)
        {
            _memory = memory;
        }

        public uint ReadPhysical(uint address)
        {
            return _memory.Read32(address);
        }

        public void WritePhysical(uint address, uint value)
        {
            _memory.Write32(address, value);
        }
    }

    private sealed class AddressSpaceGuestMemory : IGuestMemory
    {
        private readonly AddressSpace _memory;

        public AddressSpaceGuestMemory(AddressSpace memory)
        {
            _memory = memory;
        }

        public byte ReadPhysical(uint address)
        {
            return _memory.Read8(address);
        }

        public void WritePhysical(uint address, byte value)
        {
            _memory.Write8(address, value);
        }
    }

    private static uint RoundUpPowerOfTwo(uint value)
    {
        uint result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}
