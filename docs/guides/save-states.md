# Save states

XGrid exposes **xemu/QEMU snapshots** as four named slots. There is no private
XGrid snapshot format; the machine's state is written into the hard-disk image
using the core's own snapshot support.

## Requirements

- **In-process core hosting.** Save states use the in-process ABI. With
  **Settings → System → Core hosting → Separate process**, the menu entries are
  disabled because the shell has no ABI to call.
- A running console.
- A writable HDD image.

## Slots

| Slot | Snapshot name |
| --- | --- |
| 1 | `xgrid-slot-1` |
| 2 | `xgrid-slot-2` |
| 3 | `xgrid-slot-3` |
| 4 | `xgrid-slot-4` |

The selected slot is shown in the menu labels and on the Home page buttons.

## Saving, loading and deleting

Use any of:

- **Emulation → Save State / Load State / Delete State**
- **F6** to save to the selected slot
- **F7** to load the selected slot
- The **Save state** and **Load state** buttons on the Home page

A request is queued and executed on the core's main loop. The shell polls for
completion and shows a toast plus an Activity log line. Only one request runs at
a time; a second request is refused while the first is in flight.

## What is captured

A snapshot includes the VM state: CPU, RAM, devices and the frame thumbnail. It
is stored inside the HDD image, so it survives replacing the application folder.

State size depends on guest RAM and device state; expect tens of megabytes per
slot.

## Loading caveats

- Load into a machine that booted from the same HDD image and disc configuration.
- The core restores the machine; the shell does not rewind its own library or
  settings.
- A failed load is reported in the Activity log. The core resumes the guest if
  the restore fails.
- Save states are not portable between different core versions or emulator
  implementations.

## If save states are disabled

Check **Settings → System → Core hosting**. Switch to **In-process (recommended)**
and power-cycle the console. The `xgrid_core.dll` library must be present beside
the core executable.

## Deleting states

**Emulation → Delete State** removes the selected slot from the HDD image. This
cannot be undone.
