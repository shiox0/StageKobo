// すてーじ工房：特効パーティクル（テクスチャ不要）
// _Shape 0 = ふんわり丸（スモーク）、1 = キラキラ（スパーク・空中のキラキラ）、2 = 四角（紙吹雪）
Shader "StageKobo/Particle"
{
    Properties
    {
        _Shape ("形 (0=丸 1=キラキラ 2=四角)", Float) = 0
        _Boost ("明るさ倍率", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(Off,0,On,1)] _ZWrite ("ZWrite", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _Shape, _Boost;

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
                float2 uv : TEXCOORD0;
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
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.uv - 0.5;
                float r = length(p) * 2.0;
                float a;
                if (_Shape < 0.5)
                {
                    a = saturate(1.0 - r);
                    a = a * a * (0.6 + 0.4 * a);
                }
                else if (_Shape < 1.5)
                {
                    float core = saturate(1.0 - r * 2.5); core *= core;
                    float crs = max(saturate(1.0 - abs(p.x) * 14.0) * saturate(1.0 - abs(p.y) * 2.2),
                                    saturate(1.0 - abs(p.y) * 14.0) * saturate(1.0 - abs(p.x) * 2.2));
                    a = saturate(core + crs * 0.9);
                }
                else a = 1.0;
                float4 col = i.color;
                col.rgb *= _Boost;
                col.a *= a;
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
