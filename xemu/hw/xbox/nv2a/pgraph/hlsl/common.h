/*
 * Geforce NV2A PGRAPH HLSL Shader Generator
 *
 * Copyright (c) 2015 espes
 * Copyright (c) 2015 Jannik Vogel
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

#ifndef HW_XBOX_NV2A_PGRAPH_HLSL_COMMON_H
#define HW_XBOX_NV2A_PGRAPH_HLSL_COMMON_H

#include "qemu/osdep.h"
#include "qemu/mstring.h"

#include "hw/xbox/nv2a/pgraph/glsl/common.h"
#include "hw/xbox/nv2a/pgraph/glsl/vsh.h"
#include "hw/xbox/nv2a/pgraph/glsl/psh.h"

#define HLSL_MAT4(idx) \
    "xgrid_mat4(" GLSL_C(idx) ", " GLSL_C(idx + 1) ", " \
                 GLSL_C(idx + 2) ", " GLSL_C(idx + 3) ")"

/*
 * Constant buffer layout. This must stay in sync with the cbuffer in
 * pgraph_hlsl_get_common(): HLSL constant-buffer arrays are 16-byte aligned,
 * so scalar arrays are stored as one float4 per element on the C side too.
 */
typedef struct XGridD3D11Constants {
    float c[NV2A_VERTEXSHADER_CONSTANTS][4];
    float clipRange[4];
    float fogParam[4];
    float lightInfiniteDirection[NV2A_MAX_LIGHTS][4];
    float lightInfiniteHalfVector[NV2A_MAX_LIGHTS][4];
    float lightLocalAttenuation[NV2A_MAX_LIGHTS][4];
    float lightLocalPosition[NV2A_MAX_LIGHTS][4];
    float ltc1[NV2A_LTC1_COUNT][4];
    float ltctxa[NV2A_LTCTXA_COUNT][4];
    float ltctxb[NV2A_LTCTXB_COUNT][4];
    float material_alpha[4];
    float pointParams[8][4];
    float specularParams[4];
    float specularParamsBack[4];
    float surfaceSize[4];
    float vshCarryFog[4];
    int alphaRef[4];
    float bumpMat[4][4];
    float bumpOffset[4][4];
    float bumpScale[4][4];
    int clipRegion[8][4];
    uint32_t colorKey[4];
    uint32_t colorKeyMask[4];
    float consts[18][4];
    float depthFactor[4];
    float depthOffset[4];
    float fogColor[4];
    int surfaceScale[4];
    float texScale[4][4];
    float texSize[4][4];
    int texSwizzle[4][4];
} XGridD3D11Constants;

MString *pgraph_hlsl_get_common(bool smooth_shading);

#endif /* HW_XBOX_NV2A_PGRAPH_HLSL_COMMON_H */
