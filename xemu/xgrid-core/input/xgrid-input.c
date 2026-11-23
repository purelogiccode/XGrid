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
#define XGRID_INPUT_SIZE 32u

static HANDLE g_mapping;
static const uint8_t *g_view;
static bool g_announced;

static const char *source_name(void)
{
    static char name[256];
    const char *configured = getenv("XGRID_INPUT_SHM");

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

bool xgrid_input_read(uint32_t *buttons, int32_t *lx, int32_t *ly,
                      int32_t *rx, int32_t *ry, int32_t *lt, int32_t *rt)
{
    if (!ensure_open()) {
        return false;
    }

    uint32_t values[8];
    memcpy(values, g_view, sizeof(values));

    if (values[0] != XGRID_INPUT_MAGIC) {
        return false;
    }

    if (buttons != NULL) {
        *buttons = values[1];
    }

    if (lx != NULL) {
        *lx = (int32_t)values[2];
    }

    if (ly != NULL) {
        *ly = (int32_t)values[3];
    }

    if (rx != NULL) {
        *rx = (int32_t)values[4];
    }

    if (ry != NULL) {
        *ry = (int32_t)values[5];
    }

    if (lt != NULL) {
        *lt = (int32_t)values[6];
    }

    if (rt != NULL) {
        *rt = (int32_t)values[7];
    }

    if (!g_announced) {
        g_announced = true;
        fprintf(stderr, "xgrid input: port 1 reads the shell pad\n");
    }

    return true;
}

#else

bool xgrid_input_enabled(void)
{
    return false;
}

bool xgrid_input_read(uint32_t *buttons, int32_t *lx, int32_t *ly,
                      int32_t *rx, int32_t *ry, int32_t *lt, int32_t *rt)
{
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
