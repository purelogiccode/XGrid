#!/usr/bin/env python3
"""Report upstream xemu and Meson subproject updates.

The XGrid tree carries xemu as normal tracked source. This script compares the
reviewed upstream commit and every git-backed ``subprojects/*.wrap`` pin with
the upstream repositories. It prints a Markdown report and, when ``--publish``
is passed on GitHub Actions, creates or updates one issue.

No third-party Python packages are required.
"""
from __future__ import annotations

import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
XEMU = ROOT / "xemu"
UPSTREAM = "xemu-project/xemu"
ISSUE_TITLE = "[xemu-updates] Upstream xemu and dependency-lock report"
USER_AGENT = "XGrid-upstream-report"


def github_get(path: str) -> dict | list | None:
    token = os.environ.get("GITHUB_TOKEN", "")
    headers = {"Accept": "application/vnd.github+json", "User-Agent": USER_AGENT}
    if token:
        headers["Authorization"] = "Bearer " + token
    request = urllib.request.Request("https://api.github.com" + path, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, ValueError):
        return None


def gitlab_get(host: str, path: str) -> dict | list | None:
    project = urllib.parse.quote(path, safe="")
    url = f"https://{host}/api/v4/projects/{project}/repository/commits?per_page=1"
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, ValueError):
        return None


def read_wraps() -> list[dict[str, str]]:
    wraps = []
    for path in sorted((XEMU / "subprojects").glob("*.wrap")):
        text = path.read_text(encoding="utf-8")
        url_match = re.search(r"^\s*url\s*=\s*(\S+)\s*$", text, re.MULTILINE)
        revision_match = re.search(r"^\s*revision\s*=\s*([0-9a-fA-F]{7,40})\s*$",
                                   text, re.MULTILINE)
        if not url_match or not revision_match:
            continue
        wraps.append({
            "wrap": path.name,
            "url": url_match.group(1).rstrip("/"),
            "revision": revision_match.group(1),
        })
    return wraps


def normalize_repo(url: str) -> tuple[str, str] | None:
    text = url
    if text.startswith("git@"):
        text = text[4:].replace(":", "/", 1)
    text = re.sub(r"^https?://", "", text)
    text = text.removesuffix(".git")
    parts = text.split("/")
    if len(parts) < 3:
        return None
    host = parts[0]
    path = "/".join(parts[1:])
    if host == "github.com":
        return ("github", path)
    if host in ("gitlab.com", "gitlab.freedesktop.org"):
        return ("gitlab", host + "/" + path)
    return None


def latest_commit(kind: str, repo: str) -> dict[str, str] | None:
    if kind == "github":
        data = github_get(f"/repos/{repo}/commits?per_page=1")
        if isinstance(data, list) and data:
            item = data[0]
            return {
                "sha": item.get("sha", ""),
                "date": (item.get("commit", {}).get("committer", {}) or {}).get("date", ""),
                "subject": (item.get("commit", {}).get("message", "").splitlines() or [""])[0],
            }
        return None

    host, path = repo.split("/", 1)
    data = gitlab_get(host, path)
    if isinstance(data, list) and data:
        item = data[0]
        return {
            "sha": item.get("id", ""),
            "date": item.get("committed_date", ""),
            "subject": (item.get("title", "") or "").splitlines()[0],
        }
    return None


def latest_tag(kind: str, repo: str) -> str | None:
    if kind == "github":
        data = github_get(f"/repos/{repo}/releases/latest")
        if isinstance(data, dict) and data.get("tag_name"):
            return data["tag_name"]
        tags = github_get(f"/repos/{repo}/tags?per_page=1")
        if isinstance(tags, list) and tags:
            return tags[0].get("name")
        return None

    host, path = repo.split("/", 1)
    project = urllib.parse.quote(path, safe="")
    request = urllib.request.Request(
        f"https://{host}/api/v4/projects/{project}/repository/tags?per_page=1",
        headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            tags = json.load(response)
        return tags[0].get("name") if tags else None
    except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError, ValueError):
        return None


def upstream_report() -> tuple[list[str], bool]:
    version_path = XEMU / "XEMU_VERSION"
    commit_path = XEMU / "XEMU_COMMIT"
    reviewed_path = XEMU / "XEMU_REVIEWED_COMMIT"
    current_version = version_path.read_text(encoding="utf-8").strip()
    current_commit = commit_path.read_text(encoding="utf-8").strip()
    reviewed_commit = (
        reviewed_path.read_text(encoding="utf-8").strip()
        if reviewed_path.exists()
        else current_commit
    )
    lines = [f"Lineage base: **{current_version}** (`{current_commit[:12]}`)"]
    if reviewed_commit != current_commit:
        lines.append(f"Reviewed through: `{reviewed_commit[:12]}`")
    updated = False

    release = github_get(f"/repos/{UPSTREAM}/releases/latest")
    tag = None
    if isinstance(release, dict):
        tag = release.get("tag_name")
    if not tag:
        tags = github_get(f"/repos/{UPSTREAM}/tags?per_page=1")
        if isinstance(tags, list) and tags:
            tag = tags[0].get("name")
    if not tag:
        lines.append("Latest release: could not query GitHub.")
        return lines, False

    tag_commit = github_get(f"/repos/{UPSTREAM}/commits/{urllib.parse.quote(tag)}")
    latest_commit = tag_commit.get("sha", "") if isinstance(tag_commit, dict) else ""
    lines.append(f"Latest upstream: **{tag}** (`{latest_commit[:12]}`)")
    if latest_commit and latest_commit != reviewed_commit:
        updated = True
        compare = github_get(
            f"/repos/{UPSTREAM}/compare/{reviewed_commit}...{latest_commit}")
        if isinstance(compare, dict):
            lines.append(f"Commits since reviewed: **{compare.get('total_commits', '?')}**; "
                         f"changed files: **{len(compare.get('files', []))}**.")
            commits = compare.get("commits", [])
            for item in commits[-25:]:
                sha = item.get("sha", "")[:12]
                message = (item.get("commit", {}).get("message", "").splitlines() or [""])[0]
                date = (item.get("commit", {}).get("committer", {}) or {}).get("date", "")[:10]
                lines.append(f"- `{sha}` {date} {message}")
            if len(commits) > 25:
                lines.append(f"- ... and {len(commits) - 25} more")
        else:
            lines.append("Could not compare the commits on GitHub.")
    return lines, updated


def wrap_report() -> tuple[list[str], bool]:
    lines = ["| Wrap | Pinned dependency lock | Latest | State |",
             "| --- | --- | --- | --- |"]
    updated = False
    for item in read_wraps():
        target = normalize_repo(item["url"])
        if target is None:
            continue
        kind, repo = target
        commit = latest_commit(kind, repo)
        tag = latest_tag(kind, repo)
        if commit is None:
            latest = "query failed"
            state = "check failed"
        else:
            latest_sha = commit["sha"]
            if latest_sha.startswith(item["revision"]) or item["revision"].startswith(latest_sha):
                latest = "pinned"
                state = "up to date"
            else:
                updated = True
                latest = commit["subject"][:80] or commit["sha"][:12]
                state = "update available"
                if tag:
                    latest = f"{tag}: {latest}"
                latest += f" (`{latest_sha[:12]}`)"
        lines.append(
            f"| `{item['wrap']}` | `{item['revision'][:12]}` | {latest} | {state} |")
    if len(lines) == 2:
        lines.append("| (none) | | | |")
    return lines, updated


def publish(body: str) -> None:
    repo = os.environ.get("GITHUB_REPOSITORY", "")
    token = os.environ.get("GITHUB_TOKEN", "")
    if not repo or not token:
        print("Not publishing: GITHUB_REPOSITORY and GITHUB_TOKEN are required.")
        return

    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": "Bearer " + token,
        "User-Agent": USER_AGENT,
        "Content-Type": "application/json",
    }
    url = f"https://api.github.com/repos/{repo}"
    request = urllib.request.Request(
        url + "/issues?state=open&per_page=100", headers=headers)
    with urllib.request.urlopen(request, timeout=30) as response:
        issues = json.load(response)
    existing = next(
        (issue for issue in issues if issue.get("title", "").startswith("[xemu-updates]")),
        None)
    payload = json.dumps({
        "title": ISSUE_TITLE,
        "body": body,
    }).encode("utf-8")
    if existing:
        request = urllib.request.Request(
            url + f"/issues/{existing['number']}", data=payload, headers=headers,
            method="PATCH")
        action = "updated"
    else:
        request = urllib.request.Request(
            url + "/issues", data=payload, headers=headers, method="POST")
        action = "created"
    with urllib.request.urlopen(request, timeout=30) as response:
        issue = json.load(response)
    print(f"Issue {action}: {issue.get('html_url', '')}")


def main() -> int:
    upstream_lines, upstream_updated = upstream_report()
    wrap_lines, wrap_updated = wrap_report()
    lines = [
        "# xemu and subproject update report",
        "",
        f"Generated {datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M UTC')}.",
        "",
        "## Upstream xemu",
        *upstream_lines,
        "",
        "## Meson subprojects",
        "",
        "The `revision=` values below are **Meson dependency locks for third-party "
        "libraries**. They are not commits in XGrid, are not stored in this Git "
        "repository, and are not unpushed work. Meson downloads those upstream "
        "commits while configuring the build.",
        "",
        *wrap_lines,
        "",
        "Apply upstream changes selectively; this report only notifies. "
        "After reviewing a release, update `xemu/XEMU_REVIEWED_COMMIT` to the "
        "reviewed upstream commit. Update `XEMU_VERSION` and `XEMU_COMMIT` only "
        "for a deliberate full lineage rebase, and refresh the relevant `.wrap` "
        "revision when its dependency is adopted. See "
        "`docs/development/xemu-upgrade.md` for the runbook.",
    ]
    body = "\n".join(lines)
    print(body)
    if "--publish" in sys.argv and (upstream_updated or wrap_updated):
        publish(body)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
