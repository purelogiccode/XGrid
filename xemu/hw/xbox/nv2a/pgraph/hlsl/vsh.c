/*
 * Geforce NV2A PGRAPH HLSL Shader Generator
 *
 * Copyright (c) 2015 espes
 * Copyright (c) 2015 Jannik Vogel
 * Copyright (c) 2020-2026 Matt Borgerson
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
#include "hw/xbox/nv2a/pgraph/pgraph.h"
#include "vsh.h"

MString *pgraph_hlsl_gen_vsh(const VshState *state)
{
    MString *output = pgraph_hlsl_get_common(state->smooth_shading);
    MString *header = mstring_new();
    MString *body = mstring_new();

    mstring_append(header,
                   "#define FLOAT_MAX asfloat(0x7F7FFFFF)\n"
                   "\n");

    if (state->is_fixed_function) {
        pgraph_hlsl_gen_vsh_ff(state, header, body);
    } else {
        mstring_append(body, "  oFog = vshCarryFog;\n");
        pgraph_hlsl_gen_vsh_prog(VSH_VERSION_XVS,
                                 (uint32_t *)state->programmable.program_data,
                                 state->programmable.program_length, header,
                                 body);
        if (!state->point_params_enable) {
            mstring_append_fmt(body, "  oPts.x = %f;\n",
                               state->point_size <= 0.f ? 1.f :
                                                         state->point_size);
        }
    }

    if (!state->fog_enable) {
        mstring_append(body, "  oFog = float4(1.0, 1.0, 1.0, 1.0);\n");
    } else {
        if (!state->is_fixed_function) {
            mstring_append(body, "  float fogDistance = oFog.x;\n");
        }

        float infinite_fogdistance_result = 0.0f;
        float nan_fogfactor_result = 0.0f;

        switch (state->fog_mode) {
        case FOG_MODE_LINEAR:
        case FOG_MODE_LINEAR_ABS:
            infinite_fogdistance_result = 1.0f;
            nan_fogfactor_result = 1.0f;
            mstring_append(body, "  float fogFactor = fogParam.x + "
                                 "fogDistance * fogParam.y;\n");
            mstring_append(body, "  fogFactor -= 1.0;\n");
            break;
        case FOG_MODE_EXP:
            infinite_fogdistance_result = 1.0f;
            nan_fogfactor_result = 1.0f;
            /* fallthrough */
        case FOG_MODE_EXP_ABS:
            mstring_append(body, "  float fogFactor = fogParam.x + "
                                 "exp2(fogDistance * fogParam.y * 16.0);\n");
            mstring_append(body, "  fogFactor -= 1.5;\n");
            break;
        case FOG_MODE_EXP2:
        case FOG_MODE_EXP2_ABS:
            mstring_append(body,
                           "  float fogFactor = fogParam.x + "
                           "exp2(-fogDistance * fogDistance * fogParam.y * "
                           "fogParam.y * 32.0);\n");
            mstring_append(body, "  fogFactor -= 1.5;\n");
            break;
        default:
            assert(!"Invalid fog mode");
            break;
        }
        switch (state->fog_mode) {
        case FOG_MODE_LINEAR_ABS:
        case FOG_MODE_EXP_ABS:
        case FOG_MODE_EXP2_ABS:
            mstring_append(body, "  fogFactor = abs(fogFactor);\n");
            break;
        default:
            break;
        }

        mstring_append_fmt(
            body,
            "  if (isinf(fogDistance)) {\n"
            "    oFog = float4(%f, %f, %f, %f);\n"
            "  } else {\n"
            "    oFog = clamp(xgrid_NaNToValue(xgrid_splat(fogFactor), %f), "
            "-FLOAT_MAX, FLOAT_MAX);\n"
            "  }\n",
            infinite_fogdistance_result, infinite_fogdistance_result,
            infinite_fogdistance_result, infinite_fogdistance_result,
            nan_fogfactor_result);
    }

    mstring_append(output, mstring_get_str(header));
    mstring_unref(header);

    mstring_append(
        output,
        "XGridVSOut main(XGridVSIn input)\n"
        "{\n"
        "    float4 v0 = input.v0;\n"
        "    float4 v1 = input.v1;\n"
        "    float4 v2 = input.v2;\n"
        "    float4 v3 = input.v3;\n"
        "    float4 v4 = input.v4;\n"
        "    float4 v5 = input.v5;\n"
        "    float4 v6 = input.v6;\n"
        "    float4 v7 = input.v7;\n"
        "    float4 v8 = input.v8;\n"
        "    float4 v9 = input.v9;\n"
        "    float4 v10 = input.v10;\n"
        "    float4 v11 = input.v11;\n"
        "    float4 v12 = input.v12;\n"
        "    float4 v13 = input.v13;\n"
        "    float4 v14 = input.v14;\n"
        "    float4 v15 = input.v15;\n"
        "\n"
        "    float4 oPos = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oD0 = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oD1 = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oB0 = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oB1 = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oPts = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oFog = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oT0 = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oT1 = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oT2 = float4(0.0, 0.0, 0.0, 1.0);\n"
        "    float4 oT3 = float4(0.0, 0.0, 0.0, 1.0);\n"
        "\n");

    mstring_append(output, mstring_get_str(body));
    mstring_unref(body);

    mstring_append(output,
                   "\n"
                   "    XGridVSOut output;\n"
                   "    output.pos = float4(oPos.x, oPos.y, oPos.z, oPos.w);\n"
                   /* Guest depth in the units used by the Xbox depth range.
                    * D3D interpolates SV_Position.z perspective-correctly, but
                    * the guest can request screen-linear Z; carry the value
                    * explicitly with noperspective interpolation. */
                   "    output.vtxZ = (oPos.z / oPos.w) * clipRange.y;\n"
                   "    output.vtxD0 = clamp(xgrid_NaNToOne(oD0), 0.0, 1.0);\n"
                   "    output.vtxB0 = clamp(xgrid_NaNToOne(oB0), 0.0, 1.0);\n"
                   "    output.vtxFog = oFog.x;\n"
                   "    output.vtxT0 = oT0;\n"
                   "    output.vtxT1 = oT1;\n"
                   "    output.vtxT2 = oT2;\n"
                   "    output.vtxT3 = oT3;\n"
                   "\n");

    if (state->specular_enable) {
        mstring_append(
            output,
            "    output.vtxD1 = clamp(xgrid_NaNToOne(oD1), 0.0, 1.0);\n"
            "    output.vtxB1 = clamp(xgrid_NaNToOne(oB1), 0.0, 1.0);\n");
        if (state->ignore_specular_alpha) {
            mstring_append(output,
                           "    output.vtxD1.w = 1.0;\n"
                           "    output.vtxB1.w = 1.0;\n");
        }
    } else {
        mstring_append(output,
                       "    output.vtxD1 = float4(0.0, 0.0, 0.0, 1.0);\n"
                       "    output.vtxB1 = float4(0.0, 0.0, 0.0, 1.0);\n");
    }

    mstring_append(output, "    return output;\n}\n");

    return output;
}
