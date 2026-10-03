// すてーじ工房：照明のビーム（フェイクボリューメトリック）
// ブラウザ版 20_materials.js の BEAM_FS をそのまま移植。円錐メッシュ（+Y方向・uv.y=根元0→先端1）に貼る。
// _Slot / _Hue / _C1-3 / _Intensity / _Master は Animator から動かす（リモコンのチャンネル）。
// 音に反応（AudioLink）：低音で明るさが跳ねる。ステージの真ん中（_ALCenter）から外の灯体ほど少し遅れて伝わる。_ALAmount = 強さ
Shader "StageKobo/Beam"
{
    Properties
    {
        _C1 ("色1", Color) = (1,1,1,1)
        _C2 ("色2", Color) = (1,1,1,1)
        _C3 ("色3", Color) = (1,1,1,1)
        _Slot ("色スロット (0-2 / 3=虹)", Float) = 0
        _Hue ("虹の色相", Float) = 0
        _Intensity ("明るさ（演出）", Float) = 1
        _Master ("マスター", Float) = 1
        _Gain ("強さ（灯体の種類）", Float) = 1
        _Len ("長さ (m)", Float) = 20
        _Haze ("スモーク量", Float) = 0.7
        _EndFade ("先端を消す", Range(0,1)) = 1
        _ALAmount ("音に反応（AudioLink）の強さ", Range(0,1)) = 0
        _ALCenter ("ステージの真ん中（ワールド）", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "SKAudioLink.cginc"

            float4 _C1, _C2, _C3, _ALCenter;
            float _Slot, _Hue, _Intensity, _Master, _Gain, _Len, _Haze, _EndFade, _ALAmount;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float y : TEXCOORD0;
                float3 n : TEXCOORD1;
                float3 v : TEXCOORD2;
                float3 wp : TEXCOORD3;
                float al : TEXCOORD4;       // 音に反応した明るさの倍率
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.y = v.uv.y;
                float3 vp = UnityObjectToViewPos(v.vertex.xyz);
                o.n = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                o.v = normalize(-vp);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.al = 1.0;
                float w = skALWeight(_ALAmount);
                if (w > 0.0)
                {
                    float3 op = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);   // 灯体の位置
                    o.al = skALGain(skALBand(0, min(length(op.xz - _ALCenter.xz) * 1.2, 20.0)), w);
                }
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
                float d = i.y * _Len;
                float along = exp(-d * 0.05) * smoothstep(0.0, 0.35, d);
                along *= lerp(1.0, 1.0 - smoothstep(0.75, 1.0, i.y), _EndFade);
                float edge = pow(abs(dot(normalize(i.n), normalize(i.v))), 1.6);
                float camFade = smoothstep(0.6, 5.0, distance(i.wp, _WorldSpaceCameraPos));
                float dust = 0.85 + 0.15 * sin(i.wp.y * 3.0 + _Time.y * 0.7 + i.wp.x * 2.0);
                float a = _Gain * _Intensity * _Master * _Haze * along * edge * camFade * dust * 0.28 * i.al;
                return float4(skPalette() * max(a, 0.0), 0.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
