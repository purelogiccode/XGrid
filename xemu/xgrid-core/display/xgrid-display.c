/*
 * XGrid host display sink.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * See display/xgrid-display.h. Windows publishes into a named file mapping;
 * other hosts use a POSIX shared-memory object. Both carry the same 32-byte
 * header and top-down BGRA frame, so the shell reads either one the same way.
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
#else
#include <fcntl.h>
#include <sys/mman.h>
#include <unistd.h>
#endif

#define XGRID_DISPLAY_MAGIC 0x53444758u /* 'XGDS' little endian */
#define XGRID_DISPLAY_HEADER 32u

static char g_configured_name[256];
static XGridDisplayFrameCallback g_frame_callback;
static void *g_frame_callback_opaque;

static void *g_view;
static uint32_t g_capacity;
static uint32_t g_width;
static uint32_t g_height;
static uint32_t g_stride;
static uint64_t g_frame;

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

#ifdef _WIN32

static HANDLE g_mapping;

static void platform_release_section(bool unlink_object)
{
    (void)unlink_object;

    if (g_view != NULL) {
        UnmapViewOfFile(g_view);
        g_view = NULL;
    }

    if (g_mapping != NULL) {
        CloseHandle(g_mapping);
        g_mapping = NULL;
    }

    g_capacity = 0;
}

static bool platform_map_section(uint32_t size, uint32_t width,
                                 uint32_t height, uint32_t stride)
{
    const char *name = sink_name();
    HANDLE mapping;
    void *view;

    (void)width;
    (void)height;
    (void)stride;

    if (name == NULL) {
        return false;
    }

    mapping = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0,
                                 size, name);
    if (mapping == NULL) {
        return false;
    }

    view = MapViewOfFile(mapping, FILE_MAP_WRITE, 0, 0, size);
    if (view == NULL) {
        CloseHandle(mapping);
        return false;
    }

    g_mapping = mapping;
    g_view = view;
    return true;
}

#else

static int g_fd = -1;
static char g_mapped_name[256];
static bool g_cleanup_registered;

static void platform_release_section(bool unlink_object)
{
    if (g_view != NULL) {
        munmap(g_view, g_capacity);
        g_view = NULL;
    }

    if (g_fd >= 0) {
        close(g_fd);
        g_fd = -1;
    }

    if (unlink_object && g_mapped_name[0] != '\0') {
        shm_unlink(g_mapped_name);
        g_mapped_name[0] = '\0';
    }

    g_capacity = 0;
}

static void platform_cleanup(void)
{
    platform_release_section(true);
}

static bool platform_map_section(uint32_t size, uint32_t width,
                                 uint32_t height, uint32_t stride)
{
    const char *name = sink_name();
    int fd;
    void *view;

    (void)width;
    (void)height;
    (void)stride;

    if (name == NULL) {
        return false;
    }

    if (!g_cleanup_registered) {
        atexit(platform_cleanup);
        g_cleanup_registered = true;
    }

    fd = shm_open(name, O_CREAT | O_RDWR, 0600);
    if (fd < 0) {
        return false;
    }

    if (ftruncate(fd, (off_t)size) != 0) {
        close(fd);
        return false;
    }

    view = mmap(NULL, size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
    if (view == MAP_FAILED) {
        close(fd);
        return false;
    }

    g_fd = fd;
    g_view = view;
    snprintf(g_mapped_name, sizeof(g_mapped_name), "%s", name);
    return true;
}

#endif /* _WIN32 */

void xgrid_display_set_sink(const char *name)
{
    if (name == NULL || name[0] == '\0') {
        g_configured_name[0] = '\0';
        return;
    }

    snprintf(g_configured_name, sizeof(g_configured_name), "%s", name);
}

bool xgrid_embed_enabled(void)
{
#ifdef _WIN32
    const char *value = getenv("XGRID_EMBED");
    return value != NULL && value[0] == '1';
#else
    return false;
#endif
}

bool xgrid_display_enabled(void)
{
    return sink_name() != NULL;
}

void xgrid_display_set_frame_callback(XGridDisplayFrameCallback callback,
                                      void *opaque)
{
    g_frame_callback = callback;
    g_frame_callback_opaque = opaque;
}

static bool map_section(uint32_t width, uint32_t height, uint32_t stride)
{
    uint32_t size;
    const char *name = sink_name();

    if (name == NULL || stride == 0) {
        return false;
    }

    if (height != 0 &&
        (uint64_t)stride > (0xFFFFFFFFu - XGRID_DISPLAY_HEADER) / height) {
        return false;
    }

    size = XGRID_DISPLAY_HEADER + stride * height;

    if (g_view != NULL && g_capacity >= size && g_width == width &&
        g_height == height) {
        return true;
    }

    if (g_view != NULL) {
        platform_release_section(false);
    }

    if (!platform_map_section(size, width, height, stride)) {
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
    const uint8_t *source = (const uint8_t *)pixels;
    uint8_t *base;

    if (pixels == NULL || width == 0 || height == 0 ||
        (uint64_t)stride < (uint64_t)width * 4) {
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

    /*
     * glReadPixels returns the framebuffer bottom-up (the GL origin is the
     * lower-left corner); the shell draws the pixels top-down, so the rows are
     * written in reverse. A whole-buffer memcpy here presents every frame
     * upside down - the boot animation hides it, a text overlay does not.
     */
    for (uint32_t row = 0; row < height; row++) {
        memcpy(base + XGRID_DISPLAY_HEADER + (size_t)row * stride,
               source + (size_t)(height - 1 - row) * stride,
               (size_t)width * 4);
    }

    /* Pixels first, then the counter, so a reader never sees a torn frame
     * without also seeing the matching count. */
    g_frame++;
    memcpy(base + 16, &g_frame, 8);

    if (g_frame_callback != NULL) {
        g_frame_callback(g_frame_callback_opaque);
    }
}
