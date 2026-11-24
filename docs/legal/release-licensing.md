# Release licensing

An XGrid release is a GPL binary distribution. The package must carry the
license set and the corresponding source must be available.

## What every archive carries

- `COPYING` — the GPL-2.0 text.
- `LICENSE` — the project notice.
- `THIRD_PARTY_NOTICES.md` — every redistributed component and ported file.
- `README.md`, `RELEASE-NOTES.md`, `VERSION`.

Checksums and `package-manifest.json` are published beside the archives.

## The Windows core package

The `win-x64` frontend archive also redistributes:

- `xemu.exe`;
- `xgrid_core.dll`;
- `xgrid-core.json`;
- the runtime DLLs those binaries import.

The core is GPL-2.0; the runtime DLLs keep their own licenses. The notices list
them, including the LGPL-3.0-or-later libraries that make the combined package
GPL-3.0-or-later where included.

## Source

The corresponding source is this repository at the released tag:

- the Avalonia shell and integration projects under `src/`;
- the tracked core under `xemu/`;
- XGrid's native additions under `xemu/xgrid-core/`;
- the upstream edit record under `xemu/xemu-patches/`;
- the build scripts under `scripts/`.

See [Source availability](source-availability.md).

## Verification

```powershell
pwsh -File scripts/package.ps1
pwsh -File scripts/package-verify.ps1
```

`package-verify.ps1` checks license completeness, archive contents, the bundled
core, checksums and the manifest.

## What must not ship

- BIOS/flash ROM images;
- MCPX boot ROMs;
- EEPROM dumps;
- HDD images;
- game discs or extracted game data;
- code-signed binaries claiming to be official unless they are.

## Dependency license texts

The full license texts of the redistributed libraries ship in `licenses/` in
every release archive. `licenses/README.md` maps each component to its text; the
copyright lines remain in `THIRD_PARTY_NOTICES.md`. The package gate requires
every text in the folder in every archive.

When a dependency's license text changes, or a component is added or removed,
update the folder and the mapping in the same change.

## Changes to the license set

If a release adds or removes a redistributed component:

1. update `THIRD_PARTY_NOTICES.md`;
2. update this page and [Licensing](licensing.md);
3. re-run the package gate;
4. record the change in the release notes.

## Not legal advice

Review the actual license texts before distributing a modified build.
