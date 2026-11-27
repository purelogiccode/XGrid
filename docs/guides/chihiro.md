# Sega Chihiro

XGrid includes the Sega Chihiro machine from the xemu Chihiro lineage. A
Chihiro is an Xbox with a media board, a DIMM and an arcade baseboard: the same
CPU and NV2A, but it boots SEGABOOT from its own flash, loads a game image into
DIMM RAM, and talks to JVS, card readers and drive boards instead of a DVD
drive.

> **Netboot images only.** XGrid boots Chihiro **`.bin` netboot images** (raw
> FATX images), not Redump `.cue`/`.bin` GD-ROM sets or MAME `.chd` files
> directly. Build one in **Settings → System → Sega Chihiro** from an
> `XBOXOG - Chihiro` ISO, a Redump cue/bin set, or a MAME `.chd` with the game's
> key. See
> [Converting an Xbox-format Chihiro ISO](#converting-an-xbox-format-chihiro-iso)
> and [Converting a GD-ROM](#converting-a-gd-rom).

## Firmware

The Chihiro needs the machine's own firmware. XGrid ships none of it. Put these
files in one folder:

| File | Size | What it is |
| --- | --- | --- |
| `chihiro_xbox_bios.bin` | 512 KB | The Chihiro Xbox BIOS (MAME's `chihiro` set). |
| `fpr21042_m29w160et.bin` | 2 MB | The media-board flash with SEGABOOT. |
| `ic10_g24lc64.bin` | 8 KB | The QC board's EEPROM. |
| `ic11_24lc024.bin` | 128 bytes | The baseboard's EEPROM (cabinet settings and saves). |
| `pc20_g24lc64.bin` | 8 KB | The SC board's EEPROM. |
| `ver1305.bin` | 1 MiB | Optional network-board firmware (Au1500/VxWorks). |

The network firmware is not needed by the gun and driving games, but network
titles check its version: MJ2 requires 12.06, MJ3 13.05, OutRun 2 SP 11.01 and
Gundam 12.09 or later. Without it the board reports 3.17 and those titles stop
with their firmware-version error. `ver1305.bin` comes from a Chihiro firmware
update disc, not from the MAME ROM set.

## Setup

1. Open **Settings → System → Sega Chihiro**.
2. Choose the folder with the five required firmware files. Add
   `ver1305.bin` if you have it.
3. Turn **Enable Chihiro** on if you want every launch to use the Chihiro.
   Launching a `.bin` image selects it for that launch even when this is off.
4. Add the folder with your netboot images in **Settings → Library**.
5. Pick the `.bin` in **Library** and press **Power on**.

A Chihiro launch uses the Chihiro BIOS, 128 MB of guest RAM and no Xbox hard
disk. The shell writes `default_machine = 'chihiro'` and the
`[chihiro.roms]` paths into the generated `xemu.toml`.

## Cabinet settings

**Settings → System → Sega Chihiro** also controls the cabinet:

- **Free play** lets Start begin a game without inserting a coin.
- **Region** chooses Auto, Japan, USA or Export. A game that stops with
  `Error 05` may need another region.
- **DIMM size** defaults to Auto. Gundam Battle Operating Simulator needs
  1024 MB; other titles use 128 to 512 MB.
- **Media board** defaults to Auto. Early Crazy Taxi High Roller and House of
  the Dead III cabinets are Type 1; most others are Type 3.
- **Force feedback** sends the drive board's forces to a force-feedback wheel,
  or the first gamepad's rumble when no wheel is found. **Feedback strength**
  scales it from 0 to 200%; a wheel also has **Wheel weight**, **Autocenter**,
  **Autocenter strength** and **Invert direction**.

## Linked cabinets

Ollie King, OutRun 2, OutRun 2 SP and Maximum Tune support two to four linked
cabinets. XGrid bridges the Chihiro network board's Ethernet between instances
over TCP; each instance runs its own game.

1. Give every instance its own data folder (the `--data-dir` argument or a
   separate portable copy).
2. Start cabinet 1 first. In **Settings → System → Sega Chihiro**, turn
   **Link cabinets** on and choose the same **Cabinets** count everywhere.
3. Cabinet 1 leaves **Host address** empty and listens. Cabinets 2 to 4 enter
   cabinet 1's LAN address (`127.0.0.1` when both run on one PC) and their own
   **This cabinet** number.
4. Put `ver1305.bin` in the firmware folder.
5. Launch the same game in each instance.

The host listens on TCP 9100 plus each other cabinet's number (9102, 9103 and
9104). The games show **CONNECTION WAS ESTABLISHED** when the link is up. Save
states are disabled while the link runs, because the other cabinets would not
come back with a snapshot. MJ2/MJ3 are network mahjong games with their own
server; the cabinet link is not a substitute for it.

XGrid writes the cabinet count and 1-based cabinet number into the backup of
Ollie King, OutRun 2, OutRun 2 SP and Maximum Tune. If a game still reports
that its **GAME ASSIGNMENTS** differ from the link, set the count and link ID
in the game's test menu once; the link cannot form while they disagree.

Four OutRun 2 instances on one PC have been validated: the host listens on
9102, 9103 and 9104, each client connects to its own port, and the game
reports cabinet 1 of 4 through cabinet 4 of 4.

## Converting an Xbox-format Chihiro ISO

Some homebrew Chihiro conversions (the `XBOXOG - Chihiro` style ISOs) are Xbox
XISOs that already contain the decrypted Chihiro file tree: `boot.id`, the game
executable and the `media` folder. XGrid can turn one into a netboot image
without a key:

- **Settings → System → Sega Chihiro → Convert ISO...**, or
- the CLI:

```text
XGrid.Cli.exe chihiro-netboot --image "Ghost Squad (XBOXOG).iso" --out "ghost-squad.bin"
```

The converter writes a 128/256/512/1024 MiB FATX image (the game area is the
DIMM size less the 16 MiB media-board system area). It maps a `boot.id` name
such as `\vsg.xbe` to the `vsg.xbe.bak` or `default.xbe` file when the patched
ISO renamed the executable. The output `.bin` is ready for the Library.

## Converting a GD-ROM

The Redump cue/bin sets and MAME CHD sets are raw GD-ROM dumps. XGrid converts
either form itself:

1. For a Redump set, extract the `.cue` and its `.bin` tracks into one folder.
   A MAME `.chd` is read directly.
2. Get the game's small MAME `.zip` (the security key).
3. Choose **Settings → System → Sega Chihiro → Convert GD-ROM...** and pick the
   `.cue` or `.chd`, the key `.zip`, and then the output `.bin`.
4. Put the `.bin` in an XGrid library folder and launch it.

The CLI does the same:

```text
XGrid.Cli.exe chihiro-redump --disc "House of the Dead 3, The (World).cue" --key hotd3.zip --out hotd3.bin
XGrid.Cli.exe chihiro-redump --disc "G:\MAME\MAME CHDs\hotd3\gdx-0001.chd" --key hotd3.zip --out hotd3.bin
```

`--cue` is accepted as an alias for `--disc`. The converter reads the
high-density track directly from the cue/bin track or the CHD and accepts both
`80`-byte `.data` keys and 16 KB `.pic` keys. It reads the loader named by the
key, then decrypts the FATX image that loader names. The output image keeps the
game's FATX name (such as `VSG.BIN`); rename the catalogued `.bin` only for your
own library organization.

The small key sets used by this collection are `hotd3.zip`, `outr2.zip`,
`vcop3.zip`, `ghostsqu.zip`, `crtaxihr.zip`, `wangmid.zip`, `wangmid2.zip`,
`ollie.zip`, `mj2.zip`, `mj3.zip` and `mj3evo.zip`.

XGrid does not bundle game keys, firmware, GD-ROM data or game images. The
converter's format and DES byte-swap scheme follow the MIT-licensed
[chihiro-netboot](https://github.com/Tovarichtch/chihiro-netboot); without the
matching key it cannot make a bootable image.

## Titles

The cabinet table covers the main Chihiro releases:

| Title | Boot-tested |
| --- | --- |
| Ghost Squad | Yes |
| OutRun 2 | Yes |
| The House of the Dead III | Yes |
| Virtua Cop 3 | Yes |
| Crazy Taxi High Roller | Yes |
| Ollie King | Yes |
| Wangan Midnight Maximum Tune | Yes |
| Wangan Midnight Maximum Tune 2 | Yes |
| Sega Network Taisen Mahjong MJ2 / MJ3 / MJ3 Evolution | Boots past the firmware check with `ver1305.bin`; the games then wait for their network |

The test means SEGABOOT reached the game and the cabinet profile was selected;
it is not a promise for every scene or network mode. The MJ titles are network
mahjong games and need a linked cabinet or server to go further.

## Controls

The cabinet's default keyboard map is below. Change it in **Settings → Input →
Sega Chihiro cabinet keys**: click a binding, press the new key, and the shell
writes it into `[chihiro.jvs]` for player 1 or `[chihiro.jvs_p2]` for player 2
at launch. The gamepad maps are fixed.

Click the game picture once after launch: the embedded core takes keyboard
focus for the cabinet (the Xbox path keeps focus in the shell for its pad
sink). Click the toolbar or a shell page to return focus to XGrid. Then press
**5** for a coin and **Enter** to start.

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Start | Enter | Start |
| Service | `L` | L3 (left stick click) |
| Coin | `5` | Back |
| Test | `K` | R3 (right stick click) |
| Steer left / right | `Left` / `Right` | Left stick X |
| Gas / brake | `Up` / `Down` | RT / LT |
| Gear up / down | `]` / `[` | D-pad up / down |
| View change | (game-specific) | Y |
| Card in | `C` | X |
| Gun trigger / body | (game-specific) | RT / A |
| Gun change / reload | (game-specific) | B |

Player 2 on a two-player gun cabinet uses a second gamepad or the **Player 2**
rows in the same Settings page:

| Action | Keyboard | Second gamepad |
| --- | --- | --- |
| Start | keypad Enter | Start |
| Coin | keypad 2 | Back |
| Card in | `V` | X |
| Aim | arrow keys | Right stick |
| Trigger | (bindable) | RT |
| Body | (bindable) | A |
| Change / pedal | (bindable) | B |
| Reload | (bindable) | LT |

The arrow keys move player 2's crosshair at the **Player 2 aim speed**; 100%
crosses the picture in about 0.75 seconds. The P2 gun trigger, body,
change/pedal and reload keys are unbound by default because the second gamepad
supplies them; bind them in Settings for a keyboard-only second gun. The aim
keys only apply to the gun profiles, so the driving games still use the arrows
for player 1.

The core selects the cabinet profile from the game's `boot.id`/executable:
The House of the Dead III, Virtua Cop 3 and Ghost Squad use the gun layout;
Crazy Taxi High Roller, OutRun 2 and Maximum Tune use the driving layout; Ollie
King and Gundam have their own layouts. The first detected gamepad drives the
cabinet; a second gamepad aims player 2's gun in the gun games. A wheel that SDL
exposes as a gamepad works through its stick and triggers.

If a homebrew or renamed executable is not recognized, choose its cabinet under
**Undetected game** in the cabinet keys. The core still auto-detects games in
its cabinet table; the selection is only the fallback.

Gundam Battle Operating Simulator's twin-stick cockpit is under **Gundam
cockpit** in Settings:

| Action | Keyboard |
| --- | --- |
| Left stick | `W` / `S` / `A` / `D` |
| Left trigger / button | `Q` / `E` |
| Right stick | `Up` / `Down` / `Left` / `Right` |
| Right trigger / button | `Right Shift` / `Right Ctrl` |
| Pedal | `Space` |

These bindings are written to `[chihiro.jvs.gundam]` at launch.

In the gun games, move the host cursor over the picture to aim player 1; the
left mouse button fires, the right button is the body/action or reload button,
and RT/A/B do the same on a gamepad. Moving off the picture reports the
off-screen/reload position. Player 2 aims with the second gamepad's right stick
or the arrow keys. The shell cursor is still visible, so use fullscreen or a
large picture for the best aim. In gun games the mouse buttons belong to the
gun, so the core's pop-up menu stays on **F2** (or the Guide button) and a
double-click does not toggle fullscreen.

On Windows, Linux and macOS, turn on **Separate gun pointers** in
**Settings → System → Sega Chihiro** to read each light gun or mouse on its
own. Absolute light guns are preferred: the first aims player 1 and the second
player 2. Their left button fires, the right is body, the middle is change or
pedal and the side buttons reload. Relative mice are tracked separately too,
starting from the centre of the picture. Windows reads them through Raw Input,
Linux through evdev (`/dev/input/event*`) and macOS through IOHID. With the
setting off, the host cursor and second-gamepad paths above still apply.

Use **Player 1 pointer** and **Player 2 pointer** to pin a device by name when
automatic order picks the wrong one. Launch a gun game once with the setting
on, choose **Refresh** to read the core's pointer list, then select the gun for
each player. Automatic keeps the absolute-first order; a pinned device wins
over it.

Ghost Squad and Maximum Tune use the emulated card readers. Press **C** (or X)
to push player 1's card in hand into the reader, and **V** (or X on the second
pad) for player 2; with no card it creates a blank one. The core keeps the card
with the cabinet's card files, so a later session picks it up again. Ghost
Squad's two-player card screen uses both readers, one per player.

The readers and their card files are under **Settings → System → Sega Chihiro →
Card files**:

| Slot | Reader |
| --- | --- |
| Ghost Squad player 1 / player 2 | HW210 slot 1 / slot 2 |
| Gundam B.O.S. | The Gundam HW210 slot |
| Maximum Tune 1 / 2 | CRP-1231 MT1 / MT2 |

An empty slot creates a blank card in the core's `cards` folder the first time
a game asks for one. Choose a `.bin` card to keep a specific card; the game
writes its progress back to that file. **Card readers** turns both readers off
for games that should not see them. **Open cards folder** opens the folder
where the core creates blank cards.

## Force feedback

With **Force feedback** on, the core maps the drive board's effects to an SDL
haptic wheel:

- OutRun 2's Sega 838 spring, road resistance and event packages.
- Maximum Tune's V257 centering, damper and directional pushes.
- The Sega 838 vibration channel as a periodic buzz.

**Feedback strength** scales everything. **Wheel weight** is the share of the
drive board's damper the wheel applies. **Autocenter** and **Autocenter
strength** center the wheel while the game is not driving it. **Invert
direction** flips the push if the wheel pulls away from centre. With no
force-feedback wheel, the event channel rumbles the first gamepad instead.

## Limits

- **No direct GD-ROM boot.** The ported machine loads a FATX netboot image into
  DIMM; it does not emulate the GD-ROM drive. Convert the disc first, in XGrid,
  using the game's key.
- **Basic gamepad mapping in this build.** The first detected gamepad drives
  the cabinet (stick, triggers, D-pad and face buttons); a second gamepad aims
  player 2's gun. The cabinet keys are editable in Settings, including the
  Gundam cockpit.
- **Separate light guns work on Windows, Linux and macOS.** With **Separate
  gun pointers** on, the core reads each mouse or light gun through Raw Input
  on Windows, evdev on Linux and IOHID on macOS. Absolute light guns aim
  players 1 and 2 first; relative mice start at the centre. On Linux the user
  must be able to read `/dev/input/event*` (usually the `input` group); on
  macOS allow Input Monitoring if the system asks. macOS releases currently
  ship the shell only, so use a core built from this tree or point **Emulator
  binary** at one.
- **Chihiro audio is incomplete.** Upstream xemu's Chihiro audio emulation can
  produce silence or missing effects. The host audio backend and the Xbox path
  work; the Chihiro APU stream is a known upstream limitation, not a shell
  setting.
- **Network titles stop at their network startup.** MJ2/MJ3/MJ3 Evolution boot
  past the firmware-version check with `ver1305.bin`, but they need their own
  server and do not reach a solo game.
- **Shell save states** write to `chihiro_snapshots.qcow2` in the data folder;
  they are disabled while a cabinet link runs. Load a slot with the same
  netboot image that wrote it.
- The Chihiro BIOS must match MAME's `chihiro_xbox_bios.bin` (SHA-1
  `b700b0041af8f84835e45d1d1250247bf7077188`). A retail Xbox BIOS will not boot
  the Chihiro machine.

## Troubleshooting

| Problem | Check |
| --- | --- |
| "Chihiro files not found" | All five required files are in the chosen folder and the names match the table above. Add `ver1305.bin` for MJ2/MJ3. |
| "is not a Chihiro BIOS" | Use `chihiro_xbox_bios.bin`, not `Complex_4627.bin` or another Xbox BIOS. |
| `is not a FATX image` | The `.bin` is still a GD-ROM or encrypted image; convert it to netboot format. |
| Stuck on SEGABOOT or a black screen | Check the core log for the media-board flash and the game's `boot.id`; a bad or missing image leaves SEGABOOT waiting. |
| "Error 14" / "Network firmware version" | Put `ver1305.bin` in the firmware folder. MJ2 needs 12.06, MJ3 13.05. |
| "Error 05" / region error | Try another **Region** in Settings; the game may not accept the cabinet's current region. |
| "DNS failed" or a network wait screen | MJ2/MJ3 need their own network server; the cabinet link is not a substitute. |
| No sound in a Chihiro game | Known upstream xemu Chihiro limitation; the host audio backend and the Xbox path work. |
| Linked cabinets do not connect | Start cabinet 1 first, use its LAN address on the others, allow TCP 9102-9104 through the firewall, and use the same game and firmware on every instance. |
| No input | The core window must have keyboard focus. See [Controls](#controls). |

The core log is in **Activity** and, in separate-process mode,
`<data>/xemu/xemu.log`.
