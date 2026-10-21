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
| .NET SDK | 10.0.301 |
| Free disk | C: 81.2 GB · D: 435.6 GB · H: 4.48 TB |
| Toolchain | git, gcc (mingw64) present; **clang, make, bash absent** |
| `References2` | 9 sets, 43,153 files — manifest PASS |
| Real-ISO corpus | `H:\Tests\XBOXTest`, 37 ISOs (benchmark-only) |
| Gate media | all present (`hdd.qcow2`, `triangle.iso`, samples, BIOS/MCPX/EEPROM) |
| GL vendor/version | **not captured** — run any `--gpu gl` leg and record it |

Notes:
- The old plan text "C: has only ~5 GB free — keep media copies on D:" does not
  describe H1 (81 GB free). Do not propagate it.
- nxdk corpus rows (M-B) are **blocked on H1**: no clang/make. `C:\msys64`
  exists, so the fix is a package install, not a toolchain project.

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

**H1 is the reference host (verified 2026-10-20).** All four semantic anchors
reproduce **exactly** on H1 with the current build, `vec=0`, no fault:

| leg (1B, fast, `--gpu sw`) | RAM SHA | CPU SHA | wall |
|---|---|---|---|
| triangle | `F57934BF…` ✓ | `CF79FC17…` ✓ | 9.56 s |
| menu | `660CA11B…` ✓ | `F5D61C94…` ✓ | 39.08 s |

So the emulator is deterministic and the anchors are host-independent as
intended. Walls read +10% (triangle) / +17% (menu) against the recorded
quiet-host references (8.69 s / 33.3 s), which is host noise, not a
configuration fault: the plan's ceilings should be read as ratios on this
machine, not as absolutes.

- **M-A can be recorded here**, provided the `sw` leg of
  `scripts/present-path-acceptance.ps1` is compared against H1's own reference
  (10% above the recorded numbers), not the raw 33.3 s figure.
- The §204-close reading of menu sw **62.6 s** / triangle **14.0 s** did not
  reproduce today (39.08 / 9.56 s). Treat it as a transient (thermal, Dropbox
  sync, or a background build), not as a host property.

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
4. **nxdk toolchain.** Install clang/make on whichever machine will own the M-B
   corpus rows. `C:\msys64` already exists on H1 and C: has 81 GB free, so the
   old "must install on D:" constraint no longer binds.
