/*
 * Geforce NV2A PGRAPH Direct3D 11 Renderer - presentation
 *
 * Copyright (c) 2012 espes
 * Copyright (c) 2015 Jannik Vogel
 * Copyright (c) 2018-2026 Matt Borgerson
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2 of the License, or (at your option) any later version.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, see <http://www.gnu.org/licenses/>.
 */

#ifndef COBJMACROS
#define COBJMACROS
#endif

#include "qemu/osdep.h"
#include "hw/xbox/nv2a/nv2a_int.h"
#include "hw/xbox/nv2a/pgraph/pgraph.h"
#include "hw/xbox/nv2a/pgraph/util.h"
#include "renderer.h"

#include <epoxy/gl.h>
#include "gloffscreen.h"

extern GloContext *g_nv2a_context_display;
extern GloContext *g_nv2a_context_render;

static uint8_t expand5(unsigned int v)
{
    return (uint8_t)((v << 3) | (v >> 2));
}

static uint8_t expand6(unsigned int v)
{
    return (uint8_t)((v << 2) | (v >> 4));
}

static uint32_t surface_pixel_to_bgra(DXGI_FORMAT format, const uint8_t *src)
{
    switch (format) {
    case DXGI_FORMAT_B8G8R8A8_UNORM:
        return *(const uint32_t *)src;
    case DXGI_FORMAT_B5G5R5A1_UNORM: {
        uint16_t v = *(const uint16_t *)src;
        unsigned int b = expand5(v & 0x1F);
        unsigned int g = expand5((v >> 5) & 0x1F);
        unsigned int r = expand5((v >> 10) & 0x1F);
        unsigned int a = (v & 0x8000) ? 0xFF : 0x00;
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
    case DXGI_FORMAT_B5G6R5_UNORM: {
        uint16_t v = *(const uint16_t *)src;
        unsigned int b = expand5(v & 0x1F);
        unsigned int g = expand6((v >> 5) & 0x3F);
        unsigned int r = expand5((v >> 11) & 0x1F);
        return (0xFFu << 24) | (r << 16) | (g << 8) | b;
    }
    case DXGI_FORMAT_R8_UNORM: {
        unsigned int v = src[0];
        return (0xFFu << 24) | (v << 16);
    }
    case DXGI_FORMAT_R8G8_UNORM: {
        unsigned int r = src[0];
        unsigned int g = src[1];
        return (0xFFu << 24) | (r << 16) | (g << 8);
    }
    case DXGI_FORMAT_D32_FLOAT: {
        float f = *(const float *)src;
        unsigned int v = (unsigned int)(MIN(MAX(f, 0.0f), 1.0f) * 255.0f);
        return (0xFFu << 24) | (v << 16) | (v << 8) | v;
    }
    default:
        return 0xFF000000u;
    }
}

static void pvideo_overlay(NV2AState *d, uint8_t *pixels, unsigned int width,
                           unsigned int height)
{
    PGRAPHState *pg = &d->pgraph;

    bool enabled = (d->pvideo.regs[NV_PVIDEO_BUFFER] & NV_PVIDEO_BUFFER_0_USE)
        && d->pvideo.regs[NV_PVIDEO_SIZE_IN] != 0xFFFFFFFF;
    if (!enabled) {
        return;
    }

    hwaddr base = d->pvideo.regs[NV_PVIDEO_BASE];
    hwaddr limit = d->pvideo.regs[NV_PVIDEO_LIMIT];
    hwaddr offset = d->pvideo.regs[NV_PVIDEO_OFFSET];

    int in_width =
        GET_MASK(d->pvideo.regs[NV_PVIDEO_SIZE_IN], NV_PVIDEO_SIZE_IN_WIDTH);
    int in_height =
        GET_MASK(d->pvideo.regs[NV_PVIDEO_SIZE_IN], NV_PVIDEO_SIZE_IN_HEIGHT);
    int in_pitch =
        GET_MASK(d->pvideo.regs[NV_PVIDEO_FORMAT], NV_PVIDEO_FORMAT_PITCH);
    int in_color =
        GET_MASK(d->pvideo.regs[NV_PVIDEO_FORMAT], NV_PVIDEO_FORMAT_COLOR);

    unsigned int out_width =
        GET_MASK(d->pvideo.regs[NV_PVIDEO_SIZE_OUT], NV_PVIDEO_SIZE_OUT_WIDTH);
    unsigned int out_height =
        GET_MASK(d->pvideo.regs[NV_PVIDEO_SIZE_OUT], NV_PVIDEO_SIZE_OUT_HEIGHT);
    unsigned int out_x =
        GET_MASK(d->pvideo.regs[NV_PVIDEO_POINT_OUT], NV_PVIDEO_POINT_OUT_X);
    unsigned int out_y =
        GET_MASK(d->pvideo.regs[NV_PVIDEO_POINT_OUT], NV_PVIDEO_POINT_OUT_Y);

    if (in_width <= 0 || in_height <= 0 || in_pitch <= 0 ||
        out_width == 0 || out_height == 0) {
        return;
    }

    assert(in_color == NV_PVIDEO_FORMAT_COLOR_LE_CR8YB8CB8YA8);
    assert(offset + in_pitch * in_height <= limit);
    if (base + offset + (hwaddr)in_pitch * in_height >
        memory_region_size(d->vram)) {
        return;
    }

    pgraph_apply_scaling_factor(pg, &out_x, &out_y);
    pgraph_apply_scaling_factor(pg, &out_width, &out_height);
    if (out_x >= width || out_y >= height) {
        return;
    }
    out_width = MIN(out_width, width - out_x);
    out_height = MIN(out_height, height - out_y);
    if (out_width == 0 || out_height == 0) {
        return;
    }

    bool color_key_enabled =
        GET_MASK(d->pvideo.regs[NV_PVIDEO_FORMAT], NV_PVIDEO_FORMAT_DISPLAY);
    unsigned int color_key = d->pvideo.regs[NV_PVIDEO_COLOR_KEY] & 0xFFFFFF;
    unsigned int key_r = GET_MASK(color_key, NV_PVIDEO_COLOR_KEY_RED);
    unsigned int key_g = GET_MASK(color_key, NV_PVIDEO_COLOR_KEY_GREEN);
    unsigned int key_b = GET_MASK(color_key, NV_PVIDEO_COLOR_KEY_BLUE);

    const uint8_t *source = d->vram_ptr + base + offset;

    for (unsigned int y = 0; y < out_height; y++) {
        const unsigned int sy = (unsigned int)((uint64_t)y * in_height /
                                               out_height);
        const uint8_t *line = source + (size_t)sy * in_pitch;
        uint8_t *dst = pixels + ((size_t)(out_y + y) * width + out_x) * 4;
        for (unsigned int x = 0; x < out_width; x++, dst += 4) {
            if (color_key_enabled &&
                dst[2] == key_r && dst[1] == key_g && dst[0] == key_b) {
                continue;
            }
            unsigned int sx = (unsigned int)((uint64_t)x * in_width /
                                             out_width);
            uint8_t r, g, b;
            convert_yuy2_to_rgb(line, sx, &r, &g, &b);
            dst[0] = b;
            dst[1] = g;
            dst[2] = r;
            dst[3] = 0xFF;
        }
    }
}

static bool ensure_display_staging(PGRAPHD3D11State *r,
                                   D3D11SurfaceBinding *surface)
{
    D3D11_TEXTURE2D_DESC desc;
    ID3D11Texture2D_GetDesc(surface->texture, &desc);
    desc.Usage = D3D11_USAGE_STAGING;
    desc.BindFlags = 0;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    desc.MiscFlags = 0;

    if (r->display_staging) {
        D3D11_TEXTURE2D_DESC existing;
        ID3D11Texture2D_GetDesc(r->display_staging, &existing);
        if (existing.Width == desc.Width && existing.Height == desc.Height &&
            existing.Format == desc.Format) {
            return true;
        }
        ID3D11Texture2D_Release(r->display_staging);
        r->display_staging = NULL;
    }

    HRESULT hr = ID3D11Device_CreateTexture2D(r->device, &desc, NULL,
                                              &r->display_staging);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 display staging creation failed "
                        "(hr=0x%08lx)\n", (unsigned long)hr);
        return false;
    }
    return true;
}

void pgraph_d3d11_present(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    static bool logged_first_present;

    VGADisplayParams vga_display_params;
    d->vga.get_params(&d->vga, &vga_display_params);

    D3D11SurfaceBinding *surface = pgraph_d3d11_surface_get_within(
        d, d->pcrtc.start + vga_display_params.line_offset);
    if (surface == NULL || !surface->color || !surface->width ||
        !surface->height) {
        return;
    }

    if (!logged_first_present) {
        logged_first_present = true;
        fprintf(stderr, "nv2a: Direct3D 11 presenting %ux%u surface\n",
                surface->width, surface->height);
    }

    pgraph_d3d11_upload_surface_data(
        d, surface, !tcg_enabled());

    if (!ensure_display_staging(r, surface)) {
        return;
    }

    ID3D11DeviceContext_CopyResource(
        r->context, (ID3D11Resource *)r->display_staging,
        (ID3D11Resource *)surface->texture);

    D3D11_MAPPED_SUBRESOURCE mapped;
    HRESULT hr = ID3D11DeviceContext_Map(
        r->context, (ID3D11Resource *)r->display_staging, 0, D3D11_MAP_READ,
        0, &mapped);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 display map failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return;
    }

    unsigned int width, height;
    d->vga.get_resolution(&d->vga, (int *)&width, (int *)&height);
    if (d->vga.cr[NV_PRMCIO_INTERLACE_MODE] !=
        NV_PRMCIO_INTERLACE_MODE_DISABLED) {
        height *= 2;
    }
    pgraph_apply_scaling_factor(pg, &width, &height);
    if (width == 0 || height == 0) {
        width = MAX(surface->width, 1);
        height = MAX(surface->height, 1);
    }

    D3D11_TEXTURE2D_DESC desc;
    ID3D11Texture2D_GetDesc(surface->texture, &desc);
    const unsigned int tex_w = desc.Width;
    const unsigned int tex_h = desc.Height;
    const unsigned int bpp = d3d11_surface_texel_size(&surface->fmt);

    uint8_t *pixels = g_malloc((size_t)width * height * 4);

    if (surface->fmt.format == DXGI_FORMAT_B8G8R8A8_UNORM &&
        width == tex_w && height == tex_h) {
        for (unsigned int y = 0; y < height; y++) {
            const uint8_t *src_row = (const uint8_t *)mapped.pData +
                                     (size_t)(tex_h - 1 - y) * mapped.RowPitch;
            memcpy(pixels + (size_t)y * width * 4, src_row,
                   (size_t)width * 4);
        }
    } else {
        for (unsigned int y = 0; y < height; y++) {
            unsigned int sy = (unsigned int)((uint64_t)y * tex_h / height);
            sy = MIN(sy, tex_h - 1);
            /* The texture stores guest row 0 at its bottom row. */
            const unsigned int src_y = tex_h - 1 - sy;
            const uint8_t *src_row =
                (const uint8_t *)mapped.pData + src_y * mapped.RowPitch;
            uint8_t *dst_row = pixels + (size_t)y * width * 4;
            for (unsigned int x = 0; x < width; x++) {
                unsigned int sx =
                    (unsigned int)((uint64_t)x * tex_w / width);
                sx = MIN(sx, tex_w - 1);
                uint32_t pixel = surface_pixel_to_bgra(surface->fmt.format,
                                                       src_row + sx * bpp);
                memcpy(dst_row + x * 4, &pixel, 4);
            }
        }
    }

    pvideo_overlay(d, pixels, width, height);

    ID3D11DeviceContext_Unmap(r->context,
                              (ID3D11Resource *)r->display_staging, 0);

    glo_set_current(g_nv2a_context_display);

    if (!r->gl_display_buffer) {
        glGenTextures(1, (GLuint *)&r->gl_display_buffer);
        glBindTexture(GL_TEXTURE_2D, r->gl_display_buffer);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        r->display_width = 0;
        r->display_height = 0;
    }

    glBindTexture(GL_TEXTURE_2D, r->gl_display_buffer);
    if (width != r->display_width || height != r->display_height) {
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_BGRA,
                     GL_UNSIGNED_BYTE, NULL);
        r->display_width = width;
        r->display_height = height;
    }
    /* Match the GL renderer's display texture orientation: the shared
     * presentation shader expects the guest's top row at texture v=1. */
    uint8_t *row_tmp = g_malloc((size_t)width * 4);
    for (unsigned int y = 0; y < height / 2; y++) {
        uint8_t *top = pixels + (size_t)y * width * 4;
        uint8_t *bottom = pixels + (size_t)(height - 1 - y) * width * 4;
        memcpy(row_tmp, top, (size_t)width * 4);
        memcpy(top, bottom, (size_t)width * 4);
        memcpy(bottom, row_tmp, (size_t)width * 4);
    }
    g_free(row_tmp);
    glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
    glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, width, height, GL_BGRA,
                    GL_UNSIGNED_BYTE, pixels);
    glFlush();

    glo_set_current(g_nv2a_context_render);

    g_free(pixels);
}

void pgraph_d3d11_init_display(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    r->display_staging = NULL;
    r->gl_display_buffer = 0;
    r->display_width = 0;
    r->display_height = 0;
    r->display_initialized = true;
}

void pgraph_d3d11_finalize_display(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (r->display_staging) {
        ID3D11Texture2D_Release(r->display_staging);
        r->display_staging = NULL;
    }

    if (r->gl_display_buffer) {
        glo_set_current(g_nv2a_context_display);
        GLuint texture = r->gl_display_buffer;
        glDeleteTextures(1, &texture);
        r->gl_display_buffer = 0;
        glo_set_current(g_nv2a_context_render);
    }
}

int pgraph_d3d11_get_framebuffer_surface(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    qemu_mutex_lock(&d->pfifo.lock);

    VGADisplayParams vga_display_params;
    d->vga.get_params(&d->vga, &vga_display_params);

    D3D11SurfaceBinding *surface = pgraph_d3d11_surface_get_within(
        d, d->pcrtc.start + vga_display_params.line_offset);
    if (surface == NULL || !surface->color) {
        qemu_mutex_unlock(&d->pfifo.lock);
        return 0;
    }

    surface->frame_time = pg->frame_time;
    qemu_event_reset(&d->pgraph.sync_complete);
    qatomic_set(&pg->sync_pending, true);
    pfifo_kick(d);
    qemu_mutex_unlock(&d->pfifo.lock);
    qemu_event_wait(&d->pgraph.sync_complete);

    return (int)r->gl_display_buffer;
}
