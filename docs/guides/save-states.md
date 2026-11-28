# Save states

XGrid exposes **xemu/QEMU snapshots** as four named slots. There is no private
XGrid snapshot format; the machine's state is written into the core's snapshot
store using its own snapshot support. An Xbox uses its qcow2 hard-disk image, or
`xbox_snapshots.qcow2` when the HDD is raw or read-only; a Sega Chihiro uses a
dedicated qcow2 file.

## Requirements

- **In-process core hosting on Windows.** Save states use the in-process ABI.
  With **Settings → System → Core hosting → Separate process**, the menu entries
  are disabled because the shell has no ABI to call. The Linux core is a
  separate process, so it has no shell save-state controls.
- A running console.
- A writable HDD image for Xbox. A raw or read-only HDD gets a dedicated
  `xbox_snapshots.qcow2` store automatically; a qcow2 HDD keeps the snapshots
  inside the image. Chihiro uses its own snapshot store.

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
is stored inside the qcow2 HDD image, in `xbox_snapshots.qcow2` when the Xbox HDD
is raw or read-only, or in the Chihiro qcow2, so it survives replacing the
application folder.

State size depends on guest RAM and device state; expect tens of megabytes per
slot.

## Automatic session state

Two session settings turn the four manual slots into a resume-where-you-left-off
workflow:

- **Save state before power off** writes a snapshot named `xgrid-session` when
  you power off, press Stop, or close the XGrid window.
- **Resume the saved session** restores that snapshot when the Power button
  starts the dashboard.

The automatic snapshot is separate from slots 1–4 and is overwritten on each
save. It needs the same requirements as a manual state: in-process hosting on
Windows and a writable snapshot store.

- The snapshot restores into a fresh machine. XGrid lets the console finish its
  early boot first, so the dashboard or game may appear for about 12 seconds
  before the saved state is restored. Loading during the first seconds of the
  retail boot trips a DSP state assertion in the core.
- Launching a title from the Library starts it fresh; only the Power button and
  the dashboard path resume the snapshot.
- If the snapshot is missing or cannot be read, the console continues booting
  normally and the Activity log records why.
- Use the same store (HDD image, `xbox_snapshots.qcow2` or the Chihiro qcow2)
  and core version that wrote the snapshot.

## Xbox snapshot store

A qcow2 HDD holds the snapshots itself. When the configured HDD is raw,
read-only or absent, the shell points `sys.files.snapshot_store_path` at
`xbox_snapshots.qcow2` in the XGrid data folder. The core creates that qcow2 on
the first boot and uses it for the four slots and the session state. Keep the
store with the HDD image; a snapshot taken against one image does not belong to
another.

## Sega Chihiro

A Chihiro launch writes snapshots into `chihiro_snapshots.qcow2` in the launch's
data folder (the shell sets `snapshot_store_path`). The core creates the qcow2 on
the first boot. The DIMM's game data is stored as a delta against the netboot
image, so a slot is typically tens of megabytes.

Load a Chihiro slot with the same `.bin` image; the core rejects a snapshot
taken with a different game. Save states are refused while a cabinet link is
enabled.

## Loading caveats

- Load into a machine that booted from the same store and disc configuration:
  the same HDD image and `xbox_snapshots.qcow2` on Xbox, or the same netboot
  `.bin` on Chihiro.
- The core restores the machine; the shell does not rewind its own library or
  settings.
- A failed load is reported in the Activity log. The core resumes the guest if
  the restore fails.
- Save states are not portable between different core versions or emulator
  implementations.
- **Delete State** removes the selected slot from the store. This cannot be
  undone.

## If save states are disabled

Check **Settings → System → Core hosting**. Switch to **In-process
(recommended)** and power-cycle the console. `xgrid_core.dll` must be present
beside the core executable.

On Linux the core is a separate process, so the shell save-state controls are
not available. On a separate Windows process the controls are disabled by
design.

## Related pages

- [Playing games](playing-games.md) — pause, frame step and fast-forward.
- [Configuration](../reference/configuration.md) — data files and snapshot
  names.
- [Architecture](../reference/architecture.md) — the in-process snapshot bridge.
