# xemu 0.8.139 full-port manifest

Status: **2026-11-22, operator direction.** XGrid's immediate program is a
**P1 sprint** (one playable game fast, on the existing engine, NuGet host
packages allowed); the long-run program is a **full source port of xemu 0.8.139
to managed C#** for breadth and fidelity. This file is the ledger: every
in-scope xemu/QEMU module has a row, a target C# file (or "new"), a status and
the differential that proves it. `ImplementationPlan.md` -> `E1` is the task
plan; this file is the coverage record.

Reference identity (do not drift):

- xemu **0.8.139**, commit `aa54c34cae1c09410d74c69a44210a9867fe2d16`,
  2026-10-21 (QEMU 10.2.0 base).
- Prior snapshots `0.8.137` / `0.8.136` are cross-checks only; a port cites
  0.8.139 unless the file is byte-identical and the older note says so.
- License: GPL-2.0 overall; `hw/xbox/**` per-file LGPL-2+/MIT/GPL-2+.
  Every port keeps its notice and is added to `THIRD_PARTY_NOTICES.md`
  (`docs/porting-policy.md`).

## 1. What "full port" means

"Full" is defined by **scope**, not by file count:

1. Every Xbox-facing module in `hw/xbox/**` is ported (the machine, MCPX, APU,
   NV2A, SMBus/SMC/EEPROM, XID, nvnet, PCI).
2. Every QEMU module the Xbox machine depends on is ported where XGrid does not
   already implement the same behavior faithfully: `hw/ide`, `hw/usb` (OHCI +
   core), `hw/audio/ac97`, `hw/display`, `hw/intc/i8259`, `hw/timer/i8254` and
   `mc146818rtc`, `hw/i2c`, `hw/pci`, `hw/core`, `hw/input`, the `softmmu`/
   `system` memory dispatch, `accel/tcg` + `tcg` + `target/i386`, `migration`
   (snapshots), `block` (qcow2/raw paths used by the machine), `net` (hub/slirp
   as used), and the `ui`/`audio`/`input` host backends.
3. The port is **source-level and managed C#**. No QEMU/xemu native library is
   linked, and no C/C++ emulator code enters the build
   (`docs/porting-policy.md` §2). The reference tree under `References2/` is
   read-only and is never shipped.
4. The port replaces **implementations**, not the product shell: the CLI, the
   Avalonia frontend, `XGrid.Host`, the snapshot format, the test harness and
   the gates stay.

**Out of scope, with reasons** (a row must stay in this table to stay out):

| Area | Reason |
|---|---|
| `hw/xbox/nv2a/pgraph/vk/**` | Host-renderer choice. The port targets the GL renderer; Vulkan stays a possible later backend, not hardware behavior. |
| `hw/xbox/chihiro*`, `chihiro-usb.c` | Sega Chihiro arcade hardware, not an Xbox console. Kept as a fact source only (`xemu-Playtest-v2`). |
| `linux-user/`, `bsd-user/`, `qga/`, `storage-daemon/`, `tools/`, `tests/`, `scripts/`, `subprojects/`, `rust/`, `docs/`, `po/`, `contrib/`, `debian/` | QEMU host packaging/test infrastructure, not the emulated machine. |
| `qapi/`, `qobject/`, `qom/` code generation | A port uses plain C# objects; the reference's object model is not behavior. Individual state structs are ported into the C# types that own them. |
| `pc-bios/`, `roms/`, firmware images | User-supplied at run time and never committed; the port reads the same firmware the current engine reads. |
| `gdbstub/`, `monitor/`, `disas/` | Debugger surfaces. XGrid has its own debugger; port them only if a task needs a reference behavior. |
| `authz/`, `crypto/`, `io/`, `chardev/`, `fsdev/`, `nbd/`, `scsi/`, `semihosting/`, `replay/`, `plugins/`, `ebpf/`, `dump/` | Not reachable from the Xbox machine as xemu configures it. Revisit only with a measured dependency. |

## 2. Completion metric

`port completion = verified in-scope modules / all in-scope modules`.

A module is **verified** only when all of these hold:

1. The C# implementation exists and is reachable in the machine path (behind a
   flag until the differential passes).
2. A differential exists: a reference vector table (DSP model), a synthetic
   register/method test with pre/post state, or a title/corpus row that fails
   before the port and passes after it.
3. The four accurate 1B goldens and the ktest log stay byte-identical, or the
   task records the measured re-baseline with the reason.
4. `THIRD_PARTY_NOTICES.md` and `docs/research-notes/sources.md` name the port.
5. `pwsh -File scripts/port-manifest.ps1 -Check` is green (XFP0).

Status key: `not-started`, `in-progress` (own implementation exists but is not
the reference port), `ported` (translated, differential pending), `verified`,
`out-of-scope`, `n/a` (host backend choice).

### 2.1 Differential class per module type

Every in-scope module names one of three differential classes; the appendix
below records it per file.

1. **Reference vector table** - the port is driven by vectors generated from
   the reference itself (the DSP model: 929 decode/ALU/interrupt vectors from
   `tools/dsp-oracle`; the texture oracle for S3TC). The port must match every
   vector exactly.
2. **Register/method pre-post table** - a synthetic or traced register/method
   sequence with the before/after state (PCI config, PIC/PIT/RTC registers,
   SMBus transactions, OHCI hub/port, NV2A methods). The port must reproduce
   the reference's latched state and side effects exactly.
3. **Title/corpus row** - a boot/gameplay/corpus row that fails before the port
   and passes after it (the four accurate 1B goldens, the ktest log, the
   22-row compatibility matrix, the 007/Halo/NGB title rows). Used where the
   module's behavior is only observable in a whole-frame or whole-session
   result.

The full-port rule: a module is `verified` only when its class-1/2 differential
is exact **and** the class-3 bundle (four accurate 1B goldens + ktest log)
stays byte-identical, or the task records the measured re-baseline.

### 2.2 `hw/xbox` per-file coverage

Section 7 is generated from `References2/xemu-0.8.139/hw/xbox` by
`scripts/port-manifest.ps1 -UpdateFileList`; it is the authoritative per-file
coverage (one row per reference file). The grouped tables in section 3 are the
human summary and must agree with it. `scripts/port-manifest.ps1 -Check`
verifies the file set, the target files, the port headers and the notices, and
prints the coverage. `docs/open-source.md` (the GPL-2.0 status and publication
checklist) is linked from the Documentation section of `README.md`.

## 3. `hw/xbox` ledger (149 files, 1,720 KB)

### 3.1 Root (24 files)

| Reference file | Target C# | Status | Differential |
|---|---|---|---|
| `xbox.c` | `XGrid.Host/XboxMachine.cs`, `XGrid.Bus/XboxBus.cs` | in-progress | 1B boot + ktest |
| `xbox.h` | same | in-progress | type/shape only |
| `xbox_pci.c` / `.h` | `XGrid.Bus/Pci/*` | in-progress | PCI config reads vs reference |
| `acpi_xbox.c` / `.h` | new | not-started | ACPI PM1 / SMBus wake path |
| `amd_smbus.c` / `.h` | `XGrid.Bus/Smbus/Amd756Smbus.cs` | in-progress | bus transaction vectors |
| `smbus.h` | same | in-progress | - |
| `smbus_xbox_smc.c` | `XGrid.Bus/Smbus/SmcDevice.cs` | in-progress | SMC command/reply vectors |
| `smbus_adm1032.c` | new | not-started | temperature device |
| `smbus_cx25871.c` | `XGrid.Bus/Smbus/ConexantCx25871.cs` | in-progress | video-encoder state |
| `smbus_fs454.c` | new | not-started | fan controller |
| `smbus_storage.c` | new | not-started | SMBus EEPROM/IPMI path |
| `smbus_xcalibur.c` | new | not-started | video encoder |
| `eeprom_generation.c` / `.h` | `XGrid.Bus/Smbus/Eeprom24c02.cs` (partial) | in-progress | EEPROM image vectors |
| `lpc47m157.c` | new | not-started | Super-I/O |
| `xblc.c` | new | not-started | boot-logic controller |
| `xid.c` / `xid.h` | `XGrid.Bus/Usb/XidGamepad.cs` | in-progress | XID descriptor/state |
| `xid-gamepad.c` | same | in-progress | pad report vectors |
| `chihiro.c`, `chihiro-usb.c` | - | out-of-scope | arcade hardware |

### 3.2 `mcpx/` (34 files: 1 root + 9 `apu/` + 12 `apu/dsp/` + 4 `apu/dsp/interp/` + 5 `apu/vp/` + 3 `nvnet/`)

| Reference file | Target C# | Status | Differential |
|---|---|---|---|
| `mcpx/apu/apu.c` / `apu.h` | `XGrid.Audio/McpxApuDevice.cs` | in-progress | register + frame vectors |
| `mcpx/apu/apu_regs.h` / `apu_int.h` | `McpxApuDevice` + `ApuVpFrame.cs` | ported | constants, TM8 |
| `mcpx/apu/fpconv.h` | `Vp/ApuVpFrame.cs` | verified | TM8 |
| `mcpx/apu/debug.c`, `monitor.c`, `trace.h`, `apu_debug.h` | - | n/a | debug/trace surfaces |
| `mcpx/apu/vp/vp.c` / `vp.h` | `McpxApuDevice.Vp.cs` | verified | TM8 PCM/DFT policy |
| `mcpx/apu/vp/adpcm.h` | `Vp/ApuVpAdpcm.cs` | verified | TM8 + ADPCM vectors |
| `mcpx/apu/vp/svf.h`, `hrtf.h` | `Vp/ApuVpFilters.cs` | verified | TM8 |
| `mcpx/apu/dsp/dsp.c`, `dsp_c.c`, `dsp.h`, `dsp_internal.h` | `Dsp/McpxApuDsp.cs` | verified | P1.3 guest-identical |
| `mcpx/apu/dsp/dsp_dma.c` / `.h` | `Dsp/DspDma.cs` | verified | P1.0/P1.2 vectors |
| `mcpx/apu/dsp/gp_ep.c` / `.h` | `Dsp/McpxApuDsp.cs` | verified | P1.3 |
| `mcpx/apu/dsp/dsp_jit.c` | - | n/a | optional JIT; interpreter is the port target |
| `mcpx/apu/dsp/interp/dsp_cpu.c`, `dsp_cpu.h`, `dsp_cpu_regs.h`, `dsp_emu.c.inc`, `debug.c`, `debug.h` | `Dsp/Dsp56300*.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/trace.h`, `dsp/trace.h` | - | n/a | trace surfaces |
| `mcpx/aci.c` | `XGrid.Bus/Aci/McpxAciDevice.cs` | in-progress | AC'97 codec vectors (XFP6) |
| `mcpx/nvnet/nvnet.c`, `nvnet_regs.h`, `trace.h` | `XGrid.Bus/Net/McpxNvNetDevice.cs` | in-progress | DHCP/ARP frame vectors (XFP8) |

### 3.3 `nv2a/` (21 files + `pgraph/` 16 + `gl/` 14 + `glsl/` 14 + `null/` 2 + `vk/` 21)

| Reference file | Target C# | Status | Differential |
|---|---|---|---|
| `nv2a.c` / `nv2a.h` / `nv2a_int.h` | `XGrid.Nv2a/Nv2aMmio.cs` + device core | in-progress | 1B method/IRQ counters |
| `nv2a_regs.h` | same | in-progress | register constants |
| `pbus.c`, `pfb.c`, `pcrtc.c`, `pramdac.c`, `prmcio.c`, `prmdio.c`, `prmvio.c`, `pvideo.c`, `pmc.c`, `ptimer.c` | `Nv2aMmio` partials | in-progress | per-block register vectors |
| `pfifo.c` | `Nv2aMmio` pusher | in-progress | pushbuffer decode vectors |
| `stubs.c`, `user.c`, `debug.c`, `debug.h`, `debug_gl.h`, `trace.h` | - | n/a | debug surfaces |
| `pgraph/pgraph.c` / `pgraph.h` | `Nv2aMmio` + `FixedFunctionVertexState.cs` | in-progress | method/state table (XFP1) |
| `pgraph/vertex.c` | `Nv2aMmio` inline/array fetch | in-progress | vertex vectors (XFP2) |
| `pgraph/texture.c` / `.h` | `TextureFormats.cs` + sampler | in-progress | format table (XFP4) |
| `pgraph/surface.h`, `swizzle.c` / `.h` | `Nv2aMmio` surface path | in-progress | surface/swizzle vectors |
| `pgraph/s3tc.c` / `.h` | `S3tcDecoder.cs` | verified | 16 block vectors |
| `pgraph/psh_regs.h`, `vsh_regs.h` | `PshCombiner.cs`, `VshProgram.cs` | ported | register decode |
| `pgraph/util.h` | `TextureFormats.cs` | ported | helpers |
| `pgraph/rdi.c`, `profile.c`, `debug_renderdoc.c` | - | n/a | debug/profiling |
| `pgraph/glsl/psh.c` | `PshCombiner.cs` | verified | live GL compile + corpus |
| `pgraph/glsl/vsh-prog.c` | `VshProgram.cs`, `VshProgramPlan.cs` | verified | VshProgramPlanTests |
| `pgraph/glsl/vsh-ff.c` | `FixedFunctionVertex.cs` | verified | `xgu *_ffp` rows |
| `pgraph/glsl/vsh.c`, `vsh.h` | `FixedFunctionVertexState.cs` | ported | decode |
| `pgraph/glsl/*.h` | `PshGlslGenerator`/`VshGlslGenerator` partials | in-progress | XFP3 |
| `pgraph/gl/**` (14 files, 220 KB) | `XGrid.GpuGl` replaced by the port | not-started | XFP5 |
| `pgraph/null/**` | - | n/a | null renderer |
| `pgraph/vk/**` (21 files, 451 KB) | - | out-of-scope | host renderer choice |

## 4. QEMU ledger (the modules the Xbox machine uses)

| Reference area | Target C# | Status | Differential |
|---|---|---|---|
| `hw/ide/{core,pci,atapi,cdrom}.c` | `XGrid.Storage/IdeController.cs`, `AtaDevice.cs` | in-progress | ATAPI probe + 27 READ DMA (XFP7) |
| `hw/usb/hcd-ohci.c` | `XGrid.Bus/Usb/OhciUsbController.cs` | in-progress | hub/port vectors (XFP9) |
| `hw/usb/core.c`, `dev-hub.c`, `dev-storage.c` | `UsbHub.cs` + device stubs | in-progress | XFP9 |
| `hw/audio/ac97.c` | new | not-started | PCM/file-register vectors (XFP6) |
| `hw/intc/i8259.c` | `XGrid.Bus/Interrupts/Pic8259.cs` | in-progress | ELCR/level vectors (TM2) |
| `hw/timer/i8254.c` | `XGrid.Bus/Timers/Pit8254.cs` | in-progress | mode/count vectors |
| `hw/timer/mc146818rtc.c` | `XGrid.Bus/Timers/Rtc146818.cs` | in-progress | register vectors |
| `hw/i2c/**` | `XGrid.Bus/Smbus/*` | in-progress | XFP8 |
| `hw/pci/pci.c`, `pci_host.c` | `XGrid.Bus/Pci/PciBus.cs` | in-progress | config-space vectors |
| `hw/core/**` (qdev helpers) | plain C# types | n/a | object model not ported |
| `hw/input/**` | frontend pad path | in-progress | XFP12 |
| `hw/display/**` | `Nv2aMmio` display model | in-progress | XFP4/XFP5 |
| `softmmu/**`, `system/memory.c` | `XGrid.Memory/AddressSpace.cs`, `Mmu.cs` | in-progress | XFP11 |
| `accel/tcg/**`, `tcg/**`, `target/i386/tcg/**` | `XGrid.Cpu` interpreter/raw JIT | not-started | XFP11 (cpu-oracle + ktest + 1B) |
| `migration/**` | `XGrid.Debug` snapshot (`.xgs`) | not-started | XFP13 round trip |
| `block/qcow2.c`, `block/raw-format.c` | `XGrid.Storage/Qcow2DiskImage.cs` | in-progress | image read vectors |
| `net/**` (hub/slirp as used) | `XGrid.Bus/Net/*` | in-progress | XFP8 |
| `ui/**`, `audio/**`, `input/**` | Avalonia/WGL/`WaveAudioBackend`/XInput | in-progress | XFP12 host smoke |
| `qom/**`, `qobject/**`, `qapi/**` | - | n/a | plain C# object model |

## 5. Port order and why

**P1 is the objective: at least one game playable.** The critical path is the
**P1 sprint** (P1S0-P1S3: measure 007, live NuGet audio + AC'97, speed to
0.5x, the nine-part gate), which uses the existing engine. The XFP order below
starts **after P1**; breadth (P2/P3, seven of seven) and the remaining
device/host ports follow. A full-port module is promoted into the sprint only
when P1S0's measurement proves it is the fastest fix.

1. **GPU** (XFP1-XFP5) - the largest fidelity gap and the blocker for Halo
   (a D3D resource wait the reference does not have), Ninja Gaiden Black (the
   fragment dither), and the ported GL renderer that makes every other visual
   row trustworthy.
2. **Audio** (XFP6) - the one silent part of the playability definition.
3. **Storage/IDE, SMBus/SMC, USB** (XFP7-XFP9) - the waits Bad Boys II and
   Destroy All Humans! show, and the save/load path.
4. **Machine assembly** (XFP10) - ties the ported devices into one machine and
   gives a single 1B differential.
5. **CPU/memory** (XFP11) - the largest single port, sequenced last because the
   existing engine is bit-exact and faster than xemu on the CPU-bound ktest;
   replacing it before the devices are ported would risk playability for no
   near-term gain. It is still in scope: the full port includes it.
6. **Host integration and snapshots** (XFP12-XFP13) - product surface.
7. **Parity close** (XFP14) - ledger 100%, seven titles, release.

## 6. Updating this ledger

- A port change updates the row's status **in the same commit** as the code.
- `scripts/port-manifest.ps1 -Check` (written by XFP0) verifies:
  - every `Ported from <project> <version> <path>` in `src/**` maps to a row;
  - every `ported`/`verified` row names an existing target file;
  - every in-scope reference module has a status (no silent omissions);
  - `THIRD_PARTY_NOTICES.md` names every ported module.
- `out-of-scope` rows need a reason in §1; deleting a row is not an escape.

## 7. hw/xbox per-file coverage

Generated from `References2/xemu-0.8.139/hw/xbox` by `scripts/port-manifest.ps1 -UpdateFileList`. The grouped ledger above is the human summary; this table is the authoritative per-file coverage.

<!-- HW-XBOX-FILES:BEGIN (generated by scripts/port-manifest.ps1 -UpdateFileList) -->

| Reference file | Target C# | Status | Differential class |
|---|---|---|---|
| `acpi_xbox.c` | `new` | not-started | ACPI PM1 / SMBus wake path |
| `acpi_xbox.h` | `new` | not-started | type/shape only |
| `amd_smbus.c` | `src/XGrid.Bus/Smbus/Amd756Smbus.cs` | in-progress | bus transaction vectors |
| `amd_smbus.h` | `src/XGrid.Bus/Smbus/Amd756Smbus.cs` | in-progress | type/shape only |
| `chihiro-usb.c` | `-` | out-of-scope | Sega Chihiro arcade hardware |
| `chihiro.c` | `-` | out-of-scope | Sega Chihiro arcade hardware |
| `eeprom_generation.c` | `src/XGrid.Bus/Smbus/Eeprom24c02.cs` | in-progress | EEPROM image vectors |
| `eeprom_generation.h` | `src/XGrid.Bus/Smbus/Eeprom24c02.cs` | in-progress | type/shape only |
| `lpc47m157.c` | `new` | not-started | Super-I/O |
| `mcpx/aci.c` | `src/XGrid.Bus/Aci/McpxAciDevice.cs` | in-progress | AC'97 codec vectors (XFP6) |
| `mcpx/apu/apu_debug.h` | `-` | n/a | debug/trace surface |
| `mcpx/apu/apu_int.h` | `src/XGrid.Audio/Vp/ApuVpFrame.cs` | ported | constants, TM8 |
| `mcpx/apu/apu_regs.h` | `src/XGrid.Audio/Vp/ApuVpFrame.cs` | ported | constants, TM8 |
| `mcpx/apu/apu.c` | `src/XGrid.Audio/McpxApuDevice.cs` | in-progress | register + frame vectors |
| `mcpx/apu/apu.h` | `src/XGrid.Audio/McpxApuDevice.cs` | in-progress | register + frame vectors |
| `mcpx/apu/debug.c` | `-` | n/a | debug/trace surface |
| `mcpx/apu/dsp/debug.h` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/dsp_c.c` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/dsp_dma_regs.h` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/dsp_dma.c` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/dsp_dma.h` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/dsp_internal.h` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/dsp_jit.c` | `-` | n/a | optional JIT / trace surface |
| `mcpx/apu/dsp/dsp.c` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/dsp.h` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/gp_ep.c` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/gp_ep.h` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/interp/debug.c` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/interp/dsp_cpu_regs.h` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/interp/dsp_cpu.c` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/interp/dsp_cpu.h` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/interp/dsp_dis.c.inc` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/interp/dsp_emu.c.inc` | `src/XGrid.Audio/Dsp/Dsp56300Core.cs` | verified | 929-vector oracle, P1.2 |
| `mcpx/apu/dsp/interp/meson.build` | `-` | n/a | build/trace metadata |
| `mcpx/apu/dsp/meson.build` | `-` | n/a | build/trace metadata |
| `mcpx/apu/dsp/trace-events` | `-` | n/a | build/trace metadata |
| `mcpx/apu/dsp/trace.h` | `-` | n/a | optional JIT / trace surface |
| `mcpx/apu/fpconv.h` | `src/XGrid.Audio/Vp/ApuVpFrame.cs` | verified | TM8 PCM |
| `mcpx/apu/meson.build` | `-` | n/a | build/trace metadata |
| `mcpx/apu/monitor.c` | `-` | n/a | debug/trace surface |
| `mcpx/apu/trace-events` | `-` | n/a | build/trace metadata |
| `mcpx/apu/trace.h` | `-` | n/a | debug/trace surface |
| `mcpx/apu/vp/adpcm.h` | `src/XGrid.Audio/McpxApuDevice.Vp.cs` | verified | TM8 PCM/DFT policy |
| `mcpx/apu/vp/hrtf.h` | `src/XGrid.Audio/McpxApuDevice.Vp.cs` | verified | TM8 PCM/DFT policy |
| `mcpx/apu/vp/meson.build` | `-` | n/a | build/trace metadata |
| `mcpx/apu/vp/svf.h` | `src/XGrid.Audio/McpxApuDevice.Vp.cs` | verified | TM8 PCM/DFT policy |
| `mcpx/apu/vp/vp.c` | `src/XGrid.Audio/McpxApuDevice.Vp.cs` | verified | TM8 PCM/DFT policy |
| `mcpx/apu/vp/vp.h` | `src/XGrid.Audio/McpxApuDevice.Vp.cs` | verified | TM8 PCM/DFT policy |
| `mcpx/meson.build` | `-` | n/a | build/trace metadata |
| `mcpx/nvnet/meson.build` | `-` | n/a | build/trace metadata |
| `mcpx/nvnet/nvnet_regs.h` | `src/XGrid.Bus/Net/McpxNvNetDevice.cs` | in-progress | DHCP/ARP frame vectors (XFP8) |
| `mcpx/nvnet/nvnet.c` | `src/XGrid.Bus/Net/McpxNvNetDevice.cs` | in-progress | DHCP/ARP frame vectors (XFP8) |
| `mcpx/nvnet/trace-events` | `-` | n/a | build/trace metadata |
| `mcpx/nvnet/trace.h` | `-` | n/a | trace surface |
| `meson.build` | `-` | n/a | build/trace metadata |
| `nv2a/debug_gl.h` | `-` | n/a | debug/stub surface |
| `nv2a/debug.c` | `-` | n/a | debug/stub surface |
| `nv2a/debug.h` | `-` | n/a | debug/stub surface |
| `nv2a/meson.build` | `-` | n/a | build/trace metadata |
| `nv2a/nv2a_int.h` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/nv2a_regs.h` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/nv2a.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/nv2a.h` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/pbus.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/pcrtc.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/pfb.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/pfifo.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/pgraph/debug_renderdoc.c` | `-` | n/a | debug/profiling surface |
| `nv2a/pgraph/gl/blit.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/constants.h` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/debug.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/debug.h` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/display.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/draw.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/gpuprops.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/meson.build` | `-` | n/a | build/trace metadata |
| `nv2a/pgraph/gl/renderer.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/renderer.h` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/reports.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/shaders.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/surface.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/texture.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/gl/vertex.c` | `src/XGrid.GpuGl` | not-started | GL renderer port (XFP5) |
| `nv2a/pgraph/glsl/common.c` | `src/XGrid.GpuGl` | in-progress | shader generator (XFP3) |
| `nv2a/pgraph/glsl/common.h` | `src/XGrid.GpuGl` | in-progress | shader generator (XFP3) |
| `nv2a/pgraph/glsl/geom.c` | `src/XGrid.GpuGl` | in-progress | shader generator (XFP3) |
| `nv2a/pgraph/glsl/geom.h` | `src/XGrid.GpuGl` | in-progress | shader generator (XFP3) |
| `nv2a/pgraph/glsl/meson.build` | `-` | n/a | build/trace metadata |
| `nv2a/pgraph/glsl/psh.c` | `src/XGrid.Nv2a/PshCombiner.cs` | verified | register/method table + live GL compile + corpus |
| `nv2a/pgraph/glsl/psh.h` | `src/XGrid.GpuGl/PshGlslGenerator.cs` | in-progress | shader generator (XFP3) |
| `nv2a/pgraph/glsl/shaders.c` | `src/XGrid.GpuGl` | in-progress | shader generator (XFP3) |
| `nv2a/pgraph/glsl/shaders.h` | `src/XGrid.GpuGl` | in-progress | shader generator (XFP3) |
| `nv2a/pgraph/glsl/vsh-ff.c` | `src/XGrid.Nv2a/FixedFunctionVertex.cs` | verified | xgu *_ffp corpus rows |
| `nv2a/pgraph/glsl/vsh-ff.h` | `src/XGrid.Nv2a/FixedFunctionVertexState.cs` | ported | decode |
| `nv2a/pgraph/glsl/vsh-prog.c` | `src/XGrid.Nv2a/VshProgram.cs` | verified | VshProgramPlanTests + corpus |
| `nv2a/pgraph/glsl/vsh-prog.h` | `src/XGrid.Nv2a/VshProgram.cs` | ported | register decode |
| `nv2a/pgraph/glsl/vsh.c` | `src/XGrid.Nv2a/FixedFunctionVertexState.cs` | ported | decode |
| `nv2a/pgraph/glsl/vsh.h` | `src/XGrid.Nv2a/FixedFunctionVertexState.cs` | ported | decode |
| `nv2a/pgraph/meson.build` | `-` | n/a | build/trace metadata |
| `nv2a/pgraph/methods.h.inc` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/null/meson.build` | `-` | n/a | build/trace metadata |
| `nv2a/pgraph/null/renderer.c` | `-` | n/a | null renderer |
| `nv2a/pgraph/null/renderer.h` | `-` | n/a | null renderer |
| `nv2a/pgraph/pgraph.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/pgraph.h` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/profile.c` | `-` | n/a | debug/profiling surface |
| `nv2a/pgraph/psh_regs.h` | `src/XGrid.Nv2a/PshCombiner.cs` | ported | register decode |
| `nv2a/pgraph/rdi.c` | `-` | n/a | debug/profiling surface |
| `nv2a/pgraph/s3tc.c` | `src/XGrid.Nv2a/S3tcDecoder.cs` | verified | 16 block vectors + texture oracle |
| `nv2a/pgraph/s3tc.h` | `src/XGrid.Nv2a/S3tcDecoder.cs` | verified | 16 block vectors + texture oracle |
| `nv2a/pgraph/surface.h` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/swizzle.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/swizzle.h` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/texture.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/texture.h` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/thirdparty/gloffscreen/common.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/thirdparty/gloffscreen/gloffscreen.h` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/thirdparty/gloffscreen/sdl.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/thirdparty/meson.build` | `-` | n/a | build/trace metadata |
| `nv2a/pgraph/util.h` | `src/XGrid.Nv2a/TextureFormats.cs` | ported | helpers |
| `nv2a/pgraph/vertex.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | register/method pre-post table (XFP1-XFP4) |
| `nv2a/pgraph/vk/blit.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/buffer.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/command.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/constants.h` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/debug.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/debug.h` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/display.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/draw.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/glsl.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/glsl.h` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/gpuprops.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/image.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/instance.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/meson.build` | `-` | n/a | build/trace metadata |
| `nv2a/pgraph/vk/renderer.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/renderer.h` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/reports.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/shaders.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/surface-compute.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/surface.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/texture.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vk/vertex.c` | `-` | out-of-scope | host renderer choice |
| `nv2a/pgraph/vsh_regs.h` | `src/XGrid.Nv2a/PshCombiner.cs` | ported | register decode |
| `nv2a/pmc.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/pramdac.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/prmcio.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/prmdio.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/prmvio.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/ptimer.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/pvideo.c` | `src/XGrid.Nv2a/Nv2aMmio.cs` | in-progress | 1B method/IRQ counters |
| `nv2a/stubs.c` | `-` | n/a | debug/stub surface |
| `nv2a/trace-events` | `-` | n/a | build/trace metadata |
| `nv2a/trace.h` | `-` | n/a | debug/stub surface |
| `nv2a/user.c` | `-` | n/a | debug/stub surface |
| `smbus_adm1032.c` | `new` | not-started | temperature device |
| `smbus_cx25871.c` | `src/XGrid.Bus/Smbus/ConexantCx25871.cs` | in-progress | video-encoder state |
| `smbus_fs454.c` | `new` | not-started | fan controller |
| `smbus_storage.c` | `new` | not-started | SMBus EEPROM/IPMI path |
| `smbus_xbox_smc.c` | `src/XGrid.Bus/Smbus/SmcDevice.cs` | in-progress | SMC command/reply vectors |
| `smbus_xcalibur.c` | `new` | not-started | video encoder |
| `smbus.h` | `src/XGrid.Bus/Smbus/SmbusDevice.cs` | in-progress | type/shape only |
| `xblc.c` | `new` | not-started | boot-logic controller |
| `xbox_pci.c` | `src/XGrid.Bus/Pci/PciBus.cs` | in-progress | PCI config reads vs reference |
| `xbox_pci.h` | `src/XGrid.Bus/Pci/PciBus.cs` | in-progress | type/shape only |
| `xbox.c` | `src/XGrid.Host/XboxMachine.cs` | in-progress | 1B boot + ktest |
| `xbox.h` | `src/XGrid.Host/XboxMachine.cs` | in-progress | type/shape only |
| `xid-gamepad.c` | `src/XGrid.Bus/Usb/XidGamepad.cs` | in-progress | pad report vectors |
| `xid.c` | `src/XGrid.Bus/Usb/XidGamepad.cs` | in-progress | XID descriptor/state |
| `xid.h` | `src/XGrid.Bus/Usb/XidGamepad.cs` | in-progress | type/shape only |

<!-- HW-XBOX-FILES:END -->
