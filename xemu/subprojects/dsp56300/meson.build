project('dsp56300', version: '0.1.3')

fs = import('fs')

host_cpu = host_machine.cpu_family()
host_os = host_machine.system()

if host_os == 'darwin'
  os_suffix = 'apple-darwin'
elif host_os == 'windows'
  os_suffix = (host_cpu == 'aarch64') ? 'pc-windows-gnullvm' : 'pc-windows-gnu'
elif host_os == 'linux'
  os_suffix = 'unknown-linux-gnu'
else
  error('Unsupported host OS: ' + host_os)
endif
rust_target = host_cpu + '-' + os_suffix

# Release archive hashes for dsp56300 0.1.3. A downloaded archive is verified
# before it is unpacked; delete a mismatched archive and reconfigure to refetch.
archive_hashes = {
  'aarch64-apple-darwin': '1636d6f629a44a3f2384589a85142a5c5ea284d520df456403d34cc8aee5dac7',
  'aarch64-pc-windows-gnullvm': 'bbc2ee95907a543cb23d72e4b3793b8d778a2e290e23b3d421ae761b1966e2a5',
  'aarch64-unknown-linux-gnu': 'f880608456390771720d2c257bdc2e97a79d4da043cbc8822d112f9f01e2003c',
  'x86_64-apple-darwin': '76d973ab01e9097a37acc52f3bfc5c95259dcc3445acc797f4eee9735c9e5252',
  'x86_64-pc-windows-gnu': 'ff031c6daf89f4c2c78a5943920bf483b044e8ec805feaf1efe32c80f991b09c',
  'x86_64-unknown-linux-gnu': '04aff4e070913205a7913d8d8a6076f99150c347d21df584b76b436ed5170fc8',
}
if rust_target not in archive_hashes
  error('No dsp56300 archive hash for ' + rust_target)
endif
expected_hash = archive_hashes[rust_target]

ver = meson.project_version()
prefix = 'dsp56300-@0@-@1@'.format(ver, rust_target)
archive = '@0@.tar.gz'.format(prefix)
url = 'https://github.com/xemu-project/dsp56300/releases/download/v@0@/@1@'.format(ver, archive)
lib = meson.current_build_dir() / prefix / 'lib' / 'libdsp56300_emu_ffi.a'

if not fs.exists(lib)
  src_archive = meson.current_source_dir() / archive
  if not fs.exists(src_archive)
    message('Downloading dsp56300 @0@ for @1@...'.format(ver, rust_target))
    run_command(find_program('curl'), '-fsSL', url, '-o', src_archive, check: true)
  endif
  actual_hash = fs.hash(src_archive, 'sha256')
  if actual_hash != expected_hash
    error('dsp56300 archive checksum mismatch for @0@: expected @1@, got @2@'.format(
      rust_target, expected_hash, actual_hash))
  endif
  # The subproject build directory does not exist on a fresh configure, and GNU
  # tar treats a Windows path's drive letter as a remote host.
  if not fs.exists(meson.current_build_dir())
    fs.mkdir(meson.current_build_dir())
  endif
  tar_args = ['xzf', src_archive, '-C', meson.current_build_dir()]
  if host_os == 'windows'
    tar_args += ['--force-local']
  endif
  run_command('tar', tar_args, check: true)
endif

link_args = []
if host_os == 'linux'
  link_args = ['-lpthread', '-ldl', '-lm']
elif host_os == 'windows'
  link_args = ['-lbcrypt', '-lntdll', '-luserenv', '-lws2_32']
elif host_os == 'darwin'
  link_args = ['-lpthread', '-lm', '-framework', 'Security']
endif

dsp56300_dep = declare_dependency(
  link_args: [lib] + link_args,
  include_directories: include_directories(prefix / 'include'),
)

meson.override_dependency('dsp56300-emu-ffi', dsp56300_dep)
