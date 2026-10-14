"""scan_objects.py - locate NUL-terminated object-name strings in RAM dumps.

Object-manager header names are stored NUL-terminated (ANSI) inline, so a
standalone '<name>\\0' hit is evidence the object exists (120).

Usage: python scan_objects.py <ram.bin> [<ram.bin> ...]
"""

import sys

NAMES = [
    '\\Device\\Harddisk0',
    '\\Device\\Harddisk0\\Partition0',
    '\\Device\\Harddisk0\\Partition1',
    '\\Device\\Harddisk0\\Partition2',
    '\\Device\\Harddisk0\\Partition3',
    '\\Device\\Harddisk0\\Partition7',
    '\\Device\\CdRom0',
    '\\??\\D:',
    '\\??\\CdRom0:',
    '\\??\\C:',
    '\\??\\E:',
    '\\??\\A:',
]


def find_all(data, pat, limit=10):
    out = []
    i = 0
    while True:
        i = data.find(pat, i)
        if i < 0:
            break
        out.append(hex(i))
        i += 1
        if len(out) >= limit:
            break
    return out


for path in sys.argv[1:]:
    data = open(path, 'rb').read()
    print('===', path, '===')
    for name in NAMES:
        bare = find_all(data, name.encode('ascii') + b'\x00')
        wide = find_all(data, name.encode('utf-16-le') + b'\x00\x00')
        if bare or wide:
            print('  %-34s ansi:%-30s wide:%s' % (repr(name), ','.join(bare), ','.join(wide)))
