// すてーじ工房：ステージに置く照明アセット（LEDバー・ストロボ／ブラインダー）
// ブラウザ版 50_assets.js の led_bar / strobe の update(F, i, n, xn) をそのまま移植（GLB に入るのは書き出した瞬間の色だけなので、ここで動かす）。
//   LEDバー：メッシュはインポーターが作り直す（分割1個 = 箱1個、uv.x = 下から何番目か）。
//     _Mode 0 キューに合わせる（照明の色・明るさ）／1 流れる／2 レベルメーター／3 常時点灯（本体色）
//     色（_Slot / _Hue / _C1-3）・明るさ（_Intensity）・マスター（_Master）は Animator から（ムービングライトと同じチャンネル）
//   ストロボ：小節の頭でフラッシュ（_Accent）。明るさ「ストロボ」（_DimStrobe）・照明ぜんぶ「ストロボ」（_StrobeTrig）で点滅
// 音に反応（AudioLink）：レベルメーターは本物の音（配列で並べると、並びの順に低い音 → 高い音のイコライザー。_Freq）。
//   ほかは低音で明るさが跳ねる。ストロボは「小節の頭」の代わりに強いキックで光る。_ALAmount = 強さ
Shader "StageKobo/StageLight"
{
    Properties
    {
        _Kind ("種類 (0=LEDバー 1=ストロボ)", Float) = 0
        _Mode ("LEDバーの光り方 (0=キュー 1=流れる 2=レベルメーター 3=常時点灯)", Float) = 0
        _Color ("本体色（常時点灯・ストロボ）", Color) = (1,1,1,1)
        _Power ("明るさ", Float) = 1
        _Seg ("分割数", Float) = 14
        _Idx ("同じ種類の中で左から何番目か", Float) = 0
        _Freq ("レベルメーターが拾う音の高さ（0 = 低い〜1 = 高い、-1 = 低音）", Float) = -1
        [Toggle] _Accent ("小節の頭でフラッシュ（ストロボ）", Float) = 1
        _C1 ("色1", Color) = (1,1,1,1)
        _C2 ("色2", Color) = (1,1,1,1)
        _C3 ("色3", Color) = (1,1,1,1)
        _Slot ("色スロット (0-2 / 3=虹)", Float) = 0
        _Hue ("虹の色相", Float) = 0
        _Intensity ("明るさ（演出）", Float) = 1
        _Master ("マスター", Float) = 1
        _DimStrobe ("明るさ＝ストロボ（ストロボ用）", Float) = 0
        _StrobeTrig ("照明ぜんぶ＝ストロボ（ストロボ用）", Float) = 0
        _BPM ("BPM（コントローラーが無いとき）", Float) = 128
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
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "SKCommon.cginc"

            float4 _Color, _C1, _C2, _C3;
            float _Kind, _Mode, _Power, _Seg, _Idx, _Freq, _Accent, _Slot, _Hue, _Intensity, _Master, _DimStrobe, _StrobeTrig, _BPM, _ALAmount;
            float _Udon_SKBpm;   // コントローラーのいまの BPM

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 col : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float skHash1(float n) { return frac(sin(n * 127.1 + 311.7) * 43758.5453); }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float beat = skBeat(_BPM);
                float w = skALWeight(_ALAmount);
                float wn = _ALAmount > 0.001 ? saturate(w / _ALAmount) : 0.0;   // 0〜1（曲が流れていて音が大きいと 1）
                float3 c;
                if (_Kind > 0.5)
                {
                    // ---- ストロボ：小節の頭で 1 → 0.33 秒で消える（ブラウザ版の accent）
                    float bpm = (_Udon_SKSync > 0.5 && _Udon_SKBpm > 0.5) ? _Udon_SKBpm : _BPM;
                    float sec = frac(beat * 0.25) * 4.0 * 60.0 / max(bpm, 1.0);
                    float acc = _Accent > 0.5 ? saturate(1.0 - 3.0 * sec) * 4.0 : 0.0;
                    if (w > 0.0 && _Accent > 0.5) acc = lerp(acc, smoothstep(0.7, 1.0, skALBand(0, 0)) * 3.0, wn);   // 強いキックで光る
                    float k = _DimStrobe > 0.5 ? (frac(_Time.y * 13.0) < 0.35 ? 6.0 : 0.0) : (_StrobeTrig > 0.5 ? 6.0 : acc);
                    c = _Color.rgb * (k * _Power * _Master + 0.02);
                }
                else
                {
                    // ---- LEDバー（分割 j 番目。下から）
                    float n = max(1.0, floor(_Seg + 0.5));
                    float j = floor(v.uv.x + 0.5);
                    float k = 1.0;
                    if (_Mode < 0.5)
                    {
                        k = _Intensity * (0.6 + 0.4 * sin(_Time.y * 3.0 + j * 0.5 + _Idx));
                        if (w > 0.0) k *= skALGain(skALSmooth(0, 12), w);
                    }
                    else if (_Mode < 1.5)
                    {
                        float a = fmod(floor(beat * 4.0), n), b = fmod(floor(beat * 4.0 + n * 0.5), n);
                        k = (abs(a - j) < 0.5 || abs(b - j) < 0.5) ? 1.6 : 0.12;
                        if (w > 0.0) k *= skALGain(skALBand(0, 0), w);
                    }
                    else if (_Mode < 2.5)
                    {
                        float lv = (0.25 + 0.75 * skHash1(_Idx * 3.0 + floor(beat * 2.0))) * (0.5 + 0.5 * exp(-frac(beat) * 3.0));
                        if (w > 0.0)
                        {
                            // 本物の音：並べたときは並びの順に低い音 → 高い音（_Freq）。1台だけなら低音
                            float al = _Freq > -0.5 ? skALSpectrum(_Freq) : skALBand(0, 0);
                            lv = lerp(lv, 0.04 + 0.96 * al, saturate(wn * 2.0));
                        }
                        k = j / n < lv ? 1.0 : 0.05;
                    }
                    else if (w > 0.0) k = skALGain(skALSmooth(0, 12), w);
                    float3 base = _Mode > 2.5 ? _Color.rgb : skPaletteColor(_Slot, _Hue, _C1.rgb, _C2.rgb, _C3.rgb);
                    c = base * (2.2 * k * _Master * _Power);
                }
                o.col = max(c, 0.0);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 col = float4(i.col, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
