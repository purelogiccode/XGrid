# XGrid

> **CI is disabled.** This repository is currently private and the account's
> GitHub Free plan has no GitHub Actions available for it, so
> `.github/workflows/` is manual-only. Every gate still runs — locally: see
> [Build and test](#build-and-test) and `docs/ci.md`. The **GPL-2.0 licence is
> open source**; the repository publication checklist is `docs/open-source.md`.

XGrid is a cross-platform original Xbox emulator written in **C#/.NET**. It is
licensed under the **GNU General Public License, version 2** (`COPYING`) and may
port code from GPL-compatible reference projects, primarily **xemu 0.8.139** and
its QEMU base. Ported files retain their original copyright and license notices;
see `THIRD_PARTY_NOTICES.md` and `docs/porting-policy.md`.

The emulator itself stays **full managed C#**: no QEMU/xemu native library is
embedded, and no C/C++ emulator code is part of the build. **C# first:** P/Invoke
is allowed only for host APIs or OS facilities (OpenGL, XInput, executable
memory for the raw JIT, host audio), and every edge is inventoried in
`docs/porting-policy.md` §2.

## Program: full xemu port, playable first

The engineering program is a **full source port of xemu 0.8.139 to managed C#**,
sequenced so retail titles become playable as early as possible. The module
ledger is [`docs/xemu-port-manifest.md`](docs/xemu-port-manifest.md); the task
plan is `ImplementationPlan.md` E1 (XFP0-XFP14). **The objective is at least one
game playable (P1).** The gates are **M1** 007's own render/input milestone
(met), **P1** at least one game playable, **P2/P3** a second and third title
(breadth after P1), **P4** seven of seven and **P5** the ledger complete.
**Playable** means own render, live input, ≥ 0.5× guest time, 10 minutes,
save/load, the xemu frame policy and **live host audio** - not an offline WAV.

**The fast path is a P1 sprint** on the current engine: **P1S0** measures 007
(wall per 1B, guest speed, presented fps, stage split), **P1S1** adds a live NuGet
audio sink and the AC'97 codec, **P1S2** fixes the measured speed blockers to
≥ 0.5×, and **P1S3** runs the nine-part gate. **NuGet host packages are allowed**
(C# first; the native edges are inventoried in `docs/porting-policy.md`). The
full xemu port starts after P1.

## Status

The LLE console chain boots: MCPX ROM -> 1BL -> 2BL -> kernel -> dashboard XBE.
The NV2A runs a software rasterizer and a host-GL backend; the CPU has an
accurate interpreter and a fast raw-JIT engine; the Avalonia frontend and CLI
are usable and ship as self-contained packages. The APU voice processor, the
ported xemu 0.8.139 GP/EP DSP core and the VP mixer execute the guest microcode
and produce real PCM; the AC'97 codec path (XFP6) completes the live audio.

**Real games: no title is playable yet.** The acceptance set is the **seven
optimized ISOs** in `H:\Tests\XBOXReady`; the broad library is **`E:\Microsoft
Xbox` (945 retail CHDs, 1.44 TB)**, used for title triage and the CHD path, not
for gates. **007 - Agent Under Fire** reaches its
own first-level driving scene, takes scripted pad input, round-trips a save state
and matches the xemu 0.8.139 frame (`docs/compatibility.md`), but it runs at
**0.11× guest time** (12.85 s per 1B steps) and its audio is an offline WAV - so
it is a milestone (M1), not playability. The other six boot with no fault at the
1B matrix budget: **Halo**, **Ninja Gaiden Black**, **Tak** and **50 Cent**
render their own content at their anchors; **Bad Boys II** and **Destroy All
Humans!** are documented waits. The first playable title needs the ported GL
renderer (XFP1-XFP5), live AC'97 audio (XFP6) and, if the measurement says the
CPU is the wall, the CPU core (XFP11) - see the speed checkpoint in E1. The
verified homebrew surface is the corpus and the compatibility matrix in
`docs/compatibility.md`; local retail probes are recorded in
`docs/research-notes/real-game-probes.md`. The gates are run locally (CI is
disabled; see `docs/ci.md`). The repository stays **private until a game is
playable** (`docs/open-source.md`).

## Goals

- **Playable retail titles first** (P1-P3), then seven of seven (P4)
- **Full xemu 0.8.139 source port** to managed C#, module ledger complete (P5)
- C# 15 / .NET 11, NativeAOT-compatible core where practical
- Windows, Linux, and macOS support, with reproducible self-contained packages
- CPU: accurate interpreter + raw x86-64 JIT now; ported `softmmu`/TCG (XFP11)
- Full Xbox hardware port: MCPX, NV2A, APU/AC'97, IDE, USB/OHCI/XID, SMBus, nvnet
- FATX and XDVDFS support, XBE loading
- Graphics: portable software rasterizer as the exact oracle; ported OpenGL
  renderer (Vulkan later)
- Debugger, tracing, deterministic tests, and fuzzing
- Reproducible releases with a reviewed license set (`docs/release-licensing-review.md`)

## Documentation

- `ImplementationPlan.md` - full implementation plan; the current program is E1 (XFP)
- `docs/xemu-port-manifest.md` - the full-port module ledger and out-of-scope list
- `docs/open-source.md` - GPL-2.0 status and the publication checklist
- `docs/porting-policy.md` - how a port is taken, tested and recorded
- `docs/ci.md` - continuous integration (currently disabled), the quality gates, their local equivalents and the release process
- `docs/release-licensing-review.md` - GPL-2.0 and third-party licensing review behind the packaged release
- `docs/release-notes/` - release notes, one file per version
- `docs/research-notes/` - extracted facts from reference projects and the licensing inventory

## Repository layout

```text
src/          emulator core and device projects (XGrid.Core, XGrid.Cpu, ...)
tests/        unit, integration, compatibility, and performance tests
benchmarks/   BenchmarkDotNet benchmarks
tools/        developer tools (added in later phases)
scripts/      build/test/format scripts
docs/         architecture and research notes
```

## Build and test

```powershell
dotnet build XGrid.sln -c Release
dotnet test XGrid.sln -c Release
dotnet format XGrid.sln --verify-no-changes
```

These commands are the gate while CI is disabled. `docs/ci.md` lists the rest of
it (InspectCode, package audit, coverage floors, deterministic build) as local
equivalents, and what has to be restored before the workflows run again.

## Desktop frontend

The cross-platform Avalonia GUI lives in `src/XGrid.Frontend.Avalonia`:

```powershell
dotnet run --project src/XGrid.Frontend.Avalonia
```

It provides power/pause/stop with a live software-rendered display, a disc library,
a settings surface (firmware, storage, memory, performance, network), an activity log,
and runtime accent themes. Headless `XGrid.Cli` remains the default for gate and
automation runs. See `src/XGrid.Frontend.Avalonia/README.md` and
`docs/research-notes/avalonia-frontend.md`.

## Release packages

The release tree (CLI + frontend, self-contained for Windows, Linux and macOS,
with the GPL-2.0 license set, release notes, checksums and a manifest) is built
by one command:

```powershell
pwsh -File scripts/package.ps1
```

Release notes live in `docs/release-notes/`; the licensing obligations behind the
packaged file set are reviewed in `docs/release-licensing-review.md`. The
`release.yml` workflow runs the same steps when CI is re-enabled.

## Firmware

XGrid does not ship copyrighted firmware, BIOS images, EEPROMs, or game data. Users must supply
their own legally obtained files.

## License

XGrid is free software under the **GNU General Public License, version 2**.
See `COPYING` for the full text and `LICENSE` for the project notice.
Third-party reference projects and ported modules are recorded in
`THIRD_PARTY_NOTICES.md`, `docs/porting-policy.md` and
`docs/research-notes/sources.md`. Ported files keep their original copyright
and license notices.
