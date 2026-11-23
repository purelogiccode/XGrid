/*
 * XGrid ZArchive (.zar) block driver for the xemu core.
 *
 * SPDX-License-Identifier: GPL-2.0
 *
 * Exposes the disc image stored in a ZArchive as the guest's DVD, decoding
 * 64 KiB zstd blocks on demand: nothing is extracted. The format facts are in
 * docs/research-notes/zarchive-format.md, taken from the operator's
 * ZArchiveSharp port of ZArchive 0.1.2 (Exzap).
 *
 * The driver owns its file handle (like the CHD driver does): reading through
 * the block layer from bdrv_open is not allowed, and a disc read is a cold
 * operation where a plain FILE* is simpler and just as fast.
 */

#include "qemu/osdep.h"
#include "qapi/error.h"
#include "block/block_int.h"
#include "qemu/module.h"
#include "qemu/iov.h"
#include <zstd.h>

#define XGRIDZAR_MAGIC 0x169F52D6u
#define XGRIDZAR_VERSION 0x61BF3A01u
#define XGRIDZAR_FOOTER_SIZE 144u
#define XGRIDZAR_BLOCK_SIZE 65536u
#define XGRIDZAR_ENTRIES_PER_RECORD 16u
#define XGRIDZAR_RECORD_SIZE 40u
#define XGRIDZAR_TREE_ENTRY_SIZE 16u

typedef struct BDRVXgridZarState {
    FILE *file;
    uint8_t *offset_records;
    uint64_t records_size;
    uint8_t *names;
    uint32_t name_size;
    uint8_t *file_tree;
    uint32_t tree_size;
    uint64_t compressed_offset;
    uint64_t compressed_size;
    uint64_t file_offset;
    uint64_t file_size;
    uint8_t *block;
    uint8_t *compressed;
    uint64_t cached_block;
} BDRVXgridZarState;

static uint32_t zar_be32(const uint8_t *p)
{
    return ldl_be_p(p);
}

static uint64_t zar_be64(const uint8_t *p)
{
    return ldq_be_p(p);
}

static int zar_probe(const uint8_t *buf, int buf_size, const char *filename)
{
    (void)buf;
    (void)buf_size;
    (void)filename;
    /* The magic is at the end of the file, out of reach of a header probe. */
    return 0;
}

static bool zar_read_at(BDRVXgridZarState *s, uint64_t offset, uint64_t size,
                        void *buffer)
{
    if (fseeko(s->file, (off_t)offset, SEEK_SET) != 0) {
        return false;
    }

    return fread(buffer, 1, size, s->file) == size;
}

static bool zar_read_section(BDRVXgridZarState *s, uint64_t offset,
                             uint64_t size, uint8_t **out, Error **errp)
{
    if (size == 0 || size > 0x7FFFFFFFull) {
        error_setg(errp, "xgridzar: unsupported section size %" PRIu64, size);
        return false;
    }

    *out = g_malloc(size);
    if (!zar_read_at(s, offset, size, *out)) {
        error_setg(errp, "xgridzar: could not read a %" PRIu64 " byte section",
                   size);
        g_free(*out);
        *out = NULL;
        return false;
    }

    return true;
}

static void zar_entry_name(const BDRVXgridZarState *s, uint32_t name_offset,
                           char *out, size_t out_size)
{
    if (name_offset >= s->name_size || out_size == 0) {
        out[0] = '\0';
        return;
    }

    /* One header byte carries a 7-bit length; the high bit selects the
     * two-byte form (with the upstream quirk that the second byte is read
     * from the header byte again). */
    uint8_t header = s->names[name_offset];
    uint32_t length = header & 0x7Fu;
    uint32_t start;
    if ((header & 0x80u) != 0) {
        if (name_offset + 1 >= s->name_size) {
            out[0] = '\0';
            return;
        }
        length |= (uint32_t)header << 7;
        start = name_offset + 2;
    } else {
        start = name_offset + 1;
    }

    uint32_t available = s->name_size - start;
    if (length > available) {
        length = available;
    }

    if (length >= out_size) {
        length = out_size - 1;
    }

    for (uint32_t i = 0; i < length; i++) {
        out[i] = (char)s->names[start + i];
    }

    out[length] = '\0';
}

static bool zar_name_is_disc(const char *name)
{
    size_t length = strlen(name);
    if (length < 4) {
        return false;
    }

    return strcasecmp(name + length - 4, ".iso") == 0;
}

static int xgridzar_open(BlockDriverState *bs, QDict *options, int flags,
                         Error **errp)
{
    BDRVXgridZarState *s = bs->opaque;
    uint8_t footer[XGRIDZAR_FOOTER_SIZE];
    uint64_t file_length;
    uint64_t total_size;
    uint32_t version;
    uint32_t magic;
    int ret;

    ret = bdrv_open_file_child(NULL, options, "file", bs, errp);
    if (ret < 0) {
        return ret;
    }

    s->file = fopen(bs->file->bs->filename, "rb");
    if (s->file == NULL) {
        error_setg(errp, "xgridzar: cannot open '%s'", bs->file->bs->filename);
        return -EINVAL;
    }

    if (fseeko(s->file, 0, SEEK_END) != 0) {
        error_setg(errp, "xgridzar: cannot size the archive");
        return -EINVAL;
    }

    file_length = (uint64_t)ftello(s->file);
    if (file_length < XGRIDZAR_FOOTER_SIZE) {
        error_setg(errp, "xgridzar: file is too small to be an archive");
        return -EINVAL;
    }

    if (!zar_read_at(s, file_length - XGRIDZAR_FOOTER_SIZE,
                     XGRIDZAR_FOOTER_SIZE, footer)) {
        error_setg(errp, "xgridzar: could not read the archive footer");
        return -EINVAL;
    }

    magic = zar_be32(footer + 140);
    version = zar_be32(footer + 136);
    total_size = zar_be64(footer + 128);
    if (magic != XGRIDZAR_MAGIC || version != XGRIDZAR_VERSION) {
        error_setg(errp, "xgridzar: not a ZArchive 0.1.2 file");
        return -EINVAL;
    }

    if (total_size != file_length) {
        error_setg(errp, "xgridzar: truncated archive (%" PRIu64 " of %" PRIu64
                   " bytes)", file_length, total_size);
        return -EINVAL;
    }

    s->compressed_offset = zar_be64(footer + 0);
    s->compressed_size = zar_be64(footer + 8);
    uint64_t records_offset = zar_be64(footer + 16);
    s->records_size = zar_be64(footer + 24);
    uint64_t names_offset = zar_be64(footer + 32);
    s->name_size = (uint32_t)zar_be64(footer + 40);
    uint64_t tree_offset = zar_be64(footer + 48);
    s->tree_size = (uint32_t)zar_be64(footer + 56);

    if (s->records_size == 0 || s->records_size % XGRIDZAR_RECORD_SIZE != 0 ||
        s->name_size == 0 || s->tree_size < XGRIDZAR_TREE_ENTRY_SIZE ||
        s->tree_size % XGRIDZAR_TREE_ENTRY_SIZE != 0) {
        error_setg(errp, "xgridzar: archive sections are malformed");
        return -EINVAL;
    }

    if (!zar_read_section(s, records_offset, s->records_size,
                          &s->offset_records, errp) ||
        !zar_read_section(s, names_offset, s->name_size, &s->names, errp) ||
        !zar_read_section(s, tree_offset, s->tree_size, &s->file_tree, errp)) {
        return -EINVAL;
    }

    /* Largest .iso entry wins; an archive of loose files has none. */
    bool found = false;
    for (uint32_t i = 0; i < s->tree_size / XGRIDZAR_TREE_ENTRY_SIZE; i++) {
        const uint8_t *entry = s->file_tree + (i * XGRIDZAR_TREE_ENTRY_SIZE);
        uint32_t name_and_type = zar_be32(entry);
        char name[260];
        uint64_t offset;
        uint64_t size;

        if ((name_and_type & 0x80000000u) == 0) {
            continue;
        }

        zar_entry_name(s, name_and_type & 0x7FFFFFFFu, name, sizeof(name));
        offset = zar_be32(entry + 4) |
                 ((uint64_t)(zar_be32(entry + 12) & 0xFFFFu) << 32);
        size = zar_be32(entry + 8) |
               ((uint64_t)(zar_be32(entry + 12) >> 16) << 32);
        if (!zar_name_is_disc(name) || size == 0) {
            continue;
        }

        if (!found || size > s->file_size) {
            s->file_offset = offset;
            s->file_size = size;
            found = true;
        }
    }

    if (!found) {
        error_setg(errp, "xgridzar: no .iso entry in this archive; repack it "
                   "with `ZArchiveSharp --iso <game.iso> <out.zar>`");
        return -EINVAL;
    }

    s->cached_block = UINT64_MAX;
    s->block = g_malloc(XGRIDZAR_BLOCK_SIZE);
    s->compressed = g_malloc(XGRIDZAR_BLOCK_SIZE + 1);
    bs->total_sectors = s->file_size / BDRV_SECTOR_SIZE;
    bs->bl.request_alignment = 1;

    return 0;
}

static void xgridzar_close(BlockDriverState *bs)
{
    BDRVXgridZarState *s = bs->opaque;

    if (s->file != NULL) {
        fclose(s->file);
        s->file = NULL;
    }

    g_free(s->offset_records);
    g_free(s->names);
    g_free(s->file_tree);
    g_free(s->block);
    g_free(s->compressed);
}

static int xgridzar_read_block(BDRVXgridZarState *s, uint64_t archive_block)
{
    uint64_t record_index = archive_block / XGRIDZAR_ENTRIES_PER_RECORD;
    uint32_t sub_index = archive_block % XGRIDZAR_ENTRIES_PER_RECORD;
    const uint8_t *record;
    uint64_t offset;
    uint32_t compressed_size;

    if ((record_index + 1) * XGRIDZAR_RECORD_SIZE > s->records_size) {
        return -EIO;
    }

    record = s->offset_records + (record_index * XGRIDZAR_RECORD_SIZE);
    offset = zar_be64(record);
    for (uint32_t i = 0; i < sub_index; i++) {
        offset += (uint64_t)lduw_be_p(record + 8 + (i * 2)) + 1;
    }

    compressed_size = (uint32_t)lduw_be_p(record + 8 + (sub_index * 2)) + 1;
    if (offset + compressed_size > s->compressed_size) {
        return -EIO;
    }

    if (!zar_read_at(s, s->compressed_offset + offset, compressed_size,
                     s->compressed)) {
        return -EIO;
    }

    if (compressed_size == XGRIDZAR_BLOCK_SIZE) {
        memcpy(s->block, s->compressed, XGRIDZAR_BLOCK_SIZE);
        return 0;
    }

    size_t decoded = ZSTD_decompress(s->block, XGRIDZAR_BLOCK_SIZE,
                                     s->compressed, compressed_size);
    if (ZSTD_isError(decoded) || decoded == 0) {
        return -EIO;
    }

    if (decoded < XGRIDZAR_BLOCK_SIZE) {
        memset(s->block + decoded, 0, XGRIDZAR_BLOCK_SIZE - decoded);
    }

    return 0;
}

static int coroutine_fn GRAPH_RDLOCK
xgridzar_co_preadv(BlockDriverState *bs, int64_t offset, int64_t bytes,
                   QEMUIOVector *qiov, BdrvRequestFlags flags)
{
    BDRVXgridZarState *s = bs->opaque;
    int64_t done = 0;

    while (done < bytes) {
        uint64_t position = s->file_offset + (uint64_t)offset + (uint64_t)done;
        uint64_t archive_block = position / XGRIDZAR_BLOCK_SIZE;
        uint32_t within = position % XGRIDZAR_BLOCK_SIZE;
        uint32_t length = MIN((uint64_t)(bytes - done),
                              (uint64_t)(XGRIDZAR_BLOCK_SIZE - within));

        if (s->cached_block != archive_block) {
            int ret = xgridzar_read_block(s, archive_block);
            if (ret < 0) {
                return ret;
            }

            s->cached_block = archive_block;
        }

        qemu_iovec_from_buf(qiov, done, s->block + within, length);
        done += length;
    }

    return 0;
}

static BlockDriver bdrv_xgridzar = {
    .format_name     = "xgridzar",
    .instance_size   = sizeof(BDRVXgridZarState),
    .bdrv_probe      = zar_probe,
    .bdrv_open       = xgridzar_open,
    .bdrv_child_perm = bdrv_default_perms,
    .bdrv_close      = xgridzar_close,
    .bdrv_co_preadv  = xgridzar_co_preadv,
    .is_format       = true,
};

static void bdrv_xgridzar_init(void)
{
    bdrv_register(&bdrv_xgridzar);
}

block_init(bdrv_xgridzar_init);
