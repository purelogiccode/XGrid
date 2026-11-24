# Save states

XGrid exposes **xemu/QEMU snapshots** as four named slots.

## Requirements

- **In-process core hosting** (**Settings → System → Core hosting**).
  Separate process disables save states.
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

## What is captured

The VM state: CPU, RAM, devices and a frame thumbnail. States are stored inside
the HDD image, so they survive replacing the application folder. Expect tens of
megabytes per slot.

## Caveats

- Load with the same HDD and disc configuration.
- A failed load is reported; the core resumes the guest.
- States are not portable between core versions or emulator implementations.
- Deleting a slot cannot be undone.
