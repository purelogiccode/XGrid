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

`ver1305.bin` (1 MiB, network-board firmware) is optional for the gun and
driving games, but MJ2/MJ3 and OutRun 2 SP check its version. It comes from a
Chihiro firmware update disc, not the MAME ROM set.

Open **Settings -> System -> Sega Chihiro**, choose the folder, and turn on
**Enable Chihiro**. A `.bin` launch selects the Chihiro even when the switch is
off. The machine uses 128 MB of RAM and no Xbox hard disk.

The same page sets **Free play** (Start without a coin), **Region** (Auto,
Japan, USA, Export; Error 05 may need another), **DIMM size** (Auto; Gundam
needs 1024 MB), **Media board** (Auto, Type 1, Type 3) and **Force feedback**
(a connected force-feedback wheel, or gamepad rumble when no wheel is found).
The wheel also has strength, weight, autocenter and direction settings.

## Linked cabinets

Ollie King, OutRun 2, OutRun 2 SP and Maximum Tune support two to four linked
cabinets. In **Settings -> System -> Sega Chihiro**:

1. Give every instance its own data folder.
2. Start cabinet 1 first and turn on **Link cabinets** in every instance.
3. Choose the same **Cabinets** count and a different **This cabinet** number.
4. Cabinet 1 leaves **Host address** empty; the others enter its LAN address
   (two instances on one PC can use `127.0.0.1`).
5. Put `ver1305.bin` in the firmware folder and launch the same game.

The host listens on TCP 9100 plus each other cabinet's number (9102-9104). The
games show **CONNECTION WAS ESTABLISHED** when the link is up. Save states are
disabled while the link runs. MJ2/MJ3 need their own network server; the link
is not a substitute.

## Converting an Xbox-format Chihiro ISO

The `XBOXOG - Chihiro` patched ISOs already contain the decrypted Chihiro file
tree. Use **Settings -> System -> Sega Chihiro -> Convert ISO...**, or:

```text
XGrid.Cli.exe chihiro-netboot --image "Ghost Squad (XBOXOG).iso" --out "ghost-squad.bin"
```

The converter writes a FATX netboot image and aliases a renamed executable
(`vsg.xbe` -> `vsg.xbe.bak`/`default.xbe`) when the patch renamed it.

## Converting a GD-ROM

XGrid converts a Redump cue/bin set or a MAME `.chd` with the game's small MAME
key `.zip`:

1. For a Redump set, extract the `.cue` and its `.bin` tracks into one folder.
   A MAME `.chd` is read directly.
2. Open **Settings -> System -> Sega Chihiro -> Convert GD-ROM...**.
3. Pick the `.cue` or `.chd`, the key `.zip`, and the output `.bin`.

Or use the CLI:

```text
XGrid.Cli.exe chihiro-redump --disc "House of the Dead 3, The (World).cue" --key hotd3.zip --out hotd3.bin
XGrid.Cli.exe chihiro-redump --disc "G:\MAME\MAME CHDs\hotd3\gdx-0001.chd" --key hotd3.zip --out hotd3.bin
```

Both `80`-byte `.data` and 16 KB `.pic` keys are read. Key sets: `hotd3.zip`,
`outr2.zip`, `vcop3.zip`, `ghostsqu.zip`, `crtaxihr.zip`, `wangmid.zip`,
`wangmid2.zip`, `ollie.zip`, `mj2.zip`, `mj3.zip`, `mj3evo.zip`. Put the
resulting `.bin` in a library folder and launch it. XGrid bundles no keys,
firmware or game data.

## Controls

The keys can be changed in **Settings -> Input -> Sega Chihiro cabinet keys**.

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Start | Enter | Start |
| Service | L | L3 |
| Coin | 5 | Back |
| Test | K | R3 |
| Steer | Left / Right | Left stick X |
| Gas / brake | Up / Down | RT / LT |
| Gear up / down | ] / [ | D-pad up / down |
| Card in | C | X |
| Gun trigger / body | game-specific | RT / A |
| Gun change / reload | game-specific | B |

The game's cabinet profile is detected from its executable; gun, driving,
Ollie King and Gundam layouts are supported. If a homebrew or renamed
executable is not recognized, choose its layout under **Undetected game** in
the cabinet keys; known games still auto-detect. The first detected gamepad
drives the cabinet, and a second gamepad aims player 2's gun in the gun games. In gun
games, move the host cursor over the picture to aim player 1; left-click fires,
right-click is the body/action or reload button, and RT/A/B do the same on a
gamepad. Leaving the picture reports the off-screen/reload position. Player 2
aims with the second pad's right stick or the arrow keys; RT fires, A is body,
B is change or pedal and LT reloads. On Windows, Linux and macOS,
**Separate gun pointers** reads each light gun or mouse on its own: absolute
light guns aim players 1 and 2 in order, with fire, body, change/pedal and
reload on their buttons. Windows uses Raw Input, Linux evdev and macOS IOHID.
On Linux the user must be able to read `/dev/input/event*`; on macOS allow
Input Monitoring if asked. macOS releases currently ship the shell only, so
use a core built from this tree or point **Emulator binary** at one.
Press **C** (or X) to insert player 1's card and **V** (or X on the second pad)
for player 2's; Ghost Squad and Maximum Tune create a blank card when none is
in hand, and the core keeps it for later sessions. The HW210 and CRP-1231 card
files are assignable under **Card files** in Settings; **Open cards folder**
opens the core's card folder. In gun games the mouse buttons belong to the gun,
so the core's pop-up menu stays on **F2** (or the Guide button) and a
double-click does not toggle fullscreen. Force feedback
drives a force-feedback wheel, or rumbles the first gamepad when no wheel is
found; the wheel's weight, autocenter and direction are in Settings. Gundam's
twin-stick keys (WASD + Q/E, the arrow keys + Right Shift/Ctrl, Space pedal)
are editable under **Gundam cockpit**.

Click the game picture once after launch so the embedded core takes keyboard
focus. Press **5** for a coin and **Enter** to start. Click the toolbar or a
shell page to return focus to XGrid.

Boot-tested: Ghost Squad, OutRun 2, The House of the Dead III, Virtua Cop 3,
Crazy Taxi High Roller, Ollie King and Wangan Midnight Maximum Tune 1/2. Two
OutRun 2 instances linked as cabinets 1 and 2 and both showed **CONNECTION WAS
ESTABLISHED**; four headless OutRun 2 instances linked as cabinets 1-4 on TCP
9102-9104 and each reported its cabinet number of 4. The MJ2/MJ3/MJ3 Evolution
conversions boot past the network-firmware check with `ver1305.bin` and then
wait for their own server.

## Limits

- Direct GD-ROM boot is not supported; convert to `.bin` first.
- Per-game bindings are editable in Settings, including the Gundam cockpit;
  separate light-gun aiming works on Windows, Linux and macOS.
- Chihiro audio is incomplete in upstream xemu; some games are silent or have
  missing effects. The host backend and Xbox audio path are not at fault.
- Linked cabinets are exposed in Settings; save states are disabled while the
  link runs.
- MJ2/MJ3/MJ3 Evolution boot to their network startup but need their own
  network server to play.
- The Chihiro BIOS must be MAME's `chihiro_xbox_bios.bin`; a retail Xbox BIOS
  will not boot it.
- Shell save slots write to `chihiro_snapshots.qcow2` in the data folder; load a
  slot with the same netboot image that wrote it. Save states are disabled
  while a cabinet link runs.

See [Chihiro](../docs/guides/chihiro.md) for the full guide and troubleshooting.
