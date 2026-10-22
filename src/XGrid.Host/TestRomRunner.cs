using System;
using XGrid.Bus.Io;
using XGrid.Cpu;

namespace XGrid.Host;

/// <summary>
/// Runs a flat-binary test ROM on a bare <see cref="XboxMachine"/> (no BIOS,
/// kernel, or disk required). Reuses the machine as-is: the image is copied to
/// guest-physical base, the CPU enters flat protected mode, and an
/// <see cref="TestRomConsole"/> is registered on I/O port 0xE9.
/// </summary>
/// <remarks>
/// Pass convention: the ROM ends with <c>HLT</c>; <c>EAX == 0</c> means pass,
/// any other <c>EAX</c> value is a failure code.
/// </remarks>
public sealed class TestRomRunner : IDisposable
{
    /// <summary>Guest console port (Bochs-style debug output).</summary>
    public const ushort ConsolePort = 0xE9;

    private const uint GdtAddress = 0x7000;

    private readonly IDisposable _consoleRegistration;
    private bool _disposed;

    /// <summary>Creates a runner around an existing machine and attaches the console.</summary>
    /// <param name="machine">Machine to run ROMs on (reused as-is).</param>
    /// <param name="name">ROM name used in the TESTROM PASS/FAIL verdict.</param>
    public TestRomRunner(XboxMachine machine, string name)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentException.ThrowIfNullOrEmpty(name);
        Machine = machine;
        Name = name;
        Console = new TestRomConsole();
        _consoleRegistration = machine.Bus.Io.Register(ConsolePort, 1, Console);
    }

    /// <summary>Gets the machine the ROM runs on.</summary>
    public XboxMachine Machine { get; }

    /// <summary>Gets the ROM name used in verdict lines.</summary>
    public string Name { get; }

    /// <summary>Gets the guest console attached at port 0xE9.</summary>
    public TestRomConsole Console { get; }

    /// <summary>Gets whether the ROM halted with EAX == 0 (pass).</summary>
    public bool Passed => Machine.Cpu.Context.Halted && Machine.Cpu.Context.Eax == 0;

    /// <summary>
    /// Copies the image to guest-physical base and enters flat protected mode at
    /// entry with the given stack. Follows the <c>CpuHarness</c> pattern (flat GDT
    /// at 0x7000, selectors 0x08/0x10, no paging).
    /// </summary>
    public void LoadImage(ReadOnlySpan<byte> image, uint baseAddress, uint entryAddress, uint stackAddress)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (image.IsEmpty)
        {
            throw new ArgumentException("Test ROM image must not be empty.", nameof(image));
        }

        if (baseAddress + (ulong)image.Length > Machine.Ram.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(baseAddress), "Test ROM image does not fit in RAM.");
        }

        Machine.Reset();
        image.CopyTo(Machine.Ram.GetSpan(baseAddress, image.Length));

        XboxCpu cpu = Machine.Cpu;
        cpu.Reset();
        cpu.Context.Cr0 = 1;
        cpu.Context.CodeSegment32 = true;
        cpu.Context.SetSegment(SegmentRegister.Cs, 0x08, 0);
        cpu.Context.SetSegment(SegmentRegister.Ds, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Ss, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Es, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Fs, 0x10, 0);
        cpu.Context.SetSegment(SegmentRegister.Gs, 0x10, 0);
        cpu.Context.Eflags = FlagBits.Reserved1;
        cpu.Context.Esp = stackAddress;

        // Flat 32-bit code/data descriptors at selector 0x08/0x10.
        cpu.Context.Gdtr.Base = GdtAddress;
        cpu.Context.Gdtr.Limit = 0xFFFF;
        Machine.Ram.Write32(GdtAddress + 8, 0x0000FFFF);
        Machine.Ram.Write32(GdtAddress + 12, 0x00CF9A00);
        Machine.Ram.Write32(GdtAddress + 16, 0x0000FFFF);
        Machine.Ram.Write32(GdtAddress + 20, 0x00CF9200);

        cpu.Context.Eip = entryAddress;
    }

    /// <summary>Runs up to <paramref name="steps"/> instructions; returns the count executed.</summary>
    public ulong Run(ulong steps)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ulong executed = 0;
        while (executed < steps && !Machine.Cpu.Context.Halted)
        {
            ulong remaining = steps - executed;
            int batch = remaining > int.MaxValue ? int.MaxValue : (int)remaining;
            int done = Machine.Run(batch);
            if (done <= 0)
            {
                break;
            }

            executed += (ulong)done;
        }

        return executed;
    }

    /// <summary>Unregisters the debug console from the I/O bus and marks the runner disposed; repeat calls are no-ops.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _consoleRegistration.Dispose();
            _disposed = true;
        }
    }
}
