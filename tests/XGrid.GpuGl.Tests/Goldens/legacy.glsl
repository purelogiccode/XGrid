#version 330 core
uniform vec4 uConsts[18];
uniform vec4 uFogColor;
uniform int uFogEnable;
uniform int uAlphaTestEnable;
uniform int uAlphaFunc;
uniform float uAlphaRef;
uniform int uZclipActive;
uniform int uZclipCull;
uniform float uZclipMin;
uniform float uZclipMax;
uniform float uZetaRange;
uniform int uZetaBytes;
uniform int uTexOk[4];
uniform vec2 uTexSize[4];
uniform int uTexLinear[4];
uniform int uTexWrapX[4];
uniform int uTexWrapY[4];
uniform int uTexCube[4];
uniform int uTexDim[4];
uniform int uTexBilinear[4];
uniform int uTexConvolution[4];
uniform vec4 uTexBorder[4];
uniform uint uTexSwzMaskX[4];
uniform uint uTexSwzMaskY[4];
uniform int uTexMorton[4];
uniform int uTexCubeOk[4];
uniform int uTexCubeW[4];
uniform int uTexCubeH[4];
uniform int uTexCubeStride[4];
uniform int uTexBase[4];
uniform int uTexBytes[4];
uniform ivec4 uTexFieldOff[4];
uniform ivec4 uTexFieldW[4];
uniform vec4 uTexFieldC[4];
uniform int uDotMap[4];
uniform uint uGuestWord[96];
uniform int uTexPoolBase[4];
uniform vec4 uBumpMatrix[3];
uniform float uBumpLumeScale[3];
uniform float uBumpLumeOffset[3];
uniform vec3 uEyePos;
uniform uint uShaderClip;
uniform vec4 uTri[25];
flat in int vTri;
#define T(i) uTri[((vTri) * 25) + (i)]
uniform sampler2D uTex0;
uniform sampler2D uTex1;
uniform sampler2D uTex2;
uniform sampler2D uTex3;
out vec4 fragColor;

float InMap(float x, int m) {
  if (m == 0) return max(x, 0.0);
  if (m == 32) return 1.0 - clamp(x, 0.0, 1.0);
  if (m == 64) return 2.0 * max(x, 0.0) - 1.0;
  if (m == 96) return -2.0 * max(x, 0.0) + 1.0;
  if (m == 128) return max(x, 0.0) - 0.5;
  if (m == 160) return -max(x, 0.0) + 0.5;
  if (m == 192) return x;
  return -x;
}
vec3 InMap3(vec3 x, int m) {
  return vec3(InMap(x.r, m), InMap(x.g, m), InMap(x.b, m));
}
float OutMap(float x, int m) {
  if (m == 8) return x - 0.5;
  if (m == 16) return x * 2.0;
  if (m == 24) return (x - 0.5) * 2.0;
  if (m == 32) return x * 4.0;
  if (m == 48) return x / 2.0;
  return x;
}
vec3 OutMap3(vec3 x, int m) {
  return vec3(OutMap(x.r, m), OutMap(x.g, m), OutMap(x.b, m));
}
bool AlphaTest(float a) {
  int f = uAlphaFunc;
  if (f == 1) return a < uAlphaRef;
  if (f == 2) return a == uAlphaRef;
  if (f == 3) return a <= uAlphaRef;
  if (f == 4) return a > uAlphaRef;
  if (f == 5) return a != uAlphaRef;
  if (f == 6) return a >= uAlphaRef;
  if (f == 7) return true;
  return false;
}
vec2 CubeRemap2D(vec3 d) {
  float ax = abs(d.x); float ay = abs(d.y); float az = abs(d.z);
  if (ax > ay && ax > az) {
    float u = d.x > 0.0 ? -d.z : d.z;
    return vec2(u / ax, d.y / ax);
  }
  if (ay > ax && ay > az) {
    float v = d.y > 0.0 ? -d.z : d.z;
    return vec2(d.x / ay, v / ay);
  }
  float u = d.z > 0.0 ? d.x : -d.x;
  return vec2(u / az, d.y / az);
}
int WrapI(int v, int s, int mode) {
  if (mode == 0) {
    int m = v - (s * (v / s));
    if (m < 0) m += s;
    return m;
  }
  if (mode == 1) {
    int p = s * 2;
    int m = v - (p * (v / p));
    if (m < 0) m += p;
    return m < s ? m : p - 1 - m;
  }
  return v < 0 ? 0 : (v >= s ? s - 1 : v);
}
vec4 FetchTexel(int slot, ivec2 t) {
  if (slot == 0) return texelFetch(uTex0, t, 0);
  if (slot == 1) return texelFetch(uTex1, t, 0);
  if (slot == 2) return texelFetch(uTex2, t, 0);
  return texelFetch(uTex3, t, 0);
}
vec4 SampleTap(int slot, ivec2 t) {
  ivec2 size = ivec2(int(uTexSize[slot].x), int(uTexSize[slot].y));
  int wu = uTexWrapX[slot];
  int wv = uTexWrapY[slot];
  if (wu == 3 || wv == 3) {
    bool outU = wu == 3 && (t.x < 0 || t.x >= size.x);
    bool outV = wv == 3 && (t.y < 0 || t.y >= size.y);
    if (outU || outV) return uTexBorder[slot];
  }
  t.x = WrapI(t.x, size.x, wu);
  t.y = WrapI(t.y, size.y, wv);
  return FetchTexel(slot, t);
}
vec4 SampleSlot(int slot, vec2 uv) {
  if (uTexOk[slot] != 1) return vec4(0.0, 0.0, 0.0, 1.0);
  vec2 c = (uTexLinear[slot] == 1) ? uv : uv * uTexSize[slot];
  if (uTexBilinear[slot] != 1) {
    return SampleTap(slot, ivec2(int(floor(c.x)), int(floor(c.y))));
  }
  float fx = c.x - 0.5;
  float fy = c.y - 0.5;
  int x0 = int(floor(fx));
  int y0 = int(floor(fy));
  float wx = fx - float(x0);
  float wy = fy - float(y0);
  vec4 t00 = SampleTap(slot, ivec2(x0, y0));
  vec4 t10 = SampleTap(slot, ivec2(x0 + 1, y0));
  vec4 t01 = SampleTap(slot, ivec2(x0, y0 + 1));
  vec4 t11 = SampleTap(slot, ivec2(x0 + 1, y0 + 1));
  float w00 = (1.0 - wx) * (1.0 - wy);
  float w10 = wx * (1.0 - wy);
  float w01 = (1.0 - wx) * wy;
  float w11 = wx * wy;
  return (t00 * w00) + (t10 * w10) + (t01 * w01) + (t11 * w11);
}
uint SpreadZ(uint x) {
  x = (x | (x << 16u)) & 0x0000FFFFu;
  x = (x | (x << 8u)) & 0x00FF00FFu;
  x = (x | (x << 4u)) & 0x0F0F0F0Fu;
  x = (x | (x << 2u)) & 0x33333333u;
  x = (x | (x << 1u)) & 0x55555555u;
  return x;
}
uint SpreadO(uint x) {
  x = (x | (x << 16u)) & 0x0000FFFFu;
  x = (x | (x << 8u)) & 0x00FF00FFu;
  x = (x | (x << 4u)) & 0x0F0F0F0Fu;
  x = (x | (x << 2u)) & 0x33333333u;
  x = (x | (x << 1u)) & 0x55555555u;
  return x << 1u;
}
uint SwzBits(uint v, uint mask) {
  uint r = 0u;
  uint bit = 1u;
  for (int i = 0; i < 32; i++) {
    if ((mask & bit) != 0u) { if ((v & 0x80000000u) != 0u) r |= bit; v <<= 1u; }
    bit <<= 1u;
  }
  return r;
}
int CubeFaceSelect(vec3 d, out vec2 st) {
  float ax = abs(d.x); float ay = abs(d.y); float az = abs(d.z);
  if (ax >= ay && ax >= az) {
    if (d.x > 0.0) { st = vec2(((-d.z / ax) + 1.0) * 0.5, ((-d.y / ax) + 1.0) * 0.5); return 0; }
    st = vec2(((d.z / ax) + 1.0) * 0.5, ((-d.y / ax) + 1.0) * 0.5); return 1;
  }
  if (ay >= ax && ay >= az) {
    if (d.y > 0.0) { st = vec2(((d.x / ay) + 1.0) * 0.5, ((d.z / ay) + 1.0) * 0.5); return 2; }
    st = vec2(((d.x / ay) + 1.0) * 0.5, ((-d.z / ay) + 1.0) * 0.5); return 3;
  }
  if (d.z > 0.0) { st = vec2(((d.x / az) + 1.0) * 0.5, ((-d.y / az) + 1.0) * 0.5); return 4; }
  st = vec2(((-d.x / az) + 1.0) * 0.5, ((-d.y / az) + 1.0) * 0.5); return 5;
}
uint PoolWord(int slot, int entry) {
  int base = uTexPoolBase[slot] + entry;
  if (base < 0 || base >= 48) { return 0u; }
  return uGuestWord[base * 4];
}
uint ChannelMask(int w) {
  if (w == 1) return 1u;
  if (w == 4) return 15u;
  if (w == 5) return 31u;
  if (w == 6) return 63u;
  return 255u;
}
float DecodeField(int v, int w) {
  if (w == 1) return float(v);
  if (w == 4) return float(v) * (1.0 / 15.0);
  if (w == 5) return float(v) * (1.0 / 31.0);
  if (w == 6) return float(v) * (1.0 / 63.0);
  return float(v) * (1.0 / 255.0);
}
vec4 DecodeGuestTexel(int slot, uint word) {
  ivec4 off = ivec4(uTexFieldOff[slot]);
  ivec4 w = ivec4(uTexFieldW[slot]);
  vec4 c = uTexFieldC[slot];
  vec4 d = vec4(c.r, c.g, c.b, c.a);
  if (w.x > 0) { d.r = DecodeField(int((word >> uint(off.x)) & ChannelMask(w.x)), w.x); }
  if (w.y > 0) { d.g = DecodeField(int((word >> uint(off.y)) & ChannelMask(w.y)), w.y); }
  if (w.z > 0) { d.b = DecodeField(int((word >> uint(off.z)) & ChannelMask(w.z)), w.z); }
  if (w.w > 0) { d.a = DecodeField(int((word >> uint(off.w)) & ChannelMask(w.w)), w.w); }
  return vec4(float(word >> 24u) / 255.0, float((word >> 16u) & 0xFFu) / 255.0, float((word >> 8u) & 0xFFu) / 255.0, 1.0);
}
vec4 CubeTexel(int slot, int face, int tx, int ty) {
  tx = clamp(tx, 0, uTexCubeW[slot] - 1);
  ty = clamp(ty, 0, uTexCubeH[slot] - 1);
  uint word = PoolWord(slot, ((face * uTexCubeH[slot]) + ty) * uTexCubeW[slot] + tx);
  if (uTexBytes[slot] == 2) word &= 0xFFFFu;
  return DecodeGuestTexel(slot, word);
}
vec4 SampleCubeSlot(int slot, vec3 d) {
  if (uTexCubeOk[slot] != 1) return vec4(1.0, 1.0, 1.0, 1.0);
  vec2 st;
  int face = CubeFaceSelect(d, st);
  if (uTexBilinear[slot] != 1) {
    return CubeTexel(slot, face, int(floor(st.x * float(uTexCubeW[slot]))),
                     int(floor(st.y * float(uTexCubeH[slot]))));
  }
  float fx = (st.x * float(uTexCubeW[slot])) - 0.5;
  float fy = (st.y * float(uTexCubeH[slot])) - 0.5;
  int x0 = int(floor(fx)); int y0 = int(floor(fy));
  float wx = fx - float(x0); float wy = fy - float(y0);
  vec4 c00 = CubeTexel(slot, face, x0, y0);
  vec4 c10 = CubeTexel(slot, face, x0 + 1, y0);
  vec4 c01 = CubeTexel(slot, face, x0, y0 + 1);
  vec4 c11 = CubeTexel(slot, face, x0 + 1, y0 + 1);
  return (c00 * ((1.0 - wx) * (1.0 - wy))) + (c10 * (wx * (1.0 - wy)))
       + (c01 * ((1.0 - wx) * wy)) + (c11 * (wx * wy));
}
float Sign1(float x) { return ((x * 255.0) - 128.0) / 127.0; }
float Sign2(float x) { float s = x * 255.0; return s >= 128.0 ? (s - 255.5) / 127.5 : (s + 0.5) / 127.5; }
float Sign3(float x) { float s = x * 255.0; return s >= 128.0 ? (s - 256.0) / 127.0 : s / 127.0; }
float HemiD3D(float h) { return (h - 32768.0) / 32767.0; }
float HemiGl(float h) { return h >= 32768.0 ? (h - 65535.5) / 32767.5 : (h + 0.5) / 32767.5; }
float Hemi(float h) { return h >= 32768.0 ? (h - 65536.0) / 32767.0 : h / 32767.0; }
float HemiZ(float h, float l) { return sqrt(max(0.0, 1.0 - (h * h) - (l * l))); }
vec3 DotMap(int m, vec4 t) {
  if (m == 1) return vec3(Sign1(t.r), Sign1(t.g), Sign1(t.b));
  if (m == 2) return vec3(Sign2(t.r), Sign2(t.g), Sign2(t.b));
  if (m == 3) return vec3(Sign3(t.r), Sign3(t.g), Sign3(t.b));
  float hi = float((uint(t.a * 255.0) << 8u) | uint(t.r * 255.0));
  float lo = float((uint(t.g * 255.0) << 8u) | uint(t.b * 255.0));
  if (m == 4) return vec3(hi / 65535.0, lo / 65535.0, 1.0);
  if (m == 5) { float h = HemiD3D(hi); float l = HemiD3D(lo); return vec3(h, l, HemiZ(h, l)); }
  if (m == 6) { float h = HemiGl(hi); float l = HemiGl(lo); return vec3(h, l, HemiZ(h, l)); }
  if (m == 7) { float h = Hemi(hi); float l = Hemi(lo); return vec3(h, l, HemiZ(h, l)); }
  return vec3(t.r, t.g, t.b);
}
bool StageDot(int stage, vec3 coord, vec4 inTexel, int inSlot, out float d) {
  d = 0.0;
  int map = uDotMap[stage];
  if (map < 0 || map > 15) return false;
  if (map >= 4 && map <= 7) {
    uint pair = PoolWord(inSlot, 0);
    uint w = pair;
    vec4 t = vec4(float(w & 0xFFu), float((w >> 8u) & 0xFFu), float((w >> 16u) & 0xFFu), float((w >> 24u) & 0xFFu)) * (1.0 / 255.0);
    d = dot(coord, DotMap(map, t));
    return true;
  }
  d = dot(coord, DotMap(map, inTexel));
  return true;
}
vec4 SampleCubeOrRemap(int slot, vec3 d) {
  if (uTexCube[slot] == 1) return SampleCubeSlot(slot, d);
  return SampleSlot(slot, CubeRemap2D(d));
}
vec3 SpecularReflect(vec3 n, vec3 e) {
  float scale = (2.0 * dot(n, e)) / dot(n, n);
  return (scale * n) - e;
}
vec4 SampleConvolved(int slot, vec3 p) {
  if (uTexOk[slot] != 1) return vec4(0.0, 0.0, 0.0, 1.0);
  float invW = (p.z != 0.0) ? (1.0 / p.z) : 1.0;
  float du = (uTexLinear[slot] == 1) ? invW : (invW / max(uTexSize[slot].x, 1.0));
  float dv = (uTexLinear[slot] == 1) ? invW : (invW / max(uTexSize[slot].y, 1.0));
  vec2 base = p.xy * invW;
  vec4 acc = vec4(0.0);
  acc += SampleSlot(slot, base + vec2(-1.0 * du, -1.0 * dv)) * 0.0625;
  acc += SampleSlot(slot, base + vec2(0.0 * du, -1.0 * dv)) * 0.1250;
  acc += SampleSlot(slot, base + vec2(1.0 * du, -1.0 * dv)) * 0.0625;
  acc += SampleSlot(slot, base + vec2(-1.0 * du, 0.0 * dv)) * 0.1250;
  acc += SampleSlot(slot, base + vec2(0.0 * du, 0.0 * dv)) * 0.2500;
  acc += SampleSlot(slot, base + vec2(1.0 * du, 0.0 * dv)) * 0.1250;
  acc += SampleSlot(slot, base + vec2(-1.0 * du, 1.0 * dv)) * 0.0625;
  acc += SampleSlot(slot, base + vec2(0.0 * du, 1.0 * dv)) * 0.1250;
  acc += SampleSlot(slot, base + vec2(1.0 * du, 1.0 * dv)) * 0.0625;
  return acc;
}

void main() {
  float px = gl_FragCoord.x + T(2).w;
  float py = gl_FragCoord.y + T(2).z;
  vec2 vA = T(0).xy;
  vec2 vB = T(0).zw;
  vec2 vC = T(1).xy;
  float area = ((vB.x - vA.x) * (vC.y - vA.y)) - ((vC.x - vA.x) * (vB.y - vA.y));
  float invArea = 1.0 / area;
  float w0 = (((vB.x - vA.x) * (py - vA.y)) - ((vB.y - vA.y) * (px - vA.x))) * invArea;
  float w1 = (((vC.x - vB.x) * (py - vB.y)) - ((vC.y - vB.y) * (px - vB.x))) * invArea;
  float w2 = 1.0 - w0 - w1;
  if (!((w0 >= 0.0 && w1 >= 0.0 && w2 >= 0.0) || (w0 <= 0.0 && w1 <= 0.0 && w2 <= 0.0))) discard;
  float p0 = w0;
  float p1 = w1;
  float p2 = w2;
  if (T(1).z == 1.0) {
    float s0 = w0 * T(2).y;
    float s1 = w1 * T(1).w;
    float s2 = w2 * T(2).x;
    float sum = s0 + s1 + s2;
    if (!isnan(sum) && !isinf(sum) && sum != 0.0) {
      float invSum = 1.0 / sum;
      p0 = s0 * invSum;
      p1 = s1 * invSum;
      p2 = s2 * invSum;
    }
  }
  vec4 color = (p1 * T(3)) + (p2 * T(4)) + (p0 * T(5));
  vec4 spec = (p1 * T(6)) + (p2 * T(7)) + (p0 * T(8));
  vec4 c0 = (p1 * T(9)) + (p2 * T(10)) + (p0 * T(11));
  vec4 c1 = (p1 * T(12)) + (p2 * T(13)) + (p0 * T(14));
  vec4 c2 = (p1 * T(15)) + (p2 * T(16)) + (p0 * T(17));
  vec4 c3 = (p1 * T(18)) + (p2 * T(19)) + (p0 * T(20));
  float fogFactor = (p1 * T(21).z) + (p2 * T(22).z) + (p0 * T(23).z);
  float depthZ = (w1 * T(21).x) + (w2 * T(22).x) + (w0 * T(23).x);

  vec4 outcColor = color;
  if (uTexOk[0] == 1) outcColor *= SampleSlot(0, c0.xy);
  if (uZclipActive == 1) {
    float zv = depthZ * uZetaRange;
    if (zv < uZclipMin || zv > uZclipMax) {
      if (uZclipCull == 1) discard;
      depthZ = clamp(zv, uZclipMin, uZclipMax) / uZetaRange;
    }
  }
  vec4 outc = outcColor;
  if (uAlphaTestEnable == 1 && !AlphaTest(outc.a)) discard;
  float dz = clamp(depthZ, 0.0, 1.0);
  if (uZetaBytes == 4) {
    gl_FragDepth = float(min(uint(dz * 16777215.0 + 0.5), 0xFFFFFFu)) / 16777215.0;
  } else if (uZetaBytes == 2) {
    gl_FragDepth = float(min(uint(dz * 65535.0 + 0.5), 0xFFFFu)) / 65535.0;
  } else {
    gl_FragDepth = dz;
  }
  fragColor = outc;
}
