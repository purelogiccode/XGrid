/*
 * Geforce NV2A PGRAPH Direct3D 11 Renderer - shader cache
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

#ifndef COBJMACROS
#define COBJMACROS
#endif

#include "qemu/osdep.h"
#include "qemu/fast-hash.h"
#include "qemu/lru.h"
#include "hw/xbox/nv2a/nv2a_int.h"
#include "hw/xbox/nv2a/pgraph/pgraph.h"
#include "hw/xbox/nv2a/pgraph/glsl/shaders.h"
#include "hw/xbox/nv2a/pgraph/hlsl/psh.h"
#include "hw/xbox/nv2a/pgraph/hlsl/vsh.h"
#include "ui/xemu-settings.h"
#include "renderer.h"

#include <d3dcompiler.h>
#include <glib/gstdio.h>

#define D3D11_SHADER_CACHE_MAGIC 0x53484458u /* 'XDSH' */
#define D3D11_SHADER_CACHE_VERSION 1u
#define D3D11_SHADER_CACHE_MAX_FILES 4096u
#define D3D11_SHADER_CACHE_KEEP_FILES 3072u

static const size_t shader_cache_size = 2048;

static const D3D11_INPUT_ELEMENT_DESC vertex_layout_desc[] = {
    { "TEXCOORD", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 0,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 1, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 16,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 2, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 32,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 3, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 48,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 4, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 64,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 5, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 80,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 6, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 96,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 7, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 112,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 8, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 128,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 9, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 144,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 10, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 160,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 11, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 176,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 12, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 192,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 13, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 208,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 14, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 224,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
    { "TEXCOORD", 15, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, 240,
      D3D11_INPUT_PER_VERTEX_DATA, 0 },
};

typedef struct D3D11ShaderCacheFile {
    char *path;
    gint64 mtime;
} D3D11ShaderCacheFile;

static char *shader_cache_dir;

static void shader_cache_file_free(gpointer data)
{
    D3D11ShaderCacheFile *file = data;
    g_free(file->path);
    g_free(file);
}

static gint shader_cache_file_compare(gconstpointer a, gconstpointer b)
{
    const D3D11ShaderCacheFile *file_a = *(D3D11ShaderCacheFile *const *)a;
    const D3D11ShaderCacheFile *file_b = *(D3D11ShaderCacheFile *const *)b;
    return (file_a->mtime > file_b->mtime) -
           (file_a->mtime < file_b->mtime);
}

static void shader_cache_prune(void)
{
    GDir *dir = g_dir_open(shader_cache_dir, 0, NULL);
    if (!dir) {
        return;
    }

    GPtrArray *files =
        g_ptr_array_new_with_free_func(shader_cache_file_free);
    const char *name;
    while ((name = g_dir_read_name(dir)) != NULL) {
        if (!g_str_has_suffix(name, ".vs") &&
            !g_str_has_suffix(name, ".ps")) {
            continue;
        }
        char *path = g_build_filename(shader_cache_dir, name, NULL);
        GStatBuf stat;
        if (g_stat(path, &stat) == 0) {
            D3D11ShaderCacheFile *file = g_malloc(sizeof(*file));
            file->path = path;
            file->mtime = stat.st_mtime;
            g_ptr_array_add(files, file);
        } else {
            g_free(path);
        }
    }
    g_dir_close(dir);

    if (files->len > D3D11_SHADER_CACHE_MAX_FILES) {
        g_ptr_array_sort(files, shader_cache_file_compare);
        guint remove = files->len - D3D11_SHADER_CACHE_KEEP_FILES;
        for (guint i = 0; i < remove; i++) {
            D3D11ShaderCacheFile *file = g_ptr_array_index(files, i);
            g_unlink(file->path);
        }
    }
    g_ptr_array_free(files, TRUE);
}

static void shader_cache_init(void)
{
    if (shader_cache_dir) {
        return;
    }

    if (g_config.general.shader_cache_dir &&
        g_config.general.shader_cache_dir[0]) {
        shader_cache_dir = g_strdup(g_config.general.shader_cache_dir);
    } else {
        gchar *cwd = g_get_current_dir();
        shader_cache_dir =
            g_build_filename(cwd, "d3d11-shader-cache", NULL);
        g_free(cwd);
    }
    if (g_mkdir_with_parents(shader_cache_dir, 0700) != 0) {
        g_free(shader_cache_dir);
        shader_cache_dir = NULL;
        return;
    }
    shader_cache_prune();
}

static char *shader_cache_path(uint64_t hash, const char *extension)
{
    return g_strdup_printf("%s/%016" PRIx64 ".%s", shader_cache_dir, hash,
                           extension);
}

static ID3D10Blob *shader_cache_load(uint64_t hash, const char *extension)
{
    if (!shader_cache_dir) {
        return NULL;
    }

    g_autofree char *path = shader_cache_path(hash, extension);
    gchar *data = NULL;
    gsize size = 0;
    if (!g_file_get_contents(path, &data, &size, NULL) || size < 8) {
        g_free(data);
        return NULL;
    }

    uint32_t magic;
    uint32_t version;
    memcpy(&magic, data, 4);
    memcpy(&version, data + 4, 4);
    if (magic != D3D11_SHADER_CACHE_MAGIC ||
        version != D3D11_SHADER_CACHE_VERSION) {
        g_free(data);
        return NULL;
    }

    ID3D10Blob *blob = NULL;
    HRESULT hr = D3DCreateBlob(size - 8, &blob);
    if (SUCCEEDED(hr)) {
        memcpy(ID3D10Blob_GetBufferPointer(blob), data + 8, size - 8);
    }
    g_free(data);
    return blob;
}

static void shader_cache_store(uint64_t hash, const char *extension,
                               ID3D10Blob *blob)
{
    if (!shader_cache_dir || !blob) {
        return;
    }

    g_autofree char *path = shader_cache_path(hash, extension);
    gsize payload = ID3D10Blob_GetBufferSize(blob);
    uint8_t *data = g_malloc(8 + payload);
    uint32_t magic = D3D11_SHADER_CACHE_MAGIC;
    uint32_t version = D3D11_SHADER_CACHE_VERSION;
    memcpy(data, &magic, 4);
    memcpy(data + 4, &version, 4);
    memcpy(data + 8, ID3D10Blob_GetBufferPointer(blob), payload);
    g_file_set_contents(path, (const gchar *)data, 8 + payload, NULL);
    g_free(data);
}

static void shader_cache_remove(uint64_t hash, const char *extension)
{
    if (!shader_cache_dir) {
        return;
    }
    g_autofree char *path = shader_cache_path(hash, extension);
    g_unlink(path);
}

static ID3DBlob *compile_hlsl(const char *source, const char *entry,
                              const char *target, const char *name)
{
    ID3DBlob *blob = NULL;
    ID3DBlob *errors = NULL;
    UINT flags = D3DCOMPILE_OPTIMIZATION_LEVEL3 |
                 D3DCOMPILE_ENABLE_BACKWARDS_COMPATIBILITY;

    HRESULT hr = D3DCompile(source, strlen(source), name, NULL, NULL, entry,
                            target, flags, 0, &blob, &errors);
    if (FAILED(hr)) {
        fprintf(stderr, "nv2a: D3D11 %s compile failed (hr=0x%08lx)\n", name,
                (unsigned long)hr);
        if (errors) {
            fprintf(stderr, "%s\n",
                    (const char *)ID3D10Blob_GetBufferPointer(errors));
        }
        if (blob) {
            ID3D10Blob_Release(blob);
        }
        if (errors) {
            ID3D10Blob_Release(errors);
        }
        return NULL;
    }

    if (errors) {
        ID3D10Blob_Release(errors);
    }
    return blob;
}

static bool create_shader_binding(PGRAPHState *pg,
                                  D3D11ShaderBinding *binding)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    MString *vs_src = pgraph_hlsl_gen_vsh(&binding->state.vsh);
    MString *ps_src = pgraph_hlsl_gen_psh(&binding->state.psh);
    g_autofree gchar *vs_source = g_strdup(mstring_get_str(vs_src));
    g_autofree gchar *ps_source = g_strdup(mstring_get_str(ps_src));

    uint64_t vs_hash =
        fast_hash((const uint8_t *)vs_source, strlen(vs_source));
    uint64_t ps_hash =
        fast_hash((const uint8_t *)ps_source, strlen(ps_source));

    ID3DBlob *vs_blob = shader_cache_load(vs_hash, "vs");
    ID3DBlob *ps_blob = shader_cache_load(ps_hash, "ps");
    bool vs_cached = vs_blob != NULL;
    bool ps_cached = ps_blob != NULL;

    if (!vs_blob) {
        vs_blob = compile_hlsl(vs_source, "main", "vs_5_0", "vertex shader");
        if (vs_blob) {
            shader_cache_store(vs_hash, "vs", vs_blob);
        }
    }
    if (!ps_blob) {
        ps_blob = compile_hlsl(ps_source, "main", "ps_5_0", "pixel shader");
        if (ps_blob) {
            shader_cache_store(ps_hash, "ps", ps_blob);
        }
    }
    mstring_unref(vs_src);
    mstring_unref(ps_src);

    if (!vs_blob || !ps_blob) {
        if (vs_blob) {
            ID3D10Blob_Release(vs_blob);
        }
        if (ps_blob) {
            ID3D10Blob_Release(ps_blob);
        }
        binding->failed = true;
        return false;
    }

    HRESULT hr = ID3D11Device_CreateVertexShader(
        r->device, ID3D10Blob_GetBufferPointer(vs_blob),
        ID3D10Blob_GetBufferSize(vs_blob), NULL, &binding->vs);
    if (FAILED(hr) && vs_cached) {
        /* A stale cache entry (for example after a compiler update) is
         * discarded and compiled again once. */
        shader_cache_remove(vs_hash, "vs");
        ID3D10Blob_Release(vs_blob);
        vs_blob = compile_hlsl(vs_source, "main", "vs_5_0", "vertex shader");
        vs_cached = false;
        if (vs_blob) {
            shader_cache_store(vs_hash, "vs", vs_blob);
            hr = ID3D11Device_CreateVertexShader(
                r->device, ID3D10Blob_GetBufferPointer(vs_blob),
                ID3D10Blob_GetBufferSize(vs_blob), NULL, &binding->vs);
        }
    }
    if (SUCCEEDED(hr)) {
        hr = ID3D11Device_CreatePixelShader(
            r->device, ID3D10Blob_GetBufferPointer(ps_blob),
            ID3D10Blob_GetBufferSize(ps_blob), NULL, &binding->ps);
        if (FAILED(hr) && ps_cached) {
            shader_cache_remove(ps_hash, "ps");
            ID3D10Blob_Release(ps_blob);
            ps_blob = compile_hlsl(ps_source, "main", "ps_5_0",
                                   "pixel shader");
            ps_cached = false;
            if (ps_blob) {
                shader_cache_store(ps_hash, "ps", ps_blob);
                hr = ID3D11Device_CreatePixelShader(
                    r->device, ID3D10Blob_GetBufferPointer(ps_blob),
                    ID3D10Blob_GetBufferSize(ps_blob), NULL, &binding->ps);
            }
        }
    }
    if (SUCCEEDED(hr) && vs_blob && ps_blob) {
        hr = ID3D11Device_CreateInputLayout(
            r->device, vertex_layout_desc, ARRAY_SIZE(vertex_layout_desc),
            ID3D10Blob_GetBufferPointer(vs_blob),
            ID3D10Blob_GetBufferSize(vs_blob), &binding->input_layout);
    }

    if (!vs_blob || !ps_blob) {
        if (vs_blob) {
            ID3D10Blob_Release(vs_blob);
        }
        if (ps_blob) {
            ID3D10Blob_Release(ps_blob);
        }
        binding->failed = true;
        return false;
    }

    ID3D10Blob_Release(vs_blob);
    ID3D10Blob_Release(ps_blob);

    if (FAILED(hr)) {
        fprintf(stderr,
                "nv2a: D3D11 shader object creation failed (hr=0x%08lx)\n",
                (unsigned long)hr);
        binding->failed = true;
        return false;
    }

    binding->initialized = true;
    return true;
}

static void shader_cache_entry_init(Lru *lru, LruNode *node, const void *key)
{
    D3D11ShaderBinding *binding = container_of(node, D3D11ShaderBinding, node);
    memcpy(&binding->state, key, sizeof(ShaderState));
    binding->vs = NULL;
    binding->ps = NULL;
    binding->input_layout = NULL;
    binding->initialized = false;
    binding->failed = false;
}

static void shader_cache_entry_post_evict(Lru *lru, LruNode *node)
{
    D3D11ShaderBinding *binding = container_of(node, D3D11ShaderBinding, node);
    if (binding->input_layout) {
        ID3D11InputLayout_Release(binding->input_layout);
    }
    if (binding->ps) {
        ID3D11PixelShader_Release(binding->ps);
    }
    if (binding->vs) {
        ID3D11VertexShader_Release(binding->vs);
    }
    binding->input_layout = NULL;
    binding->ps = NULL;
    binding->vs = NULL;
    binding->initialized = false;
}

static bool shader_cache_entry_compare(Lru *lru, LruNode *node,
                                       const void *key)
{
    D3D11ShaderBinding *binding = container_of(node, D3D11ShaderBinding, node);
    return memcmp(&binding->state, key, sizeof(ShaderState));
}

static void pack_uniforms(PGRAPHState *pg, D3D11ShaderBinding *binding)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    XGridD3D11Constants constants;
    memset(&constants, 0, sizeof(constants));

    VshUniformValues vsh;
    memset(&vsh, 0, sizeof(vsh));
    int vsh_locs[VshUniform__COUNT];
    for (int i = 0; i < VshUniform__COUNT; i++) {
        vsh_locs[i] = 0;
    }
    pgraph_glsl_set_vsh_uniform_values(pg, &binding->state.vsh, vsh_locs,
                                       &vsh);

    PshUniformValues psh;
    memset(&psh, 0, sizeof(psh));
    int psh_locs[PshUniform__COUNT];
    for (int i = 0; i < PshUniform__COUNT; i++) {
        psh_locs[i] = 0;
    }
    pgraph_glsl_set_psh_uniform_values(pg, psh_locs, &psh);

    memcpy(constants.c, vsh.c, sizeof(constants.c));
    memcpy(constants.clipRange, vsh.clipRange, sizeof(constants.clipRange));
    constants.fogParam[0] = vsh.fogParam[0][0];
    constants.fogParam[1] = vsh.fogParam[0][1];
    for (int i = 0; i < NV2A_MAX_LIGHTS; i++) {
        memcpy(constants.lightInfiniteDirection[i],
               vsh.lightInfiniteDirection[i], sizeof(float) * 3);
        memcpy(constants.lightInfiniteHalfVector[i],
               vsh.lightInfiniteHalfVector[i], sizeof(float) * 3);
        memcpy(constants.lightLocalAttenuation[i],
               vsh.lightLocalAttenuation[i], sizeof(float) * 3);
        memcpy(constants.lightLocalPosition[i], vsh.lightLocalPosition[i],
               sizeof(float) * 3);
    }
    memcpy(constants.ltc1, vsh.ltc1, sizeof(constants.ltc1));
    memcpy(constants.ltctxa, vsh.ltctxa, sizeof(constants.ltctxa));
    memcpy(constants.ltctxb, vsh.ltctxb, sizeof(constants.ltctxb));
    constants.material_alpha[0] = vsh.material_alpha[0];
    for (int i = 0; i < 8; i++) {
        constants.pointParams[i][0] = vsh.pointParams[i];
    }
    memcpy(constants.specularParams, vsh.specularParams,
           sizeof(float) * 3);
    memcpy(constants.specularParamsBack, vsh.specularParamsBack,
           sizeof(float) * 3);
    constants.surfaceSize[0] = vsh.surfaceSize[0][0];
    constants.surfaceSize[1] = vsh.surfaceSize[0][1];
    memcpy(constants.vshCarryFog, vsh.vshCarryFog,
           sizeof(constants.vshCarryFog));

    constants.alphaRef[0] = psh.alphaRef[0];
    memcpy(constants.bumpMat, psh.bumpMat, sizeof(constants.bumpMat));
    for (int i = 0; i < 4; i++) {
        constants.bumpOffset[i][0] = psh.bumpOffset[i];
        constants.bumpScale[i][0] = psh.bumpScale[i];
    }
    memcpy(constants.clipRegion, psh.clipRegion, sizeof(constants.clipRegion));
    memcpy(constants.colorKey, psh.colorKey, sizeof(constants.colorKey));
    memcpy(constants.colorKeyMask, psh.colorKeyMask,
           sizeof(constants.colorKeyMask));
    memcpy(constants.consts, psh.consts, sizeof(constants.consts));
    constants.depthFactor[0] = psh.depthFactor[0];
    constants.depthOffset[0] = psh.depthOffset[0];
    memcpy(constants.fogColor, psh.fogColor, sizeof(constants.fogColor));
    constants.surfaceScale[0] = psh.surfaceScale[0][0];
    constants.surfaceScale[1] = psh.surfaceScale[0][1];
    for (int i = 0; i < 4; i++) {
        D3D11TextureBinding *texture = &r->textures[i];
        if (texture->valid) {
            constants.texScale[i][0] = 1.0f;
            constants.texSize[i][0] = texture->width;
            constants.texSize[i][1] = texture->height;
            for (int j = 0; j < 4; j++) {
                constants.texSwizzle[i][j] = texture->swizzle[j];
            }
        } else {
            constants.texScale[i][0] = psh.texScale[i];
            constants.texSize[i][0] = 1.0f;
            constants.texSize[i][1] = 1.0f;
            constants.texSwizzle[i][0] = 0;
            constants.texSwizzle[i][1] = 1;
            constants.texSwizzle[i][2] = 2;
            constants.texSwizzle[i][3] = 3;
        }
    }

    D3D11_MAPPED_SUBRESOURCE mapped;
    HRESULT hr = ID3D11DeviceContext_Map(
        r->context, (ID3D11Resource *)r->constant_buffer, 0,
        D3D11_MAP_WRITE_DISCARD, 0, &mapped);
    if (FAILED(hr)) {
        return;
    }
    memcpy(mapped.pData, &constants, sizeof(constants));
    ID3D11DeviceContext_Unmap(r->context,
                              (ID3D11Resource *)r->constant_buffer, 0);

    ID3D11Buffer *buffer = r->constant_buffer;
    ID3D11DeviceContext_VSSetConstantBuffers(r->context, 0, 1, &buffer);
    ID3D11DeviceContext_PSSetConstantBuffers(r->context, 0, 1, &buffer);
}

void pgraph_d3d11_bind_shaders(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    ShaderState state = pgraph_glsl_get_shader_state(pg);
    state.vsh.compressed_attrs = 0;
    state.vsh.uniform_attrs = 0;
    state.vsh.swizzle_attrs = 0;

    uint64_t hash = fast_hash((const uint8_t *)&state, sizeof(state));
    LruNode *node = lru_lookup(&r->shader_cache, hash, &state);
    D3D11ShaderBinding *binding =
        container_of(node, D3D11ShaderBinding, node);

    if (!binding->initialized && !binding->failed) {
        create_shader_binding(pg, binding);
    }

    r->shader_binding = binding;
    pg->program_data_dirty = false;
    pgraph_clear_dirty_reg_map(pg);

    if (!binding->initialized) {
        return;
    }

    ID3D11DeviceContext_VSSetShader(r->context, binding->vs, NULL, 0);
    ID3D11DeviceContext_PSSetShader(r->context, binding->ps, NULL, 0);
    ID3D11DeviceContext_IASetInputLayout(r->context, binding->input_layout);

    pack_uniforms(pg, binding);
}

void pgraph_d3d11_init_shaders(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;
    shader_cache_init();
    lru_init(&r->shader_cache);
    r->shader_cache_entries = g_malloc_n(shader_cache_size,
                                         sizeof(D3D11ShaderBinding));
    for (size_t i = 0; i < shader_cache_size; i++) {
        lru_add_free(&r->shader_cache, &r->shader_cache_entries[i].node);
    }
    r->shader_cache.init_node = shader_cache_entry_init;
    r->shader_cache.compare_nodes = shader_cache_entry_compare;
    r->shader_cache.post_node_evict = shader_cache_entry_post_evict;

    D3D11_BUFFER_DESC desc = {
        .ByteWidth = sizeof(XGridD3D11Constants),
        .Usage = D3D11_USAGE_DYNAMIC,
        .BindFlags = D3D11_BIND_CONSTANT_BUFFER,
        .CPUAccessFlags = D3D11_CPU_ACCESS_WRITE,
        .MiscFlags = 0,
        .StructureByteStride = 0,
    };
    HRESULT hr = ID3D11Device_CreateBuffer(r->device, &desc, NULL,
                                           &r->constant_buffer);
    if (FAILED(hr)) {
        fprintf(stderr,
                "nv2a: D3D11 constant buffer creation failed (hr=0x%08lx)\n",
                (unsigned long)hr);
    }
}

void pgraph_d3d11_finalize_shaders(PGRAPHState *pg)
{
    PGRAPHD3D11State *r = pg->d3d11_renderer_state;

    if (r->constant_buffer) {
        ID3D11Buffer_Release(r->constant_buffer);
        r->constant_buffer = NULL;
    }

    if (r->shader_cache_entries) {
        lru_flush(&r->shader_cache);
        g_free(r->shader_cache_entries);
        r->shader_cache_entries = NULL;
    }
    r->shader_binding = NULL;
}
