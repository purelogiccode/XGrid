# Development hosts

Status: created 2026-10-20. Development runs on **two machines that share one
Dropbox tree** (`C:\Users\HomePC\Dropbox\source\repos\CSharp_XboxEmulator`).
Their free space, toolchains, media and absolute walls differ. Past sessions
recorded "quiet host" numbers without saying which machine produced them, and a
later session read the difference as a regression (§204/§205 close: the
*unmodified* baseline measured menu sw 62.6 s against a recorded 33.3 s). This
file exists so that never happens again.

## Rules

1. **Run `pwsh -File scripts/host-doctor.ps1` at session start.** It prints the
   fingerprint below, allows/denies the plan's preconditions, and verifies
   `References2/` against `docs/refs-manifest.json`.
2. **Every measured table names its host** (`H1`/`H2`) or says `same-window A/B`.
3. **Two anchor classes, never mixed:**
   - *Semantic* — 1B RAM/CPU SHAs, frame SHAs, and every counter (`draws`,
     `frames`, `stalls`, `BackendUnsupported`, `verifyFailures`, `MethodTotal`).
     These must be **identical on both machines** for the same commit. A
     mismatch is a determinism bug to file, not an environment note.
   - *Performance* — walls, `msps`, `rasterPsh`, `drawMs`/`flushMs`. Host-local.
     Only same-window A/B ratios travel between hosts.
4. **References are never copied out of `References2/`.** See `AGENTS.md`; the
   manifest is a checksum list, not a backup.

## H1 — PETERSONPC (home)

Captured 2026-10-20 by `scripts/host-doctor.ps1`.

| Field | Value |
|---|---|
| Machine / user | `PETERSONPC` / `Peterson` |
| OS | Windows 11 Pro 10.0.26300 |
| CPU | AMD Ryzen 9 7900, 12 cores / 24 logical |
| RAM | 31 GB |
| PowerShell | 7.6.6 |
| .NET SDK | 11.0.100 |
| Free disk | C: 81.2 GB · D: 435.6 GB · H: 4.48 TB |
| Toolchain | git, gcc (mingw64) on PATH; **nxdk toolchain present inside MSYS2** at `C:\msys64` (clang, lld, llvm-dlltool, make, bison, flex, cmake) - not on PATH (2026-10-22) |
| `References2` | 9 sets, 43,153 files — manifest PASS |
| Real-ISO corpus | `H:\Tests\XBOXReady` (**2 optimized XDVDFS ISOs**: 007, Halo v1.09 - the xemu oracle set) + `H:\Tests\XBOXTest` (37 full-disc Redump ISOs, XGrid-only benchmark media; xemu cannot boot them) |
| xemu runtime | `data\xemu` (xemu 0.8.139, `xemu.toml` points at the local media; `-bios` passed explicitly) |
| Gate media | all present (`hdd.qcow2`, `triangle.iso`, samples, BIOS/MCPX/EEPROM) |
| GL vendor/version | **not captured** — run any `--gpu gl` leg and record it |

Notes:
- The old plan text "C: has only ~5 GB free — keep media copies on D:" does not
  describe H1 (81 GB free). Do not propagate it.
- nxdk corpus rows (M-B) **build on H1** (confirmed 2026-10-22): the toolchain
  (clang, lld, llvm-dlltool, make, bison, flex, cmake) lives inside MSYS2 at
  `C:\msys64`, nxdk is at `D:\nxdk` (`a6c31ee`), and
  `scripts/build-nxdk-samples.ps1` drives both; `host-doctor.ps1` now checks the
  MSYS2 default install instead of PATH only (it used to report "clang/make
  absent" on this machine).

## H2 — (other machine, identity to be confirmed)

Not accessible as of 2026-10-20; the operator can reach it the morning of
2026-10-21. Fill this in from `host-doctor.ps1` on that machine, then delete
this banner.

| Field | Value |
|---|---|
| Machine / user | *TBD* |
| OS | *TBD* |
| CPU / logical | *TBD* |
| RAM | *TBD* |
| .NET SDK | *TBD* (the ktest-smoke `-XisoSharp` default embeds an older user name, so the profile directory may differ) |
| Free disk | *TBD* |
| Toolchain | *TBD* |
| `References2` | *TBD* — must PASS the manifest; if it fails, that machine's copy is the damaged one |
| Real-ISO corpus | *TBD* |
| GL vendor/version | *TBD* |

## Reference host

**H1 is the reference host (verified 2026-10-20).** The semantic anchors reproduce
**exactly** on H1, `vec=0`, no fault. Current values (post-§206g cull landing):

| leg (1B, fast, `--gpu sw`, **gate media**) | RAM SHA | CPU SHA | wall |
|---|---|---|---|
| triangle (HDD + DVD) | `B45254CA…` | `7DAB63EB…` | 11.3 s |
| menu (HDD) | `51B56687…` | `F5D61C94…` | 35-54 s (load-dependent) |

**Flag-set caveat (measured 2026-10-21).** The fast-mode triangle RAM SHA also
depends on `--frame-first`: that flag caps batches at 2M steps while it hunts the
first flip, which moves interrupt delivery and therefore guest RAM. Same media,
1B, `--jit --jit-raw`: `B45254CA…` without it, `0117A5E2…` with it (CPU
`7DAB63EB…` and every counter identical in both). The CPU SHA and the counters
are the portable anchors; name the exact flag set whenever you quote a fast-mode
RAM SHA. `scripts/boot-to-first-frame.ps1` always passes `--frame-first`, so its
rows carry `0117A5E2…`; the anchor table above is the plain-run value.

**Correction 2026-10-21 (§208d).** The triangle row used to read
`DD1D516E…`/`CF79FC17…`/8.0 s. That pair is the **DVD-only** leg, which never
launches the disc's XBE (`raster-leg.ps1` and `present-path-acceptance.ps1` both
used it); it measures the boot animation - 2,673 draws / 35 frames / 42.3M px
instead of 2,706 / 68 / 57.7M. A stashed pre-change build reproduces both pairs,
so this is a harness/media fact, not a regression. Both scripts now use HDD+DVD.
Triangle `gl+replace` on the real workload is 7.5 s (was "6.4").

The pre-cull run reproduced the then-recorded anchors exactly (triangle
`F57934BF…`/`CF79FC17…`, menu `660CA11B…`/`F5D61C94…`), which is what established
H1 as the reference host and proved the build deterministic. The cull-face
landing then moved **RAM only** (culled triangles stop writing zeta) with every
CPU SHA and every frame SHA identical — controlled pre/post A/B, see decisions
§206g.

- **M-A can be recorded here**, provided the `sw` leg of
  `scripts/present-path-acceptance.ps1` is compared against H1's own reference
  (10% above the recorded numbers), not the raw 33.3 s figure.
- The §204-close reading of menu sw **62.6 s** / triangle **14.0 s** did not
  reproduce today (39.08 / 9.56 s pre-cull, 35.4 / 8.0 s post-cull). Treat it as
  a transient (thermal, Dropbox sync, or a background build), not a host property.

## Open items

1. **Fast-mode SHA determinism — RESOLVED for H1 (2026-10-20).** The canonical
   1B anchors reproduce exactly on H1 (`F57934BF…`/`CF79FC17…` triangle,
   `660CA11B…`/`F5D61C94…` menu), so the §199l note that `A2DCA55F…` "stopped
   reproducing even on pre-patch `025d20d`" was not a live determinism bug on
   this machine. Remaining: confirm the same SHAs on H2 tomorrow. If H2 disagrees,
   that *is* a determinism bug and the spin-skip/vblank path is the suspect.
2. **Wall offset, not semantics.** H1 reads +10% (triangle) / +17% (menu) against
   the recorded 8.69 s / 33.3 s while every SHA matches. Either the reference
   numbers came from an idle/cooler state of H1, or from H2. Decide when H2 is
   fingerprinted; until then compare ratios, never absolutes.
3. **H2 fingerprint** (tomorrow). Fill the H2 table, then delete the banner.
   If H2's `References2` fails the manifest, that machine's copy is the damaged
   one — do not "fix" H1 from it.
4. **nxdk toolchain - DONE on H1 (2026-10-22).** M-B rows build with
   `scripts/build-nxdk-samples.ps1` (MSYS2 `C:\msys64`, nxdk `D:\nxdk`);
   `nxdk/usb` is a row now (`docs/research-notes/nxdk-usb-sample.md`). H2 needs
   the same packages before it can build M-B rows.
