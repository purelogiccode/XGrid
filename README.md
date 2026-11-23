# XGrid

> **CI is disabled.** This repository is currently private and the account's
> GitHub Free plan has no GitHub Actions available for it, so
> `.github/workflows/` is manual-only. Every gate still runs — locally: see
> [Build and test](#build-and-test) and `docs/ci.md`. The **GPL-2.0 licence is
> open source**; the repository publication checklist is `docs/open-source.md`.

XGrid is a cross-platform original Xbox emulator. It is licensed under the
**GNU General Public License, version 2** (`COPYING`).

**Architecture (operator decision 2026-11-23, "A"): XGrid is the shell and the
xemu 0.8.139 codebase is the emulation core.** The emulator itself — QEMU TCG
i386, the NV2A and its OpenGL renderer, the MCPX/APU/ACI/IDE/USB devices, audio,
input and netplay — is the vendored xemu codebase, used in place rather than
re-implemented. XGrid provides the application: the Avalonia frontend, settings,
library, per-title overrides, input mapping, snapshot orchestration, the
diagnostics/corpus tooling and the packaging pipeline.

This repository is an **independent project, not a GitHub fork**: `CSharp_XGrid`
is canonical, xemu is vendored under `native/xemu/` (GPL-2.0) with XGrid's
changes tracked as a patch set in `native/xemu-patches/`, so a new xemu release
is a patch rebase rather than a re-port. xemu and QEMU are credited here and in
`THIRD_PARTY_NOTICES.md`; the release ships their source (or a written offer)
and our patches, as GPL-2.0 requires.

The earlier program — a **full source port of xemu to managed C#** — is now the
**reference and oracle**, not the product path: the managed emulator
(`--cpu managed`), its four 1B goldens, the ktest byte-identical log and the
full test suite stay in the tree to validate the core and catch regressions.

## Program: playable first, xemu core as the performance path

The program is **playable first on the xemu core**. The shell launches the
vendored core, embeds its display and drives it from settings, the library and
per-title overrides; the managed emulator remains the oracle. The gates are
**M1** 007's own render/input milestone (met), **P1** at least one game
playable, **P2/P3** a second and third title, **P4** seven of seven and **P5**
the ledger complete. **Playable** means own render, live input, ≥ 0.5× guest
time, 10 minutes, save/load, the xemu frame policy and **live host audio** - not
an offline WAV.

**Current integration step: A3 (media).** A0-A2 are done: `XGrid.Core.Xemu`
generates the core configuration, starts the process, embeds its window (or
presents the core's frame sink), and forwards the shell pad. A3 is half done:
entry-form `.zar` archives play, while an `--iso` archive needs the in-memory
XDVDFS synthesizer. Next: A4 (display filters) and A5 (release pass).

**Media formats.** The core reads ISO/XISO natively (QEMU block layer). CHD and
ZAR are **not** block formats the core can mount, so they are added as real-time
drivers rather than an extraction step: a tracked patch in `native/xemu-patches/`
implements `xgridchd` (vendored libchdr) and `xgridzar` (zstd, already a QEMU
dependency), and the guest reads the disc directly. This is a patch inside this
repository - not a GitHub fork - which is why the README credits the codebase.

## Status

The product path is the maintained xemu core: the shell starts it, embeds its
display and drives it from settings, the library and per-title overrides. ISO,
CHD and entry-form ZAR discs mount directly in the core; 007 boots from all
three. The current task queue is A3 (XDVDFS synthesizer for `--iso` ZAR), then
A4 (display filters) and A5 (release pass).

The managed C# emulator is **the oracle, not the product**: it boots the LLE
console chain (MCPX ROM -> 1BL -> 2BL -> kernel -> dashboard), runs the
software and OpenGL NV2A backends, the raw-JIT CPU and the APU/DSP mixer, and
keeps the four accurate 1B goldens, the ktest log and the 2,200-test suite
green. `XGrid.Cli` remains its command-line harness.

**Media.** The acceptance set is the **seven optimized ISOs** in
`H:\Tests\XBOXReady`; the broad library is **`E:\Microsoft Xbox` (945 retail
CHDs, 1.44 TB)**, used for triage and the CHD path, never for gates. The
verified homebrew corpus and the title matrix are in `docs/compatibility.md`;
retail probes are in `docs/research-notes/real-game-probes.md`. CI is disabled
and every gate runs locally (`docs/ci.md`). The repository stays **private
until a game is playable** (`docs/open-source.md`).

## Goals

- **Playable retail titles** on the maintained xemu core
- **XGrid owns the application**: shell, settings, library, per-title overrides,
  input mapping, display filters, diagnostics and packaging
- **The core stays a patch set**: `native/xgrid-core` + `native/xemu-patches`
  over the pinned xemu 0.8.139 baseline, so an upstream release is a rebase
- **The managed emulator stays the oracle**: four accurate 1B goldens, the
  ktest log and the full test suite validate core changes
- ISO/XISO, CHD and ZAR media without extraction; FATX and XDVDFS readers
- Windows, Linux, and macOS support, with reproducible self-contained packages
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
src/          the shell (XGrid.Frontend.Avalonia, XGrid.Core.Xemu, XGrid.Input,
              XGrid.Xbe, XGrid.XboxFs) and the managed oracle projects
native/       xemu patches, xgrid-core additions and the disposable build tree
tests/        unit, integration, compatibility, and performance tests
benchmarks/   BenchmarkDotNet benchmarks
tools/        oracle, DSP/CPU vector and ktest tooling
scripts/      build/test/format, core sync/build and packaging scripts
docs/         architecture, status and research notes
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

It provides power/stop for the embedded xemu core, a disc library, a settings
surface (firmware, storage, guest memory, display filter), per-title overrides,
an activity log and runtime accent themes. `XGrid.Cli` remains the harness for
the managed oracle and the automation scripts. See
`src/XGrid.Frontend.Avalonia/README.md` and
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
