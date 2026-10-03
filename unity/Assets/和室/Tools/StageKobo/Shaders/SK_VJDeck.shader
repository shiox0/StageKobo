// すてーじ工房：VJ のデッキ（映像の素を1つ描く Custom Render Texture 用）
// 値は UdonSharp 版の VJ コントローラー（StageKoboVJ）が毎フレーム入れる。
// コントローラーが無いとき（_Driven = 0）は、拍・時間をシェーダーの中で計算して、書き出したときの設定のまま動く。
// 音に反応（AudioLink）：「拍のドン」が低音になる（イコライザーは本物の音）。_ALAmount = 強さ
Shader "StageKobo/VJDeck"
{
    Properties
    {
        _Gen ("映像の素の番号", Float) = 0
        _Active ("描く（0 = 見えていないので省略）", Float) = 1
        _Driven ("コントローラーが値を入れている", Float) = 0
        _BPM ("BPM（コントローラーが無いとき）", Float) = 128
        _Speed ("速さ", Float) = 1
        _Bt ("拍 × 速さ", Float) = 0
        _T ("時間 × 速さ", Float) = 0
        _Pulse ("拍のドン（1→0）", Float) = 0
        _Cnt ("数", Float) = 6
        _Zoom ("ズーム", Float) = 1
        _Asp ("縦横比", Float) = 1.7778
        _C1 ("色1", Color) = (0.56,0.83,1,1)
        _C2 ("色2", Color) = (1,0.54,0.85,1)
        _C3 ("色3", Color) = (1,1,1,1)
        _Img ("画像", 2D) = "black" {}
        _ImgAsp ("画像の縦横比", Float) = 1.6
        _ImgOk ("画像がある", Float) = 0
        _ImgK ("画像のゆっくり寄り", Float) = 0
        _Txt ("文字", 2D) = "black" {}
        _TxtAsp ("文字の縦横比", Float) = 4
        _ALAmount ("音に反応（AudioLink）の強さ", Range(0,1)) = 0
    }
    SubShader
    {
        Lighting Off
        Blend One Zero
        Pass
        {
            Name "VJDeck"
            CGPROGRAM
            #include "UnityCustomRenderTexture.cginc"
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 3.5
            #include "SKCommon.cginc"
            #include "SKVJ.cginc"

            float _Gen, _Active, _Driven, _BPM, _Speed, _Bt, _T, _Pulse, _Cnt, _Zoom, _Asp;
            float4 _C1, _C2, _C3;
            sampler2D _Img; float _ImgAsp, _ImgOk, _ImgK;
            sampler2D _Txt; float _TxtAsp;
            float _ALAmount;

            float4 frag(v2f_customrendertexture IN) : COLOR
            {
                if (_Active < 0.5) return float4(0, 0, 0, 1);
                float2 uv = IN.localTexcoord.xy;
                float bt = _Bt, tt = _T, pu = _Pulse;
                if (_Driven < 0.5)
                {
                    float beat = skBeat(_BPM);
                    bt = beat * _Speed; tt = _Time.y * _Speed; pu = exp(-frac(beat) * 4.5);
                }
                float wAL = skALWeight(_ALAmount);
                if (wAL > 0.0)
                {
                    float bass = skALBand(0, 0);
                    pu = lerp(pu, bass, wAL);
                    skPatALw = wAL; skPatALPulse = bass;
                }
                int gen = (int)(_Gen + 0.5);
                float3 c1 = _C1.rgb, c2 = _C2.rgb, c3 = _C3.rgb;
                float3 col = float3(0, 0, 0);
                if (gen <= 22) col = skGen(gen, uv, bt, tt, pu, _Cnt, _Zoom, _Asp, c1, c2, c3);
                else if (gen == 23)
                {
                    // 画像：画面いっぱいに（はみ出す分は切る）、ゆっくり寄る＋拍で少し跳ねる
                    if (_ImgOk < 0.5) col = lerp(c1, c2, uv.y) * 0.08;
                    else
                    {
                        float2 q = uv - 0.5;
                        if (_Asp > _ImgAsp) q.y *= _ImgAsp / _Asp; else q.x *= _Asp / _ImgAsp;
                        q /= _Zoom * (1.0 + 0.1 * _ImgK + 0.05 * pu);
                        col = tex2D(_Img, q + 0.5).rgb;
                    }
                }
                else if (gen == 24)
                {
                    // 文字
                    float2 q = (uv - 0.5) * float2(_Asp, 1.0);
                    float hh = min(0.5, 0.92 * _Asp / _TxtAsp) * _Zoom * (1.0 + 0.1 * pu);
                    float2 tuv = float2(q.x / (hh * _TxtAsp), q.y / hh) + 0.5;
                    float inb = step(0.0, tuv.x) * step(tuv.x, 1.0) * step(0.0, tuv.y) * step(tuv.y, 1.0);
                    float4 tx = tex2D(_Txt, clamp(tuv, 0.0, 1.0)) * inb;
                    col = lerp(c1, c2, tuv.y) * tx.a * (0.75 + 0.6 * pu) + lerp(c2, c1, uv.y) * 0.05;
                }
                return float4(max(col, 0.0), 1.0);
            }
            ENDCG
        }
    }
}
