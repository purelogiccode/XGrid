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

The same folder may also hold `ver1305.bin`, the network-board firmware. It is
optional for single cabinets.

## Setup

1. Open **Settings → System → Sega Chihiro**.
2. Choose the folder with the five firmware files.
3. Turn **Enable Chihiro** on if you want every launch to use the Chihiro.
   Launching a `.bin` image selects it for that launch even when this is off.
4. Add the folder with your netboot images in **Settings → Library**.
5. Pick the `.bin` in **Library** and press **Power on**.

A Chihiro launch uses the Chihiro BIOS, 128 MB of guest RAM and no Xbox hard
disk. The shell writes `default_machine = 'chihiro'` and the
`[chihiro.roms]` paths into the generated `xemu.toml`.

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
| Sega Network Taisen Mahjong MJ2 / MJ3 / MJ3 Evolution | Converted; the static cabinet profile is selected |

The test means SEGABOOT reached the game and the cabinet profile was selected;
it is not a promise for every scene or network mode.

## Controls

The core's default Chihiro keyboard map is below. The keys can be changed in
`xemu.toml` under `[chihiro.jvs]`; the shell does not expose a Chihiro key editor
yet.

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

The core selects the cabinet profile from the game's `boot.id`/executable:
The House of the Dead III, Virtua Cop 3 and Ghost Squad use the gun layout;
Crazy Taxi High Roller, OutRun 2 and Maximum Tune use the driving layout; Ollie
King and Gundam have their own layouts. The first detected gamepad drives the
cabinet; a wheel that SDL exposes as a gamepad works through its stick and
triggers.

In the gun games, move the host cursor over the picture to aim; the left mouse
button fires, the right button is the body/action or reload button, and RT/A/B
do the same on a gamepad. Moving off the picture reports the off-screen/reload
position. The shell cursor is still visible, so use fullscreen or a large
picture for the best aim.

Ghost Squad and Maximum Tune use the emulated card readers. Press **C** (or X)
to push the card in hand into the reader; with no card it creates a blank one.
The core keeps the card with the cabinet's card files, so a later session picks
it up again. Ghost Squad's second reader and the linked-cabinet UI are not
exposed by the shell yet.

## Limits

- **No direct GD-ROM boot.** The ported machine loads a FATX netboot image into
  DIMM; it does not emulate the GD-ROM drive. Convert the disc first, in XGrid,
  using the game's key.
- **Basic gamepad mapping in this build.** The first detected gamepad drives
  the cabinet (stick, triggers, D-pad and face buttons). Per-game bindings and
  force feedback are still follow-ups.
- **Linked cabinets and the second card reader** are present in the core but
  not exposed by the shell. Player 1's card insert works.
- **Shell save-state slots are Xbox-oriented.** The Chihiro core keeps its own
  cabinet saves in the baseboard EEPROM; the shell's HDD-backed snapshot slots
  are not a Chihiro feature yet.
- The Chihiro BIOS must match MAME's `chihiro_xbox_bios.bin` (SHA-1
  `b700b0041af8f84835e45d1d1250247bf7077188`). A retail Xbox BIOS will not boot
  the Chihiro machine.

## Troubleshooting

| Problem | Check |
| --- | --- |
| "Chihiro files not found" | All five files are in the chosen folder and the names match the table above. |
| "is not a Chihiro BIOS" | Use `chihiro_xbox_bios.bin`, not `Complex_4627.bin` or another Xbox BIOS. |
| `is not a FATX image` | The `.bin` is still a GD-ROM or encrypted image; convert it to netboot format. |
| Stuck on SEGABOOT or a black screen | Check the core log for the media-board flash and the game's `boot.id`; a bad or missing image leaves SEGABOOT waiting. |
| No input | The core window must have keyboard focus. See [Controls](#controls). |

The core log is in **Activity** and, in separate-process mode,
`<data>/xemu/xemu.log`.
