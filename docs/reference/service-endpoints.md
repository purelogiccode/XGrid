# Service endpoints

XGrid uses a small set of HTTPS services. Every call is best-effort: a failure
never prevents the emulator from starting or a game from running. This page is
the quick reference; the detailed contracts are linked below.

## Endpoints

| Purpose | Method and URL | Authentication | Opt-out |
| --- | --- | --- | --- |
| Usage statistics | `POST https://www.purelogiccode.com/ApplicationStats/stats` | `Authorization: Bearer <stats key>` | `XGRID_DISABLE_STATISTICS=1` |
| Bug reports | `POST https://www.purelogiccode.com/bugreport/api/send-bug-report` | `X-API-KEY: <bug-report key>` | `XGRID_DISABLE_BUG_REPORTS=1` |
| Donation verification | `POST https://www.purelogiccode.com/api/donations/verify` | `X-API-KEY: <desktop key>` | Do not check a donation |
| Donation page | `GET https://www.purelogiccode.com/donate?app=xgrid&email=...` | Anonymous | Do not press **Donate** |
| Update check | `GET https://api.github.com/repos/purelogiccode/XGrid/releases/latest` | Optional `XGRID_GITHUB_TOKEN` | `XGRID_DISABLE_UPDATE_CHECK=1` |
| Tag fallback | `GET https://api.github.com/repos/purelogiccode/XGrid/tags` | Optional token | Same as update check |

## Request summaries

### Statistics

```json
{
  "applicationId": "xgrid",
  "version": "0.1.0"
}
```

More: [Logging and diagnostics](../guides/diagnostics.md).

### Bug report

```json
{
  "message": "=== Environment Details === ...",
  "applicationName": "XGrid",
  "version": "0.1.0",
  "environment": "Windows 10.0; X64; 64-bit process, 64-bit OS",
  "stackTrace": "System.InvalidOperationException: ..."
}
```

More: [Logging and diagnostics](../guides/diagnostics.md) and
[Privacy and data](../legal/privacy.md).

### Donation verification

```json
{
  "email": "donor@example.com",
  "applicationSlug": "xgrid"
}
```

More: [Donation API](donation-api.md).

## Timeouts and failure behavior

| Client | Timeout | On failure |
| --- | --- | --- |
| Statistics | 8 seconds | Debug log, ignored. |
| Bug report | 12 seconds per report | Queue logs the failure and continues. |
| Donation | 10 seconds | Returns `Unavailable`; the banner stays. |
| Update check | 8 seconds | Debug log, no prompt. |

No service call is retried in a tight loop. The bug-report queue enforces the
service limit of ten reports per minute.

## Overrides

| Variable | Effect |
| --- | --- |
| `XGRID_DISABLE_STATISTICS` | Set to `1` or `true` to skip the launch hit. |
| `XGRID_DISABLE_BUG_REPORTS` | Set to `1` or `true` to stop report uploads. |
| `XGRID_DISABLE_UPDATE_CHECK` | Set to `1` or `true` to skip GitHub. |
| `XGRID_BUGREPORT_API_KEY` | Alternate bug-report key. |
| `XGRID_STATS_API_KEY` | Alternate statistics key. |
| `XGRID_GITHUB_TOKEN` | Token for a private repository or higher rate limit. |
| `XGRID_DONATION_ENDPOINT` | Alternate donation verification URL. |
| `XGRID_DONATION_PAGE` | Alternate donation page. |
| `XGRID_DONATION_API_KEY` | Alternate donation key. |

Override values are read once at startup. Missing values fall back to the
compiled defaults. A malformed endpoint is reported as an unavailable service
rather than crashing the shell.
