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

The section is `1 MB guest RAM + 0x10800` of control data. The CPU runs in
`xgridcpu-host.exe`; C# owns the section, the read shadows and the write ring.

| Offset | Field |
|---|---|
| 0 | `magic` (`'XGCH'`) |
| 4 | `version` (2) |
| 8 | `ready` (host sets 1 after RAM/window mapping) |
| 12 | `quit` |
| 16 | `status` (1 = run requested, 2 = stopped) |
| 20 | `run_budget` (instructions) |
| 24 | `executed` (u64, this slice) |
| 32 | `rc` |
| 36 | `ring_count` |
| 40 | `irq_pending` (C# sets 1) |
| 44 | `irq_vector` (0-255) |
| 48 | `reset` (1 = re-seat the CPU at 0x1000:0x8000) |
| 52 | `nwindows` |
| 56 | `windows[8]` - `{base, size, flags, shadow}` (16 bytes each) |
| 184 | `io_state` (0 idle, 1 request, 2 done) |
| 188 | `io_flags` (bit0: port I/O) |
| 192 | `io_addr`, 196 `io_size`, 200 `io_value` (u64), 208 `io_icount` (u64) |
| 0x100 | `shadow[0x10000]` - shadow windows are slices of this |
| 0x10100 | `ring[64]` - queued `XgcMmioAccess` writes |
| 0x10700 | `XgcRegs` (13 x u32) |

Window flags: `1` = the base is an I/O port, `2` = live reads.

- **Shadow window** (`0`): reads return bytes from the section shadow that C#
  refreshes between slices. Use it for ROM/flash and registers whose value is
  a pure function of device state at slice boundaries.
- **Live window** (`2`): a read publishes `{addr, size, flags, icount}` with
  `io_state = 1` and spins; C# (which is already polling for the slice to end)
  drains the write ring in order and answers into `io_value` with
  `io_state = 2`. This makes device registers - and device **time**, via
  `io_icount` - exact at the access.
- Writes are always queued and are published to the section **before** a live
  read is announced, so a read observes every earlier write in order.

## Measured seam costs (H1, 2026-11-22)

`tools/xcn1-host-probe`, real-mode loops of 100,000 accesses:

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
