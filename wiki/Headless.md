# Headless and command line

`XGrid.Frontend.Avalonia` accepts settings overrides and can run the console
without opening the shell window. The core still boots and renders off-screen;
the run counts published frames for scripts and benchmarks.

## Examples

Boot a disc for 30 seconds:

```text
XGrid.Frontend.Avalonia.exe --headless --seconds 30 --disc "D:\Games\Halo.iso"
```

Stop after 1,800 frames and write the JSON report:

```text
XGrid.Frontend.Avalonia.exe --headless --frames 1800 --summary run.json --disc "D:\Games\Halo.iso"
```

Benchmark with a fixed frame budget (600 frames by default):

```text
XGrid.Frontend.Avalonia.exe --benchmark --summary bench.json --disc "D:\Games\Halo.iso"
```

Use explicit firmware and an isolated data folder:

```text
XGrid.Frontend.Avalonia.exe --headless --seconds 20 ^
  --data-dir D:\Bench\data ^
  --core xemu\dist\xemu.exe ^
  --bios data\xemu\Complex_4627.bin ^
  --mcpx data\xemu\mcpx_1.0.bin ^
  --eeprom data\xemu\eeprom.bin ^
  --hdd D:\Bench\hdd.qcow2 ^
  --disc D:\Games\Halo.iso
```

A positional `.iso`, `.xiso`, `.chd`, `.cci`, `.zar` or Chihiro `.bin` path is a
`--disc` shortcut and powers on. A `.bin` uses the firmware configured in
**Settings → System → Sega Chihiro**.

## Common switches

| Switch | Meaning |
| --- | --- |
| `--disc <path>` | Disc image to insert and boot. |
| `--core <path>` | Emulator binary for this run. |
| `--data-dir <path>` | Shell data folder for this run. |
| `--bios`, `--mcpx`, `--eeprom`, `--hdd` | Firmware and storage overrides. |
| `--renderer <OPENGL\|VULKAN\|D3D11\|NULL>` | Core renderer. |
| `--scale <1-8>` | Internal surface scale. |
| `--filtering <linear\|nearest>` | Core texture filtering. |
| `--filter <nearest\|bilinear\|scanline\|crt>` | Presentation filter. |
| `--aspect`, `--fit`, `--ram`, `--audio`, `--audio-device` | Display, memory and audio overrides. |
| `--headless`, `--benchmark`, `--seconds`, `--frames`, `--summary` | Headless controls. |
| `-h`, `--help`, `--version` | Reference and version. |

Use `--` to stop parsing and pass the rest to Avalonia. Overrides are not saved to
`settings.json`. `--seconds`, `--frames`, `--summary` and `--benchmark` imply
`--headless`.

Without a limit, a headless run stops after 60 seconds and a benchmark stops
after 600 frames. A frame limit with no time cap runs until that many frames.

## Output and exit codes

```text
xgrid: renderer=OPENGL scale=1 filtering=linear filter=nearest size=1280x960
xgrid: audio=none
xgrid: frames=1800 seconds=30.04 firstFrame=1.20 fps=60.02 frameTime=16.66ms stop=frames exit=0
```

| Code | Meaning |
| --- | --- |
| `0` | Clean run. |
| `1` | Core failure or an unwritable summary. |
| `2` | Invalid switches, firmware or media. |
| `3` | No frames were published. |

Audio defaults to the `none` (null) sink. The hard disk is written at shutdown,
so benchmark jobs should use a copy. Off-screen rendering still needs a working
OpenGL context; on a Linux server without a display, run under Xvfb.

Full guide: [Command line and headless](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/headless.md)
and [Configuration](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/configuration.md).
