# Logging and diagnostics

XGrid routes diagnostics through **Serilog**. The shell, CLI and library code
write to one rolling log; warnings and errors can be forwarded to the bug-report
service. The **Activity** page shows the same events in the app.

## Activity

**Activity** records the launch plan, renderer, scale, RAM, disc, audio backend,
core start/stop and failures. Search or copy the relevant lines into a bug
report. The log dock shows the newest lines while you play.

## Log files

| Platform | Default folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\XGrid\logs` |
| Linux | `~/.local/share/XGrid/logs` |
| macOS | the .NET local application-data folder under `XGrid/logs` |

Files are `xgrid-YYYYMMDD.log`; 14 days are kept. Override with
`XGRID_LOG_DIRECTORY` or set `XGRID_LOG_LEVEL` to `Verbose`, `Debug`,
`Information`, `Warning`, `Error` or `Fatal`. `XGRID_FRONTEND_TRACE=1` enables
debug-level frontend/native diagnostics.

In separate-process mode the core writes `<data>/xemu/xemu.log`.

## Bug reports and statistics

Warnings and errors are queued to the bug-report service on a background worker
(ten per minute maximum). A report contains the environment, application
version, message and exception details. It does not contain game data, firmware,
save states or screenshots.

One usage hit is sent at launch with the application ID and version only.

Opt out:

```text
XGRID_DISABLE_BUG_REPORTS=1
XGRID_DISABLE_STATISTICS=1
XGRID_DISABLE_UPDATE_CHECK=1
```

## Update check

After the window opens, XGrid asks GitHub for the latest release (or tag). If a
newer version exists, it offers to open the release page. The repository is
public, so no token is required; `XGRID_GITHUB_TOKEN` is optional and only raises
the API rate limit.

## Reporting a bug

Include the version, platform, renderer, hosting mode, the Activity lines and
the matching log excerpt. Use the
[issue tracker](https://github.com/purelogiccode/XGrid/issues). For
vulnerabilities, use private reporting per
[SECURITY.md](https://github.com/purelogiccode/XGrid/blob/master/SECURITY.md).

Full guide: [Logs and diagnostics](https://github.com/purelogiccode/XGrid/blob/master/docs/guides/diagnostics.md)
and [Privacy and data](Privacy).
