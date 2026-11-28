#!/usr/bin/env python3
"""Report upstream xemu, native dependency and host-package updates.

The XGrid tree carries xemu as normal tracked source. This script compares the
reviewed upstream commit, the native dependency manifest
(``xemu/dependencies.json``) and every Meson ``subprojects/*.wrap`` pin with the
upstream repositories. It also prints the Windows build-host package lock.

It prints a Markdown report and, when ``--publish`` is passed on GitHub Actions,
creates or updates one issue. The issue is closed when everything is current.
``--check`` validates the manifest against the tree without network access.

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
MANIFEST = XEMU / "dependencies.json"
HOST_LOCK = XEMU / "msys2-packages.lock"
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


def read_manifest() -> dict:
    if not MANIFEST.exists():
        raise SystemExit(f"missing dependency manifest: {MANIFEST}")
    try:
        data = json.loads(MANIFEST.read_text(encoding="utf-8"))
    except ValueError as error:
        raise SystemExit(f"invalid dependency manifest {MANIFEST}: {error}") from error
    if data.get("schemaVersion") != 1:
        raise SystemExit(f"unsupported dependency manifest schema in {MANIFEST}")
    if not isinstance(data.get("components"), list):
        raise SystemExit(f"dependency manifest {MANIFEST} has no components list")
    return data


def wrap_file_version(entry: dict[str, str]) -> str:
    for key in ("directory", "source_filename", "source_url", "wrapdb_version"):
        match = re.search(r"(\d+(?:\.\d+)+)", entry.get(key, ""))
        if match:
            return match.group(1)
    return ""


def read_wraps() -> list[dict[str, str]]:
    wraps = []
    for path in sorted((XEMU / "subprojects").glob("*.wrap")):
        text = path.read_text(encoding="utf-8")
        entry = {
            "wrap": path.name,
            "path": f"xemu/subprojects/{path.name}",
        }
        if re.search(r"^\s*\[wrap-git\]\s*$", text, re.MULTILINE):
            url_match = re.search(r"^\s*url\s*=\s*(\S+)\s*$", text, re.MULTILINE)
            revision_match = re.search(r"^\s*revision\s*=\s*([0-9a-fA-F]{7,40})\s*$",
                                       text, re.MULTILINE)
            if not url_match or not revision_match:
                continue
            entry.update({
                "kind": "wrap-git",
                "url": url_match.group(1).rstrip("/"),
                "revision": revision_match.group(1),
            })
        elif re.search(r"^\s*\[wrap-file\]\s*$", text, re.MULTILINE):
            for key in ("directory", "source_filename", "source_url",
                        "source_hash", "wrapdb_version"):
                match = re.search(rf"^\s*{key}\s*=\s*(.+?)\s*$", text, re.MULTILINE)
                entry[key] = match.group(1) if match else ""
            entry.update({
                "kind": "wrap-file",
                "url": entry["source_url"].rstrip("/"),
                "revision": "",
                "version": wrap_file_version(entry),
            })
        else:
            continue
        wraps.append(entry)
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


def version_key(text: str) -> tuple[int, ...]:
    return tuple(int(part) for part in re.findall(r"\d+", text))


def compare_versions(left: str, right: str) -> int:
    left_key = version_key(left)
    right_key = version_key(right)
    width = max(len(left_key), len(right_key))
    left_key += (0,) * (width - len(left_key))
    right_key += (0,) * (width - len(right_key))
    return (left_key > right_key) - (left_key < right_key)


def same_pin(left: str, right: str) -> bool:
    if not left or not right:
        return False
    return left.startswith(right) or right.startswith(left)


def manifest_drift(component: dict, wraps: dict[str, dict[str, str]]) -> str:
    location = component.get("location", "")
    wrap = wraps.get(location)
    if wrap is None:
        return ""
    if wrap["kind"] != component.get("kind"):
        return "manifest kind does not match the wrap"
    if wrap["kind"] == "wrap-git":
        if not same_pin(component.get("pin", ""), wrap.get("revision", "")):
            return "manifest pin does not match the wrap revision"
    else:
        if compare_versions(component.get("version", ""), wrap.get("version", "")) != 0:
            return "manifest version does not match the wrap file"
        if (component.get("sourceHash") and
                component["sourceHash"] != wrap.get("source_hash", "")):
            return "manifest sourceHash does not match the wrap file"
    return ""


def downloaded_drift(component: dict) -> str:
    if component.get("kind") != "downloaded":
        return ""
    path = ROOT / component.get("location", "")
    if not path.exists():
        return "downloaded subproject file is missing"
    text = path.read_text(encoding="utf-8")
    match = re.search(r"project\([^)]*?version\s*:\s*'([^']+)'", text, re.DOTALL)
    if not match:
        return "cannot read the subproject version from its meson.build"
    if compare_versions(match.group(1), component.get("version", "")) != 0:
        return f"manifest version {component.get('version')} does not match meson.build {match.group(1)}"
    return ""


def upstream_report() -> tuple[list[str], bool, int]:
    version_path = XEMU / "XEMU_VERSION"
    commit_path = XEMU / "XEMU_COMMIT"
    reviewed_path = XEMU / "XEMU_REVIEWED_COMMIT"
    if not version_path.exists() or not commit_path.exists():
        return ["Could not read `xemu/XEMU_VERSION` or `xemu/XEMU_COMMIT`."], False, 1
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
        return lines, False, 1

    tag_commit = github_get(f"/repos/{UPSTREAM}/commits/{urllib.parse.quote(tag)}")
    latest_commit_sha = tag_commit.get("sha", "") if isinstance(tag_commit, dict) else ""
    if not latest_commit_sha:
        lines.append(f"Latest release: **{tag}**; could not resolve its commit on GitHub.")
        return lines, False, 1

    lines.append(f"Latest upstream: **{tag}** (`{latest_commit_sha[:12]}`)")
    if latest_commit_sha == reviewed_commit:
        return lines, False, 0

    compare = github_get(
        f"/repos/{UPSTREAM}/compare/{reviewed_commit}...{latest_commit_sha}")
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
    return lines, True, 0


def component_report(manifest: dict) -> tuple[list[str], int, int, int, list[str]]:
    lines = ["| Component | Kind | Pinned | Latest | State |",
             "| --- | --- | --- | --- | --- |"]
    wraps = {entry["path"]: entry for entry in read_wraps()}
    declared: set[str] = set()
    updates = 0
    moved = 0
    unknown = 0
    problems: list[str] = []

    for component in manifest.get("components", []):
        cid = component.get("id", "?")
        kind = component.get("kind", "?")
        track = component.get("track", "commits")
        location = component.get("location", "")
        declared.add(location)
        drift = manifest_drift(component, wraps) or downloaded_drift(component)
        version = component.get("version", "")
        pin = component.get("pin", "")

        if drift:
            problems.append(f"{cid}: {drift}")
            latest = "-"
            state = "manifest drift"
        elif track == "none":
            latest = "-"
            state = "not checked"
        else:
            target = normalize_repo(component.get("upstream", ""))
            if target is None:
                unknown += 1
                latest = "unsupported upstream"
                state = "query failed"
            elif track == "tags":
                tag = latest_tag(*target)
                if tag is None:
                    unknown += 1
                    latest = "-"
                    state = "query failed"
                else:
                    latest = tag
                    if compare_versions(tag, version) > 0:
                        updates += 1
                        state = "update available"
                    else:
                        state = "current"
            else:
                commit = latest_commit(*target)
                if commit is None:
                    unknown += 1
                    latest = "-"
                    state = "query failed"
                else:
                    latest = f"`{commit['sha'][:12]}`"
                    if same_pin(pin, commit["sha"]):
                        state = "current"
                    else:
                        moved += 1
                        state = "upstream moved"

        pinned = version if track == "tags" else (pin[:12] if pin else (version or "-"))
        lines.append(f"| `{cid}` | {kind} | `{pinned}` | {latest} | {state} |")

    for path, wrap in wraps.items():
        if path in declared:
            continue
        problems.append(f"{path} is not listed in xemu/dependencies.json")
        lines.append(f"| `{wrap['wrap']}` | {wrap['kind']} | - | - | unlisted in manifest |")

    if len(lines) == 2:
        lines.append("| (none) | | | | |")
    return lines, updates, moved, unknown, problems


def host_report() -> list[str]:
    if not HOST_LOCK.exists():
        return ["Host lock: **missing** (`xemu/msys2-packages.lock`). "
                "Run `pwsh -File scripts/msys2-lock.ps1 -Write` on the build host."]
    rows: list[tuple[str, str]] = []
    generated = ""
    for raw in HOST_LOCK.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line:
            continue
        if line.startswith("#"):
            if "generated" in line.lower():
                generated = line.lstrip("#").strip()
            continue
        parts = line.split()
        if len(parts) >= 2:
            rows.append((parts[0], parts[1]))

    header = f"Host lock: **{len(rows)} packages**"
    if generated:
        header += f" ({generated})"
    lines = [header, "", "| Package | Version |", "| --- | --- |"]
    for name, version in rows:
        lines.append(f"| `{name}` | `{version}` |")
    if not rows:
        lines.append("| (none) | |")
    return lines


def manifest_check() -> tuple[list[str], str]:
    manifest = read_manifest()
    components = manifest.get("components", [])
    wraps = {entry["path"]: entry for entry in read_wraps()}
    problems: list[str] = []
    seen_ids: set[str] = set()
    seen_locations: set[str] = set()

    for component in components:
        cid = component.get("id", "")
        location = component.get("location", "")
        if not cid:
            problems.append("a component has no id")
            continue
        if cid in seen_ids:
            problems.append(f"{cid}: duplicate component id")
        seen_ids.add(cid)
        if not location:
            problems.append(f"{cid}: missing location")
            continue
        if location in seen_locations:
            problems.append(f"{cid}: duplicate location {location}")
        seen_locations.add(location)
        if not (ROOT / location).exists():
            problems.append(f"{cid}: location does not exist: {location}")
        if component.get("kind") in ("wrap-git", "wrap-file"):
            if location not in wraps:
                problems.append(f"{cid}: {location} is not a wrap in the tree")
            else:
                drift = manifest_drift(component, wraps)
                if drift:
                    problems.append(f"{cid}: {drift}")
        if component.get("kind") == "downloaded":
            drift = downloaded_drift(component)
            if drift:
                problems.append(f"{cid}: {drift}")

    for path in wraps:
        if path not in seen_locations:
            problems.append(f"{path} is not listed in xemu/dependencies.json")

    thirdparty = XEMU / "xgrid-core" / "thirdparty"
    if thirdparty.exists():
        for child in sorted(entry for entry in thirdparty.iterdir() if entry.is_dir()):
            relative = child.relative_to(ROOT).as_posix()
            if not any(location == relative or location.startswith(relative + "/")
                       for location in seen_locations):
                problems.append(f"{relative} is not covered by xemu/dependencies.json")

    for name in ("dsp56300", "libvduse", "libvhost-user"):
        path = XEMU / "subprojects" / name
        if path.exists():
            relative = path.relative_to(ROOT).as_posix()
            if not any(location == relative or location.startswith(relative + "/")
                       for location in seen_locations):
                problems.append(f"{relative} is not covered by xemu/dependencies.json")

    lock = manifest.get("host", {}).get("lockFile", "")
    if not lock:
        problems.append("manifest host.lockFile is missing")
    elif not (ROOT / lock).exists():
        problems.append(f"host lock file is missing: {lock}")

    summary = (f"Dependency manifest OK: {len(components)} components, "
               f"{len(wraps)} wraps, {len(seen_locations)} locations.")
    return problems, summary


def github_request(method: str, path: str, payload: dict | None = None) -> dict:
    token = os.environ.get("GITHUB_TOKEN", "")
    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": "Bearer " + token,
        "User-Agent": USER_AGENT,
    }
    data = None
    if payload is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request("https://api.github.com" + path, data=data,
                                     headers=headers, method=method)
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def publish(body: str, close: bool) -> None:
    repo = os.environ.get("GITHUB_REPOSITORY", "")
    token = os.environ.get("GITHUB_TOKEN", "")
    if not repo or not token:
        print("Not publishing: GITHUB_REPOSITORY and GITHUB_TOKEN are required.")
        return

    issues = github_request("GET", f"/repos/{repo}/issues?state=open&per_page=100")
    existing = next(
        (issue for issue in issues if issue.get("title", "").startswith("[xemu-updates]")),
        None,
    )
    if existing is None and close:
        print("Nothing to publish: no open report and nothing to update.")
        return

    payload: dict = {"title": ISSUE_TITLE, "body": body}
    if close:
        payload["state"] = "closed"
        payload["state_reason"] = "completed"
    if existing:
        issue = github_request("PATCH", f"/repos/{repo}/issues/{existing['number']}", payload)
        print(f"Issue {'closed' if close else 'updated'}: {issue.get('html_url', '')}")
    else:
        issue = github_request("POST", f"/repos/{repo}/issues", payload)
        print(f"Issue created: {issue.get('html_url', '')}")


def main() -> int:
    if "--check" in sys.argv:
        problems, summary = manifest_check()
        for problem in problems:
            print(f"ERROR: {problem}")
        print(summary)
        return 1 if problems else 0

    manifest = read_manifest()
    upstream_lines, upstream_updated, upstream_unknown = upstream_report()
    (component_lines, component_updated, component_moved,
     component_unknown, problems) = component_report(manifest)
    host_lines = host_report()

    updates = int(upstream_updated) + component_updated
    unknown = upstream_unknown + component_unknown
    clean = updates == 0 and unknown == 0 and not problems
    if updates:
        status = "updates available."
    elif problems:
        status = "manifest problems need attention."
    elif unknown:
        status = "some upstream queries failed."
    elif component_moved:
        status = (f"clean - release pins current; {component_moved} commit pins "
                  "moved (informational).")
    else:
        status = "clean - nothing to update."

    lines = [
        "# xemu and native dependency update report",
        "",
        f"Generated {datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M UTC')}.",
        "",
        f"**Status:** {status}",
        "",
        "## Upstream xemu",
        "",
        *upstream_lines,
        "",
        "## Native dependencies",
        "",
        "The versions below come from `xemu/dependencies.json`; every Meson wrap "
        "must have an entry there. `tags` rows compare the latest release tag "
        "with the pinned version, `commits` rows compare the latest upstream "
        "commit with the pin, and `not checked` rows follow the xemu/QEMU "
        "lineage. An `upstream moved` row is informational: it does not keep "
        "this issue open by itself.",
        "",
        *component_lines,
        "",
        "## Windows host packages",
        "",
        *host_lines,
        "",
        "Host package updates are not queried from the network. Run `pacman -Qu` "
        "on the Windows build host and refresh the lock after reviewing the "
        "package changes.",
        "",
        "Apply upstream changes selectively; this report only notifies. "
        "After reviewing a release, update `xemu/XEMU_REVIEWED_COMMIT` to the "
        "reviewed upstream commit. Update `XEMU_VERSION` and `XEMU_COMMIT` only "
        "for a deliberate full lineage rebase, refresh the relevant `.wrap` "
        "revision when its dependency is adopted, and keep `xemu/dependencies.json` "
        "and `xemu/msys2-packages.lock` in sync. See "
        "`docs/development/xemu-upgrade.md` and `docs/development/dependencies.md`.",
    ]
    if problems:
        lines.extend(["", "## Manifest problems", ""])
        lines.extend(f"- {problem}" for problem in problems)

    body = "\n".join(lines)
    print(body)
    if "--publish" in sys.argv:
        publish(body, close=clean)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
