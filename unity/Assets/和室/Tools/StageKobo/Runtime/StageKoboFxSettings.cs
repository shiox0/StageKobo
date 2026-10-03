// すてーじ工房：追加の特効（好きなパーティクルシステム）の設定
// メニュー「和室 → すてーじ工房 → 特効（パーティクル）の設定」で編集する。シーンに 1 個だけ置く（EditorOnly：ワールドには入らない）。
// 組み立てるときにここを読んで、リモコン・ステージ裏の照明卓にボタンを足し、UdonSharp 版のコントローラーにパーティクルを渡す。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Washitsu.StageKobo
{
    [AddComponentMenu("")]   // 「Add Component」には出さない（専用のウィンドウから作る）
    public class StageKoboFxSettings : MonoBehaviour
    {
        public const int MaxEntries = 16;   // ボタンの数の上限（StageKoboController の FxC0〜FxC15 と同じ数）

        [Serializable]
        public class Entry
        {
            public string name = "特効";
            public List<ParticleSystem> systems = new List<ParticleSystem>();
            [Tooltip("0 = 押すたびに 1 回出す / 1 = 押すと出しっぱなし、もう一度押すと止める（ON/OFF）")]
            public int mode = 0;
            [Tooltip("「1回」のとき：何秒で止めるか（0 = パーティクルの設定のまま。ループするパーティクルは秒数を入れる）")]
            public float seconds = 0f;
            [Tooltip("パーティクルの中にある音（AudioSource）も一緒に鳴らす")]
            public bool sound = true;
        }

        public List<Entry> entries = new List<Entry>();
        [Tooltip("最初からある特効（スパーク・紙吹雪・スモーク）のボタンも出す")]
        public bool showBuiltin = true;
    }
}
