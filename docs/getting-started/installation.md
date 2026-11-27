# Installation

XGrid is distributed as portable archives. There is no installer, no system
service and no registry dependency. Keep the extracted folder wherever you like.

## Download

Get the archive for your platform from the
[releases page](https://github.com/purelogiccode/XGrid/releases). If no packaged
archive is published yet, build the tree with
[Building from source](../development/building.md).

| Archive | Platform | Contains |
| --- | --- | --- |
| `XGrid-frontend-win-x64.zip` | Windows 10/11 x64 | Avalonia shell **and** the emulator core under `core/xemu/` |
| `XGrid-frontend-linux-x64.tar.gz` | Linux x64 | Avalonia shell **and** the standalone core under `core/xemu/` |
| `XGrid-frontend-osx-x64.tar.gz` | macOS Intel | Shell only |
| `XGrid-frontend-osx-arm64.tar.gz` | macOS Apple Silicon | Shell only |
| `XGrid-cli-<rid>.zip` / `.tar.gz` | all RIDs | Validation CLI and managed reference harness |

The archives are self-contained: you do not need to install .NET.

Every release tree also contains the license set, a `VERSION` file,
`RELEASE-NOTES.md`, per-RID `SHA256SUMS-<rid>.txt`, a combined
`SHA256SUMS.txt` and `package-manifest.json`.

> **Platform note.** Windows has the full shell integration. The Linux frontend
> archive bundles the standalone core, which presents and reads input through
> SDL in its own window; save states, pause/step, fast-forward and live disc
> swap need the Windows in-process core. The macOS archives contain the shell
> only.

## Verify the download

Windows PowerShell:

```powershell
Get-FileHash .\XGrid-frontend-win-x64.zip -Algorithm SHA256
Get-Content .\SHA256SUMS-win-x64.txt
```

Linux and macOS:

```bash
sha256sum -c SHA256SUMS-linux-x64.txt
# or
shasum -a 256 XGrid-frontend-osx-arm64.tar.gz
```

Compare the value with the official release page or the checksum file published
beside the archive. Do not run archives from a mirror you do not trust.

## Extract and run

1. Extract the archive into a folder you can write to.
2. Start the application:
   - **Windows:** `XGrid.Frontend.Avalonia.exe`
   - **Linux:** `./XGrid.Frontend.Avalonia`
   - **macOS:** the `XGrid.Frontend.Avalonia` executable inside the archive.
3. On first run, XGrid creates its data folder, an empty configuration file and
   the generated core working directory.

### Linux runtime packages

The Linux core is built against the host's system libraries rather than
bundling them. The release notes list the exact set; install the runtime
packages if they are missing. Typical names include SDL3, GLib, libepoxy, zstd,
lz4 and the usual X11/Wayland client libraries.

### macOS external core

The macOS archives are shell only. Point XGrid at an upstream xemu 0.8.139 build:
use **Settings → System → Emulator binary**, or set `XGRID_XEMU_CORE` before
launch. The shell manages the library, settings, logs and screenshots; the core
presents in its own window.

## Data folder

| Platform | Default data folder |
| --- | --- |
| Windows | `%APPDATA%\XGrid\` |
| Linux | `~/.config/XGrid/` (the platform application-data folder) |
| macOS | `~/Library/Application Support/XGrid/` |

Set `XGRID_FRONTEND_DATA_DIR` to use a portable or test-specific folder, or pass
`--data-dir <path>` for a single session. The application folder is never
modified at runtime.

Logs live separately under the per-user local application-data folder:

| Platform | Default log folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\XGrid\logs` |
| Linux | `~/.local/share/XGrid/logs` |
| macOS | the .NET local application-data folder under `XGrid/logs` |

## First launch warnings

The binaries are **not code-signed or notarized**.

- **Windows SmartScreen** may show “Windows protected your PC”. Choose
  **More info → Run anyway** only after verifying the archive checksum.
- **macOS Gatekeeper** may quarantine the archive. After extracting, either
  allow the app in **System Settings → Privacy & Security**, or run:

  ```bash
  xattr -dr com.apple.quarantine /path/to/XGrid
  ```

- **Linux** may need the execute bit preserved by the archive extractor. If it
  is missing, run `chmod +x XGrid.Frontend.Avalonia`.

## Updating

1. Keep your data folder (`settings.json`, `covers/`, `screenshots/`) and your
   firmware/HDD images wherever you store them.
2. Replace the application folder with the new release.
3. Start XGrid. Your settings and save states remain.

Save states live inside the HDD image (or the Chihiro snapshot store), not in
the application folder. Replacing the application does not remove them.

## Uninstalling

Delete the application folder. Delete the data folder only if you also want to
remove settings, local covers and screenshots. Your HDD image and game images
are separate files and are never touched by the uninstall.

## Next step

Continue to [First boot](first-boot.md).
