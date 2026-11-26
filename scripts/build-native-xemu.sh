#!/usr/bin/env bash
# Build the maintained xemu core from the vendored, tracked tree (xemu/) on
# Linux. The tree at xemu/ is the source of truth: xemu 0.8.139 plus the XGrid
# additions in xemu/xgrid-core and the patch record in xemu/xemu-patches.
#
# Output: xemu/dist/xemu and xemu/dist/xgrid-core.json. The shell looks in
# xemu/dist/ automatically, so a source checkout is ready to run the shell
# without setting XGRID_XEMU_CORE.
#
# Usage: bash scripts/build-native-xemu.sh [-j N] [--clean]
#
# Prerequisites are listed in docs/development/building.md. In short: a C/C++
# toolchain, make, ninja, pkg-config, meson >= 1.5, Python 3 with PyYAML, and
# the development headers for GLib, zlib, zstd, lz4, libepoxy, libusb, libpcap,
# libsamplerate, GMP and SDL3 (3.4.x).
set -euo pipefail

jobs=$(nproc 2>/dev/null || echo 12)
clean=0

usage() {
    sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'
}

while [ $# -gt 0 ]; do
    case "$1" in
        -j)
            [ $# -ge 2 ] || { echo "-j needs a job count" >&2; exit 2; }
            jobs=$2
            shift 2
            ;;
        -j*)
            jobs=${1#-j}
            shift
            ;;
        --clean)
            clean=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

case "$jobs" in
    ''|*[!0-9]*) echo "invalid job count: $jobs" >&2; exit 2 ;;
esac

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
tree=$root/xemu
if [ ! -d "$tree" ]; then
    echo "missing $tree; the vendored tree is part of this repository" >&2
    exit 1
fi

for tool in cc c++ make ninja pkg-config python3; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "missing required tool: $tool" >&2
        exit 1
    fi
done

if ! command -v meson >/dev/null 2>&1; then
    echo "missing meson >= 1.5; install it with 'pip install meson>=1.5'" >&2
    exit 1
fi

meson_version=$(meson --version)
if ! python3 - "$meson_version" <<'PY'
import sys

parts = sys.argv[1].split(".")
sys.exit(0 if (int(parts[0]), int(parts[1])) >= (1, 5) else 1)
PY
then
    echo "meson >= 1.5 is required (found $meson_version)" >&2
    exit 1
fi

for module in yaml tomli; do
    if ! python3 -c "import $module" >/dev/null 2>&1; then
        if [ "$module" = tomli ] && python3 -c 'import tomllib' >/dev/null 2>&1; then
            continue
        fi
        echo "missing Python module $module; install it with 'pip install $module'" >&2
        exit 1
    fi
done

for package in sdl3 glib-2.0 zlib libzstd liblz4 epoxy libusb-1.0 libpcap samplerate gmp; do
    if ! pkg-config --exists "$package"; then
        echo "missing pkg-config dependency: $package" >&2
        exit 1
    fi
done

cd "$tree"
if [ "$clean" -eq 1 ]; then
    rm -rf build dist
fi

./build.sh -j"$jobs" \
    --disable-werror --disable-docs --disable-guest-agent \
    --disable-vnc --disable-spice --disable-gtk --disable-sdl-image \
    --disable-libssh --disable-curses --disable-brlapi \
    -Dzstd=enabled --enable-tools --disable-curl

# The media checks use the QEMU tools from the object tree; they are not part
# of the packaged core.
ninja -C build qemu-img qemu-io

# Tell the shell which renderers and audio backends this build can start. The
# Windows build script writes the same manifest; `-audio-drivers` exits before
# SDL/GL, so it is safe on a headless build host.
version=$(head -n 1 XEMU_VERSION | tr -d '\r')
if grep -q '^#define CONFIG_VULKAN\b' build/config-host.h; then
    renderers='["OPENGL", "VULKAN", "NULL"]'
else
    renderers='["OPENGL", "NULL"]'
fi
audio_drivers=$(./dist/xemu -audio-drivers 2>/dev/null |
    awk 'found && NF { print } /^Available audio drivers:/ { found = 1 }')
if [ -z "$audio_drivers" ]; then
    audio_drivers=$'none\nwav'
fi
audio_json=$(printf '%s\n' "$audio_drivers" | sed 's/.*/"&"/' | paste -sd, -)
printf '{\n  "core": "xemu",\n  "version": "%s",\n  "renderers": %s,\n  "audioDrivers": [%s]\n}\n' \
    "$version" "$renderers" "$audio_json" > dist/xgrid-core.json

size=$(wc -c < dist/xemu)
printf 'core: %s (%.1f MB, renderers %s, audio %s)\n' \
    "$tree/dist/xemu" "$(awk "BEGIN { print $size / 1048576 }")" "$renderers" \
    "$(printf '%s' "$audio_drivers" | tr '\n' ' ')"
printf 'run: %s --version\n' "$tree/dist/xemu"
