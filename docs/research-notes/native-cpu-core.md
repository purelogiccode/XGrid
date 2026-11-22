# Native CPU core (XCN0-XCN3)

Status: **2026-11-22, XCN0 active.** Operator decision: the product CPU is the
vendored xemu 0.8.139 / QEMU 10.2.0 TCG i386 core behind `libxgridcpu`; every
other emulator component stays managed C#. This note records the architecture,
the build, the spike and the measured risks. The plan is `ImplementationPlan.md`
E0N; the status is `docs/status.json`.

## Why

- 007 at 30B steps is 99.5% CPU: 413.254 s wall, GL 1.724 s, devices 0.481 s.
- The raw JIT is 13.7 ns/step (0.100x); 0.5x needs 2.7 ns/step; the managed
  TCG port reached 66-90 ns/step (0% of the required speedup, P1S5).
- The reference TCG is the only measured engine at ~1.2 ns/step and it is
  portable. Embedding it buys xemu-class CPU speed while the C# GPU, APU,
  devices, frontend and tests stay.
- It is **not** native 5-10x; WHP/KVM is out of scope. The target is ~1x on
  007 and xemu-class behaviour.

## Architecture

```
C# machine (XboxMachine, AddressSpace, devices, NV2A, APU, frontend)
        |  ICpuCore  (src/XGrid.Cpu.Native)
        v
libxgridcpu (native/xgridcpu)  -- C ABI --
        |   xgridcpu_create / map_ram / map_mmio / run(n) / set_irq
        |   get_state / set_state / flush_tb
        v
vendored xemu 0.8.139 = QEMU 10.2.0 (target/i386, accel/tcg, tcg/, exec/,
system/memory) -- no QEMU machine, devices, block, ui, audio or net
```

- Guest RAM is one pinned buffer mapped at GPA 0 (64/128 MB); the C# devices
  read it through the existing `IGpuMemory`/`IGuestMemory` adapters with no
  copy. The NV2A pushbuffer stays in RAM and is parsed by the C# pusher.
- The vCPU runs in a **native host process** against a named shared section.
  MMIO is a QEMU `MemoryRegionOps` whose read handler returns bytes from a
  shared **read shadow** and whose write handler queues an `XgcMmioAccess` in a
  ring; the C# device model services the ring and refreshes the shadow between
  run slices. No managed code is called from the TCG/softmmu stack (the
  .NET-hosted DLL path is blocked by Control Flow Guard).
- Interrupts: `Pic8259` (the existing ELCR/level model) drives the host's
  `irq_pending`/`irq_vector` fields; `xgc_set_irq` raises `CPU_INTERRUPT_HARD`
  and `pic_read_irq()` returns the queued vector at the next instruction
  boundary when IF=1. The CPU has **no local APIC** (CPUID_APIC cleared), as on
  the console.
- Timing: `--cpu native` advances devices by the executed-instruction count
  (or the icount virtual clock). `--cpu managed` keeps the exact step model.
- Snapshots: a native profile stores the `CPUX86State` architectural fields
  (GPRs, segments, FPU/SSE/MMX, MSRs, TSC) plus the C# device state; TBs are
  flushed on load. The exact `.xgs` remains the managed profile.

## Build (H1)

Toolchain: MSYS2 **MINGW64** (`C:\msys64\mingw64`): gcc 16.1.0, meson 1.12.1,
ninja 1.13.2, python 3.14.10, glib 2.88.1, pixman 0.46.4, zlib 1.4, libepoxy,
xxhash, diffutils, PyYAML. `C:\mingw64`, `C:\ninja`, `C:\cmake`, `C:\Qt` are
standalone fallbacks. Build in a `MSYSTEM=MINGW64` shell.

```
cp -r References2/xemu-0.8.139 native/qemu   # git-ignored; never edit References2
cd native/qemu
./configure --target-list=i386-softmmu --without-default-features --enable-tcg \
  --disable-werror --disable-docs --disable-tools --disable-sdl --disable-opengl \
  --disable-curl --disable-slirp
ninja -C build qemu-system-i386.exe
```

Local patches (recorded in `native/qemu-patches/`; all build-tooling only):

1. `configure`: MSYS2 `ensurepip` is broken, so use the system Python and
   `/mingw64/bin/meson` instead of a `pyvenv`.
2. `subprojects/dsp56300/meson.build`: create the extraction directory and use
   `tar --force-local -xzf` (GNU tar treats `D:` as a remote host).
3. `ui/meson.build`: do not build xemu's UI.
4. `hw/9pfs/meson.build`: no 9p backend (MinGW has no `sys/resource.h`).
5. `hw/meson.build`: `hw/core` only (no QEMU device models).
6. `hw/core/meson.build`: no EIF/libcbor/libfdt (device tree).
7. `tests/meson.build`: skip the xemu test tree.

The CPU/memory subset comes from the normal QEMU archives, not a full
executable: `libqemu-i386-softmmu.a` (target/TCG), `libsystem.a`
(system/memory/core), `libqemuutil.a`, `libqom.a`, `libio.a`, `libcrypto.a`.
`libxgridcpu` links them plus the shim; the full QEMU tree (and its devices)
is never shipped. The `hw/core` dependency is only `cpu_create`,
`qdev_realize` and the `none` machine; XCN1 should replace patch 5 with a
custom meson target.

The spike's standalone init sequence (no `qemu_init`, no machine run) is:
`qemu_init_cpu_list()` -> `module_call_init(MODULE_INIT_QOM)` -> create a
`none-machine` object and publish it as `root/machine` (so `qdev_get_machine()`
resolves) -> `cpu_exec_init_all()` -> `tcg_allowed = true` ->
`page_init()` -> `tb_htable_init()` -> `tcg_init(32 MiB, 0, 1)` ->
`tcg_prologue_init()` -> `use_icount = ICOUNT_PRECISE` with
`timers_state.icount_time_shift = 0`. `cpu_create("pentium3")` then works and
`cpu_exec()` runs on the icount budget.

## Spike (XCN0) acceptance

From C#: create a vCPU; map 64 MB at GPA 0 and a 4 KB MMIO region with
callbacks; run a real-mode stub that writes RAM, reads and writes the MMIO
word; enter protected mode and run a loop; inject an IRQ and observe the
handler; return on a step budget and resume. Measure steps/s and callback
cost; project the 007 leg. **Stop** if the CPU cannot be driven standalone
without the QEMU machine/main loop, or if the projection cannot reach
2.7 ns/step on 007.

## XCN0 result (2026-11-22)

The host toolchain is MSYS2 MINGW64 (`C:\msys64\mingw64`); `C:\mingw64`,
`C:\ninja`, `C:\cmake` and `C:\Qt` are fallbacks. The vendored tree configures
and builds with the thirteen recorded patches (configure venv, dsp56300 tar,
UI, 9pfs, `hw` scoping, EIF/FDT, tests, backends, Vulkan/GLSL/imgui/implot/VMA,
the x86-only `none` machine, and the single-threaded `qemu_init_vcpu`). The
CPU/memory/TCG archives build; `libxgridcpu` links filtered copies plus the
explicit QOM/machine/CPU objects.

The standalone init path is: `qemu_init_subsystems()` (locks, BQL, RCU, QOM
module list, runstate) -> a `none-machine` object with the `unattached` /
`peripheral` / `peripheral-anon` containers -> a dummy `AccelOpsClass` with
no-op `create_vcpu_thread`/`handle_interrupt` -> `accel_init_machine()` ->
`cpu_address_space_init(cpu, 0, ...)` -> `cpu_reset()`. `CF_USE_ICOUNT` and
`cpu->icount_budget = chunk` make the TB prologue count guest instructions;
`xgc_run()` returns at the budget or at `EXCP_HALTED`.

**Both smoke tests pass:**

- `native/xgridcpu/xgc_test.exe`: 100,000-step budget, RAM store, **real MMIO
  read/write callbacks** (0xCAFEBABE at 0xF0000004), EAX/ECX/EIP exact.
- `native/xgridcpu/xgc_ring_test.exe`: the XCN1 shadow+ring path — reads from
  a native shadow window, writes queued and drained by the host, 0xCAFEBABE at
  0xF0000004, 100,000-step budget.
- `tools/xcn0-probe` (C#): vCPU, RAM, registers and the 100,000-step budget
  from managed code; the 0xF0000000 window is RAM in this leg.

**Measured engine rate:** a 100M-step self-loop runs at **246.9 Msteps/s from
C** (0.405 s) and **247.5 Msteps/s from C#** (0.404 s) — 4.05 ns/step, ~0.34x
guest time on this trivial loop. That is 3.4x the current raw JIT
(73 Msteps/s) and in the xemu engine class; a title workload will be slower
(x87/SSE, TB churn and devices), and 0.5x is 365 Msteps/s. The next measurement
is the 007 boot leg once the MMIO ring and the machine seam exist.

**XCN1 native host PASS (2026-11-22).** The CPU now runs in a native process
(`native/xgridcpu/xgridcpu-host.exe`) attached to a named shared section: 1 MB
guest RAM plus a control block (read shadow, write ring, budget/status and
registers). `tools/xcn1-host-probe` creates the section, starts the host, sets
the read shadow, signals a 100,000-step slice, drains the write ring and
checks the registers; a following 100M self-loop measures **235.5 Msteps/s**
across the process boundary (0.425 s). C# never calls the DLL, so the .NET
Control Flow Guard boundary is gone. The .NET-hosted DLL MMIO path remains the
documented CFG-blocked diagnostic; the host is the product architecture.

**XCN1 interrupt line (2026-11-22).** The shared control block now carries
`irq_pending`/`irq_vector` and `reset`. The probe loads a real-mode IVT
handler, sets `sti`, waits for a flag, injects vector `0x20` from C# and checks
that the handler ran: **PASS** (flag=1, EIP past the wait loop, EFLAGS restored
by `iret`). Two findings made it work:

- QEMU's `cpu_interrupt()` asserts `bql_locked()` and then kicks a vCPU thread.
  A single-threaded embedder must OR `CPU_INTERRUPT_HARD` into the request word
  itself and never kick (the first attempt `abort()`ed at the assert).
- `x86_cpu_realizefn()` creates a local APIC whenever CPUID_APIC is set, and
  `cpu_get_pic_interrupt()` returns from the LAPIC before `pic_read_irq()`, so
  the injected vector was consumed by an uninitialised LAPIC and real-mode
  delivery raised `#GP` (`exc=13`, zero instructions executed). Clearing
  `CPUID_APIC` before `qdev_realize` both fixes delivery and matches the
  console, whose interrupt controller is the MCPX 8259 pair.

The 100M self-loop still measures 235-245 Msteps/s across the boundary with
the IRQ slice in place.

**Next (XCN1):** synchronous MMIO reads (a request/response slot the native
handler spins on while C# services it) or a per-window shadow refresh policy;
map every Xbox window (NV2A, APU/ACI, PCI config, ROM/flash) and the PIO
space; then the machine seam (PIC/PIT/APU/NV2A) and the 007 boot.

## Risks / open questions

1. **Standalone init.** QEMU has no system-mode library target. The shim must
   call the QOM type init, the TCG init, the TB-cache init and `cpu_create`
   without `qemu_init`/the machine. This is the spike's main unknown.
2. **Step budget.** `cpu_exec()` has no public "run N instructions" parameter;
   the icount decrementer or a small `cpu_exec` patch is the candidate. The
   spike measures both.
3. **PIC vector hook.** QEMU's x86 CPU fetches the vector through its PIC
   interface; the C# PIC needs a small hook (or QEMU's i8259 with C# IRQ
   lines). The spike uses a synthetic vector first.
4. **Determinism.** xemu's TCG is host-clock based and not bit-exact run to
   run. The product's gates are xemu parity + the device/GPU corpus; the
   managed engine keeps the four accurate 1B goldens and the ktest log.
   A deterministic native mode (icount/TSC patch) is optional and priced
   separately.
5. **Fork maintenance.** Pinned to xemu 0.8.139 / QEMU 10.2.0; upgrades are a
   deliberate rebase of `native/qemu-patches/`, not a rolling dependency.
6. **Security.** Native code now executes untrusted guest behaviour. QEMU is
   designed for that, but the managed-only memory-safety posture changes; the
   release notes must say so.

## Licence

GPL-2.0 stays. QEMU and xemu are GPL-2.0, so embedding is compatible; a
permissive relicense is not. The release must ship the corresponding QEMU
source (or a written offer), the build patches and the notices
(`THIRD_PARTY_NOTICES.md`, `docs/open-source.md`).

## Gates

- `--cpu managed`: the four accurate 1B goldens, the ktest byte-identical log,
  the cpu-oracle and the existing corpus. Unchanged.
- `--cpu native`: xemu parity (007 frame policy, boot-to-gameplay, live audio,
  input, save/load), the device/GPU corpus, no fault, and a recorded one-time
  re-baseline of any CPU-dependent anchors with the measured reason.
