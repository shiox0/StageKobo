// すてーじ工房：VJ の出力（デッキ A/B を混ぜて、エフェクト・色・パッドを掛ける Custom Render Texture 用）
// ブラウザ版 65_vj.js の VJ_FX_FS を移植。前のコマ（残像）は Double Buffered の _SelfTexture2D から読む。
// 値は UdonSharp 版の VJ コントローラー（StageKoboVJ）が毎フレーム入れる。
Shader "StageKobo/VJFx"
{
    Properties
    {
        _A ("デッキA", 2D) = "black" {}
        _B ("デッキB", 2D) = "black" {}
        _Txt ("文字", 2D) = "black" {}
        _Xf ("クロスフェーダー", Float) = 0
        _Asp ("縦横比", Float) = 1.7778
        _ResX ("横のピクセル数", Float) = 640
        _ResY ("縦のピクセル数", Float) = 360
        _T ("時間", Float) = 0
        _ShakeX ("揺れX", Float) = 0
        _ShakeY ("揺れY", Float) = 0
        _ZoomP ("ズーム", Float) = 0
        _Rot ("回転", Float) = 0
        _Mirror ("ミラー", Float) = 0
        _Kale ("万華鏡", Float) = 0
        _KaleRot ("万華鏡の回転", Float) = 0
        _Pix ("モザイク", Float) = 0
        _Glitch ("グリッチ", Float) = 0
        _GlitchSeed ("グリッチの種", Float) = 0
        _Rgb ("RGBずれ", Float) = 0
        _Fb ("残像", Float) = 0
        _Edge ("ネオン輪郭", Float) = 0
        _ColMode ("色の付け方", Float) = 0
        _Hue ("色相", Float) = 0
        _Post ("ポスター", Float) = 0
        _Invert ("反転", Float) = 0
        _Scan ("走査線", Float) = 0
        _Flash ("フラッシュ", Float) = 0
        _Strobe ("ストロボ", Float) = 0
        _Bright ("明るさ", Float) = 1
        _Black ("暗転", Float) = 0
        _TxtOn ("文字を重ねる", Float) = 0
        _TxtAsp ("文字の縦横比", Float) = 4
        _TxtK ("文字のドン", Float) = 0
        _C1 ("色1", Color) = (0.56,0.83,1,1)
        _C2 ("色2", Color) = (1,0.54,0.85,1)
        _C3 ("色3", Color) = (1,1,1,1)
    }
    SubShader
    {
        Lighting Off
        Blend One Zero
        Pass
        {
            Name "VJFx"
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 3.5
            #include "SKPattern.cginc"

            sampler2D _A, _B, _Txt;
            float _Xf, _Asp, _ResX, _ResY, _T, _ShakeX, _ShakeY, _ZoomP, _Rot, _Mirror, _Kale, _KaleRot, _Pix, _Glitch, _GlitchSeed, _Rgb, _Fb, _Edge;
            float _ColMode, _Hue, _Post, _Invert, _Scan, _Flash, _Strobe, _Bright, _Black, _TxtOn, _TxtAsp, _TxtK;
            float4 _C1, _C2, _C3;

            static const float3 LUM = float3(0.299, 0.587, 0.114);
            float2 glmod2f(float2 x, float y) { return x - y * floor(x / y); }
            float3 src(float2 uv)
            {
                uv = 1.0 - abs(1.0 - glmod2f(uv, 2.0));   // 端で折り返す
                return lerp(tex2Dlod(_A, float4(uv, 0, 0)).rgb, tex2Dlod(_B, float4(uv, 0, 0)).rgb, _Xf);
            }
            float3 hueShift(float3 c, float a)
            {
                const float3 k = float3(0.57735, 0.57735, 0.57735);
                float ca = cos(a);
                return c * ca + cross(k, c) * sin(a) + k * dot(k, c) * (1.0 - ca);
            }

            float4 frag(v2f_customrendertexture IN) : COLOR
            {
                float2 vUv = IN.localTexcoord.xy;
                float2 c = (vUv - 0.5) * float2(_Asp, 1.0);
                c += float2(_ShakeX, _ShakeY);
                c /= 1.0 + _ZoomP;
                c = rot2(c, _Rot);
                if (_Mirror > 2.5) c = abs(c); else if (_Mirror > 1.5) c.y = abs(c.y); else if (_Mirror > 0.5) c.x = abs(c.x);
                if (_Kale > 1.5)
                {
                    float a = atan2(c.y, c.x) + _KaleRot, r = length(c), s = 6.2831853 / _Kale;
                    a = glmod(a, s); a = abs(a - s * 0.5); c = float2(cos(a), sin(a)) * r;
                }
                if (_Pix > 0.001) { float px = lerp(0.006, 0.07, _Pix); c = (floor(c / px) + 0.5) * px; }
                float2 uv = c / float2(_Asp, 1.0) + 0.5;
                if (_Glitch > 0.001)
                {
                    float row = floor(uv.y * 16.0 + h11(_GlitchSeed) * 5.0); float r = h11(row * 1.37 + _GlitchSeed);
                    if (r < _Glitch * 0.6) uv.x += (h11(row * 3.1 + _GlitchSeed * 1.7) - 0.5) * 0.35 * _Glitch;
                    if (h11(row * 7.7 + _GlitchSeed) < _Glitch * 0.25) uv.y += 0.04 * _Glitch;
                }
                float2 off = (vUv - 0.5) * _Rgb * 0.05 + float2(_Glitch * 0.015, 0.0);
                float3 col = float3(src(uv + off).r, src(uv).g, src(uv - off).b);
                if (_Edge > 0.5)
                {
                    float2 e = 1.5 / float2(_ResX, _ResY);
                    float l = dot(src(uv - float2(e.x, 0.0)), LUM), rr = dot(src(uv + float2(e.x, 0.0)), LUM);
                    float u = dot(src(uv + float2(0.0, e.y)), LUM), d = dot(src(uv - float2(0.0, e.y)), LUM);
                    float g = clamp(length(float2(rr - l, u - d)) * 4.0, 0.0, 1.5);
                    col = col * 0.12 + lerp(_C1.rgb, _C3.rgb, clamp(g - 0.5, 0.0, 1.0)) * g * 1.3;
                }
                float lum = dot(col, LUM);
                if (_ColMode > 2.5) col = float3(lum, lum, lum);
                else if (_ColMode > 1.5) col = lerp(_C1.rgb * 0.1, _C2.rgb * 1.1, smoothstep(0.03, 0.85, lum));
                else if (_ColMode > 0.5)
                {
                    float t = clamp(lum * 1.1, 0.0, 1.0);
                    col = t < 0.33 ? lerp(float3(0, 0, 0), _C1.rgb, t / 0.33) : (t < 0.66 ? lerp(_C1.rgb, _C2.rgb, (t - 0.33) / 0.33) : lerp(_C2.rgb, _C3.rgb, (t - 0.66) / 0.34));
                }
                if (abs(_Hue) > 0.0005) col = max(hueShift(col, _Hue), 0.0);
                if (_Post > 0.5) col = floor(col * 4.0 + 0.5) / 4.0;
                col *= _Bright;
                if (_Fb > 0.001)
                {
                    float2 fc = rot2((vUv - 0.5), 0.012) * 0.965 + 0.5;
                    col = max(col, tex2Dlod(_SelfTexture2D, float4(fc, 0, 0)).rgb * 0.9 * _Fb);
                }
                if (_Invert > 0.001) col = lerp(col, max(1.0 - col, 0.0), _Invert);
                if (_Scan > 0.5)
                {
                    col *= 0.8 + 0.2 * sin(vUv.y * _ResY * 1.7);
                    col += (h11(floor(vUv.y * _ResY * 0.5) + floor(_T * 24.0)) - 0.5) * 0.05;
                    float2 v = vUv - 0.5; col *= 1.0 - dot(v, v) * 0.9;
                }
                if (_TxtOn > 0.001)
                {
                    float2 q = (vUv - 0.5) * float2(_Asp, 1.0);
                    float hh = min(0.3, 0.9 * _Asp / _TxtAsp) * (1.0 + 0.12 * _TxtK);
                    float2 tuv = float2(q.x / (hh * _TxtAsp), q.y / hh) + 0.5;
                    float inb = step(0.0, tuv.x) * step(tuv.x, 1.0) * step(0.0, tuv.y) * step(tuv.y, 1.0);
                    float4 tx = tex2D(_Txt, clamp(tuv, 0.0, 1.0)) * inb;
                    col = lerp(col, lerp(float3(1.2, 1.2, 1.2), _C3.rgb * 1.3, 0.35) * (0.6 + 0.6 * _TxtK), tx.a * _TxtOn);
                }
                col = lerp(col, float3(1.25, 1.25, 1.25), clamp(_Flash, 0.0, 1.0));
                col = lerp(col, float3(1.4, 1.4, 1.4), _Strobe);
                col *= 1.0 - _Black;
                return float4(max(col, 0.0), 1.0);
            }
            ENDCG
        }
    }
}
