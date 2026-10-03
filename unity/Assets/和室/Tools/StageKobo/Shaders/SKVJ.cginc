// すてーじ工房：VJ の映像の素（ブラウザ版 65_vj.js の VJ_GEN_FS を移植）
// 番号はブラウザ版の VJ_GENS と同じ並び：
//   0 トンネル / 1 同心円 / 2 放射 / 3 シンセ / 4 ポリゴン / 5 リサージュ / 6 うず / 7 プラズマ / 8 市松 / 9 ドット波 / 10 ハニカム
//   11 ストライプ / 12 ワープ / 13 ハート / 14 EQ / 15 セル / 16〜22 これまでの VJ パターン / 23 画像 / 24 文字 / 25 黒
//   （26 合成 は v0.6 で付けて v0.8 で外した）
#ifndef SK_VJ_INCLUDED
#define SK_VJ_INCLUDED

#include "SKPattern.cginc"

#define SK_PI 3.14159265
#define SK_TAU 6.2831853

// ---- 幾何学の小道具 ----
float sdPoly(float2 p, float n, float r) { float a = atan2(p.x, p.y) + SK_PI; float b = SK_TAU / n; return cos(floor(0.5 + a / b) * b - a) * length(p) - r; }
float sdSeg(float2 p, float2 a, float2 b) { float2 pa = p - a, ba = b - a; float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0); return length(pa - ba * h); }
float dot2(float2 v) { return dot(v, v); }
float sdHeart(float2 p)
{
    p.x = abs(p.x);
    if (p.y + p.x > 1.0) return sqrt(dot2(p - float2(0.25, 0.75))) - 0.35355;
    return sqrt(min(dot2(p - float2(0.0, 1.0)), dot2(p - 0.5 * max(p.x + p.y, 0.0)))) * sign(p.x - p.y);
}
float3 pal3(float k, float3 a, float3 b, float3 c) { float m = glmod(k, 3.0); return m < 1.0 ? a : (m < 2.0 ? b : c); }
static const float2 SK_HS = float2(1.0, 1.7320508);
float4 hexCell(float2 p)
{
    float4 hc = floor(float4(p, p - float2(0.5, 1.0)) / SK_HS.xyxy) + 0.5;
    float4 h = float4(p - hc.xy * SK_HS, p - (hc.zw + 0.5) * SK_HS);
    return dot(h.xy, h.xy) < dot(h.zw, h.zw) ? float4(h.xy, hc.xy) : float4(h.zw, hc.zw + 0.5);
}
float hexD(float2 p) { p = abs(p); return max(dot(p, SK_HS * 0.5), p.x); }

// 映像の素を1つ描く（0〜22。画像・文字・黒はデッキのシェーダーの中で）
float3 skGen(int gen, float2 uv, float bt, float tt, float pu, float n, float zoom, float asp, float3 c1, float3 c2, float3 c3)
{
    float2 p = (uv - 0.5) * float2(asp, 1.0) / zoom;
    float3 col = float3(0, 0, 0);
    float r = length(p), a = atan2(p.y, p.x);
    if (gen == 0)
    {
        float ns = max(3.0, floor(n)); float2 q = rot2(p, bt * 0.12);
        float aa = atan2(q.x, q.y) + SK_PI, b = SK_TAU / ns; float d = cos(floor(0.5 + aa / b) * b - aa) * length(q);
        float z = 0.5 / max(d, 0.002) + bt * 0.5; float k = frac(z);
        float band = smoothstep(0.0, 0.05, k) * smoothstep(0.34, 0.26, k);
        col = lerp(c1, c2, step(1.0, glmod(floor(z), 2.0))) * band * (0.5 + 0.8 * pu) + c3 * exp(-abs(k - 0.62) * 40.0) * 0.5;
        col *= smoothstep(0.0, 0.1, d) * (0.6 + 0.4 * smoothstep(0.0, 0.5, d));
    }
    else if (gen == 1)
    {
        float k = r * max(1.0, n) * 1.6 - bt; float f = frac(k);
        col = pal3(floor(k), c1, c2, c3) * smoothstep(0.0, 0.08, f) * smoothstep(0.42, 0.3, f) * (0.5 + 0.8 * pu * exp(-r * 1.5));
        col += c3 * exp(-r * 9.0) * (0.4 + pu);
    }
    else if (gen == 2)
    {
        float m = max(3.0, floor(n)) * 2.0; float s = step(0.5, frac((a + bt * 0.2) / SK_TAU * m));
        col = lerp(c1, c2, s) * (0.3 + 0.7 * smoothstep(0.95, 0.0, r)) * (0.6 + 0.6 * pu);
        col += c3 * smoothstep(0.12 + 0.06 * pu, 0.0, r);
    }
    else if (gen == 3)
    {
        col = lerp(c2 * 0.12, c1 * 0.22, smoothstep(-0.1, 0.5, p.y));
        float2 sp = p - float2(0.0, 0.1); float sun = smoothstep(0.24, 0.235, length(sp));
        float cut = step(0.0, sp.y) + step(0.0, sin(sp.y * 90.0 - bt * 3.0));
        col = lerp(col, lerp(c3, c2, smoothstep(0.25, -0.1, sp.y)) * 1.2, sun * clamp(cut, 0.0, 1.0));
        if (p.y < -0.02)
        {
            float z = 0.12 / (-p.y); float2 g = float2(p.x * z * 3.0, z - bt * 0.5);
            float2 gd = abs(frac(g) - 0.5) / fwidth(g); float ln = 1.0 - min(min(gd.x, gd.y), 1.0);
            col = lerp(c2 * 0.04, c1 * 1.3, ln * smoothstep(7.0, 0.8, z)) * (0.7 + 0.5 * pu);
        }
    }
    else if (gen == 4)
    {
        float ns = max(3.0, floor(n));
        [loop] for (int i = 0; i < 7; i++)
        {
            float fi = (float)i;
            float rr = (0.05 + fi * 0.065) * (1.0 + 0.18 * pu);
            float2 q = rot2(p, bt * 0.25 * (glmod(fi, 2.0) < 1.0 ? 1.0 : -1.0) + fi * 0.4);
            float d = abs(sdPoly(q, ns, rr));
            col += pal3(fi, c1, c2, c3) * (smoothstep(0.01, 0.0, d) * 1.2 + exp(-d * 45.0) * 0.25);
        }
    }
    else if (gen == 5)
    {
        float A = clamp(floor(n), 1.0, 6.0), B = A + 1.0, best = 1e9; float2 pv = float2(0, 0);
        if (abs(p.x) < 0.6 && abs(p.y) < 0.56)
        {
            [loop] for (int i = 0; i <= 96; i++)
            {
                float s = (float)i / 96.0 * SK_TAU;
                float2 q = float2(0.44 * sin(A * s + bt * 0.35), 0.4 * sin(B * s));
                if (i > 0) best = min(best, sdSeg(p, pv, q));
                pv = q;
            }
        }
        col = lerp(c1, c2, 0.5 + 0.5 * sin(bt * 0.5)) * (smoothstep(0.006, 0.0, best) * 1.3 + exp(-best * 30.0) * (0.3 + 0.5 * pu));
    }
    else if (gen == 6)
    {
        float arms = max(1.0, floor(n)); float k = a / SK_TAU * arms + log(r + 1e-3) * 1.6 - bt * 0.5; float f = frac(k);
        col = lerp(c1, c2, step(1.0, glmod(floor(k), 2.0))) * smoothstep(0.0, 0.06, f) * smoothstep(0.55, 0.47, f) * smoothstep(0.0, 0.08, r);
        col += c3 * exp(-r * 10.0) * (0.3 + pu);
    }
    else if (gen == 7)
    {
        float2 q = p * (2.0 + n * 0.4); float s = tt * 0.6 + bt * 0.25;
        float v = sin(q.x * 2.0 + s) + sin(q.y * 2.6 - s * 0.8) + sin((q.x + q.y) * 1.4 + s * 0.6) + sin(length(q) * 3.0 - s * 1.2);
        v = v * 0.25 + 0.5; col = v < 0.5 ? lerp(c1, c2, v * 2.0) : lerp(c2, c3, v * 2.0 - 1.0); col *= 0.65 + 0.5 * pu;
    }
    else if (gen == 8)
    {
        float m = max(2.0, floor(n)); float ang = 0.785398 * (floor(bt) + smoothstep(0.0, 0.3, frac(bt)));
        float2 q = rot2(p, ang) * m; float2 id = floor(q), f = frac(q);
        float flip = glmod(id.x + id.y + floor(bt), 2.0); float e = smoothstep(0.0, 0.05, min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y)));
        col = lerp(c1, c2, flip) * (0.5 + 0.5 * e) * (0.65 + 0.55 * pu);
    }
    else if (gen == 9)
    {
        float m = 6.0 + n * 2.0; float2 g = p * m; float2 id = floor(g) + 0.5, f = frac(g) - 0.5;
        float w = 0.5 + 0.5 * sin(length(id) / m * 12.0 - bt * SK_PI);
        float rr = 0.06 + 0.4 * w * (0.75 + 0.25 * pu);
        col = lerp(c1, c2, w) * smoothstep(rr, rr - 0.06, length(f));
    }
    else if (gen == 10)
    {
        float4 h = hexCell(p * (3.0 + n)); float e = smoothstep(0.5, 0.43, hexD(h.xy));
        float rr = h21(h.zw + floor(bt) * 7.13); float on = step(0.55, rr);
        col = pal3(floor(rr * 3.0), c1, c2, c3) * e * (on * (0.55 + 0.7 * pu) + 0.06);
        col += c3 * smoothstep(0.47, 0.5, hexD(h.xy)) * 0.25;
    }
    else if (gen == 11)
    {
        float m = max(1.0, floor(n)); float2 q = rot2(p, 0.6); float k = q.x * m * 2.0 + bt; float f = frac(k);
        col = pal3(floor(k), c1, c2, c3) * smoothstep(0.0, 0.04, f) * smoothstep(0.62, 0.56, f) * (0.55 + 0.6 * pu);
    }
    else if (gen == 12)
    {
        [loop] for (int L = 0; L < 3; L++)
        {
            float fl = (float)L;
            float rays = 70.0 + fl * 45.0; float id = floor(a / SK_TAU * rays); float h = h11(id * (fl + 1.37) + fl * 9.1);
            float z = frac(h * 7.0 + bt * (0.15 + 0.2 * h) * max(1.0, n * 0.25));
            float pos = z * z * 0.9, len = 0.015 + 0.2 * z * z * (0.4 + pu);
            float d = abs(r - pos - len * 0.5) - len * 0.5; float aw = abs(frac(a / SK_TAU * rays) - 0.5);
            col += lerp(c3, h < 0.5 ? c1 : c2, 0.6) * smoothstep(0.008, 0.0, d) * smoothstep(0.3, 0.0, aw) * z * 1.6;
        }
        col += c1 * exp(-r * 12.0) * 0.4;
    }
    else if (gen == 13)
    {
        float m = 3.0 + n * 0.5; float2 q = p * m + float2(0.0, bt * 0.5); float2 id = floor(q), f = frac(q) - 0.5;
        float h = h21(id), sz = 0.22 + 0.16 * h; float2 o = float2(h21(id + 3.1) - 0.5, h21(id + 7.7) - 0.5) * 0.3;
        float2 l = rot2(f - o, sin(tt * 2.0 + h * 9.0) * 0.4) / (sz * (1.0 + 0.12 * pu));
        float d = sdHeart(l * 1.2 + float2(0.0, 0.55));
        float show = step(0.35, h);
        col = pal3(floor(h * 9.0), c1, c2, c3) * (smoothstep(0.03, 0.0, d) + exp(-max(d, 0.0) * 12.0) * 0.25) * show;
        col += lerp(c1, c2, uv.y) * 0.06;
    }
    else if (gen == 14)
    {
        col = vjPattern(5, 0.5 + (uv - 0.5) / zoom, tt, bt, c1, c2, c3, asp);
    }
    else if (gen == 15)
    {
        float2 q = p * (3.0 + n * 0.6); float2 ip = floor(q), fp = frac(q); float d1 = 9.0, d2 = 9.0; float2 cid = float2(0, 0);
        [unroll] for (int y = -1; y <= 1; y++)
            [unroll] for (int x = -1; x <= 1; x++)
            {
                float2 g = float2((float)x, (float)y);
                float2 o = float2(h21(ip + g), h21(ip + g + 5.3)); o = 0.5 + 0.4 * sin(tt * 0.6 + SK_TAU * o);
                float d = length(g + o - fp);
                if (d < d1) { d2 = d1; d1 = d; cid = ip + g; } else if (d < d2) d2 = d;
            }
        float rr = h21(cid + floor(bt) * 3.7); float edge = smoothstep(0.06, 0.0, d2 - d1);
        col = pal3(floor(rr * 3.0), c1, c2, c3) * (step(0.6, rr) * (0.45 + 0.7 * pu) + 0.08) + c3 * edge * 0.7;
    }
    else if (gen >= 16 && gen <= 22)
    {
        int k = gen == 16 ? 0 : gen == 17 ? 1 : gen == 18 ? 2 : gen == 19 ? 3 : gen == 20 ? 4 : gen == 21 ? 6 : 7;
        col = vjPattern(k, 0.5 + (uv - 0.5) / zoom, tt, bt, c1, c2, c3, asp) * (0.8 + 0.35 * pu);
    }
    return col;
}

#endif
