# Logging, diagnostics and updates

XGrid uses **Serilog** for all diagnostic logging. The shell, the CLI and the
library code write to one rolling log; warnings and errors can be submitted to
the centralized bug-report service.

Every control in the shell has a tooltip. Hover a button, menu item, path box,
slider or toggle to see what it does.

## Log files

Logs are written to the per-user local application-data folder, under
`XGrid/logs`, one file per day. The last 14 days are kept. On Windows this is
`%LOCALAPPDATA%\XGrid\logs`.

| Variable | Effect |
| --- | --- |
| `XGRID_LOG_DIRECTORY` | Write logs somewhere else. |
| `XGRID_LOG_LEVEL` | `Verbose`, `Debug`, `Information`, `Warning`, `Error` or `Fatal`. |
| `XGRID_FRONTEND_TRACE=1` | Enables debug-level frontend diagnostics. |

The **Activity** page shows the same events in the window.

## Bug reports

Warnings and errors are queued to the centralized bug-report API on a background
worker, at most ten per minute. Reports contain:

- environment details: date, application name and version, OS version,
  architecture, bitness, Windows/Linux/macOS version, processor count, base
  directory and temp path;
- the error message;
- the exception type, message, source and stack trace.

Set `XGRID_DISABLE_BUG_REPORTS=1` to stop forwarding. Logging continues.

## Usage statistics

At launch XGrid sends one hit to the ApplicationStats service with the
application id `xgrid` and the running version. The call is best-effort and
never delays startup. Set `XGRID_DISABLE_STATISTICS=1` to opt out.

## Update check

After the main window opens, XGrid asks GitHub for the latest release. If a
newer tag exists, a dialog offers to open the release page. The check is
best-effort and can be disabled with `XGRID_DISABLE_UPDATE_CHECK=1`.

The GitHub repository must be public, or `XGRID_GITHUB_TOKEN` must contain a
token that can read it. Without either, the check is skipped silently.

See the full guide: [Logging, diagnostics and updates](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/diagnostics.md).
