using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using XGrid.Bus.Net;
using XGrid.Bus.Smbus;
using XGrid.Bus.Usb;
using XGrid.Core.Diagnostics;
using XGrid.Cpu;
using XGrid.Graphics;
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
            case "testrom":
                return TestRom(args);
            case "fatx":
                return Fatx(args);
            case "xdvdfs":
                return Xdvdfs(args);
            case "sector":
                return Sector(args);
            case "xbe":
                return Xbe(args);
            case "disc":
                return Disc(args);
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
        string? dvdPath = null;
        ulong steps = 1_000_000;
        int trace = 0;
        int traceStart = 0;
        bool traceRegs = false;
        uint? breakAddress = null;
        List<uint> breakLogAddresses = [];
        uint ramMb = 64;
        List<(uint Address, int Length, bool Virtual)> dumps = [];
        string? fbPath = null;
        string? framePath = null;
        string? frameScanoutPath = null;
        string? frameDisplayPath = null;
        string? frameFirstPath = null;
        bool frameFirstSaved = false;
        string? pgraphPath = null;
        ulong pgraphMax = 262144;
        ulong eipHistInterval = 0;
        string? ramDumpPath = null;
        string? raminPath = null;
        string? eepromOutPath = null;
        string? saveStatePath = null;
        ulong saveAt = 0;
        bool hasSaveAt = false;
        string? loadStatePath = null;
        string? writeDumpDir = null;
        string? padButtons = null;
        string? padAnalog = null;
        string? padSticks = null;
        ulong padStep = 0;
        string? padScriptSpec = null;
        string? netBackend = null;
        string? netInjectHex = null;
        string? netLink = null;
        bool noEosQuirk = false;
        bool idleSkip = false;
        bool idleSkipProbe = false;
        string texFilter = "legacy";
        bool fixedFunction = false;
        bool fetchFast = false;
        bool decodeCache = true;
        bool decodeVerify = false;
        bool blockExec = false;
        bool blockVerify = false;
        bool specExec = false;
        bool emit = false;
        bool emitInline = false;
        bool emitDiag = false;
        bool shapeTally = false;
        bool jit = false;
        bool jitExact = false;
        bool jitTally = false;
        bool jitMmio = false;
        bool jitSpin = false;
        bool jitRaw = false;
        bool rawShapes = false;
        bool jitTiming = false;
        int jitThreshold = 8;
        int jitSlice = 256;
        bool bench = false;
        string? benchName = null;
        string gpuBackend = "sw";
        bool gpuTime = false;
        bool windowMode = false;
        bool gpuReplace = false;
        string? hotspotPath = null;
        List<string> scans = [];
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
                case "--dvd":
                    dvdPath = NextValue(args, ref i);
                    break;
                case "--steps":
                    steps = ulong.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
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
                case "--break-log":
                    {
                        string text = NextValue(args, ref i);
                        string hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
                        breakLogAddresses.Add(Convert.ToUInt32(hex, 16));
                        break;
                    }
                case "--dump":
                    {
                        string[] parts = NextValue(args, ref i).Split(',');
                        string addressText = parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? parts[0][2..]
                            : parts[0];
                        uint address = Convert.ToUInt32(addressText, 16);
                        int length = parts.Length > 1 ? Convert.ToInt32(parts[1], 16) : 0x40;
                        dumps.Add((address, length, false));
                        break;
                    }
                case "--dumpv":
                    {
                        string[] parts = NextValue(args, ref i).Split(',');
                        string addressText = parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? parts[0][2..]
                            : parts[0];
                        uint address = Convert.ToUInt32(addressText, 16);
                        int length = parts.Length > 1 ? Convert.ToInt32(parts[1], 16) : 0x40;
                        dumps.Add((address, length, true));
                        break;
                    }
                case "--ram":
                    ramMb = uint.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--fb":
                    fbPath = NextValue(args, ref i);
                    break;
                case "--frame":
                    framePath = NextValue(args, ref i);
                    break;
                case "--frame-scanout":
                    frameScanoutPath = NextValue(args, ref i);
                    break;
                case "--frame-display":
                    frameDisplayPath = NextValue(args, ref i);
                    break;
                case "--frame-first":
                    frameFirstPath = NextValue(args, ref i);
                    break;
                case "--pgraph":
                    pgraphPath = NextValue(args, ref i);
                    break;
                case "--pgraph-max":
                    pgraphMax = ulong.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--eip-hist":
                    eipHistInterval = ulong.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--ramdump":
                    ramDumpPath = NextValue(args, ref i);
                    break;
                case "--ramin":
                    raminPath = NextValue(args, ref i);
                    break;
                case "--eeprom-out":
                    eepromOutPath = NextValue(args, ref i);
                    break;
                case "--save-state":
                    saveStatePath = NextValue(args, ref i);
                    break;
                case "--save-at":
                    saveAt = ulong.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    hasSaveAt = true;
                    break;
                case "--load-state":
                    loadStatePath = NextValue(args, ref i);
                    break;
                case "--writedump":
                    writeDumpDir = NextValue(args, ref i);
                    break;
                case "--pad-buttons":
                    padButtons = NextValue(args, ref i);
                    break;
                case "--pad-analog":
                    padAnalog = NextValue(args, ref i);
                    break;
                case "--pad-sticks":
                    padSticks = NextValue(args, ref i);
                    break;
                case "--pad-step":
                    padStep = ulong.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--pad-script":
                    padScriptSpec = NextValue(args, ref i);
                    break;
                case "--net-backend":
                    netBackend = NextValue(args, ref i);
                    break;
                case "--net-inject":
                    netInjectHex = NextValue(args, ref i);
                    break;
                case "--net-link":
                    netLink = NextValue(args, ref i);
                    break;
                case "--no-007-eos-quirk":
                    noEosQuirk = true;
                    break;
                case "--idle-skip":
                    idleSkip = true;
                    break;
                case "--idle-skip-probe":
                    idleSkipProbe = true;
                    break;
                case "--ffp":
                    fixedFunction = true;
                    break;
                case "--tex-filter":
                    texFilter = NextValue(args, ref i);
                    if (texFilter is not ("legacy" or "registers"))
                    {
                        Console.Error.WriteLine($"Invalid --tex-filter '{texFilter}' (expected legacy|registers)");
                        return 1;
                    }

                    break;
                case "--fetch-fast":
                    fetchFast = true;
                    break;
                case "--block-exec":
                    blockExec = true;
                    break;
                case "--block-verify":
                    blockVerify = true;
                    break;
                case "--spec-exec":
                    specExec = true;
                    break;
                case "--emit":
                    emit = true;
                    break;
                case "--emit-inline":
                    emitInline = true;
                    break;
                case "--emit-diag":
                    emitDiag = true;
                    break;
                case "--shape-tally":
                    shapeTally = true;
                    break;
                case "--jit":
                    jit = true;
                    break;
                case "--jit-exact":
                    jitExact = true;
                    break;
                case "--jit-mmio":
                    jitMmio = true;
                    break;
                case "--jit-spin":
                    jitSpin = true;
                    break;
                case "--jit-raw":
                    jitRaw = true;
                    break;
                case "--raw-shapes":
                    rawShapes = true;
                    break;
                case "--jit-timing":
                    jitTiming = true;
                    break;
                case "--jit-tally":
                    jitTally = true;
                    break;
                case "--jit-slice":
                    jitSlice = int.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--jit-threshold":
                    jitThreshold = int.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--bench":
                    bench = true;
                    break;
                case "--bench-name":
                    benchName = NextValue(args, ref i);
                    break;
                case "--decode-cache":
                    decodeCache = true;
                    break;
                case "--no-decode-cache":
                    decodeCache = false;
                    break;
                case "--decode-verify":
                    decodeVerify = true;
                    break;
                case "--gpu":
                    gpuBackend = NextValue(args, ref i);
                    break;
                case "--window":
                    windowMode = true;
                    break;
                case "--gpu-replace":
                    gpuReplace = true;
                    break;
                case "--gpu-time":
                    gpuTime = true;
                    break;
                case "--hotspot":
                    hotspotPath = NextValue(args, ref i);
                    break;
                case "--scan":
                    scans.Add(NextValue(args, ref i));
                    break;
                case "--watch":
                    {
                        string[] parts = NextValue(args, ref i).Split(',');
                        string startText = parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? parts[0][2..]
                            : parts[0];
                        string endText = parts[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? parts[1][2..]
                            : parts[1];
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

        if (hasSaveAt && saveStatePath is null)
        {
            Console.Error.WriteLine("--save-at requires --save-state <path>");
            return 1;
        }

        XboxMachine? machine = null;
        try
        {
            machine = new XboxMachine(ramMb * 1024 * 1024);
            machine.LoadBios(
                File.ReadAllBytes(biosPath),
                mcpxPath is null ? null : File.ReadAllBytes(mcpxPath));
            if (eepromPath is not null)
            {
                machine.LoadEeprom(File.ReadAllBytes(eepromPath));
            }

            if (hddPath is not null)
            {
                machine.AttachHdd(hddPath);
            }

            if (dvdPath is not null)
            {
                machine.AttachDvd(dvdPath);
            }

            machine.Reset();
            machine.Nv2A.MethodStreamCap = pgraphMax;
            // The per-method histogram (two dictionary updates per PGRAPH
            // method) is a diagnostic no CLI path consumes: the method dump
            // reads MethodStream. Dropping it removes ~18M dictionary updates
            // per 1B menu steps (Track D, measured).
            machine.Nv2A.CollectMethodHistogram = false;
            machine.SevenEosQuirkEnabled = !noEosQuirk;
            machine.Nv2A.TextureFilterRegisters = string.Equals(texFilter, "registers", StringComparison.Ordinal);
            machine.Nv2A.FixedFunctionTransform = fixedFunction;
            Console.WriteLine(
                $"007 EOS quirk={(machine.SevenEosQuirkEnabled ? "on" : "off")} (TitleId 0x4541000D EOS 2->0)");
            if (fixedFunction)
            {
                Console.WriteLine(
                    "Fixed-function vertex stage=on (SET_TRANSFORM_EXECUTION_MODE FIXED draws; lighting falls back to unlit)");
            }

            if (!string.Equals(texFilter, "legacy", StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"Texture filter={texFilter} (MIN/MAG tent at LOD 0; convolution only when MIN is CONVOLUTION_2D_LOD0)");
            }

            if (!string.Equals(gpuBackend, "sw", StringComparison.Ordinal) &&
                !string.Equals(gpuBackend, "gl", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Invalid --gpu '{gpuBackend}' (expected sw|gl)");
                return 1;
            }

            if (windowMode && !string.Equals(gpuBackend, "gl", StringComparison.Ordinal))
            {
                Console.WriteLine("Window mode implies the GL backend (--window => --gpu gl)");
                gpuBackend = "gl";
            }

            if (gpuReplace && !string.Equals(gpuBackend, "gl", StringComparison.Ordinal))
            {
                Console.WriteLine("Replacement mode implies the GL backend (--gpu-replace => --gpu gl)");
                gpuBackend = "gl";
            }

            if (string.Equals(gpuBackend, "gl", StringComparison.Ordinal))
            {
                // Slice-1 GPU backend: probe-gated; on failure fall back to
                // software for the session (Backend stays null). The per-flip
                // CPU readback is opt-in (--frame): a flip stays on the GPU,
                // which is the Track-B fast-mode present path. The backend
                // reads through the machine's own GPU-memory adapter, so
                // texture reads and the present-upload dirty watch are the
                // exact lanes the device uses.
                Nv2a.IGpuMemory? gpuMemory = machine.Nv2A.GpuMemory;
                string gpuWhy = "the machine exposes no GPU memory adapter";
                Nv2a.IPgraphBackend? gpuBackendInstance = null;
                bool gpuCreated = gpuMemory is not null &&
                    GpuGl.GlBackendFactory.TryCreate(gpuMemory, windowMode, gpuReplace, framePath is not null || frameFirstPath is not null,
                        out gpuBackendInstance, out gpuWhy);
                if (gpuCreated)
                {
                    machine.Nv2A.Backend = gpuBackendInstance;
                    Console.WriteLine(
                        $"GPU backend=gl {(gpuReplace ? "replace=on " : string.Empty)}{gpuWhy} " +
                        $"readback={(framePath is not null || frameFirstPath is not null ? "on" : "off")}");
                }
                else
                {
                    Console.WriteLine($"GPU backend=sw (gl probe failed: {gpuWhy})");
                }
            }

            if (idleSkip || idleSkipProbe)
            {
                machine.Cpu.IdleSkipEnabled = true;
                machine.Cpu.IdleSkipMeasureOnly = idleSkipProbe && !idleSkip;
                Console.WriteLine(
                    $"Idle-skip={(machine.Cpu.IdleSkipMeasureOnly ? "probe" : "on")} " +
                    $"minIterations={machine.Cpu.IdleSkipMinIterations} chunk={machine.Cpu.IdleSkipChunkSteps} " +
                    "(absolute-step recipes: use flag-off runs)");
            }

            if (gpuTime)
            {
                machine.Nv2A.RasterTimingEnabled = true;
                Console.WriteLine("GPU raster timing=on (host-only diagnostic; no guest state change)");
            }

            HotspotProfiler? hotspot = null;
            if (hotspotPath is not null)
            {
                hotspot = new HotspotProfiler();
                machine.Cpu.HotspotObserver = hotspot;
                Console.WriteLine($"Hotspot profiler on (report: {hotspotPath})");
            }

            if (fetchFast)
            {
                machine.Cpu.FetchFastPathEnabled = true;
                Console.WriteLine(
                    "Fetch fast path=on (single-translate fetch; absolute-step recipes: use flag-off runs)");
            }

            if (decodeCache || decodeVerify || blockExec || blockVerify || specExec || emit || shapeTally || jit)
            {
                machine.Cpu.DecodeCacheEnabled = true;
                Console.WriteLine(
                    $"Decode cache=on (entryCap={DecodeCache.MaxEntries}; implies fetch-fast; " +
                    "absolute-step recipes: use flag-off runs)");
                if (decodeVerify)
                {
                    machine.Cpu.DecodeCacheVerify = true;
                    Console.WriteLine("Decode cache verify=on (re-reads source bytes on every hit)");
                }
            }

            if (blockExec || blockVerify || specExec || emit || shapeTally)
            {
                machine.Cpu.BlockExecutionEnabled = true;
                machine.Cpu.BlockVerify = blockVerify;
                if (hotspot is not null)
                {
                    hotspot.Blocks = machine.Cpu.BlockStatistics;
                }

                Console.WriteLine($"Block execution=on (verify={blockVerify}; per-instruction boundaries retained)");
            }

            if (specExec || emit || shapeTally)
            {
                machine.Cpu.SpecExecutionEnabled = true;
                Console.WriteLine(
                    "Spec execution=on (pre-specialized shapes; implies block-exec; per-instruction boundaries retained)");
            }

            if (shapeTally)
            {
                machine.Cpu.SpecStatistics!.ShapeTallyEnabled = true;
                Console.WriteLine("Shape tally=on (served-shape histogram; diagnostic only)");
            }

            if (emit || emitInline || emitDiag)
            {
                if (shapeTally)
                {
                    // §175 E3-0: the tally measures executed shapes on the
                    // member path; emitted tails bypass TryNext, so emission
                    // stays off while tallying to keep the histogram exact.
                    Console.WriteLine("Emit=suppressed (--shape-tally measures the member path)");
                }
                else if (emitInline || emitDiag)
                {
                    machine.Cpu.EmitInlineEnabled = true;
                    Console.WriteLine(
                        "Emit=on (DynamicMethod block tails; implies spec-exec; per-instruction boundaries retained)");
                    Console.WriteLine("Emit inline bodies=on (E3-1 lean memory shapes)");
                    if (emitDiag)
                    {
                        machine.Cpu.EmitInlineCountersEnabled = true;
                        Console.WriteLine("Emit inline diag=on (fast-path counters; diagnostic only)");
                    }
                }
                else
                {
                    machine.Cpu.EmitEnabled = true;
                    Console.WriteLine(
                        "Emit=on (DynamicMethod block tails; implies spec-exec; per-instruction boundaries retained)");
                }
            }

            if (jit)
            {
                machine.Cpu.JitBuildThreshold = jitThreshold;
                machine.Cpu.JitExact = jitExact;
                machine.Cpu.JitTally = jitTally;
                machine.Cpu.JitMmioFast = jitMmio;
                machine.Cpu.JitSpinSkip = jitSpin;
                machine.Cpu.JitRawBlocks = jitRaw;
                machine.Cpu.RawShapeTally = rawShapes && jitRaw;
                machine.Cpu.JitTiming = jitTiming;
                machine.Cpu.JitSpecSlice = jitSlice;
                machine.Cpu.JitEnabled = true;
                Console.WriteLine(
                    $"Jit=on (fast-mode block engine; whole-block native serving; " +
                    $"poll={(jitExact ? "per-member(exact)" : "per-block")}; threshold={jitThreshold}; slice={jitSlice}" +
                    (jitTally ? "; tally=on" : string.Empty) +
                    (jitMmio ? "; mmio=on" : string.Empty) +
                    (jitSpin ? "; spin=on" : string.Empty) +
                    (jitTiming ? "; timing=on" : string.Empty) +
                    "; absolute-step recipes: use flag-off runs)");
            }

            if (loadStatePath is not null)
            {
                try
                {
                    machine.LoadState(loadStatePath);
                }
                catch (InvalidDataException exception)
                {
                    Console.Error.WriteLine($"Cannot load snapshot: {exception.Message}");
                    return 6;
                }
                catch (EndOfStreamException)
                {
                    Console.Error.WriteLine($"Cannot load snapshot: {loadStatePath} is truncated.");
                    return 6;
                }

                Console.WriteLine(
                    $"Loaded state {loadStatePath} EIP=0x{machine.Cpu.Context.Eip:X8}");
            }

            if (netBackend is not null)
            {
                if (string.Equals(netBackend, "socket", StringComparison.Ordinal))
                {
                    machine.Net.Backend = new SocketNetworkBackend();
                    Console.WriteLine("NVNET backend=socket (user-mode ARP/ICMP/UDP)");
                }
                else if (!string.Equals(netBackend, "loopback", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Invalid --net-backend '{netBackend}' (expected loopback|socket)");
                    return 1;
                }
            }

            if (netLink is not null)
            {
                if (string.Equals(netLink, "down", StringComparison.Ordinal))
                {
                    machine.Net.SetLinkUp(false);
                    Console.WriteLine("NVNET link=down");
                }
                else if (!string.Equals(netLink, "up", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Invalid --net-link '{netLink}' (expected up|down)");
                    return 1;
                }
            }

            PadInput? padInput = null;
            if (padButtons is not null || padAnalog is not null || padSticks is not null)
            {
                if (!PadInput.TryParse(padButtons, padAnalog, padSticks, out PadInput pad))
                {
                    Console.Error.WriteLine(
                        "Invalid --pad options (buttons: hex16 or names like down/a+down; analog: 16 hex chars; sticks: lx,ly,rx,ry)");
                    return 1;
                }

                padInput = pad;
                if (padStep == 0)
                {
                    machine.Gamepad.SetInput(pad.Buttons, pad.Analog, pad.Sticks);
                    Console.WriteLine($"Pad buttons=0x{pad.Buttons:X4} analog={BitConverter.ToString(pad.Analog)}");
                }
            }

            bool padApplied = padInput is null || padStep == 0;

            PadScript? padScript = null;
            if (padScriptSpec is not null)
            {
                if (!PadScript.TryParse(padScriptSpec, out PadScript? parsed) || parsed is null)
                {
                    Console.Error.WriteLine(
                        "Invalid --pad-script spec (events: buttonset@step[+hold],... e.g. down@960000000,a@961000000)");
                    return 1;
                }

                padScript = parsed;
                Console.WriteLine($"Pad script events={padScript.Count}");
            }

            // Diagnostic override for bus-watch bisection (XGRID_WATCH_START/END hex):
            // the default watch is the kernel's debug-buffer range, but a differential
            // investigation needs to point it at the diverging address instead.
            if (uint.TryParse(
                    Environment.GetEnvironmentVariable("XGRID_WATCH_START"),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out uint watchStartOverride))
            {
                machineWatchStart = watchStartOverride;
            }

            if (uint.TryParse(
                    Environment.GetEnvironmentVariable("XGRID_WATCH_END"),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out uint watchEndOverride))
            {
                machineWatchEnd = watchEndOverride;
            }

            bool watchTrace = string.Equals(
                Environment.GetEnvironmentVariable("XGRID_WATCH_TRACE"),
                "1",
                StringComparison.Ordinal);

            machine.Memory.WatchStart = machineWatchStart;
            machine.Memory.WatchEnd = machineWatchEnd;
            var watchHits = new Dictionary<uint, (int Count, string First, string Last)>();
            machine.Memory.WatchHit += (address, size, value) =>
            {
                string mark = $"{machine.Cpu.Context.Eip:X8}:{size}={value:X}";
                if (watchTrace)
                {
                    Console.WriteLine(
                        $"WHIT tsc={machine.Cpu.Context.Tsc} eip={machine.Cpu.Context.Eip:X8} " +
                        $"addr={address:X8} size={size} value={value:X8}");
                }

                if (watchHits.TryGetValue(address, out var entry))
                {
                    watchHits[address] = (entry.Count + 1, entry.First, mark);
                }
                else if (watchHits.Count < 500)
                {
                    watchHits[address] = (1, mark, mark);
                }
            };

            ulong executed = 0;
            bool breakHit = false;
            System.Diagnostics.Stopwatch? benchWatch =
                bench ? System.Diagnostics.Stopwatch.StartNew() : null;
            if (breakAddress is null && breakLogAddresses.Count > 0)
            {
                int logAdvance = 0;
                while (executed < steps && !machine.Cpu.Context.Halted)
                {
                    ApplyPadIfDue(executed);
                    ApplyScriptIfDue(executed);
                    if (breakLogAddresses.Contains(machine.Cpu.Context.Eip))
                    {
                        XboxContext logs = machine.Cpu.Context;
                        uint caller = 0;
                        uint argument = 0;
                        uint espPhysical = machine.Cpu.Mmu.Translate(
                            logs.Esp, isWrite: false, isInstruction: false, out bool logFaulted, out _);
                        if (!logFaulted)
                        {
                            caller = machine.Memory.Read32(espPhysical);
                            argument = machine.Memory.Read32(espPhysical + 4);
                        }

                        Console.WriteLine(
                            $"HIT {executed} EIP={logs.Eip:X8} ESI={logs.Esi:X8} EDI={logs.Edi:X8} EAX={logs.Eax:X8} " +
                            $"EFLAGS={logs.Eflags:X8} ESP={logs.Esp:X8} " +
                            $"caller={caller:X8} arg={argument:X8}");
                    }

                    machine.Cpu.Step();
                    executed++;

                    if (++logAdvance >= 4096)
                    {
                        machine.Advance((ulong)logAdvance);
                        logAdvance = 0;
                    }
                }

                if (logAdvance > 0)
                {
                    machine.Advance((ulong)logAdvance);
                }
            }
            else if (breakAddress is { } breakEip)
            {
                int breakAdvance = 0;
                while (executed < steps && !machine.Cpu.Context.Halted)
                {
                    ApplyPadIfDue(executed);
                    ApplyScriptIfDue(executed);
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
                executed += RunBatch(machine, Math.Min((ulong)traceStart, steps));
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
                    Console.WriteLine(
                        $"    {(instruction.Mnemonic == Mnemonic.In ? "IN " : "OUT")} port=0x{port:X4} value=0x{machine.Cpu.Context.Eax & 0xFFFF:X4}");
                }
            }

            const ulong progressInterval = 250_000_000;
            ulong nextProgress = (executed / progressInterval + 1) * progressInterval;
            EipHistogram? eipHist = eipHistInterval > 0 ? new EipHistogram() : null;
            ulong sinceHist = 0;
            ulong prevEosFires = machine.SevenEosQuirkFires;
            ApplyScriptIfDue(executed);
            while (!breakHit && executed < steps && !machine.Cpu.Context.Halted
                   && !(hasSaveAt && executed >= saveAt))
            {
                ulong remaining = steps - executed;
                ulong target = Math.Min(remaining, nextProgress - executed);
                if (eipHist is not null)
                {
                    target = Math.Min(target, eipHistInterval - sinceHist);
                }

                if (!padApplied && padStep > executed)
                {
                    target = Math.Min(target, padStep - executed);
                }

                if (padScript is { } splitter && splitter.TryGetNextBoundary(executed, out ulong scriptStep))
                {
                    target = Math.Min(target, scriptStep - executed);
                }

                if (hasSaveAt && saveAt > executed)
                {
                    target = Math.Min(target, saveAt - executed);
                }

                if (windowMode)
                {
                    // Live window: keep batches short so the message pump runs
                    // at ~100 ms granularity (host-side scheduling only; guest
                    // instruction boundaries are unchanged).
                    target = Math.Min(target, 2_000_000);
                }

                if (frameFirstPath is not null && !frameFirstSaved)
                {
                    // First-frame anchor: the flip happens inside a batch, so
                    // cap the batch while hunting it (2M-step anchor resolution
                    // instead of the 250M progress interval; negligible cost,
                    // and only until the first frame is saved).
                    target = Math.Min(target, 2_000_000);
                }

                ulong ran = RunBatch(machine, target);
                if (ran == 0)
                {
                    break;
                }

                executed += ran;
                ApplyPadIfDue(executed);
                ApplyScriptIfDue(executed);
                if (frameFirstPath is not null && !frameFirstSaved &&
                    machine.Nv2A.FramesPresented > 0)
                {
                    // Track E harness: the first presented frame's step anchor
                    // and pixels (headless; works for sw and gl backends).
                    frameFirstSaved = true;
                    SavePresentedFrame(machine, frameFirstPath);
                    Console.WriteLine($"FrameFirst step={executed} path={frameFirstPath}");
                }

                if (windowMode)
                {
                    machine.Nv2A.Backend?.Pump();
                }
                if (machine.SevenEosQuirkFires != prevEosFires)
                {
                    prevEosFires = machine.SevenEosQuirkFires;
                    Console.WriteLine(
                        $"007 EOS quirk fired at step {executed} EIP=0x{machine.Cpu.Context.Eip:X8} fires={prevEosFires}");
                }

                if (eipHist is not null)
                {
                    sinceHist += ran;
                    if (sinceHist >= eipHistInterval)
                    {
                        eipHist.Sample(machine.Cpu.Context.Eip);
                        sinceHist = 0;
                    }
                }

                if (executed >= nextProgress)
                {
                    Console.WriteLine(
                        $"Progress {executed} instruction(s) EIP=0x{machine.Cpu.Context.Eip:X8}");
                    nextProgress += progressInterval;
                }
            }

            if (benchWatch is not null)
            {
                benchWatch.Stop();
                double benchWall = benchWatch.Elapsed.TotalSeconds;
                double benchMsps = benchWall > 0
                    ? executed / benchWall / 1_000_000.0
                    : 0;
                string benchRamSha = Convert.ToHexString(SHA256.HashData(
                    machine.Ram.GetReadOnlySpan(0, (int)machine.Ram.Size)));
                string benchCpuSha;
                using (var benchState = new MemoryStream())
                {
                    using (var benchWriter = new BinaryWriter(benchState, System.Text.Encoding.UTF8, leaveOpen: true))
                    {
                        machine.Cpu.SaveState(benchWriter);
                    }

                    benchCpuSha = Convert.ToHexString(SHA256.HashData(
                        benchState.GetBuffer().AsSpan(0, (int)benchState.Length)));
                }

                Console.WriteLine(
                    $"BENCH name={benchName ?? "boot"} steps={executed} wall={benchWall:F3} msps={benchMsps:F2} " +
                    $"ramSha256={benchRamSha} cpuSha256={benchCpuSha}");
            }

            if (saveStatePath is not null)
            {
                try
                {
                    machine.SaveState(saveStatePath);
                }
                catch (IOException exception)
                {
                    Console.Error.WriteLine($"Cannot save snapshot: {exception.Message}");
                    return 6;
                }

                Console.WriteLine(
                    $"Saved state {saveStatePath} at step {executed} EIP=0x{machine.Cpu.Context.Eip:X8}");
                if (hasSaveAt)
                {
                    return 0;
                }
            }

            if (netInjectHex is not null)
            {
                byte[] frame;
                try
                {
                    string hex = netInjectHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? netInjectHex[2..]
                        : netInjectHex;
                    frame = Convert.FromHexString(hex);
                }
                catch (FormatException)
                {
                    Console.Error.WriteLine("Invalid --net-inject hex (expected even-length hex bytes)");
                    return 1;
                }

                bool delivered = machine.Net.InjectFrame(frame);
                Console.WriteLine(
                    $"NVNET inject len={frame.Length} delivered={delivered} " +
                    $"rx_pkts={machine.Net.RxPackets} cant_rx={machine.Net.CantReceiveCount}");
            }

            PrintState(machine, executed, dumps);
            if (hotspot is not null && hotspotPath is not null)
            {
                hotspot.WriteReport(Console.Out, 15);
                hotspot.SaveReport(hotspotPath);
                Console.WriteLine($"Hotspot report {hotspotPath} total={hotspot.TotalInstructions}");
            }

            if (ramDumpPath is not null)
            {
                File.WriteAllBytes(ramDumpPath, machine.Ram.GetSpan(0, (int)machine.Ram.Size).ToArray());
                Console.WriteLine($"RAM {ramDumpPath} {machine.Ram.Size} bytes");
            }

            if (raminPath is not null)
            {
                byte[] ramin = machine.Nv2A.PraminSnapshot();
                File.WriteAllBytes(raminPath, ramin);
                Console.WriteLine($"PRAMIN {raminPath} {ramin.Length} bytes");
            }

            if (eepromOutPath is not null)
            {
                // The guest rewrites the EEPROM through the SMBus (video mode,
                // language, region); write the live image so a later run can
                // boot with the user's settings.
                byte[] eeprom = new byte[Eeprom24C02.Size];
                machine.CopyEeprom(eeprom);
                File.WriteAllBytes(eepromOutPath, eeprom);
                Console.WriteLine($"EEPROM {eepromOutPath} {eeprom.Length} bytes");
            }

            if (fbPath is not null)
            {
                DumpFramebuffer(machine, fbPath);
            }

            if (framePath is not null)
            {
                SavePresentedFrame(machine, framePath);
            }

            if (frameScanoutPath is not null)
            {
                SaveScanoutFrame(machine, frameScanoutPath);
            }

            if (frameDisplayPath is not null)
            {
                SaveDisplayFrame(machine, frameDisplayPath);
            }

            if (pgraphPath is not null)
            {
                using var pgraphOut = new StreamWriter(pgraphPath);
                foreach ((uint subchannel, uint method, uint parameter) in machine.Nv2A.MethodStream)
                {
                    pgraphOut.Write(subchannel);
                    pgraphOut.Write(' ');
                    pgraphOut.Write(method.ToString("X4"));
                    pgraphOut.Write(' ');
                    pgraphOut.WriteLine(parameter.ToString("X8"));
                }

                Console.WriteLine($"PGRAPH {pgraphPath} methods={machine.Nv2A.MethodStream.Count}");
            }

            if (writeDumpDir is not null)
            {
                DumpHddWrites(machine, writeDumpDir);
            }

            foreach (string pattern in scans)
            {
                ScanRam(machine, pattern);
            }

            var watchKeys = new List<uint>(watchHits.Keys);
            watchKeys.Sort();
            foreach (uint key in watchKeys)
            {
                var entry = watchHits[key];
                Console.WriteLine($"Watch: W{key:X8} n={entry.Count} first={entry.First} last={entry.Last}");
            }

            if (eipHist is not null)
            {
                Console.WriteLine($"EIPHIST samples={eipHist.Total}");
                foreach ((uint eip, ulong count) in eipHist.Top(40))
                {
                    Console.WriteLine($"EIPHIST {eip:X8} n={count}");
                }
            }

            return 0;

            void ApplyPadIfDue(ulong at)
            {
                if (!padApplied && at >= padStep && padInput is { } due)
                {
                    machine.Gamepad.SetInput(due.Buttons, due.Analog, due.Sticks);
                    Console.WriteLine(
                        $"Pad buttons=0x{due.Buttons:X4} analog={BitConverter.ToString(due.Analog)} at step {at}");
                    padApplied = true;
                }
            }

            void ApplyScriptIfDue(ulong at)
            {
                if (padScript is { } script && script.ApplyIfDue(at, out PadInput pressed))
                {
                    machine.Gamepad.SetInput(pressed.Buttons, pressed.Analog, pressed.Sticks);
                    Console.WriteLine(
                        $"Pad script buttons=0x{pressed.Buttons:X4} analog={BitConverter.ToString(pressed.Analog)} at step {at}");
                }
            }
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

    private static int TestRom(string[] args)
    {
        string? binPath = null;
        uint baseAddress = 0x1000;
        uint entryAddress = 0x1000;
        uint stackAddress = 0x80000;
        ulong steps = 10_000_000;
        uint ramMb = 64;
        string? hddPath = null;
        string? dvdPath = null;
        string? padButtons = null;
        string? padScriptSpec = null;
        string? framePath = null;
        string? pgraphPath = null;
        ulong pgraphMax = 262144;
        uint? breakAddress = null;
        List<(uint Address, int Length, bool Virtual)> dumps = [];

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--bin":
                    binPath = NextValue(args, ref i);
                    break;
                case "--base":
                    baseAddress = ParseTestRomAddress(NextValue(args, ref i));
                    break;
                case "--entry":
                    entryAddress = ParseTestRomAddress(NextValue(args, ref i));
                    break;
                case "--stack":
                    stackAddress = ParseTestRomAddress(NextValue(args, ref i));
                    break;
                case "--steps":
                    steps = ulong.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--ram":
                    ramMb = uint.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--hdd":
                    hddPath = NextValue(args, ref i);
                    break;
                case "--dvd":
                    dvdPath = NextValue(args, ref i);
                    break;
                case "--pad-buttons":
                    padButtons = NextValue(args, ref i);
                    break;
                case "--pad-script":
                    padScriptSpec = NextValue(args, ref i);
                    break;
                case "--frame":
                    framePath = NextValue(args, ref i);
                    break;
                case "--pgraph":
                    pgraphPath = NextValue(args, ref i);
                    break;
                case "--pgraph-max":
                    pgraphMax = ulong.Parse(NextValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--break":
                    breakAddress = ParseTestRomAddress(NextValue(args, ref i));
                    break;
                case "--dump":
                    {
                        string[] parts = NextValue(args, ref i).Split(',');
                        string addressText = parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                            ? parts[0][2..]
                            : parts[0];
                        uint address = Convert.ToUInt32(addressText, 16);
                        int length = parts.Length > 1 ? Convert.ToInt32(parts[1], 16) : 0x40;
                        dumps.Add((address, length, false));
                        break;
                    }

                default:
                    Console.Error.WriteLine($"Unknown testrom option: {args[i]}");
                    return 1;
            }
        }

        if (binPath is null)
        {
            Console.Error.WriteLine("testrom requires --bin <path>");
            return 1;
        }

        byte[] image;
        try
        {
            image = File.ReadAllBytes(binPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot read test ROM: {exception.Message}");
            return 4;
        }

        string name = Path.GetFileNameWithoutExtension(binPath);
        if (string.IsNullOrEmpty(name))
        {
            name = "rom";
        }

        XboxMachine machine;
        try
        {
            machine = new XboxMachine(ramMb * 1024 * 1024);
            if (hddPath is not null)
            {
                machine.AttachHdd(hddPath);
            }

            if (dvdPath is not null)
            {
                machine.AttachDvd(dvdPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot attach media: {exception.Message}");
            return 4;
        }

        using var runner = new TestRomRunner(machine, name);
        try
        {
            runner.LoadImage(image, baseAddress, entryAddress, stackAddress);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Cannot load test ROM: {exception.Message}");
            return 1;
        }

        machine.Nv2A.MethodStreamCap = pgraphMax;

        if (padButtons is not null)
        {
            if (!PadInput.TryParse(padButtons, null, null, out PadInput pad))
            {
                Console.Error.WriteLine("Invalid --pad-buttons (hex16 or names like down/a+down)");
                return 1;
            }

            machine.Gamepad.SetInput(pad.Buttons, pad.Analog, pad.Sticks);
        }

        PadScript? padScript = null;
        if (padScriptSpec is not null)
        {
            if (!PadScript.TryParse(padScriptSpec, out PadScript? parsed) || parsed is null)
            {
                Console.Error.WriteLine("Invalid --pad-script spec (events: buttonset@step[+hold],...)");
                return 1;
            }

            padScript = parsed;
        }

        ulong executed = 0;
        bool breakHit = false;
        try
        {
            if (breakAddress is { } stop)
            {
                int breakAdvance = 0;
                while (executed < steps && !machine.Cpu.Context.Halted)
                {
                    ApplyScriptIfDue(executed);
                    if (machine.Cpu.Context.Eip == stop)
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

                breakHit = machine.Cpu.Context.Eip == stop;
            }
            else
            {
                ApplyScriptIfDue(executed);
                while (executed < steps && !machine.Cpu.Context.Halted)
                {
                    ulong target = steps - executed;
                    if (padScript is { } splitter &&
                        splitter.TryGetNextBoundary(executed, out ulong scriptStep))
                    {
                        target = Math.Min(target, scriptStep - executed);
                    }

                    ulong ran = RunBatch(machine, target);
                    if (ran == 0)
                    {
                        break;
                    }

                    executed += ran;
                    ApplyScriptIfDue(executed);
                }
            }
        }
        catch (UnsupportedInstructionException exception)
        {
            Console.Error.WriteLine($"Unsupported instruction: {exception.Message}");
            Console.WriteLine(TestRomVerdict(runner, CountPresentedNonZero(machine)));
            PrintState(machine, executed, dumps);
            return 2;
        }
        catch (MemoryAccessException exception)
        {
            Console.Error.WriteLine($"Memory error: {exception.Message}");
            Console.WriteLine(TestRomVerdict(runner, CountPresentedNonZero(machine)));
            PrintState(machine, executed, dumps);
            return 3;
        }
        catch (UnhandledGuestException exception)
        {
            Console.Error.WriteLine($"Unhandled guest exception: {exception.Message}");
            Console.WriteLine(TestRomVerdict(runner, CountPresentedNonZero(machine)));
            PrintState(machine, executed, dumps);
            return 5;
        }

        runner.Console.Flush();
        Console.WriteLine(TestRomVerdict(runner, CountPresentedNonZero(machine)));
        if (breakHit)
        {
            Console.WriteLine($"Break: EIP=0x{machine.Cpu.Context.Eip:X8} after {executed} instruction(s)");
        }

        foreach (string line in runner.Console.Lines)
        {
            Console.WriteLine($"TESTROM LOG: {line}");
        }

        PrintState(machine, executed, dumps);

        if (framePath is not null)
        {
            SavePresentedFrame(machine, framePath);
        }

        if (pgraphPath is not null)
        {
            using var pgraphOut = new StreamWriter(pgraphPath);
            foreach ((uint subchannel, uint method, uint parameter) in machine.Nv2A.MethodStream)
            {
                pgraphOut.Write(subchannel);
                pgraphOut.Write(' ');
                pgraphOut.Write(method.ToString("X4"));
                pgraphOut.Write(' ');
                pgraphOut.WriteLine(parameter.ToString("X8"));
            }

            Console.WriteLine($"PGRAPH {pgraphPath} methods={machine.Nv2A.MethodStream.Count}");
        }

        return runner.Passed ? 0 : 1;

        void ApplyScriptIfDue(ulong at)
        {
            if (padScript is { } script && script.ApplyIfDue(at, out PadInput pressed))
            {
                machine.Gamepad.SetInput(pressed.Buttons, pressed.Analog, pressed.Sticks);
            }
        }
    }

    private static uint ParseTestRomAddress(string text)
    {
        string hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        return Convert.ToUInt32(hex, 16);
    }

    private static string TestRomVerdict(TestRomRunner runner, uint nonzero)
    {
        XboxMachine machine = runner.Machine;
        string counters =
            $"draws={machine.Nv2A.DrawsExecuted} textured={machine.Nv2A.TexturedPixels} " +
            $"frames={machine.Nv2A.FramesPresented} nonzero={nonzero}";
        return runner.Passed
            ? $"TESTROM PASS {runner.Name} {counters}"
            : $"TESTROM FAIL {runner.Name} eax={machine.Cpu.Context.Eax} " +
              $"eip=0x{machine.Cpu.Context.Eip:X8} {counters}";
    }

    private static uint CountPresentedNonZero(XboxMachine machine)
    {
        if (!machine.Nv2A.TryCopyPresentedFrame(out uint width, out uint height, out byte[] bgra))
        {
            return 0;
        }

        var frame = new DisplayFrame();
        frame.Show(width, height, bgra);
        return frame.CountNonZeroPixels();
    }

    private static void DumpFramebuffer(XboxMachine machine, string path)
    {
        uint address = machine.Nv2A.PresentedColorOffset;
        uint pitch = machine.Nv2A.PresentedColorPitch;
        uint width = machine.Nv2A.PresentedWidth;
        uint height = machine.Nv2A.PresentedHeight;
        uint bpp = machine.Nv2A.PresentedColorBpp;
        uint ramSize = machine.Ram.Size;
        if (width == 0 || width > 4096 || height == 0 || height > 4096 ||
            (bpp != 1 && bpp != 2 && bpp != 4) || pitch < width * bpp ||
            address + (height - 1) * pitch + width * bpp > ramSize)
        {
            Console.WriteLine($"FB skipped: addr=0x{address:X8} {width}x{height} bpp={bpp} pitch={pitch}");
            return;
        }

        byte[] raw = new byte[width * height * bpp];
        for (uint y = 0; y < height; y++)
        {
            machine.Ram.GetSpan(address + y * pitch, (int)(width * bpp))
                .CopyTo(new Span<byte>(raw, (int)(y * width * bpp), (int)(width * bpp)));
        }

        File.WriteAllBytes(path, raw);
        Console.WriteLine($"FB {path} {width}x{height} bpp={bpp} pitch={pitch} addr=0x{address:X8}");
    }

    private static ulong RunBatch(XboxMachine machine, ulong remaining)
    {
        int batch = remaining > int.MaxValue ? int.MaxValue : (int)remaining;
        return batch <= 0 ? 0 : (ulong)machine.Run(batch);
    }

    private static void SaveScanoutFrame(XboxMachine machine, string path)
    {
        if (machine.Nv2A.TryCopyScanoutFrame(out uint width, out uint height, out byte[] bgra))
        {
            var frame = new DisplayFrame();
            frame.Show(width, height, bgra);
            frame.SaveBmp(path);
            Console.WriteLine(
                $"FrameScanout {path} {width}x{height} nonzero={frame.CountNonZeroPixels()} " +
                machine.Nv2A.PresentedFrameDebug);
        }
        else
        {
            Console.WriteLine($"FrameScanout unavailable ({machine.Nv2A.PresentedFrameDebug})");
        }
    }

    private static void SaveDisplayFrame(XboxMachine machine, string path)
    {
        if (machine.Nv2A.TryCopyDisplayFrame(out uint width, out uint height, out byte[] bgra))
        {
            var frame = new DisplayFrame();
            frame.Show(width, height, bgra);
            frame.SaveBmp(path);
            Console.WriteLine(
                $"FrameDisplay {path} {width}x{height} nonzero={frame.CountNonZeroPixels()} " +
                machine.Nv2A.PresentedFrameDebug);
        }
        else
        {
            Console.WriteLine($"FrameDisplay unavailable ({machine.Nv2A.PresentedFrameDebug})");
        }
    }

    private static void SavePresentedFrame(XboxMachine machine, string path)
    {
        if (machine.Nv2A.TryCopyPresentedFrame(out uint width, out uint height, out byte[] bgra))
        {
            var frame = new DisplayFrame();
            frame.Show(width, height, bgra);
            frame.SaveBmp(path);
            Console.WriteLine(
                $"Frame {path} {width}x{height} nonzero={frame.CountNonZeroPixels()} " +
                machine.Nv2A.PresentedFrameDebug);
        }
        else
        {
            Console.WriteLine($"Frame unavailable ({machine.Nv2A.PresentedFrameDebug})");
        }
    }

    private static void DumpHddWrites(XboxMachine machine, string directory)
    {
        Storage.OverlayDiskImage? overlay = machine.HddOverlay;
        if (overlay is null)
        {
            Console.WriteLine("WRITEDUMP none (no HDD attached)");
            return;
        }

        uint[] sectors = overlay.GetModifiedSectors();
        Directory.CreateDirectory(directory);
        byte[] buffer = new byte[512];
        var manifest = new System.Text.StringBuilder();
        manifest.Append("sectors=").Append(sectors.Length)
            .Append(" writes=").AppendLine(overlay.WriteCount.ToString(CultureInfo.InvariantCulture));
        foreach (uint lba in sectors)
        {
            overlay.ReadSector(lba, buffer);
            File.WriteAllBytes(Path.Combine(directory, $"{lba:X8}.bin"), buffer);
            manifest.Append(lba.ToString("X8", CultureInfo.InvariantCulture)).Append(' ');
            for (int i = 0; i < 16; i++)
            {
                manifest.Append(buffer[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            manifest.AppendLine();
        }

        File.WriteAllText(Path.Combine(directory, "manifest.txt"), manifest.ToString());

        var ranges = new List<string>();
        for (int i = 0; i < sectors.Length;)
        {
            uint first = sectors[i];
            uint last = first;
            while (i + 1 < sectors.Length && sectors[i + 1] == last + 1)
            {
                last = sectors[++i];
            }

            i++;
            ranges.Add(first == last
                ? first.ToString("X6", CultureInfo.InvariantCulture)
                : first.ToString("X6", CultureInfo.InvariantCulture) + "-" +
                  last.ToString("X6", CultureInfo.InvariantCulture));
        }

        var repeats = new Dictionary<uint, int>();
        foreach (uint lba in overlay.WriteLog)
        {
            repeats.TryGetValue(lba, out int count);
            repeats[lba] = count + 1;
        }

        var top = new List<(uint Lba, int Count)>();
        foreach (var entry in repeats)
        {
            if (entry.Value > 1)
            {
                top.Add((entry.Key, entry.Value));
            }
        }

        top.Sort((a, b) => b.Count.CompareTo(a.Count));

        var topText = new List<string>();
        for (int i = 0; i < top.Count && i < 10; i++)
        {
            topText.Add(
                top[i].Lba.ToString("X6", CultureInfo.InvariantCulture) + "x" +
                top[i].Count.ToString(CultureInfo.InvariantCulture));
        }

        string rangeText = ranges.Count > 40
            ? string.Join(" ", ranges.GetRange(0, 40)) + $" (+{ranges.Count - 40} more)"
            : string.Join(" ", ranges);
        Console.WriteLine($"WRITEDUMP {directory} sectors={sectors.Length} writes={overlay.WriteCount}");
        Console.WriteLine($"  ranges: {rangeText}");
        Console.WriteLine($"  repeats: {(topText.Count == 0 ? "(none)" : string.Join(" ", topText))}");
    }

    private static DecodedInstruction Trace(XboxMachine machine, bool print, bool traceRegs)
    {
        XboxContext context = machine.Cpu.Context;
        uint linear = context.GetSegmentBase(SegmentRegister.Cs) + context.Eip;
        Span<byte> bytes = stackalloc byte[15];
        uint firstPhysical = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            uint physical = machine.Cpu.Mmu.Translate(linear + (uint)i, isWrite: false, isInstruction: true, out _,
                out _, sideEffects: false);
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

    private static void PrintUnmappedTopPages(
        string kind, IReadOnlyDictionary<uint, int> pages)
    {
        if (pages.Count == 0)
        {
            return;
        }

        var sorted = new List<(uint Page, int Count)>();
        foreach (var entry in pages)
        {
            sorted.Add((entry.Key, entry.Value));
        }

        sorted.Sort((a, b) => b.Count.CompareTo(a.Count));
        var log = new System.Text.StringBuilder();
        long total = 0;
        int shown = 0;
        int limit = sorted.Count <= 2048 ? sorted.Count : 20;
        foreach (var entry in sorted)
        {
            total += entry.Count;
            if (shown < limit)
            {
                log.Append(CultureInfo.InvariantCulture, $" {entry.Page:X5}={entry.Count}");
                shown++;
            }
        }

        if (sorted.Count > shown)
        {
            log.Append(CultureInfo.InvariantCulture, $" (+{sorted.Count - shown} pages)");
        }

        Console.WriteLine($"Unmapped {kind} pages ({sorted.Count}, total {total}):{log}");
    }

    private static string TopOffsets(IReadOnlyDictionary<uint, int> counts)
    {
        var sorted = new List<(uint Offset, int Count)>();
        foreach (var entry in counts)
        {
            sorted.Add((entry.Key, entry.Value));
        }

        sorted.Sort((a, b) => b.Count.CompareTo(a.Count));
        var log = new System.Text.StringBuilder();
        for (int i = 0; i < sorted.Count && i < 8; i++)
        {
            log.Append(CultureInfo.InvariantCulture, $" {sorted[i].Offset:X2}={sorted[i].Count}");
        }

        return log.ToString();
    }

    private static string FormatTextureFormats(
        IReadOnlyDictionary<uint, long> attempts,
        IReadOnlyDictionary<uint, long> misses)
    {
        var sorted = new List<(uint Format, long Attempts, long Misses)>();
        foreach (var entry in attempts)
        {
            misses.TryGetValue(entry.Key, out long missed);
            sorted.Add((entry.Key, entry.Value, missed));
        }

        sorted.Sort((a, b) => b.Attempts.CompareTo(a.Attempts));
        var log = new System.Text.StringBuilder();
        for (int i = 0; i < sorted.Count && i < 12; i++)
        {
            log.Append(CultureInfo.InvariantCulture, $" {sorted[i].Format:X2}={sorted[i].Attempts}/{sorted[i].Misses}");
        }

        return log.ToString();
    }

    private static string FormatModeFallbacks(IReadOnlyDictionary<int, long> counts)
    {
        var sorted = new List<(int Mode, long Count)>();
        foreach (var entry in counts)
        {
            sorted.Add((entry.Key, entry.Value));
        }

        sorted.Sort((a, b) => b.Count.CompareTo(a.Count));
        var log = new System.Text.StringBuilder();
        for (int i = 0; i < sorted.Count && i < 8; i++)
        {
            log.Append(CultureInfo.InvariantCulture, $" {sorted[i].Mode:X2}={sorted[i].Count}");
        }

        return log.ToString();
    }

    private static void PrintState(XboxMachine machine, ulong executed,
        IReadOnlyList<(uint Address, int Length, bool Virtual)> dumps)
    {
        XboxContext context = machine.Cpu.Context;
        Console.WriteLine($"Executed {executed} instruction(s)");
        Console.WriteLine(
            $"EIP=0x{context.Eip:X8} Halted={context.Halted} CR0=0x{context.Cr0:X8} EFLAGS=0x{context.Eflags:X8}");
        Console.WriteLine(
            $"LastFault vec={machine.Cpu.LastFaultVector} EIP=0x{machine.Cpu.LastFaultEip:X8} err=0x{machine.Cpu.LastFaultErrorCode:X8} CR2=0x{context.Cr2:X8}");
        Console.WriteLine(
            $"EAX=0x{context.Eax:X8} EBX=0x{context.Ebx:X8} ECX=0x{context.Ecx:X8} EDX=0x{context.Edx:X8}");
        Console.WriteLine(
            $"ESI=0x{context.Esi:X8} EDI=0x{context.Edi:X8} EBP=0x{context.Ebp:X8} ESP=0x{context.Esp:X8}");
        Console.WriteLine(
            $"CR3=0x{context.Cr3:X8} IDT=0x{context.Idtr.Base:X8}/{context.Idtr.Limit:X4} GDT=0x{context.Gdtr.Base:X8}/{context.Gdtr.Limit:X4}");
        Console.WriteLine($"OverlayEnabled={machine.Bus.RomOverlayEnabled}");
        if (machine.Cpu.IdleSkipEnabled)
        {
            Console.WriteLine(
                $"Idle-skip measureOnly={machine.Cpu.IdleSkipMeasureOnly} " +
                $"triggers={machine.Cpu.IdleSkipTriggers} skippedSteps={machine.Cpu.IdleSkipSkippedSteps} " +
                $"iters={machine.Cpu.IdleSkipIterations} staticRejects={machine.Cpu.IdleSkipStaticRejects} " +
                $"accessAborts={machine.Cpu.IdleSkipAccessAborts} structuralAborts={machine.Cpu.IdleSkipStructuralAborts} " +
                $"stopIrq={machine.Cpu.IdleSkipStopIrq} stopWatch={machine.Cpu.IdleSkipStopWatch} " +
                $"stopDeadline={machine.Cpu.IdleSkipStopDeadline} stopBudget={machine.Cpu.IdleSkipStopBudget} " +
                $"stopReadDeadline={machine.Cpu.IdleSkipStopReadDeadline} stopModelLost={machine.Cpu.IdleSkipStopModelLost} " +
                $"invocations={machine.Cpu.IdleSkipInvocations} triggerDrops={machine.Cpu.IdleSkipTriggerDrops} " +
                $"hostNull={machine.Cpu.IdleSkipHostNull} " +
                $"hlt={machine.Cpu.IdleSkipHltExecuted} lastTriggerEip=0x{machine.Cpu.IdleSkipLastTriggerEip:X8}");
            Console.WriteLine(
                $"Idle-skip steps: armable={machine.Cpu.IdleSkipArmableSteps} " +
                $"mmio={machine.Cpu.IdleSkipRejectedStepsMmioRead} pio={machine.Cpu.IdleSkipRejectedStepsPio} " +
                $"memWrite={machine.Cpu.IdleSkipRejectedStepsMemWrite} " +
                $"watchOverflow={machine.Cpu.IdleSkipRejectedStepsWatchOverflow} " +
                $"staticRule={machine.Cpu.IdleSkipRejectedStepsStaticRule} " +
                $"structural={machine.Cpu.IdleSkipRejectedStepsStructural} " +
                $"headsUntracked={machine.Cpu.IdleSkipHeadsUntracked}");
            foreach (IdleSkipHeadStat headStat in machine.Cpu.IdleSkipTopRejectedHeads(8))
            {
                Console.WriteLine(
                    $"Idle-skip head 0x{headStat.Head:X8} {headStat.Reason} " +
                    $"steps={headStat.Steps} addr=0x{headStat.Address:X8}");
            }

            // §119: name the device-address heads even when small (the MMIO/PIO
            // buckets are the wave's measurement target).
            int deviceHeads = 0;
            foreach (IdleSkipHeadStat headStat in machine.Cpu.IdleSkipTopRejectedHeads(256))
            {
                if (headStat.Reason is not (IdleSkipRejectReason.MmioRead or IdleSkipRejectReason.Pio))
                {
                    continue;
                }

                Console.WriteLine(
                    $"Idle-skip device head 0x{headStat.Head:X8} {headStat.Reason} " +
                    $"steps={headStat.Steps} addr=0x{headStat.Address:X8}");
                if (++deviceHeads >= 6)
                {
                    break;
                }
            }
        }

        Console.WriteLine(
            $"PIC master base=0x{machine.Bus.Pic.MasterBase:X2} mask=0x{machine.Bus.Pic.MasterMask:X2} " +
            $"slaveMask=0x{machine.Bus.Pic.SlaveMask:X2} " +
            $"req=0x{machine.Bus.Pic.MasterRequest:X2} isr=0x{machine.Bus.Pic.MasterInService:X2}");
        Console.WriteLine(
            $"PIT ch0 divisor={machine.Bus.Pit.Channel0Divisor} mode={machine.Bus.Pit.Channel0Mode}");
        Console.WriteLine($"Interrupts delivered={machine.Cpu.InterruptCount}");

        // §131 leg (d): printed only while the decode cache is enabled so
        // flag-off logs stay byte-identical to the master baselines.
        if (machine.Cpu.BlockStatistics is { } blocks)
        {
            Console.WriteLine(
                $"Block stats dispatches={blocks.Dispatches} instructions={blocks.Instructions} builds={blocks.Builds} hits={blocks.Hits} entries={blocks.Count} peak={blocks.PeakCount} invalidations={blocks.Invalidations} capFlushes={blocks.CapFlushes} completed={blocks.Completed} redirects={blocks.Redirects} boundaryExits={blocks.BoundaryExits} verifyFailures={blocks.VerifyFailures}");
        }

        // §139: printed only while specialization is enabled so every other
        // mode's logs stay byte-identical to their baselines.
        if (machine.Cpu.SpecExecutionEnabled)
        {
            DecodedBlockCache? spec = machine.Cpu.SpecStatistics;
            Console.WriteLine(
                $"Spec stats shapeHits={spec?.ShapeHits ?? 0} shapeFallbacks={spec?.ShapeFallbacks ?? 0} " +
                $"specBlocks={spec?.SpecBlocks ?? 0} specRan={spec?.SpecRan ?? 0} verifyFailures={spec?.VerifyFailures ?? 0}");
            if (spec is not null)
            {
                foreach ((XboxCpu.SpecShape shape, ulong count) in spec.TopFallbackShapes(12))
                {
                    Console.WriteLine(
                        $"Spec fallback {shape.Mnemonic}/{shape.Op1Kind}/{shape.Op2Kind}/{shape.Size}x{shape.Condition}/{shape.SegmentOverride}={count}");
                }

                if (spec.ShapeTallyEnabled)
                {
                    // §175 E3-0: full served-shape ranking (top 40; the
                    // histogram caps at 512 distinct shapes).
                    foreach ((XboxCpu.SpecShape shape, ulong count) in spec.TopServedShapes(40))
                    {
                        Console.WriteLine(
                            $"Spec served {shape.Mnemonic}/{shape.Op1Kind}/{shape.Op2Kind}/{shape.Size}x{shape.Condition}/{shape.SegmentOverride}={count}");
                    }
                }
            }
        }

        // §171: printed only while emission is enabled so every other
        // mode's logs stay byte-identical to their baselines.
        if (machine.Cpu.EmitEnabled)
        {
            DecodedBlockCache? emit = machine.Cpu.SpecStatistics;
            Console.WriteLine(
                $"Emit stats builds={emit?.EmitBuilds ?? 0} hits={emit?.EmitHits ?? 0} ran={emit?.EmitRan ?? 0}");
            if (machine.Cpu.EmitInlineEnabled)
            {
                Console.WriteLine($"Emit inline members={emit?.EmitInlineMembers ?? 0} fast={emit?.EmitFastMembers ?? 0}");
            }

            if (machine.Cpu.EmitInlineCountersEnabled)
            {
                long hits = machine.Cpu.EmitInlineFastHits;
                long misses = machine.Cpu.EmitInlineTagMisses;
                long twins = machine.Cpu.EmitInlineTwinCalls;
                double denom = hits + twins;
                double hitRate = denom == 0 ? 0 : hits / denom;
                Console.WriteLine(FormattableString.Invariant(
                    $"Emit inline diag fastHits={hits} tagMisses={misses} twinCalls={twins} hitRate={hitRate:F4}"));
            }
        }

        // J1: printed only while the fast-mode engine is enabled so every
        // other mode's logs stay byte-identical to their baselines.
        if (machine.Cpu.JitEnabled)
        {
            Console.WriteLine(
                $"Host timing executeMs={machine.RunExecuteMs:F0} advanceMs={machine.RunAdvanceMs:F0} " +
                "(machine.Run: Cpu.Execute vs device Advance; the rest of the wall is CLI/bookkeeping)");
            Console.WriteLine(
                $"Jit stats blocks={machine.Cpu.JitBlocksBuilt} served={machine.Cpu.JitServedTotal} " +
                $"invocations={machine.Cpu.JitInvocations} calls={machine.Cpu.JitCallCount} faults={machine.Cpu.JitFaults} " +
                $"runnerMs={machine.Cpu.JitRunnerTicks / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0:F0} " +
                $"outerMs={machine.Cpu.JitOuterTicks / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0:F0} " +
                $"nonJitMs={machine.Cpu.NonJitTicks / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0:F0} " +
                $"nonJitCalls={machine.Cpu.NonJitCalls} nonJitInstructions={machine.Cpu.NonJitInstructions} " +
                $"slowMembers={machine.Cpu.JitSlowMembers} slowMem={machine.Cpu.RawSlowMemoryMembers} slowDev={machine.Cpu.RawSlowDeviceMembers} " +
                (machine.Cpu.JitRawBlocks ? $"rawBuilt={machine.Cpu.JitRawBlocksBuilt} rawInv={machine.Cpu.JitRawInvocations} rawServed={machine.Cpu.JitRawServedMembers} rawChained={machine.Cpu.JitRawChainedBlocks} rawBuildFails={XboxCpu.RawBuildFailures} " : string.Empty) +
                (machine.Cpu.JitMmioFast ? $"mmioMembers={machine.Cpu.JitMmioMembers} " : string.Empty) +
                (machine.Cpu.JitSpinSkip ? $"spinSkips={machine.Cpu.JitSpinSkips} spinSkippedSteps={machine.Cpu.JitSpinSkippedSteps} " : string.Empty) + $"yieldReasons={string.Join('/', machine.Cpu.JitYieldReasons)} " +
                FormatJitBuckets(machine.Cpu.JitBuckets) + " " +
                $"rejectedHeads={machine.Cpu.JitRejectedHeads} transientBails={machine.Cpu.JitTransientBails} " +
                $"pendingVisits={machine.Cpu.JitPendingVisits} " +
                $"cachedHeads={machine.Cpu.JitHeadCacheCount} pendingHeads={machine.Cpu.JitPendingHeadCount} " +
                $"blockGen={machine.Cpu.JitBlockGeneration} pageDrops={machine.Cpu.DecodeCachePageDrops} " +
                $"fullFlushes={machine.Cpu.DecodeCacheFullFlushes} revalidations={machine.Cpu.JitRevalidations} " +
                $"flushSrc(pt={machine.Cpu.FlushFromPtStore} mmu={machine.Cpu.FlushFromMmu} " +
                $"invlpg={machine.Cpu.FlushFromFlushPage} host={machine.Cpu.FlushFromHost})");

            if (machine.Cpu.JitTally
                || string.Equals(Environment.GetEnvironmentVariable("XGRID_JIT_TAIL"), "1", StringComparison.Ordinal))
            {
                long[] yield = machine.Cpu.JitYieldReasons;
                Console.WriteLine(
                    $"Jit yield pending={yield[0]} rejected={yield[1]} formBail={yield[2]} " +
                    $"mismatch={yield[3]} noProgress={yield[4]}");
                Console.WriteLine(
                    $"Jit tail calls={machine.Cpu.NonJitCalls} instructions={machine.Cpu.NonJitInstructions} " +
                    $"runs={string.Join(',', machine.Cpu.NonJitRunBuckets)}");
                Console.WriteLine("Jit rejected " + FormatJitMnemonicTop(machine.Cpu.JitRejectedMnemonics, 12));
                Console.WriteLine("Jit tail " + FormatJitMnemonicTop(machine.Cpu.NonJitMnemonics, 16));
            }

            if (string.Equals(Environment.GetEnvironmentVariable("XGRID_SLOW"), "1", StringComparison.Ordinal))
            {
                Console.WriteLine("Slow members " + FormatJitMnemonicTop(machine.Cpu.SlowMemberCounts, 16));
            }

            if (machine.Cpu.RawShapeTally)
            {
                long rawHelperTotal = 0;
                foreach (long count in machine.Cpu.RawHelperShapeCounts)
                {
                    rawHelperTotal += count;
                }

                Console.WriteLine(
                    $"Raw helper shapes total={rawHelperTotal} " +
                    FormatJitMnemonicTop(machine.Cpu.RawHelperShapeCounts, 24));
                long[] detail = machine.Cpu.RawHelperShapeDetail;
                var top = new List<(int Key, long Count)>();
                for (int key = 0; key < detail.Length; key++)
                {
                    if (detail[key] != 0)
                    {
                        top.Add((key, detail[key]));
                    }
                }

                top.Sort(static (left, right) => right.Count.CompareTo(left.Count));
                for (int i = 0; i < top.Count && i < 24; i++)
                {
                    int key = top[i].Key;
                    int sizeCode = key % 8;
                    int rest = key / 8;
                    int op2 = rest % 4;
                    rest /= 4;
                    int op1 = rest % 4;
                    int mnemonic = rest / 4;
                    string sizeText = sizeCode switch
                    {
                        1 => "8",
                        2 => "16",
                        3 => "32",
                        4 => "64",
                        5 => "80",
                        6 => "128",
                        _ => "?",
                    };
                    Console.WriteLine(
                        $"  rawshape {((Mnemonic)mnemonic),-12} {Form((OperandKind)op1)}" +
                        $"{Form((OperandKind)op2)} size={sizeText,-3} count={top[i].Count}");
                    continue;

                    string Form(OperandKind kind) => kind switch
                    {
                        OperandKind.Register => "R",
                        OperandKind.Memory => "M",
                        OperandKind.Immediate => "I",
                        _ => "-",
                    };
                }
            }

            if (XboxCpu.RawProfileEnabled)
            {
                Console.WriteLine(machine.Cpu.FormatRawKindProfile());
            }
        }

        if (machine.Cpu.DecodeCacheEnabled)
        {
            ulong decodeCacheHits = machine.Cpu.DecodeCacheHits;
            ulong decodeCacheMisses = machine.Cpu.DecodeCacheMisses;
            double decodeCacheTotal = decodeCacheHits + decodeCacheMisses;
            double hitShare = decodeCacheTotal == 0 ? 0 : decodeCacheHits / decodeCacheTotal;
            Console.WriteLine(
                $"Decode cache hits={decodeCacheHits} misses={decodeCacheMisses} " +
                FormattableString.Invariant($"hitShare={hitShare:F4} ") +
                $"decodes={machine.Cpu.DecodeCount} fills={machine.Cpu.DecodeCacheFills} " +
                $"entries={machine.Cpu.DecodeCacheEntries} peak={machine.Cpu.DecodeCachePeakEntries} " +
                $"pageDrops={machine.Cpu.DecodeCachePageDrops} fullFlushes={machine.Cpu.DecodeCacheFullFlushes} " +
                $"crossPageSkips={machine.Cpu.DecodeCacheSkippedCrossPage} " +
                $"nonRamSkips={machine.Cpu.DecodeCacheSkippedNonRam} " +
                $"verifyFailures={machine.Cpu.DecodeCacheVerifyFailures}");
            if (machine.Cpu.DecodeCacheNonRamPages is { } nonRamPages)
            {
                var top = System.Linq.Enumerable.Take(
                    System.Linq.Enumerable.Select(
                        System.Linq.Enumerable.OrderByDescending(nonRamPages, static pair => pair.Value),
                        static pair => $"0x{pair.Key:X5}({pair.Value})"),
                    8);
                Console.WriteLine("Non-cacheable fetch pages: " + string.Join(' ', top));
            }
        }

        Console.WriteLine(
            $"IDE accesses={machine.Ide.AccessCount} " +
            $"lastCmd=0x{machine.Ide.LastCommand:X2} " +
            $"(master=0x{machine.Ide.PrimaryMaster.LastCommand:X2} " +
            $"slave=0x{machine.Ide.PrimarySlave.LastCommand:X2})");
        Console.WriteLine($"IDE cmds: {string.Join(" ", machine.Ide.CommandLog)}");
        Console.WriteLine($"ATAPI pkts: {string.Join(" ", machine.Ide.PrimarySlave.PacketLog)}");
        Console.WriteLine(
            $"ATAPI partial: master={machine.Ide.PrimaryMaster.PacketBytesReceived} slave={machine.Ide.PrimarySlave.PacketBytesReceived}");
        Console.WriteLine(
            $"IDE BMIBA=0x{machine.Ide.GetBarBase(4):X} status=0x{machine.Ide.BusMasterStatus:X2} prdt=0x{machine.Ide.BusMasterPrdt:X8} starts={machine.Ide.BusMasterStarts} pktbytes={machine.Ide.PrimarySlave.TotalPacketBytes}");
        Console.WriteLine($"IDE trace: {string.Join(" ", machine.Ide.AccessLog)}");
        Console.WriteLine(
            $"RTC accesses={machine.Bus.Rtc.AccessCount} pie={machine.Bus.Rtc.PeriodicEnabled}");
        var portLog = new System.Text.StringBuilder();
        foreach (var entry in machine.Bus.Io.PortAccessCounts)
        {
            portLog.Append(CultureInfo.InvariantCulture, $" {entry.Key:X4}={entry.Value}");
        }

        Console.WriteLine($"Port accesses:{portLog}");
        var unmappedBuckets = new List<uint>(machine.Memory.UnmappedAccessCounts.Keys);
        unmappedBuckets.Sort();
        var unmappedLog = new System.Text.StringBuilder();
        foreach (uint bucket in unmappedBuckets)
        {
            unmappedLog.Append(CultureInfo.InvariantCulture,
                $" {bucket:X3}={machine.Memory.UnmappedAccessCounts[bucket]}");
        }

        Console.WriteLine($"Unmapped phys (1MB buckets):{unmappedLog}");
        PrintUnmappedTopPages("read", machine.Memory.UnmappedPageReads);
        PrintUnmappedTopPages("write", machine.Memory.UnmappedPageWrites);
        var configKeys = new List<ushort>(machine.Bus.Pci.ConfigAccessCounts.Keys);
        configKeys.Sort();
        var configLog = new System.Text.StringBuilder();
        foreach (ushort key in configKeys)
        {
            configLog.Append(CultureInfo.InvariantCulture, $" {key:X4}={machine.Bus.Pci.ConfigAccessCounts[key]}");
        }

        Console.WriteLine($"PCI cfg (bus+devfn):{configLog}");
        var barLog = new System.Text.StringBuilder();
        int[] barSlots = [0x10, 0x18, 0x20, 0x28, 0x30, 0x48];
        foreach (int devfn in barSlots)
        {
            uint cmdAddr = Bus.Pci.PciBus.MakeConfigAddress((byte)(devfn >> 3), 0, 0x04);
            uint barAddr = Bus.Pci.PciBus.MakeConfigAddress((byte)(devfn >> 3), 0, 0x10);
            barLog.Append(
                $" {devfn:X2}:cmd={machine.Bus.Pci.ReadConfig(cmdAddr, 2):X4}," +
                $"bar0={machine.Bus.Pci.ReadConfig(barAddr, 4):X8}");
        }

        uint nvCmdAddr = Bus.Pci.PciBus.MakeConfigAddress(0, 0, 0x04) | (1u << 16);
        uint nvBarAddr = Bus.Pci.PciBus.MakeConfigAddress(0, 0, 0x10) | (1u << 16);
        barLog.Append(
            $" NV1:cmd={machine.Bus.Pci.ReadConfig(nvCmdAddr, 2):X4}," +
            $"bar0={machine.Bus.Pci.ReadConfig(nvBarAddr, 4):X8}");
        uint intRoute = machine.Bus.Pci.ReadConfig(
            Bus.Pci.PciBus.MakeConfigAddress(1, 0, 0x6C), 4);
        uint pirqRoute = machine.Bus.Pci.ReadConfig(
            Bus.Pci.PciBus.MakeConfigAddress(1, 0, 0x68), 4);
        uint acpiRoute = machine.Bus.Pci.ReadConfig(
            Bus.Pci.PciBus.MakeConfigAddress(1, 0, 0x64), 4);
        barLog.Append($" introute={intRoute:X8} pirq={pirqRoute:X8} acpi={acpiRoute:X8}");

        Console.WriteLine($"PCI state:{barLog}");
        var smcLog = new System.Text.StringBuilder();
        foreach (var entry in machine.Bus.Smc.ReadCounts)
        {
            smcLog.Append(CultureInfo.InvariantCulture, $" R{entry.Key:X2}={entry.Value}");
        }

        foreach (var entry in machine.Bus.Smc.WriteCounts)
        {
            smcLog.Append(CultureInfo.InvariantCulture, $" W{entry.Key:X2}={entry.Value}");
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
            txnLog.Append(
                CultureInfo.InvariantCulture,
                $" {txn.SlaveAddress:X2}:{(txn.Read ? "R" : "W")}p{txn.Protocol}:{txn.Command:X2}{(txn.Ok ? string.Empty : "!")}");
        }

        Console.WriteLine($"SMBus log:{txnLog}");
        var nv2ALog = new System.Text.StringBuilder();
        foreach (var entry in machine.Nv2A.ReadCounts)
        {
            nv2ALog.Append(CultureInfo.InvariantCulture, $" R{entry.Key:X6}={entry.Value}");
        }

        foreach (var entry in machine.Nv2A.WriteCounts)
        {
            nv2ALog.Append(CultureInfo.InvariantCulture, $" W{entry.Key:X6}={entry.Value}");
        }

        Console.WriteLine($"NV2A regs:{nv2ALog}");
        Console.WriteLine(
            $"NV2A draws executed={machine.Nv2A.DrawsExecuted} skipped={machine.Nv2A.SkippedDraws} " +
            $"pixels={machine.Nv2A.DrawnPixels} textured={machine.Nv2A.TexturedPixels} clears={machine.Nv2A.ClearsExecuted} " +
            $"blended={machine.Nv2A.BlendedPixels} depth={machine.Nv2A.DepthTestedPixels} " +
            $"zclip={machine.Nv2A.ZclipClampedPixels} zclipCull={machine.Nv2A.ZclipCulledPixels} " +
            $"culled={machine.Nv2A.TrianglesCulled} " +
            $"frames={machine.Nv2A.FramesPresented} stalls={machine.Nv2A.FlipStalls} " +
            $"programLoad={machine.Nv2A.TransformProgramLoadDebug} constLoad={machine.Nv2A.TransformConstantLoadDebug} " +
            $"inlineElements={machine.Nv2A.InlineElementCount} " +
            $"fixedFn={machine.Nv2A.FixedFunctionDraws} fixedFnLit={machine.Nv2A.FixedFunctionLitDraws} " +
            $"fixedFnLights={machine.Nv2A.FixedFunctionLightedDraws}");
        if (machine.Nv2A.RasterTimingEnabled)
        {
            Console.WriteLine(
                $"NV2A raster seconds legacy={machine.Nv2A.RasterLegacySeconds:F2} " +
                $"psh={machine.Nv2A.RasterPshSeconds:F2} " +
                $"calls={machine.Nv2A.RasterLegacyCalls}/{machine.Nv2A.RasterPshCalls} " +
                $"fastfills={machine.Nv2A.FastKindDebug} (host-only diagnostic)");
            var configs = new List<KeyValuePair<string, (long Draws, long Pixels)>>(machine.Nv2A.CombinerConfigStats);
            configs.Sort((a, b) => b.Value.Pixels.CompareTo(a.Value.Pixels));
            for (int i = 0; i < configs.Count && i < 10; i++)
            {
                Console.WriteLine($"NV2A combiner cfg n={configs[i].Value.Pixels} fills={configs[i].Value.Draws} {configs[i].Key}");
            }
        }
        Console.WriteLine(
            $"NV2A tex ok={machine.Nv2A.TextureSampleSuccesses} rgb0={machine.Nv2A.TextureSampleRgbZero} " +
            $"disabled={machine.Nv2A.TextureSampleDisabled} badsize={machine.Nv2A.TextureSampleBadSize} " +
            $"formats={FormatTextureFormats(machine.Nv2A.TextureFormatAttempts, machine.Nv2A.TextureFormatMisses)} " +
            $"filters={machine.Nv2A.TextureFilterDebug} " +
            $"fallbacks={machine.Nv2A.TexModeFallbacks} byMode={FormatModeFallbacks(machine.Nv2A.TexModeFallbacksByMode)} " +
            $"dotMaps={machine.Nv2A.UnsupportedDotMaps}");
        Console.WriteLine(
            $"NV2A combiner rgb0={machine.Nv2A.CombinerRgbZeroFragments} rgb0Alpha={machine.Nv2A.CombinerRgbZeroAlphaPositiveFragments} " +
            machine.Nv2A.CombinerStateDebug);
        var texStates =
            new List<(string Key, long Samples, long RgbZero, long Fallbacks, long Misses, uint FirstAddress, uint
                FirstTexel, float MinU, float MaxU, float MinV, float MaxV)>(machine.Nv2A.TextureStateStats);
        texStates.Sort((a, b) => (b.Samples + b.Fallbacks + b.Misses).CompareTo(a.Samples + a.Fallbacks + a.Misses));
        for (int i = 0; i < texStates.Count && i < 16; i++)
        {
            var state = texStates[i];
            Console.WriteLine(
                $"NV2A texstate n={state.Samples} rgb0={state.RgbZero} fb={state.Fallbacks} miss={state.Misses} " +
                $"addr={state.FirstAddress:X8} texel={state.FirstTexel:X8} " +
                $"u=[{state.MinU:G},{state.MaxU:G}] v=[{state.MinV:G},{state.MaxV:G}] {state.Key}");
        }

        Console.WriteLine(
            $"NV2A semaphore releases={machine.Nv2A.SemaphoreReleases} last={machine.Nv2A.SemaphoreDebug}");
        if (machine.Nv2A.Backend is not null)
        {
            Console.WriteLine(
                $"GPU {machine.Nv2A.Backend.DebugSummary} BackendUnsupported={machine.Nv2A.Backend.BackendUnsupported} " +
                $"methods={machine.Nv2A.MethodTotal}");
            if (machine.Nv2A.Backend is GpuGl.GlPgraphBackend windowBackend &&
                !string.Equals(windowBackend.WindowStatus, "off", StringComparison.Ordinal))
            {
                Console.WriteLine(
                    $"GL window presents={windowBackend.WindowPresents} status={windowBackend.WindowStatus}");
            }
        }

        var pmcEnLogText = new System.Text.StringBuilder();
        foreach (uint entry in machine.Nv2A.PmcIntrEnLog)
        {
            pmcEnLogText.Append(CultureInfo.InvariantCulture, $"{entry:X8},");
        }

        Console.WriteLine(
            $"NV2A irq pgraphEn={machine.Nv2A.PgraphEnabledDebug:X8} pgraphPending={machine.Nv2A.PgraphPendingDebug:X8} " +
            $"pmcEn={machine.Nv2A.PmcEnabledDebug:X8} notifications={machine.Nv2A.NotificationsRaised} asserted={machine.Nv2A.NotificationAsserted} " +
            $"pmcEnLog=[{pmcEnLogText}]");
        Console.WriteLine(
            $"NV2A pramin writes={machine.Nv2A.PraminWrites} " +
            $"w0={machine.Nv2A.ReadRaminWord(0):X8} w4={machine.Nv2A.ReadRaminWord(4):X8} " +
            $"w8={machine.Nv2A.ReadRaminWord(8):X8} wC={machine.Nv2A.ReadRaminWord(0xC):X8} " +
            $"w10={machine.Nv2A.ReadRaminWord(0x10):X8}");
        Console.WriteLine(
            $"APU ist={machine.Apu.InterruptStatus:X8} ien={machine.Apu.InterruptEnable:X8} " +
            $"fectl={machine.Apu.FrontEndControl:X8} asserted={machine.Apu.InterruptAsserted} " +
            $"reads={machine.Apu.ReadCount} writes={machine.Apu.WriteCount} voiceOff={machine.Apu.VoiceOffCompletions}");
        foreach (string entry in machine.Apu.EventLog)
        {
            Console.WriteLine($"APU log: {entry}");
        }

        Console.WriteLine(
            $"ACI nam={machine.Aci.NamAccesses} nabm={machine.Aci.NabmAccesses}");
        Console.WriteLine(
            $"007 EOS quirk enabled={machine.SevenEosQuirkEnabled} checks={machine.SevenEosQuirkChecks} fires={machine.SevenEosQuirkFires}");
        Console.WriteLine(
            $"NVNET reads={machine.Net.Reads} writes={machine.Net.Writes} " +
            $"regs:{TopOffsets(machine.Net.RegReadCounts)} wr:{TopOffsets(machine.Net.RegWriteCounts)}");
        Console.WriteLine(
            $"NVNET tx_pkts={machine.Net.TxPackets} tx_bytes={machine.Net.TxBytes} kicks={machine.Net.TxKicks} cant_tx={machine.Net.CantTransmitCount} " +
            $"rx_pkts={machine.Net.RxPackets} rx_bytes={machine.Net.RxBytes} drop_filter={machine.Net.RxDropFilter} drop_nobuf={machine.Net.RxDropNoBuf} oversize={machine.Net.RxOversize} cant_rx={machine.Net.CantReceiveCount} " +
            $"link={machine.Net.LinkUp} autoneg={machine.Net.AutoNegPending} backend={machine.Net.Backend.GetType().Name} " +
            $"tx base=0x{machine.Net.TxRingBaseAddress:X8} cur=0x{machine.Net.TxRingCurrentAddress:X8} next=0x{machine.Net.TxRingNextAddress:X8} " +
            $"rx base=0x{machine.Net.RxRingBaseAddress:X8} cur=0x{machine.Net.RxRingCurrentAddress:X8} next=0x{machine.Net.RxRingNextAddress:X8} " +
            $"irq={machine.Net.InterruptAsserted} asserts={machine.Net.IrqAsserts} deasserts={machine.Net.IrqDeasserts}");
        Console.WriteLine(
            $"USB0 acc={machine.Usb0.RegisterAccesses} irq={machine.Usb0.InterruptAsserted} " +
            $"ctl={machine.Usb0.ControlTransfers} intr={machine.Usb0.InterruptTransfers} addr={machine.Usb0.DeviceAddress} " +
            $"cfg={machine.Usb0.Function?.Configured} chead=0x{machine.Usb0.ControlHead:X8} hcca=0x{machine.Usb0.Hcca:X8} " +
            $"p0=0x{machine.Usb0.GetPortStatus(0):X8} p1=0x{machine.Usb0.GetPortStatus(1):X8} " +
            $"p2=0x{machine.Usb0.GetPortStatus(2):X8} p3=0x{machine.Usb0.GetPortStatus(3):X8}");
        Console.WriteLine(
            $"USB1 acc={machine.Usb1.RegisterAccesses} irq={machine.Usb1.InterruptAsserted} " +
            $"ctl={machine.Usb1.ControlTransfers} intr={machine.Usb1.InterruptTransfers} addr={machine.Usb1.DeviceAddress} " +
            $"cfg={machine.Usb1.Function?.Configured} " +
            $"p0=0x{machine.Usb1.GetPortStatus(0):X8} p1=0x{machine.Usb1.GetPortStatus(1):X8} " +
            $"p2=0x{machine.Usb1.GetPortStatus(2):X8} p3=0x{machine.Usb1.GetPortStatus(3):X8}");
        if (machine.Usb0.Function is not null)
        {
            var usbLog = machine.Usb0.Function.RequestLog;
            for (int i = Math.Max(0, usbLog.Count - 16); i < usbLog.Count; i++)
            {
                Console.WriteLine($"USB0 log: {usbLog[i]}");
            }
        }

        var usbXfer = machine.Usb0.TransferLog;
        for (int i = Math.Max(0, usbXfer.Count - 16); i < usbXfer.Count; i++)
        {
            Console.WriteLine($"USB0 xfer: {usbXfer[i]}");
        }

        Console.WriteLine(
            $"USB0 regs:{TopOffsets(machine.Usb0.RegReadCounts)} wr:{TopOffsets(machine.Usb0.RegWriteCounts)}");

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
                        uint physical = machine.Cpu.Mmu.Translate(linear, isWrite: false, isInstruction: false,
                            out bool faulted, out _, sideEffects: false);
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

    private static int Fatx(string[] args)
    {
        if (args.Length < 2 || (!string.Equals(args[1], "ls", StringComparison.Ordinal) &&
                                !string.Equals(args[1], "cat", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine(
                "Usage: xgrid fatx <ls|cat> --image <path> --partition <lba> [--size <sectors>] [--path \\dir] [--out <file>]");
            return 1;
        }

        bool list = string.Equals(args[1], "ls", StringComparison.Ordinal);
        string? imagePath = null;
        uint partition = 0;
        uint size = 0;
        string path = "\\";
        string? output = null;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--image":
                    imagePath = NextValue(args, ref i);
                    break;
                case "--partition":
                    partition = Convert.ToUInt32(HexText(NextValue(args, ref i)), 16);
                    break;
                case "--size":
                    size = Convert.ToUInt32(HexText(NextValue(args, ref i)), 16);
                    break;
                case "--path":
                    path = NextValue(args, ref i);
                    break;
                case "--out":
                    output = NextValue(args, ref i);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown fatx option: {args[i]}");
                    return 1;
            }
        }

        if (imagePath is null)
        {
            Console.Error.WriteLine("fatx requires --image <path> --partition <lba>");
            return 1;
        }

        try
        {
            using Storage.IDiskImage image = XboxMachine.OpenDiskImage(imagePath);
            if (size == 0)
            {
                size = FindNextSuperblock(image, partition);
            }

            var fs = XboxFs.FatxFileSystem.Open(image, partition, size);
            if (list)
            {
                foreach (var entry in fs.ListDirectory(path))
                {
                    Console.WriteLine($"{(entry.IsDirectory ? "<DIR>" : $"{entry.Size,12}")} {entry.Name}");
                }
            }
            else
            {
                byte[] data = fs.ReadFile(path);
                if (output is null)
                {
                    using var stdout = Console.OpenStandardOutput();
                    stdout.Write(data, 0, data.Length);
                }
                else
                {
                    File.WriteAllBytes(output, data);
                }
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidDataException or FileNotFoundException
                                              or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"FATX error: {exception.Message}");
            return 4;
        }
    }

    private static int Xdvdfs(string[] args)
    {
        if (args.Length < 2 || (!string.Equals(args[1], "ls", StringComparison.Ordinal) &&
                                !string.Equals(args[1], "cat", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine(
                "Usage: xgrid xdvdfs <ls|cat> --image <path> [--path \\dir] [--out <file>]");
            return 1;
        }

        bool list = string.Equals(args[1], "ls", StringComparison.Ordinal);
        string? imagePath = null;
        string path = "\\";
        string? output = null;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--image":
                    imagePath = NextValue(args, ref i);
                    break;
                case "--path":
                    path = NextValue(args, ref i);
                    break;
                case "--out":
                    output = NextValue(args, ref i);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown xdvdfs option: {args[i]}");
                    return 1;
            }
        }

        if (imagePath is null)
        {
            Console.Error.WriteLine("xdvdfs requires --image <path>");
            return 1;
        }

        try
        {
            using var fs = XboxFs.XdvdfsFileSystem.Open(imagePath);
            if (list)
            {
                foreach (var entry in fs.ListDirectory(path))
                {
                    Console.WriteLine($"{(entry.IsDirectory ? "<DIR>" : $"{entry.Size,12}")} {entry.Name}");
                }
            }
            else
            {
                byte[] data = fs.ReadFile(path);
                if (output is null)
                {
                    using var stdout = Console.OpenStandardOutput();
                    stdout.Write(data, 0, data.Length);
                }
                else
                {
                    File.WriteAllBytes(output, data);
                }
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidDataException or FileNotFoundException
                                              or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"XDVDFS error: {exception.Message}");
            return 4;
        }
    }

    private static int Sector(string[] args)
    {
        string? imagePath = null;
        uint lba = 0;
        uint count = 1;
        string? output = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--image":
                    imagePath = NextValue(args, ref i);
                    break;
                case "--lba":
                    lba = Convert.ToUInt32(HexText(NextValue(args, ref i)), 16);
                    break;
                case "--count":
                    count = Convert.ToUInt32(HexText(NextValue(args, ref i)), 16);
                    break;
                case "--out":
                    output = NextValue(args, ref i);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown sector option: {args[i]}");
                    return 1;
            }
        }

        if (imagePath is null)
        {
            Console.Error.WriteLine("Usage: xgrid sector --image <path> --lba <hex> [--count N] [--out <file>]");
            return 1;
        }

        if (count == 0 || count > 256)
        {
            Console.Error.WriteLine("sector count must be 1-256");
            return 1;
        }

        try
        {
            using Storage.IDiskImage image = XboxMachine.OpenDiskImage(imagePath);
            byte[] data = new byte[count * 512];
            for (uint i = 0; i < count; i++)
            {
                image.ReadSector(lba + i, new Span<byte>(data, (int)(i * 512), 512));
            }

            if (output is not null)
            {
                File.WriteAllBytes(output, data);
                Console.WriteLine($"Sector {output} lba=0x{lba:X} count={count}");
                return 0;
            }

            for (uint i = 0; i < count; i++)
            {
                Console.WriteLine($"LBA 0x{lba + i:X}:");
                for (int row = 0; row < 512; row += 16)
                {
                    Console.Write($"  {row:X3}:");
                    for (int b = 0; b < 16; b++)
                    {
                        Console.Write($" {data[(i * 512) + row + b]:X2}");
                    }

                    Console.WriteLine();
                }
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidDataException or FileNotFoundException
                                              or DirectoryNotFoundException or ArgumentOutOfRangeException)
        {
            Console.Error.WriteLine($"Sector error: {exception.Message}");
            return 4;
        }
    }

    private static uint FindNextSuperblock(Storage.IDiskImage image, uint partition)
    {
        Span<byte> sector = stackalloc byte[512];
        for (uint lba = partition + 0x400; lba < image.SectorCount; lba += 0x400)
        {
            image.ReadSector(lba, sector);
            if (sector[0] == 'F' && sector[1] == 'A' && sector[2] == 'T' && sector[3] == 'X')
            {
                return lba - partition;
            }
        }

        return image.SectorCount - partition;
    }

    private static string HexText(string text)
    {
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
    }

    /// <summary>
    /// <c>xgrid disc title &lt;path&gt;</c>: prints the XBE certificate title id
    /// and title name an image resolves to - the key the frontend's per-title
    /// overrides (V2) use, so a user can look up what to write in
    /// <c>TitleOverrides</c> without launching the title.
    /// </summary>
    private static int Disc(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[1], "title", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: xgrid disc title <iso|xbe path>");
            return 1;
        }

        string? path = null;
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--image":
                case "--file":
                    path = NextValue(args, ref i);
                    break;
                default:
                    path ??= args[i];

                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("disc title requires an ISO/XISO or .xbe path");
            return 1;
        }

        if (!DiscTitleId.TryRead(path, out uint titleId, out string titleName))
        {
            Console.Error.WriteLine(
                $"No XBE title id read from '{path}' (unsupported or unreadable image; a CHD has no XDVDFS reader yet).");
            return 4;
        }

        Console.WriteLine($"TitleId=0x{titleId:X8} key=title:{titleId:X8} Title='{titleName}'");
        return 0;
    }

    private static int Xbe(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[1], "info", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "Usage: xgrid xbe info (--file <path> | --image <path> --partition <lba> [--size <sectors>] --path \\file.xbe)");
            return 1;
        }

        string? filePath = null;
        string? imagePath = null;
        uint partition = 0;
        uint size = 0;
        string path = "\\default.xbe";

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--file":
                    filePath = NextValue(args, ref i);
                    break;
                case "--image":
                    imagePath = NextValue(args, ref i);
                    break;
                case "--partition":
                    partition = Convert.ToUInt32(HexText(NextValue(args, ref i)), 16);
                    break;
                case "--size":
                    size = Convert.ToUInt32(HexText(NextValue(args, ref i)), 16);
                    break;
                case "--path":
                    path = NextValue(args, ref i);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown xbe option: {args[i]}");
                    return 1;
            }
        }

        try
        {
            byte[] image;
            if (filePath is not null)
            {
                image = File.ReadAllBytes(filePath);
            }
            else
            {
                if (imagePath is null)
                {
                    Console.Error.WriteLine("xbe info requires --file or --image with --partition and --path");
                    return 1;
                }

                using Storage.IDiskImage disk = XboxMachine.OpenDiskImage(imagePath);
                if (size == 0)
                {
                    size = FindNextSuperblock(disk, partition);
                }

                var fs = XboxFs.FatxFileSystem.Open(disk, partition, size);
                image = fs.ReadFile(path);
            }

            var xbe = XGrid.Xbe.XbeImage.Load(image);
            Console.WriteLine(
                $"Type={xbe.Type} Base=0x{xbe.BaseAddress:X8} Entry=0x{xbe.EntryPoint:X8} (stored 0x{xbe.EntryPointStored:X8})");
            Console.WriteLine(
                $"TitleId=0x{xbe.TitleId:X8} Title='{xbe.TitleName}' Media=0x{xbe.AllowedMedia:X} Region=0x{xbe.GameRegion:X} Version=0x{xbe.Version:X}");
            Console.WriteLine(
                $"Sections={xbe.Sections.Count} Libraries={xbe.Libraries.Count} Tls={(xbe.Tls is null ? "none" : "present")}");
            foreach (var section in xbe.Sections)
            {
                Console.WriteLine(
                    $"  {section.Name,-12} VA=0x{section.VirtualAddress:X8} vsize=0x{section.VirtualSize:X} raw=0x{section.RawSize:X} flags=0x{section.Flags:X}");
            }

            foreach (var library in xbe.Libraries)
            {
                Console.WriteLine($"  lib {library.Name} v{library.Major}.{library.Minor}.{library.Build}");
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidDataException or FileNotFoundException
                                              or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"XBE error: {exception.Message}");
            return 4;
        }
    }

    private static string NextValue(string[] args, ref int index)
    {
        return index + 1 >= args.Length ? throw new ArgumentException($"Missing value for {args[index]}") : args[++index];
    }

    private static string FormatJitMnemonicTop(long[] counts, int take)
    {
        var top = new List<(long Count, int Index)>();
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] > 0)
            {
                top.Add((counts[i], i));
            }
        }

        top.Sort((a, b) => b.Count.CompareTo(a.Count));
        var text = new System.Text.StringBuilder("top=");
        for (int i = 0; i < top.Count && i < take; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }

            text.Append(Enum.GetName(typeof(Mnemonic), top[i].Index)
                ?? top[i].Index.ToString(CultureInfo.InvariantCulture));
            text.Append(':');
            text.Append(top[i].Count.ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    private static string FormatJitBuckets((long Ticks, long Count)[] buckets)
    {
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < buckets.Length; i++)
        {
            if (i > 0)
            {
                text.Append(' ');
            }

            text.Append(CultureInfo.InvariantCulture, $"b{i}={buckets[i].Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:F0}ms/{buckets[i].Count}");
        }

        return text.ToString();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("XGrid - original Xbox emulator");
        Console.WriteLine();
        Console.WriteLine("Usage: xgrid <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine(
            "  boot --bios <path> [--mcpx <path>] [--eeprom <path>] [--hdd <path>] [--dvd <path>] [--steps N] [--trace N] [--trace-start N] [--dump addr[,len]] [--ramdump <path>] [--ramin <path>] [--eeprom-out <path>] [--save-state <path> [--save-at N]] [--load-state <path>] [--break addr] [--break-log addr]... [--ram MB] [--fb <path>] [--frame <path>] [--frame-first <path>] [--frame-scanout <path>] [--frame-display <path>] [--writedump <dir>] [--pad-buttons <set> --pad-analog <hex> --pad-sticks <lx,ly,rx,ry> [--pad-step N] [--pad-script <set@step[+hold],...>]] [--eip-hist N] [--net-backend <loopback|socket>] [--net-inject <hex>] [--net-link <up|down>] [--no-007-eos-quirk] [--idle-skip] [--idle-skip-probe] [--fetch-fast] [--decode-cache|--no-decode-cache] [--decode-verify] [--block-exec] [--block-verify] [--spec-exec] [--shape-tally] [--emit] [--emit-inline] [--emit-diag] [--jit] [--jit-mmio] [--jit-spin] [--jit-raw] [--raw-shapes] [--jit-timing] [--hotspot <csv>] [--gpu sw|gl] [--gpu-replace] [--gpu-time] [--window] [--ffp] [--tex-filter legacy|registers] [--bench] [--bench-name <name>]");
        Console.WriteLine("                   Load firmware and execute up to N instructions");
        Console.WriteLine(
            "                   Decode cache defaults on; --no-decode-cache restores legacy fetch/decode.");
        Console.WriteLine(
            "                   --fetch-fast and --decode-verify default off; verify implies cache even with opt-out.");
        Console.WriteLine(
            "                   --block-exec/--block-verify/--spec-exec default off; spec implies block-exec.");
        Console.WriteLine(
            "                   --shape-tally implies spec-exec, prints the served-shape histogram at exit, and suppresses --emit.");
        Console.WriteLine(
            "                   --emit-inline implies --emit and serves E3-1 lean bodies for covered memory shapes.");
        Console.WriteLine(
            "                   --emit-diag implies --emit-inline and prints baked fast-path counters (diagnostic).");
        Console.WriteLine(
            "                   --jit enables the fast-mode block engine (whole-block native serving; implies spec-exec; block-granular IRQ poll).");
        Console.WriteLine(
            "                   --bench prints BENCH steps/wall/msps + RAM/CPU SHA-256 after the run (host-only diagnostic).");
        Console.WriteLine(
            "  testrom --bin <file> [--base <hex>] [--entry <hex>] [--stack <hex>] [--steps N] [--ram MB] [--hdd <path>] [--dvd <path>] [--pad-buttons <set>] [--pad-script <spec>] [--frame <path>] [--pgraph <path>] [--dump addr[,len]] [--break addr]");
        Console.WriteLine("                   Run a flat-binary test ROM without firmware (EAX=0+HLT=pass)");
        Console.WriteLine(
            "  fatx <ls|cat> --image <path> --partition <lba> [--size <sectors>] [--path \\dir] [--out <file>]");
        Console.WriteLine("                   List or extract files from a FATX partition");
        Console.WriteLine(
            "  xdvdfs <ls|cat> --image <path> [--path \\dir] [--out <file>]");
        Console.WriteLine("                   List or extract files from an XDVDFS (Xbox DVD) image");
        Console.WriteLine("  sector --image <path> --lba <hex> [--count N] [--out <file>]");
        Console.WriteLine("                   Dump raw disk sectors as hex");
        Console.WriteLine(
            "  xbe info (--file <path> | --image <path> --partition <lba> [--size <sectors>] --path \\file)");
        Console.WriteLine("                   Show XBE headers, sections, and libraries");
        Console.WriteLine("  disc title <path>");
        Console.WriteLine(
            "                   Show the XBE title id/name of an ISO/XISO or loose .xbe (the key per-title overrides use)");
        Console.WriteLine("  --help, -h       Show this help");
        Console.WriteLine("  --version, -v    Show the version");
    }
}
