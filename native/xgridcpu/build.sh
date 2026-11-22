#!/usr/bin/env bash
# Build the XCN0 native CPU core (C smoke test + xgridcpu.dll) against the
# vendored QEMU archives. Run from native/xgridcpu in an MSYSTEM=MINGW64 shell.
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

# The system and target archives are thin/fat and drag the monitor, HMP,
# migration, net, block, chardev, audio and UI objects. The CPU core does not
# need them; rebuild normal archives with only the CPU/memory/TCG members.
make_filtered() {
  local src="$1" dst="$2" pat="$3"
  rm -f "$dst"
  ( cd "$BUILD" && ar t "$src" | grep -v -E "$pat" | \
      xargs -n 100 ar rcs "$dst" )
}
make_filtered libsystem.a libsystem-xgc.a \
  'monitor|hmp|qmp|migration|net_|block|job|chardev|audio_|ui_|disas|replay|semihosting|stats|tpm|backends_|authz|blockdev|io_|crypto_|dump_|qtest'
make_filtered libqemu-i386-softmmu.a libqemu-i386-xgc.a \
  'monitor|hmp|qmp|migration|net_|block|job|chardev|audio_|ui_|disas|replay|semihosting|stats|tpm|backends_|authz|blockdev|io_|crypto_|dump_|xemu|qtest'

COMMON=(
  -O2 -g -std=gnu11
  -I.
  -I"$QEMU" -I"$QEMU/include"
  -I"$BUILD" -I"$BUILD/target/i386" -I"$BUILD/qapi" -I"$BUILD/trace"
  -I"$QEMU/target/i386" -I"$QEMU/tcg/i386"
  -I"$QEMU/host/include/x86_64" -I"$QEMU/host/include/generic"
  -DCOMPILING_PER_TARGET
  -DCONFIG_TARGET='"i386-softmmu-config-target.h"'
  -DCONFIG_DEVICES='"i386-softmmu-config-devices.h"'
  -DCONFIG_SOFTMMU -D_GNU_SOURCE -D_FILE_OFFSET_BITS=64
  $(pkg-config --cflags glib-2.0 pixman-1 zlib)
)

LIBS=(
  -Wl,--start-group
  "$BUILD/libsystem.a.p/hw_core_machine.c.obj"
  "$BUILD/libsystem.a.p/hw_core_null-machine.c.obj"
  "$BUILD/libhwcore.a.p/hw_core_vmstate-if.c.obj"
  "$BUILD/libqemu-i386-softmmu.a.p/target_i386_cpu.c.obj"
  "$BUILD/libqemu-i386-xgc.a" "$BUILD/libsystem-xgc.a"
  "$UTIL_MIN" "$BUILD/libhwcore.a" "$BUILD/libqom.a"
  -Wl,--end-group
  $(pkg-config --libs glib-2.0 pixman-1 zlib)
  -lshlwapi -lpathcch -lsynchronization -lxxhash -lwinmm
  -lws2_32 -lbcrypt -lntdll -luserenv
)

$CC "${COMMON[@]}" -o xgc_test.exe test.c xgridcpu.c stubs.c "${LIBS[@]}"
$CC "${COMMON[@]}" -o xgc_ring_test.exe ring_test.c xgridcpu.c stubs.c "${LIBS[@]}"
$CC "${COMMON[@]}" -shared -o xgridcpu.dll xgridcpu.c stubs.c "${LIBS[@]}"
