#!/usr/bin/env python3
"""Generate a CycloneDX SBOM for the native XGrid core.

The source inventory comes from ``xemu/dependencies.json`` and the Windows
build-host lock (``xemu/msys2-packages.lock``). When a package manifest is
passed, every shipped ``core/xemu`` DLL is added as a file component with its
recorded SHA-256.

The output is deterministic for the same version, commit and inputs: the serial
number is a UUIDv5 and no timestamp is written.

Usage:
    python .github/scripts/native_sbom.py --output native-sbom.cdx.json
    python .github/scripts/native_sbom.py \
        --package-manifest dist/package-manifest.json \
        --output dist/native-sbom.cdx.json
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import urllib.parse
import uuid
import xml.etree.ElementTree as ElementTree
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
XEMU = ROOT / "xemu"
MANIFEST = XEMU / "dependencies.json"
HOST_LOCK = XEMU / "msys2-packages.lock"
BUILD_PROPS = ROOT / "Directory.Build.props"
SERIAL_NAMESPACE = uuid.UUID("6f1e6d2a-4f4f-5d1b-9c7e-8f0a1b2c3d4e")


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def repo_version() -> str:
    if BUILD_PROPS.exists():
        try:
            tree = ElementTree.parse(BUILD_PROPS)
            for element in tree.iter():
                tag = element.tag.rsplit("}", 1)[-1]
                if tag == "Version" and element.text:
                    return element.text.strip()
        except ElementTree.ParseError:
            pass
    return "0.0.0-dev"


def repo_commit() -> str:
    try:
        result = subprocess.run(
            ["git", "-C", str(ROOT), "rev-parse", "HEAD"],
            check=True, capture_output=True, text=True)
        return result.stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        commit_path = XEMU / "XEMU_COMMIT"
        return commit_path.read_text(encoding="utf-8").strip() if commit_path.exists() else ""


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
        # purl has no portable gitlab type; keep the group/project path in a
        # generic purl instead of emitting a PURL that scanners reject.
        return ("generic", path)
    return None


def component_ref(component: dict) -> str:
    return component.get("pin") or component.get("version") or ""


def purl(component: dict) -> str:
    target = normalize_repo(component.get("upstream", ""))
    reference = component_ref(component)
    if target is not None:
        kind, path = target
        base = f"pkg:{kind}/{path}"
        if reference:
            return f"{base}@{urllib.parse.quote(reference, safe='.-_+')}"
        return base
    base = f"pkg:generic/{urllib.parse.quote(component.get('id', 'unknown'), safe='.-_+')}"
    if reference:
        return f"{base}@{urllib.parse.quote(reference, safe='.-_+')}"
    return base


def manifest_components(manifest: dict) -> list[dict]:
    components = []
    for entry in manifest.get("components", []):
        version = component_ref(entry)
        item = {
            "type": "library",
            "bom-ref": f"xemu:{entry.get('id', 'unknown')}",
            "name": entry.get("id", "unknown"),
            "purl": purl(entry),
            "properties": [
                {"name": "xgrid:kind", "value": entry.get("kind", "")},
                {"name": "xgrid:track", "value": entry.get("track", "")},
                {"name": "xgrid:location", "value": entry.get("location", "")},
            ],
        }
        if version:
            item["version"] = version
        if entry.get("license"):
            item["licenses"] = [{"license": {"name": entry["license"]}}]
        if entry.get("upstream"):
            item["externalReferences"] = [{"type": "vcs", "url": entry["upstream"]}]
        components.append(item)
    return components


def host_components(lock: Path) -> list[dict]:
    components = []
    if not lock.exists():
        return components
    for raw in lock.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split()
        if len(parts) < 2:
            continue
        name, version = parts[0], parts[1]
        components.append({
            "type": "library",
            "bom-ref": f"host:{name}",
            "name": name,
            "version": version,
            "purl": f"pkg:generic/{urllib.parse.quote(name, safe='.-_+')}"
                    f"@{urllib.parse.quote(version, safe='.-_+')}",
            "properties": [{"name": "xgrid:source", "value": "msys2"}],
        })
    return components


def package_components(package_manifest: Path) -> list[dict]:
    if not package_manifest.exists():
        raise SystemExit(f"missing package manifest: {package_manifest}")
    data = read_json(package_manifest)
    core = data.get("core") or {}
    components = []
    for row in core.get("libraries", []):
        name = row.get("name", "")
        if not name:
            continue
        item = {
            "type": "file",
            "bom-ref": f"file:{row.get('rid', '')}/{name}",
            "name": f"core/xemu/{name}",
            "properties": [
                {"name": "xgrid:rid", "value": row.get("rid", "")},
                {"name": "xgrid:source", "value": "windows-core"},
            ],
        }
        if row.get("version"):
            item["version"] = row["version"]
        if row.get("sha256"):
            item["hashes"] = [{"alg": "SHA-256", "content": row["sha256"]}]
        components.append(item)
    return components


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default="native-sbom.cdx.json")
    parser.add_argument("--package-manifest", default="")
    parser.add_argument("--version", default="")
    parser.add_argument("--commit", default="")
    args = parser.parse_args()

    manifest = read_json(MANIFEST)
    version = args.version or repo_version()
    commit = args.commit or repo_commit()

    package_manifest = Path(args.package_manifest) if args.package_manifest else None
    if package_manifest and package_manifest.exists():
        version = read_json(package_manifest).get("version", version)

    components = manifest_components(manifest)
    components.extend(host_components(HOST_LOCK))
    if package_manifest:
        components.extend(package_components(package_manifest))

    product_ref = f"pkg:generic/xgrid@{urllib.parse.quote(version, safe='.-_+')}"
    bom = {
        "bomFormat": "CycloneDX",
        "specVersion": "1.5",
        "serialNumber": f"urn:uuid:{uuid.uuid5(SERIAL_NAMESPACE, f'xgrid/{version}/{commit}')}",
        "version": 1,
        "metadata": {
            "tools": {
                "components": [{
                    "type": "application",
                    "name": "xgrid-native-sbom",
                    "version": "1",
                }],
            },
            "component": {
                "type": "application",
                "bom-ref": product_ref,
                "name": "XGrid",
                "version": version,
                "purl": product_ref,
                "properties": [
                    {"name": "xgrid:commit", "value": commit},
                    {"name": "xgrid:core.version", "value": (XEMU / "XEMU_VERSION").read_text(
                        encoding="utf-8").strip() if (XEMU / "XEMU_VERSION").exists() else ""},
                    {"name": "xgrid:core.commit", "value": (XEMU / "XEMU_COMMIT").read_text(
                        encoding="utf-8").strip() if (XEMU / "XEMU_COMMIT").exists() else ""},
                ],
            },
        },
        "components": components,
        "dependencies": [{
            "ref": product_ref,
            "dependsOn": [item["bom-ref"] for item in components],
        }],
    }

    output = Path(args.output)
    if not output.is_absolute():
        output = ROOT / output
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(bom, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Wrote {output} ({len(components)} components, version {version})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
