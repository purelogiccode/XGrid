/*
 * XDVDFS game-partition detection shared by the XGrid block drivers.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 */

#include "qemu/osdep.h"
#include "xgrid-iso.h"

#define XGRID_ISO_SECTOR 2048u
#define XGRID_ISO_DESCRIPTOR_SECTOR 32u
#define XGRID_ISO_MAGIC "MICROSOFT*XBOX*MEDIA"
#define XGRID_ISO_MAGIC_LENGTH 20u
#define XGRID_ISO_TRAILING_MAGIC 0x7ECu

/*
 * Game-partition offsets, in probe order: optimized XISO first, then the
 * retail XGD layouts (XGD2, XGD3, XGD2 hybrid, XGD1). The values match the
 * managed XdvdfsSectorReader and are relative to the start of the image.
 */
static const uint64_t xgrid_iso_offsets[] = {
    0x00000000ull,
    0x0FD90000ull,
    0x02080000ull,
    0x89D80000ull,
    0x18300000ull,
};

uint64_t xgrid_iso_find_game_offset(XGridIsoReadFn read, void *opaque,
                                    uint64_t size)
{
    uint8_t descriptor[XGRID_ISO_SECTOR];

    if (read == NULL) {
        return UINT64_MAX;
    }

    for (size_t i = 0; i < G_N_ELEMENTS(xgrid_iso_offsets); i++) {
        uint64_t base = xgrid_iso_offsets[i];
        uint64_t descriptor_offset =
            base + (uint64_t)XGRID_ISO_DESCRIPTOR_SECTOR * XGRID_ISO_SECTOR;

        if (base > size || descriptor_offset > size ||
            size - descriptor_offset < XGRID_ISO_SECTOR) {
            continue;
        }

        if (!read(opaque, descriptor_offset, descriptor, sizeof(descriptor))) {
            continue;
        }

        if (memcmp(descriptor, XGRID_ISO_MAGIC, XGRID_ISO_MAGIC_LENGTH) != 0) {
            continue;
        }

        if (memcmp(descriptor + XGRID_ISO_TRAILING_MAGIC, XGRID_ISO_MAGIC,
                   XGRID_ISO_MAGIC_LENGTH) != 0) {
            continue;
        }

        return base;
    }

    return UINT64_MAX;
}
