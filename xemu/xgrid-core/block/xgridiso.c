/*
 * XGrid raw ISO block driver for the xemu core.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * Presents the XDVDFS game partition of an ISO/XISO at LBA 0. An optimized
 * XISO already starts there; a full Redump image keeps its video partition in
 * front, so this driver detects the XGD partition offset and maps guest reads
 * past it. Nothing is extracted and the image is never modified.
 */

#include "qemu/osdep.h"
#include "qapi/error.h"
#include "block/block-io.h"
#include "block/block_int.h"
#include "qemu/module.h"
#include "qemu/iov.h"
#include "xgrid-iso.h"

typedef struct BDRVXgridIsoState {
    uint64_t offset;
} BDRVXgridIsoState;

static bool xgridiso_probe_read(void *opaque, uint64_t offset, void *buffer,
                                size_t size)
{
    FILE *file = opaque;

    if (fseeko(file, (off_t)offset, SEEK_SET) != 0) {
        return false;
    }

    return fread(buffer, 1, size, file) == size;
}

static int xgridiso_open(BlockDriverState *bs, QDict *options, int flags,
                         Error **errp)
{
    BDRVXgridIsoState *s = bs->opaque;
    const char *filename;
    FILE *file;
    int64_t length;
    int ret;

    ret = bdrv_open_file_child(NULL, options, "file", bs, errp);
    if (ret < 0) {
        return ret;
    }

    length = bdrv_getlength(bs->file->bs);
    if (length < 0) {
        error_setg_errno(errp, -length, "xgridiso: cannot size the image");
        return length;
    }

    filename = bs->file->bs->filename;
    file = fopen(filename, "rb");
    if (file == NULL) {
        error_setg_errno(errp, errno, "xgridiso: cannot open '%s'", filename);
        return -errno;
    }

    s->offset = xgrid_iso_find_game_offset(xgridiso_probe_read, file,
                                           (uint64_t)length);
    fclose(file);

    /* An unrecognized layout keeps the historical raw behavior. */
    if (s->offset == UINT64_MAX) {
        s->offset = 0;
    }

    bs->total_sectors = (uint64_t)(length - (int64_t)s->offset) /
                        BDRV_SECTOR_SIZE;
    bs->bl.request_alignment = 1;

    return 0;
}

static int coroutine_fn GRAPH_RDLOCK
xgridiso_co_preadv(BlockDriverState *bs, int64_t offset, int64_t bytes,
                   QEMUIOVector *qiov, BdrvRequestFlags flags)
{
    BDRVXgridIsoState *s = bs->opaque;

    if (offset < 0 || bytes < 0 || offset > INT64_MAX - (int64_t)s->offset) {
        return -EINVAL;
    }

    return bdrv_co_preadv(bs->file, offset + (int64_t)s->offset, bytes, qiov,
                          flags);
}

static BlockDriver bdrv_xgridiso = {
    .format_name     = "xgridiso",
    .instance_size   = sizeof(BDRVXgridIsoState),
    .bdrv_open       = xgridiso_open,
    .bdrv_child_perm = bdrv_default_perms,
    .bdrv_co_preadv  = xgridiso_co_preadv,
    .is_format       = true,
};

static void bdrv_xgridiso_init(void)
{
    bdrv_register(&bdrv_xgridiso);
}

block_init(bdrv_xgridiso_init);
