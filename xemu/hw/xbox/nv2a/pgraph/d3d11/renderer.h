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

#ifndef HW_XBOX_NV2A_PGRAPH_D3D11_RENDERER_H
#define HW_XBOX_NV2A_PGRAPH_D3D11_RENDERER_H

#include "qemu/osdep.h"
#include "qemu/thread.h"
#include "qemu/queue.h"
#include "qemu/lru.h"

#ifndef _WIN32
#error "The Direct3D 11 renderer is Windows only"
#endif

#include <d3d11.h>
#include <d3d11_1.h>
#include "hw/xbox/nv2a/nv2a_int.h"
#include "hw/xbox/nv2a/pgraph/surface.h"
#include "hw/xbox/nv2a/pgraph/glsl/shaders.h"
#include "constants.h"
#include "texture.h"

static inline unsigned int d3d11_surface_texel_size(
    const D3D11SurfaceFormatInfo *fmt)
{
    return fmt->float_depth ? 4 : fmt->bytes_per_pixel;
}

typedef struct D3D11SurfaceBinding {
    QTAILQ_ENTRY(D3D11SurfaceBinding) entry;
    MemAccessCallback *access_cb;
    hwaddr vram_addr;

    SurfaceShape shape;
    uintptr_t dma_addr;
    uintptr_t dma_len;
    bool color;
    bool swizzle;

    unsigned int width;
    unsigned int height;
    unsigned int pitch;
    size_t size;

    bool cleared;
    int frame_time;
    int draw_time;
    bool draw_dirty;
    bool download_pending;
    bool upload_pending;

    ID3D11Texture2D *texture;
    ID3D11Texture2D *staging;
    ID3D11ShaderResourceView *srv;
    ID3D11RenderTargetView *rtv;
    ID3D11DepthStencilView *dsv;
    D3D11SurfaceFormatInfo fmt;
} D3D11SurfaceBinding;

typedef struct D3D11ShaderBinding D3D11ShaderBinding;

typedef struct D3D11QueryReport {
    QSIMPLEQ_ENTRY(D3D11QueryReport) entry;
    bool clear;
    uint32_t parameter;
    unsigned int query_count;
    ID3D11Query **queries;
} D3D11QueryReport;

struct D3D11ShaderBinding {
    LruNode node;
    ShaderState state;

    ID3D11VertexShader *vs;
    ID3D11PixelShader *ps;
    ID3D11InputLayout *input_layout;

    bool initialized;
    bool failed;
};

/**
 * PGRAPHD3D11State:
 *
 * Per-machine state for the native Direct3D 11 renderer. Phase 1 owns the
 * device, immediate context and the renderer flush/sync events. Surface
 * caching lives here too because presentation and every later draw phase read
 * the same color/zeta targets.
 */
struct PGRAPHD3D11State {
    ID3D11Device *device;
    ID3D11DeviceContext *context;
    ID3D11DeviceContext1 *context1;
    D3D_FEATURE_LEVEL feature_level;

    bool initialized;
    bool surfaces_initialized;

    QTAILQ_HEAD(, D3D11SurfaceBinding) surfaces;
    D3D11SurfaceBinding *color_binding;
    D3D11SurfaceBinding *zeta_binding;

    D3D11TextureBinding textures[NV2A_MAX_TEXTURES];

    QSIMPLEQ_HEAD(, D3D11QueryReport) report_queue;
    ID3D11Query **zpass_queries;
    unsigned int zpass_query_count;
    bool zpass_query_active;
    uint32_t zpass_pixel_count_result;

    bool downloads_pending;
    QemuEvent downloads_complete;
    bool download_dirty_surfaces_pending;
    QemuEvent dirty_surfaces_download_complete;

    ID3D11Texture2D *display_staging;
    unsigned int gl_display_buffer;
    unsigned int display_width;
    unsigned int display_height;
    bool display_initialized;

    Lru shader_cache;
    D3D11ShaderBinding *shader_cache_entries;
    D3D11ShaderBinding *shader_binding;
    ID3D11Buffer *constant_buffer;

    ID3D11Buffer *vertex_buffer;
    size_t vertex_buffer_capacity;
    ID3D11Buffer *index_buffer;
    size_t index_buffer_capacity;

    ID3D11BlendState *blend_state;
    uint32_t blend_state_hash;
    ID3D11RasterizerState *rasterizer_state;
    uint32_t rasterizer_state_hash;
    ID3D11DepthStencilState *depth_stencil_state;
    uint32_t depth_stencil_state_hash;

    bool draw_active;
    bool cull_both;
};

void pgraph_d3d11_init_surfaces(PGRAPHState *pg);
void pgraph_d3d11_finalize_surfaces(PGRAPHState *pg);
void pgraph_d3d11_surface_update(NV2AState *d, bool upload, bool color_write,
                                 bool zeta_write);
void pgraph_d3d11_surface_flush(NV2AState *d);
void pgraph_d3d11_process_pending_downloads(NV2AState *d);
void pgraph_d3d11_download_dirty_surfaces(NV2AState *d);
void pgraph_d3d11_surface_download_if_dirty(NV2AState *d,
                                            D3D11SurfaceBinding *surface);
void pgraph_d3d11_surface_invalidate(NV2AState *d,
                                     D3D11SurfaceBinding *surface);
void pgraph_d3d11_unbind_surface(NV2AState *d, bool color);
D3D11SurfaceBinding *pgraph_d3d11_surface_get(NV2AState *d, hwaddr addr);
D3D11SurfaceBinding *pgraph_d3d11_surface_get_within(NV2AState *d, hwaddr addr);
void pgraph_d3d11_upload_surface_data(NV2AState *d,
                                      D3D11SurfaceBinding *surface,
                                      bool force);
void pgraph_d3d11_bind_surface_targets(NV2AState *d);
void pgraph_d3d11_clear_surface(NV2AState *d, uint32_t parameter);
void pgraph_d3d11_draw_begin(NV2AState *d);
void pgraph_d3d11_draw_end(NV2AState *d);
void pgraph_d3d11_image_blit(NV2AState *d);
void pgraph_d3d11_init_display(NV2AState *d);
void pgraph_d3d11_finalize_display(PGRAPHState *pg);
void pgraph_d3d11_present(NV2AState *d);
int pgraph_d3d11_get_framebuffer_surface(NV2AState *d);
unsigned int pgraph_d3d11_mark_surfaces_in_range_for_upload(NV2AState *d,
                                                            hwaddr start,
                                                            hwaddr size);
void pgraph_d3d11_download_surfaces_in_range_if_dirty(PGRAPHState *pg,
                                                      hwaddr start,
                                                      hwaddr size);
void pgraph_d3d11_init_shaders(PGRAPHState *pg);
void pgraph_d3d11_finalize_shaders(PGRAPHState *pg);
void pgraph_d3d11_bind_shaders(PGRAPHState *pg);
void pgraph_d3d11_init_buffers(PGRAPHState *pg);
void pgraph_d3d11_finalize_buffers(PGRAPHState *pg);
void pgraph_d3d11_flush_draw(NV2AState *d);

#endif /* HW_XBOX_NV2A_PGRAPH_D3D11_RENDERER_H */
