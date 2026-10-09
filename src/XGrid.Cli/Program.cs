using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using XGrid.Cpu;
using XGrid.Host;
using XGrid.Memory;

namespace XGrid.Cli;

internal static class Program
{
    private const string Version = "0.1.0";

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        switch (args[0])
        {
            case "--help":
            case "-h":
                PrintUsage();
                return 0;
            case "--version":
            case "-v":
                Console.WriteLine($"XGrid {Version}");
                return 0;
            case "boot":
                return Boot(args);
            default:
                Console.Error.WriteLine($"Unknown command: {args[0]}");
                PrintUsage();
                return 1;
        }
    }

    private static int Boot(string[] args)
    {
        string? biosPath = null;
        string? mcpxPath = null;
        string? eepromPath = null;
        string? hddPath = null;
        int steps = 1_000_000;
        int trace = 0;
        int traceStart = 0;
        bool traceRegs = false;
        uint? breakAddress = null;
        uint ramMb = 64;
        List<(uint Address, int Length, bool Virtual)> dumps = new();
        List<string> scans = new();
        uint machineWatchStart = 0x35B00;
        uint machineWatchEnd = 0x35D00;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--bios":
                    biosPath = NextValue(args, ref i);
                    break;
                case "--mcpx":
                    mcpxPath = NextValue(args, ref i);
                    break;
                case "--eeprom":
                    eepromPath = NextValue(args, ref i);
                    break;
                case "--hdd":
                    hddPath = NextValue(args, ref i);
                    break;
                case "--steps":
                    steps = int.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--trace":
                    trace = int.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--trace-start":
                    traceStart = int.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--trace-regs":
                    traceRegs = true;
                    break;
                case "--break":
                    {
                        string text = NextValue(args, ref i);
                        string hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
                        breakAddress = Convert.ToUInt32(hex, 16);
                        break;
                    }
                case "--dump":
                    {
                        string[] parts = NextValue(args, ref i).Split(',');
                        string addressText = parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? parts[0][2..] : parts[0];
                        uint address = Convert.ToUInt32(addressText, 16);
                        int length = parts.Length > 1 ? Convert.ToInt32(parts[1], 16) : 0x40;
                        dumps.Add((address, length, false));
                        break;
                    }
                case "--dumpv":
                    {
                        string[] parts = NextValue(args, ref i).Split(',');
                        string addressText = parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? parts[0][2..] : parts[0];
                        uint address = Convert.ToUInt32(addressText, 16);
                        int length = parts.Length > 1 ? Convert.ToInt32(parts[1], 16) : 0x40;
                        dumps.Add((address, length, true));
                        break;
                    }
                case "--ram":
                    ramMb = uint.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--scan":
                    scans.Add(NextValue(args, ref i));
                    break;
                case "--watch":
                    {
                        string[] parts = NextValue(args, ref i).Split(',');
                        string startText = parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? parts[0][2..] : parts[0];
                        string endText = parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? parts[1][2..] : parts[1];
                        machineWatchStart = Convert.ToUInt32(startText, 16);
                        machineWatchEnd = Convert.ToUInt32(endText, 16);
                        break;
                    }
                default:
                    Console.Error.WriteLine($"Unknown boot option: {args[i]}");
                    return 1;
            }
        }

        if (biosPath is null)
        {
            Console.Error.WriteLine("boot requires --bios <path>");
            return 1;
        }

        XboxMachine? machine = null;
        try
        {
            machine = new XboxMachine(ramMb * 1024 * 1024);
            machine.LoadBios(
                File.ReadAllBytes(biosPath),
                mcpxPath is null ? default : File.ReadAllBytes(mcpxPath));
            if (eepromPath is not null)
            {
                machine.LoadEeprom(File.ReadAllBytes(eepromPath));
            }

            if (hddPath is not null)
            {
                machine.AttachHdd(hddPath);
            }

            machine.Reset();

            machine.Memory.WatchStart = machineWatchStart;
            machine.Memory.WatchEnd = machineWatchEnd;
            var watchHits = new System.Collections.Generic.Dictionary<uint, (int Count, string First, string Last)>();
            machine.Memory.WatchHit += (address, size, value) =>
            {
                string mark = $"{machine.Cpu.Context.Eip:X8}:{size}={value:X}";
                if (watchHits.TryGetValue(address, out var entry))
                {
                    watchHits[address] = (entry.Count + 1, entry.First, mark);
                }
                else if (watchHits.Count < 500)
                {
                    watchHits[address] = (1, mark, mark);
                }
            };

            int executed = 0;
            bool breakHit = false;
            if (breakAddress is uint breakEip)
            {
                int breakAdvance = 0;
                while (executed < steps && !machine.Cpu.Context.Halted)
                {
                    if (machine.Cpu.Context.Eip == breakEip)
                    {
                        break;
                    }

                    machine.Cpu.Step();
                    executed++;

                    if (++breakAdvance >= 4096)
                    {
                        machine.Advance((ulong)breakAdvance);
                        breakAdvance = 0;
                    }
                }

                if (breakAdvance > 0)
                {
                    machine.Advance((ulong)breakAdvance);
                }

                Console.WriteLine($"Break: EIP=0x{machine.Cpu.Context.Eip:X8} after {executed} instruction(s)");
                breakHit = machine.Cpu.Context.Eip == breakEip;
            }

            if (breakAddress is null && trace > 0 && traceStart > 0)
            {
                executed += machine.Run(Math.Min(traceStart, steps));
            }

            int advanceCounter = 0;
            for (int i = 0; i < trace && !machine.Cpu.Context.Halted && executed < steps; i++)
            {
                DecodedInstruction instruction = Trace(machine, print: true, traceRegs);
                machine.Cpu.Step();
                executed++;

                if (++advanceCounter >= 4096)
                {
                    machine.Advance((ulong)advanceCounter);
                    advanceCounter = 0;
                }

                if (instruction.Mnemonic is Mnemonic.In or Mnemonic.Out)
                {
                    uint port = instruction.Operand1.Kind == OperandKind.Register
                        ? machine.Cpu.Context.Edx & 0xFFFF
                        : instruction.Operand1.Immediate;
                    Console.WriteLine($"    {(instruction.Mnemonic == Mnemonic.In ? "IN " : "OUT")} port=0x{port:X4} value=0x{machine.Cpu.Context.Eax & 0xFFFF:X4}");
                }
            }

            if (!breakHit && executed < steps)
            {
                executed += machine.Run(steps - executed);
            }

            PrintState(machine, executed, dumps);
            foreach (string pattern in scans)
            {
                ScanRam(machine, pattern);
            }
            var watchKeys = new System.Collections.Generic.List<uint>(watchHits.Keys);
            watchKeys.Sort();
            foreach (uint key in watchKeys)
            {
                var entry = watchHits[key];
                Console.WriteLine($"Watch: W{key:X8} n={entry.Count} first={entry.First} last={entry.Last}");
            }

            return 0;
        }
        catch (UnsupportedInstructionException exception)
        {
            Console.Error.WriteLine($"Unsupported instruction: {exception.Message}");
            if (machine is not null)
            {
                PrintState(machine, 0, dumps);
            }

            return 2;
        }
        catch (MemoryAccessException exception)
        {
            Console.Error.WriteLine($"Memory error: {exception.Message}");
            return 3;
        }
        catch (UnhandledGuestException exception)
        {
            Console.Error.WriteLine($"Unhandled guest exception: {exception.Message}");
            if (machine is not null)
            {
                PrintState(machine, 0, dumps);
            }

            return 5;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot read firmware: {exception.Message}");
            return 4;
        }
    }

    private static DecodedInstruction Trace(XboxMachine machine, bool print, bool traceRegs)
    {
        XboxContext context = machine.Cpu.Context;
        uint linear = context.GetSegmentBase(SegmentRegister.Cs) + context.Eip;
        Span<byte> bytes = stackalloc byte[15];
        uint firstPhysical = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            uint physical = machine.Cpu.Mmu.Translate(linear + (uint)i, isWrite: false, isInstruction: true, out _, out _, sideEffects: false);
            if (i == 0)
            {
                firstPhysical = physical;
            }

            bytes[i] = machine.Memory.Read8(physical);
        }

        DecodedInstruction instruction = Decoder.Decode(bytes, context.DefaultOperandSize, context.DefaultAddressSize);
        if (!print)
        {
            return instruction;
        }

        string hex = string.Empty;
        for (int i = 0; i < Math.Min(instruction.Length, 8); i++)
        {
            hex += $" {bytes[i]:X2}";
        }

        Console.WriteLine(
            $"{context.Eip:X8} P={firstPhysical:X8} CS={context.GetSegmentSelector(SegmentRegister.Cs):X4} {instruction.Mnemonic,-12}{hex}");

        if (traceRegs)
        {
            Console.WriteLine(
                $"    EAX={context.Eax:X8} EBX={context.Ebx:X8} ECX={context.Ecx:X8} EDX={context.Edx:X8} " +
                $"ESI={context.Esi:X8} EDI={context.Edi:X8} EBP={context.Ebp:X8} ESP={context.Esp:X8}");
        }

        return instruction;
    }

    private static void PrintState(XboxMachine machine, int executed, IReadOnlyList<(uint Address, int Length, bool Virtual)> dumps)
    {
        XboxContext context = machine.Cpu.Context;
        Console.WriteLine($"Executed {executed} instruction(s)");
        Console.WriteLine($"EIP=0x{context.Eip:X8} Halted={context.Halted} CR0=0x{context.Cr0:X8} EFLAGS=0x{context.Eflags:X8}");
        Console.WriteLine($"EAX=0x{context.Eax:X8} EBX=0x{context.Ebx:X8} ECX=0x{context.Ecx:X8} EDX=0x{context.Edx:X8}");
        Console.WriteLine($"ESI=0x{context.Esi:X8} EDI=0x{context.Edi:X8} EBP=0x{context.Ebp:X8} ESP=0x{context.Esp:X8}");
        Console.WriteLine($"CR3=0x{context.Cr3:X8} IDT=0x{context.Idtr.Base:X8}/{context.Idtr.Limit:X4} GDT=0x{context.Gdtr.Base:X8}/{context.Gdtr.Limit:X4}");
        Console.WriteLine($"OverlayEnabled={machine.Bus.RomOverlayEnabled}");
        Console.WriteLine(
            $"PIC master base=0x{machine.Bus.Pic.MasterBase:X2} mask=0x{machine.Bus.Pic.MasterMask:X2} " +
            $"slaveMask=0x{machine.Bus.Pic.SlaveMask:X2} " +
            $"req=0x{machine.Bus.Pic.MasterRequest:X2} isr=0x{machine.Bus.Pic.MasterInService:X2}");
        Console.WriteLine(
            $"PIT ch0 divisor={machine.Bus.Pit.Channel0Divisor} mode={machine.Bus.Pit.Channel0Mode}");
        Console.WriteLine($"Interrupts delivered={machine.Cpu.InterruptCount}");
        Console.WriteLine(
            $"IDE accesses={machine.Ide.PrimaryMaster.AccessCount} " +
            $"lastCmd=0x{machine.Ide.PrimaryMaster.LastCommand:X2}");
        Console.WriteLine(
            $"RTC accesses={machine.Bus.Rtc.AccessCount} pie={machine.Bus.Rtc.PeriodicEnabled}");
        var portLog = new System.Text.StringBuilder();
        foreach (var entry in machine.Bus.Io.PortAccessCounts)
        {
            portLog.Append($" {entry.Key:X4}={entry.Value}");
        }

        Console.WriteLine($"Port accesses:{portLog}");
        var unmappedBuckets = new System.Collections.Generic.List<uint>(machine.Memory.UnmappedAccessCounts.Keys);
        unmappedBuckets.Sort();
        var unmappedLog = new System.Text.StringBuilder();
        foreach (uint bucket in unmappedBuckets)
        {
            unmappedLog.Append($" {bucket:X3}={machine.Memory.UnmappedAccessCounts[bucket]}");
        }

        Console.WriteLine($"Unmapped phys (1MB buckets):{unmappedLog}");
        var configKeys = new System.Collections.Generic.List<ushort>(machine.Bus.Pci.ConfigAccessCounts.Keys);
        configKeys.Sort();
        var configLog = new System.Text.StringBuilder();
        foreach (ushort key in configKeys)
        {
            configLog.Append($" {key:X4}={machine.Bus.Pci.ConfigAccessCounts[key]}");
        }

        Console.WriteLine($"PCI cfg (bus+devfn):{configLog}");
        var barLog = new System.Text.StringBuilder();
        int[] barSlots = { 0x10, 0x18, 0x20, 0x28, 0x30, 0x48 };
        foreach (int devfn in barSlots)
        {
            uint cmdAddr = XGrid.Bus.Pci.PciBus.MakeConfigAddress((byte)(devfn >> 3), 0, 0x04);
            uint barAddr = XGrid.Bus.Pci.PciBus.MakeConfigAddress((byte)(devfn >> 3), 0, 0x10);
            barLog.Append(
                $" {devfn:X2}:cmd={machine.Bus.Pci.ReadConfig(cmdAddr, 2):X4}," +
                $"bar0={machine.Bus.Pci.ReadConfig(barAddr, 4):X8}");
        }

        uint nvCmdAddr = XGrid.Bus.Pci.PciBus.MakeConfigAddress(0, 0, 0x04) | (1u << 16);
        uint nvBarAddr = XGrid.Bus.Pci.PciBus.MakeConfigAddress(0, 0, 0x10) | (1u << 16);
        barLog.Append(
            $" NV1:cmd={machine.Bus.Pci.ReadConfig(nvCmdAddr, 2):X4}," +
            $"bar0={machine.Bus.Pci.ReadConfig(nvBarAddr, 4):X8}");
        uint intRoute = machine.Bus.Pci.ReadConfig(
            XGrid.Bus.Pci.PciBus.MakeConfigAddress(1, 0, 0x6C), 4);
        uint pirqRoute = machine.Bus.Pci.ReadConfig(
            XGrid.Bus.Pci.PciBus.MakeConfigAddress(1, 0, 0x68), 4);
        uint acpiRoute = machine.Bus.Pci.ReadConfig(
            XGrid.Bus.Pci.PciBus.MakeConfigAddress(1, 0, 0x64), 4);
        barLog.Append($" introute={intRoute:X8} pirq={pirqRoute:X8} acpi={acpiRoute:X8}");

        Console.WriteLine($"PCI state:{barLog}");
        var smcLog = new System.Text.StringBuilder();
        foreach (var entry in machine.Bus.Smc.ReadCounts)
        {
            smcLog.Append($" R{entry.Key:X2}={entry.Value}");
        }

        foreach (var entry in machine.Bus.Smc.WriteCounts)
        {
            smcLog.Append($" W{entry.Key:X2}={entry.Value}");
        }

        Console.WriteLine($"SMC cmds:{smcLog}");
        Console.WriteLine(
            $"SMBus txns={machine.Bus.Smbus.TransactionCount} " +
            $"last={machine.Bus.Smbus.LastSlaveAddress:X2}:{(machine.Bus.Smbus.LastRead ? "R" : "W")}" +
            $"p{machine.Bus.Smbus.LastProtocol} cmd={machine.Bus.Smbus.LastCommand:X2} " +
            $"ok={machine.Bus.Smbus.LastOk} stat={machine.Bus.Smbus.Status:X2} ctl={machine.Bus.Smbus.Control:X2}");
        var txnLog = new System.Text.StringBuilder();
        foreach (var txn in machine.Bus.Smbus.RecentTransactions)
        {
            txnLog.Append($" {txn.SlaveAddress:X2}:{(txn.Read ? "R" : "W")}p{txn.Protocol}:{txn.Command:X2}={txn.Data0:X2}{txn.Data1:X2}{(txn.Ok ? string.Empty : "!")}");
        }

        Console.WriteLine($"SMBus log:{txnLog}");
        var nv2aLog = new System.Text.StringBuilder();
        foreach (var entry in machine.Nv2a.ReadCounts)
        {
            nv2aLog.Append($" R{entry.Key:X6}={entry.Value}");
        }

        foreach (var entry in machine.Nv2a.WriteCounts)
        {
            nv2aLog.Append($" W{entry.Key:X6}={entry.Value}");
        }

        Console.WriteLine($"NV2A regs:{nv2aLog}");

        foreach ((uint address, int length, bool virtualDump) in dumps)
        {
            for (int offset = 0; offset < length; offset += 16)
            {
                string hex = string.Empty;
                for (int i = 0; i < 16 && offset + i < length; i++)
                {
                    uint linear = address + (uint)offset + (uint)i;
                    if (virtualDump)
                    {
                        uint physical = machine.Cpu.Mmu.Translate(linear, isWrite: false, isInstruction: false, out bool faulted, out _, sideEffects: false);
                        hex += faulted ? " ??" : $" {machine.Memory.Read8(physical):X2}";
                    }
                    else
                    {
                        hex += $" {machine.Memory.Read8(linear):X2}";
                    }
                }

                Console.WriteLine($"{address + (uint)offset:X8}:{hex}");
            }
        }
    }

    private static void ScanRam(XboxMachine machine, string hex)
    {
        if ((hex.Length & 1) != 0)
        {
            Console.Error.WriteLine($"Ignoring odd-length scan pattern: {hex}");
            return;
        }

        byte[] pattern = new byte[hex.Length / 2];
        for (int i = 0; i < pattern.Length; i++)
        {
            pattern[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        Span<byte> ram = machine.Ram.GetSpan(0, (int)machine.Ram.Size);
        int hits = 0;
        for (int i = 0; i + pattern.Length <= ram.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (ram[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                Console.WriteLine($"Scan {hex} hit at P={(uint)i:X8}");
                if (++hits >= 48)
                {
                    Console.WriteLine($"Scan {hex} hit cap reached (48); stopping.");
                    return;
                }
            }
        }

        Console.WriteLine($"Scan {hex} complete: {hits} hit(s).");
    }

    private static string NextValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {args[index]}");
        }

        return args[++index];
    }

    private static void PrintUsage()
    {
        Console.WriteLine("XGrid - original Xbox emulator");
        Console.WriteLine();
        Console.WriteLine("Usage: xgrid <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  boot --bios <path> [--mcpx <path>] [--eeprom <path>] [--hdd <path>] [--steps N] [--trace N] [--trace-start N] [--dump addr[,len]] [--ram MB]");
        Console.WriteLine("                   Load firmware and execute up to N instructions");
        Console.WriteLine("  --help, -h       Show this help");
        Console.WriteLine("  --version, -v    Show the version");
    }
}
