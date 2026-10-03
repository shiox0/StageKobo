// すてーじ工房：客席の人のシルエット（拍に合わせて弾む）
// uv.x に1人ずつの位相が入っている（ブラウザ版の書き出しで設定）。
// 弾み方はブラウザ版と同じ：|sin((拍 + 位相*0.3)π)|² × 0.07 × ノリ
// 輪郭がステージの色（パレットの色1）でうっすら光る。
// 音に反応（AudioLink）：低音（キック）で弾む。1人ずつ少しずつ遅れて（位相）、ふちの光も音で明るくなる。_ALAmount = 強さ
Shader "StageKobo/Crowd"
{
    Properties
    {
        _Color ("体の色", Color) = (0.05,0.055,0.09,1)
        _C1 ("ふちの色（照明の色1）", Color) = (0.56,0.83,1,1)
        [HideInInspector] _C2 ("照明の色2（未使用）", Color) = (1,1,1,1)
        [HideInInspector] _C3 ("照明の色3（未使用）", Color) = (1,1,1,1)
        _Rim ("ふちの光", Float) = 0.35
        _Jump ("ノリ（拍で弾む）", Float) = 0.5
        _BPM ("BPM（同期していないとき）", Float) = 128
        _ALAmount ("音に反応（AudioLink）の強さ", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "SKCommon.cginc"

            float4 _Color, _C1;
            float _Rim, _Jump, _BPM, _ALAmount;

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
                float3 n : TEXCOORD0;
                float3 wp : TEXCOORD1;
                float rimK : TEXCOORD2;     // ふちの光の倍率（音に反応）
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float s = sin((skBeat(_BPM) + v.uv.x * 0.3) * UNITY_PI);
                float jump = s * s;
                o.rimK = 1.0;
                float w = skALWeight(_ALAmount);
                if (w > 0.0)
                {
                    float b = skALBand(0, v.uv.x * 12.0);   // 1人ずつ 0〜0.13 秒遅れて
                    jump = lerp(jump, smoothstep(0.12, 0.85, b), w);
                    o.rimK = skALGain(skALSmooth(0, 12), w);
                }
                float3 p = v.vertex.xyz + float3(0, jump * 0.07 * _Jump, 0);
                o.pos = UnityObjectToClipPos(float4(p, 1));
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, float4(p, 1)).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.n);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                float3 amb = ShadeSH9(float4(n, 1));
                float ndl = saturate(dot(n, normalize(_WorldSpaceLightPos0.xyz)));
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0) * _Rim * i.rimK;
                float3 c = _Color.rgb * (amb + _LightColor0.rgb * ndl) + _C1.rgb * rim;
                return float4(c, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
