# Headless runs and the command line

Headless mode runs the console without the XGrid window. The core still boots
and renders off-screen: the display sink publishes frames into a shared section
and XGrid counts them, so a run can be part of a script, a benchmark or a
regression job without presenting a picture. The same command line can override
settings for one session without changing `settings.json`.

## Quick start

Boot a disc for 30 seconds and write a JSON report:

```text
XGrid.Frontend.Avalonia.exe --headless --seconds 30 --summary run.json --disc "D:\Games\Halo.iso"
```

Stop after 1,800 published frames instead of a wall-clock limit:

```text
XGrid.Frontend.Avalonia.exe --headless --frames 1800 --disc "D:\Games\Halo.iso"
```

A frame limit has no default time cap; add `--seconds` as well when the guest
might never reach the frame count. With neither limit, the run stops after 60
seconds.

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
settings, so a normal launch afterwards uses the configured values again. On
Linux and macOS, use the executable name without `.exe`.

If `--disc` is omitted, a configured default disc is used. With no disc at all,
the core boots the dashboard from the hard-disk image.

## What the run prints

```text
xgrid: core=D:\repo\xemu\dist\xemu.exe
xgrid: disc=D:\Games\Halo.iso
xgrid: renderer=OPENGL scale=1 filtering=linear filter=nearest size=1280x960
xgrid: audio=none
xgrid: frames=1800 seconds=30.04 firstFrame=1.20 fps=60.02 stop=frames exit=0
xgrid: summary=D:\Bench\halo.json
```

| Field | Meaning |
| --- | --- |
| `frames` | Frames the core published from its rendering loop. |
| `seconds` | Wall-clock run length, including start-up and shutdown. |
| `firstFrame` | Seconds from process start to the first published frame. |
| `fps` | Frames per second between the first and last frame, so boot time does not dilute a benchmark. |
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
  "stop": "frames",
  "exitCode": 0,
  "result": 0
}
```

The folder is created when it does not exist. A summary write failure is
reported and turns the process exit code into `1`.

## Diagnostics

A headless run writes the same Serilog rolling log as the shell and sends one
best-effort usage hit at launch. Warnings and errors still reach the bug-report
queue. The console report and the JSON summary are the stable machine-readable
output; the log file has the exception and core details. See
[Logging and diagnostics](diagnostics.md).

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

The full list is in [Configuration](../reference/configuration.md). The switches
are case-insensitive and accept both `--name value` and `--name=value`.

| Area | Switches |
| --- | --- |
| Launch | `--disc`, `--core`, `--data-dir`, `--bios`, `--mcpx`, `--eeprom`, `--hdd` |
| Graphics | `--renderer`, `--scale`, `--filtering`, `--filter`, `--aspect`, `--fit` |
| Machine | `--ram`, `--audio` |
| Headless | `--seconds`, `--frames`, `--summary` |

`--seconds`, `--frames` and `--summary` select headless mode even when
`--headless` is omitted. A positional disc path also works when it ends in
`.iso`, `.xiso`, `.chd` or `.zar`; it implies `--autostart`. Use `--` to stop
parsing and pass the rest to Avalonia unchanged.

## Host notes

- Headless mode uses the XGrid display sink. A stock upstream xemu binary has
  neither the sink nor the POSIX shared-memory transport, so it publishes no
  frames.
- The core still creates an OpenGL context for off-screen rendering. A host with
  a working desktop driver is enough on Windows; on Linux and macOS the SDL video
  backend must be able to create a hidden window (use Xvfb on a server without a
  display).
- Audio defaults to the `none` backend for a headless run. Pass `--audio wav` to
  capture to `audio.wav` in the core working directory instead.
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
