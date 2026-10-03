// すてーじ工房：AudioLink（曲の音に反応する）
// AudioLink がワールドにあると、グローバルのテクスチャ _AudioTexture（128×64）に音の解析結果が入る。
// AudioLink が無いワールドでは何も入らない（幅 16 以下）→ 反応しない（いつもの BPM の演出のまま）。
// AudioLink のパッケージが入っていないプロジェクトでもコンパイルできるよう、AudioLink.cginc は読み込まず、
// 同じ場所（ALPASS_～）を自前で読む（AudioLink 2.5 以降の並び。3.x で確認）。
//   _ALAmount（マテリアル）  … インポーターの「反応の強さ」。0 = 反応しない
//   _Udon_SKALOff（グローバル）… リモコン・照明卓の「音に反応 OFF」（UdonSharp 版のコントローラーが入れる）
// 曲が止まっている・一時停止中（YamaPlayer などが AudioLink に「再生の状態」を渡す）や、音が鳴っていないときは反応しない。
#ifndef SK_AUDIOLINK_INCLUDED
#define SK_AUDIOLINK_INCLUDED

float _Udon_SKALOff;
float4 _AudioTexture_TexelSize;

#if SHADER_TARGET >= 35 && !defined(SHADER_TARGET_SURFACE_ANALYSIS)
Texture2D<float4> _AudioTexture;
float4 skALData(uint2 xy) { return _AudioTexture[xy]; }
bool skALReady()
{
    #if defined(SHADER_API_GLCORE)
    return _AudioTexture_TexelSize.z > 16.0;
    #else
    uint w, h;
    _AudioTexture.GetDimensions(w, h);
    return w > 16;
    #endif
}
#else
// 古い描き方（#pragma target 3.5 未満）では読まない
float4 skALData(uint2 xy) { return float4(0, 0, 0, 0); }
bool skALReady() { return false; }
#endif

// 帯域ごとの音の大きさ（0〜1）。band：0 低音（キック）/ 1 中低音 / 2 中高音 / 3 高音
// delay：何コマ前の値か（0〜127。1コマ ≒ 1/90 秒。ずらすと音が波のように伝わる）
float skALBand(float band, float delay)
{
    return saturate(skALData(uint2((uint)clamp(delay, 0.0, 127.0), (uint)band)).x);
}

// なめらかにした音の大きさ（ALPASS_FILTEREDAUDIOLINK）。x：0 = いちばんなめらか（約 1.7 秒）… 15 = ほぼそのまま
float skALSmooth(float band, float x)
{
    return saturate(skALData(uint2((uint)clamp(x, 0.0, 15.0), 28u + (uint)band)).x);
}

// 周波数ごとの大きさ（イコライザー用）。f：0 = 低い音 … 1 = 高い音
float skALSpectrum(float f)
{
    float bin = lerp(18.0, 220.0, saturate(f));   // 10オクターブ × 24 のうち、いちばん下と上を少し外す
    uint b = (uint)bin;
    float4 a = skALData(uint2(b % 128u, 4u + b / 128u));
    float4 c = skALData(uint2((b + 1u) % 128u, 4u + (b + 1u) / 128u));
    return saturate(lerp(a.y, c.y, frac(bin)) * 1.2);   // y = なめらかにした大きさ（AudioLink が使っているもの）
}

// 音に反応する度合い（0 = いつもの演出のまま … amount）
//  ・AudioLink の「再生の状態」が 再生中・配信中（音量 0 でない）→ 反応する（静かな所は半分）
//  ・一時停止・停止・読み込み中・エラー → 音が鳴っていなければ反応しない
//  ・状態が分からない（AudioLink だけで使うとき）→ 音が鳴っていれば反応する
float skALWeight(float amount)
{
    float a = amount * (1.0 - saturate(_Udon_SKALOff));
    if (a <= 0.001) return 0.0;
    if (!skALReady()) return 0.0;
    float4 ms = skALData(uint2(5, 22));                 // ALPASS_MEDIASTATE（音量, 時間, 状態, ループ）
    float st = floor(ms.z + 0.5);
    float playing = ((abs(st - 1.0) < 0.5 || abs(st - 5.0) < 0.5) && ms.x > 0.001) ? 1.0 : 0.0;
    float avg = (skALSmooth(0.0, 0.0) + skALSmooth(2.0, 0.0)) * 0.5;   // ゆっくりの音量（約 1.7 秒）
    float loud = smoothstep(0.02, 0.12, avg);
    return a * max(playing * (0.5 + 0.5 * loud), loud);
}

// 明るさの倍率：反応しないとき 1。反応するときは、音が小さいと暗め（0.3）・大きいと明るめ（1.7）
float skALGain(float level, float w)
{
    return lerp(1.0, 0.3 + 1.4 * level, w);
}

#endif
