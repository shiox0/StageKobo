// すてーじ工房：ステージ裏の確認用モニター（カメラの映像・VJ の出力・デッキ）
// 照明の影響を受けない画面。_MainTex は RenderTexture / Custom Render Texture（UdonSharp 版の VJ が「編集中のデッキ」を差し替える）
Shader "StageKobo/Screen"
{
    Properties
    {
        _MainTex ("映像", 2D) = "black" {}
        _Bright ("明るさ", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Bright;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb * _Bright;
                // 画面のふちを少し暗く（モニターらしく）
                float2 q = i.uv - 0.5;
                c *= 1.0 - dot(q, q) * 0.35;
                return float4(max(c, 0.0), 1.0);
            }
            ENDCG
        }
    }
}
