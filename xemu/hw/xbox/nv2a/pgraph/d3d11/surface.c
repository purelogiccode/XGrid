/*
 * Geforce NV2A PGRAPH Direct3D 11 Renderer
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
#include "hw/xbox/nv2a/pgraph/swizzle.h"
#include "hw/xbox/nv2a/pgraph/util.h"
#include "renderer.h"

/*
 * D3D11 textures are stored top-down, while the guest's surface rows start at
 * the top of the picture and the renderer maps guest y=0 to the bottom of clip
 * space. The GL backend gets the same result because GL texture row 0 is the
 * bottom row. To keep one convention in this backend, uploads write guest row
 * y to D3D rows (texture_height - 1 - y) and downloads read them back the same
 * way. Clears and scissors convert their rects with the same flip.
 */

static bool surface_scale_dimensions(PGRAPHState *pg, unsigned int width,
                                     unsigned int height, unsigned int *out_w,
                                     unsigned int *out_h)
{
    unsigned int w = MAX(width, 1) * pg->surface_scale_factor;
    unsigned int h = MAX(height, 1) * pg->surface_scale_factor;
    *out_w = w;
    *out_h = h;
    return width != 0 && height != 0;
}

static unsigned int surface_texel_size(const D3D11SurfaceFormatInfo *fmt)
{
    return fmt->float_depth ? 4 : fmt->bytes_per_pixel;
}

static void surface_get_dimensions(PGRAPHState *pg, unsigned int *width,
                                   unsigned int *height)
{
    bool swizzle = (pg->surface_type == NV097_SET_SURFACE_FORMAT_TYPE_SWIZZLE);
    if (swizzle) {
        *width = 1 << pg->surface_shape.log_width;
        *height = 1 << pg->surface_shape.log_height;
    } else {
        *width = pg->surface_shape.clip_width;
        *height = pg->surface_shape.clip_height;
    }
}

static bool framebuffer_dirty(PGRAPHState *pg)
{
    bool shape_changed = memcmp(&pg->surface_shape, &pg->last_surface_shape,
                                sizeof(SurfaceShape)) != 0;
    if (!shape_changed || (!pg->surface_shape.color_format
            && !pg->surface_shape.zeta_format)) {
        return false;
    }
    return true;
}

static bool check_surface_overlaps_range(const D3D11SurfaceBinding *surface,
                                         hwaddr range_start, hwaddr range_len)
{
    hwaddr surface_end = surface->vram_addr + surface->size;
    hwaddr range_end = range_start + range_len;
    return !(surface->vram_addr >= range_end || range_start >= surface_end);
}

static void surface_destroy(PGRAPHState *pg, D3D11SurfaceBinding *surface)
{
    if (surface->dsv) {
        ID3D11DepthStencilView_Release(surface->dsv);
        surface->dsv = NULL;
    }
    if (surface->rtv) {
        ID3D11RenderTargetView_Release(surface->rtv);
        surface->rtv = NULL;
    }
    if (surface->srv) {
        ID3D11ShaderResourceView_Release(surface->srv);
        surface->srv = NULL;
    }
    if (surface->staging) {
        ID3D11Texture2D_Release(surface->staging);
        surface->staging = NULL;
    }
    if (surface->texture) {
        ID3D11Texture2D_Release(surface->texture);
        surface->texture = NULL;
    }
    (void)pg;
}

static bool surface_create_views(NV2AState *d, D3D11SurfaceBinding *surface)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    unsigned int tex_w, tex_h;
    surface_scale_dimensions(pg, surface->width, surface->height, &tex_w,
                             &tex_h);
    static bool logged_first_surface;

    if (!logged_first_surface) {
        logged_first_surface = true;
        fprintf(stderr,
                "nv2a: Direct3D 11 created %s surface %ux%u fmt=%d at 0x%"
                HWADDR_PRIx "\n",
                surface->color ? "color" : "zeta", tex_w, tex_h,
                (int)surface->fmt.format, surface->vram_addr);
    }

    D3D11_TEXTURE2D_DESC desc = {
        .Width = tex_w,
        .Height = tex_h,
        .MipLevels = 1,
        .ArraySize = 1,
        .Format = surface->fmt.format,
        .SampleDesc = { 1, 0 },
        .Usage = D3D11_USAGE_DEFAULT,
        .BindFlags = surface->color
            ? (D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE)
            : D3D11_BIND_DEPTH_STENCIL,
        .CPUAccessFlags = 0,
        .MiscFlags = 0,
    };

    HRESULT hr = ID3D11Device_CreateTexture2D(r->device, &desc, NULL,
                                              &surface->texture);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 surface texture creation failed "
                        "(hr=0x%08lx, %ux%u fmt=%d)\n",
                (unsigned long)hr, tex_w, tex_h, desc.Format);
        return false;
    }

    if (surface->color) {
        hr = ID3D11Device_CreateRenderTargetView(r->device,
                                                 (ID3D11Resource *)surface->texture,
                                                 NULL, &surface->rtv);
    } else {
        hr = ID3D11Device_CreateDepthStencilView(r->device,
                                                 (ID3D11Resource *)surface->texture,
                                                 NULL, &surface->dsv);
    }
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 surface view creation failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return false;
    }

    if (surface->color) {
        D3D11_SHADER_RESOURCE_VIEW_DESC srv_desc = {
            .Format = surface->fmt.format,
            .ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D,
        };
        srv_desc.Texture2D.MostDetailedMip = 0;
        srv_desc.Texture2D.MipLevels = 1;
        hr = ID3D11Device_CreateShaderResourceView(
            r->device, (ID3D11Resource *)surface->texture, &srv_desc,
            &surface->srv);
        if (FAILED(hr)) {
            fprintf(stderr,
                    "nv2a: D3D11 surface SRV creation failed (hr=0x%08lx)\n",
                    (unsigned long)hr);
            return false;
        }
    }

    D3D11_TEXTURE2D_DESC staging_desc = desc;
    staging_desc.Usage = D3D11_USAGE_STAGING;
    staging_desc.BindFlags = 0;
    staging_desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ |
                                  D3D11_CPU_ACCESS_WRITE;
    hr = ID3D11Device_CreateTexture2D(r->device, &staging_desc, NULL,
                                      &surface->staging);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 surface staging creation failed "
                        "(hr=0x%08lx)\n", (unsigned long)hr);
        return false;
    }

    return true;
}

static void register_cpu_access_callback(NV2AState *d,
                                         D3D11SurfaceBinding *surface);
static void invalidate_overlapping_surfaces(NV2AState *d,
                                            D3D11SurfaceBinding *surface);

static D3D11SurfaceBinding *surface_put(NV2AState *d, hwaddr addr,
                                        D3D11SurfaceBinding *surface_in)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    assert(pgraph_d3d11_surface_get(d, addr) == NULL);

    D3D11SurfaceBinding *surface_out = g_malloc(sizeof(*surface_out));
    *surface_out = *surface_in;

    invalidate_overlapping_surfaces(d, surface_out);

    QTAILQ_INSERT_TAIL(&r->surfaces, surface_out, entry);

    register_cpu_access_callback(d, surface_out);

    return surface_out;
}

D3D11SurfaceBinding *pgraph_d3d11_surface_get(NV2AState *d, hwaddr addr)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    D3D11SurfaceBinding *surface;
    QTAILQ_FOREACH (surface, &r->surfaces, entry) {
        if (surface->vram_addr == addr) {
            return surface;
        }
    }
    return NULL;
}

D3D11SurfaceBinding *pgraph_d3d11_surface_get_within(NV2AState *d, hwaddr addr)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    D3D11SurfaceBinding *surface;
    QTAILQ_FOREACH (surface, &r->surfaces, entry) {
        if (addr >= surface->vram_addr &&
            addr < (surface->vram_addr + surface->size)) {
            return surface;
        }
    }
    return NULL;
}

void pgraph_d3d11_unbind_surface(NV2AState *d, bool color)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (color) {
        r->color_binding = NULL;
    } else {
        r->zeta_binding = NULL;
    }

    pgraph_d3d11_bind_surface_targets(d);
}

void pgraph_d3d11_surface_invalidate(NV2AState *d, D3D11SurfaceBinding *surface)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    trace_nv2a_pgraph_surface_invalidated(surface->vram_addr);

    if (surface == r->color_binding) {
        assert(d->pgraph.surface_color.buffer_dirty);
        pgraph_d3d11_unbind_surface(d, true);
    }
    if (surface == r->zeta_binding) {
        assert(d->pgraph.surface_zeta.buffer_dirty);
        pgraph_d3d11_unbind_surface(d, false);
    }

    if (tcg_enabled() && surface->access_cb) {
        mem_access_callback_remove_by_ref(qemu_get_cpu(0), surface->access_cb);
    }
    surface->access_cb = NULL;

    surface_destroy(pg, surface);

    QTAILQ_REMOVE(&r->surfaces, surface, entry);
    g_free(surface);
}

static void surface_copy_rows(uint8_t *dst, unsigned int dst_pitch,
                              const uint8_t *src, unsigned int src_pitch,
                              unsigned int row_bytes, unsigned int rows)
{
    for (unsigned int y = 0; y < rows; y++) {
        memcpy(dst + y * dst_pitch, src + y * src_pitch,
               MIN(row_bytes, MIN(dst_pitch, src_pitch)));
    }
}

static void surface_copy_expand_row(uint8_t *dst, const uint8_t *src,
                                    unsigned int width,
                                    unsigned int bytes_per_pixel,
                                    unsigned int factor)
{
    for (unsigned int x = 0; x < width; x++) {
        for (unsigned int i = 0; i < factor; i++) {
            memcpy(dst, src, bytes_per_pixel);
            dst += bytes_per_pixel;
        }
        src += bytes_per_pixel;
    }
}

static void surface_copy_shrink_row(uint8_t *dst, const uint8_t *src,
                                    unsigned int width,
                                    unsigned int bytes_per_pixel,
                                    unsigned int factor)
{
    for (unsigned int x = 0; x < width; x++) {
        memcpy(dst, src, bytes_per_pixel);
        dst += bytes_per_pixel;
        src += bytes_per_pixel * factor;
    }
}

static void surface_upload(NV2AState *d, D3D11SurfaceBinding *surface,
                           bool force)
{
    if (!(surface->upload_pending || force) || !surface->width ||
        !surface->height) {
        return;
    }

    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    unsigned int tex_w, tex_h;
    surface_scale_dimensions(pg, surface->width, surface->height, &tex_w,
                             &tex_h);

    nv2a_profile_inc_counter(NV2A_PROF_SURF_UPLOAD);

    const unsigned int bpp = surface->fmt.bytes_per_pixel;
    const unsigned int texel = surface_texel_size(&surface->fmt);
    const unsigned int row_bytes = surface->width * bpp;
    const unsigned int linear_pitch = surface->width * bpp;

    uint8_t *linear = (uint8_t *)d->vram_ptr + surface->vram_addr;
    unsigned int source_pitch = surface->pitch;
    uint8_t *unswizzled = NULL;
    if (surface->swizzle) {
        unswizzled = g_malloc(surface->size);
        unswizzle_rect(linear, surface->width, surface->height, unswizzled,
                       linear_pitch, bpp);
        linear = unswizzled;
        source_pitch = linear_pitch;
    }

    D3D11_MAPPED_SUBRESOURCE mapped;
    HRESULT hr = ID3D11DeviceContext_Map(r->context,
                                         (ID3D11Resource *)surface->staging,
                                         0, D3D11_MAP_WRITE, 0, &mapped);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 surface upload map failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        g_free(unswizzled);
        return;
    }

    const unsigned int scaled_row_bytes =
        surface->width * pg->surface_scale_factor * texel;
    uint8_t *row = g_malloc(MAX(scaled_row_bytes, 1));

    for (unsigned int y = 0; y < surface->height; y++) {
        const uint8_t *src = linear + y * source_pitch;

        if (surface->fmt.float_depth) {
            const uint16_t *src16 = (const uint16_t *)src;
            float *dstf = (float *)row;
            for (unsigned int x = 0; x < surface->width; x++) {
                float v = convert_f16_to_float(src16[x]) / f16_max;
                for (unsigned int i = 0; i < pg->surface_scale_factor; i++) {
                    *dstf++ = v;
                }
            }
        } else if (pg->surface_scale_factor == 1) {
            memcpy(row, src, row_bytes);
        } else {
            surface_copy_expand_row(row, src, surface->width, bpp,
                                    pg->surface_scale_factor);
        }

        for (unsigned int i = 0; i < pg->surface_scale_factor; i++) {
            unsigned int dst_y =
                tex_h - 1 - (y * pg->surface_scale_factor + i);
            memcpy((uint8_t *)mapped.pData + dst_y * mapped.RowPitch, row,
                   scaled_row_bytes);
        }
    }

    g_free(row);
    ID3D11DeviceContext_Unmap(r->context,
                              (ID3D11Resource *)surface->staging, 0);
    ID3D11DeviceContext_CopyResource(r->context,
                                     (ID3D11Resource *)surface->texture,
                                     (ID3D11Resource *)surface->staging);
    g_free(unswizzled);

    surface->upload_pending = false;
    surface->draw_time = pg->draw_time;

    trace_nv2a_pgraph_surface_upload(surface->color ? "COLOR" : "ZETA",
                                     surface->swizzle ? "sz" : "lin",
                                     surface->vram_addr, surface->width,
                                     surface->height, surface->pitch, bpp);
}

void pgraph_d3d11_upload_surface_data(NV2AState *d,
                                      D3D11SurfaceBinding *surface,
                                      bool force)
{
    surface_upload(d, surface, force);
}

static void surface_download(NV2AState *d, D3D11SurfaceBinding *surface,
                             bool force)
{
    if (!(surface->download_pending || force) || !surface->width ||
        !surface->height) {
        return;
    }

    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    unsigned int tex_w, tex_h;
    surface_scale_dimensions(pg, surface->width, surface->height, &tex_w,
                             &tex_h);

    nv2a_profile_inc_counter(NV2A_PROF_SURF_DOWNLOAD);

    ID3D11DeviceContext_CopyResource(r->context,
                                     (ID3D11Resource *)surface->staging,
                                     (ID3D11Resource *)surface->texture);

    D3D11_MAPPED_SUBRESOURCE mapped;
    HRESULT hr = ID3D11DeviceContext_Map(r->context,
                                         (ID3D11Resource *)surface->staging,
                                         0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 surface download map failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return;
    }

    const unsigned int bpp = surface->fmt.bytes_per_pixel;
    const unsigned int row_bytes = surface->width * bpp;
    const unsigned int linear_pitch = surface->pitch;
    uint8_t *linear = g_malloc(surface->size);
    const unsigned int factor = pg->surface_scale_factor;

    for (unsigned int y = 0; y < surface->height; y++) {
        unsigned int src_y = tex_h - 1 - y * factor;
        const uint8_t *src =
            (const uint8_t *)mapped.pData + src_y * mapped.RowPitch;

        if (surface->fmt.float_depth) {
            uint16_t *dst16 = (uint16_t *)(linear + y * linear_pitch);
            const float *srcf = (const float *)src;
            for (unsigned int x = 0; x < surface->width; x++) {
                float v = srcf[x * factor] * f16_max;
                uint32_t f16 = MIN((uint32_t)MAX(v, 0.0f), 0xFFFFu);
                dst16[x] = (uint16_t)f16;
            }
        } else if (factor == 1) {
            memcpy(linear + y * linear_pitch, src, row_bytes);
        } else {
            surface_copy_shrink_row(linear + y * linear_pitch, src,
                                    surface->width, bpp, factor);
        }
    }

    ID3D11DeviceContext_Unmap(r->context,
                              (ID3D11Resource *)surface->staging, 0);

    uint8_t *dest = d->vram_ptr + surface->vram_addr;
    if (surface->swizzle) {
        swizzle_rect(linear, surface->width, surface->height, dest,
                     surface->pitch, bpp);
    } else {
        surface_copy_rows(dest, surface->pitch, linear, linear_pitch,
                          row_bytes, surface->height);
    }
    g_free(linear);

    memory_region_set_client_dirty(d->vram, surface->vram_addr,
                                   surface->pitch * surface->height,
                                   DIRTY_MEMORY_VGA);
    memory_region_set_client_dirty(d->vram, surface->vram_addr,
                                   surface->pitch * surface->height,
                                   DIRTY_MEMORY_NV2A_TEX);

    surface->download_pending = false;
    surface->draw_dirty = false;

    trace_nv2a_pgraph_surface_download(surface->color ? "COLOR" : "ZETA",
                                       surface->swizzle ? "sz" : "lin",
                                       surface->vram_addr, surface->width,
                                       surface->height, surface->pitch, bpp);
}

void pgraph_d3d11_surface_download_if_dirty(NV2AState *d,
                                            D3D11SurfaceBinding *surface)
{
    if (surface->draw_dirty) {
        surface_download(d, surface, true);
    }
}

void pgraph_d3d11_process_pending_downloads(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    D3D11SurfaceBinding *surface;
    QTAILQ_FOREACH (surface, &r->surfaces, entry) {
        surface_download(d, surface, false);
    }

    qatomic_set(&r->downloads_pending, false);
    qemu_event_set(&r->downloads_complete);
}

void pgraph_d3d11_download_dirty_surfaces(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    D3D11SurfaceBinding *surface;
    QTAILQ_FOREACH (surface, &r->surfaces, entry) {
        pgraph_d3d11_surface_download_if_dirty(d, surface);
    }

    qatomic_set(&r->download_dirty_surfaces_pending, false);
    qemu_event_set(&r->dirty_surfaces_download_complete);
}

unsigned int pgraph_d3d11_mark_surfaces_in_range_for_upload(
    NV2AState *d, hwaddr start, hwaddr size)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    unsigned int num_marked = 0;
    D3D11SurfaceBinding *surface;
    QTAILQ_FOREACH (surface, &r->surfaces, entry) {
        if (check_surface_overlaps_range(surface, start, size)) {
            surface->upload_pending = true;
            ++num_marked;
        }
    }
    return num_marked;
}

void pgraph_d3d11_download_surfaces_in_range_if_dirty(PGRAPHState *pg,
                                                      hwaddr start,
                                                      hwaddr size)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    NV2AState *d = container_of(pg, NV2AState, pgraph);

    D3D11SurfaceBinding *surface;
    QTAILQ_FOREACH (surface, &r->surfaces, entry) {
        if (check_surface_overlaps_range(surface, start, size)) {
            pgraph_d3d11_surface_download_if_dirty(d, surface);
        }
    }
}

static void surface_access_callback(void *opaque, MemoryRegion *mr, hwaddr addr,
                                    hwaddr len, bool write)
{
    NV2AState *d = (NV2AState *)opaque;
    qemu_mutex_lock(&d->pgraph.lock);

    PGRAPHD3D11State *r = d->pgraph.d3d11_renderer_state;
    bool wait_for_downloads = false;

    D3D11SurfaceBinding *surface;
    QTAILQ_FOREACH (surface, &r->surfaces, entry) {
        if (!check_surface_overlaps_range(surface, addr, len)) {
            continue;
        }

        if (surface->draw_dirty) {
            surface->download_pending = true;
            wait_for_downloads = true;
        }
        if (write) {
            surface->upload_pending = true;
        }
    }

    qemu_mutex_unlock(&d->pgraph.lock);

    if (wait_for_downloads) {
        qemu_mutex_lock(&d->pfifo.lock);
        qemu_event_reset(&r->downloads_complete);
        qatomic_set(&r->downloads_pending, true);
        pfifo_kick(d);
        qemu_mutex_unlock(&d->pfifo.lock);
        qemu_event_wait(&r->downloads_complete);
    }
}

static void register_cpu_access_callback(NV2AState *d,
                                         D3D11SurfaceBinding *surface)
{
    if (tcg_enabled() && surface->width && surface->height) {
        surface->access_cb = mem_access_callback_insert(
            qemu_get_cpu(0), d->vram, surface->vram_addr, surface->size,
            &surface_access_callback, d);
    } else {
        surface->access_cb = NULL;
    }
}

static void invalidate_overlapping_surfaces(NV2AState *d,
                                            D3D11SurfaceBinding *surface)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    D3D11SurfaceBinding *other, *next;
    QTAILQ_FOREACH_SAFE(other, &r->surfaces, entry, next) {
        if (check_surface_overlaps_range(surface, other->vram_addr,
                                         other->size)) {
            trace_nv2a_pgraph_surface_evict_overlapping(
                other->vram_addr, other->width, other->height, other->pitch);
            pgraph_d3d11_surface_download_if_dirty(d, other);
            pgraph_d3d11_surface_invalidate(d, other);
        }
    }
}

static void surface_evict_old(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    const int surface_age_limit = 5;

    D3D11SurfaceBinding *s, *next;
    QTAILQ_FOREACH_SAFE(s, &r->surfaces, entry, next) {
        int last_used = pg->frame_time - s->frame_time;
        if (last_used >= surface_age_limit) {
            trace_nv2a_pgraph_surface_evict_reason("old", s->vram_addr);
            pgraph_d3d11_surface_download_if_dirty(d, s);
            pgraph_d3d11_surface_invalidate(d, s);
        }
    }
}

static bool check_surface_compatibility(const D3D11SurfaceBinding *s1,
                                        const D3D11SurfaceBinding *s2,
                                        bool strict)
{
    bool format_compatible = (s1->color == s2->color) &&
                             (s1->fmt.format == s2->fmt.format) &&
                             (s1->pitch == s2->pitch);
    if (!format_compatible) {
        return false;
    }

    if (!strict) {
        return (s1->width >= s2->width) && (s1->height >= s2->height);
    }
    return (s1->width == s2->width) && (s1->height == s2->height);
}

static void populate_surface_binding_entry_sized(NV2AState *d, bool color,
                                                 unsigned int width,
                                                 unsigned int height,
                                                 D3D11SurfaceBinding *entry)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    Surface *surface;
    hwaddr dma_address;
    D3D11SurfaceFormatInfo fmt;

    if (color) {
        surface = &pg->surface_color;
        dma_address = pg->dma_color;
        assert(pg->surface_shape.color_format != 0);
        assert(pg->surface_shape.color_format <
               ARRAY_SIZE(kelvin_surface_color_format_d3d11_map));
        fmt = kelvin_surface_color_format_d3d11_map[pg->surface_shape.color_format];
        if (fmt.bytes_per_pixel == 0) {
            fprintf(stderr, "nv2a: unimplemented D3D11 color surface format 0x%x\n",
                    pg->surface_shape.color_format);
            assert(!"unimplemented color surface format");
        }
    } else {
        surface = &pg->surface_zeta;
        dma_address = pg->dma_zeta;
        assert(pg->surface_shape.zeta_format != 0);
        assert(pg->surface_shape.zeta_format <
               ARRAY_SIZE(kelvin_surface_zeta_fixed_format_d3d11_map));
        const D3D11SurfaceFormatInfo *map =
            pg->surface_shape.z_format
                ? kelvin_surface_zeta_float_format_d3d11_map
                : kelvin_surface_zeta_fixed_format_d3d11_map;
        fmt = map[pg->surface_shape.zeta_format];
    }

    DMAObject dma = nv_dma_load(d, dma_address);
    assert(dma.dma_class == NV_DMA_IN_MEMORY_CLASS);
    assert(surface->offset <= dma.limit);
    assert(surface->offset + surface->pitch * height <= dma.limit + 1);
    assert(surface->pitch % fmt.bytes_per_pixel == 0);
    assert((dma.address & ~0x07FFFFFF) == 0);

    memset(entry, 0, sizeof(*entry));
    entry->shape = (color || !r->color_binding) ? pg->surface_shape :
                                                  r->color_binding->shape;
    entry->fmt = fmt;
    entry->color = color;
    entry->swizzle = (pg->surface_type == NV097_SET_SURFACE_FORMAT_TYPE_SWIZZLE);
    entry->vram_addr = dma.address + surface->offset;
    entry->width = width;
    entry->height = height;
    entry->pitch = surface->pitch;
    entry->size = height * MAX(surface->pitch, width * fmt.bytes_per_pixel);
    entry->upload_pending = true;
    entry->download_pending = false;
    entry->draw_dirty = false;
    entry->dma_addr = dma.address;
    entry->dma_len = dma.limit;
    entry->frame_time = pg->frame_time;
    entry->draw_time = pg->draw_time;
    entry->cleared = false;
}

static void populate_surface_binding_entry(NV2AState *d, bool color,
                                           D3D11SurfaceBinding *entry)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    unsigned int width, height;

    if (color || !r->color_binding) {
        surface_get_dimensions(pg, &width, &height);
        pgraph_apply_anti_aliasing_factor(pg, &width, &height);
        if (pg->surface_type != NV097_SET_SURFACE_FORMAT_TYPE_SWIZZLE) {
            width += pg->surface_shape.clip_x;
            height += pg->surface_shape.clip_y;
        }
    } else {
        width = r->color_binding->width;
        height = r->color_binding->height;
    }

    populate_surface_binding_entry_sized(d, color, width, height, entry);
}

void pgraph_d3d11_bind_surface_targets(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    ID3D11RenderTargetView *rtv =
        r->color_binding ? r->color_binding->rtv : NULL;
    ID3D11DepthStencilView *dsv =
        r->zeta_binding ? r->zeta_binding->dsv : NULL;
    UINT num_views = rtv ? 1 : 0;

    ID3D11DeviceContext_OMSetRenderTargets(r->context, num_views,
                                           num_views ? &rtv : NULL, dsv);
}

static void update_surface_part(NV2AState *d, bool upload, bool color)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    D3D11SurfaceBinding entry;
    populate_surface_binding_entry(d, color, &entry);

    Surface *surface = color ? &pg->surface_color : &pg->surface_zeta;

    bool mem_dirty = !tcg_enabled() && memory_region_test_and_clear_dirty(
                                           d->vram, entry.vram_addr, entry.size,
                                           DIRTY_MEMORY_NV2A);

    if (upload && (surface->buffer_dirty || mem_dirty)) {
        pgraph_d3d11_unbind_surface(d, color);

        D3D11SurfaceBinding *found =
            pgraph_d3d11_surface_get(d, entry.vram_addr);
        if (found != NULL) {
            D3D11SurfaceBinding *other =
                color ? r->zeta_binding : r->color_binding;
            if (found == other) {
                NV2A_UNIMPLEMENTED("Same color & zeta surface offset");
                pgraph_d3d11_unbind_surface(d, !color);
            }
        }

        bool should_create = true;

        if (found != NULL) {
            bool is_compatible = check_surface_compatibility(found, &entry, false);
            if (found->swizzle != entry.swizzle) {
                is_compatible &= (pg->clearing || found->cleared) &&
                                 check_surface_compatibility(found, &entry, true);
            }

            if (is_compatible && color &&
                !check_surface_compatibility(found, &entry, true)) {
                D3D11SurfaceBinding zeta_entry;
                populate_surface_binding_entry_sized(d, !color, found->width,
                                                     found->height,
                                                     &zeta_entry);
                hwaddr color_end = found->vram_addr + found->size;
                hwaddr zeta_end = zeta_entry.vram_addr + zeta_entry.size;
                is_compatible &= found->vram_addr >= zeta_end ||
                                 zeta_entry.vram_addr >= color_end;
            }

            if (is_compatible && !color && r->color_binding) {
                is_compatible &= (found->width == r->color_binding->width) &&
                                 (found->height == r->color_binding->height);
            }

            if (is_compatible) {
                pg->surface_binding_dim.width = found->width;
                pg->surface_binding_dim.clip_x = found->shape.clip_x;
                pg->surface_binding_dim.clip_width = found->shape.clip_width;
                pg->surface_binding_dim.height = found->height;
                pg->surface_binding_dim.clip_y = found->shape.clip_y;
                pg->surface_binding_dim.clip_height = found->shape.clip_height;
                found->upload_pending |= mem_dirty;
                pg->surface_zeta.buffer_dirty |= color;
                should_create = false;
            } else {
                pgraph_d3d11_surface_download_if_dirty(d, found);
                pgraph_d3d11_surface_invalidate(d, found);
            }
        }

        if (should_create) {
            if (!surface_create_views(d, &entry)) {
                return;
            }
            found = surface_put(d, entry.vram_addr, &entry);

            pg->surface_binding_dim.width = entry.width;
            pg->surface_binding_dim.clip_x = entry.shape.clip_x;
            pg->surface_binding_dim.clip_width = entry.shape.clip_width;
            pg->surface_binding_dim.height = entry.height;
            pg->surface_binding_dim.clip_y = entry.shape.clip_y;
            pg->surface_binding_dim.clip_height = entry.shape.clip_height;

            if (color && r->zeta_binding &&
                (r->zeta_binding->width != entry.width ||
                 r->zeta_binding->height != entry.height)) {
                pg->surface_zeta.buffer_dirty = true;
            }
        }

        if (color) {
            r->color_binding = found;
        } else {
            r->zeta_binding = found;
        }

        pgraph_d3d11_bind_surface_targets(d);
        surface->buffer_dirty = false;
    }

    if (!upload && surface->draw_dirty) {
        D3D11SurfaceBinding *binding =
            color ? r->color_binding : r->zeta_binding;
        if (!tcg_enabled() && binding) {
            surface_download(d, binding, true);
        }
        surface->write_enabled_cache = false;
        surface->draw_dirty = false;
    }
}

void pgraph_d3d11_surface_update(NV2AState *d, bool upload, bool color_write,
                                 bool zeta_write)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    pg->surface_shape.z_format =
        GET_MASK(pgraph_reg_r(pg, NV_PGRAPH_SETUPRASTER),
                 NV_PGRAPH_SETUPRASTER_Z_FORMAT);

    color_write = color_write &&
                  (pg->clearing || pgraph_color_write_enabled(pg));
    zeta_write = zeta_write && (pg->clearing || pgraph_zeta_write_enabled(pg));

    if (upload) {
        bool fb_dirty = framebuffer_dirty(pg);
        if (fb_dirty) {
            memcpy(&pg->last_surface_shape, &pg->surface_shape,
                   sizeof(SurfaceShape));
            pg->surface_color.buffer_dirty = true;
            pg->surface_zeta.buffer_dirty = true;
        }

        if (pg->surface_color.buffer_dirty) {
            pgraph_d3d11_unbind_surface(d, true);
        }
        if (color_write) {
            update_surface_part(d, true, true);
        }
        if (pg->surface_zeta.buffer_dirty) {
            pgraph_d3d11_unbind_surface(d, false);
        }
        if (zeta_write) {
            update_surface_part(d, true, false);
        }
    } else {
        if ((color_write || pg->surface_color.write_enabled_cache) &&
            pg->surface_color.draw_dirty) {
            update_surface_part(d, false, true);
        }
        if ((zeta_write || pg->surface_zeta.write_enabled_cache) &&
            pg->surface_zeta.draw_dirty) {
            update_surface_part(d, false, false);
        }
    }

    if (upload) {
        pg->draw_time++;
    }

    bool swizzle = (pg->surface_type == NV097_SET_SURFACE_FORMAT_TYPE_SWIZZLE);

    if (r->color_binding) {
        r->color_binding->frame_time = pg->frame_time;
        if (upload) {
            surface_upload(d, r->color_binding, false);
            r->color_binding->draw_time = pg->draw_time;
            r->color_binding->swizzle = swizzle;
        }
    }

    if (r->zeta_binding) {
        r->zeta_binding->frame_time = pg->frame_time;
        if (upload) {
            surface_upload(d, r->zeta_binding, false);
            r->zeta_binding->draw_time = pg->draw_time;
            r->zeta_binding->swizzle = swizzle;
        }
    }

    if (r->color_binding && r->zeta_binding) {
        assert((r->color_binding->width == r->zeta_binding->width) &&
               (r->color_binding->height == r->zeta_binding->height));
    }

    surface_evict_old(d);
}

static uint8_t clear_unorm8(float value)
{
    if (!(value > 0.0f)) {
        return 0;
    }
    if (value >= 1.0f) {
        return 255;
    }
    return (uint8_t)(value * 255.0f + 0.5f);
}

static uint16_t clear_unorm_bits(float value, unsigned int bits)
{
    const uint32_t max = (1u << bits) - 1;

    if (!(value > 0.0f)) {
        return 0;
    }
    if (value >= 1.0f) {
        return (uint16_t)max;
    }
    return (uint16_t)(value * (float)max + 0.5f);
}

static void clear_color_pixels(uint8_t *data, size_t row_bytes,
                               DXGI_FORMAT format, const D3D11_RECT *rect,
                               unsigned int width, unsigned int height,
                               const float rgba[4], uint32_t parameter)
{
    const bool write_r = parameter & NV097_CLEAR_SURFACE_R;
    const bool write_g = parameter & NV097_CLEAR_SURFACE_G;
    const bool write_b = parameter & NV097_CLEAR_SURFACE_B;
    const bool write_a = parameter & NV097_CLEAR_SURFACE_A;

    const uint8_t r8 = clear_unorm8(rgba[0]);
    const uint8_t g8 = clear_unorm8(rgba[1]);
    const uint8_t b8 = clear_unorm8(rgba[2]);
    const uint8_t a8 = clear_unorm8(rgba[3]);
    const uint16_t r5 = clear_unorm_bits(rgba[0], 5);
    const uint16_t g5 = clear_unorm_bits(rgba[1], 5);
    const uint16_t b5 = clear_unorm_bits(rgba[2], 5);
    const uint16_t g6 = clear_unorm_bits(rgba[1], 6);

    const LONG left = MAX(rect->left, 0);
    const LONG top = MAX(rect->top, 0);
    const LONG right = MIN(rect->right, (LONG)width);
    const LONG bottom = MIN(rect->bottom, (LONG)height);

    for (LONG y = top; y < bottom; y++) {
        uint8_t *row = data + (size_t)y * row_bytes;

        for (LONG x = left; x < right; x++) {
            switch (format) {
            case DXGI_FORMAT_B8G8R8A8_UNORM: {
                uint8_t *pixel = row + (size_t)x * 4;
                if (write_b) pixel[0] = b8;
                if (write_g) pixel[1] = g8;
                if (write_r) pixel[2] = r8;
                if (write_a) pixel[3] = a8;
                break;
            }
            case DXGI_FORMAT_B5G5R5A1_UNORM: {
                uint16_t *pixel = (uint16_t *)row + x;
                uint16_t value;
                memcpy(&value, pixel, sizeof(value));
                if (write_b) value = (value & ~0x001Fu) | b5;
                if (write_g) value = (value & ~0x03E0u) | (g5 << 5);
                if (write_r) value = (value & ~0x7C00u) | (r5 << 10);
                if (write_a) {
                    value = (value & ~0x8000u) |
                            (rgba[3] >= 0.5f ? 0x8000u : 0);
                }
                memcpy(pixel, &value, sizeof(value));
                break;
            }
            case DXGI_FORMAT_B5G6R5_UNORM: {
                uint16_t *pixel = (uint16_t *)row + x;
                uint16_t value;
                memcpy(&value, pixel, sizeof(value));
                if (write_b) value = (value & ~0x001Fu) | b5;
                if (write_g) value = (value & ~0x07E0u) | (g6 << 5);
                if (write_r) value = (value & ~0xF800u) | (r5 << 11);
                memcpy(pixel, &value, sizeof(value));
                break;
            }
            case DXGI_FORMAT_R8_UNORM:
                if (write_r) row[x] = r8;
                break;
            case DXGI_FORMAT_R8G8_UNORM: {
                uint8_t *pixel = row + (size_t)x * 2;
                if (write_r) pixel[0] = r8;
                if (write_g) pixel[1] = g8;
                break;
            }
            default:
                break;
            }
        }
    }
}

/*
 * ClearRenderTargetView and ClearView cannot apply the guest's per-channel
 * clear mask, and ClearRenderTargetView also ignores the clear rectangle. Use
 * the staging copy for those cases: read the target, patch the requested
 * channels in the rectangle, and write it back.
 */
static void clear_color_rect(NV2AState *d, D3D11SurfaceBinding *surface,
                             const D3D11_RECT *rect, const float rgba[4],
                             uint32_t parameter)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    D3D11_TEXTURE2D_DESC desc;
    ID3D11Texture2D_GetDesc(surface->texture, &desc);

    const LONG left = MAX(rect->left, 0);
    const LONG top = MAX(rect->top, 0);
    const LONG right = MIN(rect->right, (LONG)desc.Width);
    const LONG bottom = MIN(rect->bottom, (LONG)desc.Height);
    if (left >= right || top >= bottom) {
        return;
    }

    const unsigned int bpp = surface->fmt.bytes_per_pixel;
    const unsigned int width = (unsigned int)(right - left);
    const unsigned int height = (unsigned int)(bottom - top);
    const size_t row_bytes = (size_t)width * bpp;
    const size_t size = row_bytes * height;

    D3D11_BOX box = {
        .left = left,
        .top = top,
        .front = 0,
        .right = right,
        .bottom = bottom,
        .back = 1,
    };
    ID3D11DeviceContext_CopySubresourceRegion(
        r->context, (ID3D11Resource *)surface->staging, 0, left, top, 0,
        (ID3D11Resource *)surface->texture, 0, &box);

    D3D11_MAPPED_SUBRESOURCE mapped;
    HRESULT hr = ID3D11DeviceContext_Map(r->context,
                                         (ID3D11Resource *)surface->staging,
                                         0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 clear readback failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return;
    }

    uint8_t *data = g_malloc(size);
    for (unsigned int y = 0; y < height; y++) {
        memcpy(data + (size_t)y * row_bytes,
               (const uint8_t *)mapped.pData +
                   (size_t)(top + y) * mapped.RowPitch + (size_t)left * bpp,
               row_bytes);
    }
    ID3D11DeviceContext_Unmap(r->context,
                              (ID3D11Resource *)surface->staging, 0);

    const D3D11_RECT local_rect = {
        .left = 0,
        .top = 0,
        .right = (LONG)width,
        .bottom = (LONG)height,
    };
    clear_color_pixels(data, row_bytes, surface->fmt.format, &local_rect, width,
                       height, rgba, parameter);

    hr = ID3D11DeviceContext_Map(r->context,
                                 (ID3D11Resource *)surface->staging, 0,
                                 D3D11_MAP_WRITE, 0, &mapped);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 clear writeback failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        g_free(data);
        return;
    }
    for (unsigned int y = 0; y < height; y++) {
        memcpy((uint8_t *)mapped.pData +
                   (size_t)(top + y) * mapped.RowPitch + (size_t)left * bpp,
               data + (size_t)y * row_bytes, row_bytes);
    }
    ID3D11DeviceContext_Unmap(r->context,
                              (ID3D11Resource *)surface->staging, 0);
    g_free(data);

    ID3D11DeviceContext_CopySubresourceRegion(
        r->context, (ID3D11Resource *)surface->texture, 0, left, top, 0,
        (ID3D11Resource *)surface->staging, 0, &box);
}

static bool clear_rect_covers_texture(const D3D11_RECT *rect,
                                      unsigned int width,
                                      unsigned int height)
{
    return rect->left <= 0 && rect->top <= 0 && rect->right >= (LONG)width &&
           rect->bottom >= (LONG)height;
}

void pgraph_d3d11_clear_surface(NV2AState *d, uint32_t parameter)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    pg->clearing = true;

    bool write_color = (parameter & NV097_CLEAR_SURFACE_COLOR);
    bool write_zeta =
        (parameter & (NV097_CLEAR_SURFACE_Z | NV097_CLEAR_SURFACE_STENCIL));

    pgraph_d3d11_surface_update(d, true, write_color, write_zeta);

    if (!r->color_binding && !r->zeta_binding) {
        pg->clearing = false;
        return;
    }

    unsigned int xmin = GET_MASK(pgraph_reg_r(pg, NV_PGRAPH_CLEARRECTX),
                                 NV_PGRAPH_CLEARRECTX_XMIN);
    unsigned int xmax = GET_MASK(pgraph_reg_r(pg, NV_PGRAPH_CLEARRECTX),
                                 NV_PGRAPH_CLEARRECTX_XMAX);
    unsigned int ymin = GET_MASK(pgraph_reg_r(pg, NV_PGRAPH_CLEARRECTY),
                                 NV_PGRAPH_CLEARRECTY_YMIN);
    unsigned int ymax = GET_MASK(pgraph_reg_r(pg, NV_PGRAPH_CLEARRECTY),
                                 NV_PGRAPH_CLEARRECTY_YMAX);

    unsigned int width = xmax - xmin + 1;
    unsigned int height = ymax - ymin + 1;
    pgraph_apply_anti_aliasing_factor(pg, &xmin, &ymin);
    pgraph_apply_anti_aliasing_factor(pg, &width, &height);

    /* Only a full clear lets the next linear/swizzle surface reuse skip the
     * writeback; a partial clear leaves content that must reach VRAM. */
    const bool full_clear =
        !xmin && !ymin && width >= pg->surface_binding_dim.width &&
        height >= pg->surface_binding_dim.height;

    pgraph_apply_scaling_factor(pg, &xmin, &ymin);
    pgraph_apply_scaling_factor(pg, &width, &height);

    D3D11SurfaceBinding *target = write_color && r->color_binding
                                      ? r->color_binding
                                      : r->zeta_binding;
    if (!target) {
        pg->clearing = false;
        return;
    }
    unsigned int tex_w, tex_h;
    surface_scale_dimensions(pg, target->width, target->height, &tex_w,
                             &tex_h);

    D3D11_RECT rect = {
        .left = xmin,
        .top = (LONG)(tex_h - (ymin + height)),
        .right = xmin + width,
        .bottom = tex_h - ymin,
    };

    const bool all_color_channels =
        (parameter & NV097_CLEAR_SURFACE_COLOR) ==
        (NV097_CLEAR_SURFACE_R | NV097_CLEAR_SURFACE_G |
         NV097_CLEAR_SURFACE_B | NV097_CLEAR_SURFACE_A);

    if (write_color && r->color_binding) {
        float rgba[4];
        pgraph_get_clear_color(pg, rgba);

        bool cleared = false;

        if (all_color_channels && r->context1) {
            const FLOAT color[4] = { rgba[0], rgba[1], rgba[2], rgba[3] };
            ID3D11DeviceContext1_ClearView(r->context1,
                                           (ID3D11View *)r->color_binding->rtv,
                                           color, &rect, 1);
            cleared = true;
        } else if (all_color_channels &&
                   clear_rect_covers_texture(&rect, tex_w, tex_h)) {
            const FLOAT color[4] = { rgba[0], rgba[1], rgba[2], rgba[3] };
            ID3D11DeviceContext_ClearRenderTargetView(r->context,
                                                      r->color_binding->rtv,
                                                      color);
            cleared = true;
        }

        if (!cleared) {
            clear_color_rect(d, r->color_binding, &rect, rgba, parameter);
        }
    }

    if (write_zeta && r->zeta_binding) {
        float depth;
        int stencil;
        UINT flags = 0;
        pgraph_get_clear_depth_stencil_value(pg, &depth, &stencil);
        if (parameter & NV097_CLEAR_SURFACE_Z) {
            flags |= D3D11_CLEAR_DEPTH;
        }
        if (parameter & NV097_CLEAR_SURFACE_STENCIL) {
            flags |= D3D11_CLEAR_STENCIL;
        }
        if (r->context1 && flags == (D3D11_CLEAR_DEPTH | D3D11_CLEAR_STENCIL)) {
            const FLOAT clear[4] = { depth, (FLOAT)stencil, 0.0f, 0.0f };
            ID3D11DeviceContext1_ClearView(r->context1,
                                           (ID3D11View *)r->zeta_binding->dsv,
                                           clear, &rect, 1);
        } else {
            ID3D11DeviceContext_ClearDepthStencilView(r->context,
                                                      r->zeta_binding->dsv,
                                                      flags, depth,
                                                      (UINT8)stencil);
        }
    }

    if (r->color_binding) {
        r->color_binding->draw_dirty |= write_color &&
                                        pgraph_color_write_enabled(pg);
        r->color_binding->cleared =
            write_color && full_clear && all_color_channels;
    }
    if (r->zeta_binding) {
        r->zeta_binding->draw_dirty |= write_zeta &&
                                       pgraph_zeta_write_enabled(pg);
        r->zeta_binding->cleared = write_zeta && full_clear;
    }

    pg->clearing = false;
}

void pgraph_d3d11_init_surfaces(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    QTAILQ_INIT(&r->surfaces);
    r->color_binding = NULL;
    r->zeta_binding = NULL;

    qemu_event_init(&r->downloads_complete, false);
    qemu_event_init(&r->dirty_surfaces_download_complete, false);
}

void pgraph_d3d11_finalize_surfaces(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    NV2AState *d = container_of(pg, NV2AState, pgraph);

    r->color_binding = NULL;
    r->zeta_binding = NULL;

    D3D11SurfaceBinding *surface, *next;
    QTAILQ_FOREACH_SAFE(surface, &r->surfaces, entry, next) {
        if (tcg_enabled() && surface->access_cb) {
            mem_access_callback_remove_by_ref(qemu_get_cpu(0),
                                              surface->access_cb);
        }
        surface_destroy(pg, surface);
        QTAILQ_REMOVE(&r->surfaces, surface, entry);
        g_free(surface);
    }

    qemu_event_destroy(&r->downloads_complete);
    qemu_event_destroy(&r->dirty_surfaces_download_complete);
    (void)d;
}

void pgraph_d3d11_surface_flush(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    bool update_surface = (r->color_binding || r->zeta_binding);

    pg->surface_color.draw_dirty = false;
    pg->surface_zeta.draw_dirty = false;
    memset(&pg->last_surface_shape, 0, sizeof(pg->last_surface_shape));
    pgraph_d3d11_unbind_surface(d, true);
    pgraph_d3d11_unbind_surface(d, false);

    D3D11SurfaceBinding *s, *next;
    QTAILQ_FOREACH_SAFE(s, &r->surfaces, entry, next) {
        pgraph_d3d11_surface_invalidate(d, s);
    }

    if (update_surface) {
        pgraph_d3d11_surface_update(d, true, true, true);
    }
}
