# Native dependencies

The core has three dependency surfaces, and each has one record:

| Surface | Record | Covers |
| --- | --- | --- |
| Upstream lineage | `xemu/XEMU_VERSION`, `xemu/XEMU_COMMIT`, `xemu/XEMU_REVIEWED_COMMIT` | The xemu/QEMU release the core started from and the newest reviewed upstream commit. |
| Third-party source | `xemu/dependencies.json` and `xemu/subprojects/*.wrap` | Meson git and file wraps, the downloaded `dsp56300` library, vendored libchdr/libCCI and their nested codecs. |
| Windows build host | `xemu/msys2-packages.lock` | The MSYS2 MINGW64 toolchain and runtime DLLs the release links and ships. |

`THIRD_PARTY_NOTICES.md` remains the canonical license record, and `licenses/`
holds the shipped license texts. The records above answer "which version is
this and where does it come from", not "under which license is it shipped".

## The manifest

`xemu/dependencies.json` lists every native component with:

| Field | Meaning |
| --- | --- |
| `id` | Stable name used by the report. |
| `kind` | `wrap-git`, `wrap-file`, `downloaded` or `vendored`. |
| `track` | `tags`, `commits` or `none`; see the policy below. |
| `version` | The release the pin corresponds to, for `tags` rows. |
| `pin` | The commit or Meson revision, for `commits` rows and git wraps. |
| `sourceHash` | The tarball SHA-256 for `wrap-file` rows. |
| `upstream` | The canonical upstream repository or project page. |
| `license` | The license recorded in the notices; a cross-check, not the license text. |
| `location` | The file or directory in this repository. |
| `notes` | Why the dependency is here and anything an update must preserve. |

Every `xemu/subprojects/*.wrap` must have one entry. Vendored directories under
`xemu/xgrid-core/thirdparty/` and the tracked subprojects (`dsp56300`,
`libvduse`, `libvhost-user`) must be covered by at least one `location`.

## Checks

```powershell
python .github/scripts/xemu_upstream_report.py --check
pwsh -File scripts/msys2-lock.ps1
```

`--check` is offline. It verifies that every wrap and vendored tree is listed,
that the manifest pin, version and source hash still match the tree, that the
downloaded `dsp56300` version matches its `meson.build`, and that the host lock
exists. The weekly workflow runs it before the network report.

`scripts/msys2-lock.ps1` compares the installed MSYS2 packages with
`xemu/msys2-packages.lock`. Run it on the Windows build host after updating
MSYS2. Refresh the lock with `-Write` only after reviewing the package changes;
a lock refresh changes the shipped DLL set and may change the license table.

## The weekly report

`.github/workflows/xemu-upstream-report.yml` runs every Monday and can be
dispatched manually. It reports:

- the reviewed upstream xemu commit versus the latest xemu release;
- every manifest component according to its `track` policy;
- the Windows host package lock.

It opens or updates one issue titled `[xemu-updates] ...` and closes the issue
when no release update, manifest problem or failed query remains. An
`upstream moved` row for a commit-pinned component is informational and does not
keep the issue open by itself. The report is notification-only: it never edits
the tree, opens pull requests or implies an obligation to adopt a change. A
failed upstream query keeps the issue open rather than reporting a false clean
state.

## Update policy

| `track` | Used for | What an update means |
| --- | --- | --- |
| `tags` | Release-pinned libraries: SDL3, `dsp56300`, libchdr, miniz and zstd. | Adopt a newer release tag deliberately. Do not chase upstream `HEAD`. |
| `commits` | xemu forks (`imgui`, `implot`, `nv2a_vsh_cpu`, `genconfig`) and QEMU project pins. | Upstream moved; review the commits before changing the pin. A move is not automatically an update. |
| `none` | QEMU copies, the LZMA SDK, and fallback-only wraps such as curl and xxHash. | Recorded for inventory only; on Windows the shipped library comes from the host lock, and the other entries follow the xemu/QEMU lineage. |

Rules:

- Do not bump a pin because a newer commit exists. Build the core and run the
  smoke tests in [Updating from upstream xemu](xemu-upgrade.md) first.
- On Windows, system packages win over the Meson fallbacks for SDL3, zlib,
  zstd, LZ4, GLib, OpenSSL and the other runtime libraries. The host lock, not
  the `.wrap` revision, is what the release actually ships.
- When a dependency is replaced, removed or its license changes, update
  `THIRD_PARTY_NOTICES.md`, `licenses/` and this manifest in the same change.
- When a package is added to the build host, add it to `scripts/msys2-lock.ps1`
  and refresh the lock.
- `dsp56300` is downloaded by its own `meson.build`, not a Meson wrap. The file
  pins the release archive SHA-256 for every supported target; when the version
  moves, update the version and the whole hash map together.

## SBOM and vulnerability scan

`.github/scripts/native_sbom.py` produces a deterministic CycloneDX 1.5 SBOM
from the manifest, the Windows host lock and, when given, the package manifest:

```powershell
python .github/scripts/native_sbom.py --output native-sbom.cdx.json

# Include every shipped DLL's version and SHA-256.
python .github/scripts/native_sbom.py `
  --package-manifest dist/package-manifest.json `
  --output dist/native-sbom.cdx.json
```

The release workflow generates the source SBOM, runs `osv-scanner` against it
with a pinned scanner version, and uploads both the SBOM and the scan result
with the release. The scan is report-only: an OSV finding is attached as
`osv-native.json` rather than blocking the release, because C/C++ attribution
still needs manual triage. Run the same command locally with the scanner binary
for the host platform:

```powershell
osv-scanner scan source --lockfile native-sbom.cdx.json `
  --format json --output-file osv-native.json
```

## Packaging

`scripts/package.ps1` records every `core/xemu/*.dll` name, version resource and
SHA-256 under `core.libraries` in `package-manifest.json`.
`scripts/package-verify.ps1` fails when a shipped DLL is missing from the
manifest or the manifest lists a DLL the archive does not carry. The hash
identifies a DLL even when its version resource is empty. This makes an old host
package visible in a release archive even when the tracked pins have not
changed.

## See also

- [Updating from upstream xemu](xemu-upgrade.md) - selective-port runbook.
- [Core lineage and upstream](xemu-upstream.md) - the lineage markers.
- [Building from source](building.md) - prerequisites and build commands.
- [Quality gates](quality-gates.md) - the local and CI checks.
- [Releasing](releasing.md) - archives, licenses and the package manifest.
