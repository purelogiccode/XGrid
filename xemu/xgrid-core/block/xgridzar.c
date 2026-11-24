/*
 * XGrid ZArchive (.zar) block driver for the xemu core.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * Exposes the disc image stored in a ZArchive as the guest's DVD, decoding
 * 64 KiB zstd blocks on demand: nothing is extracted. An entry-form archive
 * exposes its largest .iso entry; a file-tree archive is synthesized into an
 * XDVDFS volume (see the synthesizer below). The format facts are pinned by
 * tests/XGrid.Core.Tests/ZarArchiveTests.cs and the media guide.
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

typedef struct XGridZarIsoNode XGridZarIsoNode;
typedef struct XGridZarExtent XGridZarExtent;

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
    bool synthesized;
    XGridZarIsoNode *iso_nodes;
    uint32_t iso_node_count;
    uint8_t *iso_descriptor;
    XGridZarExtent *iso_extents;
    uint32_t iso_extent_count;
    uint64_t iso_size;
} BDRVXgridZarState;

static bool zar_iso_build(BDRVXgridZarState *s, Error **errp);
static void zar_iso_free(BDRVXgridZarState *s);

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
        if (!zar_iso_build(s, errp)) {
            return -EINVAL;
        }
    }

    s->cached_block = UINT64_MAX;
    s->block = g_malloc(XGRIDZAR_BLOCK_SIZE);
    s->compressed = g_malloc(XGRIDZAR_BLOCK_SIZE + 1);
    bs->total_sectors = (s->synthesized ? s->iso_size : s->file_size) /
                        BDRV_SECTOR_SIZE;
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
    zar_iso_free(s);
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

/*
 * XDVDFS synthesis for file-tree archives.
 *
 * `ZArchiveSharp --iso` stores the game as its files, not as one .iso entry.
 * The guest expects an XDVDFS volume, so this builds the volume descriptor and
 * the directory tables in memory, places each archived file at a sector
 * boundary, and maps guest reads onto the archive's uncompressed extents. The
 * layout follows XISOSharp's writer: the descriptor at sector 32, the root
 * table at sector 264, DWORD-aligned records that never straddle a sector,
 * 0xFF padding, a balanced search tree for the left/right offsets, and
 * sector-rounded sizes for subdirectory tables.
 */
#define XGRIDZAR_ISO_SECTOR 2048u
#define XGRIDZAR_ISO_ROOT_SECTOR 264u
#define XGRIDZAR_ISO_MAX_TABLE 0x3FFFCu /* u16 child offsets in DWORDs */
#define XGRIDZAR_ISO_MAX_NAME 42u
#define XGRIDZAR_ISO_MAX_DEPTH 64
#define XGRIDZAR_ISO_MAGIC "MICROSOFT*XBOX*MEDIA"

typedef struct XGridZarIsoNode {
    char name[XGRIDZAR_ISO_MAX_NAME + 1];
    bool is_dir;
    bool linked;
    uint64_t archive_offset;
    uint32_t file_size;
    uint32_t first_child;
    uint32_t child_count;
    uint32_t entry_offset;
    uint32_t data_sector;
    uint32_t table_bytes;
    uint32_t table_sectors;
    uint8_t *table;
    struct XGridZarIsoNode **children;
    struct XGridZarIsoNode *bst_left;
    struct XGridZarIsoNode *bst_right;
} XGridZarIsoNode;

typedef struct XGridZarExtent {
    uint64_t offset;
    uint64_t length;
    const uint8_t *memory;
    XGridZarIsoNode *file;
} XGridZarExtent;

static uint32_t zar_iso_sectors(uint64_t bytes)
{
    return (uint32_t)((bytes + XGRIDZAR_ISO_SECTOR - 1) / XGRIDZAR_ISO_SECTOR);
}

static uint32_t zar_iso_align4(uint32_t value)
{
    return (value + 3u) & ~3u;
}

static int zar_iso_name_compare(const void *left, const void *right)
{
    const XGridZarIsoNode *a = *(XGridZarIsoNode * const *)left;
    const XGridZarIsoNode *b = *(XGridZarIsoNode * const *)right;
    const unsigned char *pa = (const unsigned char *)a->name;
    const unsigned char *pb = (const unsigned char *)b->name;

    while (*pa != '\0' && *pb != '\0') {
        int ca = g_ascii_tolower(*pa);
        int cb = g_ascii_tolower(*pb);
        if (ca != cb) {
            return ca - cb;
        }
        pa++;
        pb++;
    }

    return (int)*pa - (int)*pb;
}

static bool zar_iso_name_valid(const char *name)
{
    size_t length = strlen(name);

    if (length == 0 || length > XGRIDZAR_ISO_MAX_NAME) {
        return false;
    }

    for (size_t i = 0; i < length; i++) {
        unsigned char c = (unsigned char)name[i];
        if (c < 0x20 || c == '/' || c == '\\' || c == ':') {
            return false;
        }
    }

    return true;
}

static uint32_t zar_iso_name_length(const BDRVXgridZarState *s,
                                    uint32_t name_offset)
{
    uint8_t header;
    uint32_t length;

    if (name_offset >= s->name_size) {
        return 0;
    }

    header = s->names[name_offset];
    length = header & 0x7Fu;
    if ((header & 0x80u) != 0) {
        length |= (uint32_t)header << 7;
    }

    return length;
}

static XGridZarIsoNode *zar_iso_build_tree(XGridZarIsoNode **children,
                                           int low, int high)
{
    if (low >= high) {
        return NULL;
    }

    int middle = low + ((high - low) / 2);
    XGridZarIsoNode *root = children[middle];
    root->bst_left = zar_iso_build_tree(children, low, middle);
    root->bst_right = zar_iso_build_tree(children, middle + 1, high);
    return root;
}

static bool zar_iso_layout(BDRVXgridZarState *s, XGridZarIsoNode *dir,
                           int depth, Error **errp)
{
    if (depth > XGRIDZAR_ISO_MAX_DEPTH) {
        error_setg(errp, "xgridzar: directory tree is too deep");
        return false;
    }

    if (dir->child_count == 0) {
        dir->table_bytes = 0;
        dir->table_sectors = 1;
        dir->table = g_malloc(XGRIDZAR_ISO_SECTOR);
        memset(dir->table, 0xFF, XGRIDZAR_ISO_SECTOR);
        return true;
    }

    if (dir->first_child > s->iso_node_count ||
        dir->child_count > s->iso_node_count - dir->first_child) {
        error_setg(errp, "xgridzar: directory child range is out of bounds");
        return false;
    }

    dir->children = g_new0(XGridZarIsoNode *, dir->child_count);
    for (uint32_t i = 0; i < dir->child_count; i++) {
        XGridZarIsoNode *child = &s->iso_nodes[dir->first_child + i];
        if (child->linked) {
            error_setg(errp, "xgridzar: file tree has more than one parent "
                       "for '%s'", child->name);
            return false;
        }
        child->linked = true;
        dir->children[i] = child;
    }

    qsort(dir->children, dir->child_count, sizeof(XGridZarIsoNode *),
          zar_iso_name_compare);
    for (uint32_t i = 1; i < dir->child_count; i++) {
        if (strcasecmp(dir->children[i - 1]->name, dir->children[i]->name) == 0) {
            error_setg(errp, "xgridzar: duplicate name '%s' in one directory",
                       dir->children[i]->name);
            return false;
        }
    }

    uint32_t offset = 0;
    for (uint32_t i = 0; i < dir->child_count; i++) {
        XGridZarIsoNode *child = dir->children[i];
        uint32_t entry_size = zar_iso_align4(14u + (uint32_t)strlen(child->name));
        if ((offset % XGRIDZAR_ISO_SECTOR) + entry_size > XGRIDZAR_ISO_SECTOR) {
            offset = (offset + XGRIDZAR_ISO_SECTOR - 1) /
                     XGRIDZAR_ISO_SECTOR * XGRIDZAR_ISO_SECTOR;
        }
        child->entry_offset = offset;
        offset += entry_size;
        if (offset > XGRIDZAR_ISO_MAX_TABLE) {
            error_setg(errp, "xgridzar: directory table exceeds the XDVDFS "
                       "offset limit");
            return false;
        }
    }

    dir->table_bytes = offset;
    dir->table_sectors = MAX(1u, zar_iso_sectors(offset));
    dir->table = g_malloc((size_t)dir->table_sectors * XGRIDZAR_ISO_SECTOR);
    memset(dir->table, 0xFF, (size_t)dir->table_sectors * XGRIDZAR_ISO_SECTOR);

    /* The child tables are needed for their sizes before the entries are
     * written, but their offsets were assigned above. */
    for (uint32_t i = 0; i < dir->child_count; i++) {
        if (dir->children[i]->is_dir &&
            !zar_iso_layout(s, dir->children[i], depth + 1, errp)) {
            return false;
        }
    }

    (void)zar_iso_build_tree(dir->children, 0, (int)dir->child_count);

    for (uint32_t i = 0; i < dir->child_count; i++) {
        XGridZarIsoNode *child = dir->children[i];
        uint8_t *entry = dir->table + child->entry_offset;
        uint16_t left = child->bst_left != NULL
            ? (uint16_t)(child->bst_left->entry_offset / 4) : 0;
        uint16_t right = child->bst_right != NULL
            ? (uint16_t)(child->bst_right->entry_offset / 4) : 0;
        uint32_t size = child->is_dir
            ? child->table_sectors * XGRIDZAR_ISO_SECTOR : child->file_size;
        size_t name_length = strlen(child->name);

        stw_le_p(entry, left);
        stw_le_p(entry + 2, right);
        stl_le_p(entry + 4, 0); /* start sector, assigned after placement */
        stl_le_p(entry + 8, size);
        entry[12] = child->is_dir ? 0x10 : 0x20;
        entry[13] = (uint8_t)name_length;
        memcpy(entry + 14, child->name, name_length);
    }

    return true;
}

static bool zar_iso_assign(BDRVXgridZarState *s, XGridZarIsoNode *dir,
                           uint64_t *next, Error **errp)
{
    for (uint32_t i = 0; i < dir->child_count; i++) {
        XGridZarIsoNode *child = dir->children[i];
        uint64_t sectors;

        if (child->is_dir) {
            child->data_sector = (uint32_t)*next;
            sectors = child->table_sectors;
            if (*next + sectors > UINT32_MAX) {
                error_setg(errp, "xgridzar: synthesized image exceeds the "
                           "XDVDFS sector range");
                return false;
            }
            *next += sectors;
            if (!zar_iso_assign(s, child, next, errp)) {
                return false;
            }
        } else if (child->file_size > 0) {
            child->data_sector = (uint32_t)*next;
            sectors = zar_iso_sectors(child->file_size);
            if (*next + sectors > UINT32_MAX) {
                error_setg(errp, "xgridzar: synthesized image exceeds the "
                           "XDVDFS sector range");
                return false;
            }
            *next += sectors;
        } else {
            child->data_sector = 0;
        }
    }

    return true;
}

static void zar_iso_fill_sectors(BDRVXgridZarState *s, XGridZarIsoNode *dir)
{
    for (uint32_t i = 0; i < dir->child_count; i++) {
        XGridZarIsoNode *child = dir->children[i];
        stl_le_p(dir->table + child->entry_offset + 4, child->data_sector);
        if (child->is_dir) {
            zar_iso_fill_sectors(s, child);
        }
    }
}

static uint32_t zar_iso_count_dirs(const XGridZarIsoNode *node)
{
    uint32_t count = 1;
    for (uint32_t i = 0; i < node->child_count; i++) {
        if (node->children[i]->is_dir) {
            count += zar_iso_count_dirs(node->children[i]);
        }
    }
    return count;
}

static uint32_t zar_iso_count_files(const XGridZarIsoNode *node)
{
    uint32_t count = 0;
    for (uint32_t i = 0; i < node->child_count; i++) {
        if (node->children[i]->is_dir) {
            count += zar_iso_count_files(node->children[i]);
        } else if (node->children[i]->file_size > 0) {
            count++;
        }
    }
    return count;
}

static void zar_iso_collect_extents(XGridZarIsoNode *node,
                                    XGridZarExtent **cursor)
{
    XGridZarExtent *extent = (*cursor)++;

    extent->offset = (uint64_t)node->data_sector * XGRIDZAR_ISO_SECTOR;
    extent->length = (uint64_t)node->table_sectors * XGRIDZAR_ISO_SECTOR;
    extent->memory = node->table;
    extent->file = NULL;

    for (uint32_t i = 0; i < node->child_count; i++) {
        XGridZarIsoNode *child = node->children[i];
        if (child->is_dir) {
            zar_iso_collect_extents(child, cursor);
        } else if (child->file_size > 0) {
            extent = (*cursor)++;
            extent->offset = (uint64_t)child->data_sector * XGRIDZAR_ISO_SECTOR;
            extent->length = (uint64_t)zar_iso_sectors(child->file_size) *
                             XGRIDZAR_ISO_SECTOR;
            extent->memory = NULL;
            extent->file = child;
        }
    }
}

static int zar_iso_extent_compare(const void *left, const void *right)
{
    const XGridZarExtent *a = left;
    const XGridZarExtent *b = right;

    if (a->offset < b->offset) {
        return -1;
    }
    if (a->offset > b->offset) {
        return 1;
    }
    return 0;
}

static bool zar_iso_parse(BDRVXgridZarState *s, Error **errp)
{
    uint32_t count = s->tree_size / XGRIDZAR_TREE_ENTRY_SIZE;

    if (count == 0) {
        error_setg(errp, "xgridzar: the file tree is empty");
        return false;
    }

    s->iso_nodes = g_new0(XGridZarIsoNode, count);
    s->iso_node_count = count;

    for (uint32_t i = 0; i < count; i++) {
        const uint8_t *entry = s->file_tree + (i * XGRIDZAR_TREE_ENTRY_SIZE);
        uint32_t name_and_type = zar_be32(entry);
        uint32_t field1 = zar_be32(entry + 4);
        uint32_t field2 = zar_be32(entry + 8);
        uint32_t field3 = zar_be32(entry + 12);
        XGridZarIsoNode *node = &s->iso_nodes[i];
        bool is_file = (name_and_type & 0x80000000u) != 0;

        if (i == 0) {
            if (is_file || name_and_type != 0x7FFFFFFFu) {
                error_setg(errp, "xgridzar: unsupported file-tree root");
                return false;
            }
            node->is_dir = true;
            node->linked = true;
            node->first_child = field1;
            node->child_count = field2;
            continue;
        }

        zar_entry_name(s, name_and_type & 0x7FFFFFFFu, node->name,
                       sizeof(node->name));
        uint32_t declared = zar_iso_name_length(s, name_and_type & 0x7FFFFFFFu);
        if (declared == 0 || declared > XGRIDZAR_ISO_MAX_NAME) {
            error_setg(errp, "xgridzar: '%s' exceeds the 42-character XDVDFS "
                       "name limit", node->name);
            return false;
        }
        if (!zar_iso_name_valid(node->name)) {
            error_setg(errp, "xgridzar: '%s' is not a valid XDVDFS name",
                       node->name);
            return false;
        }

        if (is_file) {
            uint64_t offset = field1 | ((uint64_t)(field3 & 0xFFFFu) << 32);
            uint64_t size = field2 | ((uint64_t)(field3 >> 16) << 32);
            if (size > UINT32_MAX) {
                error_setg(errp, "xgridzar: '%s' exceeds the 4 GiB XDVDFS "
                           "file limit", node->name);
                return false;
            }
            node->archive_offset = offset;
            node->file_size = (uint32_t)size;
        } else {
            node->is_dir = true;
            node->first_child = field1;
            node->child_count = field2;
            if (node->first_child > count ||
                node->child_count > count - node->first_child) {
                error_setg(errp, "xgridzar: '%s' has an invalid child range",
                           node->name);
                return false;
            }
        }
    }

    return true;
}

static bool zar_iso_build(BDRVXgridZarState *s, Error **errp)
{
    XGridZarIsoNode *root;
    uint64_t next;
    uint32_t dir_count;
    uint32_t file_count;
    XGridZarExtent *cursor;
    uint8_t *descriptor;

    if (!zar_iso_parse(s, errp)) {
        return false;
    }

    root = &s->iso_nodes[0];
    if (!zar_iso_layout(s, root, 0, errp)) {
        return false;
    }

    root->data_sector = XGRIDZAR_ISO_ROOT_SECTOR;
    next = (uint64_t)XGRIDZAR_ISO_ROOT_SECTOR + root->table_sectors;
    if (!zar_iso_assign(s, root, &next, errp)) {
        return false;
    }

    zar_iso_fill_sectors(s, root);

    descriptor = g_malloc0(XGRIDZAR_ISO_SECTOR);
    memcpy(descriptor, XGRIDZAR_ISO_MAGIC, strlen(XGRIDZAR_ISO_MAGIC));
    stl_le_p(descriptor + 0x14, XGRIDZAR_ISO_ROOT_SECTOR);
    stl_le_p(descriptor + 0x18, root->table_bytes);
    stq_le_p(descriptor + 0x1C, 0); /* deterministic 1601 FILETIME */
    memcpy(descriptor + 0x7EC, XGRIDZAR_ISO_MAGIC,
           strlen(XGRIDZAR_ISO_MAGIC));
    s->iso_descriptor = descriptor;

    dir_count = zar_iso_count_dirs(root);
    file_count = zar_iso_count_files(root);
    s->iso_extent_count = 1 + dir_count + file_count;
    s->iso_extents = g_new0(XGridZarExtent, s->iso_extent_count);

    s->iso_extents[0].offset = 32u * XGRIDZAR_ISO_SECTOR;
    s->iso_extents[0].length = XGRIDZAR_ISO_SECTOR;
    s->iso_extents[0].memory = descriptor;

    cursor = &s->iso_extents[1];
    zar_iso_collect_extents(root, &cursor);
    qsort(s->iso_extents, s->iso_extent_count, sizeof(XGridZarExtent),
          zar_iso_extent_compare);

    for (uint32_t i = 1; i < s->iso_extent_count; i++) {
        const XGridZarExtent *previous = &s->iso_extents[i - 1];
        if (s->iso_extents[i].offset < previous->offset + previous->length) {
            error_setg(errp, "xgridzar: synthesized extents overlap");
            return false;
        }
    }

    s->iso_size = next * XGRIDZAR_ISO_SECTOR;
    s->synthesized = true;
    return true;
}

static void zar_iso_free(BDRVXgridZarState *s)
{
    if (s->iso_nodes != NULL) {
        for (uint32_t i = 0; i < s->iso_node_count; i++) {
            g_free(s->iso_nodes[i].table);
            g_free(s->iso_nodes[i].children);
        }
        g_free(s->iso_nodes);
        s->iso_nodes = NULL;
    }

    g_free(s->iso_descriptor);
    s->iso_descriptor = NULL;
    g_free(s->iso_extents);
    s->iso_extents = NULL;
    s->iso_extent_count = 0;
    s->iso_node_count = 0;
}

static int zar_iso_read_file(BDRVXgridZarState *s, XGridZarIsoNode *file,
                             uint64_t position, uint8_t *out, uint32_t length)
{
    while (length > 0) {
        uint64_t absolute = file->archive_offset + position;
        uint64_t block = absolute / XGRIDZAR_BLOCK_SIZE;
        uint32_t within = (uint32_t)(absolute % XGRIDZAR_BLOCK_SIZE);
        uint32_t chunk = MIN(length, (uint32_t)(XGRIDZAR_BLOCK_SIZE - within));

        if (s->cached_block != block) {
            int ret = xgridzar_read_block(s, block);
            if (ret < 0) {
                return ret;
            }
            s->cached_block = block;
        }

        memcpy(out, s->block + within, chunk);
        out += chunk;
        position += chunk;
        length -= chunk;
    }

    return 0;
}

static int coroutine_fn GRAPH_RDLOCK
xgridzar_iso_preadv(BlockDriverState *bs, int64_t offset, int64_t bytes,
                    QEMUIOVector *qiov)
{
    BDRVXgridZarState *s = bs->opaque;
    int64_t done = 0;

    while (done < bytes) {
        uint64_t position = (uint64_t)offset + (uint64_t)done;
        uint32_t low = 0;
        uint32_t high = s->iso_extent_count;
        const XGridZarExtent *extent;

        while (low < high) {
            uint32_t middle = low + ((high - low) / 2);
            if (s->iso_extents[middle].offset <= position) {
                low = middle + 1;
            } else {
                high = middle;
            }
        }

        if (low == 0 ||
            position >= s->iso_extents[low - 1].offset +
                        s->iso_extents[low - 1].length) {
            uint64_t end = low < s->iso_extent_count
                ? s->iso_extents[low].offset : s->iso_size;
            uint64_t length = MIN((uint64_t)(bytes - done), end - position);
            if (length == 0) {
                return -EIO;
            }
            qemu_iovec_memset(qiov, done, 0, length);
            done += length;
            continue;
        }

        extent = &s->iso_extents[low - 1];
        uint64_t within = position - extent->offset;
        uint64_t length = MIN((uint64_t)(bytes - done),
                              extent->length - within);
        if (extent->memory != NULL) {
            qemu_iovec_from_buf(qiov, done, extent->memory + within, length);
        } else {
            XGridZarIsoNode *file = extent->file;
            uint64_t available = within < file->file_size
                ? file->file_size - within : 0;
            uint64_t copy = MIN(length, available);
            uint64_t copied = 0;

            while (copied < copy) {
                uint64_t chunk = MIN(copy - copied, 65536u);
                uint8_t *buffer = g_malloc(chunk);
                int ret = zar_iso_read_file(s, file, within + copied,
                                            buffer, (uint32_t)chunk);
                if (ret < 0) {
                    g_free(buffer);
                    return ret;
                }
                qemu_iovec_from_buf(qiov, done + copied, buffer, chunk);
                g_free(buffer);
                copied += chunk;
            }
            if (copy < length) {
                qemu_iovec_memset(qiov, done + copy, 0, length - copy);
            }
        }
        done += length;
    }

    return 0;
}

static int coroutine_fn GRAPH_RDLOCK
xgridzar_entry_preadv(BlockDriverState *bs, int64_t offset, int64_t bytes,
                      QEMUIOVector *qiov, BdrvRequestFlags flags)
{
    BDRVXgridZarState *s = bs->opaque;
    int64_t done = 0;

    (void)flags;

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

static int coroutine_fn GRAPH_RDLOCK
xgridzar_co_preadv(BlockDriverState *bs, int64_t offset, int64_t bytes,
                   QEMUIOVector *qiov, BdrvRequestFlags flags)
{
    BDRVXgridZarState *s = bs->opaque;

    if (s->synthesized) {
        return xgridzar_iso_preadv(bs, offset, bytes, qiov);
    }

    return xgridzar_entry_preadv(bs, offset, bytes, qiov, flags);
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
