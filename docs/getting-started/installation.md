# Installation

XGrid is distributed as portable archives. There is no installer and no system
service; keep the extracted folder wherever you like.

## Download

| Archive | Platform | Contains |
| --- | --- | --- |
| `XGrid-frontend-win-x64.zip` | Windows 10/11 x64 | Avalonia shell **and** the emulator core under `core/xemu/` |
| `XGrid-cli-win-x64.zip` | Windows 10/11 x64 | Validation CLI and the managed reference harness |
| `XGrid-frontend-linux-x64.tar.gz` | Linux x64 | Avalonia shell **and** the standalone emulator core under `core/xemu/` |
| `XGrid-frontend-osx-x64.tar.gz` | macOS Intel | Shell only |
| `XGrid-frontend-osx-arm64.tar.gz` | macOS Apple Silicon | Shell only |
| `XGrid-cli-<rid>` | all RIDs | Validation CLI |

> **Windows has the full shell integration; Linux has the bundled core with its
> own window.** On Linux the core presents and reads input through SDL, so
> save states, pause/step and disc swap are not available. The macOS archives
> contain the shell only; point them at an upstream xemu 0.8.139 build with
> `XGRID_XEMU_CORE` or the **Emulator binary** setting.

Every archive includes:

- `COPYING` and `LICENSE`;
- `THIRD_PARTY_NOTICES.md`;
- `README.md` and `RELEASE-NOTES.md`;
- a `VERSION` file.

The Linux core links the host's SDL3, GLib, libepoxy, zstd and related system
libraries; install their runtime packages if they are not already present.

Checksums and `package-manifest.json` are published beside the archives.

## Extract and run

1. Extract the archive to a folder you can write to.
2. Start the application:
   - Windows: `XGrid.Frontend.Avalonia.exe`
   - Linux: `./XGrid.Frontend.Avalonia`
   - macOS: run the `XGrid.Frontend.Avalonia` executable inside the extracted archive.
3. On first run, XGrid creates its data folder and an empty configuration.

```text
Windows  %APPDATA%\XGrid\
Linux    ~/.config/XGrid/        (or the platform application-data folder)
macOS    ~/Library/Application Support/XGrid/
```

Set `XGRID_FRONTEND_DATA_DIR` to use a portable or test-specific data folder.

## First launch warnings

The binaries are **not code-signed or notarized**.

- **Windows SmartScreen** may show “Windows protected your PC”. Choose
  **More info → Run anyway** only if you downloaded the archive from the
  official release page and verified its checksum.
- **macOS Gatekeeper** may quarantine the archive. After extracting:
  ```bash
  xattr -dr com.apple.quarantine /path/to/XGrid
  ```

## Updating

1. Keep your data folder (`settings.json`, `xemu/`, `screenshots/`).
2. Replace the application folder with the new release.
3. Start XGrid; your settings and save states remain in the data folder.

Save states live inside the HDD image, not in the application folder, so replacing
the application does not remove them.

## Next step

Continue to [First boot](first-boot.md).
