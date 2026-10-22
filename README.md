# XGrid

[![CI](https://github.com/purelogiccode/XGrid/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/purelogiccode/XGrid/actions/workflows/ci.yml)
[![CodeQL](https://github.com/purelogiccode/XGrid/actions/workflows/codeql.yml/badge.svg?branch=master)](https://github.com/purelogiccode/XGrid/actions/workflows/codeql.yml)
[![Release](https://github.com/purelogiccode/XGrid/actions/workflows/release.yml/badge.svg)](https://github.com/purelogiccode/XGrid/actions/workflows/release.yml)

XGrid is a cross-platform original Xbox emulator written in C#/.NET, built as a clean-room
reimplementation of documented Xbox hardware behavior. It is not a port of xemu,
Cxbx-Reloaded, or xqemu.

## Status

Phase 0 (project bootstrap): solution, projects, tests, analyzers, and CI are being established.

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
- `docs/ci.md` - continuous integration, quality gates and the release process
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

A project license has not been finalized yet; see `LICENSE`. Third-party reference licenses are
recorded in `docs/research-notes/sources.md`.
