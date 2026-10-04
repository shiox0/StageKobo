// すてーじ工房：ドットLEDモニター（ブラウザ版 20_materials.js の LED_FS / PATTERN_GLSL を移植）
// _Src 0 = テクスチャ（ステージカメラの RenderTexture・画像・文字）、1 = VJパターン、2 = 消灯
// VJ リモコンの映像（_VJTex）は _Src より優先：_VJ（モニターの「VJ映像」・Animator）／_VJAlways（映すもの＝VJリモコンの映像）
// ／_VJBackdrop × _Udon_SKVJBack（背景の LED。VJ リモコンの「背景LED」）。切り抜きは _Udon_SKVJMap（0 = 1枚ずつ、1 = つなげて1枚）
// 音に反応（AudioLink）：VJパターンの「拍のドン」が低音になり、明るさも音で少し揺れる（イコライザーは本物の音）。_ALAmount = 強さ
// VJパターン：0 三角コンフェッティ / 1 パステル虹 / 2 銀河 / 3 三角トンネル / 4 ゆめかわ流体 / 5 イコライザー / 6 ポップドット / 7 キラキラ星
Shader "StageKobo/LEDDot"
{
    Properties
    {
        _MainTex ("映像（RenderTexture・画像）", 2D) = "black" {}
        _MainRect ("映像の切り抜き (x, y, 幅, 高さ)：縦横比を変えずに貼る", Vector) = (0,0,1,1)
        _MainLod ("ドット1個ぶんの平均（画像・文字）", Float) = 0
        _Src ("映すもの (0=映像 1=VJ 2=消灯)", Float) = 1
        _Pattern ("VJパターン番号", Float) = 0
        _DotsX ("ドット数 横", Float) = 96
        _DotsY ("ドット数 縦", Float) = 54
        _Aspect ("縦横比", Float) = 1.78
        _Gap ("ドットの隙間", Range(0,0.9)) = 0.35
        [Toggle] _Round ("丸ドット", Float) = 1
        [Toggle] _Led ("ドット表示", Float) = 1
        [Toggle] _Flip ("左右反転", Float) = 0
        [Toggle] _FlipV ("UVの上下を戻す（GLB の取り込みで上下が反転するため。映像が逆さなら切り替え）", Float) = 1
        _Bright ("明るさ", Float) = 1
        _BPM ("BPM（拍に合わせる模様用）", Float) = 128
        _C1 ("パターン色1", Color) = (0.56,0.83,1,1)
        _C2 ("パターン色2", Color) = (1,0.54,0.85,1)
        _C3 ("パターン色3", Color) = (1,1,1,1)
        _Tint ("色味", Color) = (1,1,1,1)
        _VJTex ("VJ リモコンの映像", 2D) = "black" {}
        _VJ ("VJ 映像を映す（モニターの切り替え）", Float) = 0
        _VJAlways ("いつも VJ 映像（映すもの＝VJリモコンの映像）", Float) = 0
        _VJBackdrop ("背景の LED（VJ リモコンの「背景LED」で切り替え）", Float) = 0
        _VJRect ("VJ 映像の切り抜き：1枚ずつ (x, y, 幅, 高さ)", Vector) = (0,0,1,1)
        _VJRectSpan ("VJ 映像の切り抜き：つなげて1枚", Vector) = (0,0,1,1)
        _VJLod ("ドット1個ぶんの平均（1枚ずつ）", Float) = 0
        _VJLodSpan ("ドット1個ぶんの平均（つなげて1枚）", Float) = 0
        _ALAmount ("音に反応（AudioLink）の強さ", Range(0,1)) = 0
        _Sel ("映すものの切り替え（-1 = いつもの / 0 カメラ1 / 1 カメラ2 / 2 VJ / 3 映像）", Float) = -1
        _CamA ("カメラ1", 2D) = "black" {}
        _CamARect ("カメラ1の切り抜き", Vector) = (0,0,1,1)
        _CamB ("カメラ2", 2D) = "black" {}
        _CamBRect ("カメラ2の切り抜き", Vector) = (0,0,1,1)
        _VideoTex ("映像（YamaPlayer が入れる）", 2D) = "black" {}
        _MonAsp ("この画面の縦横比（映像の切り抜き用）", Float) = 1.78
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "SKCommon.cginc"
            #include "SKPattern.cginc"

            sampler2D _MainTex;
            float4 _MainRect;
            float _MainLod;
            float _Src, _Pattern, _DotsX, _DotsY, _Aspect, _Gap, _Round, _Led, _Flip, _FlipV, _Bright, _BPM;
            float4 _C1, _C2, _C3, _Tint;
            sampler2D _VJTex;
            float _VJ, _VJAlways, _VJBackdrop, _VJLod, _VJLodSpan;
            float4 _VJRect, _VJRectSpan;
            float _Udon_SKVJBack;   // VJ リモコンの「背景LED」（VRCShader.SetGlobalFloat）
            float _Udon_SKVJMap;    // 0 = 1枚ずつ、1 = つなげて1枚
            float _ALAmount;
            float _Sel, _MonAsp;
            sampler2D _CamA, _CamB, _VideoTex;
            float4 _CamARect, _CamBRect, _VideoTex_TexelSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 al : TEXCOORD1;      // 音に反応：x 度合い, y 低音のドン, z なめらかな音量
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                // glTF の UV は「上が 0」、Unity は「下が 0」。glTFast / UniGLTF は取り込みで V を反転するので、ブラウザ版と同じ向きに戻す
                o.uv = float2(v.uv.x, _FlipV > 0.5 ? 1.0 - v.uv.y : v.uv.y);
                // 音（AudioLink）は面ごとに同じ値なので、頂点で1回だけ読む
                float w = skALWeight(_ALAmount);
                o.al = float3(0, 0, 0);
                if (w > 0.0) o.al = float3(w, skALBand(0, 0), max(skALSmooth(0, 12), skALSmooth(1, 12)));
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.uv;
                if (_Flip > 0.5) uv.x = 1.0 - uv.x;
                float2 dots = float2(_DotsX, _DotsY);
                float2 g = uv * dots;
                float2 suv = _Led > 0.5 ? (floor(g) + 0.5) / dots : uv;
                float t = _Time.y;
                float beat = skBeat(_BPM);   // UdonSharp 版では全員で同期した拍
                float3 c;
                // 操作パネルの「モニター」：カメラ1・カメラ2・VJ・映像（_Sel。-1 のときは今まで通り）
                bool vj = _VJ > 0.5 || _VJAlways > 0.5 || (_VJBackdrop > 0.5 && _Udon_SKVJBack > 0.5);
                if (_Sel > -0.5) vj = _Sel > 1.5 && _Sel < 2.5;
                if (_Sel > -0.5 && _Sel < 0.5) c = tex2Dlod(_CamA, float4(_CamARect.xy + suv * _CamARect.zw, 0, 0)).rgb;
                else if (_Sel > 0.5 && _Sel < 1.5) c = tex2Dlod(_CamB, float4(_CamBRect.xy + suv * _CamBRect.zw, 0, 0)).rgb;
                else if (_Sel > 2.5)
                {
                    // 映像（YamaPlayer）：縦横比を変えずに、まん中を切り抜く。映像が無いときは黒
                    float ta = _VideoTex_TexelSize.w > 0.5 ? _VideoTex_TexelSize.z / _VideoTex_TexelSize.w : 1.78;
                    float2 sz = ta > _MonAsp ? float2(_MonAsp / ta, 1.0) : float2(1.0, ta / _MonAsp);
                    float2 vuv = (1.0 - sz) * 0.5 + suv * sz;
                    float lod = _Led > 0.5 ? max(0.0, log2(max(1.0, _VideoTex_TexelSize.z * sz.x / _DotsX)) - 0.5) : 0.0;
                    c = _VideoTex_TexelSize.z > 4.5 ? tex2Dlod(_VideoTex, float4(vuv, 0, lod)).rgb : float3(0, 0, 0);
                }
                else if (vj)
                {
                    bool span = _Udon_SKVJMap > 0.5;
                    float4 R = span ? _VJRectSpan : _VJRect;
                    c = tex2Dlod(_VJTex, float4(R.xy + suv * R.zw, 0, _Led > 0.5 ? (span ? _VJLodSpan : _VJLod) : 0.0)).rgb;
                }
                else if (_Src < 0.5) c = tex2Dlod(_MainTex, float4(_MainRect.xy + suv * _MainRect.zw, 0, _Led > 0.5 ? _MainLod : 0.0)).rgb;
                else if (_Src < 1.5)
                {
                    skPatALw = i.al.x; skPatALPulse = i.al.y;
                    c = vjPattern((int)_Pattern, suv, t, beat, _C1.rgb, _C2.rgb, _C3.rgb, _Aspect) * skALGain(i.al.z, i.al.x * 0.5);
                }
                else c = float3(0, 0, 0);
                c *= _Tint.rgb * _Bright;
                if (_Led > 0.5)
                {
                    float2 f = frac(g) - 0.5;
                    float d = _Round > 0.5 ? length(f) : max(abs(f.x), abs(f.y));
                    float r = 0.5 * (1.0 - _Gap);
                    float w = fwidth(g.x) + fwidth(g.y);
                    float m = 1.0 - smoothstep(r - w * 0.6, r + w * 0.6, d);
                    float cov = _Round > 0.5 ? 3.14159 * r * r : 4.0 * r * r;
                    m = lerp(m, cov, smoothstep(0.35, 1.0, w));
                    c = c * m * (_Round > 0.5 ? 1.25 : 1.0) + 0.004 * (1.0 - m);
                }
                return float4(c, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
