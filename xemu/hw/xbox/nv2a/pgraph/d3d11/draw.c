/*
 * Geforce NV2A PGRAPH Direct3D 11 Renderer - draw path
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
#include "renderer.h"
#include "reports.h"
#include "vertex.h"

#include <d3dcompiler.h>

#define VERTEX_BUFFER_INITIAL (1024 * 1024)
#define VERTEX_BUFFER_LIMIT (96 * 1024 * 1024)
#define INDEX_BUFFER_INITIAL (64 * 1024)
#define INDEX_BUFFER_LIMIT (16 * 1024 * 1024)

static uint32_t hash_blend_state(PGRAPHState *pg)
{
    uint32_t blend = pgraph_reg_r(pg, NV_PGRAPH_BLEND);
    uint32_t control_0 = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_0);
    return blend ^ (control_0 & 0x3C000000);
}

/* D3D11 does not allow color factors in the alpha blend slots. The Xbox
 * blend factors are symmetric, so use the matching alpha factor. */
static D3D11_BLEND alpha_blend_factor(D3D11_BLEND factor)
{
    switch (factor) {
    case D3D11_BLEND_SRC_COLOR:
        return D3D11_BLEND_SRC_ALPHA;
    case D3D11_BLEND_INV_SRC_COLOR:
        return D3D11_BLEND_INV_SRC_ALPHA;
    case D3D11_BLEND_DEST_COLOR:
        return D3D11_BLEND_DEST_ALPHA;
    case D3D11_BLEND_INV_DEST_COLOR:
        return D3D11_BLEND_INV_DEST_ALPHA;
    default:
        return factor;
    }
}

static uint32_t hash_rasterizer_state(PGRAPHState *pg)
{
    uint32_t raster = pgraph_reg_r(pg, NV_PGRAPH_SETUPRASTER);
    return raster;
}

static uint32_t hash_depth_stencil_state(PGRAPHState *pg)
{
    return pgraph_reg_r(pg, NV_PGRAPH_CONTROL_0) ^
           (pgraph_reg_r(pg, NV_PGRAPH_CONTROL_1) * 2654435761u) ^
           (pgraph_reg_r(pg, NV_PGRAPH_CONTROL_2) * 2246822519u);
}

static bool update_blend_state(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    uint32_t hash = hash_blend_state(pg);
    if (r->blend_state && r->blend_state_hash == hash) {
        return true;
    }

    if (r->blend_state) {
        ID3D11BlendState_Release(r->blend_state);
        r->blend_state = NULL;
    }

    uint32_t blend = pgraph_reg_r(pg, NV_PGRAPH_BLEND);
    uint32_t control_0 = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_0);
    uint32_t sfactor = GET_MASK(blend, NV_PGRAPH_BLEND_SFACTOR);
    uint32_t dfactor = GET_MASK(blend, NV_PGRAPH_BLEND_DFACTOR);
    uint32_t equation = GET_MASK(blend, NV_PGRAPH_BLEND_EQN);
    assert(sfactor < ARRAY_SIZE(pgraph_blend_factor_d3d11_map));
    assert(dfactor < ARRAY_SIZE(pgraph_blend_factor_d3d11_map));
    assert(equation < ARRAY_SIZE(pgraph_blend_equation_d3d11_map));

    D3D11_BLEND_DESC desc;
    memset(&desc, 0, sizeof(desc));
    desc.AlphaToCoverageEnable = FALSE;
    desc.IndependentBlendEnable = FALSE;
    desc.RenderTarget[0].BlendEnable = !!(blend & NV_PGRAPH_BLEND_EN);

    desc.RenderTarget[0].SrcBlend = pgraph_blend_factor_d3d11_map[sfactor];
    desc.RenderTarget[0].DestBlend = pgraph_blend_factor_d3d11_map[dfactor];
    desc.RenderTarget[0].BlendOp = pgraph_blend_equation_d3d11_map[equation];
    desc.RenderTarget[0].SrcBlendAlpha =
        alpha_blend_factor(desc.RenderTarget[0].SrcBlend);
    desc.RenderTarget[0].DestBlendAlpha =
        alpha_blend_factor(desc.RenderTarget[0].DestBlend);
    desc.RenderTarget[0].BlendOpAlpha = desc.RenderTarget[0].BlendOp;
    desc.RenderTarget[0].RenderTargetWriteMask =
        ((control_0 & NV_PGRAPH_CONTROL_0_RED_WRITE_ENABLE)
             ? D3D11_COLOR_WRITE_ENABLE_RED
             : 0) |
        ((control_0 & NV_PGRAPH_CONTROL_0_GREEN_WRITE_ENABLE)
             ? D3D11_COLOR_WRITE_ENABLE_GREEN
             : 0) |
        ((control_0 & NV_PGRAPH_CONTROL_0_BLUE_WRITE_ENABLE)
             ? D3D11_COLOR_WRITE_ENABLE_BLUE
             : 0) |
        ((control_0 & NV_PGRAPH_CONTROL_0_ALPHA_WRITE_ENABLE)
             ? D3D11_COLOR_WRITE_ENABLE_ALPHA
             : 0);

    HRESULT hr = ID3D11Device_CreateBlendState(r->device, &desc,
                                               &r->blend_state);
    if (FAILED(hr)) {
        fprintf(stderr,
                "nv2a: D3D11 blend state failed (hr=0x%08lx, sfactor=%u "
                "dfactor=%u equation=%u)\n",
                (unsigned long)hr, desc.RenderTarget[0].SrcBlend,
                desc.RenderTarget[0].DestBlend, desc.RenderTarget[0].BlendOp);

        /* Never leave the pipeline without a valid blend state. */
        memset(&desc, 0, sizeof(desc));
        desc.RenderTarget[0].SrcBlend = D3D11_BLEND_ONE;
        desc.RenderTarget[0].DestBlend = D3D11_BLEND_ZERO;
        desc.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
        desc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
        desc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ZERO;
        desc.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
        desc.RenderTarget[0].RenderTargetWriteMask =
            D3D11_COLOR_WRITE_ENABLE_ALL;
        hr = ID3D11Device_CreateBlendState(r->device, &desc,
                                           &r->blend_state);
        if (FAILED(hr)) {
            return false;
        }
    }
    r->blend_state_hash = hash;
    return true;
}

static bool update_rasterizer_state(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    uint32_t hash = hash_rasterizer_state(pg);
    if (r->rasterizer_state && r->rasterizer_state_hash == hash) {
        return true;
    }

    if (r->rasterizer_state) {
        ID3D11RasterizerState_Release(r->rasterizer_state);
        r->rasterizer_state = NULL;
    }

    uint32_t raster = pgraph_reg_r(pg, NV_PGRAPH_SETUPRASTER);
    uint32_t cull_face = GET_MASK(raster, NV_PGRAPH_SETUPRASTER_CULLCTRL);
    uint32_t front_mode =
        GET_MASK(raster, NV_PGRAPH_SETUPRASTER_FRONTFACEMODE);
    assert(cull_face < ARRAY_SIZE(pgraph_cull_face_d3d11_map));

    bool cull_enable = raster & NV_PGRAPH_SETUPRASTER_CULLENABLE;
    r->cull_both =
        cull_enable &&
        cull_face == NV_PGRAPH_SETUPRASTER_CULLCTRL_FRONT_AND_BACK;

    D3D11_RASTERIZER_DESC desc;
    memset(&desc, 0, sizeof(desc));
    desc.FillMode = front_mode == NV_PGRAPH_SETUPRASTER_FRONTFACEMODE_LINE
                        ? D3D11_FILL_WIREFRAME
                        : D3D11_FILL_SOLID;
    desc.CullMode = (cull_enable && !r->cull_both)
                        ? pgraph_cull_face_d3d11_map[cull_face]
                        : D3D11_CULL_NONE;
    /* The shaders map guest screen coordinates (y down) directly to clip
     * space. D3D11's viewport then flips y again into the render target, so
     * the projected winding is reversed relative to the guest. Encode the
     * front face with that inversion, matching the Vulkan backend. */
    desc.FrontCounterClockwise =
        !(raster & NV_PGRAPH_SETUPRASTER_FRONTFACE);
    desc.DepthBias = 0;
    desc.DepthBiasClamp = 0.0f;
    desc.SlopeScaledDepthBias = 0.0f;
    desc.DepthClipEnable = FALSE;
    desc.ScissorEnable = TRUE;
    desc.MultisampleEnable = FALSE;
    desc.AntialiasedLineEnable = FALSE;

    HRESULT hr = ID3D11Device_CreateRasterizerState(r->device, &desc,
                                                    &r->rasterizer_state);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 rasterizer state failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return false;
    }
    r->rasterizer_state_hash = hash;
    return true;
}

static bool update_depth_stencil_state(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    uint32_t hash = hash_depth_stencil_state(pg);
    if (r->depth_stencil_state && r->depth_stencil_state_hash == hash) {
        return true;
    }

    if (r->depth_stencil_state) {
        ID3D11DepthStencilState_Release(r->depth_stencil_state);
        r->depth_stencil_state = NULL;
    }

    uint32_t control_0 = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_0);
    uint32_t control_1 = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_1);
    uint32_t control_2 = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_2);
    uint32_t depth_func = GET_MASK(control_0, NV_PGRAPH_CONTROL_0_ZFUNC);
    uint32_t stencil_func = GET_MASK(control_1,
                                     NV_PGRAPH_CONTROL_1_STENCIL_FUNC);
    uint32_t stencil_mask_read = GET_MASK(
        control_1, NV_PGRAPH_CONTROL_1_STENCIL_MASK_READ);
    uint32_t stencil_mask_write = GET_MASK(
        control_1, NV_PGRAPH_CONTROL_1_STENCIL_MASK_WRITE);
    uint32_t op_fail = GET_MASK(control_2,
                                NV_PGRAPH_CONTROL_2_STENCIL_OP_FAIL);
    uint32_t op_zfail = GET_MASK(control_2,
                                 NV_PGRAPH_CONTROL_2_STENCIL_OP_ZFAIL);
    uint32_t op_zpass = GET_MASK(control_2,
                                 NV_PGRAPH_CONTROL_2_STENCIL_OP_ZPASS);

    assert(depth_func < ARRAY_SIZE(pgraph_depth_func_d3d11_map));
    assert(stencil_func < ARRAY_SIZE(pgraph_stencil_func_d3d11_map));
    assert(op_fail < ARRAY_SIZE(pgraph_stencil_op_d3d11_map));
    assert(op_zfail < ARRAY_SIZE(pgraph_stencil_op_d3d11_map));
    assert(op_zpass < ARRAY_SIZE(pgraph_stencil_op_d3d11_map));

    D3D11_DEPTH_STENCIL_DESC desc;
    memset(&desc, 0, sizeof(desc));
    desc.DepthEnable = !!(control_0 & NV_PGRAPH_CONTROL_0_ZENABLE);
    desc.DepthWriteMask = (control_0 & NV_PGRAPH_CONTROL_0_ZWRITEENABLE)
                              ? D3D11_DEPTH_WRITE_MASK_ALL
                              : D3D11_DEPTH_WRITE_MASK_ZERO;
    desc.DepthFunc = pgraph_depth_func_d3d11_map[depth_func];
    desc.StencilEnable = !!(control_1 &
                            NV_PGRAPH_CONTROL_1_STENCIL_TEST_ENABLE);
    desc.StencilReadMask = (UINT8)stencil_mask_read;
    desc.StencilWriteMask = (UINT8)stencil_mask_write;
    desc.FrontFace.StencilFailOp = pgraph_stencil_op_d3d11_map[op_fail];
    desc.FrontFace.StencilDepthFailOp = pgraph_stencil_op_d3d11_map[op_zfail];
    desc.FrontFace.StencilPassOp = pgraph_stencil_op_d3d11_map[op_zpass];
    desc.FrontFace.StencilFunc = pgraph_stencil_func_d3d11_map[stencil_func];
    desc.BackFace = desc.FrontFace;



    HRESULT hr = ID3D11Device_CreateDepthStencilState(
        r->device, &desc, &r->depth_stencil_state);
    if (FAILED(hr)) {
        fprintf(stderr,
                "nv2a: D3D11 depth-stencil state failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return false;
    }
    r->depth_stencil_state_hash = hash;
    return true;
}

static bool update_pipeline(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    D3D11ShaderBinding *shader = r->shader_binding;

    if (!shader || !shader->initialized) {
        return false;
    }

    return update_blend_state(d) && update_rasterizer_state(d) &&
           update_depth_stencil_state(d);
}

static void set_viewport_and_scissor(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    unsigned int width = MAX(pg->surface_binding_dim.width, 1);
    unsigned int height = MAX(pg->surface_binding_dim.height, 1);
    pgraph_apply_scaling_factor(pg, &width, &height);

    D3D11_VIEWPORT viewport = {
        .TopLeftX = 0.0f,
        .TopLeftY = 0.0f,
        .Width = (FLOAT)width,
        .Height = (FLOAT)height,
        .MinDepth = 0.0f,
        .MaxDepth = 1.0f,
    };
    ID3D11DeviceContext_RSSetViewports(r->context, 1, &viewport);

    unsigned int x = pg->surface_shape.clip_x;
    unsigned int y = pg->surface_shape.clip_y;
    unsigned int w = pg->surface_shape.clip_width;
    unsigned int h = pg->surface_shape.clip_height;
    pgraph_apply_anti_aliasing_factor(pg, &x, &y);
    pgraph_apply_anti_aliasing_factor(pg, &w, &h);
    pgraph_apply_scaling_factor(pg, &x, &y);
    pgraph_apply_scaling_factor(pg, &w, &h);

    int top = (int)height - (int)y - (int)h;
    int bottom = (int)height - (int)y;
    D3D11_RECT rect = {
        .left = MAX((int)x, 0),
        .top = MAX(top, 0),
        .right = MIN((int)width, (int)x + (int)w),
        .bottom = MIN((int)height, bottom),
    };
    ID3D11DeviceContext_RSSetScissorRects(r->context, 1, &rect);
}

static bool ensure_vertex_buffer(PGRAPHD3D11State *r, size_t bytes)
{
    if (bytes > VERTEX_BUFFER_LIMIT) {
        return false;
    }
    if (r->vertex_buffer && r->vertex_buffer_capacity >= bytes) {
        return true;
    }

    if (r->vertex_buffer) {
        ID3D11Buffer_Release(r->vertex_buffer);
        r->vertex_buffer = NULL;
    }

    size_t capacity = MAX(bytes, VERTEX_BUFFER_INITIAL);
    D3D11_BUFFER_DESC desc = {
        .ByteWidth = (UINT)capacity,
        .Usage = D3D11_USAGE_DYNAMIC,
        .BindFlags = D3D11_BIND_VERTEX_BUFFER,
        .CPUAccessFlags = D3D11_CPU_ACCESS_WRITE,
    };
    HRESULT hr = ID3D11Device_CreateBuffer(r->device, &desc, NULL,
                                           &r->vertex_buffer);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 vertex buffer failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return false;
    }
    r->vertex_buffer_capacity = capacity;
    return true;
}

static bool ensure_index_buffer(PGRAPHD3D11State *r, size_t bytes)
{
    if (bytes > INDEX_BUFFER_LIMIT) {
        return false;
    }
    if (r->index_buffer && r->index_buffer_capacity >= bytes) {
        return true;
    }

    if (r->index_buffer) {
        ID3D11Buffer_Release(r->index_buffer);
        r->index_buffer = NULL;
    }

    size_t capacity = MAX(bytes, INDEX_BUFFER_INITIAL);
    D3D11_BUFFER_DESC desc = {
        .ByteWidth = (UINT)capacity,
        .Usage = D3D11_USAGE_DYNAMIC,
        .BindFlags = D3D11_BIND_INDEX_BUFFER,
        .CPUAccessFlags = D3D11_CPU_ACCESS_WRITE,
    };
    HRESULT hr = ID3D11Device_CreateBuffer(r->device, &desc, NULL,
                                           &r->index_buffer);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 index buffer failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        return false;
    }
    r->index_buffer_capacity = capacity;
    return true;
}

void pgraph_d3d11_flush_draw(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (!r->draw_active) {
        return;
    }

    if (!(r->color_binding || r->zeta_binding)) {
        return;
    }

    XGridDrawData data;
    if (!pgraph_d3d11_prepare_vertices(d, &data)) {
        return;
    }

    size_t vertex_bytes = (size_t)data.vertex_count * sizeof(XGridVertex);
    if (!ensure_vertex_buffer(r, vertex_bytes)) {
        pgraph_d3d11_free_draw_data(&data);
        return;
    }

    D3D11_MAPPED_SUBRESOURCE mapped;
    HRESULT hr = ID3D11DeviceContext_Map(
        r->context, (ID3D11Resource *)r->vertex_buffer, 0,
        D3D11_MAP_WRITE_DISCARD, 0, &mapped);
    if (FAILED(hr)) {
        pgraph_d3d11_free_draw_data(&data);
        return;
    }
    memcpy(mapped.pData, data.vertices, vertex_bytes);
    ID3D11DeviceContext_Unmap(r->context,
                              (ID3D11Resource *)r->vertex_buffer, 0);

    UINT stride = sizeof(XGridVertex);
    UINT offset = 0;
    ID3D11DeviceContext_IASetVertexBuffers(r->context, 0, 1,
                                           &r->vertex_buffer, &stride,
                                           &offset);
    ID3D11DeviceContext_IASetPrimitiveTopology(r->context, data.topology);

    if (data.indexed && data.index_count) {
        size_t index_bytes = (size_t)data.index_count * sizeof(uint32_t);
        if (!ensure_index_buffer(r, index_bytes)) {
            pgraph_d3d11_free_draw_data(&data);
            return;
        }
        hr = ID3D11DeviceContext_Map(
            r->context, (ID3D11Resource *)r->index_buffer, 0,
            D3D11_MAP_WRITE_DISCARD, 0, &mapped);
        if (FAILED(hr)) {
            pgraph_d3d11_free_draw_data(&data);
            return;
        }
        memcpy(mapped.pData, data.indices, index_bytes);
        ID3D11DeviceContext_Unmap(r->context,
                                  (ID3D11Resource *)r->index_buffer, 0);
        ID3D11DeviceContext_IASetIndexBuffer(r->context, r->index_buffer,
                                             DXGI_FORMAT_R32_UINT, 0);
        ID3D11DeviceContext_DrawIndexed(r->context, data.index_count, 0, 0);
    } else if (data.use_draw_arrays) {
        for (unsigned int i = 0; i < pg->draw_arrays_length; i++) {
            int start = pg->draw_arrays_start[i] - (int)data.base_index;
            if (start < 0) {
                continue;
            }
            ID3D11DeviceContext_Draw(r->context,
                                     pg->draw_arrays_count[i], start);
        }
    } else {
        ID3D11DeviceContext_Draw(r->context, data.vertex_count, 0);
    }

    pgraph_d3d11_free_draw_data(&data);
}

static void pgraph_d3d11_set_surface_dirty(PGRAPHState *pg, bool color,
                                           bool zeta)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    color = color && pgraph_color_write_enabled(pg);
    zeta = zeta && pgraph_zeta_write_enabled(pg);
    pg->surface_color.draw_dirty |= color;
    pg->surface_zeta.draw_dirty |= zeta;

    if (r->color_binding) {
        r->color_binding->draw_dirty |= color;
        r->color_binding->frame_time = pg->frame_time;
        r->color_binding->cleared = false;
    }

    if (r->zeta_binding) {
        r->zeta_binding->draw_dirty |= zeta;
        r->zeta_binding->frame_time = pg->frame_time;
        r->zeta_binding->cleared = false;
    }
}

void pgraph_d3d11_draw_begin(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    r->draw_active = false;

    uint32_t control_0 = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_0);
    bool color_write = control_0 &
        (NV_PGRAPH_CONTROL_0_ALPHA_WRITE_ENABLE |
         NV_PGRAPH_CONTROL_0_RED_WRITE_ENABLE |
         NV_PGRAPH_CONTROL_0_GREEN_WRITE_ENABLE |
         NV_PGRAPH_CONTROL_0_BLUE_WRITE_ENABLE);
    bool depth_test = control_0 & NV_PGRAPH_CONTROL_0_ZENABLE;
    bool stencil_test = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_1) &
                        NV_PGRAPH_CONTROL_1_STENCIL_TEST_ENABLE;
    bool is_nop_draw = !(color_write || depth_test || stencil_test);

    pgraph_d3d11_surface_update(d, true, true, depth_test || stencil_test);

    if (is_nop_draw) {
        return;
    }

    if (!(r->color_binding || r->zeta_binding)) {
        return;
    }

    pgraph_d3d11_bind_textures(d);
    pgraph_d3d11_bind_shaders(pg);

    if (!update_pipeline(d)) {
        return;
    }

    if (r->cull_both) {
        return;
    }

    pgraph_d3d11_bind_surface_targets(d);
    set_viewport_and_scissor(d);

    float blend_color[4];
    pgraph_argb_pack32_to_rgba_float(
        pgraph_reg_r(pg, NV_PGRAPH_BLENDCOLOR), blend_color);

    /* D3D11 has one blend factor for both color and alpha. Constant-alpha
     * factors need the alpha broadcast to RGB; only do this when no
     * constant-color factor shares the same state. */
    uint32_t blend = pgraph_reg_r(pg, NV_PGRAPH_BLEND);
    uint32_t sfactor = GET_MASK(blend, NV_PGRAPH_BLEND_SFACTOR);
    uint32_t dfactor = GET_MASK(blend, NV_PGRAPH_BLEND_DFACTOR);
    bool constant_alpha =
        sfactor == NV_PGRAPH_BLEND_SFACTOR_CONSTANT_ALPHA ||
        sfactor == NV_PGRAPH_BLEND_SFACTOR_ONE_MINUS_CONSTANT_ALPHA ||
        dfactor == NV_PGRAPH_BLEND_DFACTOR_CONSTANT_ALPHA ||
        dfactor == NV_PGRAPH_BLEND_DFACTOR_ONE_MINUS_CONSTANT_ALPHA;
    bool constant_color =
        sfactor == NV_PGRAPH_BLEND_SFACTOR_CONSTANT_COLOR ||
        sfactor == NV_PGRAPH_BLEND_SFACTOR_ONE_MINUS_CONSTANT_COLOR ||
        dfactor == NV_PGRAPH_BLEND_DFACTOR_CONSTANT_COLOR ||
        dfactor == NV_PGRAPH_BLEND_DFACTOR_ONE_MINUS_CONSTANT_COLOR;
    if (constant_alpha && !constant_color) {
        blend_color[0] = blend_color[3];
        blend_color[1] = blend_color[3];
        blend_color[2] = blend_color[3];
    }

    ID3D11DeviceContext_OMSetBlendState(r->context, r->blend_state,
                                        blend_color, 0xFFFFFFFF);
    uint32_t stencil_ref = GET_MASK(pgraph_reg_r(pg, NV_PGRAPH_CONTROL_1),
                                    NV_PGRAPH_CONTROL_1_STENCIL_REF);
    ID3D11DeviceContext_OMSetDepthStencilState(
        r->context, r->depth_stencil_state, stencil_ref);
    ID3D11DeviceContext_RSSetState(r->context, r->rasterizer_state);

    pgraph_d3d11_begin_zpass_query(pg);

    r->draw_active = true;
}

void pgraph_d3d11_draw_end(NV2AState *d)
{
    PGRAPHState *pg = &d->pgraph;
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (!r->draw_active) {
        return;
    }

    uint32_t control_0 = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_0);
    bool color_write = control_0 &
        (NV_PGRAPH_CONTROL_0_ALPHA_WRITE_ENABLE |
         NV_PGRAPH_CONTROL_0_RED_WRITE_ENABLE |
         NV_PGRAPH_CONTROL_0_GREEN_WRITE_ENABLE |
         NV_PGRAPH_CONTROL_0_BLUE_WRITE_ENABLE);
    bool depth_test = control_0 & NV_PGRAPH_CONTROL_0_ZENABLE;
    bool stencil_test = pgraph_reg_r(pg, NV_PGRAPH_CONTROL_1) &
                        NV_PGRAPH_CONTROL_1_STENCIL_TEST_ENABLE;

    pgraph_d3d11_flush_draw(d);
    pgraph_d3d11_end_zpass_query(pg);
    pgraph_vsh_carryover_update(pg);

    pg->draw_time++;
    if (r->color_binding && pgraph_color_write_enabled(pg)) {
        r->color_binding->draw_time = pg->draw_time;
    }
    if (r->zeta_binding && pgraph_zeta_write_enabled(pg)) {
        r->zeta_binding->draw_time = pg->draw_time;
    }

    pgraph_d3d11_set_surface_dirty(pg, color_write,
                                   depth_test || stencil_test);

    r->draw_active = false;
}

void pgraph_d3d11_init_buffers(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    r->vertex_buffer = NULL;
    r->vertex_buffer_capacity = 0;
    r->index_buffer = NULL;
    r->index_buffer_capacity = 0;

    r->blend_state = NULL;
    r->blend_state_hash = 0;
    r->rasterizer_state = NULL;
    r->rasterizer_state_hash = 0;
    r->depth_stencil_state = NULL;
    r->depth_stencil_state_hash = 0;

    r->draw_active = false;
    r->cull_both = false;
}

void pgraph_d3d11_finalize_buffers(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (r->vertex_buffer) {
        ID3D11Buffer_Release(r->vertex_buffer);
        r->vertex_buffer = NULL;
    }
    if (r->index_buffer) {
        ID3D11Buffer_Release(r->index_buffer);
        r->index_buffer = NULL;
    }
    if (r->blend_state) {
        ID3D11BlendState_Release(r->blend_state);
        r->blend_state = NULL;
    }
    if (r->rasterizer_state) {
        ID3D11RasterizerState_Release(r->rasterizer_state);
        r->rasterizer_state = NULL;
    }
    if (r->depth_stencil_state) {
        ID3D11DepthStencilState_Release(r->depth_stencil_state);
        r->depth_stencil_state = NULL;
    }
}
