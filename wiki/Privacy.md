# Privacy and data

XGrid runs locally. It does not require an account, upload game data or include
advertising. The network features are small and can be turned off.

| Data | When | Destination | Opt-out |
| --- | --- | --- | --- |
| Usage hit | Once per launch | ApplicationStats | `XGRID_DISABLE_STATISTICS=1` |
| Warning/error report | On a warning or error | Bug-report service | `XGRID_DISABLE_BUG_REPORTS=1` |
| Donation email | When checking a donation or refreshing a saved address at startup | Donation API | Do not check |
| Update check | Once per launch | GitHub | `XGRID_DISABLE_UPDATE_CHECK=1` |
| Logs | Continuously | Local data folder | Delete the files |
| Settings | When changed | Local `settings.json` | Delete the file |

## What is sent

- **Statistics:** application id `xgrid` and version only.
- **Bug reports:** environment (OS, architecture, bitness, processor count,
  base directory, temp path), the error message and the exception details.
- **Donation:** the email you entered and the `xgrid` slug.
- **Update check:** a user-agent built from the app name and version.

No firmware, disc images, save states, screenshots or game-file contents are
uploaded. Paths may appear in an exception message, but the app does not attach
game data.

## Logs

Rolling files live under the per-user local application-data folder
(`%LOCALAPPDATA%\XGrid\logs` on Windows) and are kept for 14 days. The donation
email is never written to them.

## Donation records

The website and its payment provider hold the donation record and the email used
there. XGrid stores only the local email and confirmed flag in `settings.json`.

Full policy: [Privacy and data](https://github.com/purelogiccode/XGrid/blob/master/docs/legal/privacy.md).
Contact **contact@purelogiccode.com** with privacy questions.
