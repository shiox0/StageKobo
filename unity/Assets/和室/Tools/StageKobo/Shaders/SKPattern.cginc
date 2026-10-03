// すてーじ工房：VJ パターンと GLSL 互換の小道具（ブラウザ版 20_materials.js の PATTERN_GLSL を移植）
// LED ドット（SK_LEDDot）と VJ の映像の素（SK_VJDeck）で共通に使う
#ifndef SK_PATTERN_INCLUDED
#define SK_PATTERN_INCLUDED

#include "SKAudioLink.cginc"

// 音に反応（AudioLink）：呼ぶ側（LED の模様・VJ のデッキ）が vjPattern の前に入れる
static float skPatALw = 0.0;       // 反応の度合い（0 = いつもの「拍のドン」）
static float skPatALPulse = 0.0;   // 音のドン（低音 0〜1）

// ---- GLSL 互換の小道具
float  glmod(float x, float y)  { return x - y * floor(x / y); }
float3 glmod3(float3 x, float y) { return x - y * floor(x / y); }
float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
float h11(float n) { return frac(sin(n * 127.1) * 43758.5453); }
float vnoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
}
float3 hsv(float h, float s, float v)
{
    float3 k = saturate(abs(glmod3(h * 6.0 + float3(0, 4, 2), 6.0) - 3.0) - 1.0);
    return v * lerp(float3(1, 1, 1), k, s);
}
// GLSL の mat2(c,-s,s,c) * v と同じ
float2 rot2(float2 v, float a) { float c = cos(a), s = sin(a); return float2(c * v.x + s * v.y, -s * v.x + c * v.y); }
float sdTri(float2 p, float r)
{
    const float k = 1.7320508;
    p.x = abs(p.x) - r;
    p.y = p.y + r / k;
    if (p.x + k * p.y > 0.0) p = float2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
    p.x -= clamp(p.x, -2.0 * r, 0.0);
    return -length(p) * sign(p.y);
}

float3 vjPattern(int id, float2 uv, float t, float beat, float3 c1, float3 c2, float3 c3, float aspect)
{
    float2 q = float2(uv.x * aspect, uv.y);
    float2 p = (uv - 0.5) * float2(aspect, 1.0);
    float pulse = lerp(exp(-frac(beat) * 4.0), skPatALPulse, skPatALw);
    float3 col = float3(0, 0, 0);
    if (id == 0)
    {
        col = lerp(c1 * 0.45, c1, uv.y);
        [loop] for (int i = 0; i < 22; i++)
        {
            float fi = (float)i;
            float2 c = float2(h11(fi * 3.1) * aspect, frac(h11(fi + 11.0) + t * 0.035 * (0.4 + h11(fi + 5.0))) * 1.3 - 0.15);
            float s = 0.025 + 0.05 * h11(fi + 2.0);
            float d = sdTri(rot2(q - c, t * (h11(fi + 9.0) - 0.5) * 1.6 + fi), s);
            float m = glmod(fi, 3.0);
            float3 ci = m < 1.0 ? c2 : (m < 2.0 ? c3 : lerp(c1, float3(1, 1, 1), 0.65));
            col = lerp(col, ci, smoothstep(0.004, 0.0, d) * 0.9);
        }
        col *= 1.0 + 0.12 * pulse;
    }
    else if (id == 1)
    {
        float s = (q.x + q.y * 0.7) * 2.2 - t * 0.25;
        col = hsv(frac(s * 0.2), 0.32, 1.0);
        float2 g = floor(q * 10.0);
        float m = h21(g + floor(t * 1.5));
        col = lerp(col, lerp(c1, c2, h21(g + 3.0)), step(0.72, m) * 0.55);
        float2 sg = frac(q * 10.0) - 0.5;
        float sp = h21(g + 7.0);
        float star = max(smoothstep(0.03, 0.0, abs(sg.x)) * smoothstep(0.35, 0.0, abs(sg.y)), smoothstep(0.03, 0.0, abs(sg.y)) * smoothstep(0.35, 0.0, abs(sg.x)));
        col += float3(1, 1, 1) * star * step(0.85, sp) * (0.5 + 0.5 * sin(t * 4.0 + sp * 30.0));
        col = lerp(col, c3, 0.12);
    }
    else if (id == 2)
    {
        float2 pp = p * 2.0;
        float n = vnoise(pp * 2.0 + t * 0.05) * 0.6 + vnoise(pp * 5.0 - t * 0.03) * 0.4;
        col = lerp(float3(0.004, 0.006, 0.03), c1 * 0.7, smoothstep(0.35, 0.95, n));
        col += c2 * 0.55 * smoothstep(0.55, 1.0, vnoise(pp * 3.0 + 7.0 + t * 0.04));
        float2 sg = floor(q * 70.0);
        float st = h21(sg);
        float tw = 0.5 + 0.5 * sin(t * 3.0 + st * 50.0);
        col += c3 * step(0.985, st) * tw * 1.6 * smoothstep(0.45, 0.0, length(frac(q * 70.0) - 0.5));
    }
    else if (id == 3)
    {
        float2 pp = p; pp.y += 0.08;
        float d = sdTri(pp, 0.06);
        float k = d * 7.0 - beat * 0.5;
        float band = frac(k);
        float ln = smoothstep(0.32, 0.18, abs(band - 0.5));
        float3 bc = glmod(floor(k), 2.0) < 1.0 ? c1 : c2;
        col = lerp(c1 * 0.03, bc, ln * (0.55 + 0.45 * pulse));
        float2 g = frac(q * 48.0) - 0.5;
        col *= 0.75 + 0.25 * smoothstep(0.5, 0.2, length(g));
    }
    else if (id == 4)
    {
        float2 pp = p * 1.6;
        float a = vnoise(pp * 1.5 + float2(t * 0.1, -t * 0.07));
        float b = vnoise(pp * 2.3 - float2(t * 0.08, t * 0.05) + 4.0);
        col = lerp(c1, c2, smoothstep(0.3, 0.7, a));
        col = lerp(col, c3, smoothstep(0.55, 0.8, b) * 0.7);
        col = lerp(col, float3(1, 1, 1), 0.15);
        float l = abs(frac(a * 6.0) - 0.5);
        col = lerp(col, float3(1, 1, 1), smoothstep(0.035, 0.0, l) * 0.35);
    }
    else if (id == 5)
    {
        float n = 24.0;
        float bx = floor(uv.x * n);
        float h = 0.12 + 0.78 * pow(h11(bx * 1.7 + floor(beat * 2.0) * 13.0), 1.4) * (0.55 + 0.45 * pulse);
        if (skPatALw > 0.001) h = lerp(h, 0.04 + 0.92 * skALSpectrum((bx + 0.5) / n), saturate(skPatALw * 3.0));   // 音があるときは本物のイコライザー
        float on = step(uv.y, h) * step(0.14, frac(uv.x * n));
        float seg = step(0.22, frac(uv.y * 28.0));
        col = float3(0.004, 0.004, 0.004) + lerp(c1, c2, uv.y / max(h, 0.01)) * on * seg;
        col += c3 * step(abs(uv.y - h - 0.02), 0.012) * step(0.14, frac(uv.x * n));
    }
    else if (id == 6)
    {
        float3 bg = lerp(c1, c2, saturate(uv.y + 0.12 * sin(t * 0.5 + uv.x * 3.0)));
        float2 g = q * 12.0;
        float2 f = frac(g) - 0.5;
        float r = 0.2 + 0.1 * sin(beat * 3.14159 + floor(g.x) * 0.5 + floor(g.y) * 0.3);
        float dd = smoothstep(r, r - 0.05, length(f));
        col = lerp(bg * 0.85, lerp(c3, float3(1, 1, 1), 0.3), dd * 0.75);
    }
    else
    {
        col = c1 * 0.07 * (1.0 - uv.y * 0.6);
        [loop] for (int L = 0; L < 3; L++)
        {
            float fl = (float)L;
            float N = 9.0 + fl * 7.0;
            float2 g = q * N + float2(fl * 3.7, t * 0.03 * (fl + 1.0));
            float2 id2 = floor(g);
            float2 f = frac(g) - 0.5;
            float rnd = h21(id2 + fl * 11.0);
            float tw = pow(0.5 + 0.5 * sin(t * 2.2 + rnd * 40.0), 4.0);
            float star = max(smoothstep(0.035, 0.0, abs(f.x)) * smoothstep(0.4, 0.0, abs(f.y)), smoothstep(0.035, 0.0, abs(f.y)) * smoothstep(0.4, 0.0, abs(f.x)));
            star += smoothstep(0.12, 0.0, length(f)) * 0.8;
            col += lerp(c3, c2, h21(id2 + 3.0)) * star * tw * step(0.9, rnd) * 1.8;
        }
    }
    return col;
}

#endif
