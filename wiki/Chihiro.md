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
game's MAME key `.zip` and its `.cue`/`.bin` or `.chd`:

```text
python chihiro-netboot.py "House of the Dead 3, The (World).cue" hotd3.zip -o output
```

Key sets: `hotd3.zip`, `outr2.zip`, `vcop3.zip`, `ghostsqu.zip`,
`crtaxihr.zip`, `wangmid.zip`, `wangmid2.zip`, `ollie.zip`, `mj2.zip`,
`mj3.zip`, `mj3evo.zip`. Put the resulting `.bin` in a library folder and
launch it. XGrid bundles no keys, firmware or game data.

## Controls

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Start | Enter | Start |
| Service | L | L3 |
| Coin | 5 | Back |
| Test | K | R3 |
| Steer | Left / Right | Left stick X |
| Gas / brake | Up / Down | RT / LT |
| Gear up / down | ] / [ | D-pad up / down |
| Card in | C | X / Back |
| Gun trigger / body | game-specific | RT / A |
| Gun change / reload | game-specific | B |

The game's cabinet profile is detected from its executable; gun, driving,
Ollie King and Gundam layouts are supported. The first detected gamepad drives
the cabinet. In gun games, move the host cursor over the picture to aim;
left-click fires, right-click is the body/action or reload button, and RT/A/B
do the same on a gamepad. Leaving the picture reports the off-screen/reload
position. Force feedback is not exposed yet.

Click the game picture once after launch so the embedded core takes keyboard
focus. Press **5** for a coin and **Enter** to start. Click the toolbar or a
shell page to return focus to XGrid.

Boot-tested: Ghost Squad, OutRun 2, The House of the Dead III, Virtua Cop 3,
Crazy Taxi High Roller, Ollie King and Wangan Midnight Maximum Tune 1/2. The
MJ2/MJ3 conversions also select their cabinet profiles.

## Limits

- Direct GD-ROM boot is not supported; convert to `.bin` first.
- Gamepads, wheels, light-gun devices, force feedback, linked cabinets and the
  card-reader UI are not exposed by the shell yet.
- The Chihiro BIOS must be MAME's `chihiro_xbox_bios.bin`; a retail Xbox BIOS
  will not boot it.
- Shell save-state slots are Xbox-oriented; Chihiro cabinet saves live in the
  core's baseboard EEPROM.

See [Chihiro](../docs/guides/chihiro.md) for the full guide and troubleshooting.
