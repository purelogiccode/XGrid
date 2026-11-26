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

#ifndef HW_XBOX_NV2A_PGRAPH_HLSL_VSH_H
#define HW_XBOX_NV2A_PGRAPH_HLSL_VSH_H

#include "qemu/osdep.h"
#include "qemu/mstring.h"

#include "hw/xbox/nv2a/pgraph/glsl/vsh.h"
#include "common.h"

void pgraph_hlsl_gen_vsh_ff(const VshState *state, MString *header,
                            MString *body);
void pgraph_hlsl_gen_vsh_prog(uint16_t version, const uint32_t *tokens,
                              unsigned int length, MString *header,
                              MString *body);

MString *pgraph_hlsl_gen_vsh(const VshState *state);

#endif /* HW_XBOX_NV2A_PGRAPH_HLSL_VSH_H */
