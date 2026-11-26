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

#ifndef HW_XBOX_NV2A_PGRAPH_D3D11_CONSTANTS_H
#define HW_XBOX_NV2A_PGRAPH_D3D11_CONSTANTS_H

#include "qemu/osdep.h"
#include "hw/xbox/nv2a/nv2a_regs.h"

#include <d3d11.h>

typedef struct D3D11SurfaceFormatInfo {
    DXGI_FORMAT format;
    unsigned int bytes_per_pixel;
    bool depth;
    bool float_depth;
} D3D11SurfaceFormatInfo;

static const D3D11SurfaceFormatInfo
    kelvin_surface_color_format_d3d11_map[] = {
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_X1R5G5B5_Z1R5G5B5] =
        {DXGI_FORMAT_B5G5R5A1_UNORM, 2, false, false},
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_X1R5G5B5_O1R5G5B5] =
        {DXGI_FORMAT_B5G5R5A1_UNORM, 2, false, false},
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_R5G6B5] =
        {DXGI_FORMAT_B5G6R5_UNORM, 2, false, false},
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_X8R8G8B8_Z8R8G8B8] =
        {DXGI_FORMAT_B8G8R8A8_UNORM, 4, false, false},
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_X8R8G8B8_O8R8G8B8] =
        {DXGI_FORMAT_B8G8R8A8_UNORM, 4, false, false},
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_X1A7R8G8B8_Z1A7R8G8B8] =
        {DXGI_FORMAT_B8G8R8A8_UNORM, 4, false, false},
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_X1A7R8G8B8_O1A7R8G8B8] =
        {DXGI_FORMAT_B8G8R8A8_UNORM, 4, false, false},
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_A8R8G8B8] =
        {DXGI_FORMAT_B8G8R8A8_UNORM, 4, false, false},
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_B8] =
        {DXGI_FORMAT_R8_UNORM, 1, false, false},
    [NV097_SET_SURFACE_FORMAT_COLOR_LE_G8B8] =
        {DXGI_FORMAT_R8G8_UNORM, 2, false, false},
};

static const D3D11SurfaceFormatInfo
    kelvin_surface_zeta_fixed_format_d3d11_map[] = {
    [NV097_SET_SURFACE_FORMAT_ZETA_Z16] =
        {DXGI_FORMAT_D16_UNORM, 2, true, false},
    [NV097_SET_SURFACE_FORMAT_ZETA_Z24S8] =
        {DXGI_FORMAT_D24_UNORM_S8_UINT, 4, true, false},
};

static const D3D11SurfaceFormatInfo
    kelvin_surface_zeta_float_format_d3d11_map[] = {
    [NV097_SET_SURFACE_FORMAT_ZETA_Z16] =
        {DXGI_FORMAT_D32_FLOAT, 2, true, true},
    [NV097_SET_SURFACE_FORMAT_ZETA_Z24S8] =
        /* The Xbox 24-bit float format has no direct D3D equivalent; keep the
         * GL backend's fixed-point emulation until a float surface path is
         * added. */
        {DXGI_FORMAT_D24_UNORM_S8_UINT, 4, true, false},
};

static const D3D11_BLEND pgraph_blend_factor_d3d11_map[] = {
    [NV_PGRAPH_BLEND_SFACTOR_ZERO] = D3D11_BLEND_ZERO,
    [NV_PGRAPH_BLEND_SFACTOR_ONE] = D3D11_BLEND_ONE,
    [NV_PGRAPH_BLEND_SFACTOR_SRC_COLOR] = D3D11_BLEND_SRC_COLOR,
    [NV_PGRAPH_BLEND_SFACTOR_ONE_MINUS_SRC_COLOR] = D3D11_BLEND_INV_SRC_COLOR,
    [NV_PGRAPH_BLEND_SFACTOR_SRC_ALPHA] = D3D11_BLEND_SRC_ALPHA,
    [NV_PGRAPH_BLEND_SFACTOR_ONE_MINUS_SRC_ALPHA] = D3D11_BLEND_INV_SRC_ALPHA,
    [NV_PGRAPH_BLEND_SFACTOR_DST_ALPHA] = D3D11_BLEND_DEST_ALPHA,
    [NV_PGRAPH_BLEND_SFACTOR_ONE_MINUS_DST_ALPHA] = D3D11_BLEND_INV_DEST_ALPHA,
    [NV_PGRAPH_BLEND_SFACTOR_DST_COLOR] = D3D11_BLEND_DEST_COLOR,
    [NV_PGRAPH_BLEND_SFACTOR_ONE_MINUS_DST_COLOR] = D3D11_BLEND_INV_DEST_COLOR,
    [NV_PGRAPH_BLEND_SFACTOR_SRC_ALPHA_SATURATE] = D3D11_BLEND_SRC_ALPHA_SAT,
    [NV_PGRAPH_BLEND_SFACTOR_CONSTANT_COLOR] = D3D11_BLEND_BLEND_FACTOR,
    [NV_PGRAPH_BLEND_SFACTOR_ONE_MINUS_CONSTANT_COLOR] =
        D3D11_BLEND_INV_BLEND_FACTOR,
    [NV_PGRAPH_BLEND_SFACTOR_CONSTANT_ALPHA] = D3D11_BLEND_BLEND_FACTOR,
    [NV_PGRAPH_BLEND_SFACTOR_ONE_MINUS_CONSTANT_ALPHA] =
        D3D11_BLEND_INV_BLEND_FACTOR,
};

static const D3D11_BLEND_OP pgraph_blend_equation_d3d11_map[] = {
    D3D11_BLEND_OP_SUBTRACT,
    D3D11_BLEND_OP_REV_SUBTRACT,
    D3D11_BLEND_OP_ADD,
    D3D11_BLEND_OP_MIN,
    D3D11_BLEND_OP_MAX,
    D3D11_BLEND_OP_REV_SUBTRACT,
    D3D11_BLEND_OP_ADD,
};

static const D3D11_CULL_MODE pgraph_cull_face_d3d11_map[] = {
    D3D11_CULL_NONE,
    D3D11_CULL_FRONT,
    D3D11_CULL_BACK,
    D3D11_CULL_NONE,
};

static const D3D11_COMPARISON_FUNC pgraph_depth_func_d3d11_map[] = {
    D3D11_COMPARISON_NEVER,
    D3D11_COMPARISON_LESS,
    D3D11_COMPARISON_EQUAL,
    D3D11_COMPARISON_LESS_EQUAL,
    D3D11_COMPARISON_GREATER,
    D3D11_COMPARISON_NOT_EQUAL,
    D3D11_COMPARISON_GREATER_EQUAL,
    D3D11_COMPARISON_ALWAYS,
};

static const D3D11_COMPARISON_FUNC pgraph_stencil_func_d3d11_map[] = {
    D3D11_COMPARISON_NEVER,
    D3D11_COMPARISON_LESS,
    D3D11_COMPARISON_EQUAL,
    D3D11_COMPARISON_LESS_EQUAL,
    D3D11_COMPARISON_GREATER,
    D3D11_COMPARISON_NOT_EQUAL,
    D3D11_COMPARISON_GREATER_EQUAL,
    D3D11_COMPARISON_ALWAYS,
};

static const D3D11_STENCIL_OP pgraph_stencil_op_d3d11_map[] = {
    D3D11_STENCIL_OP_KEEP,
    D3D11_STENCIL_OP_KEEP,
    D3D11_STENCIL_OP_ZERO,
    D3D11_STENCIL_OP_REPLACE,
    D3D11_STENCIL_OP_INCR,
    D3D11_STENCIL_OP_DECR,
    D3D11_STENCIL_OP_INVERT,
    D3D11_STENCIL_OP_INCR,
    D3D11_STENCIL_OP_DECR,
};

#endif /* HW_XBOX_NV2A_PGRAPH_D3D11_CONSTANTS_H */
