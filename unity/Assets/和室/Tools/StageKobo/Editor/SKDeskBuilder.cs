using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// ステージ裏の操作卓（物理スイッチ）：照明卓・VJ卓。クリック／レーザー（Interact）で押す。
    /// ・ボタンの文字は Textures/SK_Labels.png（格子状の文字テクスチャ）から貼る。対応表は SKLabels.cs（どちらも Tools~/make_labels.py で生成）
    /// ・卓1台 = メッシュ1個 + マテリアル1個（StageKobo/Panel）。ランプは 24 個ずつを1つの数にして _B0〜_B3 で渡す（UdonSharp 版のコントローラーが入れる）
    /// ・卓の奥に確認用モニター（StageKobo/Screen）：照明卓 = カメラ1・2、VJ卓 = VJ 出力・デッキA・B
    /// </summary>
    public static class SKDeskBuilder
    {
        // ---- 寸法（m）
        const float CAP_W = 0.072f, CAP_D = 0.038f, CAP_H = 0.010f, PX = 0.082f, PY = 0.05f;
        const int COLS = 11;                  // 1ブロックの横の升目
        const float MARGIN = 0.05f, GROUP_GAP = 0.07f, TILT = 16f, FRONT_H = 0.80f;
        const float BRIDGE_H = 0.42f, BRIDGE_T = 0.05f, DESK_GAP = 0.45f;
        const float LIFT = 0.0015f;           // 盤面から浮かせる（文字・数字の板）

        static readonly Color PanelCol = new Color(0.075f, 0.08f, 0.12f), BodyCol = new Color(0.035f, 0.038f, 0.055f), BezelCol = new Color(0.012f, 0.012f, 0.018f), FloorCol = new Color(0.03f, 0.03f, 0.04f);
        static readonly Color TitleCol = new Color(1f, 0.56f, 0.82f), NameCol = new Color(0.5f, 0.96f, 0.84f), MonCol = new Color(0.78f, 0.82f, 0.96f), DigitCol = new Color(1f, 0.72f, 0.3f), BeatCol = new Color(0.5f, 1f, 0.88f);
        static readonly Color CBtn = new Color(0.13f, 0.15f, 0.33f), CTempo = new Color(0.1f, 0.3f, 0.32f), CTap = new Color(0.15f, 0.42f, 0.4f), CFx = new Color(0.45f, 0.2f, 0.5f),
            CWarn = new Color(0.5f, 0.12f, 0.2f), CPad = new Color(0.52f, 0.16f, 0.44f), CVJ = new Color(0.19f, 0.13f, 0.37f), CDeck = new Color(0.12f, 0.24f, 0.45f),
            CScene = new Color(0.12f, 0.3f, 0.42f), CUp = new Color(0.42f, 0.24f, 0.12f);

        static readonly string[] Nums = L("1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12");
        static readonly string[] SlotLabels = L("登録1", "登録2", "登録3", "登録4", "登録5", "登録6", "登録7", "登録8");

        static string[] L(params string[] a) { return a; }

        // ------------------------------------------------------------------ 設計図

        public class Btn
        {
            public string label, tip;
            public Color col = CBtn;
            public Color[] stripes;      // 色見本（縦じま）
            public int kind;             // 0 = 照明（StageKoboController）、1 = VJ（StageKoboVJ）
            public int act, val;
            public bool lamp = true;     // 選ばれていたら光る
            public int readout = -1;     // 数字の表示（押せない）：0 = BPM, 1 = 数, 2 = ズーム%
            public int digits = 3;
            public bool beat;            // 拍のランプ（押せない）
            public bool custom;          // 追加の特効：文字は文字のテクスチャではなく、UI の文字で重ねる
            public bool Pressable { get { return readout < 0 && !beat; } }
            // 組み立て結果
            public int slot = -1;
            public Vector3 localPos;
            public string secTitle;
            public GameObject go;
            public string Text { get { return secTitle + "：" + (string.IsNullOrEmpty(label) ? tip ?? "" : label); } }
        }

        public class Sec
        {
            public string title;
            public List<Btn> btns = new List<Btn>();
            public bool newRow, newGroup;
        }

        public class Desk
        {
            public string name, key;
            public int groups = 2;
            public List<Sec> secs = new List<Sec>();
            public List<Mon> mons = new List<Mon>();
            public Desk(string name, string key) { this.name = name; this.key = key; }
            // 組み立て結果
            public float W, Dp, zf;
            public int rows;
            public Material mat;
            public GameObject go;
            public List<Btn> buttons = new List<Btn>();   // スロット順（押せるボタンだけ）
            public List<Placed> placed = new List<Placed>();
        }

        public class Mon
        {
            public string label;
            public Texture tex;
            public float w;
            public Material mat;
            public float sw, sh;         // 画面の大きさ（映像の縦横比に合わせたもの）
        }

        /// <summary>升目に置いたもの（ボタン・数字・ランプ・見出し）</summary>
        public class Placed
        {
            public Btn b;                // null なら見出し
            public string title;
            public int group, row, col, span;
        }

        static Sec NewSec(Desk d, string title, bool newRow = false)
        {
            var s = new Sec { title = title, newRow = newRow };
            d.secs.Add(s);
            return s;
        }

        static Btn Bt(string label, int kind, int act, int val, Color col, bool lamp = true)
        {
            return new Btn { label = label, kind = kind, act = act, val = val, col = col, lamp = lamp };
        }

        static void Many(Sec s, string[] labels, int kind, int act, int[] vals, Color col, bool lamp = true)
        {
            for (int i = 0; i < labels.Length; i++) s.btns.Add(Bt(labels[i], kind, act, vals != null ? vals[i] : i, col, lamp));
        }

        static Mon NewMon(string label, Texture tex, float w) { return new Mon { label = label, tex = tex, w = w }; }

        // ------------------------------------------------------------------ 照明卓

        static Desk LightDesk(SKContext ctx)
        {
            var d = new Desk("照明卓", "Light");
            var t = NewSec(d, "テンポ");
            t.btns.Add(Bt("−5", 0, SKCh.ACT_BPM, -5, CTempo, false));
            t.btns.Add(Bt("−1", 0, SKCh.ACT_BPM, -1, CTempo, false));
            t.btns.Add(Bt("TAP", 0, SKCh.ACT_BPM, 0, CTap, false));
            t.btns.Add(Bt("+1", 0, SKCh.ACT_BPM, 1, CTempo, false));
            t.btns.Add(Bt("+5", 0, SKCh.ACT_BPM, 5, CTempo, false));
            t.btns.Add(new Btn { readout = 0, digits = 3 });
            t.btns.Add(new Btn { beat = true });
            if (ctx.channels[SKCh.MOVE] != null) Many(NewSec(d, "動きの速さ"), L("×½", "×1", "×2"), 0, SKCh.ACT_SPEED, null, CBtn);
            var master = NewSec(d, "照明ぜんぶ");
            ChannelBtns(ctx, master, SKCh.MASTER, L("Master_On", "Master_Off"));
            foreach (var b in master.btns) if (b.label == "暗転") b.col = CWarn;
            if (ctx.channels[SKCh.MASTER] != null && ctx.channels[SKCh.MASTER].IndexOf("Master_Strobe") >= 0) master.btns.Add(Bt("ストロボ", 0, SKCh.ACT_STROBE, 0, CFx, false));
            var fx = NewSec(d, "特効");
            var groups = new[] { ctx.sparks, ctx.confetti, ctx.smoke };
            var fxNames = L("スパーク", "紙吹雪", "スモーク");
            for (int g = 0; g < 3; g++) if (groups[g].Count > 0) fx.btns.Add(Bt(fxNames[g], 0, SKCh.ACT_FX, g, CFx, false));
            if (ctx.audio) Many(NewSec(d, "音に反応"), L("ON", "OFF"), 0, SKCh.ACT_AUDIO, new[] { 1, 0 }, CTempo);   // 曲の音（AudioLink）
            // 追加の特効（特効の設定ウィンドウで作ったもの）。ON/OFF のものはランプが付く
            if (ctx.fxCustom.Count > 0)
            {
                var fx2 = NewSec(d, "追加の特効", true);
                for (int k = 0; k < ctx.fxCustom.Count; k++)
                {
                    var b = Bt(ctx.fxCustom[k].name, 0, SKCh.ACT_FX, 3 + k, CFx, ctx.fxCustom[k].mode == 1);
                    b.custom = true;
                    fx2.btns.Add(b);
                }
            }
            ChannelBtns(ctx, NewSec(d, "動き", true), SKCh.MOVE, SKMath.Patterns.Select(x => "Move_" + x));
            ChannelBtns(ctx, NewSec(d, "色の付け方", true), SKCh.COLOR, SKMath.ColorModes.Select(x => "Col_" + x));
            var pal = NewSec(d, "パレット", true);
            var ch = ctx.channels[SKCh.PALETTE];
            if (ch != null)
            {
                int live = ch.IndexOf("Pal_Live");
                if (live >= 0) pal.btns.Add(Bt("シーン色", 0, SKCh.PALETTE, live, CBtn));
                var pals = J.L(ctx.data, "palettes");
                for (int i = 0; i < pals.Count; i++)
                {
                    int idx = ch.IndexOf("Pal_" + i);
                    var cs = J.L(pals[i], "c");
                    if (idx < 0 || cs.Count < 3) continue;
                    pal.btns.Add(new Btn { label = "", kind = 0, act = SKCh.PALETTE, val = idx, stripes = cs.Take(3).Select(c => J.Col(c as string, Color.white)).ToArray(), tip = J.S(pals[i], "name") });
                }
            }
            ChannelBtns(ctx, NewSec(d, "明るさ", true), SKCh.DIM, SKMath.DimModes.Select(x => "Dim_" + x));
            ChannelBtns(ctx, NewSec(d, "レーザー", true), SKCh.LASER, SKMath.LaserModes.Select(x => "Laser_" + x));
            ChannelBtns(ctx, NewSec(d, "ウォッシュ"), SKCh.WASH, L("Wash_On", "Wash_Off"));
            ChannelBtns(ctx, NewSec(d, "ペンライト"), SKCh.PEN, L("Pen_cue", "Pen_white", "Pen_rainbow"));
            ChannelBtns(ctx, NewSec(d, "メイン"), SKCh.MONITOR, L("Monitor_Live", "Monitor_VJ"));
            ChannelBtns(ctx, NewSec(d, "サブ"), SKCh.MONITOR2, L("Monitor2_Live", "Monitor2_VJ"));
            ChannelBtns(ctx, NewSec(d, "カメラ1", true), SKCh.CAM1, SKMath.CamModes.Select(x => "Shot_" + x));
            ChannelBtns(ctx, NewSec(d, "カメラ2", true), SKCh.CAM2, SKMath.CamModes.Select(x => "Shot_" + x));
            // アップ（顔寄り）で追う人：押した人を登録（ランプ = 自分が登録されている）
            if (ctx.cams[0] != null || ctx.cams[1] != null)
            {
                var up = NewSec(d, "アップで追う人", true);
                up.btns.Add(Bt("自分をカメラ1", 0, SKCh.ACT_UP, 0, CUp));
                up.btns.Add(Bt("自分をカメラ2", 0, SKCh.ACT_UP, 1, CUp));
                up.btns.Add(Bt("登録を外す", 0, SKCh.ACT_UP, 3, CWarn, false));
            }
            // 演者を照らすスポット：押した人を追う（ランプ = 自分が登録されている）・固定に戻す・ON/OFF
            if (ctx.spotLights.Count > 0)
            {
                var sp = NewSec(d, "スポットライト", true);
                sp.btns.Add(Bt("自分をスポット1", 0, SKCh.ACT_SPOT, 0, CUp));
                sp.btns.Add(Bt("自分をスポット2", 0, SKCh.ACT_SPOT, 1, CUp));
                sp.btns.Add(Bt("固定に戻す", 0, SKCh.ACT_SPOT, 3, CWarn, false));
                sp.btns.Add(Bt("ON", 0, SKCh.ACT_SPOTON, 1, CTempo));
                sp.btns.Add(Bt("OFF", 0, SKCh.ACT_SPOTON, 0, CTempo));
            }
            if (ctx.camRT[0] != null) d.mons.Add(NewMon("カメラ1", ctx.camRT[0], 0.36f));
            if (ctx.camRT[1] != null) d.mons.Add(NewMon("カメラ2", ctx.camRT[1], 0.36f));
            return d;
        }

        static void ChannelBtns(SKContext ctx, Sec s, int chIndex, IEnumerable<string> states)
        {
            var ch = ctx.channels[chIndex];
            if (ch == null) return;
            foreach (var st in states)
            {
                int idx = ch.IndexOf(st);
                if (idx < 0) continue;
                s.btns.Add(Bt(SKRemoteBuilder.JP.TryGetValue(st, out var jp) ? jp : st, 0, chIndex, idx, CBtn));
            }
        }

        // ------------------------------------------------------------------ VJ卓

        static Desk VJDesk(SKContext ctx, SKVJInfo info)
        {
            var defs = J.O(ctx.data, "vjDefs");
            var d = new Desk("VJ卓", "VJ");
            var pad = NewSec(d, "パッド");
            Many(pad, L("フラッシュ", "反転", "グリッチ", "ズーム", "ストロボ"), 1, SKVJIdx.V_PAD, null, CPad, false);
            pad.btns.Add(Bt("暗転", 1, SKVJIdx.V_BLACK, 0, CWarn));
            pad.btns.Add(new Btn { beat = true });
            var deck = NewSec(d, "デッキ", true);
            deck.btns.Add(Bt("Aを編集", 1, SKVJIdx.V_EDIT, 0, CDeck));
            deck.btns.Add(Bt("Bを編集", 1, SKVJIdx.V_EDIT, 1, CDeck));
            deck.btns.Add(Bt("◀ A", 1, SKVJIdx.V_CUT, 0, CVJ));
            deck.btns.Add(Bt("⇄", 1, SKVJIdx.V_CUT, 2, CVJ, false));
            deck.btns.Add(Bt("B ▶", 1, SKVJIdx.V_CUT, 1, CVJ));
            deck.btns.Add(Bt("⇄ 映す", 1, SKVJIdx.V_CUT, 3, CVJ));
            deck.btns.Add(Bt("選んだら切り替え", 1, SKVJIdx.V_TAKE, 0, CVJ));
            Many(NewSec(d, "切替"), L("カット", "1拍", "2拍", "4拍"), 1, SKVJIdx.V_FADELEN, new[] { 0, 1, 2, 4 }, CVJ);
            Many(NewSec(d, "拍で交互"), L("なし", "1拍", "2拍", "4拍"), 1, SKVJIdx.V_XFAUTO, new[] { 0, 1, 2, 4 }, CVJ);
            // 映像の素
            var gen = NewSec(d, "映像の素", true);
            var gs = J.L(defs, "gensShort").Select(x => x as string ?? "").ToList();
            var gf = J.L(defs, "gensFull").Select(x => x as string ?? "").ToList();
            foreach (int i in Enumerable.Range(0, Mathf.Min(gs.Count, SKVJIdx.NGEN)))   // v0.7 の書き出しの「合成」（26）は出さない
            {
                if (i == SKVJIdx.GEN_IMAGE && info.images.Count == 0) continue;
                if (i == SKVJIdx.GEN_TEXT && info.text == null) continue;
                var b = Bt(gs[i], 1, SKVJIdx.V_GEN, i, CVJ);
                b.tip = i < gf.Count ? gf[i] : gs[i];
                gen.btns.Add(b);
            }
            Many(NewSec(d, "速さ", true), L("×¼", "×½", "×1", "×2", "×4"), 1, SKVJIdx.V_SPEED, null, CVJ);
            var cnt = NewSec(d, "数");
            cnt.btns.Add(Bt("−", 1, SKVJIdx.V_COUNT, -1, CVJ, false)); cnt.btns.Add(new Btn { readout = 1, digits = 2 }); cnt.btns.Add(Bt("＋", 1, SKVJIdx.V_COUNT, 1, CVJ, false));
            var zm = NewSec(d, "ズーム");
            zm.btns.Add(Bt("−", 1, SKVJIdx.V_ZOOM, -1, CVJ, false)); zm.btns.Add(new Btn { readout = 2, digits = 3 }); zm.btns.Add(Bt("＋", 1, SKVJIdx.V_ZOOM, 1, CVJ, false));
            if (info.images.Count > 0)
            {
                var im = NewSec(d, "画像", true);
                for (int i = 0; i < Mathf.Min(info.images.Count, Nums.Length); i++) { var b = Bt(Nums[i], 1, SKVJIdx.V_IMG, i, CVJ); b.tip = "画像" + (i + 1); im.btns.Add(b); }
                Many(NewSec(d, "拍で次の画像へ"), L("なし", "1拍", "2拍", "4拍", "8拍"), 1, SKVJIdx.V_IMGRATE, new[] { 0, 1, 2, 4, 8 }, CVJ);
            }
            var col = NewSec(d, "色", true);
            col.btns.Add(Bt("照明", 1, SKVJIdx.V_PAL, -1, CVJ));
            var pals = J.L(ctx.data, "palettes");
            for (int i = 0; i < Mathf.Min(8, pals.Count); i++)
            {
                var cs = J.L(pals[i], "c");
                if (cs.Count < 3) continue;
                col.btns.Add(new Btn { label = "", kind = 1, act = SKVJIdx.V_PAL, val = i, stripes = cs.Take(3).Select(c => J.Col(c as string, Color.white)).ToArray(), tip = J.S(pals[i], "name") });
            }
            Many(NewSec(d, "色の付け方"), J.L(defs, "colorModeNames").Select(x => x as string ?? "").ToArray(), 1, SKVJIdx.V_CMODE, null, CVJ);
            Many(NewSec(d, "色が回る"), L("なし", "ゆっくり", "はやい"), 1, SKVJIdx.V_HUE, null, CVJ);
            Many(NewSec(d, "エフェクト", true), info.fxNames, 1, SKVJIdx.V_FX, null, CFx);
            Many(NewSec(d, "ミラー", true), J.L(defs, "mirrors").Select(x => x as string ?? "").ToArray(), 1, SKVJIdx.V_MIRROR, null, CVJ);
            Many(NewSec(d, "万華鏡"), L("3", "4", "6", "8", "12"), 1, SKVJIdx.V_KALE, new[] { 3, 4, 6, 8, 12 }, CVJ);
            Many(NewSec(d, "ドンの間隔", true), L("½拍", "1拍", "2拍", "4拍"), 1, SKVJIdx.V_RATE, null, CVJ);
            Many(NewSec(d, "ドゥン量"), L("なし", "弱", "中", "強"), 1, SKVJIdx.V_PUMP, null, CVJ);
            Many(NewSec(d, "FX量", true), L("弱", "中", "強"), 1, SKVJIdx.V_AMT, null, CVJ);
            Many(NewSec(d, "明るさ"), L("50%", "75%", "100%", "130%"), 1, SKVJIdx.V_BRIGHT, null, CVJ);
            if (info.text != null)
            {
                var tx = NewSec(d, "文字", true);
                tx.btns.Add(Bt("重ねる", 1, SKVJIdx.V_TEXTON, 0, CVJ));
                Many(tx, L("ドン", "点滅", "固定"), 1, SKVJIdx.V_TEXTANIM, null, CVJ);
            }
            var au = NewSec(d, "オートVJ", info.text == null);
            au.btns.Add(Bt("オート", 1, SKVJIdx.V_AUTO, 0, CScene));
            Many(au, L("1小節", "2小節", "4小節", "8小節"), 1, SKVJIdx.V_AUTOBARS, new[] { 1, 2, 4, 8 }, CVJ);
            var sc = NewSec(d, "シーン", true);
            for (int i = 0; i < info.presetNames.Count; i++) sc.btns.Add(Bt(info.presetNames[i], 1, SKVJIdx.V_PRESET, i, CScene, false));
            if (info.slotNames.Count > 0)
            {
                var sl = NewSec(d, "登録シーン", true);
                for (int i = 0; i < info.slotNames.Count; i++) sl.btns.Add(Bt(info.slotNames[i], 1, SKVJIdx.V_SLOT, i, CScene, false));
            }
            ChannelBtns(ctx, NewSec(d, "メイン", true), SKCh.MONITOR, L("Monitor_Live", "Monitor_VJ"));
            ChannelBtns(ctx, NewSec(d, "サブ"), SKCh.MONITOR2, L("Monitor2_Live", "Monitor2_VJ"));
            if (ctx.leds.Any(l => l.backdrop)) Many(NewSec(d, "背景LED"), L("映す", "映さない"), 1, SKVJIdx.V_BACK, new[] { 1, 0 }, CVJ);
            if (ctx.leds.Count(l => l.backdrop || l.camMonitor || l.vjAlways) > 1) Many(NewSec(d, "映し方"), L("1枚ずつ", "つなげて1枚"), 1, SKVJIdx.V_MAP, null, CVJ);
            d.mons.Add(NewMon("VJ 出力", info.output, 0.42f));
            d.mons.Add(NewMon("デッキA", info.deckA, 0.28f));
            d.mons.Add(NewMon("デッキB", info.deckB, 0.28f));
            return d;
        }

        // ------------------------------------------------------------------ 並べ方

        /// <summary>
        /// 升目に流し込む。見出し（1〜2升）→ ボタン… を左から右へ、入らなければ次の行（手前から奥へ）。
        /// ブロック（横に並ぶ列）は groups 個。newGroup の見出しがあればそこで次のブロック、無ければ行数がだいたい同じになるよう分ける
        /// </summary>
        public static void Layout(Desk d, Func<string, int> titleSpan)
        {
            var rows = new List<List<Placed>>();
            var rowSec = new List<int>();
            var rowGroupBreak = new List<bool>();
            List<Placed> cur = null;
            int c = COLS;
            Action<int, bool> newline = (si, gb) => { cur = new List<Placed>(); rows.Add(cur); rowSec.Add(si); rowGroupBreak.Add(gb); c = 0; };
            for (int si = 0; si < d.secs.Count; si++)
            {
                var s = d.secs[si];
                if (s.btns.Count == 0) continue;
                int ts = Mathf.Clamp(titleSpan(s.title), 1, 3);
                int need = ts + s.btns.Count;
                if (cur == null || s.newRow || s.newGroup || c + Mathf.Min(need, COLS) > COLS) newline(si, s.newGroup);
                cur.Add(new Placed { title = s.title, col = c, span = ts });
                c += ts;
                foreach (var b in s.btns)
                {
                    if (c >= COLS) { newline(si, false); c = ts; }
                    b.secTitle = s.title;
                    cur.Add(new Placed { b = b, col = c, span = 1 });
                    c++;
                }
            }
            int groups = Mathf.Max(1, d.groups);
            bool explicitBreak = rowGroupBreak.Any(x => x);
            int target = Mathf.CeilToInt(rows.Count / (float)groups);
            int g = 0, inGroup = 0, maxRows = 0;
            for (int r = 0; r < rows.Count; r++)
            {
                bool startsSec = r == 0 || rowSec[r] != rowSec[r - 1];
                if (r > 0 && g < groups - 1 && (explicitBreak ? rowGroupBreak[r] : (startsSec && inGroup >= target))) { g++; inGroup = 0; }
                foreach (var p in rows[r]) { p.group = g; p.row = inGroup; d.placed.Add(p); }
                inGroup++;
                maxRows = Mathf.Max(maxRows, inGroup);
            }
            d.groups = g + 1;
            d.rows = maxRows;
            d.W = 2 * MARGIN + d.groups * COLS * PX + (d.groups - 1) * GROUP_GAP;
            d.Dp = 2 * MARGIN + d.rows * PY;
            d.zf = d.Dp * Mathf.Cos(TILT * Mathf.Deg2Rad);
        }

        // ------------------------------------------------------------------ 組み立て

        public static void Build(SKContext ctx)
        {
            if (!ctx.udon) { ctx.Log("（ステージ裏の操作卓は UdonSharp 版のときだけ作ります。UdonSharp を入れて「UdonSharp 版を有効にする」を押してから組み立て直してください）"); return; }
            var ctrl = SKUdon.EnsureController(ctx);
            if (ctrl == null) { ctx.Log("⚠ UdonSharp のコントローラーを作れなかったので、ステージ裏の操作卓は作りませんでした"); return; }
            var atlas = LoadAtlas(ctx);
            var missing = new List<string>();

            var desks = new List<Desk> { LightDesk(ctx) };
            var vjInfo = ctx.vj != null && ctx.vj.ctrl != null ? ctx.vj : null;
            if (vjInfo != null) desks.Add(VJDesk(ctx, vjInfo));
            else if (ctx.vj != null) ctx.Log("⚠ VJ の UdonSharp を作れなかったので、VJ卓は作りませんでした");
            foreach (var d in desks)
            {
                d.secs.RemoveAll(s => s.btns.Count == 0);
                Layout(d, t => SKLabels.Span("T:" + t, 2));
            }

            // ---- 置き場所：背景（BACKDROP）の裏。卓の奥（モニターの板）を背景の裏から 0.6m 離し、ステージの台より後ろにする
            float backZ = BackdropBackZ(ctx, out bool hasBackdrop);
            float stageBack = -J.F(J.O(ctx.state, "stage"), "depth", 8f) / 2f;
            float maxZf = desks.Max(d => d.zf);
            float farEdge = Mathf.Min(backZ - 0.6f, stageBack - 0.4f);
            float zRoot = farEdge - BRIDGE_T - maxZf;
            var root = new GameObject("Backstage（ステージ裏の操作卓）");
            root.transform.SetParent(ctx.root.transform, false);
            root.transform.position = ctx.ToWorld(new Vector3(0, 0, zRoot));
            root.transform.rotation = Quaternion.LookRotation(ctx.DirToWorld(new Vector3(0, 0, 1)), Vector3.up);
            float total = desks.Sum(d => d.W) + DESK_GAP * (desks.Count - 1);
            float x = -total / 2f;

            var screenQuad = SKAssets.Save(Quad(), ctx.dir + "/Meshes/SK_ScreenQuad.asset");
            foreach (var d in desks)
            {
                d.go = new GameObject("Desk_" + d.key + "（" + d.name + "）");
                d.go.transform.SetParent(root.transform, false);
                d.go.transform.localPosition = new Vector3(x + d.W / 2f, 0, maxZf - d.zf);
                x += d.W + DESK_GAP;
                d.mat = SKAssets.NewMaterial(SKAssets.ShaderPanel, ctx.dir + "/Materials/Desk_" + d.key + ".mat");
                SetupDeskMaterial(ctx, d.mat, atlas, vjInfo);
                var mesh = BuildDeskMesh(d, missing);
                mesh.name = "SK_Desk_" + d.key;
                SKAssets.Save(mesh, ctx.dir + "/Meshes/SK_Desk_" + d.key + ".asset");
                d.go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = d.go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = d.mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                // 体が通り抜けないように（卓・モニターの板）
                float yf = FRONT_H + d.Dp * Mathf.Sin(TILT * Mathf.Deg2Rad);
                var bc = d.go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0, FRONT_H / 2f, d.zf / 2f); bc.size = new Vector3(d.W, FRONT_H, d.zf);
                var bridge = new GameObject("BridgeCollider");
                bridge.transform.SetParent(d.go.transform, false);
                var bb = bridge.AddComponent<BoxCollider>();
                bb.center = new Vector3(0, (yf + BRIDGE_H) / 2f, d.zf + BRIDGE_T / 2f); bb.size = new Vector3(d.W, yf + BRIDGE_H, BRIDGE_T);
                BuildMonitors(ctx, d, screenQuad, yf);
                // ボタン（押す所）：1個ずつ当たり判定と UdonSharp
                var rot = Quaternion.Euler(-TILT, 0, 0);
                foreach (var b in d.buttons)
                {
                    var go = new GameObject("Btn" + b.slot.ToString("000") + "_" + SKBuilder.Sanitize(b.secTitle + "_" + (string.IsNullOrEmpty(b.label) ? b.tip ?? "" : b.label)));
                    b.go = go;
                    go.transform.SetParent(d.go.transform, false);
                    go.transform.localPosition = b.localPos;
                    go.transform.localRotation = rot;
                    var c = go.AddComponent<BoxCollider>();
                    c.center = new Vector3(0, CAP_H / 2f + 0.004f, 0);
                    c.size = new Vector3(CAP_W + 0.004f, CAP_H + 0.016f, CAP_D + 0.004f);
                    if (b.custom) SKFx.DeskLabel(go, b.label, CAP_W, CAP_D, CAP_H);   // 好きな名前は UI の文字で重ねる
                }
            }
            BuildFloor(ctx, root.transform, total, maxZf);

            // ---- UdonSharp の配線
            var specs = new List<SKUdon.DeskButton>();
            for (int k = 0; k < desks.Count; k++)
                foreach (var b in desks[k].buttons)
                    specs.Add(new SKUdon.DeskButton
                    {
                        go = b.go, desk = k, slot = b.slot, kind = b.kind, act = b.act, val = b.val, lamp = b.lamp, mat = desks[k].mat, text = b.Text,
                    });
            bool ok = SKUdon.WireDesks(ctx, ctrl, vjInfo != null ? vjInfo.ctrl : null, desks.Select(d => d.mat).ToArray(), specs, root.transform);
            if (!ok)
            {
                UnityEngine.Object.DestroyImmediate(root);
                ctx.Log("⚠ 操作卓のボタンを配線できなかったので、ステージ裏の操作卓は作りませんでした");
                return;
            }
            if (missing.Count > 0) ctx.Log("⚠ 文字のテクスチャに無い文字があったので、そのボタンは文字なしにしました：" + string.Join("・", missing.Distinct().Take(12)));
            ctx.Log("ステージ裏の操作卓を置きました：" + string.Join("・", desks.Select(d => d.name + " " + d.buttons.Count + " 個")) +
                    "（背景の裏" + (hasBackdrop ? "" : "。背景が無いので客席から見えます") + "。ボタンはクリック／レーザーで押す）");
        }

        static Texture2D LoadAtlas(SKContext ctx)
        {
            string path = null;
            foreach (var guid in AssetDatabase.FindAssets(System.IO.Path.GetFileNameWithoutExtension(SKLabels.AtlasFile) + " t:Texture2D"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileName(p) == SKLabels.AtlasFile) { path = p; break; }
            }
            if (path == null) { ctx.Log("⚠ 文字のテクスチャ（" + SKLabels.AtlasFile + "）が見つかりません。ボタンは文字なしになります（パッケージの Textures フォルダを取り込み直してください）"); return null; }
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && (!imp.mipmapEnabled || imp.anisoLevel < 8 || imp.npotScale != TextureImporterNPOTScale.None || imp.maxTextureSize < SKLabels.W || imp.wrapMode != TextureWrapMode.Clamp))
            {
                imp.textureType = TextureImporterType.Default;
                imp.mipmapEnabled = true;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = true;
                imp.anisoLevel = 8;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.maxTextureSize = Mathf.Max(SKLabels.W, SKLabels.H);
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Trilinear;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void SetupDeskMaterial(SKContext ctx, Material m, Texture2D atlas, SKVJInfo vj)
        {
            if (atlas != null) m.SetTexture("_Atlas", atlas);
            m.SetFloat("_BPM", ctx.bpm);
            m.SetVector("_DigitRect", SKLabels.DigitRect());
            m.SetVector("_PressDir", new Vector4(0, -Mathf.Cos(TILT * Mathf.Deg2Rad), Mathf.Sin(TILT * Mathf.Deg2Rad), 0));
            m.SetVector("_PanelColor", Lin(PanelCol));
            m.SetVector("_BodyColor", Lin(BodyCol));
            m.SetFloat("_PressId", -1f);
            m.SetFloat("_PressT", -99f);
            if (vj != null && vj.vi != null)
            {
                int ed = vj.vi[SKVJIdx.I_EDIT];
                m.SetVector("_N0", new Vector4(vj.vi[SKVJIdx.I_CNT + ed], Mathf.Round(vj.vf[SKVJIdx.F_ZOOM + ed] * 100f), 0, 0));
            }
            EditorUtility.SetDirty(m);
        }

        static Vector4 Lin(Color c) { var l = c.linear; return new Vector4(l.r, l.g, l.b, 1f); }

        /// <summary>背景（BACKDROP）のいちばん奥（three の Z）</summary>
        public static float BackdropBackZ(SKContext ctx, out bool found)
        {
            found = false;
            float z = 1e9f;
            var bd = SKBuilder.FindDeep(ctx.model, "BACKDROP");
            if (bd != null)
                foreach (var r in bd.GetComponentsInChildren<Renderer>(true))
                {
                    var b = r.bounds;
                    if (b.size.sqrMagnitude < 1e-6f) continue;
                    for (int c = 0; c < 8; c++)
                    {
                        var wp = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                        z = Mathf.Min(z, ctx.Conv(ctx.model.InverseTransformPoint(wp)).z);
                    }
                    found = true;
                }
            if (!found) z = -J.F(J.O(ctx.state, "stage"), "depth", 8f) / 2f - 0.9f;
            return z;
        }

        // ------------------------------------------------------------------ メッシュ

        /// <summary>
        /// メッシュを作る道具。頂点ごとに
        ///  uv0 = 文字の場所（アトラスの x, y, 幅, 高さ。幅 0 = 文字なし）
        ///  uv1 = (ボタン番号, 種類, おまけ, 0)  種類：0 本体 / 1 ボタン / 2 文字の板 / 3 拍のランプ / 4 数字
        ///  uv2 = 面の中の位置（0〜1。側面は -1）
        /// 色は頂点カラー（リニア）
        /// </summary>
        public class MB
        {
            public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public List<Color> c = new List<Color>();
            public List<Vector4> uv0 = new List<Vector4>(), uv1 = new List<Vector4>();
            public List<Vector2> uv2 = new List<Vector2>();
            public List<int> t = new List<int>();

            /// <summary>a 左下・b 右下・c 右上・d 左上（表から見て）</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col, Vector4 rect, Vector4 info, Vector2 l0, Vector2 l1)
            {
                int i = v.Count;
                var nn = Vector3.Cross(d - a, b - a).normalized;
                v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
                var lc = col.linear; lc.a = 1f;
                for (int k = 0; k < 4; k++) { n.Add(nn); c.Add(lc); uv0.Add(rect); uv1.Add(info); }
                uv2.Add(new Vector2(l0.x, l0.y)); uv2.Add(new Vector2(l1.x, l0.y)); uv2.Add(new Vector2(l1.x, l1.y)); uv2.Add(new Vector2(l0.x, l1.y));
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col) { Quad(a, b, cc, d, col, Vector4.zero, Vector4.zero, -Vector2.one, -Vector2.one); }

            public Mesh ToMesh()
            {
                var m = new Mesh();
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v); m.SetNormals(n); m.SetColors(c);
                m.SetUVs(0, uv0); m.SetUVs(1, uv1); m.SetUVs(2, uv2);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static Mesh BuildDeskMesh(Desk d, List<string> missing) { return BuildDeskMB(d, missing).ToMesh(); }

        public static MB BuildDeskMB(Desk d, List<string> missing)
        {
            var mb = new MB();
            float sinT = Mathf.Sin(TILT * Mathf.Deg2Rad), cosT = Mathf.Cos(TILT * Mathf.Deg2Rad);
            float W = d.W, Dp = d.Dp, zf = d.zf, yf = FRONT_H + Dp * sinT, hw = W / 2f;
            // 盤面の座標（x 右・y 奥へ・h 盤面から上）→ 卓のローカル
            Func<float, float, float, Vector3> P = (px, py, ph) => new Vector3(px - hw, FRONT_H + py * sinT + ph * cosT, py * cosT - ph * sinT);

            // ---- 本体
            mb.Quad(P(0, 0, 0), P(W, 0, 0), P(W, Dp, 0), P(0, Dp, 0), PanelCol);
            mb.Quad(new Vector3(-hw, 0, 0), new Vector3(hw, 0, 0), new Vector3(hw, FRONT_H, 0), new Vector3(-hw, FRONT_H, 0), BodyCol);
            mb.Quad(new Vector3(-hw, 0, zf), new Vector3(-hw, 0, 0), new Vector3(-hw, FRONT_H, 0), new Vector3(-hw, yf, zf), BodyCol);
            mb.Quad(new Vector3(hw, 0, 0), new Vector3(hw, 0, zf), new Vector3(hw, yf, zf), new Vector3(hw, FRONT_H, 0), BodyCol);
            // 盤面のふち（手前の角を少し明るく）
            mb.Quad(P(0, 0, 0) + new Vector3(0, -0.012f, 0), P(W, 0, 0) + new Vector3(0, -0.012f, 0), P(W, 0, 0), P(0, 0, 0), new Color(0.16f, 0.17f, 0.24f));
            // ---- モニターの板
            float yt = yf + BRIDGE_H, z0 = zf, z1 = zf + BRIDGE_T;
            mb.Quad(new Vector3(-hw, 0, z0), new Vector3(hw, 0, z0), new Vector3(hw, yt, z0), new Vector3(-hw, yt, z0), BodyCol);
            mb.Quad(new Vector3(-hw, yt, z0), new Vector3(hw, yt, z0), new Vector3(hw, yt, z1), new Vector3(-hw, yt, z1), BodyCol);
            mb.Quad(new Vector3(hw, 0, z1), new Vector3(-hw, 0, z1), new Vector3(-hw, yt, z1), new Vector3(hw, yt, z1), BodyCol);
            mb.Quad(new Vector3(-hw, 0, z1), new Vector3(-hw, 0, z0), new Vector3(-hw, yt, z0), new Vector3(-hw, yt, z1), BodyCol);
            mb.Quad(new Vector3(hw, 0, z0), new Vector3(hw, 0, z1), new Vector3(hw, yt, z1), new Vector3(hw, yt, z0), BodyCol);
            // 卓の名前（板の左上）
            float nh = 0.06f, nw = nh * 6f;
            Label(mb, "N:" + d.name, new Vector3(-hw + 0.05f, yt - 0.025f - nh, z0 - 0.002f), nw, nh, NameCol, missing);

            // ---- ボタン・見出し・数字
            int slot = 0;
            foreach (var p in d.placed)
            {
                float gx = MARGIN + p.group * (COLS * PX + GROUP_GAP);
                float cx = gx + (p.col + p.span / 2f) * PX, cy = MARGIN + p.row * PY + PY / 2f;
                if (p.b == null)
                {
                    // 見出し：盤面に貼る文字の板（升目 span 個ぶん。1升 = 2:1）
                    float w = p.span * PX - 0.012f, h = Mathf.Min(CAP_D, w / (2f * p.span));
                    if (SKLabels.TryGet("T:" + p.title, out var rect, out _))
                        mb.Quad(P(cx - w / 2, cy - h / 2, LIFT), P(cx + w / 2, cy - h / 2, LIFT), P(cx + w / 2, cy + h / 2, LIFT), P(cx - w / 2, cy + h / 2, LIFT), TitleCol, rect, new Vector4(-1, 2, 0, 0), Vector2.zero, Vector2.one);
                    else missing.Add(p.title);
                    continue;
                }
                var b = p.b;
                if (b.readout >= 0)
                {
                    float w = CAP_W, h = Mathf.Min(CAP_D * 0.8f, w / b.digits);
                    mb.Quad(P(cx - w / 2, cy - h / 2, LIFT), P(cx + w / 2, cy - h / 2, LIFT), P(cx + w / 2, cy + h / 2, LIFT), P(cx - w / 2, cy + h / 2, LIFT), DigitCol, Vector4.zero, new Vector4(-1, 4, b.readout + 16 * b.digits, 0), Vector2.zero, Vector2.one);
                    continue;
                }
                if (b.beat)
                {
                    float r = 0.013f;
                    mb.Quad(P(cx - r, cy - r, LIFT), P(cx + r, cy - r, LIFT), P(cx + r, cy + r, LIFT), P(cx - r, cy + r, LIFT), BeatCol, Vector4.zero, new Vector4(-1, 3, 0, 0), Vector2.zero, Vector2.one);
                    continue;
                }
                b.slot = slot++;
                d.buttons.Add(b);
                b.localPos = P(cx, cy, 0);
                Cap(mb, P, cx, cy, b, missing);
            }
            return mb;
        }

        /// <summary>モニターの板（本体の色の面）に貼る文字</summary>
        static void Label(MB mb, string key, Vector3 bottomLeft, float w, float h, Color col, List<string> missing)
        {
            if (!SKLabels.TryGet(key, out var rect, out _)) { missing.Add(key.Substring(2)); return; }
            var a = bottomLeft; var r = new Vector3(w, 0, 0); var u = new Vector3(0, h, 0);
            mb.Quad(a, a + r, a + r + u, a + u, col, rect, new Vector4(-1, 2, 1, 0), Vector2.zero, Vector2.one);
        }

        /// <summary>ボタン1個：上の面（文字・色見本・ランプ）＋側面4枚</summary>
        static void Cap(MB mb, Func<float, float, float, Vector3> P, float cx, float cy, Btn b, List<string> missing)
        {
            float x0 = cx - CAP_W / 2, x1 = cx + CAP_W / 2, y0 = cy - CAP_D / 2, y1 = cy + CAP_D / 2, H = CAP_H;
            var info = new Vector4(b.slot, 1, b.stripes != null ? 1 : 0, b.lamp ? 1 : 0);
            if (b.stripes != null)
            {
                int n = b.stripes.Length;
                for (int k = 0; k < n; k++)
                {
                    float a0 = (float)k / n, a1 = (float)(k + 1) / n, xa = Mathf.Lerp(x0, x1, a0), xb = Mathf.Lerp(x0, x1, a1);
                    mb.Quad(P(xa, y0, H), P(xb, y0, H), P(xb, y1, H), P(xa, y1, H), b.stripes[k], Vector4.zero, info, new Vector2(a0, 0), new Vector2(a1, 1));
                }
            }
            else
            {
                Vector4 rect = Vector4.zero;
                if (!string.IsNullOrEmpty(b.label) && !b.custom && !SKLabels.TryGet("B:" + b.label, out rect, out _)) missing.Add(b.label);
                mb.Quad(P(x0, y0, H), P(x1, y0, H), P(x1, y1, H), P(x0, y1, H), b.col, rect, info, Vector2.zero, Vector2.one);
            }
            var side = b.stripes != null ? b.stripes[0] * 0.8f : b.col * 0.8f;
            var m1 = -Vector2.one;
            mb.Quad(P(x0, y0, 0), P(x1, y0, 0), P(x1, y0, H), P(x0, y0, H), side, Vector4.zero, info, m1, m1);
            mb.Quad(P(x1, y1, 0), P(x0, y1, 0), P(x0, y1, H), P(x1, y1, H), side, Vector4.zero, info, m1, m1);
            mb.Quad(P(x0, y1, 0), P(x0, y0, 0), P(x0, y0, H), P(x0, y1, H), side, Vector4.zero, info, m1, m1);
            mb.Quad(P(x1, y0, 0), P(x1, y1, 0), P(x1, y1, H), P(x1, y0, H), side, Vector4.zero, info, m1, m1);
        }

        /// <summary>確認用モニター（卓の奥の板に横並び。下に名前）</summary>
        static void BuildMonitors(SKContext ctx, Desk d, Mesh quad, float yf)
        {
            var mons = d.mons.Where(m => m.tex != null).ToList();
            if (mons.Count == 0) return;
            // 画面の大きさ：映像の縦横比のまま（カメラは映すモニターに合わせて縦長・横長になる）、幅 w・高さ 0.3m の中に収める
            const float MAXH = 0.30f;
            foreach (var mo in mons)
            {
                float a = (float)mo.tex.width / Mathf.Max(1, mo.tex.height);
                mo.sw = mo.w; mo.sh = mo.w / a;
                if (mo.sh > MAXH) { mo.sh = MAXH; mo.sw = MAXH * a; }
            }
            float gap = 0.06f, total = mons.Sum(m => m.sw) + gap * (mons.Count - 1);
            float x = -total / 2f, z = d.zf - 0.006f, yb = yf + 0.075f;
            var mb = new MB();
            var missing = new List<string>();
            foreach (var mo in mons)
            {
                float h = mo.sh, cx = x + mo.sw / 2f;
                x += mo.sw + gap;
                // 縁
                float e = 0.012f;
                mb.Quad(new Vector3(cx - mo.sw / 2 - e, yb - e, z + 0.002f), new Vector3(cx + mo.sw / 2 + e, yb - e, z + 0.002f), new Vector3(cx + mo.sw / 2 + e, yb + h + e, z + 0.002f), new Vector3(cx - mo.sw / 2 - e, yb + h + e, z + 0.002f), BezelCol);
                // 名前（縁の下）
                float lh = 0.03f, lw = lh * 4f;
                Label(mb, "M:" + mo.label, new Vector3(cx - lw / 2, yb - e - 0.004f - lh, z + 0.002f), lw, lh, MonCol, missing);
                // 画面
                var go = new GameObject("Screen_" + SKBuilder.Sanitize(mo.label));
                go.transform.SetParent(d.go.transform, false);
                go.transform.localPosition = new Vector3(cx, yb + h / 2f, z);
                go.transform.localScale = new Vector3(mo.sw, h, 1f);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = go.AddComponent<MeshRenderer>();
                mo.mat = SKAssets.NewMaterial(SKAssets.ShaderScreen, ctx.dir + "/Materials/Screen_" + d.key + "_" + SKBuilder.Sanitize(mo.label) + ".mat");
                mo.mat.SetTexture("_MainTex", mo.tex);
                EditorUtility.SetDirty(mo.mat);
                mr.sharedMaterial = mo.mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            // 縁と名前は卓のマテリアルで（別のメッシュ）
            var fr = new GameObject("MonitorFrames");
            fr.transform.SetParent(d.go.transform, false);
            var mesh = mb.ToMesh();
            mesh.name = "SK_DeskMon_" + d.key;
            SKAssets.Save(mesh, ctx.dir + "/Meshes/SK_DeskMon_" + d.key + ".asset");
            fr.AddComponent<MeshFilter>().sharedMesh = mesh;
            var fmr = fr.AddComponent<MeshRenderer>();
            fmr.sharedMaterial = d.mat;
            fmr.shadowCastingMode = ShadowCastingMode.Off;
            if (missing.Count > 0) ctx.Log("⚠ モニターの名前の文字が無いものがありました：" + string.Join("・", missing));
        }

        static void BuildFloor(SKContext ctx, Transform root, float total, float maxZf)
        {
            var mb = new MB();
            float hw = total / 2f + 0.8f, z0 = -1.8f, z1 = maxZf + BRIDGE_T + 0.4f, y = 0.004f;
            mb.Quad(new Vector3(-hw, y, z0), new Vector3(hw, y, z0), new Vector3(hw, y, z1), new Vector3(-hw, y, z1), FloorCol);
            // 立ち位置の目印（卓の手前に1本の線）
            mb.Quad(new Vector3(-total / 2f, y + 0.001f, -0.62f), new Vector3(total / 2f, y + 0.001f, -0.62f), new Vector3(total / 2f, y + 0.001f, -0.58f), new Vector3(-total / 2f, y + 0.001f, -0.58f), new Color(0.35f, 0.3f, 0.12f));
            var go = new GameObject("BackstageFloor");
            go.transform.SetParent(root, false);
            var mesh = mb.ToMesh();
            mesh.name = "SK_BackstageFloor";
            SKAssets.Save(mesh, ctx.dir + "/Meshes/SK_BackstageFloor.asset");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ctx.dir + "/Materials/Desk_Light.mat");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0, -0.02f, (z0 + z1) / 2f); bc.size = new Vector3(hw * 2f, 0.05f, z1 - z0);
        }

        static Mesh Quad()
        {
            var m = new Mesh { name = "SK_ScreenQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            m.RecalculateBounds();
            return m;
        }
    }
}
