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
vec4 SampleSlot(int slot, vec2 uv) {
  if (uTexOk[slot] != 1) return vec4(0.0, 0.0, 0.0, 1.0);
  vec2 c = (uTexLinear[slot] == 1) ? uv : uv * uTexSize[slot];
  ivec2 t;
  t.x = WrapI(int(floor(c.x)), int(uTexSize[slot].x), uTexWrapX[slot]);
  t.y = WrapI(int(floor(c.y)), int(uTexSize[slot].y), uTexWrapY[slot]);
  if (slot == 0) return texelFetch(uTex0, t, 0);
  if (slot == 1) return texelFetch(uTex1, t, 0);
  if (slot == 2) return texelFetch(uTex2, t, 0);
  return texelFetch(uTex3, t, 0);
}

void main() {
  float px = gl_FragCoord.x;
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

  vec4 fog = (uFogEnable == 1) ? vec4(uFogColor.rgb, clamp(fogFactor, 0.0, 1.0)) : vec4(0.0, 0.0, 0.0, 1.0);
  vec4 t0 = vec4(0.0, 0.0, 0.0, 1.0);
  vec4 t1 = vec4(0.0, 0.0, 0.0, 1.0);
  vec4 t2 = vec4(0.0, 0.0, 0.0, 1.0);
  vec4 t3 = vec4(0.0, 0.0, 0.0, 1.0);
  t0 = vec4(0.0, 0.0, 0.0, 1.0);
  t1 = vec4(0.0, 0.0, 0.0, 1.0);
  t2 = vec4(0.0, 0.0, 0.0, 1.0);
  t3 = vec4(0.0, 0.0, 0.0, 1.0);
  vec4 r0 = vec4(0.0, 0.0, 0.0, 1.0);
  vec4 r1 = vec4(0.0);
  vec3 s0A = vec3(InMap(0.0, 0), InMap(0.0, 0), InMap(0.0, 0));
  vec3 s0B = vec3(InMap(0.0, 0), InMap(0.0, 0), InMap(0.0, 0));
  vec3 s0C = vec3(InMap(0.0, 0), InMap(0.0, 0), InMap(0.0, 0));
  vec3 s0D = vec3(InMap(0.0, 0), InMap(0.0, 0), InMap(0.0, 0));
  vec3 ab0 = clamp(OutMap3((s0A * s0B), 0), -1.0, 1.0);
  vec3 cd0 = clamp(OutMap3((s0C * s0D), 0), -1.0, 1.0);
  vec3 mux0 = clamp(OutMap3((ab0 + cd0), 0), -1.0, 1.0);
  float s0aA = InMap(0.0, 0);
  float s0aB = InMap(0.0, 0);
  float s0aC = InMap(0.0, 0);
  float s0aD = InMap(0.0, 0);
  float abA0 = clamp(OutMap(s0aA * s0aB, 0), -1.0, 1.0);
  float cdA0 = clamp(OutMap(s0aC * s0aD, 0), -1.0, 1.0);
  float muxA0 = clamp(OutMap((abA0 + cdA0), 0), -1.0, 1.0);
  vec3 eF = InMap3(vec3(0.0, 0.0, 0.0), 0) * InMap3(vec3(0.0, 0.0, 0.0), 0);
  vec3 aF = InMap3(vec3(0.0, 0.0, 0.0), 0);
  vec3 bF = InMap3(vec3(0.0, 0.0, 0.0), 0);
  vec3 cF = InMap3(vec3(0.0, 0.0, 0.0), 0);
  vec3 dF = InMap3(vec3(0.0, 0.0, 0.0), 0);
  vec4 comb = vec4(dF + (cF + ((bF - cF) * aF)), 0.0);
  comb.a = InMap(0.0, 0);
  if (uZclipActive == 1) {
    float zv = depthZ * uZetaRange;
    if (zv < uZclipMin || zv > uZclipMax) {
      if (uZclipCull == 1) discard;
      depthZ = clamp(zv, uZclipMin, uZclipMax) / uZetaRange;
    }
  }
  vec4 outc = comb;
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
