# XGrid additions to the xemu core.
#
# Included from the xemu tree with `subdir('../xgrid-core')` after the block
# subsystem and the zlib/zstd dependencies exist. Everything here is ours and is
# tracked in this repository; the xemu baseline stays untouched except for the
# patch set in xemu/xemu-patches/.

libchdr_sources = files(
  'thirdparty/libchdr/src/libchdr_bitstream.c',
  'thirdparty/libchdr/src/libchdr_cdrom.c',
  'thirdparty/libchdr/src/libchdr_chd.c',
  'thirdparty/libchdr/src/libchdr_codec_cdfl.c',
  'thirdparty/libchdr/src/libchdr_codec_cdlz.c',
  'thirdparty/libchdr/src/libchdr_codec_cdzl.c',
  'thirdparty/libchdr/src/libchdr_codec_cdzs.c',
  'thirdparty/libchdr/src/libchdr_codec_flac.c',
  'thirdparty/libchdr/src/libchdr_codec_huff.c',
  'thirdparty/libchdr/src/libchdr_codec_lzma.c',
  'thirdparty/libchdr/src/libchdr_codec_zlib.c',
  'thirdparty/libchdr/src/libchdr_codec_zstd.c',
  'thirdparty/libchdr/src/libchdr_flac.c',
  'thirdparty/libchdr/src/libchdr_huffman.c',
  'thirdparty/libchdr/deps/lzma-25.01/src/LzmaDec.c',
)

libchdr_include = include_directories(
  'thirdparty/libchdr/include',
  'thirdparty/libchdr/src',
  'thirdparty/libchdr/deps/lzma-25.01/include',
)

# zlib and zstd come from the host (the same libraries QEMU already links);
# LZMA is the small vendor decoder shipped with libchdr.
libchdr = static_library('xgrid_libchdr', libchdr_sources,
  include_directories: libchdr_include,
  c_args: ['-DCHDR_SYSTEM_ZLIB', '-DCHDR_SYSTEM_ZSTD'],
)

xgrid_core_dep = declare_dependency(
  link_with: libchdr,
  include_directories: libchdr_include,
  dependencies: [zlib, zstd],
)

block_ss.add(xgrid_core_dep)
block_ss.add(files('block/xgridchd.c'))
block_ss.add(files('block/xgridzar.c'))

# Host display sink (frames to a named shared section; no SDL presentation).
system_ss.add(files('display/xgrid-display.c'))

# Host input source (port 1 pad from the shell).
system_ss.add(files('input/xgrid-input.c'))

# In-process core ABI: the shell loads the core image and calls these.
system_ss.add(files('abi/xgrid-core.c'))
