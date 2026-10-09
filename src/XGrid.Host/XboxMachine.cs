using System;
using XGrid.Bus;
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
    private ulong _nv2aRemainder;

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
        Nv2a = new Nv2aMmio(ramSize, "NV2A", new PhysicalGpuMemory(Memory));
        Nv2a.InterruptChanged += OnNv2aInterrupt;
        Memory.AddRegion(
            "NV2A",
            XboxAddressMap.Nv2aBase,
            XboxAddressMap.Nv2aSize,
            Nv2a);

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

        Bus.Lpc.RomOverlayDisabled += OnRomOverlayDisabled;
    }

    /// <summary>Gets the IDE controller.</summary>
    public IdeController Ide { get; }

    /// <summary>Gets the NV2A GPU register window.</summary>
    public Nv2aMmio Nv2a { get; }

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
    public void AttachHdd(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Span<byte> magic = stackalloc byte[4];
        using (System.IO.FileStream stream = System.IO.File.OpenRead(path))
        {
            stream.ReadExactly(magic);
        }

        IDiskImage image = magic[0] == 0x51 && magic[1] == 0x46 && magic[2] == 0x49 && magic[3] == 0xFB
            ? new Qcow2DiskImage(path)
            : new RawDiskImage(path);
        Ide.AttachPrimaryMaster(image);
    }

    /// <summary>Returns the CPU and bus to the power-on state and re-enables the MCPX overlay.</summary>
    public void Reset()
    {
        Cpu.Reset();
        Bus.Reset();
        Nv2a.Reset();
        _nv2aRemainder = 0;
        if (_overlay is not null)
        {
            _overlay.Enabled = true;
        }
    }

    /// <summary>Advances bus timers and the NV2A PTIMER by the given CPU cycles.</summary>
    public void Advance(ulong cycles)
    {
        Bus.Advance(cycles);
        _nv2aRemainder += cycles * Nv2aMmio.CoreClockHz;
        ulong ticks = _nv2aRemainder / XboxBus.CpuClockHz;
        _nv2aRemainder %= XboxBus.CpuClockHz;
        if (ticks != 0)
        {
            Nv2a.Advance(ticks);
        }
    }

    /// <summary>Runs up to the given number of instructions, advancing bus timers.</summary>
    public int Run(int maxInstructions)
    {
        const int BatchSize = 65536;
        int executed = 0;
        while (executed < maxInstructions && !Cpu.Context.Halted)
        {
            int batch = Math.Min(maxInstructions - executed, BatchSize);
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
            Bus.Nv2aPci.RaiseInterrupt();
        }
        else
        {
            Bus.Nv2aPci.LowerInterrupt();
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
