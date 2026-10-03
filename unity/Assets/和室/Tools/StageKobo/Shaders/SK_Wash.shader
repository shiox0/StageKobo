// すてーじ工房：カラーウォッシュ（舞台の床を色で照らす “フェイク” スポットライト）
// ブラウザ版の washLights（客席側の上から舞台を照らす色付きスポット4灯＋奥の1灯）の再現。
// 本物のライトは VRChat では重いので、床のメッシュをもう一度「加算」で描き、
// スポットライトの円錐と同じ計算でにじむ光だまりを作る。床の形（丸・花道など）にそのまま沿う。
// _Slot / _Hue / _C1-3 / _Intensity / _Master / _On は Animator から動かす。
// 音に反応（AudioLink）：低音で光だまりがふわっと明るくなる（少しなめらかに）。_ALAmount = 強さ
Shader "StageKobo/Wash"
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
        _On ("ウォッシュ ON/OFF", Float) = 1
        _Gain ("強さ", Float) = 0.1
        _LightPos ("ライトの位置（ステージ基準）", Vector) = (0,12,14,0)
        _LightDir ("ライトの向き（ステージ基準）", Vector) = (0,-0.6,-0.8,0)
        _CosOuter ("cos(外側の角度)", Float) = 0.83
        _CosInner ("cos(内側の角度)", Float) = 0.98
        _ALAmount ("音に反応（AudioLink）の強さ", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Offset -1, -1
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

            float4 _C1, _C2, _C3, _LightPos, _LightDir;
            float _Slot, _Hue, _Intensity, _Master, _On, _Gain, _CosOuter, _CosInner, _ALAmount;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 op : TEXCOORD0;
                float al : TEXCOORD1;       // 音に反応した明るさの倍率
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.op = v.vertex.xyz;
                o.al = 1.0;
                float w = skALWeight(_ALAmount);
                if (w > 0.0) o.al = skALGain(skALSmooth(0, 11), w);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float k = _Gain * _Intensity * _Master * _On * i.al;
                if (k <= 0.0001) return float4(0, 0, 0, 0);
                float3 toL = _LightPos.xyz - i.op;
                float3 L = normalize(toL);
                float spot = smoothstep(_CosOuter, _CosInner, dot(-L, normalize(_LightDir.xyz)));
                float ndl = saturate(L.y);           // 床は上向き
                float3 c = skPaletteColor(_Slot, _Hue, _C1.rgb, _C2.rgb, _C3.rgb);
                return float4(c * (k * spot * ndl), 0);
            }
            ENDCG
        }
    }
    FallBack Off
}
