/*
 * XGrid CCI block driver for the xemu core.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * CCI ("CCIM") is the compressed Xbox disc container used by Cerbios and
 * Repackinator: fixed 2048-byte sectors, each stored raw or as one LZ4 block,
 * with a random-access index. libCCI (vendored under thirdparty/libcci) parses
 * the header/index, decodes sectors and stitches split `name.N.cci` parts.
 *
 * This adapter adds the XGrid disc contract: the XDVDFS game partition is
 * mapped to LBA 0, so a CCI built from a full Redump image boots the game
 * rather than its video partition. Reads stay compressed; nothing is extracted.
 */

#include "qemu/osdep.h"
#include "qapi/error.h"
#include "block/block_int.h"
#include "qemu/module.h"
#include "qemu/iov.h"
#include "cci/cci.h"
#include "xgrid-iso.h"

typedef struct BDRVXgridCciState {
    cci_volume *volume;
    uint64_t offset;
} BDRVXgridCciState;

static bool xgridcci_probe_read(void *opaque, uint64_t offset, void *buffer,
                                size_t size)
{
    BDRVXgridCciState *s = opaque;

    return cci_volume_read(s->volume, offset, buffer, size) == CCI_OK;
}

static int xgridcci_error(int error)
{
    switch (error) {
    case CCI_OK:
        return 0;
    case CCI_ERR_NOMEM:
        return -ENOMEM;
    case CCI_ERR_RANGE:
    case CCI_ERR_ARG:
        return -EINVAL;
    default:
        return -EIO;
    }
}

static int xgridcci_probe(const uint8_t *buf, int buf_size, const char *filename)
{
    if (buf_size >= 4 && memcmp(buf, "CCIM", 4) == 0) {
        return 200;
    }

    if (filename != NULL) {
        const char *extension = strrchr(filename, '.');
        if (extension != NULL &&
            g_ascii_strcasecmp(extension, ".cci") == 0) {
            return 100;
        }
    }

    return 0;
}

static int xgridcci_open(BlockDriverState *bs, QDict *options, int flags,
                         Error **errp)
{
    BDRVXgridCciState *s = bs->opaque;
    const char *filename;
    uint64_t size;
    int ret;

    ret = bdrv_open_file_child(NULL, options, "file", bs, errp);
    if (ret < 0) {
        return ret;
    }

    filename = bs->file->bs->filename;
    ret = cci_open_file(&s->volume, filename);
    if (ret != CCI_OK) {
        error_setg(errp, "xgridcci: %s", cci_strerror(ret));
        return xgridcci_error(ret);
    }

    size = cci_volume_size(s->volume);
    if (size == 0) {
        error_setg(errp, "xgridcci: '%s' contains no sectors", filename);
        cci_volume_free(s->volume);
        s->volume = NULL;
        return -EINVAL;
    }

    s->offset = xgrid_iso_find_game_offset(xgridcci_probe_read, s, size);
    if (s->offset == UINT64_MAX) {
        s->offset = 0;
    }

    bs->total_sectors = (size - s->offset) / BDRV_SECTOR_SIZE;
    bs->bl.request_alignment = 1;
    bs->bl.has_variable_length = false;

    return 0;
}

static void xgridcci_close(BlockDriverState *bs)
{
    BDRVXgridCciState *s = bs->opaque;

    if (s->volume != NULL) {
        cci_volume_free(s->volume);
        s->volume = NULL;
    }
}

static int coroutine_fn GRAPH_RDLOCK
xgridcci_co_preadv(BlockDriverState *bs, int64_t offset, int64_t bytes,
                   QEMUIOVector *qiov, BdrvRequestFlags flags)
{
    BDRVXgridCciState *s = bs->opaque;
    uint8_t *buffer;
    int ret;

    if (offset < 0 || bytes < 0 ||
        offset > INT64_MAX - (int64_t)s->offset) {
        return -EINVAL;
    }

    if ((uint64_t)bytes > SIZE_MAX) {
        return -EINVAL;
    }

    if (bytes == 0) {
        return 0;
    }

    buffer = g_try_malloc((size_t)bytes);
    if (buffer == NULL) {
        return -ENOMEM;
    }

    ret = cci_volume_read(s->volume, s->offset + (uint64_t)offset, buffer,
                          (size_t)bytes);
    if (ret == CCI_OK) {
        qemu_iovec_from_buf(qiov, 0, buffer, bytes);
    }
    else {
        ret = xgridcci_error(ret);
    }

    g_free(buffer);
    return ret;
}

static int coroutine_fn GRAPH_RDLOCK
xgridcci_co_pwritev(BlockDriverState *bs, int64_t offset, int64_t bytes,
                    QEMUIOVector *qiov, BdrvRequestFlags flags)
{
    return -EROFS;
}

static int coroutine_fn GRAPH_RDLOCK
xgridcci_co_block_status(BlockDriverState *bs, unsigned int mode,
                         int64_t offset, int64_t bytes, int64_t *pnum,
                         int64_t *map, BlockDriverState **file)
{
    *pnum = bytes;
    *map = -1;
    *file = NULL;
    return BDRV_BLOCK_DATA;
}

static void xgridcci_child_perm(BlockDriverState *bs, BdrvChild *child,
                                BdrvChildRole role,
                                BlockReopenQueue *reopen_queue,
                                uint64_t parent_perm, uint64_t parent_shared,
                                uint64_t *nperm, uint64_t *nshared)
{
    bdrv_default_perms(bs, child, role, reopen_queue, parent_perm,
                       parent_shared, nperm, nshared);
    *nperm &= ~(BLK_PERM_WRITE | BLK_PERM_RESIZE);
}

static BlockDriver bdrv_xgridcci = {
    .format_name          = "xgridcci",
    .instance_size        = sizeof(BDRVXgridCciState),
    .is_format            = true,
    .bdrv_probe           = xgridcci_probe,
    .bdrv_open            = xgridcci_open,
    .bdrv_close           = xgridcci_close,
    .bdrv_child_perm      = xgridcci_child_perm,
    .bdrv_co_preadv       = xgridcci_co_preadv,
    .bdrv_co_pwritev      = xgridcci_co_pwritev,
    .bdrv_co_block_status = xgridcci_co_block_status,
};

static void bdrv_xgridcci_init(void)
{
    bdrv_register(&bdrv_xgridcci);
}

block_init(bdrv_xgridcci_init);
