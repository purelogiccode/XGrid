# Save states

XGrid exposes **xemu/QEMU snapshots** as four named slots.

## Requirements

- **In-process core hosting on Windows** (**Settings → System → Core hosting**).
  Separate process, including the Linux core, disables save states.
- A running console.
- A writable HDD image.

## Slots

| Slot | Snapshot name |
| --- | --- |
| 1 | `xgrid-slot-1` |
| 2 | `xgrid-slot-2` |
| 3 | `xgrid-slot-3` |
| 4 | `xgrid-slot-4` |

## How to use

- **Emulation → Save State / Load State / Delete State**
- **F6** saves to the selected slot.
- **F7** loads the selected slot.
- Home page **Save state** / **Load state** buttons.

The request is queued; the core executes it on its main loop and the shell polls
for completion. One request runs at a time.

## Automatic session state

Enable **Settings → System → Session → Save state before power off** to write an
automatic `xgrid-session` snapshot when you power off, press Stop, or close the
window. **Resume the saved session** restores it when the Power button starts the
dashboard.

- Separate from slots 1-4; overwritten on each save.
- Needs in-process hosting and a writable HDD image.
- Launching a game from the Library starts it fresh.
- The console finishes its early boot first; the dashboard or game may appear for
  about 12 seconds before the saved state is restored.
- A missing or unreadable snapshot leaves the normal boot running; read the
  reason in **Activity**.

## What is captured

The VM state: CPU, RAM, devices and a frame thumbnail. States are stored inside
the HDD image, so they survive replacing the application folder. Expect tens of
megabytes per slot.

## Caveats

- Load with the same HDD and disc configuration.
- A failed load is reported; the core resumes the guest.
- States are not portable between core versions or emulator implementations.
- Deleting a slot cannot be undone.
