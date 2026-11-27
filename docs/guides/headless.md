# Command line and headless runs

Headless mode runs the console without the XGrid window. The core still boots
and renders off-screen: the display sink publishes frames into a shared section
and XGrid counts them, so a run can be part of a script, a benchmark or a
regression job without presenting a picture.

The same command line can override settings for one session without changing
`settings.json`.

## Quick start

Boot a disc for 30 seconds and write a JSON report:

```text
XGrid.Frontend.Avalonia.exe --headless --seconds 30 --summary run.json --disc "D:\Games\Halo.iso"
```

Stop after 1,800 published frames instead of a wall-clock limit:

```text
XGrid.Frontend.Avalonia.exe --headless --frames 1800 --disc "D:\Games\Halo.iso"
```

On Linux and macOS, use the executable name without `.exe`.

## Benchmark runs

`--benchmark` is a headless run with a fixed frame budget so repeated runs are
comparable. With no limit it stops after 600 presented frames; `--frames` or
`--seconds` still takes precedence.

```text
XGrid.Frontend.Avalonia.exe --benchmark --disc "D:\Games\Halo.iso" --summary bench.json
```

Keep the renderer, scale, filtering and host identical between runs, and use a
copy of the hard-disk image so save data cannot change the result. The report
adds `frameTime`, the average interval between presented frames, next to `fps`.
Both are measured between the first and last frame, so boot time does not dilute
the result.

A frame limit has no default time cap; add `--seconds` as well when the guest
might never reach the frame count. With neither limit, a normal headless run
stops after 60 seconds.

## Isolated run

Run against explicit firmware and a scratch hard disk, using an isolated data
folder:

```text
XGrid.Frontend.Avalonia.exe --headless --seconds 20 ^
  --data-dir D:\Bench\data ^
  --core xemu\dist\xemu.exe ^
  --bios data\xemu\Complex_4627.bin ^
  --mcpx data\xemu\mcpx_1.0.bin ^
  --eeprom data\xemu\eeprom.bin ^
  --hdd D:\Bench\hdd.qcow2 ^
  --disc D:\Games\Halo.iso ^
  --summary D:\Bench\halo.json
```

The command line applies to the one process. It never rewrites the saved
settings, so a normal launch afterwards uses the configured values again.

If `--disc` is omitted, a configured default disc is used. With no disc at all,
the core boots the dashboard from the hard-disk image.

## What the run prints

```text
xgrid: core=D:\repo\xemu\dist\xemu.exe
xgrid: disc=D:\Games\Halo.iso
xgrid: renderer=OPENGL scale=1 filtering=linear filter=nearest size=1280x960
xgrid: audio=none
xgrid: frames=1800 seconds=30.04 firstFrame=1.20 fps=60.02 frameTime=16.66ms stop=frames exit=0
xgrid: summary=D:\Bench\halo.json
```

| Field | Meaning |
| --- | --- |
| `frames` | Frames the core published from its rendering loop. |
| `seconds` | Wall-clock run length, including start-up and shutdown. |
| `firstFrame` | Seconds from process start to the first published frame. |
| `fps` | Frames per second between the first and last frame, so boot time does not dilute a benchmark. |
| `frameTime` | Average milliseconds between presented frames (`1000 / fps`). |
| `stop` | `duration`, `frames`, `core` (the guest powered off) or `cancel` (Ctrl+C). |
| `exit` | The core's exit code. |

The frame count starts with the first published frame, so a short run spends a
larger share of its wall time in the boot path.

## JSON report

`--summary <path>` writes the same fields, plus the resolved settings:

```json
{
  "core": "D:\\repo\\xemu\\dist\\xemu.exe",
  "disc": "D:\\Games\\Halo.iso",
  "renderer": "OPENGL",
  "surfaceScale": 1,
  "filtering": "linear",
  "presentationFilter": "nearest",
  "width": 1280,
  "height": 960,
  "audio": "none",
  "frames": 1800,
  "seconds": 30.04,
  "firstFrameSeconds": 1.2,
  "fps": 60.02,
  "frameTimeMs": 16.66,
  "stop": "frames",
  "exitCode": 0,
  "result": 0
}
```

The folder is created when it does not exist. A summary write failure is
reported and turns the process exit code into `1`.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | The run completed and the core shut down cleanly. |
| `1` | The core failed to start, ended with an error, or did not shut down in time. |
| `2` | A switch, firmware path, media path or generated core configuration was rejected. |
| `3` | The core ran but published no frames; check the renderer and that the binary is the XGrid core. |

A run with `--renderer NULL` presents no picture. The display path still
publishes frames, so the run ends normally; use it for diagnostics, not for
visual output.

## Session overrides

The full list is in [Configuration](../reference/configuration.md). Switches are
case-insensitive and accept both `--name value` and `--name=value`.

| Area | Switches |
| --- | --- |
| Launch | `--disc`, `--core`, `--data-dir`, `--bios`, `--mcpx`, `--eeprom`, `--hdd` |
| Graphics | `--renderer`, `--scale`, `--filtering`, `--filter`, `--aspect`, `--fit` |
| Machine | `--ram`, `--audio`, `--audio-device` |
| Headless | `--headless`, `--benchmark`, `--seconds`, `--frames`, `--summary` |

`--seconds`, `--frames`, `--summary` and `--benchmark` select headless mode even
when `--headless` is omitted. A positional image path also works when it ends in
`.iso`, `.xiso`, `.chd`, `.cci`, `.zar` or `.bin`; it implies `--autostart`. A
`.bin` path boots the Sega Chihiro with the firmware configured in
**Settings → System → Sega Chihiro**. Use `--` to stop parsing and pass the rest
to Avalonia unchanged.

## CI recipe

A minimal Windows job can use a self-contained frontend plus a prepared data
folder:

```powershell
$env:XGRID_DISABLE_STATISTICS = '1'
$env:XGRID_DISABLE_UPDATE_CHECK = '1'
XGrid.Frontend.Avalonia.exe --headless --seconds 15 `
  --data-dir C:\ci\xgrid-data `
  --disc C:\ci\games\smoke.iso `
  --summary C:\ci\artifacts\smoke.json

if ($LASTEXITCODE -ne 0) { throw "XGrid smoke run failed: $LASTEXITCODE" }
```

Keep firmware and game images out of the repository; stage them as CI secrets
or pre-provisioned runner assets. See
[Quality gates](../development/quality-gates.md) for the repository's own
`headless-check.ps1` gate.

## Host notes

- Headless mode uses the XGrid display sink. A stock upstream xemu binary has
  neither the sink nor the POSIX shared-memory transport, so it publishes no
  frames.
- The core still creates an OpenGL context for off-screen rendering. A host with
  a working desktop driver is enough on Windows; on Linux and macOS the SDL video
  backend must be able to create a hidden window (use Xvfb on a server without a
  display).
- Audio defaults to the `none` (null) backend for a headless run. Pass
  `--audio wav` to capture to `audio.wav` in the core working directory instead,
  or `--audio-device` with a backend-specific device.
- The hard-disk image is written when the core shuts down, including on a
  duration or frame limit. Use a copy of the image for repeatable benchmark runs.
- The run uses the configured renderer unless `--renderer` overrides it. For a
  throughput benchmark, keep the real renderer and compare runs on the same host.
- The core and firmware must be the XGrid build and the files you supply; no
  firmware or game data is bundled.

## Related pages

- [Configuration](../reference/configuration.md) — every switch and file.
- [Display and filters](display.md) — presentation and renderer settings.
- [Architecture](../reference/architecture.md) — the display sink and hosting.
- [Logs and diagnostics](diagnostics.md) — the rolling log and opt-outs.
