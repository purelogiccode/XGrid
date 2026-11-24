# Privacy and data

XGrid runs locally. It does not require an account, does not upload game data and
does not include advertising or third-party analytics SDKs. The network features
are deliberately small, documented and optional.

## Summary

| Data | When | Where it goes | Can it be disabled? |
| --- | --- | --- | --- |
| Usage hit | Once at launch | ApplicationStats service | `XGRID_DISABLE_STATISTICS=1` |
| Warning/error bug report | When the app logs a warning or error | Bug-report service | `XGRID_DISABLE_BUG_REPORTS=1` |
| Donation email | When you check a donation or a saved address is refreshed | Donation verification API | Do not use **I already donated** |
| Update check | Once at launch | GitHub API | `XGRID_DISABLE_UPDATE_CHECK=1` |
| Log file | Continuously | Local data folder | Delete the file; set `XGRID_LOG_LEVEL` |
| Settings | When changed | Local `settings.json` | Delete the file |

## Usage statistics

The shell and headless runner send one `POST` per launch with:

- application id `xgrid`;
- the product version.

No email, machine name, hardware identifier, game title or path is sent. A
failure is ignored. The call is fire-and-forget and never delays the first
frame. The validation CLI does not send statistics.

## Bug reports

Warnings and errors are queued and sent to the centralized bug-report service.
A report contains:

- the application name and version;
- the operating system, architecture, process/OS bitness, processor count,
  base directory and temporary path;
- the warning/error message;
- the exception type, message, source and stack trace, when an exception exists.

Reports do not contain firmware, disc images, save states, screenshots or the
contents of game files. Paths may appear in an exception message if the code
that failed included them. The local log file contains the same event and is
useful when a report cannot be sent.

## Donation email

The optional donation email is stored locally in `settings.json`. It is sent only
to the donation verification API, only when you ask to check a donation or when
a previously saved address is refreshed at startup. It is never written to the
log and never attached to bug reports. The confirmed donation flag is local; the
website holds the donation record and the email you used there.

## Logs

Serilog writes rolling files under the per-user local application-data folder
(`%LOCALAPPDATA%\XGrid\logs` on Windows). Fourteen daily files are retained. The
log includes startup details, configured paths and diagnostic messages; it does
not include the donation email. Delete the folder at any time.

## Update check

The update check asks the GitHub API for the latest release or tag of the public
repository. It sends a user-agent built from the application name and version.
When `XGRID_GITHUB_TOKEN` is configured, the token is used as a bearer token and
is never logged.

## Website

The donation page, bug-report service, statistics service and product downloads
are hosted by PureLogicCode. Their server-side retention and payment processing
are described on the website and by the payment provider. XGrid does not control
those records.

## Contact

Privacy questions: **contact@purelogiccode.com**. Security reports:
**contact@purelogiccode.com** or GitHub private vulnerability reporting. See
`SECURITY.md`.
