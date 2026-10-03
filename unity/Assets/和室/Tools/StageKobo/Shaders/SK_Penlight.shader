// すてーじ工房：客席のペンライト（1本ずつ拍に合わせて振る）
// ブラウザ版の書き出しで、ペンライトは「まっすぐ立てた棒」を1つのメッシュにまとめてある。
// UV に 1本ずつの情報が入っている：
//   uv.x = 色相(0-99 の整数)*4 + (右手なら 2) + 位相(0-1)
//   uv.y = 棒の根元からの高さ(0-1)。glTF の取り込みで上下が反転するので _FlipV で戻す
// 振り方はブラウザ版と同じ式：回転 = Euler(0.25, 0, sin((拍 + 位相*0.15)π)*0.5*振り + 左右*0.1)
// _Mode 0 = 書き出した色（頂点カラー） / 1 = 照明の色（パレット） / 2 = 白 / 3 = 虹
// 音に反応（AudioLink）：振りは拍のまま、弾みは低音（キック）、明るさは中高音（歌・メロディ）で少し揺れる。_ALAmount = 強さ
Shader "StageKobo/Penlight"
{
    Properties
    {
        _Mode ("色 (0=書き出した色 1=照明の色 2=白 3=虹)", Float) = 0
        _Emission ("明るさ", Float) = 1.25
        _C1 ("照明の色1", Color) = (1,1,1,1)
        _C2 ("照明の色2", Color) = (1,1,1,1)
        _C3 ("照明の色3", Color) = (1,1,1,1)
        _Len ("棒の長さ (m)", Float) = 0.28
        _Sway ("振りの大きさ", Float) = 1
        _Jump ("ノリ（拍で弾む）", Float) = 0.5
        _BPM ("BPM（同期していないとき）", Float) = 128
        [Toggle] _AxisFlipX ("X反転で取り込んだ（glTFast）", Float) = 1
        [Toggle] _FlipV ("UVの上下を戻す（ペンライトの根元がずれるときは切り替え）", Float) = 1
        _ALAmount ("音に反応（AudioLink）の強さ", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "SKCommon.cginc"

            float4 _C1, _C2, _C3;
            float _Mode, _Emission, _Len, _Sway, _Jump, _BPM, _AxisFlipX, _FlipV, _ALAmount;

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
                float3 col : TEXCOORD0;
                float h : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 rot2(float2 v, float a) { float s = sin(a), c = cos(a); return float2(v.x * c - v.y * s, v.x * s + v.y * c); }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float hq = floor(v.uv.x * 0.25);
                float r = v.uv.x - hq * 4.0;
                float side = r >= 2.0 ? 1.0 : -1.0;
                float ph = r - (side > 0 ? 2.0 : 0.0);
                float h = saturate(_FlipV > 0.5 ? 1.0 - v.uv.y : v.uv.y);
                float hue = (hq + 0.5) / 100.0;

                float beat = skBeat(_BPM);
                float az = sin((beat + ph * 0.15) * UNITY_PI) * 0.5 * _Sway + side * 0.1;
                float ax = 0.25;
                // three.js（右手系）→ Unity（左手系）の取り込み方で回転の向きが変わる
                if (_AxisFlipX > 0.5) az = -az; else ax = -ax;

                float3 p = v.vertex.xyz;
                float3 pivot = p - float3(0, h * _Len, 0);
                float3 d = p - pivot;
                d.xy = rot2(d.xy, az);     // Z軸まわり
                d.yz = rot2(d.yz, ax);     // X軸まわり
                float s = sin((beat + ph * 0.3) * UNITY_PI);
                float jump = s * s, glow = 1.0;
                float w = skALWeight(_ALAmount);
                if (w > 0.0)
                {
                    jump = lerp(jump, smoothstep(0.12, 0.85, skALBand(0, ph * 12.0)), w);
                    glow = lerp(1.0, 0.7 + 0.6 * skALSmooth(2, 10), w);
                }
                float jy = jump * 0.07 * _Jump;
                p = pivot + d + float3(0, jy, 0);
                o.pos = UnityObjectToClipPos(float4(p, 1));

                float3 c;
                if (_Mode < 0.5) c = v.color.rgb * _Emission;
                else if (_Mode < 1.5)
                {
                    float slot = fmod(floor(hue * 3.0), 3.0);
                    c = (slot < 0.5 ? _C1.rgb : slot < 1.5 ? _C2.rgb : _C3.rgb) * 1.25;
                }
                else if (_Mode < 2.5) c = float3(1.25, 1.25, 1.25);
                else c = skHsl(frac(hue + _Time.y * 0.05), 0.9, 0.6) * 1.25;
                o.col = c * glow;
                o.h = h;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // 先っぽほど少し明るく（ペンライトらしく）
                return float4(i.col * lerp(0.75, 1.15, i.h), 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
