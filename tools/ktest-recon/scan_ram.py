"""scan_ram.py - search a 64 MiB physical RAM dump for path/name strings.

Usage: python scan_ram.py <ram.bin> [more.bin ...]

Searches ASCII and UTF-16LE forms of the drive-letter and device-path strings
that decide the kernel-test-suite log gate (120).
"""

import sys

STRINGS = [
    '\\??\\A:', '\\??\\B:', '\\??\\C:', '\\??\\D:', '\\??\\E:', '\\??\\F:',
    '\\??\\G:', '\\??\\CdRom0:',
    '\\Device\\CdRom0', '\\Device\\Harddisk0',
    '\\Device\\Harddisk0\\Partition0', '\\Device\\Harddisk0\\Partition1',
    '\\Device\\Harddisk0\\Partition2', '\\Device\\Harddisk0\\Partition3',
    'kernel_tests.log', 'D:\\kernel_tests.log', 'xboxdash.xbe',
]


def find(data, pat, limit=8):
    out = []
    st = 0
    while True:
        i = data.find(pat, st)
        if i < 0:
            break
        out.append(hex(i))
        st = i + 1
        if len(out) >= limit:
            break
    return out


for path in sys.argv[1:]:
    data = open(path, 'rb').read()
    print('===', path, '===')
    print('XBEH ascii:', find(data, b'XBEH'))
    for s in STRINGS:
        a = find(data, s.encode('ascii'))
        u = find(data, s.encode('utf-16-le'))
        if a or u:
            print('  %-36s ascii:%-28s u16:%s' % (repr(s), ','.join(a), ','.join(u)))
