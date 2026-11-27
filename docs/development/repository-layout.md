# Repository layout

```text
XGrid.sln                     solution
Directory.Build.props         shared build settings (warnings as errors, version)
src/
  XGrid.Frontend.Avalonia/    the desktop shell
  XGrid.Core.Xemu/            shell ↔ core integration
  XGrid.Diagnostics/          Serilog, bug reports, statistics, updates, donations
  XGrid.Input/                pad model and mapping
  XGrid.Xbe/                  XBE header/certificate reader
  XGrid.XboxFs/               FATX/XDVDFS readers
  XGrid.Cli/                  validation CLI (managed oracle harness)
  XGrid.Core/                 managed reference machine
  XGrid.Cpu/                  managed CPU oracle
  XGrid.Memory/               managed memory oracle
  XGrid.Storage/              managed storage oracle
  XGrid.Bus/                  managed device oracle
  XGrid.Nv2a/                 managed GPU oracle
  XGrid.GpuGl/                managed GL oracle
  XGrid.Graphics/             managed graphics helpers
  XGrid.Audio/                managed audio oracle
xemu/
  xgrid-core/                 XGrid's native additions
  hw/xbox/chihiro/            ported Sega Chihiro machine
  block/blkmemory.c           Chihiro DIMM memory-backed block driver
  xemu-patches/               patch record for upstream edits
  XGrid.Xemu.Tree.csproj      solution anchor for the native build
  dist/                       native build output (git-ignored)
tests/                        unit, integration and compatibility tests
tools/                        diagnostics and oracle tooling
benchmarks/                   BenchmarkDotNet projects
scripts/                      build, test, format, core, package and check scripts
docs/                         documentation site and reference
wiki/                         GitHub-wiki-ready pages
data/                         local firmware/media (git-ignored)
```

## Product layer

- `src/XGrid.Frontend.Avalonia` — the shell. Cover matching, the local index and
  the online catalog client live in `Services/Covers/`.
- `src/XGrid.Core.Xemu` — launch planning, generated configuration, process and
  in-process hosting, display/input sinks, playback and save-state wrappers.
- `src/XGrid.Diagnostics` — Serilog configuration, the warning/error bug-report
  queue, the startup statistics call, the GitHub update check and the donation
  verification client.
- `xemu/` — the native core tree.
- `scripts/build-native-xemu.ps1` — core build (Windows/MSYS2).
- `scripts/build-native-xemu.sh` — core build (Linux).
- `scripts/package.ps1` — release tree.
- `scripts/package-covers.ps1` — build the cover catalog with ffmpeg.
- `scripts/publish-covers.ps1` — mirror the cover catalog to R2 with rclone.

## Validation layer

The managed validation oracle projects (`XGrid.Core`, `XGrid.Cpu`,
`XGrid.Memory`, `XGrid.Storage`, `XGrid.Bus`, `XGrid.Nv2a`, `XGrid.GpuGl`,
`XGrid.Graphics`, `XGrid.Audio`) and `XGrid.Cli` are retained as a **reference
oracle** for regression tests and differential checks. They are not part of the
shipped emulator.

`tests/` covers both layers. The tests are designed to run without firmware or
game data. See [Testing](testing.md).

## Documentation

- `docs/` — the user manual, reference, development notes and legal pages. The
  Docsify entry point is `docs/index.html`; the sidebar is `docs/_sidebar.md`.
- `wiki/` — shorter GitHub-wiki pages that link to the full manual.
- `README.md` — project landing page and quick start.
- `CONTRIBUTING.md`, `SECURITY.md` — repository policies.

## Rules

- `data/`, `dist/`, `publish/`, `cores/` (per-RID core staging) and native build
  trees are git-ignored.
- Never commit firmware, BIOS images, EEPROMs, HDD images or game data.
- Core edits belong in `xemu/xgrid-core/`; upstream file edits are recorded in
  `xemu/xemu-patches/`.
- Keep documentation next to the behavior it describes.
- `References2/`, when present, is a read-only oracle. Do not modify it.
