# Native CPU core (XCN0-XCN3)

The product CPU is the vendored xemu 0.8.139 / QEMU 10.2.0 TCG i386 core
behind `libxgridcpu`. Every device, the NV2A, the APU, the host layer and the
frontend stay managed C#. See `ImplementationPlan.md` E0N,
`docs/research-notes/native-cpu-core.md` and `docs/porting-policy.md` §2b.

## Layout

| Path | What |
|---|---|
| `qemu/` | A build copy of `References2/xemu-0.8.139` (**git-ignored**). Never edit `References2/`; this copy is disposable. |
| `qemu-patches/` | The build-scoping patches applied to `qemu/`. Tracked; part of the GPL source offer. |
| `xgridcpu/` | The C shim (`xgridcpu.h`, `xgridcpu.c`), the C smoke test (`test.c`) and `build.sh`. |
| `../src/XGrid.Cpu.Native/` | The C# P/Invoke seam (XCN1). |
| `../tools/xcn0-probe/` | The XCN0 C# acceptance probe. |
| `../scripts/build-native-cpu.ps1` | Reproducible build entry point. |

## Build (H1)

MSYS2 **MINGW64** (`C:\msys64\mingw64`) is the working toolchain: gcc 16.1,
meson 1.12.1, ninja 1.13.2, python 3.14.10, glib 2.88.1, pixman 0.46.4, zlib
1.4, libepoxy, xxhash, diffutils, PyYAML, expat. `C:\mingw64`, `C:\ninja`,
`C:\cmake` and `C:\Qt` are standalone fallbacks.

```powershell
pwsh -File scripts/build-native-cpu.ps1
```

Or by hand, in an `MSYSTEM=MINGW64` shell:

```bash
cp -r References2/xemu-0.8.139 native/qemu      # only if native/qemu is absent
cd native/qemu
for p in ../qemu-patches/*.patch; do patch -p1 < "$p"; done
./configure --target-list=i386-softmmu --without-default-features --enable-tcg \
  --disable-werror --disable-docs --disable-tools --disable-sdl --disable-opengl \
  --disable-curl --disable-slirp --disable-virtfs
ninja -C build -j 16 libqemu-i386-softmmu.a libsystem.a libqemuutil.a libqom.a \
  libio.a libcrypto.a
cd ../xgridcpu && ./build.sh
```

The normal `qemu-system-i386.exe` target is not built: it pulls xemu's UI,
which the C# frontend replaces. The archives above are the CPU/memory subset.

## Patches (build-tooling only)

1. `0001-configure.patch` - MSYS2 `ensurepip` is broken; use the system Python
   and `/mingw64/bin/meson` instead of a pyvenv.
2. `0002-subprojects-dsp56300-meson.patch` - create the extraction directory and
   use `tar --force-local -xzf` (GNU tar treats `D:` as a remote host).
3. `0003-ui-meson.patch` - do not build xemu's UI.
4. `0004-hw-9pfs-meson.patch` - no 9p backend (MinGW has no `sys/resource.h`).
5. `0005-hw-meson.patch` - `hw/core` only (no QEMU device models).
6. `0006-hw-core-meson.patch` - no EIF/FDT; add the portable x86 TSC + APIC
   objects.
7. `0007-tests-meson.patch` - skip the xemu test tree.
8. `0008-system-meson.patch` - no libfdt; use the stub.
9. `0009-backends-meson.patch` - no IOMMUFD/SGX.
10. `0010-meson-vulkan-imgui.patch` - no Vulkan/GLSL/volk/SPIRV/imgui/implot.
11. `0011-thirdparty-vma.patch` - no VulkanMemoryAllocator.
12. `0012-hw-core-null-machine.patch` - x86-only `none` machine (no ARM
    interfaces).
13. `0013-system-cpus.patch` - no TCG vCPU thread; the host calls `cpu_exec`.

The archives build and the XCN0 tests pass:

```bash
cd native/xgridcpu && ./build.sh && ./xgc_test.exe
# XCN0 C SPIKE PASS (100k steps, RAM, real MMIO callbacks, registers)

./xgc_ring_test.exe
# XCN1 MMIO SHADOW+RING C PASS (shadow reads, queued writes, 100k steps)

$env:PATH = 'C:\msys64\mingw64\bin;' + $env:PATH
$env:XGRIDCPU_HOST = (Resolve-Path native/xgridcpu/xgridcpu-host.exe).Path
dotnet run --project tools/xcn1-host-probe -c Release
# XCN1 NATIVE HOST PASS (shadow + live MMIO/PIO, IRQ, 241.8 Msteps/s)

$env:XGRIDCPU_DLL = (Resolve-Path native/xgridcpu/xgridcpu.dll).Path
dotnet run --project tools/xcn0-probe -c Release
# XCN0 CSHARP SPIKE PASS (vCPU/RAM/step)
```

The native shadow+ring path passes in C, and the **native host process**
(`xgridcpu-host.exe` + `tools/xcn1-host-probe`) passes end-to-end: shared
guest RAM, a shared read shadow, a write ring, an injected hardware vector,
100,000-step correctness and 235.5 Msteps/s on a 100M self-loop. C# never
calls the DLL, so the .NET Control Flow Guard boundary is gone. The
.NET-hosted DLL MMIO path remains a documented CFG-blocked diagnostic; the
native host is the product architecture. The C# vCPU/RAM/step DLL probe still
passes for reference.

## Host control block (XCN1)

The section is created by C#; the layout is `RAM at 0`, then the control area
at a C#-chosen offset passed as `xgridcpu-host.exe <section> <ctrl-offset>`.
The header holds `ram_size`, `shadow_size`, `ring_capacity` and the window
table; the shadow area starts at `ctrl + 0x250` and the ring and registers
follow it.

| Offset | Field |
|---|---|
| 0 | `magic` (`'XGCH'`) |
| 4 | `version` (5) |
| 8 | `ready` (host sets 1 after RAM/window mapping) |
| 12 | `quit` |
| 16 | `status` (1 = run requested, 2 = stopped) |
| 20 | `run_budget` (instructions) |
| 24 | `executed` (u64, this slice) |
| 32 | `rc` |
| 36 | `ring_count` |
| 40 | `irq_pending` (C# sets 1) |
| 44 | `irq_vector` (legacy; the ack callback wins) |
| 48 | `reset` (1 = architectural reset, 2 = real mode at 0x1000:0x8000) |
| 52 | `nwindows` |
| 56 | `windows[24]` - `{base, size, flags, shadow, priority}` (20 bytes each) |
| 536 | `io_state` (0 idle, 1 request, 2 done) |
| 540 | `io_flags` (bit0: port I/O, bit31: interrupt acknowledge) |
| 544 | `io_addr`, 548 `io_size`, 552 `io_value` (u64), 560 `io_icount` (u64) |
| 568 | `ram_size`, 572 `shadow_size`, 576 `ring_capacity`, 584 `dropped` (u64) |

Window flags: `1` = the base is an I/O port, `2` = live reads, `4` = direct,
`8` = RAM alias (aliases the RAM at gpa 0, wrapping every `ram_size` bytes).

- **Shadow window** (`0`): reads return bytes from the section shadow that C#
  refreshes between slices. Use it for ROM/flash and registers whose value is
  a pure function of device state at slice boundaries.
- **Live window** (`2`): a read publishes `{addr, size, flags, icount}` with
  `io_state = 1` and spins; C# (which is already polling for the slice to end)
  drains the write ring in order and answers into `io_value` with
  `io_state = 2`. This makes device registers - and device **time**, via
  `io_icount` - exact at the access.
- **Direct window** (`4`): the shadow slice is plain memory
  (`memory_region_init_ram_ptr`), no callback. `priority` orders overlaps.
- **RAM alias** (`8`): `memory_region_init_alias` over the RAM region, in
  RAM-sized pieces, so the 128 MB mirror and the 0x80000000 contiguous alias
  cost no extra memory.

Writes are always queued (1024 entries) and are published to the section
**before** a live read is announced, so a read observes every earlier write in
order. `dropped` counts writes lost to a full ring (a too-large slice).

## Boot ROM probe (XCN2)

```powershell
$env:XGRIDCPU_HOST = (Resolve-Path native/xgridcpu/xgridcpu-host.exe).Path
dotnet run --project tools/xcn1-boot-probe -c Release -- `
  data/xemu/Complex_4627.bin data/xemu/mcpx_1.0.bin 200000000
```

The probe maps 64 MB RAM, the 1 MB BIOS at `0xF0000000`, its 16 MB alias at
`0xFF000000` and the overlay at `0xFFF00000` (BIOS + the 512-byte MCPX ROM at
the top), then runs an architectural reset from `0xFFFFFFF0`. The 1BL enters
protected mode (`CS=0x0008`), reads the BIOS xcode stream at `0xFF000080`
through the mirror (`ESI=0xFF00025D`), programs the chipset over PCI config
space and reaches the **AMD756 SMBus handshake at I/O ports
`0xC000`-`0xC00A`**, polling `in al, 0xC000` for status `0x10` (the SMC). With
no device windows mapped it retries forever - the expected stop, and exactly
the device the C# machine already models (`Amd756Smbus` + `SmcDevice`).

Measured: 200M steps in 0.797 s = **251 Msteps/s** (TB cache hot, ROM only).

### Interrupt acknowledge

`xgc_set_irq_callback()` installs a vector callback: when the CPU takes a
hardware interrupt it calls the device model, so a masked line or `IF=0` never
acknowledges the PIC. `xgridcpu-host.exe` implements it as another live
request (`io_flags = 0x80000000`); the C# side answers with
`Pic8259.Acknowledge()`.

## Machine seam probe (XCN2)

```powershell
$env:XGRIDCPU_HOST = (Resolve-Path native/xgridcpu/xgridcpu-host.exe).Path
dotnet run --project tools/xcn2-smc-probe -c Release -- `
  data/xemu/Complex_4627.bin data/xemu/mcpx_1.0.bin 1000000000
```

`tools/xcn2-smc-probe` adds the whole 64 KB I/O space as one live window
dispatched to the **real C# `XboxBus`**: `PciBus` (0xCF8/0xCFC, which programs
the AMD756 BAR), `Amd756Smbus` + `SmcDevice`, `Pic8259`, `Pit8254` and
`Rtc146818`. Between slices it calls `Bus.Advance(executed)` (1 instruction =
1 CPU cycle) and raises `irq_pending` while the PIC has a pending line.

Result at 1B steps: the boot chain runs through the 1BL, the SMC handshake and
19 SMBus transactions, and reaches the **kernel idle loop at
`EIP=0x8001B02E`** (the same address the managed engine idles at) with **194
interrupts delivered and acknowledged** through the C# PIC. 188.7 Msteps/s on
this I/O-heavy leg.

## Machine probe (XCN3)

```powershell
$env:XGRIDCPU_HOST = (Resolve-Path native/xgridcpu/xgridcpu-host.exe).Path
dotnet run --project tools/xcn3-machine-probe -c Release -- `
  data/xemu/Complex_4627.bin data/xemu/mcpx_1.0.bin `
  data/xemu/xbox_hdd.qcow2 data/xemu/eeprom.bin 400000000 1000000
```

`tools/xcn3-machine-probe` runs the **full `XboxMachine`** on the native CPU:
guest RAM *is* the section (host-backed `PhysicalMemory`), every
`AddressSpace` region becomes a window (`RamAliasDevice` → region alias,
flash/mirror/overlay → direct from `IImmutableImageDevice.Image`, NV2A/APU/
ACI/USB/nvnet → live), the whole 64 KB I/O space is one live window, and each
slice calls `Bus.Advance(executed)` so the PIT/RTC/APU/NV2A keep time. A
halted CPU (`executed == 0`) advances device time by the slice so a timer can
wake it.

## Measured seam costs (H1, 2026-11-22)`tools/xcn1-host-probe`, real-mode loops of 100,000 accesses:

| Path | Cost |
|---|---|
| RAM load | **25 ns** |
| MMIO write (queued) | **1843 ns** |
| MMIO read (shadow) | **1850 ns** |
| MMIO read (live, cross-process) | **3943 ns** |
| C# answer inside a live read | **27 ns** |
| 100M-step self-loop | **241.8 Msteps/s** |

The ~1.85 us is QEMU's I/O path, not the boundary: an MMIO instruction runs in
a dedicated `CF_MEMI_ONLY` translation block (`cpu_io_recompile`), which is
QEMU's design and applies to xemu too. The process boundary adds ~2 us on top
for live reads; shadow windows avoid it, and the C# service itself is ~27 ns.
At the recorded ~1M device accesses per 30B 007 steps, even the live path is
~2 s per 30B (about 6% of guest time at 1x).

`XGRID_CPU_TRACE=1` prints the slice trace (icount, EIP, request word,
pending vector, EFLAGS/hflags, exception) and enables QEMU's `int` log.

### Interrupts

`xgc_set_irq(cpu, vector)` ORs `CPU_INTERRUPT_HARD` into the vCPU's request
word and stores the vector for `pic_read_irq()`. Two constraints found on the
way:

- Do **not** call QEMU's `cpu_interrupt()` from the embedder: it asserts that
  the BQL is held and then kicks a vCPU thread that does not exist (aborts).
  The request word is the whole contract in a single-threaded host.
- The CPU must **not** own a local APIC. `x86_cpu_realizefn()` creates one
  when CPUID_APIC is set (Pentium III does), and `cpu_get_pic_interrupt()`
  asks the LAPIC first, so every vector is consumed before `pic_read_irq()`
  and delivery faults. The shim clears `CPUID_APIC` before realize, which is
  also what the Xbox has: the interrupt controller is the MCPX 8259 pair.

`XGRID_CPU_TRACE=1` prints the slice trace (icount, EIP, request word,
pending vector, EFLAGS/hflags, exception) and enables QEMU's `int` log.

Measured on the 100M-step self-loop: **246.9 Msteps/s from C** (0.405 s) and
**235.5 Msteps/s across the process boundary** (0.425 s), ~4 ns/step
(~0.32x guest time on this trivial loop). This is 3.2x the current raw JIT
(73 Msteps/s) and the first engine rate in the xemu class; a real title
workload adds x87/SSE, TB churn and devices, so 0.5x (365 Msteps/s) is the
next measured gate.

None of these changes guest behaviour. A release ships the patches and the
corresponding QEMU source (or a written offer).
