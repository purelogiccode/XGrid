using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XGrid.Audio;
using XGrid.Bus;
using XGrid.Bus.Aci;
using XGrid.Bus.Net;
using XGrid.Bus.Usb;
using XGrid.Core.Snapshots;
using XGrid.Cpu;
using XGrid.Memory;
using XGrid.Nv2a;
using XGrid.Storage;

namespace XGrid.Host;

/// <summary>The assembled Xbox machine: RAM, address map, bus, and CPU.</summary>
public sealed class XboxMachine : IIdleSkipHost
{
    /// <summary>Default retail RAM size (64 MB).</summary>
    public const uint DefaultRamSize = 64u * 1024 * 1024;

    /// <summary>V1 snapshot file magic ("XGS1").</summary>
    public const string SnapshotMagic = "XGS1";

    /// <summary>V1 snapshot container format version.</summary>
    public const uint SnapshotFormatVersion = 1;

    private const int MaxSnapshotSectionCount = 1024;
    private const ulong MaxSnapshotSectionBytes = 512u * 1024u * 1024u;

    /// <summary>Contiguous RAM alias used by the kernel physical map.</summary>
    public const uint ContiguousAliasBase = 0x80000000;

    private RomOverlayDevice? _overlay;
    private byte[]? _bios;
    private ulong _nv2ARemainder;
    private bool _sevenEosQuirkParkActive;
    private ulong _sevenEosQuirkParkSinceTsc;
    private readonly List<(string Tag, ISnapshotable Target)> _snapshotSections = new();

    /// <summary>Creates the machine with RAM, bus, and CPU wired together.</summary>
    public XboxMachine(uint ramSize = DefaultRamSize)
    {
        Ram = new PhysicalMemory(ramSize);
        Memory = new AddressSpace(Ram, UnmappedAccessPolicy.Sentinel);
        Bus = new XboxBus();
        Cpu = new XboxCpu(Memory, Bus.Io) { InterruptSource = Bus.Pic };
        Cpu.IdleSkipHost = this;
        Ide = new IdeController();
        Bus.Pci.Register(Ide);
        Ide.RegisterPorts(Bus.Io);
        Ide.DmaMemory = new AddressSpaceGuestMemory(Memory, Cpu);
        Nv2A = new Nv2AMmio(ramSize, "NV2A", new PhysicalGpuMemory(Memory, Cpu));
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

        Apu = new McpxApuDevice(new PhysicalApuMemory(Memory, Cpu));
        Apu.InterruptChanged += OnApuInterrupt;
        Memory.AddRegion(
            "MCPX APU",
            XboxAddressMap.ApuBase,
            XboxAddressMap.ApuSize,
            Apu);

        Net = new McpxNvNetDevice();
        Net.InterruptChanged += OnNetInterrupt;
        Net.DmaMemory = new PhysicalNvNetMemory(Memory, Cpu);
        Memory.AddRegion(
            "MCPX NVNET",
            XboxAddressMap.NvNetBase,
            XboxAddressMap.NvNetSize,
            Net);

        Usb0 = new OhciUsbController(XboxAddressMap.Usb0Base, new PhysicalUsbMemory(Memory, Cpu));
        Usb0.InterruptChanged += OnUsb0Interrupt;
        Gamepad = new XidGamepad();
        var controllerHub = new UsbHub(3);
        controllerHub.Attach(0, Gamepad);
        var daughterboard = new UsbHub(4);
        daughterboard.Attach(2, controllerHub);
        Usb0.Function = daughterboard;
        Memory.AddRegion("USB0", XboxAddressMap.Usb0Base, XboxAddressMap.UsbSize, Usb0);

        Usb1 = new OhciUsbController(XboxAddressMap.Usb1Base, new PhysicalUsbMemory(Memory, Cpu));
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

        RegisterSnapshotSection("CPU", Cpu);
        RegisterSnapshotSection("RAM", Ram);
        // V1 device sections (wave/snapshot-devices). Each entry cascades to
        // its sub-devices: BUS → PIC/PIT/RTC/PCI/SMBus, IDE → drives+overlay,
        // USB0/USB1 → hubs+gamepad, NET → loopback backend.
        RegisterSnapshotSection("BUS", Bus);
        RegisterSnapshotSection("IDE", Ide);
        RegisterSnapshotSection("NV2A", Nv2A);
        RegisterSnapshotSection("ACI", Aci);
        RegisterSnapshotSection("APU", Apu);
        RegisterSnapshotSection("NET", Net);
        RegisterSnapshotSection("USB0", Usb0);
        RegisterSnapshotSection("USB1", Usb1);
        RegisterSnapshotSection("MACH", new MachineScalarsSnapshot(this));
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

    /// <summary>Gets or sets whether the 007 end-of-stream compatibility quirk is enabled (default on).</summary>
    public bool SevenEosQuirkEnabled { get; set; } = true;

    /// <summary>Gets the number of guest-memory quirk checks performed.</summary>
    public ulong SevenEosQuirkChecks { get; private set; }

    /// <summary>Gets the number of times the 007 end-of-stream quirk fired.</summary>
    public ulong SevenEosQuirkFires { get; private set; }

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
        Ide.HddPath = path;
    }

    /// <summary>Gets the HDD copy-on-write overlay, or null when no hard disk is attached.</summary>
    public OverlayDiskImage? HddOverlay { get; private set; }

    /// <summary>Attaches a DVD image to the ATAPI slave drive (read-only, no overlay).</summary>
    public void AttachDvd(string path)
    {
        Dvd = OpenDvdImage(path);
        Ide.AttachDvd(Dvd);
        Ide.DvdPath = path;
    }

    /// <summary>Opens an ISO/XISO or CHD DVD image by magic number.</summary>
    public static IDvdImage OpenDvdImage(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Span<byte> magic = stackalloc byte[8];
        using (FileStream stream = File.OpenRead(path))
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
        using (FileStream stream = File.OpenRead(path))
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
        SevenEosQuirkChecks = 0;
        SevenEosQuirkFires = 0;
        _sevenEosQuirkParkActive = false;
        _sevenEosQuirkParkSinceTsc = 0;
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
        Net.Advance(cycles);
        _nv2ARemainder += cycles * Nv2AMmio.CoreClockHz;
        ulong ticks = _nv2ARemainder / XboxBus.CpuClockHz;
        _nv2ARemainder %= XboxBus.CpuClockHz;
        if (ticks != 0)
        {
            Nv2A.Advance(ticks);
        }

        CheckSevenEosQuirk();
    }

    /// <summary>
    /// PIC request/mask peek for idle-skip: true when any unmasked master request
    /// is latched (slave lines surface through the master cascade bit 2). Over-approximates
    /// delivery (ignores the in-service register), which only makes skip boundaries more
    /// conservative. Side-effect free; never acknowledges the PIC.
    /// </summary>
    bool IIdleSkipHost.IrqPendingAndEnabled
    {
        get
        {
            byte pending = (byte)(Bus.Pic.MasterRequest & ~Bus.Pic.MasterMask);
            return pending != 0;
        }
    }

    /// <summary>
    /// No device-deadline API exists without touching device projects (owned by sibling
    /// areas), so the real machine skips in fixed chunks bounded by IRQ and RAM-watch
    /// checks; the deadline hook is exercised by unit-test hosts.
    /// </summary>
    long? IIdleSkipHost.StepsUntilNextDeadline => null;

    /// <summary>
    /// Classifies a physical address as RAM-backed for idle-skip: main RAM plus the
    /// tiled/contiguous aliases, minus the flash windows that keep precedence over the
    /// write-combined mirror's first megabyte.
    /// </summary>
    bool IIdleSkipHost.IsRam(uint physicalAddress)
    {
        uint ramSize = (uint)Ram.Size;
        if (physicalAddress < ramSize)
        {
            return true;
        }

        if (physicalAddress >= XboxAddressMap.RamMirrorBase
            && physicalAddress - XboxAddressMap.RamMirrorBase < XboxAddressMap.RamMirrorSize)
        {
            return true;
        }

        if (physicalAddress >= ContiguousAliasBase && physicalAddress - ContiguousAliasBase < ramSize)
        {
            return true;
        }

        uint writeCombined = XboxAddressMap.WriteCombinedBase;
        return physicalAddress >= writeCombined + XboxAddressMap.FlashSize
               && physicalAddress - writeCombined < XboxAddressMap.WriteCombinedSize;
    }

    void IIdleSkipHost.AdvanceIdle(ulong steps)
    {
        Advance(steps);
    }

    /// <summary>
    /// Checks the 007 end-of-stream parked signature and synthesizes the missing
    /// completion after a stability delay. Returns true when the quirk fired.
    /// </summary>
    public bool CheckSevenEosQuirk()
    {
        if (!SevenEosQuirkEnabled)
        {
            return false;
        }

        if (!Cpu.Mmu.IsEnabled)
        {
            _sevenEosQuirkParkActive = false;
            return false;
        }

        SevenEosQuirkChecks++;

        if (!SevenEosQuirk.IsParkedLive(ReadVa))
        {
            _sevenEosQuirkParkActive = false;
            return false;
        }

        ulong now = Cpu.Context.Tsc;
        if (!_sevenEosQuirkParkActive)
        {
            _sevenEosQuirkParkActive = true;
            _sevenEosQuirkParkSinceTsc = now;
            return false;
        }

        if (now - _sevenEosQuirkParkSinceTsc < SevenEosQuirk.StabilityThresholdSteps)
        {
            return false;
        }

        if (!SevenEosQuirk.TryApply(ReadVa, WriteVa))
        {
            _sevenEosQuirkParkActive = false;
            return false;
        }

        SevenEosQuirkFires++;
        _sevenEosQuirkParkActive = false;
        return true;

        uint? ReadVa(uint virtualAddress)
        {
            uint physical = Cpu.Mmu.Translate(
                virtualAddress, isWrite: false, isInstruction: false,
                out bool faulted, out _, sideEffects: false);
            if (faulted)
            {
                return null;
            }

            return Memory.Read32(physical);
        }

        void WriteVa(uint virtualAddress, uint value)
        {
            uint physical = Cpu.Mmu.Translate(
                virtualAddress, isWrite: true, isInstruction: false,
                out bool faulted, out _);
            if (!faulted)
            {
                Memory.Write32(physical, value);

                // Host write outside the CPU store sites: drop the decode
                // cache page (§131 leg (d) invalidation inventory).
                Cpu.InvalidateDecodeCachePage(physical);
            }
        }
    }

    /// <summary>Runs up to the given number of instructions, advancing bus timers.</summary>
    /// <remarks>
    /// With idle-skip enabled, <see cref="XboxCpu.Execute"/> may bulk-advance
    /// devices itself (via <see cref="IIdleSkipHost.AdvanceIdle"/>) for skipped steps; those
    /// cycles are subtracted here so devices are never advanced twice.
    /// </remarks>
    public int Run(int maxInstructions)
    {
        const int batchSize = 65536;
        int executed = 0;
        while (executed < maxInstructions && !Cpu.Context.Halted)
        {
            int batch = Math.Min(maxInstructions - executed, batchSize);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            int done = Cpu.Execute(batch);
            long afterExecute = System.Diagnostics.Stopwatch.GetTimestamp();
            int hostAdvanced = Cpu.TakeIdleAdvanced();
            executed += done;
            Advance((ulong)(done - hostAdvanced));
            _runExecuteTicks += afterExecute - start;
            _runAdvanceTicks += System.Diagnostics.Stopwatch.GetTimestamp() - afterExecute;
            if (done < batch)
            {
                break;
            }
        }

        return executed;
    }

    private long _runExecuteTicks;
    private long _runAdvanceTicks;

    /// <summary>Gets the time spent inside <c>Cpu.Execute</c> across Run calls (diagnostic).</summary>
    public double RunExecuteMs => _runExecuteTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Gets the time spent inside device <c>Advance</c> across Run calls (diagnostic).</summary>
    public double RunAdvanceMs => _runAdvanceTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Registers a tagged snapshot section (V1 snapshot).</summary>
    /// <remarks>Tags must be unique; sections are written in registration order.</remarks>
    public void RegisterSnapshotSection(string tag, ISnapshotable target)
    {
        ArgumentException.ThrowIfNullOrEmpty(tag);
        ArgumentNullException.ThrowIfNull(target);

        foreach ((string existing, _) in _snapshotSections)
        {
            if (string.Equals(existing, tag, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Duplicate snapshot section tag '{tag}'.", nameof(tag));
            }
        }

        _snapshotSections.Add((tag, target));
    }

    /// <summary>
    /// Saves CPU, RAM, machine scalars, and every registered device section to a file.
    /// Must be called at an instruction boundary (between <see cref="Run"/> calls):
    /// mid-instruction guest state is not observable and is never captured.
    /// </summary>
    public void SaveState(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes(SnapshotMagic));
        writer.Write(SnapshotFormatVersion);
        writer.Write(_snapshotSections.Count);
        foreach ((string tag, ISnapshotable target) in _snapshotSections)
        {
            using var payload = new MemoryStream();
            using (var sectionWriter = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            {
                target.SaveState(sectionWriter);
            }

            byte[] bytes = payload.ToArray();
            writer.Write(tag);
            writer.Write((ulong)bytes.Length);
            writer.Write(bytes);
        }
    }

    /// <summary>
    /// Restores CPU, RAM, machine scalars, and every registered device section from a file.
    /// The caller must first construct an identical machine (same RAM size, same BIOS/MCPX/
    /// EEPROM/HDD/DVD inputs) and call <see cref="Reset"/>; only emulated state is restored,
    /// host-side configuration (disk image paths, backends) is not. Unknown sections are
    /// skipped so files written with more device sections still load. Must be called at an
    /// instruction boundary (between <see cref="Run"/> calls).
    /// </summary>
    public void LoadState(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        byte[] magic = reader.ReadBytes(4);
        if (magic.Length != 4 ||
            !string.Equals(Encoding.ASCII.GetString(magic), SnapshotMagic, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Not an XGrid snapshot (bad magic).");
        }

        uint version = reader.ReadUInt32();
        if (version != SnapshotFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported snapshot format version {version} (expected {SnapshotFormatVersion}).");
        }

        int count = reader.ReadInt32();
        if (count < 0 || count > MaxSnapshotSectionCount)
        {
            throw new InvalidDataException($"Invalid snapshot section count {count}.");
        }

        var sections = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            string tag = reader.ReadString();
            ulong length = reader.ReadUInt64();
            if (length > MaxSnapshotSectionBytes)
            {
                throw new InvalidDataException($"Snapshot section '{tag}' exceeds size limit ({length} bytes).");
            }

            byte[] payload = reader.ReadBytes((int)length);
            if ((ulong)payload.Length != length)
            {
                throw new InvalidDataException($"Snapshot section '{tag}' is truncated.");
            }

            if (!sections.TryAdd(tag, payload))
            {
                throw new InvalidDataException($"Snapshot has a duplicate section '{tag}'.");
            }
        }

        foreach ((string tag, ISnapshotable target) in _snapshotSections)
        {
            if (!sections.TryGetValue(tag, out byte[]? payload))
            {
                throw new InvalidDataException($"Snapshot is missing required section '{tag}'.");
            }

            using var payloadStream = new MemoryStream(payload, writable: false);
            using var sectionReader = new BinaryReader(payloadStream, Encoding.UTF8, leaveOpen: false);
            target.LoadState(sectionReader);
        }
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

    /// <summary>Core-owned machine scalars that live on <see cref="XboxMachine"/> itself
    /// (not in a device): the NV2A PTIMER fractional-cycle accumulator and the 007
    /// end-of-stream quirk detector state. Written as the trailing "MACH" section.</summary>
    private sealed class MachineScalarsSnapshot : ISnapshotable
    {
        private const uint MachineStateVersion = 1;

        private readonly XboxMachine _machine;

        public MachineScalarsSnapshot(XboxMachine machine)
        {
            _machine = machine;
        }

        public void SaveState(BinaryWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.Write(MachineStateVersion);
            writer.Write(_machine._nv2ARemainder);
            writer.Write(_machine.SevenEosQuirkEnabled);
            writer.Write(_machine.SevenEosQuirkChecks);
            writer.Write(_machine.SevenEosQuirkFires);
            writer.Write(_machine._sevenEosQuirkParkActive);
            writer.Write(_machine._sevenEosQuirkParkSinceTsc);
        }

        public void LoadState(BinaryReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            uint version = reader.ReadUInt32();
            if (version != MachineStateVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported MACH state version {version} (expected {MachineStateVersion}).");
            }

            _machine._nv2ARemainder = reader.ReadUInt64();
            _machine.SevenEosQuirkEnabled = reader.ReadBoolean();
            _machine.SevenEosQuirkChecks = reader.ReadUInt64();
            _machine.SevenEosQuirkFires = reader.ReadUInt64();
            _machine._sevenEosQuirkParkActive = reader.ReadBoolean();
            _machine._sevenEosQuirkParkSinceTsc = reader.ReadUInt64();
        }
    }

    /// <summary>
    /// <c>IApuMemory</c> adapter over the machine's physical address space
    /// (DSP state + VP notifier writes). Forwards to the decode cache's
    /// page-drop invalidation so cached code over DMA-written RAM can never
    /// go stale (§131 leg (d) invalidation inventory).
    /// </summary>
    private sealed class PhysicalApuMemory : IApuMemory
    {
        private readonly AddressSpace _memory;
        private readonly XboxCpu _cpu;

        public PhysicalApuMemory(AddressSpace memory, XboxCpu cpu)
        {
            _memory = memory;
            _cpu = cpu;
        }

        public uint Read32(uint address)
        {
            return _memory.Read32(address);
        }

        public void Write32(uint address, uint value)
        {
            _memory.Write32(address, value);
            _cpu.InvalidateDecodeCachePage(address);
        }

        public void Write8(uint address, byte value)
        {
            _memory.Write8(address, value);
            _cpu.InvalidateDecodeCachePage(address);
        }
    }

    /// <summary>
    /// <c>INvNetMemory</c> adapter (RX frames + descriptors; §131 leg (d)
    /// invalidation inventory). Page-drops the decode cache like the other
    /// device shims.
    /// </summary>
    private sealed class PhysicalNvNetMemory : INvNetMemory
    {
        private readonly AddressSpace _memory;
        private readonly XboxCpu _cpu;

        public PhysicalNvNetMemory(AddressSpace memory, XboxCpu cpu)
        {
            _memory = memory;
            _cpu = cpu;
        }

        public byte ReadPhysical(uint address)
        {
            return _memory.Read8(address);
        }

        public void WritePhysical(uint address, byte value)
        {
            _memory.Write8(address, value);
            _cpu.InvalidateDecodeCachePage(address);
        }
    }

    /// <summary>
    /// <c>IUsbMemory</c> adapter (HCCA/done/ED writes; §131 leg (d)
    /// invalidation inventory). Page-drops the decode cache like the other
    /// device shims.
    /// </summary>
    private sealed class PhysicalUsbMemory : IUsbMemory
    {
        private readonly AddressSpace _memory;
        private readonly XboxCpu _cpu;

        public PhysicalUsbMemory(AddressSpace memory, XboxCpu cpu)
        {
            _memory = memory;
            _cpu = cpu;
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
            _cpu.InvalidateDecodeCachePage(address);
        }

        public void Write16(uint address, ushort value)
        {
            _memory.Write16(address, value);
            _cpu.InvalidateDecodeCachePage(address);
        }

        public void Write32(uint address, uint value)
        {
            _memory.Write32(address, value);
            _cpu.InvalidateDecodeCachePage(address);
        }
    }

    /// <summary>
    /// <c>IGpuMemory</c> adapter (NV2A semaphore + surface/zeta writes — the
    /// hot one: every rasterized pixel goes through it; §131 leg (d)
    /// invalidation inventory). Page-drops the decode cache; framebuffer
    /// pages hold no cached code, so the drop is a bounds check and a clear
    /// bit in the common case. Doubles as the present-upload dirty watch
    /// handle for a backend attached to this machine (§132 (a*), §135).
    /// </summary>
    private sealed class PhysicalGpuMemory : IGpuMemory, IGpuMemoryFast, IPresentDirtyWatch
    {
        private readonly AddressSpace _memory;
        private readonly XboxCpu _cpu;
        private bool _gpuBatch;
        private bool _pageMemoValid;
        private uint _pageMemo;

        public PhysicalGpuMemory(AddressSpace memory, XboxCpu cpu)
        {
            _memory = memory;
            _cpu = cpu;
        }

        public bool PresentDirty => _memory.Ram.PresentDirty;

        public uint ReadPhysical(uint address)
        {
            return _memory.Read32(address);
        }

        public void WritePhysical(uint address, uint value)
        {
            // Writer identity (§132 finding i): the software twin's own
            // surface writes must not trip the present-upload dirty bit;
            // only real guest (CPU/device DMA) writers count.
            _memory.Ram.BeginGpuWriteSuppression();
            try
            {
                _memory.Write32(address, value);
            }
            finally
            {
                _memory.Ram.EndGpuWriteSuppression();
            }

            _cpu.InvalidateDecodeCachePage(address);
        }

        /// <summary>
        /// Rasterizer O2: one suppression scope and one decode-page memo per
        /// draw. Inside a batch the emitted pixel path writes the pinned RAM
        /// bytes directly (the CPU cannot run mid-draw, so a page drop once
        /// per draw is exact) and falls back for non-RAM, watched or
        /// unbatched addresses.
        /// </summary>
        public void BeginGpuBatch()
        {
            _gpuBatch = true;
            _pageMemoValid = false;
            _memory.Ram.BeginGpuWriteSuppression();
        }

        public void EndGpuBatch()
        {
            _memory.Ram.EndGpuWriteSuppression();
            _gpuBatch = false;
        }

        public bool TryReadPhysical(uint address, out uint value)
        {
            PhysicalMemory ram = _memory.Ram;
            if (address < ram.Size && address <= ram.Size - 4u)
            {
                value = BinaryPrimitives.ReadUInt32LittleEndian(ram.RawBytes.AsSpan((int)address, 4));
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryWritePhysical(uint address, uint value)
        {
            PhysicalMemory ram = _memory.Ram;
            if (!_gpuBatch || address >= ram.Size || address > ram.Size - 4u)
            {
                return false;
            }

            // Bus-watch parity: the legacy AddressSpace path raises CheckWatch
            // for every write, so any overlap must take that path.
            if (_memory.WatchEnd > _memory.WatchStart
                && address >= _memory.WatchStart && address < _memory.WatchEnd)
            {
                return false;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(ram.RawBytes.AsSpan((int)address, 4), value);
            uint page = address >> 12;
            if (!_pageMemoValid || page != _pageMemo)
            {
                _cpu.InvalidateDecodeCachePage(address);
                _pageMemo = page;
                _pageMemoValid = true;
            }

            return true;
        }

        public void ArmPresentWatch(uint address, uint length)
        {
            _memory.Ram.ArmPresentWatch(address, length);
        }

        public void DisarmPresentWatch()
        {
            _memory.Ram.DisarmPresentWatch();
        }

        public void MarkPresentDirty()
        {
            _memory.Ram.MarkPresentDirty();
        }

        public void ClearPresentDirty()
        {
            _memory.Ram.ClearPresentDirty();
        }
    }

    /// <summary>
    /// <c>IGuestMemory</c> adapter (IDE BM-DMA PRDT data; §131 leg (d)
    /// invalidation inventory). Page-drops the decode cache like the other
    /// device shims.
    /// </summary>
    private sealed class AddressSpaceGuestMemory : IGuestMemory
    {
        private readonly AddressSpace _memory;
        private readonly XboxCpu _cpu;

        public AddressSpaceGuestMemory(AddressSpace memory, XboxCpu cpu)
        {
            _memory = memory;
            _cpu = cpu;
        }

        public byte ReadPhysical(uint address)
        {
            return _memory.Read8(address);
        }

        public void WritePhysical(uint address, byte value)
        {
            _memory.Write8(address, value);
            _cpu.InvalidateDecodeCachePage(address);
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
