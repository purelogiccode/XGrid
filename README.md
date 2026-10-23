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
are usable. Host audio and real-game compatibility are the active product gaps.
The gates are run locally (CI is disabled; see `docs/ci.md`).

## Goals

- C#/.NET 9+, NativeAOT-compatible core where practical
- Windows, Linux, and macOS support
- Custom Pentium III CPU emulation with an interpreter first and a JIT later
- Xbox MCPX, NV2A, APU, IDE, USB, SMBus and related hardware
- FATX and XDVDFS support, XBE loading
- Vulkan graphics backend (OpenGL optional)
- Debugger, tracing, deterministic tests, and fuzzing

## Documentation

- `ImplementationPlan.md` - full implementation plan (Part II is the detailed phase plan)
- `docs/ci.md` - continuous integration (currently disabled), the quality gates, their local equivalents and the release process
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
