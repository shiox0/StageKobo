// すてーじ工房：光るもの（ネオン・エッジライト・LEDドット・ペンライト・灯体のレンズ）
// GLB の色は 0〜1 に丸められてしまうので、HDR の強さ（_Emission）はインポーターがマテリアル名から復元する。
// _PALETTE_ON のときは照明と同じ色チャンネル（_Slot / _C1-3 / _Intensity）で光る（レンズ用）。
// 音に反応（AudioLink）：_ALAmount = 強さ
//   _ALMode 0（ステージのふち・階段の LED など）：低音でふわっと明るくなる（少しなめらかに）
//   _ALMode 1（レンズ。_PALETTE_ON も）：ビームと同じく低音で跳ねる
//   _ALMode 2（ネオン装飾・文字）：低音で大きく明るさが変わる
// _Anim：ネオン・文字の「動き」（ブラウザ版の anim）。1〜3 ネオン（脈打つ・点滅・虹色）、4〜6 文字（脈打つ・点滅・虹色）
//   脈打つは、音があると低音で脈打つ
// _MAINTEX_ON：文字・ロゴ（テクスチャのアルファで形を抜く。加算のときは rgb × a）
Shader "StageKobo/Glow"
{
    Properties
    {
        _Color ("色", Color) = (1,1,1,1)
        _Emission ("発光の強さ", Float) = 2
        _Opacity ("不透明度", Range(0,1)) = 1
        [Toggle(_VERTEXCOLOR_ON)] _UseVertexColor ("頂点カラーを使う", Float) = 0
        [Toggle(_PALETTE_ON)] _UsePalette ("照明の色に従う（レンズ用）", Float) = 0
        _C1 ("色1", Color) = (1,1,1,1)
        _C2 ("色2", Color) = (1,1,1,1)
        _C3 ("色3", Color) = (1,1,1,1)
        _Slot ("色スロット", Float) = 0
        _Hue ("虹の色相", Float) = 0
        _Intensity ("明るさ（演出）", Float) = 1
        _Master ("マスター", Float) = 1
        _ALAmount ("音に反応（AudioLink）の強さ", Range(0,1)) = 0
        _ALCenter ("ステージの真ん中（ワールド）", Vector) = (0,0,0,0)
        [Enum(Decor,0,Kick,1,Neon,2)] _ALMode ("音への反応（0 = ふわっと / 1 = 低音で跳ねる / 2 = 大きく）", Float) = 0
        [Enum(None,0,Pulse,1,Blink,2,Rainbow,3,TextPulse,4,TextBlink,5,TextRainbow,6)] _Anim ("動き（ネオン・文字）", Float) = 0
        [Toggle(_MAINTEX_ON)] _UseMainTex ("テクスチャで形を抜く（文字・ロゴ）", Float) = 0
        _MainTex ("テクスチャ（文字・ロゴ）", 2D) = "white" {}
        _AnimX ("虹色のずれ（置いた位置）", Float) = 0
        _BPM ("BPM（同期していないとき）", Float) = 128
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _VERTEXCOLOR_ON
            #pragma shader_feature_local _PALETTE_ON
            #pragma shader_feature_local _MAINTEX_ON
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "SKCommon.cginc"

            float4 _Color, _C1, _C2, _C3, _ALCenter;
            float _Emission, _Opacity, _Slot, _Hue, _Intensity, _Master, _DstBlend, _ALAmount, _ALMode, _Anim, _AnimX, _BPM;
            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                UNITY_FOG_COORDS(1)
                float al : TEXCOORD2;       // 音に反応した明るさの倍率
                float2 uv : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o, o.pos);
                // ネオン・文字の「動き」（拍で脈打つ・点滅）。ブラウザ版の式
                float k = 1.0;
                float an = floor(_Anim + 0.5);
                bool pulse = an == 1.0 || an == 4.0;
                if (pulse || an == 2.0 || an == 5.0)
                {
                    float beat = skBeat(_BPM);
                    float e = exp(-frac(beat) * 3.0);
                    bool odd = fmod(floor(beat * 2.0), 2.0) > 0.5;
                    if (an == 1.0) k = 0.45 + 0.75 * e;
                    else if (an == 4.0) k = 0.75 + 0.45 * e;
                    else k = odd ? 1.0 : (an == 2.0 ? 0.1 : 0.15);
                }
                float al = 1.0;
                float w = skALWeight(_ALAmount);
                if (w > 0.0)
                {
                    bool kick = _ALMode > 0.5 && _ALMode < 1.5;
                    #ifdef _PALETTE_ON
                    kick = true;
                    #endif
                    if (kick)
                    {
                        float3 op = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);   // 灯体の位置
                        al = skALGain(skALBand(0, min(length(op.xz - _ALCenter.xz) * 1.2, 20.0)), w);
                    }
                    else if (pulse) k = lerp(k, 0.3 + 1.1 * skALBand(0, 0), saturate(w / max(_ALAmount, 0.05)));   // 脈打つ → 低音で（拍のときより大きく）
                    else if (_ALMode > 1.5) al = lerp(1.0, 0.25 + 1.5 * skALSmooth(0, 13), w);   // ネオン・文字：大きく
                    else al = lerp(1.0, 0.55 + 0.9 * skALSmooth(0, 11), w * 0.7);
                }
                o.al = k * al;
                return o;
            }

            float3 hsv2rgb(float3 c)
            {
                float3 k = saturate(abs(fmod(c.x * 6.0 + float3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0);
                return c.z * lerp(float3(1.0, 1.0, 1.0), k, c.y);
            }

            float3 skPalette()
            {
                if (_Slot < 0.5) return _C1.rgb;
                if (_Slot < 1.5) return _C2.rgb;
                if (_Slot < 2.5) return _C3.rgb;
                float3 c = hsv2rgb(float3(frac(_Hue), 0.72, 0.94));
                #ifndef UNITY_COLORSPACE_GAMMA
                c = GammaToLinearSpace(c);
                #endif
                return c;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 c;
                #ifdef _PALETTE_ON
                c = skPalette() * (0.3 + 3.0 * _Intensity) * _Master;
                #else
                c = _Color.rgb * _Emission * _Master;
                float an = floor(_Anim + 0.5);
                if (an == 3.0) c = skHsl(frac(_Time.y * 0.12 + _AnimX * 0.05), 0.85, 0.6) * max(_Color.r, max(_Color.g, _Color.b)) * _Emission * _Master;   // ネオンの虹色
                else if (an == 6.0) c = skHsl(frac(_Time.y * 0.1), 0.8, 0.6) * _Emission * _Master;   // 文字の虹色
                #endif
                float alpha = _Opacity;
                #ifdef _MAINTEX_ON
                float4 tx = tex2D(_MainTex, i.uv);
                c *= tx.rgb;
                alpha *= tx.a;
                #endif
                #ifdef _VERTEXCOLOR_ON
                c *= i.color.rgb;
                #endif
                c *= i.al;
                float4 col = float4(c, alpha);
                // 加算のときは霧の色を足さない（黒に向かって消える）
                if (_DstBlend > 0.5 && _DstBlend < 1.5) { UNITY_APPLY_FOG_COLOR(i.fogCoord, col, float4(0, 0, 0, 0)); }
                else { UNITY_APPLY_FOG(i.fogCoord, col); }
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
