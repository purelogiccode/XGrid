# Privacy and data

XGrid runs locally. It does not require an account, upload game data, show ads or
include third-party analytics SDKs.

| Data | When | Where | Opt-out |
| --- | --- | --- | --- |
| Usage hit | Once at launch | ApplicationStats | `XGRID_DISABLE_STATISTICS=1` |
| Bug report | On a warning/error | Bug-report service | `XGRID_DISABLE_BUG_REPORTS=1` |
| Donation email | Donation check | Donation API | Do not check |
| Cover catalog/images | Online covers enabled | Cloudflare R2 | Settings → Library → Cover art |
| Update check | Once at launch | GitHub API | `XGRID_DISABLE_UPDATE_CHECK=1` |
| Logs/settings | Local | Data folder | Delete or change level |

## Details

- **Usage hit:** application ID and version only.
- **Bug report:** environment, version, message and exception. No game data,
  firmware, save states, screenshots or file contents. Paths may appear in an
  exception.
- **Donation email:** stored locally; sent only to the verification API; never
  logged or attached to bug reports.
- **Covers:** matching runs locally. The catalog host receives the catalog and
  image requests, not your library contents.
- **Logs:** 14 daily files under the local application-data folder. Delete them
  at any time.

Contact: **contact@purelogiccode.com**. Security reports use private reporting
per [SECURITY.md](https://github.com/purelogiccode/XGrid/blob/master/SECURITY.md).

Full page: [Privacy and data](https://github.com/purelogiccode/XGrid/blob/master/docs/legal/privacy.md).
