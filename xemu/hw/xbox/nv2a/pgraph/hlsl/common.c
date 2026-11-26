/*
 * Geforce NV2A PGRAPH HLSL Shader Generator
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

#include "qemu/osdep.h"
#include "common.h"

MString *pgraph_hlsl_get_common(bool smooth_shading)
{
    MString *out = mstring_new();

    const char *diffuse_qualifier = smooth_shading ? "" : "nointerpolation ";

    mstring_append_fmt(
        out,
        "struct XGridVSIn {\n"
        "    float4 v0 : TEXCOORD0;\n"
        "    float4 v1 : TEXCOORD1;\n"
        "    float4 v2 : TEXCOORD2;\n"
        "    float4 v3 : TEXCOORD3;\n"
        "    float4 v4 : TEXCOORD4;\n"
        "    float4 v5 : TEXCOORD5;\n"
        "    float4 v6 : TEXCOORD6;\n"
        "    float4 v7 : TEXCOORD7;\n"
        "    float4 v8 : TEXCOORD8;\n"
        "    float4 v9 : TEXCOORD9;\n"
        "    float4 v10 : TEXCOORD10;\n"
        "    float4 v11 : TEXCOORD11;\n"
        "    float4 v12 : TEXCOORD12;\n"
        "    float4 v13 : TEXCOORD13;\n"
        "    float4 v14 : TEXCOORD14;\n"
        "    float4 v15 : TEXCOORD15;\n"
        "};\n"
        "\n"
        "struct XGridVSOut {\n"
        "    float4 pos : SV_Position;\n"
        "    %sfloat4 vtxD0 : TEXCOORD0;\n"
        "    %sfloat4 vtxD1 : TEXCOORD1;\n"
        "    %sfloat4 vtxB0 : TEXCOORD2;\n"
        "    %sfloat4 vtxB1 : TEXCOORD3;\n"
        "    float vtxFog : TEXCOORD4;\n"
        "    float4 vtxT0 : TEXCOORD5;\n"
        "    float4 vtxT1 : TEXCOORD6;\n"
        "    float4 vtxT2 : TEXCOORD7;\n"
        "    float4 vtxT3 : TEXCOORD8;\n"
        "    noperspective float vtxZ : TEXCOORD9;\n"
        "};\n"
        "\n"
        "struct XGridPSIn {\n"
        "    float4 pos : SV_Position;\n"
        "    %sfloat4 vtxD0 : TEXCOORD0;\n"
        "    %sfloat4 vtxD1 : TEXCOORD1;\n"
        "    %sfloat4 vtxB0 : TEXCOORD2;\n"
        "    %sfloat4 vtxB1 : TEXCOORD3;\n"
        "    float vtxFog : TEXCOORD4;\n"
        "    float4 vtxT0 : TEXCOORD5;\n"
        "    float4 vtxT1 : TEXCOORD6;\n"
        "    float4 vtxT2 : TEXCOORD7;\n"
        "    float4 vtxT3 : TEXCOORD8;\n"
        "    noperspective float vtxZ : TEXCOORD9;\n"
        "};\n"
        "\n"
        "struct XGridPSOut {\n"
        "    float4 color : SV_Target0;\n"
        "    float depth : SV_Depth;\n"
        "};\n"
        "\n",
        diffuse_qualifier, diffuse_qualifier, diffuse_qualifier,
        diffuse_qualifier, diffuse_qualifier, diffuse_qualifier,
        diffuse_qualifier, diffuse_qualifier);

    mstring_append_fmt(
        out,
        "cbuffer XGridConstants : register(b0) {\n"
        "    float4 c[%d];\n"
        "    float4 clipRange;\n"
        "    float4 fogParam;\n"
        "    float4 lightInfiniteDirection[%d];\n"
        "    float4 lightInfiniteHalfVector[%d];\n"
        "    float4 lightLocalAttenuation[%d];\n"
        "    float4 lightLocalPosition[%d];\n"
        "    float4 ltc1[%d];\n"
        "    float4 ltctxa[%d];\n"
        "    float4 ltctxb[%d];\n"
        "    float4 material_alpha;\n"
        "    float4 pointParams[%d];\n"
        "    float4 specularParams;\n"
        "    float4 specularParamsBack;\n"
        "    float4 surfaceSize;\n"
        "    float4 vshCarryFog;\n"
        "    int4 alphaRef;\n"
        "    float4 bumpMat[4];\n"
        "    float4 bumpOffset[4];\n"
        "    float4 bumpScale[4];\n"
        "    int4 clipRegion[8];\n"
        "    uint4 colorKey;\n"
        "    uint4 colorKeyMask;\n"
        "    float4 consts[18];\n"
        "    float4 depthFactor;\n"
        "    float4 depthOffset;\n"
        "    float4 fogColor;\n"
        "    int4 surfaceScale;\n"
        "    float4 texScale[4];\n"
        "    float4 texSize[4];\n"
        "    int4 texSwizzle[4];\n"
        "};\n"
        "\n",
        NV2A_VERTEXSHADER_CONSTANTS, NV2A_MAX_LIGHTS, NV2A_MAX_LIGHTS,
        NV2A_MAX_LIGHTS, NV2A_MAX_LIGHTS, NV2A_LTC1_COUNT, NV2A_LTCTXA_COUNT,
        NV2A_LTCTXB_COUNT, 8);

    mstring_append(
        out,
        "float4x4 xgrid_mat4(float4 c0, float4 c1, float4 c2, float4 c3)\n"
        "{\n"
        "    return float4x4(c0, c1, c2, c3);\n"
        "}\n"
        "\n"
        "/* GLSL row-vector * column-major matrix, with the C vectors stored as\n"
        " * HLSL matrix rows. */\n"
        "float4 xgrid_mul_row(float4 v, float4x4 m)\n"
        "{\n"
        "    return float4(dot(v, m[0]), dot(v, m[1]), dot(v, m[2]),\n"
        "                  dot(v, m[3]));\n"
        "}\n"
        "\n"
        "float4 xgrid_NaNToOne(float4 src)\n"
        "{\n"
        "    return float4(isnan(src.x) ? 1.0 : src.x,\n"
        "                  isnan(src.y) ? 1.0 : src.y,\n"
        "                  isnan(src.z) ? 1.0 : src.z,\n"
        "                  isnan(src.w) ? 1.0 : src.w);\n"
        "}\n"
        "\n"
        "float4 xgrid_NaNToValue(float4 src, float replacement)\n"
        "{\n"
        "    return float4(isnan(src.x) ? replacement : src.x,\n"
        "                  isnan(src.y) ? replacement : src.y,\n"
        "                  isnan(src.z) ? replacement : src.z,\n"
        "                  isnan(src.w) ? replacement : src.w);\n"
        "}\n"
        "\n"
        "float4 xgrid_splat(float v)\n"
        "{\n"
        "    return float4(v, v, v, v);\n"
        "}\n"
        "\n"
        "float3 xgrid_splat3(float v)\n"
        "{\n"
        "    return float3(v, v, v);\n"
        "}\n"
        "\n"
        "float3 xgrid_splat3(float3 v)\n"
        "{\n"
        "    return v;\n"
        "}\n"
        "\n"
        "float xgrid_clampAwayZeroInf(float t)\n"
        "{\n"
        "    if (t > 0.0 || asuint(t) == 0) {\n"
        "        t = clamp(t, asfloat(0x1F800000), asfloat(0x5F800000));\n"
        "    } else {\n"
        "        t = clamp(t, asfloat(0xDF800000), asfloat(0x9F800000));\n"
        "    }\n"
        "    return t;\n"
        "}\n"
        "\n"
        "float2 xgrid_roundScreenCoords(float2 pos)\n"
        "{\n"
        "    return trunc(pos * 16.0) / 16.0;\n"
        "}\n"
        "\n"
        "float2 xgrid_mul_mat2(float4 m, float2 v)\n"
        "{\n"
        "    return float2(m.x * v.x + m.z * v.y, m.y * v.x + m.w * v.y);\n"
        "}\n"
        "\n"
        "float xgrid_pick(float4 c, int selector)\n"
        "{\n"
        "    if (selector == 0) return c.x;\n"
        "    if (selector == 1) return c.y;\n"
        "    if (selector == 2) return c.z;\n"
        "    if (selector == 3) return c.w;\n"
        "    if (selector == 4) return 1.0;\n"
        "    return 0.0;\n"
        "}\n"
        "\n"
        "float4 xgrid_swizzle(float4 c, int4 s)\n"
        "{\n"
        "    return float4(xgrid_pick(c, s.x), xgrid_pick(c, s.y),\n"
        "                  xgrid_pick(c, s.z), xgrid_pick(c, s.w));\n"
        "}\n"
        "\n"
        "float xgrid_mod(float x, float y)\n"
        "{\n"
        "    return x - y * floor(x / y);\n"
        "}\n"
        "\n"
        "float4 xgrid_less_than(float4 a, float4 b)\n"
        "{\n"
        "    return float4(a.x < b.x ? 1.0 : 0.0, a.y < b.y ? 1.0 : 0.0,\n"
        "                  a.z < b.z ? 1.0 : 0.0, a.w < b.w ? 1.0 : 0.0);\n"
        "}\n"
        "\n"
        "float4 xgrid_greater_equal(float4 a, float4 b)\n"
        "{\n"
        "    return float4(a.x >= b.x ? 1.0 : 0.0, a.y >= b.y ? 1.0 : 0.0,\n"
        "                  a.z >= b.z ? 1.0 : 0.0, a.w >= b.w ? 1.0 : 0.0);\n"
        "}\n"
        "\n");

    return out;
}
