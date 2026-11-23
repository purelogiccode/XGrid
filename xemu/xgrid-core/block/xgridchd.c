/*
 * XGrid CHD block driver for the xemu core.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * The guest reads a disc; this driver decodes CHD hunks on demand, so a
 * compressed image boots and plays without an extraction step. All format work
 * is libchdr 0.3.0 (BSD-3-Clause, Copyright Romain Tisserand); see
 * thirdparty/libchdr/LICENSE.txt and THIRD_PARTY_NOTICES.md.
 *
 * Design notes:
 *  - Disc I/O is cold (a 12x DVD is ~16 MB/s), so a synchronous hunk decode in
 *    the read coroutine costs nothing measurable against emulation. The last
 *    hunk is kept, which makes the common 2048-byte sector walk a memcpy.
 *  - Only reads are supported; a CHD is an archive, never a writable disk.
 */

#include "qemu/osdep.h"
#include "qapi/error.h"
#include "block/block_int.h"
#include "qemu/module.h"
#include "qemu/iov.h"
#include "libchdr/chd.h"

#define XGRIDCHD_PROBE_SCORE 100
#define XGRIDCHD_MAGIC "MComprHD"

typedef struct BDRVXgridChdState {
    chd_file *chd;
    const chd_header *header;
    uint8_t *hunk;
    uint32_t cached_hunk;
} BDRVXgridChdState;

static int xgridchd_probe(const uint8_t *buf, int buf_size, const char *filename)
{
    if (buf_size < 8 || memcmp(buf, XGRIDCHD_MAGIC, 8) != 0) {
        return 0;
    }

    return XGRIDCHD_PROBE_SCORE;
}

static int xgridchd_open(BlockDriverState *bs, QDict *options, int flags,
                         Error **errp)
{
    BDRVXgridChdState *s = bs->opaque;
    const char *filename;
    chd_error err;
    int ret;

    ret = bdrv_open_file_child(NULL, options, "file", bs, errp);
    if (ret < 0) {
        return ret;
    }

    filename = bs->file->bs->filename;
    err = chd_open(filename, CHD_OPEN_READ, NULL, &s->chd);
    if (err != CHDERR_NONE) {
        error_setg(errp, "xgridchd: cannot open '%s': %s",
                   filename, chd_error_string(err));
        return -EINVAL;
    }

    s->header = chd_get_header(s->chd);
    if (s->header == NULL || s->header->hunkbytes == 0 ||
        s->header->logicalbytes == 0) {
        error_setg(errp, "xgridchd: '%s' has no readable CHD header", filename);
        chd_close(s->chd);
        s->chd = NULL;
        return -EINVAL;
    }

    s->hunk = g_malloc(s->header->hunkbytes);
    s->cached_hunk = UINT32_MAX;
    bs->total_sectors = s->header->logicalbytes / BDRV_SECTOR_SIZE;
    bs->bl.request_alignment = 1;

    return 0;
}

static void xgridchd_close(BlockDriverState *bs)
{
    BDRVXgridChdState *s = bs->opaque;

    g_free(s->hunk);
    s->hunk = NULL;

    if (s->chd != NULL) {
        chd_close(s->chd);
        s->chd = NULL;
    }
}

static int coroutine_fn GRAPH_RDLOCK
xgridchd_co_preadv(BlockDriverState *bs, int64_t offset, int64_t bytes,
                   QEMUIOVector *qiov, BdrvRequestFlags flags)
{
    BDRVXgridChdState *s = bs->opaque;
    int64_t done = 0;

    while (done < bytes) {
        uint64_t position = (uint64_t)offset + (uint64_t)done;
        uint32_t hunk = position / s->header->hunkbytes;
        uint32_t within = position % s->header->hunkbytes;
        uint32_t length = MIN((uint64_t)(bytes - done),
                              (uint64_t)(s->header->hunkbytes - within));

        if (s->cached_hunk != hunk) {
            chd_error err = chd_read(s->chd, hunk, s->hunk);
            if (err != CHDERR_NONE) {
                return -EIO;
            }

            s->cached_hunk = hunk;
        }

        qemu_iovec_from_buf(qiov, done, s->hunk + within, length);
        done += length;
    }

    return 0;
}

static BlockDriver bdrv_xgridchd = {
    .format_name     = "xgridchd",
    .instance_size   = sizeof(BDRVXgridChdState),
    .bdrv_probe      = xgridchd_probe,
    .bdrv_open       = xgridchd_open,
    .bdrv_child_perm = bdrv_default_perms,
    .bdrv_close      = xgridchd_close,
    .bdrv_co_preadv  = xgridchd_co_preadv,
    .is_format       = true,
};

static void bdrv_xgridchd_init(void)
{
    bdrv_register(&bdrv_xgridchd);
}

block_init(bdrv_xgridchd_init);
