# Updating from upstream xemu

XGrid uses a **selective port** when xemu publishes a new release. The tracked
`xemu/` tree is XGrid's core source, not a submodule and not a pristine upstream
checkout. An upstream release is therefore a reviewed porting task, not a
dependency bump.

The weekly upstream workflow opens an issue when a newer xemu release or Meson
dependency lock exists. This page is the runbook for acting on that issue.

## Ground rules

- `xemu/` is the source of truth. Builds compile it directly.
- `References2/` is a read-only oracle. Never edit it.
- `xemu/xemu-patches/` is a **historical provenance record**. Do not replay it
  against the current tree.
- `xemu/XEMU_COMMIT` and `xemu/XEMU_VERSION` record the release the core lineage
  began from.
- `xemu/XEMU_REVIEWED_COMMIT` records the newest upstream commit that has been
  reviewed for a selective port. The weekly report and the audit script compare
  from this commit, not from the original lineage base.
- A selective port never replaces the whole tree. If a deliberate full lineage
  rebase is ever chosen, that is a separate, larger change.

## 1. Freeze a branch

Start from a clean tree and create a branch for the port:

```powershell
git switch -c port/xemu-<version>
```

Record the release tag, its upstream commit and the current reviewed watermark.

## 2. Audit the delta

The audit script classifies every changed upstream file against the files XGrid
has modified and the owned paths declared in `scripts/xemu-owned-paths.txt`.

The first run clones upstream xemu into `wt-data/xemu-upstream` (git-ignored):

```powershell
pwsh -File scripts/xemu-upstream-audit.ps1 `
  -To v0.9.0 `
  -Report wt-data/xemu-v0.9.0-audit.md
```

- `-From` defaults to `xemu/XEMU_REVIEWED_COMMIT` (falling back to
  `XEMU_COMMIT`).
- Pass `-RefreshUpstream` to fetch new tags and branches.
- Pass `-UpstreamRepo <path>` to use an existing clone instead of the cache.
- Pass `-All` to list every upstream-only file; the default output groups them
  by area.

The report marks:

| Category | Meaning | Review priority |
| --- | --- | --- |
| **XGrid patch** | The file is named in a `xemu-patches` record. | Highest |
| **XGrid-owned** | The path is under a native module in `scripts/xemu-owned-paths.txt`, such as Chihiro, D3D11, HLSL or `xgrid-core`. | Highest |
| **dependency lock** | A `subprojects/*.wrap` revision changed. | High |
| **upstream only** | XGrid has no explicit change in that file. | Normal |

If a new native module is added outside `xgrid-core`, add its directory or file
to `scripts/xemu-owned-paths.txt` before the next audit.

## 3. Triage the changes

Review the audit report and the upstream commits, then decide per area:

| Decision | Use it when |
| --- | --- |
| **Adopt** | The change is compatible and useful as-is. |
| **Adapt** | The behavior is wanted, but the implementation needs XGrid changes. |
| **Skip** | The change conflicts with deliberate XGrid behavior or is irrelevant. |
| **Defer** | The change needs hardware, evidence or design work first. |

Practical rules:

- Read **XGrid patch** and **XGrid-owned** changes first. These are where a
  conflict can silently break the shell, the ABI, Chihiro or presentation.
- Prefer upstream security, correctness and compatibility fixes.
- Be conservative with changes to `ui/xemu.c`, `ui/xui/*`, `system/cpus.c`,
  NV2A and the in-process ABI.
- Do not adopt a change merely because it is newer.
- Review `.wrap` revisions separately; they are Meson dependency locks, not
  XGrid commits.

Record the adopted, adapted, skipped and deferred items in the pull request.
The audit report can be attached to the upstream issue; generated reports under
`wt-data/` are not committed.

## 4. Port the selected changes

Run these commands from the repository root. Extract the upstream diff into a
patch file, then apply it **under `xemu/`**:

```powershell
$patch = Join-Path $PWD 'wt-data/port-xemu-ui.patch'
git -C wt-data/xemu-upstream diff `
  --output=$patch `
  <from-commit>..<to-commit> -- ui/xemu.c

git apply --check --directory=xemu $patch
git apply --directory=xemu $patch
```

The `--directory=xemu` option is required because upstream patch paths start at
the repository root (`ui/xemu.c`), while the tracked tree is under `xemu/`.

To port one upstream commit at a time:

```powershell
$port = Join-Path $PWD 'wt-data/port'
git -C wt-data/xemu-upstream format-patch -1 <upstream-commit> -o $port
$patch = Get-ChildItem $port -Filter '0001-*.patch' |
    Select-Object -First 1 -ExpandProperty FullName
git apply --check --directory=xemu $patch
git apply --directory=xemu $patch
```

If a patch does not apply cleanly:

1. Do **not** force it.
2. Compare the upstream change with the current XGrid implementation.
3. Port the behavior by hand and keep the change small.
4. Add or update tests for the behavior.

Upstream commits may depend on earlier commits. Apply cumulative diffs or port
in upstream order instead of cherry-picking isolated hunks.

## 5. Review the XGrid-owned areas

After each port, check whether an upstream API or structure changed under a
native area:

- `xemu/xgrid-core/` and its Meson hooks;
- the in-process ABI and shell-facing status codes;
- presentation filters, the FPS overlay, frame-rate limit and frame skipping;
- fast-forward clock scaling and the vblank pace;
- the embedded window, DXGI presentation and input sink;
- Chihiro, D3D11 and HLSL modules;
- the disc drivers and their block-layer integration.

A clean upstream patch can still compile and then break the shell at runtime if
an ABI or display contract changed. Run the native build and the managed tests
before deciding that a port is complete.

## 6. Update the review watermark

After the release has been reviewed as a whole, update:

```text
xemu/XEMU_REVIEWED_COMMIT
```

Use the upstream release commit. This tells the weekly report and the next audit
where the review stopped. Updating the watermark does **not** mean every
upstream change was adopted; skipped and deferred changes are recorded in the
pull request.

Keep `XEMU_VERSION` and `XEMU_COMMIT` at the original lineage base unless the
core is deliberately rebased onto the new release as a whole. A full lineage
rebase is a separate decision:

- update `XEMU_VERSION` and `XEMU_COMMIT`;
- set `XEMU_REVIEWED_COMMIT` to the same commit;
- update `docs/reference/upstream-differences.md`;
- treat the old `xemu-patches` files as historical records of the 0.8.139
  lineage, not as a replay script.

When a `.wrap` dependency is adopted, update its `revision=` line and the
matching `xemu/dependencies.json` pin, version and source hash in the same
change, and confirm the build still resolves it without network surprises. If
the Windows host packages changed, refresh `xemu/msys2-packages.lock` after
reviewing the package diff.

## 7. Build and gates

```powershell
pwsh -File scripts/build-native-xemu.ps1
pwsh -File scripts/core-baseline.ps1
dotnet restore XGrid.sln
dotnet build XGrid.sln -c Release --no-restore --nologo
dotnet test XGrid.sln -c Release --nologo --no-build
dotnet format XGrid.sln --no-restore --verify-no-changes
```

For a release candidate, also run:

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

## 8. Smoke test the integrated core

The managed tests do not need firmware or game data. Finish with a manual pass
on a development machine:

- boot the dashboard and one game;
- test each renderer the build reports;
- check the presentation filters and the embedded window resize path;
- check the FPS overlay, frame-rate limit and frame skip;
- check 4× fast-forward from Space and the toolbar;
- check keyboard, gamepad and (for Chihiro) pointer input;
- save and load a state, then power off and resume;
- change a disc while the console runs;
- run a short headless session;
- check the `Activity` log for renderer, ABI or save-state errors.

Save states are QEMU snapshots. A core update can make an older state
incompatible; test with a disposable HDD image before relying on a user image.

## 9. Record the result

- Include the upstream version and commit range in the pull request.
- List adopted, adapted, skipped and deferred changes.
- Mention the audit report and the tests or games used for validation.
- Update `docs/reference/upstream-differences.md` only when the baseline or the
  patch record changes.
- Add a new `xemu-patches` record only for a new edit to an existing upstream
  file. New XGrid-native files are normal tracked files.

## Rollback

If a port causes regressions, revert the port commits. Do not rewrite the
upstream cache or the tracked tree. If the failure is in a user save state,
preserve the state and report the core version before attempting another load.
