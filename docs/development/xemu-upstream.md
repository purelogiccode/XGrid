# Maintaining the xemu tree

XGrid owns its xemu source directly in this Git repository. `xemu/` is tracked
like every other source folder; it is **not** a submodule and it has no separate
repository.

- `xemu/XEMU_VERSION` and `xemu/XEMU_COMMIT` record the upstream xemu release
  and commit the tree was last based on.
- `xemu/xemu-patches/` is the historical provenance record for earlier upstream
  edits. New XGrid changes are normal commits in this repository; do not apply
  those patches again.
- `xemu/subprojects/*.wrap` contains Meson **dependency locks**. A
  `revision=<hash>` line names the upstream commit of a third-party library,
  such as SDL3, imgui, zlib or tomlplusplus. Those hashes are not commits in
  XGrid, are not stored in this repository, and are not unpushed work. Meson
  downloads them while configuring the build.
- Keeping the dependency locks makes builds reproducible. Do not delete them
  unless the dependency is replaced by a vendored, tracked copy.

## Weekly report

`.github/workflows/xemu-upstream-report.yml` runs every Monday on the Linux
runner and can be dispatched manually. It compares:

- the recorded upstream xemu revision with the latest upstream release;
- every git-backed `.wrap` dependency lock with its upstream repository.

When something is newer it opens or updates one issue titled
`[xemu-updates] ...`. The report is notification-only; it never edits the tree.

## Applying an upstream update

1. Read the report and the upstream compare.
2. Review the upstream changes against `xemu/`, especially the files XGrid has
   modified.
3. Apply the changes in the XGrid working tree and run the quality gates.
4. Update `xemu/XEMU_VERSION` and `xemu/XEMU_COMMIT` to the new upstream base.
5. Update any `.wrap` `revision=` values you deliberately adopt.
6. Run the native core build and the managed tests before committing.

See [Upstream differences](../reference/upstream-differences.md) for the current
source-level delta from the pristine 0.8.139 tree.
