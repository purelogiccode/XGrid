# Sega Chihiro

XGrid includes the Sega Chihiro machine: an Xbox with a media board, DIMM and
arcade baseboard. It boots SEGABOOT from flash and loads a netboot image into
DIMM RAM.

> **Netboot images only.** XGrid boots FATX `.bin` images, not Redump GD-ROM sets
> or MAME `.chd` files directly. Convert the disc first.

## Firmware

Put these in one folder:

| File | Size |
| --- | --- |
| `chihiro_xbox_bios.bin` | 512 KB |
| `fpr21042_m29w160et.bin` | 2 MB |
| `ic10_g24lc64.bin` | 8 KB |
| `ic11_24lc024.bin` | 128 bytes |
| `pc20_g24lc64.bin` | 8 KB |
| `ver1305.bin` | 1 MiB (optional network firmware) |

Network titles check `ver1305.bin`: MJ2 needs 12.06, MJ3 13.05, OutRun 2 SP
11.01, Gundam 12.09+.

## Setup

1. Open **Settings → System → Sega Chihiro**.
2. Choose the firmware folder.
3. Enable **Enable Chihiro** for every launch, or just launch a `.bin`.
4. Add the netboot folder to the Library.
5. Pick the `.bin` and power on.

Chihiro launches use 128 MB and no Xbox HDD.

## Cabinet settings

Free play, region, DIMM size (Auto/128/256/512/1024 MB), media-board type
(Auto/Type 1/Type 3), force feedback, separate gun pointers, card files and
linked cabinets are on the same page.

## Convert an Xbox-format ISO

**Settings → System → Sega Chihiro → Convert ISO...**, or:

```text
XGrid.Cli.exe chihiro-netboot --image "Ghost Squad (XBOXOG).iso" --out "ghost-squad.bin"
```

## Convert a GD-ROM

Use the game's MAME key ZIP:

```text
XGrid.Cli.exe chihiro-redump --disc "House of the Dead 3, The (World).cue" --key hotd3.zip --out hotd3.bin
XGrid.Cli.exe chihiro-redump --disc "G:\MAME\MAME CHDs\hotd3\gdx-0001.chd" --key hotd3.zip --out hotd3.bin
```

XGrid bundles no keys, firmware or game data.

## Linked cabinets

Ollie King, OutRun 2, OutRun 2 SP and Maximum Tune support two to four cabinets
over TCP. Give each instance its own data folder, start cabinet 1 first, set the
same cabinet count, and have the others enter its address. The host listens on
TCP 9102–9104. Save states are disabled while linked.

## Limits

- No direct GD-ROM boot.
- Chihiro audio is incomplete.
- MJ2/MJ3 need their own network server.
- Save states use `chihiro_snapshots.qcow2` and need the same netboot image.
- Use the Chihiro BIOS, not a retail Xbox BIOS.

Full guide: [Sega Chihiro](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/chihiro.md)
and [Chihiro controls](Chihiro-Controls).
