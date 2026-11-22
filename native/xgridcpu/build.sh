#!/usr/bin/env bash
# Build the XCN0 native CPU core smoke test against the vendored QEMU archives.
# Run from native/xgridcpu inside an MSYSTEM=MINGW64 shell.
set -euo pipefail

QEMU=${QEMU:-../qemu}
BUILD="$(cd "$QEMU/build" && pwd)"
CC=${CC:-gcc}

# QEMU's qapi command stubs live in libqemuutil.a and reference the device
# handlers this CPU-only build does not compile. QEMU's archive is thin, so
# rebuild a normal archive from its members minus the command objects.
UTIL_MIN="$BUILD/libqemuutil-xgc.a"
rm -f "$UTIL_MIN"
(
  cd "$BUILD"
  ar t libqemuutil.a | grep -v -E 'qapi.*commands|stubs_' | \
    xargs -n 100 ar rcs "$UTIL_MIN"
)

$CC -O2 -g -std=gnu11 -o xgc_test.exe test.c xgridcpu.c stubs.c \
  -I. \
  -I"$QEMU" -I"$QEMU/include" \
  -I"$BUILD" -I"$BUILD/target/i386" -I"$BUILD/qapi" -I"$BUILD/trace" \
  -I"$QEMU/target/i386" -I"$QEMU/tcg/i386" \
  -I"$QEMU/host/include/x86_64" -I"$QEMU/host/include/generic" \
  -DCOMPILING_PER_TARGET \
  -DCONFIG_TARGET='"i386-softmmu-config-target.h"' \
  -DCONFIG_DEVICES='"i386-softmmu-config-devices.h"' \
  -DCONFIG_SOFTMMU -D_GNU_SOURCE -D_FILE_OFFSET_BITS=64 \
  $(pkg-config --cflags glib-2.0 pixman-1 zlib) \
  -Wl,--start-group \
  "$BUILD/libqemu-i386-softmmu.a" "$BUILD/libsystem.a" \
  "$UTIL_MIN" "$BUILD/libhwcore.a" "$BUILD/libqom.a" "$BUILD/libio.a" \
  "$BUILD/libcrypto.a" \
  -Wl,--end-group \
  $(pkg-config --libs glib-2.0 pixman-1 zlib) \
  -lshlwapi -lpathcch -lsynchronization -lxxhash -lwinmm \
  -lws2_32 -lbcrypt -lntdll -luserenv
