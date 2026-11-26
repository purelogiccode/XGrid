# Sega Chihiro

XGrid includes the Sega Chihiro machine. It boots **netboot `.bin` images**
(raw FATX), not Redump GD-ROM `.cue` sets or MAME `.chd` files directly.

## Firmware

Put your own dumps in one folder:

- `chihiro_xbox_bios.bin` (512 KB)
- `fpr21042_m29w160et.bin` (2 MB, media-board flash/SEGABOOT)
- `ic10_g24lc64.bin` (8 KB)
- `ic11_24lc024.bin` (128 bytes)
- `pc20_g24lc64.bin` (8 KB)

`ver1305.bin` (network firmware) is optional for a single cabinet.

Open **Settings -> System -> Sega Chihiro**, choose the folder, and turn on
**Enable Chihiro**. A `.bin` launch selects the Chihiro even when the switch is
off. The machine uses 128 MB of RAM and no Xbox hard disk.

## Converting an Xbox-format Chihiro ISO

The `XBOXOG - Chihiro` patched ISOs already contain the decrypted Chihiro file
tree. Use **Settings -> System -> Sega Chihiro -> Convert Chihiro ISO...**, or:

```text
XGrid.Cli.exe chihiro-netboot --image "Ghost Squad (XBOXOG).iso" --out "ghost-squad.bin"
```

The converter writes a FATX netboot image and aliases a renamed executable
(`vsg.xbe` -> `vsg.xbe.bak`/`default.xbe`) when the patch renamed it.

## Converting a GD-ROM

Raw Redump/MAME GD-ROM sets need
[chihiro-netboot](https://github.com/Tovarichtch/chihiro-netboot) with the
game's MAME key `.zip` and its `.cue`/`.bin` or `.chd`. Put the resulting
`.bin` in a library folder and launch it. XGrid bundles no keys, firmware or
game data.

## Controls

| Action | Key |
| --- | --- |
| Start | Enter |
| Service | L |
| Coin | 5 |
| Test | K |
| Steer | Left / Right |
| Gas / brake | Up / Down |
| Gear up / down | ] / [ |
| Card in | C |

The game's cabinet profile is detected from its executable; gun, driving,
Ollie King and Gundam layouts are supported. Keyboard input is the exposed
control path in this build.

## Limits

- Direct GD-ROM boot is not supported; convert to `.bin` first.
- Gamepads, wheels, light-gun devices, force feedback, linked cabinets and the
  card-reader UI are not exposed by the shell yet.
- The Chihiro BIOS must be MAME's `chihiro_xbox_bios.bin`; a retail Xbox BIOS
  will not boot it.
- Shell save-state slots are Xbox-oriented; Chihiro cabinet saves live in the
  core's baseboard EEPROM.

See [Chihiro](../docs/guides/chihiro.md) for the full guide and troubleshooting.
