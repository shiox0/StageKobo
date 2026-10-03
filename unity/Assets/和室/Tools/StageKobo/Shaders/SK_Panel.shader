// すてーじ工房：ステージ裏の操作卓（照明卓・VJ卓）
// 卓1台 = メッシュ1個。頂点に「何の面か」を持たせて、1つのマテリアルで描く（Editor/SKDeskBuilder.cs が作る）：
//   uv0 = 文字の場所（Textures/SK_Labels.png の x, y, 幅, 高さ。幅 0 = 文字なし）
//   uv1 = (ボタン番号, 種類, おまけ, ランプあり)  種類：0 本体 / 1 ボタン / 2 文字の板 / 3 拍のランプ / 4 数字
//   uv2 = 面の中の位置（0〜1。ボタンの側面は -1）、頂点カラー = 色（リニア）
// ランプ：ボタン番号 24 個ずつを1つの数にして _B0〜_B3（16 個）で受け取る（UdonSharp 版のコントローラーが入れる）
// 押したボタンは _PressId / _PressT で少し沈む（押した人の画面だけ）
// 拍のランプは、音に反応（AudioLink）しているときは低音で光る（音が届いているかの確認にもなる）
Shader "StageKobo/Panel"
{
    Properties
    {
        _Atlas ("文字のテクスチャ", 2D) = "black" {}
        _B0 ("ランプ 0-95", Vector) = (0,0,0,0)
        _B1 ("ランプ 96-191", Vector) = (0,0,0,0)
        _B2 ("ランプ 192-287", Vector) = (0,0,0,0)
        _B3 ("ランプ 288-383", Vector) = (0,0,0,0)
        _N0 ("数字（数・ズーム・おおきさ1・おおきさ2）", Vector) = (6,100,8,5)
        _N1 ("数字（パワー）", Vector) = (5,0,0,0)
        _PressId ("押したボタン", Float) = -1
        _PressT ("押した時刻", Float) = -99
        _PressDir ("沈む向き（オブジェクト空間）", Vector) = (0,-1,0,0)
        _PanelColor ("盤面の色（リニア）", Color) = (0.007,0.007,0.013,1)
        _BodyColor ("卓の本体の色（リニア）", Color) = (0.003,0.003,0.004,1)
        _LampColor ("ランプの色", Color) = (0.35,1,0.8,1)
        _BPM ("BPM（コントローラーが無いとき）", Float) = 128
        _DigitRect ("数字「0」の場所", Vector) = (0,0,0.0625,0.05)
        _ALAmount ("音に反応（AudioLink）の強さ", Range(0,1)) = 0
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
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "SKCommon.cginc"

            sampler2D _Atlas;
            float4 _B0, _B1, _B2, _B3, _N0, _N1, _PressDir, _PanelColor, _BodyColor, _LampColor, _DigitRect;
            float _PressId, _PressT, _BPM, _ALAmount;
            float _Udon_SKBpm;   // コントローラーのいまの BPM（VRCShader.SetGlobalFloat）

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float4 rect : TEXCOORD0;
                float4 info : TEXCOORD1;    // x 種類, y おまけ, z ランプ(0/1), w 押された(0-1)
                float2 loc : TEXCOORD2;
                float shade : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float lampBit(float slot)
            {
                float word = floor(slot / 24.0);
                float bit = slot - word * 24.0;
                float4 v = word < 3.5 ? _B0 : (word < 7.5 ? _B1 : (word < 11.5 ? _B2 : _B3));
                float c = fmod(word, 4.0);
                float w = c < 0.5 ? v.x : (c < 1.5 ? v.y : (c < 2.5 ? v.z : v.w));
                return fmod(floor(w / exp2(bit) + 0.0001), 2.0);
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float type = v.uv1.y;
                float lamp = 0.0, press = 0.0;
                if (type > 0.5 && type < 1.5)
                {
                    lamp = v.uv1.w > 0.5 ? lampBit(v.uv1.x) : 0.0;
                    float dt = _Time.y - _PressT;
                    if (abs(v.uv1.x - _PressId) < 0.5 && dt >= 0.0 && dt < 0.3)
                    {
                        press = 1.0 - dt / 0.3;
                        v.vertex.xyz += _PressDir.xyz * 0.007 * press;
                    }
                }
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.rect = v.uv0;
                o.info = float4(type, v.uv1.z, lamp, press);
                o.loc = v.uv2;
                // 照明なしの絵の具塗り：上を向いた面は明るく、横・手前は少し暗く
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                o.shade = 0.72 + 0.28 * saturate(n.y);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float type = i.info.x;
                float3 col = i.color.rgb;
                float2 L = i.loc;
                if (type < 0.5)
                {
                    col *= i.shade;
                }
                else if (type < 1.5)
                {
                    // ---- ボタン
                    bool top = L.x >= 0.0;
                    float on = i.info.z;
                    bool swatch = i.info.y > 0.5;
                    col *= top ? 1.0 : 0.85;
                    col = lerp(col, col * 1.7 + _LampColor.rgb * 0.06, on);
                    // 文字（上の面だけ。ふちを少しあける）
                    const float2 m = float2(0.04, 0.06);
                    float2 lu = saturate((L - m) / (1.0 - 2.0 * m));
                    float a = tex2D(_Atlas, i.rect.xy + lu * i.rect.zw).a;
                    float inside = (top && i.rect.z > 0.0 && all(L > m) && all(L < 1.0 - m)) ? 1.0 : 0.0;
                    col = lerp(col, float3(0.95, 0.95, 0.97), a * inside);
                    // ランプの帯（手前のふち）
                    if (top && L.y < 0.15)
                    {
                        float3 bar = swatch ? float3(1, 1, 1) : _LampColor.rgb;
                        col = lerp(col * 0.6, bar * 2.2, on);
                    }
                    // 押した瞬間に少し光る
                    col += i.info.w * 0.15;
                }
                else if (type < 2.5)
                {
                    // ---- 文字の板（見出し・名前）。地の色は貼ってある面と同じ（おまけ 0 = 盤面、1 = 本体・モニターの板）
                    float a = tex2D(_Atlas, i.rect.xy + saturate(L) * i.rect.zw).a;
                    float3 bg = (i.info.y > 0.5 ? _BodyColor.rgb : _PanelColor.rgb) * i.shade;
                    col = lerp(bg, col, a);
                }
                else if (type < 3.5)
                {
                    // ---- 拍のランプ（丸）
                    float beat = skBeat(_BPM);
                    float pulse = exp(-frac(beat) * 6.0);
                    float bar1 = frac(beat / 4.0) < 0.25 ? 1.0 : 0.6;   // 小節の頭は強く
                    float wAL = skALWeight(_ALAmount);
                    if (wAL > 0.0)
                    {
                        float k = saturate(wAL / max(_ALAmount, 0.05));
                        pulse = lerp(pulse, skALBand(0, 0), k);
                        bar1 = lerp(bar1, 1.0, k);
                    }
                    float d = length(L - 0.5);
                    float disc = smoothstep(0.5, 0.42, d);
                    col = lerp(_PanelColor.rgb * i.shade, col * (0.12 + 2.2 * pulse * bar1), disc);
                }
                else
                {
                    // ---- 数字（BPM・数・ズーム・おおきさ・パワー）
                    float code = floor(i.info.y + 0.5);   // 補間で 47.9999 などになっても桁数がずれないように
                    float nd = floor(code / 16.0);
                    float src = code - nd * 16.0;
                    float val = src < 0.5 ? (_Udon_SKBpm > 0.5 ? _Udon_SKBpm : _BPM)
                              : src < 1.5 ? _N0.x : src < 2.5 ? _N0.y : src < 3.5 ? _N0.z : src < 4.5 ? _N0.w : _N1.x;
                    val = floor(max(val, 0.0) + 0.5);
                    float k = min(floor(saturate(L.x) * nd), nd - 1.0);
                    float pw = pow(10.0, nd - 1.0 - k);
                    float dgt = fmod(floor(val / pw + 0.0001), 10.0);
                    float blank = (k < nd - 1.0 && val < pw) ? 1.0 : 0.0;   // 頭の 0 は出さない
                    float fu = frac(saturate(L.x) * nd);
                    float2 auv = float2(_DigitRect.x + (dgt + 0.25 + 0.5 * fu) * _DigitRect.z, _DigitRect.y + saturate(L.y) * _DigitRect.w);
                    float2 dx = ddx(L) * float2(0.5 * nd * _DigitRect.z, _DigitRect.w);
                    float2 dy = ddy(L) * float2(0.5 * nd * _DigitRect.z, _DigitRect.w);
                    float a = tex2Dgrad(_Atlas, auv, dx, dy).a * (1.0 - blank);
                    col = lerp(float3(0.004, 0.004, 0.006), col * 1.6, a);
                }
                float4 c = float4(col, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
