# Headless and command line

`XGrid.Frontend.Avalonia` accepts settings overrides and can run the console
without opening the shell window. The core still boots and renders off-screen;
the run counts published frames for scripts and benchmarks.

## Examples

Boot a disc for 30 seconds:

```text
XGrid.Frontend.Avalonia.exe --headless --seconds 30 --disc "D:\Games\Halo.iso"
```

Stop after 1,800 frames and write the JSON report (add `--seconds` for a time
cap):

```text
XGrid.Frontend.Avalonia.exe --headless --frames 1800 --summary run.json --disc "D:\Games\Halo.iso"
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

The normal shell accepts the same overrides without `--headless`. A positional
`.iso`, `.xiso`, `.chd` or `.zar` path is a `--disc` shortcut and powers on.

## Common switches

| Switch | Meaning |
| --- | --- |
| `--disc <path>` | Disc image to insert and boot. |
| `--core <path>` | Emulator binary for this run. |
| `--data-dir <path>` | Shell data folder for this run. |
| `--bios`, `--mcpx`, `--eeprom`, `--hdd` | Firmware and storage overrides. |
| `--renderer <OPENGL\|VULKAN\|NULL>` | Core renderer. |
| `--scale <1-8>` | Internal surface scale. |
| `--filtering <linear\|nearest>` | Core texture filtering. |
| `--filter <nearest\|bilinear\|scanline\|crt>` | Shell presentation filter. |
| `--aspect`, `--fit`, `--ram`, `--audio` | Display, memory and audio overrides. |
| `--headless`, `--seconds`, `--frames`, `--summary` | Headless run controls. |
| `-h`, `--help`, `--version` | Reference and version. |

Use `--` to stop parsing and pass the rest to Avalonia. Command-line overrides
are not saved to `settings.json`; they apply to the current session only.
`--seconds`, `--frames` and `--summary` imply `--headless`.

## Output and exit codes

```text
xgrid: renderer=OPENGL scale=1 filtering=linear filter=nearest size=1280x960
xgrid: audio=none
xgrid: frames=1800 seconds=30.04 firstFrame=1.20 fps=60.02 stop=frames exit=0
```

| Code | Meaning |
| --- | --- |
| `0` | Clean run. |
| `1` | Core failure or an unwritable summary. |
| `2` | Invalid switches, firmware or media. |
| `3` | No frames were published. |

Audio defaults to the `none` backend in a headless run. The hard disk is written
at shutdown, so benchmark jobs should use a copy of the image. Off-screen
rendering still needs a working OpenGL context; on a Linux server without a
display, run it under Xvfb.

Full details: [Headless runs](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/headless.md)
and [Configuration](https://github.com/purelogiccode/XGrid/blob/master/docs/reference/configuration.md).
