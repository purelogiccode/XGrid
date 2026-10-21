# frontend-smoke — headless frontend session harness

Boots the dashboard (or a supplied disc) through the frontend's
`EmulatorSession` with a chosen NV2A renderer and reports the picture, the
throughput and a save/load round trip. It exists because the renderer switch
(decisions §220) is otherwise only reachable from the desktop UI: this exercises
the same code path — GL context created, used and disposed on the emulation
thread, snapshots served on that thread, frames delivered through
`FramePacket` — and prints the log lines the app shows on the Activity page, so a
renderer fallback is visible.

It is **not** part of `XGrid.sln` and never gates a merge. Gate runs stay in
`XGrid.Cli`.

## Run

```powershell
dotnet build tools\frontend-smoke\frontend-smoke.csproj -c Release
dotnet run --project tools\frontend-smoke -c Release --no-build -- software 45
dotnet run --project tools\frontend-smoke -c Release --no-build -- gl 45
```

Arguments: `<software|gl> [seconds] [disc-path]`. Defaults: `software`, 45 s, the
configured HDD dashboard. Exit code 0 means at least one frame reached the UI
seam.

Media: `data/xemu-0.8.136-windows-x86_64` (firmware) and
`wt-data/chain-199l/hdd.qcow2` (dashboard HDD), the same files
`scripts/raster-leg.ps1` uses. `scripts/host-doctor.ps1` verifies both.

## Output

```text
LOG  Console powered on · 64 MB RAM · raw block JIT + idle-spin skip · ...
LOG  Renderer: OpenGL backend (replace=on, readback=on) - probe ok; GL_VERSION=4.6.0 NVIDIA 610.88
RESULT renderer=gl frames=1570 steps=1162936320 presented=1334 bgra=1228800 nonBlack=306817 msps=25.8 wall=45.0s
SAVED ...\wt-data\frontend-smoke\state-gl.xgs (72,113,938 bytes)
LOADED ...
DONE running=False steps=1163132928
```

`nonBlack` is the count of non-black pixels in the last delivered frame; it is
the cheap picture check (a backend that renders nothing shows 0). `msps` is
guest throughput over the run, which is what the renderer switch is for.

## Recorded measurement (2026-10-21, H1, dashboard, fast mode)

Interleaved pairs, same window, 40 s each:

| renderer | steps | msps | frames | nonBlack |
|---|---|---|---|---|
| software | 1,014,366,208 | 25.3 | 1477 | 306,817 |
| gl (replace) | 1,121,189,888 | 28.0 | 1398 | 306,817 |
| software | 1,020,985,344 | 25.5 | 1457 | 306,817 |
| gl (replace) | 1,105,920,000 | 27.6 | 1337 | 306,817 |

**+9.8% / +8.2% guest throughput** (both pairs the same sign), with an identical
picture (`nonBlack` is a stable 306,817 for every leg). A 20 s window - the boot
and intro only, before the menu draws - gains just +4.5%, because the software
rasterizer is not yet the bottleneck; the CLI's 1B menu leg (same renderer
switch, no UI capture) is the ceiling at **33.9 s -> 23.5 s (-31%)**. Replacement
mode intentionally skips the software fill, so its guest **RAM** SHA differs from
the software leg (the CPU SHA does not) - that is the documented replace-mode
contract, not a regression.
