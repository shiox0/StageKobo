// すてーじ工房：シェーダー共通
// UdonSharp 版のコントローラーがあるときは、全員で同期した「拍」を _Udon_SKBeat で受け取る。
// 無いとき（Animator だけの版）は、マテリアルの _BPM と時間から拍を計算する。
#ifndef SK_COMMON_INCLUDED
#define SK_COMMON_INCLUDED

#include "SKAudioLink.cginc"   // 曲の音に反応（AudioLink）

float _Udon_SKBeat;   // 同期した拍（VRCShader.SetGlobalFloat）
float _Udon_SKSync;   // 1 = 同期中

float skBeat(float bpm)
{
    return _Udon_SKSync > 0.5 ? _Udon_SKBeat : _Time.y * bpm / 60.0;
}

float3 skHsv2Rgb(float3 c)
{
    float3 k = saturate(abs(fmod(c.x * 6.0 + float3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0);
    return c.z * lerp(float3(1.0, 1.0, 1.0), k, c.y);
}

// three.js の setHSL（sRGB）と同じ式 → リニア
float3 skHsl(float h, float s, float l)
{
    float3 k = saturate(abs(fmod(h * 6.0 + float3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0);
    float c = (1.0 - abs(2.0 * l - 1.0)) * s;
    float3 rgb = l + c * (k - 0.5);
    #ifndef UNITY_COLORSPACE_GAMMA
    rgb = GammaToLinearSpace(saturate(rgb));
    #endif
    return rgb;
}

// 色スロット（0-2 = パレットの色1〜3、3 = 虹）
float3 skPaletteColor(float slot, float hue, float3 c1, float3 c2, float3 c3)
{
    if (slot < 0.5) return c1;
    if (slot < 1.5) return c2;
    if (slot < 2.5) return c3;
    float3 c = skHsv2Rgb(float3(frac(hue), 0.72, 0.94));
    #ifndef UNITY_COLORSPACE_GAMMA
    c = GammaToLinearSpace(c);
    #endif
    return c;
}

#endif
