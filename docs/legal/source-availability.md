# Source availability

XGrid is distributed as free software. The source that corresponds to a release
is this repository, including the maintained core and its patch record.

## What “corresponding source” includes

For a release tag:

- the Avalonia shell and integration projects in `src/`;
- the managed validation projects and tests (they are part of the repository);
- the tracked core in `xemu/`;
- XGrid's native additions in `xemu/xgrid-core/`;
- upstream edits in `xemu/xemu-patches/`;
- the build, packaging and test scripts in `scripts/`;
- the license and notice files.

## Rebuilding

The release tree can be rebuilt from source with the documented toolchain:

```powershell
dotnet build XGrid.sln -c Release
pwsh -File scripts/build-native-xemu.ps1
pwsh -File scripts/package.ps1
```

On Linux, build the core with `bash scripts/build-native-xemu.sh`; stage cores
from several hosts with `scripts/package.ps1 -CoreRoot <dir>`.

See [Building from source](../development/building.md) and
[Releasing](../development/releasing.md).

## Why the source is in the same repository

The core is not a separate download or an opaque prebuilt library. It is tracked
in `xemu/`, built by the repository's scripts, and shipped with the shell. That
makes the GPL source obligation concrete: the release tag contains everything
needed to rebuild the binaries.

## Third-party source

Third-party components keep their own upstream sources and licenses. The notices
record where each one comes from. libchdr is vendored; its license text ships in
`xemu/xgrid-core/thirdparty/libchdr/LICENSE.txt`. libCCI is vendored under
`xemu/xgrid-core/thirdparty/libcci/` with its GPL-2.0 license text.

## No firmware or games

Source availability does not grant rights to Microsoft firmware, BIOS images,
EEPROMs, HDD images or game data. Users must supply their own legally obtained
files.

## Publication

Each release tag is the source offer: it contains the source the archives were
built from. If a downstream redistributor ships XGrid binaries, that
redistributor is responsible for providing the corresponding source and the
notices, or a written offer valid under the GPL.
