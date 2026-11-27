# Save states

XGrid exposes xemu/QEMU snapshots as four named slots. There is no private XGrid
snapshot format. Xbox snapshots live in the HDD image; Chihiro snapshots live in
`chihiro_snapshots.qcow2`.

## Requirements

- **In-process core hosting on Windows.** Save states need the in-process ABI.
- A running console.
- A writable HDD image (or Chihiro snapshot store).

Separate-process hosting, including Linux, disables the shell save-state
controls.

## Slots

| Slot | Name |
| --- | --- |
| 1 | `xgrid-slot-1` |
| 2 | `xgrid-slot-2` |
| 3 | `xgrid-slot-3` |
| 4 | `xgrid-slot-4` |

**F6** saves to the selected slot, **F7** loads it. The Emulation menu and Home
page buttons do the same. Requests run one at a time on the core's main loop.

## Automatic session state

Enable **Save state before power off** to write `xgrid-session` on Power off,
Stop or window close. Enable **Resume the saved session** to restore it when the
Power button starts the dashboard.

- Launching a game from the Library starts it fresh.
- The console finishes early boot first, so the dashboard may appear for about
  12 seconds before the snapshot restores.
- A missing or unreadable snapshot falls back to a normal boot.

## Caveats

- Load with the same HDD image (or netboot `.bin`) and core version that wrote
  the state.
- Snapshots are not portable between different emulator implementations or core
  versions.
- **Delete State** removes a slot permanently.
- Chihiro save states are rejected while a cabinet link is active.

## If save states are disabled

Switch **Settings → System → Core hosting** to **In-process (recommended)** and
power-cycle. `xgrid_core.dll` must be present beside the core.

Full guide: [Save states](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/save-states.md).
