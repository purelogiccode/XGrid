# Releasing

XGrid releases are portable archives built from one versioned source tree.

## Version

The product version has one source: `Directory.Build.props` (`<Version>`). The
CLI, the frontend About page and the release packages read the same value.

Release notes must exist at:

```text
docs/release-notes/v<version>.md
```

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

### Windows frontend core

The `win-x64` frontend archive includes the emulator:

```text
core/xemu/xemu.exe
core/xemu/xgrid_core.dll
core/xemu/xgrid-core.json
<runtime DLLs>
```

[Build the core first](building.md):

```powershell
pwsh -File scripts/build-native-xemu.ps1
```

`-SkipCore` is a development escape and produces an archive that cannot emulate.

Linux and macOS frontend archives currently contain the shell only.

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
- executable presence and magic;
- no symbols or firmware/game data;
- SHA-256 sums and manifest;
- the Windows CLI `--version`;
- the bundled core in the Windows frontend archive.

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

The `release.yml` workflow mirrors the same steps when run manually; it also
generates an SBOM and build provenance.

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
Gatekeeper consequences in the release notes; users verify checksums.
