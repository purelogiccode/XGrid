/*
 * Geforce NV2A PGRAPH Direct3D 11 Renderer
 *
 * Copyright (c) 2026 Matt Borgerson
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
#include "ui/xemu-settings.h"
#include "renderer.h"
#include "reports.h"

#include <dxgi.h>

static GPUProperties pgraph_d3d11_gpu_properties;

static void pgraph_d3d11_report_hr(const char *what, HRESULT hr)
{
    fprintf(stderr, "nv2a: %s failed (HRESULT 0x%08lx)\n", what,
            (unsigned long)hr);
}

static void pgraph_d3d11_log_adapter(ID3D11Device *device)
{
    IDXGIDevice *dxgi_device = NULL;
    IDXGIAdapter *adapter = NULL;
    DXGI_ADAPTER_DESC desc;

    if (FAILED(ID3D11Device_QueryInterface(device, &IID_IDXGIDevice,
                                           (void **)&dxgi_device))) {
        return;
    }

    if (SUCCEEDED(IDXGIDevice_GetAdapter(dxgi_device, &adapter))) {
        if (SUCCEEDED(IDXGIAdapter_GetDesc(adapter, &desc))) {
            fprintf(stderr, "nv2a: Direct3D 11 adapter: %ls (%u MB)\n",
                    desc.Description,
                    (unsigned int)(desc.DedicatedVideoMemory / (1024 * 1024)));
        }
        IDXGIAdapter_Release(adapter);
    }

    IDXGIDevice_Release(dxgi_device);
}

static void pgraph_d3d11_init(NV2AState *d, Error **errp)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r;

    pg->d3d11_renderer_state = g_malloc0(sizeof(*pg->d3d11_renderer_state));
    r = pg->d3d11_renderer_state;

    static const D3D_FEATURE_LEVEL feature_levels[] = {
        D3D_FEATURE_LEVEL_11_0,
    };
    UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
    D3D_FEATURE_LEVEL feature_level = 0;
    HRESULT hr;

    hr = D3D11CreateDevice(NULL, D3D_DRIVER_TYPE_HARDWARE, NULL, flags,
                           feature_levels, ARRAY_SIZE(feature_levels),
                           D3D11_SDK_VERSION, &r->device, &feature_level,
                           &r->context);
    if (FAILED(hr)) {
        pgraph_d3d11_report_hr("D3D11CreateDevice", hr);
        error_setg(errp,
                   "Direct3D 11 device creation failed (HRESULT 0x%08lx)",
                   (unsigned long)hr);
        g_free(r);
        pg->d3d11_renderer_state = NULL;
        return;
    }

    r->feature_level = feature_level;
    r->initialized = true;

    r->context->lpVtbl->QueryInterface(r->context, &IID_ID3D11DeviceContext1,
                                       (void **)&r->context1);

    pgraph_d3d11_log_adapter(r->device);

    pgraph_d3d11_init_surfaces(pg);
    r->surfaces_initialized = true;
    pgraph_d3d11_init_textures(pg);
    pgraph_d3d11_init_reports(d);
    pgraph_d3d11_init_shaders(pg);
    pgraph_d3d11_init_buffers(pg);
    pgraph_d3d11_init_display(d);

    pg->surface_scale_factor = MAX(1, g_config.display.quality.surface_scale);
    pg->uniform_attrs = 0;
    pg->swizzle_attrs = 0;
    pg->compressed_attrs = 0;
}

static void pgraph_d3d11_finalize(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (!r) {
        return;
    }

    if (r->surfaces_initialized) {
        pgraph_d3d11_finalize_surfaces(pg);
    }
    pgraph_d3d11_finalize_textures(pg);
    pgraph_d3d11_finalize_reports(pg);
    pgraph_d3d11_finalize_shaders(pg);
    pgraph_d3d11_finalize_buffers(pg);
    pgraph_d3d11_finalize_display(pg);

    if (r->context1) {
        ID3D11DeviceContext1_Release(r->context1);
        r->context1 = NULL;
    }

    if (r->context) {
        ID3D11DeviceContext_Flush(r->context);
        ID3D11DeviceContext_Release(r->context);
        r->context = NULL;
    }

    if (r->device) {
        ID3D11Device_Release(r->device);
        r->device = NULL;
    }

    g_free(r);
    pg->d3d11_renderer_state = NULL;
}

static void pgraph_d3d11_flush(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    /* A scale change is published through a flush so that no surface or
     * texture is used at the new factor while its backing texture still has
     * the old dimensions. */
    unsigned int scale = MAX(1, g_config.display.quality.surface_scale);
    if (scale != pg->surface_scale_factor) {
        pg->surface_scale_factor = scale;
        pgraph_d3d11_surface_flush(d);
        pgraph_d3d11_invalidate_textures(pg);
    }

    if (r->context) {
        ID3D11DeviceContext_Flush(r->context);
    }

    qatomic_set(&d->pgraph.flush_pending, false);
    qemu_event_set(&d->pgraph.flush_complete);
}

static void pgraph_d3d11_sync(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (r->context) {
        ID3D11DeviceContext_Flush(r->context);
    }

    pgraph_d3d11_present(d);

    qatomic_set(&d->pgraph.sync_pending, false);
    qemu_event_set(&d->pgraph.sync_complete);
}

static void pgraph_d3d11_process_pending(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (qatomic_read(&d->pgraph.sync_pending) ||
        qatomic_read(&d->pgraph.flush_pending) ||
        qatomic_read(&r->downloads_pending) ||
        qatomic_read(&r->download_dirty_surfaces_pending)) {
        qemu_mutex_unlock(&d->pfifo.lock);
        qemu_mutex_lock(&d->pgraph.lock);
        if (qatomic_read(&r->downloads_pending)) {
            pgraph_d3d11_process_pending_downloads(d);
        }
        if (qatomic_read(&r->download_dirty_surfaces_pending)) {
            pgraph_d3d11_download_dirty_surfaces(d);
        }
        if (qatomic_read(&d->pgraph.sync_pending)) {
            pgraph_d3d11_sync(d);
        }
        if (qatomic_read(&d->pgraph.flush_pending)) {
            pgraph_d3d11_flush(d);
        }
        qemu_mutex_unlock(&d->pgraph.lock);
        qemu_mutex_lock(&d->pfifo.lock);
    }
}

static void pgraph_d3d11_flip_stall(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (r->context) {
        ID3D11DeviceContext_Flush(r->context);
    }
}

static void pgraph_d3d11_post_load(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    pg->surface_color.buffer_dirty = true;
    pg->surface_zeta.buffer_dirty = true;
    pg->surface_color.draw_dirty = false;
    pg->surface_zeta.draw_dirty = false;

    D3D11SurfaceBinding *surface, *next;
    QTAILQ_FOREACH_SAFE(surface, &r->surfaces, entry, next) {
        pgraph_d3d11_surface_invalidate(d, surface);
    }
    r->color_binding = NULL;
    r->zeta_binding = NULL;

    qatomic_set(&r->downloads_pending, false);
    qatomic_set(&r->download_dirty_surfaces_pending, false);

    pgraph_d3d11_invalidate_textures(pg);
}

static void pgraph_d3d11_pre_savevm_trigger(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    qatomic_set(&r->download_dirty_surfaces_pending, true);
    qemu_event_reset(&r->dirty_surfaces_download_complete);
}

static void pgraph_d3d11_pre_savevm_wait(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    qemu_event_wait(&r->dirty_surfaces_download_complete);
}

static void pgraph_d3d11_pre_shutdown_trigger(NV2AState *d)
{
}

static void pgraph_d3d11_pre_shutdown_wait(NV2AState *d)
{
}

static void pgraph_d3d11_set_surface_scale_factor(NV2AState *d,
                                                  unsigned int scale)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    g_config.display.quality.surface_scale = MAX(1, scale);
    if (!r) {
        return;
    }

    /* Stop the FIFO, write back the surfaces at the old scale, then let the
     * flush handler publish the new factor and rebuild the surface cache. */
    qemu_mutex_lock(&d->pfifo.lock);
    qatomic_set(&d->pfifo.halt, true);
    qemu_mutex_unlock(&d->pfifo.lock);

    qemu_mutex_lock(&d->pgraph.lock);
    qemu_event_reset(&r->dirty_surfaces_download_complete);
    qatomic_set(&r->download_dirty_surfaces_pending, true);
    qemu_mutex_unlock(&d->pgraph.lock);
    qemu_mutex_lock(&d->pfifo.lock);
    pfifo_kick(d);
    qemu_mutex_unlock(&d->pfifo.lock);
    qemu_event_wait(&r->dirty_surfaces_download_complete);

    qemu_mutex_lock(&d->pgraph.lock);
    qemu_event_reset(&d->pgraph.flush_complete);
    qatomic_set(&d->pgraph.flush_pending, true);
    qemu_mutex_unlock(&d->pgraph.lock);
    qemu_mutex_lock(&d->pfifo.lock);
    pfifo_kick(d);
    qemu_mutex_unlock(&d->pfifo.lock);
    qemu_event_wait(&d->pgraph.flush_complete);

    qemu_mutex_lock(&d->pfifo.lock);
    qatomic_set(&d->pfifo.halt, false);
    pfifo_kick(d);
    qemu_mutex_unlock(&d->pfifo.lock);
}

static unsigned int pgraph_d3d11_get_surface_scale_factor(NV2AState *d)
{
    return d->pgraph.surface_scale_factor;
}

static GPUProperties *pgraph_d3d11_get_gpu_properties(void)
{
    return &pgraph_d3d11_gpu_properties;
}

static PGRAPHRenderer pgraph_d3d11_renderer = {
    .type = CONFIG_DISPLAY_RENDERER_D3D11,
    .name = "Direct3D 11",
    .ops = {
        .init = pgraph_d3d11_init,
        .finalize = pgraph_d3d11_finalize,
        .clear_report_value = pgraph_d3d11_clear_report_value,
        .clear_surface = pgraph_d3d11_clear_surface,
        .draw_begin = pgraph_d3d11_draw_begin,
        .draw_end = pgraph_d3d11_draw_end,
        .flip_stall = pgraph_d3d11_flip_stall,
        .flush_draw = pgraph_d3d11_flush_draw,
        .get_report = pgraph_d3d11_get_report,
        .image_blit = pgraph_d3d11_image_blit,
        .pre_savevm_trigger = pgraph_d3d11_pre_savevm_trigger,
        .pre_savevm_wait = pgraph_d3d11_pre_savevm_wait,
        .pre_shutdown_trigger = pgraph_d3d11_pre_shutdown_trigger,
        .pre_shutdown_wait = pgraph_d3d11_pre_shutdown_wait,
        .process_pending = pgraph_d3d11_process_pending,
        .process_pending_reports = pgraph_d3d11_process_pending_reports,
        .post_load = pgraph_d3d11_post_load,
        .surface_flush = pgraph_d3d11_surface_flush,
        .surface_update = pgraph_d3d11_surface_update,
        .set_surface_scale_factor = pgraph_d3d11_set_surface_scale_factor,
        .get_surface_scale_factor = pgraph_d3d11_get_surface_scale_factor,
        .get_framebuffer_surface = pgraph_d3d11_get_framebuffer_surface,
        .get_gpu_properties = pgraph_d3d11_get_gpu_properties,
    },
};

static void __attribute__((constructor)) register_renderer(void)
{
    pgraph_renderer_register(&pgraph_d3d11_renderer);
}
