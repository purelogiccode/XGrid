# Sega Chihiro

XGrid includes the Sega Chihiro machine from the xemu Chihiro lineage. A
Chihiro is an Xbox with a media board, a DIMM and an arcade baseboard: the same
CPU and NV2A, but it boots SEGABOOT from its own flash, loads a game image into
DIMM RAM, and talks to JVS, card readers and drive boards instead of a DVD
drive.

> **Netboot images only.** XGrid boots Chihiro **`.bin` netboot images** (raw
> FATX images), not Redump `.cue`/`.bin` GD-ROM sets or MAME `.chd` files
> directly. The `XBOXOG - Chihiro` style Xbox ISOs convert in Settings; raw
> GD-ROM dumps need the external converter. See
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

- **Settings → System → Sega Chihiro → Convert Chihiro ISO...**, or
- the CLI:

```text
XGrid.Cli.exe chihiro-netboot --image "Ghost Squad (XBOXOG).iso" --out "ghost-squad.bin"
```

The converter writes a 128/256/512/1024 MiB FATX image (the game area is the
DIMM size less the 16 MiB media-board system area). It maps a `boot.id` name
such as `\vsg.xbe` to the `vsg.xbe.bak` or `default.xbe` file when the patched
ISO renamed the executable. The output `.bin` is ready for the Library.

## Converting a GD-ROM

The Redump and MAME Chihiro sets are raw GD-ROM dumps. Convert one to the netboot
format with [chihiro-netboot](https://github.com/Tovarichtch/chihiro-netboot),
an external MIT-licensed tool based on JayFoxRox's Chihiro-Tools:

1. Put the game's small MAME `.zip` (the security key) beside the disc dump.
2. Run `chihiro-netboot` on the `.cue`/`.bin` set or the MAME `.chd`.
3. Put the `output/<game>.bin` file in an XGrid library folder.
4. Launch the `.bin` from **Library**.

For example, with the Redump cue/bin set extracted beside the key:

```text
python chihiro-netboot.py "House of the Dead 3, The (World).cue" hotd3.zip -o output
```

The small key sets used by this collection are `hotd3.zip`, `outr2.zip`,
`vcop3.zip`, `ghostsqu.zip`, `crtaxihr.zip`, `wangmid.zip`, `wangmid2.zip`,
`ollie.zip`, `mj2.zip`, `mj3.zip` and `mj3evo.zip`. The output is named after
the key set (`hotd3.bin`, `outr2.bin`, ...); rename it for the Library if you
like.

XGrid does not bundle the converter, game keys, firmware, GD-ROM data or game
images. The converter produces a decrypted FATX image; without the matching
key it cannot make a bootable image.

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
| Card in | `C` | X / Back |
| Gun trigger / body | (game-specific) | RT / A |
| Gun change / reload | (game-specific) | B |

The core selects the cabinet profile from the game's `boot.id`/executable:
The House of the Dead III, Virtua Cop 3 and Ghost Squad use the gun layout;
Crazy Taxi High Roller, OutRun 2 and Maximum Tune use the driving layout; Ollie
King and Gundam have their own layouts. The first detected gamepad drives the
cabinet; a wheel that SDL exposes as a gamepad works through its stick and
triggers.

In the gun games, move the host cursor over the picture to aim; RT or the
configured trigger fires and A is the body/action button. Moving off the
picture reports the off-screen/reload position. The shell cursor is still
visible, so use fullscreen or a large picture for the best aim.

## Limits

- **No direct GD-ROM boot.** The ported machine loads a FATX netboot image into
  DIMM; it does not emulate the GD-ROM drive. Convert the disc first. The
  `XBOXOG - Chihiro` patched ISOs can be converted in XGrid itself; raw Redump
  `.cue` sets still need the external `chihiro-netboot` and their key.
- **Basic gamepad mapping in this build.** The first detected gamepad drives
  the cabinet (stick, triggers, D-pad and face buttons). Per-game bindings,
  force feedback, pointer devices and light-gun aim are still follow-ups.
- **Linked cabinets and card-reader UI** are present in the core but not exposed
  by the shell. They are for later work.
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
