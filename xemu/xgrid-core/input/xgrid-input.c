/*
 * XGrid host input source.
 *
 * SPDX-License-Identifier: GPL-2.0-or-later
 *
 * See input/xgrid-input.h. The shell publishes the pad; port 1 reads it.
 */

#include "xgrid-input.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#define XGRID_INPUT_MAGIC 0x49444758u /* 'XGDI' little endian */
#define XGRID_INPUT_SIZE \
    (XGRID_INPUT_HEADER + XGRID_INPUT_STRIDE * XGRID_INPUT_PADS)

static HANDLE g_mapping;
static const uint8_t *g_view;
static bool g_announced;
static char g_configured_name[256];

void xgrid_input_set_sink(const char *name)
{
    if (name == NULL || name[0] == '\0') {
        g_configured_name[0] = '\0';
        return;
    }

    snprintf(g_configured_name, sizeof(g_configured_name), "%s", name);
}

static const char *source_name(void)
{
    static char name[256];
    const char *configured;

    if (g_configured_name[0] != '\0') {
        return g_configured_name;
    }

    configured = getenv("XGRID_INPUT_SHM");

    if (configured == NULL || configured[0] == '\0') {
        return NULL;
    }

    snprintf(name, sizeof(name), "%s", configured);
    return name;
}

bool xgrid_input_enabled(void)
{
    return source_name() != NULL;
}

static bool ensure_open(void)
{
    const char *name = source_name();

    if (g_view != NULL) {
        return true;
    }

    if (name == NULL) {
        return false;
    }

    g_mapping = OpenFileMappingA(FILE_MAP_READ, FALSE, name);
    if (g_mapping == NULL) {
        return false;
    }

    g_view = (const uint8_t *)MapViewOfFile(g_mapping, FILE_MAP_READ, 0, 0,
                                            XGRID_INPUT_SIZE);
    if (g_view == NULL) {
        CloseHandle(g_mapping);
        g_mapping = NULL;
        return false;
    }

    return true;
}

bool xgrid_input_read(int pad, uint32_t *buttons, int32_t *lx, int32_t *ly,
                      int32_t *rx, int32_t *ry, int32_t *lt, int32_t *rt)
{
    uint32_t header[4];
    uint32_t values[8];

    if (pad < 0 || pad >= XGRID_INPUT_PADS) {
        return false;
    }

    if (!ensure_open()) {
        return false;
    }

    memcpy(header, g_view, sizeof(header));
    if (header[0] != XGRID_INPUT_MAGIC ||
        header[1] != XGRID_INPUT_VERSION ||
        header[2] != XGRID_INPUT_PADS) {
        return false;
    }

    memcpy(values, g_view + XGRID_INPUT_HEADER + (size_t)pad * XGRID_INPUT_STRIDE,
           sizeof(values));

    if (buttons != NULL) {
        *buttons = values[0];
    }

    if (lx != NULL) {
        *lx = (int32_t)values[1];
    }

    if (ly != NULL) {
        *ly = (int32_t)values[2];
    }

    if (rx != NULL) {
        *rx = (int32_t)values[3];
    }

    if (ry != NULL) {
        *ry = (int32_t)values[4];
    }

    if (lt != NULL) {
        *lt = (int32_t)values[5];
    }

    if (rt != NULL) {
        *rt = (int32_t)values[6];
    }

    if (!g_announced) {
        g_announced = true;
        fprintf(stderr, "xgrid input: %d pads read the shell state\n",
                XGRID_INPUT_PADS);
    }

    return true;
}

#else

void xgrid_input_set_sink(const char *name)
{
    (void)name;
}

bool xgrid_input_enabled(void)
{
    return false;
}

bool xgrid_input_read(int pad, uint32_t *buttons, int32_t *lx, int32_t *ly,
                      int32_t *rx, int32_t *ry, int32_t *lt, int32_t *rt)
{
    (void)pad;
    (void)buttons;
    (void)lx;
    (void)ly;
    (void)rx;
    (void)ry;
    (void)lt;
    (void)rt;
    return false;
}

#endif /* _WIN32 */
