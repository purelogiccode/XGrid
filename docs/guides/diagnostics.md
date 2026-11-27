# Logs and diagnostics

XGrid routes its diagnostic output through **Serilog**. The shell, the CLI and
the library code write to the same rolling log; warnings and errors can also be
forwarded to the centralized bug-report service. The **Activity** page shows the
same events inside the application.

Every control in the shell has a tooltip. Hover a button, menu item, path box,
slider or toggle to read what it does before changing it.

## The Activity page

**Activity** is the first place to look after a failed launch. It records:

- the launch plan and selected core;
- renderer, scale, RAM and audio backend;
- the inserted disc;
- process start/stop and exit codes;
- warnings and errors from the shell and hosting layer.

Use the search/filter and **Copy** action to include the relevant lines in a bug
report. The log dock at the bottom of the main window shows the newest lines
while you play.

## Log files

Serilog writes one file per day to the per-user local application-data folder:

| Platform | Default folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\XGrid\logs` |
| Linux | `~/.local/share/XGrid/logs` |
| macOS | the .NET local application-data folder under `XGrid/logs` |

Files are named `xgrid-YYYYMMDD.log`; the last 14 days are kept. Set
`XGRID_LOG_DIRECTORY` to write somewhere else, or `XGRID_LOG_LEVEL` to
`Verbose`, `Debug`, `Information`, `Warning`, `Error` or `Fatal`.

`XGRID_FRONTEND_TRACE=1` raises the level to `Debug` for the native display and
embedding diagnostics.

In separate-process mode the core writes its own log to
`<data>/xemu/xemu.log`. In-process hosting has no separate core log; its messages
reach Activity and the rolling log.

## What is logged

- application start and stop, version, data folder and command line;
- core start, stop and exit codes;
- settings read/write failures, corrupt settings files and their backups;
- media and display failures, with exception and stack trace at debug level;
- warning and error events from the shell and the emulator hosting layer.

Hot emulator paths check `Log.IsEnabled` before formatting, so the rolling file
does not slow down normal execution.

## Bug reports

Warnings and errors are queued to
`https://www.purelogiccode.com/bugreport/api/send-bug-report`. The queue runs on
a background worker and respects the service limit of ten reports per minute.
The application never waits for the network on the UI thread.

Every submitted report contains:

```text
=== Environment Details ===
Date
Application Name
Application Version
OS Version
Architecture
Bitness
Windows, Linux or MacOsX Version
Processor Count
Base Directory
Temp Path

=== Error Details ===
Error message

=== Exception Details ===
Type
Message
Source
StackTrace
```

Set `XGRID_DISABLE_BUG_REPORTS=1` to disable forwarding. The log file is still
written.

## Usage statistics

At launch the shell sends one hit to
`https://www.purelogiccode.com/ApplicationStats/stats` with the application ID
`xgrid` and the running version. This populates the usage dashboard. The call is
best-effort: a network failure, an offline machine or a rate limit does not
affect startup. Headless runs send the same hit.

Set `XGRID_DISABLE_STATISTICS=1` to opt out.

## Update check

After the main window opens, the shell asks GitHub for the latest release. If the
tag is newer than the running build, a small dialog shows the current and latest
versions and asks whether to open the release page. If the repository has no
release yet, the checker falls back to tags. The check is skipped when neither
returns a usable version.

The repository is public, so no authentication is required. `XGRID_GITHUB_TOKEN`
is optional and only raises the GitHub API rate limit. A missing token never
disables the check.

Set `XGRID_DISABLE_UPDATE_CHECK=1` to skip it.

## Environment variables

| Variable | Effect |
| --- | --- |
| `XGRID_FRONTEND_DATA_DIR` | Data folder override. |
| `XGRID_XEMU_CORE` | Core executable override. |
| `XGRID_LOG_DIRECTORY` | Log folder override. |
| `XGRID_LOG_LEVEL` | Minimum Serilog level. |
| `XGRID_FRONTEND_TRACE` | `1` enables debug-level frontend/native diagnostics. |
| `XGRID_DISABLE_BUG_REPORTS` | `1` disables warning/error forwarding. |
| `XGRID_DISABLE_STATISTICS` | `1` disables the startup usage hit. |
| `XGRID_DISABLE_UPDATE_CHECK` | `1` disables the GitHub release check. |
| `XGRID_GITHUB_TOKEN` | Optional token for a higher GitHub API rate limit. |
| `XGRID_BUGREPORT_API_KEY` | Override the bug-report API key. |
| `XGRID_STATS_API_KEY` | Override the statistics API key. |
| `XGRID_DONATION_ENDPOINT` | Override the donation verification endpoint. |
| `XGRID_DONATION_PAGE` | Override the donation page URL. |
| `XGRID_DONATION_API_KEY` | Override the donation API key. |

The command line controls the emulator itself. See
[Configuration](../reference/configuration.md) and
[Command line and headless](headless.md).

## Reporting a good bug

Include:

- XGrid version and platform;
- renderer and core hosting mode;
- the exact message from Activity and the matching `xgrid-YYYYMMDD.log` lines;
- the image format (ISO/CHD/CCI/ZAR) and whether the same title works in
  upstream xemu 0.8.139;
- steps to reproduce, if they are reliable.

Use the [issue tracker](https://github.com/purelogiccode/XGrid/issues) for
emulation and UI bugs. Report vulnerabilities privately through
[GitHub Security Advisories](https://github.com/purelogiccode/XGrid/security/advisories)
or [`SECURITY.md`](https://github.com/purelogiccode/XGrid/blob/master/SECURITY.md).

## Related pages

- [Privacy and data](../legal/privacy.md)
- [Service endpoints](../reference/service-endpoints.md)
- [Supporting XGrid](donations.md)
- [Troubleshooting](../reference/troubleshooting.md)
