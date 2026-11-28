# Releasing

XGrid releases are portable archives built from one versioned source tree.

## Version

The product version has one source: `Directory.Build.props` (`<Version>`). The
CLI, the frontend About page and the release packages read the same value.

Release notes must exist at:

```text
docs/release-notes/v<version>.md
```

The release notes index is [`docs/release-notes/index.md`](../release-notes/index.md).

## Build the release tree

```powershell
pwsh -File scripts/package.ps1
```

Optional:

```powershell
pwsh -File scripts/package.ps1 -Version 0.1.0 -Rids win-x64,linux-x64
```

The script publishes the CLI and frontend self-contained for:

- `win-x64`
- `linux-x64`
- `osx-x64`
- `osx-arm64`

It writes deterministic archives with fixed timestamps, sorted entries, the
license set, release notes, `VERSION`, per-RID checksums and
`package-manifest.json`.

For a Windows core, `package-manifest.json` also records every shipped
`core/xemu/*.dll` name, version resource and SHA-256 under `core.libraries`.
Verification fails when the archive and that record disagree, so the dependency
inventory stays visible in the release artifacts.

The release workflow also generates `sbom-native.cdx.json` from the native
manifest and host lock, runs `osv-scanner` against it, and attaches both files
to the release. See [Native dependencies](dependencies.md).

### Frontend core

A frontend archive carries the core when a build for that RID is available:

```text
# win-x64
core/xemu/xemu.exe
core/xemu/xgrid_core.dll
core/xemu/xgrid-core.json
<runtime DLLs>

# linux-x64
core/xemu/xemu
core/xemu/xgrid-core.json
core/xemu/LICENSE.txt
```

[Build the core first](building.md) on its own host:

```powershell
pwsh -File scripts/build-native-xemu.ps1   # Windows
```

```bash
bash scripts/build-native-xemu.sh          # Linux
```

The default lookup is `xemu/dist`, which matches the host's RID. To package
several RIDs from one host, build each core on its platform and collect the
outputs in per-RID folders, then pass the parent directory:

```text
cores/
  win-x64/       # xemu/dist from Windows
  linux-x64/     # xemu/dist from Linux
```

```powershell
pwsh -File scripts/package.ps1 -CoreRoot cores
```

The packager refuses a requested `win-x64` or `linux-x64` archive without its
core. `-SkipCore` is a development escape and produces archives that cannot
emulate; `package-verify.ps1` accepts that tree only with `-SkipCoreCheck`.

The macOS archives are shell-only: there is no XGrid core build script for macOS
yet. A flat Mach-O core staged under `cores/osx-x64/` or `cores/osx-arm64/` is
packaged and verified when supplied.

The packager strips Windows and Linux cores when the packaging host matches
their platform. A cross-staged or macOS core is packaged as supplied; strip it
on its own host first if size matters. The gate warns but accepts an unstripped
core.

## Verify

```powershell
pwsh -File scripts/package-verify.ps1
```

The same gate runs at the end of `scripts/package.ps1`. A release host runs it
for all four RIDs; the tree carries eight archives, the per-RID and combined
checksum files and `package-manifest.json`.

The gate checks:

- license files in every archive;
- notices coverage for ported files;
- expected archives for every RID;
- executable presence, magic and Unix execute bits;
- no symbols or firmware/game data;
- SHA-256 sums and manifest;
- the Windows CLI `--version`;
- the bundled core for every RID the manifest names, including its runtime
  libraries and license text where the platform needs them;
- that a Windows or Linux release tree really contains those cores.

## Publish

Upload the release tree:

```text
dist/
  XGrid-cli-<rid>.zip|.tar.gz
  XGrid-frontend-<rid>.zip|.tar.gz
  SHA256SUMS-<rid>.txt
  SHA256SUMS.txt
  package-manifest.json
```

The manual `release.yml` workflow verifies the tree on Windows and Linux,
publishes self-contained binaries for every platform, generates an SBOM,
attests build provenance and creates the GitHub release. The local
`scripts/package.ps1` tree is the release artifact because it is the path that
bundles and verifies the per-platform cores.

## Licensing checklist

- `COPYING` is the GPL-2.0 text.
- `LICENSE` is the project notice.
- `THIRD_PARTY_NOTICES.md` covers every redistributed component and ported file.
- The core source and `xemu/xemu-patches/` are available with the release.
- `licenses/` carries the dependency license texts; the gate checks every text
  in every archive.
- No firmware or game data is included.

See [Release licensing](../legal/release-licensing.md) and
[Source availability](../legal/source-availability.md).

## Signing

The archives are **not code-signed or notarized**. Document the SmartScreen and
Gatekeeper consequences in the release notes; users verify checksums. See
[Installation](../getting-started/installation.md#first-launch-warnings).

## Related pages

- [Building from source](building.md)
- [Quality gates](quality-gates.md)
- [Release notes index](../release-notes/index.md)
