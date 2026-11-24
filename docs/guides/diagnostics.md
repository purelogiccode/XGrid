# Logging, diagnostics and updates

XGrid routes its diagnostic output through **Serilog**. The shell, the CLI and
the library code write to the same rolling log, the debugger receives the same
events, and warnings or errors can be forwarded to the centralized bug-report
service.

Every control in the shell has a tooltip. Hover a button, menu item, path box,
slider or toggle to read what it does before changing it.

## Log files

Serilog writes one file per day to the per-user local application-data folder:

| Platform | Default folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\XGrid\logs` |
| Linux | `~/.local/share/XGrid/logs` |
| macOS | the .NET local application-data folder under `XGrid/logs` |

Files are named `xgrid-YYYYMMDD.log` and the last 14 days are kept. Set
`XGRID_LOG_DIRECTORY` to write somewhere else, or `XGRID_LOG_LEVEL` to
`Verbose`, `Debug`, `Information`, `Warning`, `Error` or `Fatal`.

`XGRID_FRONTEND_TRACE=1` is a compatibility switch that raises the level to
`Debug` for the native display and embedding diagnostics.

The **Activity** page still shows the session log in the window. It is a view
of the same events, not a second logging system.

## What is logged

- application start and stop, the version, the data folder and the command line;
- core start, stop and exit codes;
- settings read and write failures, corrupt settings files and their backups;
- media and display failures, with the exception and stack trace at debug level;
- warning and error events from the shell and from the emulator hosting layer.

Hot emulator paths check `Log.IsEnabled` before formatting a message, so the
rolling file does not slow down normal execution.

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
`https://www.purelogiccode.com/ApplicationStats/stats` with the application id
`xgrid` and the running version. This is what populates the usage dashboard.
The call is best-effort: a network failure, an offline machine or a rate limit
does not affect startup. Headless runs send the same hit.

Set `XGRID_DISABLE_STATISTICS=1` to opt out.

## Update check

After the main window opens, the shell asks GitHub for the latest release. If
the tag is newer than the running build, a small dialog shows the current and
latest versions and asks whether to open the release page. The check is skipped
when the repository has no release and no tags.

The repository currently needs authentication for the GitHub API. Set
`XGRID_GITHUB_TOKEN` to a token that can read the repository, or make the
repository public. A missing token only disables the check; it is not an error.

Set `XGRID_DISABLE_UPDATE_CHECK=1` to skip the check.

## Environment variables

| Variable | Effect |
| --- | --- |
| `XGRID_LOG_DIRECTORY` | Log folder override. |
| `XGRID_LOG_LEVEL` | Minimum Serilog level. |
| `XGRID_FRONTEND_TRACE` | `1` enables debug-level frontend/native diagnostics. |
| `XGRID_DISABLE_BUG_REPORTS` | `1` disables warning/error forwarding. |
| `XGRID_DISABLE_STATISTICS` | `1` disables the startup usage hit. |
| `XGRID_DISABLE_UPDATE_CHECK` | `1` disables the GitHub release check. |
| `XGRID_GITHUB_TOKEN` | Token for the GitHub release check. |
| `XGRID_BUGREPORT_API_KEY` | Override the bug-report API key. |
| `XGRID_STATS_API_KEY` | Override the statistics API key. |
| `XGRID_DONATION_ENDPOINT` | Override the donation verification endpoint. |
| `XGRID_DONATION_PAGE` | Override the donation page. |
| `XGRID_DONATION_API_KEY` | Override the donation API key. |

The command line still controls the emulator itself. See
[Configuration](../reference/configuration.md) and [Headless and CLI](headless.md).
See also [Service endpoints](../reference/service-endpoints.md),
[Supporting XGrid](donations.md) and [Privacy and data](../legal/privacy.md).
