# Track E: the nxdk `usb` sample runs on XGrid, and reads the hub's real descriptors (2026-10-22, session 29)

**Track:** E (corpus breadth), steered by nothing but a run. The sample
`nxdk/usb` (upstream `samples/usb` at `a6c31ee`) initialises the nxdk USB host
stack (`usbh_core_init`), installs the connect/disconnect callbacks and polls
the hubs forever. It is the first corpus row that exercises the **USB hub
enumeration path with a guest stack other than the dashboard's XPP**, and it
confirmed that XGrid serves the descriptors it should.

## The row

Build: `pwsh -File scripts/build-nxdk-samples.ps1 -Samples usb` (the toolchain
*is* installed on H1 - MSYS2 at `C:\msys64` with clang/lld/llvm-dlltool/make;
`host-doctor.ps1` used to report an incomplete toolchain because it probed PATH
only, fixed this session). Staged as `data/test-samples/corpus/usb.iso` (720,896
B).

Run: `pwsh -File scripts/boot-to-first-frame.ps1 -Sample usb -Steps 1000000000`
(HDD + DVD, fast mode). Recorded in `wt-data/btff/usb-sw.json`:

```text
BTFF sample=usb steps=1,000,000,000 wall=8.16 s frames=34 draws=2,672 pixels=41,995,882
firstFrameStep=30M firstFrameSha=6F780A33 (the boot animation)
lastFrameSha=18C7CC83  scanoutSha=119AFA48  displaySha=119AFA48
RAM ACDB5720…  CPU 1590C697…  verdict=PASS (vec=0)
```

The flipped surface is black - the sample draws its output through
`debugPrint`, which writes the **scanout buffer** (compatibility gap 1): the
`displaySha` export is the product surface and shows text. Read from the
640x480 display bitmap:

```text
Plug and unplug USB devices to print the USB device VID and PID
Device connected on USB hub port 1 (VID: 0409 PID: 55aa)
```

**What that means:** the guest's USB stack enumerated the daughterboard hub on
root port 1 and received XGrid's hub device descriptor
(`UsbHub.DeviceDescriptor`: VID `0x0409` "NEC", PID `0x55AA`) - i.e. the OHCI
port connect, control-transfer descriptors and hub class requests all served
the stack, not just the dashboard's own path. The sample keeps polling hubs;
no disconnect event is expected.

## Why it is a row and not a test

The descriptors are hard-coded in `UsbHub`/`XidGamepad` by design (there is no
physical device behind them), so the display line is a **deterministic**
function of XGrid's USB device model - the row asserts that model from the
guest side. It does not gate anything (`docs/compatibility.md` rules), but a
regression in descriptor serving or hub enumeration changes the printed text and
therefore `displaySha`.

## Follow-up candidates it exposes

- `usbh_pooling_hubs` runs forever: a second row at a later step would prove
  the poll path is stable (the current row already runs 1B steps with no fault).
- The sample prints only on connect; a disconnect row needs a pad-script-style
  hot-plug hook on the hub, which does not exist yet (filed, not built).
- The toolchain-to-room note in `docs/hosts.md` ("nxdk absent on H1") is stale;
  `host-doctor.ps1` now says BUILDABLE via MSYS2.
