# Core lineage and upstream

XGrid owns its core source directly in this Git repository. The core began as
xemu 0.8.139, but it is now part of XGrid and **evolves separately from the
original xemu codebase**. `xemu/` is tracked like every other source folder; it
is **not** a submodule and it has no separate repository. The directory name is
retained for provenance.

- `xemu/XEMU_VERSION` and `xemu/XEMU_COMMIT` record the original xemu release
  and commit the core began from.
- `xemu/XEMU_REVIEWED_COMMIT` records the newest upstream commit reviewed for a
  selective port. It can move forward without a full lineage rebase; the weekly
  report and the local audit compare from this commit.
- `xemu/xemu-patches/` is the historical provenance record for earlier upstream
  edits. New XGrid changes are normal commits in this repository; do not apply
  those patches again.
- `scripts/xemu-upstream-audit.ps1` classifies an upstream release against the
  files XGrid has modified and the owned paths in
  `scripts/xemu-owned-paths.txt`.
- `xemu/subprojects/*.wrap` contains Meson **dependency locks**. A
  `revision=<hash>` line names the upstream commit of a third-party library,
  such as SDL3, imgui, zlib or tomlplusplus. Those hashes are not commits in
  XGrid, are not stored in this repository, and are not unpushed work. Meson
  downloads them while configuring the build.
- Keeping the dependency locks makes builds reproducible. Do not delete them
  unless the dependency is replaced by a vendored, tracked copy. Every lock has
  an entry in `xemu/dependencies.json`.

## Weekly report

`.github/workflows/xemu-upstream-report.yml` runs every Monday on the Linux
runner and can be dispatched manually. It checks the dependency manifest first
and then compares:

- the reviewed upstream xemu revision with the latest upstream release;
- every component in `xemu/dependencies.json` according to its tracking policy;
- the Windows build-host package lock.

When something is newer it opens or updates one issue titled
`[xemu-updates] ...`; it closes the issue when everything is current and every
query succeeded. The report is notification-only; it never edits the tree and
does not imply an obligation to adopt an upstream change. See
[Native dependencies](dependencies.md) for the manifest, the host lock and the
check commands.

## Using upstream as a reference

A selective port is the default:

1. Read the report and the upstream compare.
2. Run the audit and review the XGrid patch and XGrid-owned changes first.
3. Adopt, adapt or skip changes; XGrid may diverge deliberately.
4. Port the selected changes under `xemu/`, not by replaying the historical
   patch record.
5. Run the native core build and the managed tests before committing.
6. Update `xemu/XEMU_REVIEWED_COMMIT` to the reviewed upstream commit.

Follow [Updating from upstream xemu](xemu-upgrade.md) for the full runbook,
including the triage table, the apply commands and the smoke-test checklist.

A full lineage rebase is a separate decision: update `xemu/XEMU_VERSION`,
`xemu/XEMU_COMMIT` and any `.wrap` revisions in the same change.

See [xemu baseline differences](../reference/upstream-differences.md) for the
source-level delta from the pristine 0.8.139 tree that XGrid started from.
