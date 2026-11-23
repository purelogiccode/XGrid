/*
 * XGrid host display sink.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * See display/xgrid-display.h. Windows only for now: the shell and the core are
 * Windows processes, and a named file mapping is the cheapest lossless path.
 */

#include "xgrid-display.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#define XGRID_DISPLAY_MAGIC 0x53444758u /* 'XGDS' little endian */
#define XGRID_DISPLAY_HEADER 32u

static HANDLE g_mapping;
static void *g_view;
static uint32_t g_capacity;
static uint32_t g_width;
static uint32_t g_height;
static uint32_t g_stride;
static uint64_t g_frame;
static char g_configured_name[256];

void xgrid_display_set_sink(const char *name)
{
    if (name == NULL || name[0] == '\0') {
        g_configured_name[0] = '\0';
        return;
    }

    snprintf(g_configured_name, sizeof(g_configured_name), "%s", name);
}

static const char *sink_name(void)
{
    static char name[256];
    const char *configured;

    if (g_configured_name[0] != '\0') {
        return g_configured_name;
    }

    configured = getenv("XGRID_DISPLAY_SHM");

    if (configured == NULL || configured[0] == '\0') {
        return NULL;
    }

    snprintf(name, sizeof(name), "%s", configured);
    return name;
}

bool xgrid_embed_enabled(void)
{
    const char *value = getenv("XGRID_EMBED");
    return value != NULL && value[0] == '1';
}

bool xgrid_display_enabled(void)
{
    return sink_name() != NULL;
}

static bool map_section(uint32_t width, uint32_t height, uint32_t stride)
{
    uint32_t size = XGRID_DISPLAY_HEADER + stride * height;
    const char *name = sink_name();

    if (name == NULL) {
        return false;
    }

    if (g_view != NULL && g_capacity >= size && g_width == width &&
        g_height == height) {
        return true;
    }

    if (g_view != NULL) {
        UnmapViewOfFile(g_view);
        g_view = NULL;
    }

    if (g_mapping != NULL) {
        CloseHandle(g_mapping);
        g_mapping = NULL;
    }

    /* Recreate when the geometry grows; the shell re-maps on a size change. */
    g_mapping = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL,
                                   PAGE_READWRITE, 0, size, name);
    if (g_mapping == NULL) {
        return false;
    }

    g_view = MapViewOfFile(g_mapping, FILE_MAP_WRITE, 0, 0, size);
    if (g_view == NULL) {
        CloseHandle(g_mapping);
        g_mapping = NULL;
        return false;
    }

    g_capacity = size;
    g_width = width;
    g_height = height;
    g_stride = stride;
    g_frame = 0;
    return true;
}

void xgrid_display_publish(const void *pixels, uint32_t width, uint32_t height,
                           uint32_t stride)
{
    uint8_t *base;

    if (pixels == NULL || width == 0 || height == 0 || stride < width * 4) {
        return;
    }

    if (!map_section(width, height, stride)) {
        return;
    }

    base = (uint8_t *)g_view;
    memcpy(base + 0, &(uint32_t){XGRID_DISPLAY_MAGIC}, 4);
    memcpy(base + 4, &width, 4);
    memcpy(base + 8, &height, 4);
    memcpy(base + 12, &stride, 4);

    /* Pixels first, then the counter, so a reader never sees a torn frame
     * without also seeing the matching count. */
    memcpy(base + XGRID_DISPLAY_HEADER, pixels, (size_t)stride * height);

    g_frame++;
    memcpy(base + 16, &g_frame, 8);
}

#else

bool xgrid_embed_enabled(void)
{
    return false;
}

void xgrid_display_set_sink(const char *name)
{
    (void)name;
}

bool xgrid_display_enabled(void)
{
    return false;
}

void xgrid_display_publish(const void *pixels, uint32_t width, uint32_t height,
                           uint32_t stride)
{
    (void)pixels;
    (void)width;
    (void)height;
    (void)stride;
}

#endif /* _WIN32 */
