# XGrid

> **CI is disabled.** This repository is private and the account's GitHub Free
> plan has no GitHub Actions available for it, so `.github/workflows/` is
> manual-only. Every gate still runs — locally: see
> [Build and test](#build-and-test) and `docs/ci.md`.

XGrid is a cross-platform original Xbox emulator written in **C#/.NET**. It is
licensed under the **GNU General Public License, version 2** (`COPYING`) and may
port code from GPL-compatible reference projects, primarily **xemu 0.8.139** and
its QEMU base. Ported files retain their original copyright and license notices;
see `THIRD_PARTY_NOTICES.md` and `docs/porting-policy.md`.

The emulator itself stays **full managed C#**: no QEMU/xemu native library is
embedded, and no C/C++ emulator code is part of the build.

## Status

The LLE console chain boots: MCPX ROM -> 1BL -> 2BL -> kernel -> dashboard XBE.
The NV2A runs a software rasterizer and a host-GL backend; the CPU has an
accurate interpreter and a fast raw-JIT engine; the Avalonia frontend and CLI
are usable and ship as self-contained packages. The APU voice processor and the
ported xemu 0.8.139 GP/EP DSP core execute the guest microcode (the VP mixer and
the AC'97 codec path are still missing, so output is not yet audible).

**Real games:** the oracle set is the **seven optimized ISOs** in
`H:\Tests\XBOXReady`. **007 - Agent Under Fire is playable** (2026-10-25, TM4):
it runs 100 billion instructions with no fault, presents its own first-level
driving scene, takes scripted pad input, round-trips a save state through the
frontend and matches the xemu 0.8.139 frame at the same scene within the
documented screenshot policy (`docs/compatibility.md`, decisions §255). Audio is
the remaining gap on that title - the voices are not mixed into the DSP output
yet (the VP mixer and the AC'97 codec are TM8) - so its PCM is a measured
silence. The other six boot with no fault at the 1B matrix budget: **Tak**
renders its own content, **Halo**, **Ninja Gaiden Black**, **50 Cent** and
**Bad Boys II** present the dashboard while their own XBE code executes, and
**Destroy All Humans!** is still in the kernel; Halo is the active TM5 target.
The verified homebrew surface is the corpus and the compatibility matrix in
`docs/compatibility.md`; local retail probes are recorded in
`docs/research-notes/real-game-probes.md`. The gates are run locally (CI is
disabled; see `docs/ci.md`).

## Goals

- C#/.NET 10, NativeAOT-compatible core where practical
- Windows, Linux, and macOS support, with reproducible self-contained packages
- Custom Pentium III CPU emulation: accurate interpreter plus a raw x86-64 JIT
- Xbox MCPX, NV2A, APU, IDE, USB, SMBus and related hardware
- FATX and XDVDFS support, XBE loading
- Graphics backends: portable software rasterizer and host OpenGL (Vulkan later)
- Debugger, tracing, deterministic tests, and fuzzing
- Reproducible releases with a reviewed license set (`docs/release-licensing-review.md`)

## Documentation

- `ImplementationPlan.md` - full implementation plan (Part II is the detailed phase plan)
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
