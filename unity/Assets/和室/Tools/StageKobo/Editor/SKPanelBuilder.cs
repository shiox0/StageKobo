using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// 操作パネル（v0.11〜）：レーザーで押す UI（World Space Canvas）の横長パネル。手前と裏に同じものを置く。
    ///  ・タブ：照明 ／ VJ（左 = デッキA・まん中 = 映す・切り替え・パッド・右 = デッキB）／ VJ の効果 ／ 特効 ／ モニター・カメラ
    ///    モニター・カメラ（v0.12〜）：左 = メイン・サブ右・サブ左・背景 LED に何を映すか、まん中・右 = カメラ1・2 の対象（名前の欄）・ライトも追従・カメラワーク
    ///    タブの切り替えは押した人の画面だけ（GameObject.SetActive の UI イベント）。ボタンの内容は全員に同期（UdonSharp 版）
    ///  ・手前：持ち運べる（取っ手に VRC Pickup・VRC Object Sync。パネルは取っ手に付いていく）。「元の場所に戻す」ボタン
    ///  ・裏：背景の裏に固定。右側に確認用のプレビュー（VJ 出力・デッキA/B・カメラ1/2）
    ///  ・以前のステージ裏の卓（物理スイッチ）とリモコン（縦長）の代わり
    /// </summary>
    public static class SKPanelBuilder
    {
        const float PX = 0.0007f;                // 1px = 0.7mm（パネル 1800px = 1.26m）
        const int W = 1800, H = 800, HEAD = 122, PAD = 18, BW = 100, BH = 46, GAP = 6, TITLE = 28, SECGAP = 10, PREV = 600;
        const float TILT = 30f;                   // 書見台のように奥へ倒す
        static Font font;

        static readonly Color BG = new Color(0.05f, 0.06f, 0.14f, 0.94f), HeadBG = new Color(0.08f, 0.09f, 0.2f, 1f), ColBG = new Color(0.08f, 0.09f, 0.19f, 0.9f);
        static readonly Color CBtn = new Color(0.13f, 0.15f, 0.33f), CTempo = new Color(0.1f, 0.3f, 0.32f), CTap = new Color(0.15f, 0.42f, 0.4f), CFx = new Color(0.45f, 0.2f, 0.5f),
            CWarn = new Color(0.5f, 0.12f, 0.2f), CPad = new Color(0.52f, 0.16f, 0.44f), CVJ = new Color(0.19f, 0.13f, 0.37f), CDeckA = new Color(0.1f, 0.25f, 0.46f), CDeckB = new Color(0.42f, 0.16f, 0.36f),
            CScene = new Color(0.12f, 0.3f, 0.42f), CUp = new Color(0.42f, 0.24f, 0.12f), CTab = new Color(0.14f, 0.16f, 0.32f);
        static readonly Color TitleCol = new Color(0.5f, 0.96f, 0.84f), SecCol = new Color(1f, 0.56f, 0.82f), TextCol = new Color(0.86f, 0.9f, 1f), MarkCol = new Color(0.5f, 1f, 0.88f),
            ACol = new Color(0.55f, 0.82f, 1f), BCol = new Color(1f, 0.62f, 0.86f);

        // ------------------------------------------------------------------ 設計図

        class Btn
        {
            public string label, tip;
            public Color color = CBtn;
            public int kind, ch = -1, val;       // kind 0 = 照明（StageKoboController）/ 1 = VJ（StageKoboVJ）
            public bool mark;                     // 選ばれていたら印
            public string[] stripes;
            public int pickCam = -1, pickSlot;    // カメラの対象の名前の欄（ボタンの文字をコントローラーが書き換える）
            public string trigger; public Animator anim;                         // Animator だけの版
            public List<ParticleSystem> ps; public List<AudioSource> audio;
        }

        class Sec
        {
            public string title;
            public Color titleCol = SecCol;
            public List<Btn> btns = new List<Btn>();
            public int info = -1, lines = 0;      // info >= 0：ボタンの代わりに状態の文字（0 = A, 1 = B, 2 = まん中）
            public int fontSize = 19;
            public int per;                       // 1 行のボタンの数（0 = 幅から自動）
            public bool header;                   // 見出しだけ（ボタンが無くても消さない）
        }

        class Column { public float x, w; public Color bg = new Color(0, 0, 0, 0); public List<Sec> secs = new List<Sec>(); }

        class Page { public string tab; public List<Column> cols = new List<Column>(); }

        static Btn B(string label, int ch, int val, Color c, bool mark = true, int kind = 0) { return new Btn { label = label, ch = ch, val = val, color = c, mark = mark, kind = kind }; }

        static Sec S(string title, params Btn[] btns) { var s = new Sec { title = title }; s.btns.AddRange(btns); return s; }

        static Sec Many(string title, string[] labels, int kind, int act, int[] vals, Color col, bool mark = true)
        {
            var s = new Sec { title = title };
            for (int i = 0; i < labels.Length; i++) s.btns.Add(B(labels[i], act, vals != null ? vals[i] : i, col, mark, kind));
            return s;
        }

        static string[] L(params string[] a) { return a; }

        /// <summary>チャンネル（Animator のレイヤー）にあるステートだけボタンにする</summary>
        static Sec Channel(SKContext ctx, string title, int chIndex, IEnumerable<string> states, Animator anim, Dictionary<string, string> rename = null)
        {
            var s = new Sec { title = title };
            var ch = ctx.channels[chIndex];
            if (ch == null) return s;
            foreach (var st in states)
            {
                int idx = ch.IndexOf(st);
                if (idx < 0) continue;
                string label = rename != null && rename.TryGetValue(st, out var r) ? r : SKRemoteBuilder.JP.TryGetValue(st, out var jp) ? jp : st;
                s.btns.Add(new Btn { label = label, ch = chIndex, val = idx, mark = true, trigger = st, anim = anim });
            }
            return s;
        }

        static Sec Palette(SKContext ctx, Animator anim)
        {
            var s = new Sec { title = "色（パレット）" };
            var ch = ctx.channels[SKCh.PALETTE];
            if (ch == null) return s;
            int live = ch.IndexOf("Pal_Live");
            if (live >= 0) s.btns.Add(new Btn { label = "シーン色", ch = SKCh.PALETTE, val = live, mark = true, trigger = "Pal_Live", anim = anim, stripes = new[] { ctx.live.c1, ctx.live.c2, ctx.live.c3 } });
            var pals = J.L(ctx.data, "palettes");
            for (int i = 0; i < pals.Count; i++)
            {
                int idx = ch.IndexOf("Pal_" + i);
                var cs = J.L(pals[i], "c");
                if (idx < 0 || cs.Count < 3) continue;
                s.btns.Add(new Btn { label = "", tip = J.S(pals[i], "name"), ch = SKCh.PALETTE, val = idx, mark = true, trigger = "Pal_" + i, anim = anim, stripes = cs.Take(3).Select(c => c as string).ToArray() });
            }
            return s;
        }

        // ------------------------------------------------------------------ ページ

        static List<Page> Pages(SKContext ctx, bool udon, SKVJInfo vjInfo)
        {
            var pages = new List<Page>();
            var a = ctx.lightAnimator;

            // ---- 照明
            var light = new List<Sec>();
            if (udon)
            {
                light.Add(S("テンポ（TAP は拍に合わせて2回以上）", B("−5", SKCh.ACT_BPM, -5, CTempo, false), B("−1", SKCh.ACT_BPM, -1, CTempo, false), B("TAP", SKCh.ACT_BPM, 0, CTap, false),
                    B("+1", SKCh.ACT_BPM, 1, CTempo, false), B("+5", SKCh.ACT_BPM, 5, CTempo, false)));
                if (ctx.channels[SKCh.MOVE] != null) light.Add(Many("動きの速さ", L("×½", "×1", "×2"), 0, SKCh.ACT_SPEED, null, CBtn));
            }
            var master = Channel(ctx, "照明ぜんぶ", SKCh.MASTER, L("Master_On", "Master_Off", "Master_Strobe"), a);
            foreach (var b in master.btns)
            {
                if (b.trigger == "Master_Off") b.color = CWarn;
                if (b.trigger == "Master_Strobe") { b.color = CFx; if (udon) { b.ch = SKCh.ACT_STROBE; b.val = 0; b.mark = false; } }
            }
            light.Add(master);
            if (udon && ctx.audio) light.Add(Many("曲の音に反応（AudioLink）", L("ON", "OFF"), 0, SKCh.ACT_AUDIO, new[] { 1, 0 }, CTempo));
            light.Add(Channel(ctx, "動き", SKCh.MOVE, SKMath.Patterns.Select(x => "Move_" + x), a));
            light.Add(Channel(ctx, "色の付け方", SKCh.COLOR, SKMath.ColorModes.Select(x => "Col_" + x), a));
            light.Add(Palette(ctx, a));
            light.Add(Channel(ctx, "明るさ", SKCh.DIM, SKMath.DimModes.Select(x => "Dim_" + x), a));
            light.Add(Channel(ctx, "レーザー", SKCh.LASER, SKMath.LaserModes.Select(x => "Laser_" + x), a));
            light.Add(Channel(ctx, "カラーウォッシュ（床の色）", SKCh.WASH, L("Wash_On", "Wash_Off"), a));
            light.Add(Channel(ctx, "客席のペンライト", SKCh.PEN, L("Pen_cue", "Pen_white", "Pen_rainbow"), a));
            if (udon && ctx.spotLights.Count > 0)
                light.Add(S("スポットライト（押した人を追う・もう一度で固定。カメラの「ライトも追従」が ON ならカメラの対象）", B("自分→スポット1", SKCh.ACT_SPOT, 0, CUp), B("自分→スポット2", SKCh.ACT_SPOT, 1, CUp), B("固定に戻す", SKCh.ACT_SPOT, 3, CWarn, false),
                    B("スポット ON", SKCh.ACT_SPOTON, 1, CTempo), B("スポット OFF", SKCh.ACT_SPOTON, 0, CTempo)));
            pages.Add(Flow("照明", light, 3));

            // ---- VJ（UdonSharp 版で VJ があるときだけ）
            if (udon && vjInfo != null && vjInfo.ctrl != null)
            {
                var defs = J.O(ctx.data, "vjDefs");
                var gs = J.L(defs, "gensShort").Select(x => x as string ?? "").ToList();
                var gf = J.L(defs, "gensFull").Select(x => x as string ?? "").ToList();
                var vjPage = new Page { tab = "VJ" };
                float x0 = PAD, wA = 560, wC = 600, g = (W - 2 * PAD - wA * 2 - wC) / 2f;
                for (int k = 0; k < 2; k++)
                {
                    int off = k == 0 ? SKVJIdx.DECK_A : SKVJIdx.DECK_B;
                    var deckCol = k == 0 ? CDeckA : CDeckB;
                    var col = new Column { x = k == 0 ? x0 : x0 + wA + g + wC + g, w = wA, bg = new Color(deckCol.r, deckCol.g, deckCol.b, 0.18f) };
                    col.secs.Add(new Sec { title = k == 0 ? "デッキA（左）" : "デッキB（右）", titleCol = k == 0 ? ACol : BCol, info = k, lines = 3 });
                    var gen = new Sec { title = "映像の素" };
                    for (int i = 0; i < Mathf.Min(gs.Count, SKVJIdx.NGEN); i++)
                    {
                        if (i == SKVJIdx.GEN_IMAGE && vjInfo.images.Count == 0) continue;
                        if (i == SKVJIdx.GEN_TEXT && vjInfo.text == null) continue;
                        gen.btns.Add(new Btn { label = gs[i], tip = i < gf.Count ? gf[i] : gs[i], kind = 1, ch = off + SKVJIdx.V_GEN, val = i, mark = true, color = deckCol });
                    }
                    col.secs.Add(gen);
                    col.secs.Add(Many("速さ", L("×¼", "×½", "×1", "×2", "×4"), 1, off + SKVJIdx.V_SPEED, null, CVJ));
                    col.secs.Add(S("数 ・ ズーム", B("数 −", off + SKVJIdx.V_COUNT, -1, CVJ, false, 1), B("数 ＋", off + SKVJIdx.V_COUNT, 1, CVJ, false, 1),
                        B("ズーム −", off + SKVJIdx.V_ZOOM, -1, CVJ, false, 1), B("ズーム ＋", off + SKVJIdx.V_ZOOM, 1, CVJ, false, 1)));
                    vjPage.cols.Add(col);
                }
                var c = new Column { x = x0 + wA + g, w = wC, bg = new Color(0.2f, 0.18f, 0.4f, 0.22f) };
                c.secs.Add(new Sec { title = "いま映っているもの", titleCol = TitleCol, info = 2, lines = 4, fontSize = 18 });
                c.secs.Add(S("映す（A と B を切り替える）", B("◀ A を映す", SKVJIdx.V_CUT, 0, CDeckA, true, 1), B("⇄ 入れ替え", SKVJIdx.V_CUT, 2, CVJ, false, 1), B("B を映す ▶", SKVJIdx.V_CUT, 1, CDeckB, true, 1),
                    B("選んだら切り替え", SKVJIdx.V_TAKE, 0, CVJ, true, 1)));
                var how = Many("切り替えの長さ", L("カット", "1拍", "2拍", "4拍"), 1, SKVJIdx.V_FADELEN, new[] { 0, 1, 2, 4 }, CVJ);
                c.secs.Add(how);
                c.secs.Add(Many("拍で交互に映す", L("しない", "1拍", "2拍", "4拍"), 1, SKVJIdx.V_XFAUTO, new[] { 0, 1, 2, 4 }, CVJ));
                var pad = Many("パッド（押した瞬間）", L("フラッシュ", "反転", "グリッチ", "ズーム", "ストロボ"), 1, SKVJIdx.V_PAD, null, CPad, false);
                pad.btns.Add(B("暗転", SKVJIdx.V_BLACK, 0, CWarn, true, 1));
                c.secs.Add(pad);
                var au = new Sec { title = "オートVJ（おまかせで切り替え）" };
                au.btns.Add(B("オート", SKVJIdx.V_AUTO, 0, CScene, true, 1));
                au.btns.AddRange(Many("", L("1小節", "2小節", "4小節", "8小節"), 1, SKVJIdx.V_AUTOBARS, new[] { 1, 2, 4, 8 }, CVJ).btns);
                c.secs.Add(au);
                vjPage.cols.Insert(1, c);
                pages.Add(vjPage);

                // ---- VJ の効果
                var fxs = new List<Sec>();
                var sc = new Sec { title = "シーン（まとめて切り替え）" };
                for (int i = 0; i < vjInfo.presetNames.Count; i++) sc.btns.Add(B(vjInfo.presetNames[i], SKVJIdx.V_PRESET, i, CScene, false, 1));
                fxs.Add(sc);
                var vcol = new Sec { title = "色" };
                vcol.btns.Add(B("照明の色", SKVJIdx.V_PAL, -1, CVJ, true, 1));
                var pals = J.L(ctx.data, "palettes");
                for (int i = 0; i < Mathf.Min(8, pals.Count); i++)
                {
                    var cs = J.L(pals[i], "c");
                    if (cs.Count < 3) continue;
                    vcol.btns.Add(new Btn { label = "", tip = J.S(pals[i], "name"), kind = 1, ch = SKVJIdx.V_PAL, val = i, mark = true, stripes = cs.Take(3).Select(x => x as string).ToArray() });
                }
                fxs.Add(vcol);
                fxs.Add(Many("色の付け方", J.L(defs, "colorModeNames").Select(x => x as string ?? "").ToArray(), 1, SKVJIdx.V_CMODE, null, CVJ));
                fxs.Add(Many("色が回る", L("なし", "ゆっくり", "はやい"), 1, SKVJIdx.V_HUE, null, CVJ));
                fxs.Add(Many("エフェクト（押すたびに ON / OFF）", vjInfo.fxNames, 1, SKVJIdx.V_FX, null, CFx));
                fxs.Add(Many("ミラー", J.L(defs, "mirrors").Select(x => x as string ?? "").ToArray(), 1, SKVJIdx.V_MIRROR, null, CVJ));
                fxs.Add(Many("万華鏡（何枚に分けるか）", L("3", "4", "6", "8", "12"), 1, SKVJIdx.V_KALE, new[] { 3, 4, 6, 8, 12 }, CVJ));
                fxs.Add(Many("ドンの間隔", L("½拍", "1拍", "2拍", "4拍"), 1, SKVJIdx.V_RATE, null, CVJ));
                fxs.Add(Many("ドゥン量（拍で脈打つ）", L("なし", "弱", "中", "強"), 1, SKVJIdx.V_PUMP, null, CVJ));
                fxs.Add(Many("エフェクトの強さ", L("弱", "中", "強"), 1, SKVJIdx.V_AMT, null, CVJ));
                fxs.Add(Many("明るさ", L("50%", "75%", "100%", "130%"), 1, SKVJIdx.V_BRIGHT, null, CVJ));
                if (vjInfo.text != null)
                {
                    var tx = new Sec { title = "文字を重ねる" };
                    tx.btns.Add(B("重ねる", SKVJIdx.V_TEXTON, 0, CVJ, true, 1));
                    tx.btns.AddRange(Many("", L("ドン", "点滅", "固定"), 1, SKVJIdx.V_TEXTANIM, null, CVJ).btns);
                    fxs.Add(tx);
                }
                if (vjInfo.images.Count > 0)
                {
                    var im = new Sec { title = "画像（いま映っているデッキに）" };
                    for (int i = 0; i < Mathf.Min(vjInfo.images.Count, 12); i++) im.btns.Add(B("画像" + (i + 1), SKVJIdx.V_IMG, i, CVJ, true, 1));
                    fxs.Add(im);
                    fxs.Add(Many("拍で次の画像へ", L("しない", "1拍", "2拍", "4拍", "8拍"), 1, SKVJIdx.V_IMGRATE, new[] { 0, 1, 2, 4, 8 }, CVJ));
                }
                if (vjInfo.slotNames.Count > 0)
                {
                    var sl = new Sec { title = "登録シーン（ブラウザで登録したもの）" };
                    for (int i = 0; i < vjInfo.slotNames.Count; i++) sl.btns.Add(B(vjInfo.slotNames[i], SKVJIdx.V_SLOT, i, CScene, false, 1));
                    fxs.Add(sl);
                }
                var fxPage = Flow("VJ の効果", fxs, 3, true);
                pages.Add(fxPage);
            }

            // ---- 特効（これから増えても、このタブだけで並べられるように独立）
            var fxl = new List<Sec>();
            var fx = new Sec { title = "特効（押すと 1 回出る）" };
            var groups = new[] { ctx.sparks, ctx.confetti, ctx.smoke };
            string[] fxNames = { "スパーク", "紙吹雪", "スモーク" };
            for (int gi = 0; gi < 3; gi++)
                if (groups[gi].Count > 0) fx.btns.Add(new Btn { label = fxNames[gi], color = CFx, ch = SKCh.ACT_FX, val = gi, ps = groups[gi] });
            fxl.Add(fx);
            if (ctx.fxCustom.Count > 0)
            {
                var fx2 = new Sec { title = udon && ctx.fxCustom.Any(x => x.mode == 1) ? "追加の特効（ON/OFF のものは、もう一度押すと止まる）" : "追加の特効" };
                for (int k = 0; k < ctx.fxCustom.Count; k++)
                {
                    var cc = ctx.fxCustom[k];
                    fx2.btns.Add(new Btn { label = cc.name, color = CFx, ch = SKCh.ACT_FX, val = 3 + k, ps = cc.systems, audio = cc.audio, mark = udon && cc.mode == 1 });
                }
                fxl.Add(fx2);
            }
            pages.Add(Flow("特効", fxl, 2));

            // ---- モニター・カメラ
            pages.Add(MonitorCameraPage(ctx, udon, vjInfo));

            foreach (var p in pages) foreach (var col in p.cols) col.secs.RemoveAll(s => s.info < 0 && s.btns.Count == 0 && !s.header);
            foreach (var p in pages) foreach (var col in p.cols) if (col.secs.All(s => s.btns.Count == 0 && s.info < 0)) col.secs.Clear();
            pages.RemoveAll(p => p.cols.All(col => col.secs.Count == 0));
            return pages;
        }

        /// <summary>
        /// モニター・カメラのタブ。左の列 = どのモニターに何を映すか（1 行 = 1 台）、まん中・右 = カメラ1・カメラ2
        /// （映す人を名前で選ぶ・ライトも追従・カメラワーク）
        /// </summary>
        static Page MonitorCameraPage(SKContext ctx, bool udon, SKVJInfo vjInfo)
        {
            var a = ctx.lightAnimator;
            var p = new Page { tab = "モニター・カメラ" };
            float wL = 540, wC = (W - 2 * PAD - wL - 2 * 20f) / 2f;
            var left = new Column { x = PAD, w = wL, bg = new Color(0.2f, 0.18f, 0.4f, 0.18f) };
            p.cols.Add(left);
            var rn = new Dictionary<string, string>();
            string[] keys = { "Cam1", "Cam2", "VJ", "Video" }, labels = { "カメラ1", "カメラ2", "VJ", "映像" };
            string[] pres = { "Monitor", "Monitor2", "Monitor3" };
            string[] titles = { "メインモニター", "サブモニター 右（客席から見て）", "サブモニター 左（客席から見て）" };
            int[] chs = { SKCh.MONITOR, SKCh.MONITOR2, SKCh.MONITOR3 };
            for (int g = 0; g < 3; g++)
            {
                for (int k = 0; k < 4; k++) rn[pres[g] + "_" + keys[k]] = labels[k];
                var sec = Channel(ctx, titles[g], chs[g], keys.Select(k => pres[g] + "_" + k), a, rn);
                sec.per = 4;
                foreach (var b in sec.btns) if (b.label == "VJ") b.color = CVJ; else if (b.label == "映像") b.color = CScene;
                left.secs.Add(sec);
            }
            // 背景の LED：模様 ／ VJ ／ 映像
            bool hasBack = ctx.leds.Any(l => l.backdrop);
            bool hasVJ = udon && vjInfo != null && vjInfo.ctrl != null;
            bool hasBackVideo = ctx.channels[SKCh.BACKDROP] != null;
            if (hasBack)
            {
                var bk = new Sec { title = "背景の LED", per = 4 };
                if (udon)
                {
                    if (hasVJ || hasBackVideo) bk.btns.Add(B("模様", SKCh.ACT_BACK, 0, CBtn));
                    if (hasVJ) bk.btns.Add(B("VJ", SKCh.ACT_BACK, 1, CVJ));
                    if (hasBackVideo) bk.btns.Add(B("映像", SKCh.ACT_BACK, 2, CScene));
                }
                else
                {
                    var c = Channel(ctx, "", SKCh.BACKDROP, L("Back_Normal", "Back_Video"), a, new Dictionary<string, string> { { "Back_Normal", "模様" }, { "Back_Video", "映像" } });
                    bk.btns.AddRange(c.btns);
                }
                if (bk.btns.Count > 1) left.secs.Add(bk);
            }
            if (hasVJ && ctx.leds.Count(l => l.backdrop || l.camMonitor || l.vjAlways) > 1)
            {
                var map = Many("VJ の映し方（LED が何枚もあるとき）", L("1枚ずつ", "つなげて1枚"), 1, SKVJIdx.V_MAP, null, CVJ);
                map.per = 4;
                left.secs.Add(map);
            }

            // カメラ1・カメラ2
            for (int k = 0; k < 2; k++)
            {
                var col = new Column { x = PAD + wL + 20f + k * (wC + 20f), w = wC, bg = new Color(0.25f, 0.16f, 0.08f, 0.16f) };
                bool hasCam = ctx.cams[k] != null || ctx.camAnimators[k] != null;
                if (!hasCam) { p.cols.Add(col); continue; }
                col.secs.Add(new Sec { title = "カメラ" + (k + 1), titleCol = TitleCol, header = true });
                if (udon && ctx.cams[k] != null)
                {
                    var who = new Sec { title = "映す人（名前を押す・もう一度押すと外れる）", per = 3, fontSize = 17 };
                    for (int i = 0; i < 6; i++)
                        who.btns.Add(new Btn { label = "－", ch = SKCh.ACT_PICK, val = k * 100 + i, color = CUp, mark = true, pickCam = k, pickSlot = i });
                    col.secs.Add(who);
                    var nav = new Sec { title = "", per = 3 };
                    nav.btns.Add(B("◀ 前の 6 人", SKCh.ACT_PICK, k * 100 + 90, CTab, false));
                    nav.btns.Add(B("次の 6 人 ▶", SKCh.ACT_PICK, k * 100 + 91, CTab, false));
                    nav.btns.Add(B("外す（全体を映す）", SKCh.ACT_PICK, k * 100 + 99, CWarn, false));
                    col.secs.Add(nav);
                    if (ctx.spotLights.Count > k)
                    {
                        var link = Many("スポット" + (k + 1) + "もこの人を追う（ライトも追従）", L("ON", "OFF"), 0, SKCh.ACT_LINK, new[] { k * 10 + 1, k * 10 }, CTempo);
                        link.per = 3;
                        col.secs.Add(link);
                    }
                }
                if (ctx.camAnimators[k] != null)
                {
                    var shots = Channel(ctx, "カメラワーク", k == 0 ? SKCh.CAM1 : SKCh.CAM2, SKMath.CamModes.Select(x => "Shot_" + x), ctx.camAnimators[k]);
                    shots.per = 4;
                    col.secs.Add(shots);
                }
                p.cols.Add(col);
            }
            return p;
        }

        /// <summary>セクションを n 列に流し込む（順番はそのまま、いちばん低い列へ）</summary>
        static Page Flow(string tab, List<Sec> secs, int n, bool withInfo = false)
        {
            var p = new Page { tab = tab };
            float colW = (W - 2 * PAD - (n - 1) * 20f) / n;
            for (int i = 0; i < n; i++) p.cols.Add(new Column { x = PAD + i * (colW + 20f), w = colW });
            var hs = new float[n];
            if (withInfo)
            {
                // VJ の効果のページ：いまの状態（まん中の文字）を左の列のいちばん上に
                p.cols[0].secs.Add(new Sec { title = "いま映っているもの", titleCol = TitleCol, info = 2, lines = 4, fontSize = 16 });
                hs[0] += SecHeight(p.cols[0].secs[0], colW);
            }
            foreach (var s in secs)
            {
                if (s.btns.Count == 0) continue;
                int best = 0;
                for (int i = 1; i < n; i++) if (hs[i] < hs[best] - 1f) best = i;
                p.cols[best].secs.Add(s);
                hs[best] += SecHeight(s, colW);
            }
            return p;
        }

        static int PerRow(float w) { return Mathf.Max(1, Mathf.FloorToInt((w + GAP) / (BW + GAP))); }

        static int PerRow(Sec s, float w) { return s.per > 0 ? s.per : PerRow(w); }

        static float SecHeight(Sec s, float w)
        {
            if (s.info >= 0) return TITLE + s.lines * (s.fontSize + 7) + 8 + SECGAP;
            if (s.btns.Count == 0) return string.IsNullOrEmpty(s.title) ? 0 : TITLE;
            int rows = (s.btns.Count + PerRow(s, w) - 1) / PerRow(s, w);
            return (string.IsNullOrEmpty(s.title) ? 0 : TITLE) + rows * (BH + GAP) + SECGAP;
        }

        // ------------------------------------------------------------------ 組み立て

        /// <summary>組み立ての最後のほう（VJ のコントローラーを作ったあと）に呼ぶ</summary>
        public static void Build(SKContext ctx)
        {
            if (ctx.lightAnimator == null) return;
            var cfg = ctx.cfg;
            font = LoadFont();
            bool udon = ctx.udon;
            var vjInfo = ctx.vj != null && ctx.vj.ctrl != null ? ctx.vj : null;
            if (udon && ctx.vj != null && vjInfo == null) ctx.Log("⚠ VJ の UdonSharp を作れなかったので、操作パネルに VJ のタブは作りませんでした");
            var pages = Pages(ctx, udon, vjInfo);
            var st = J.O(ctx.state, "stage");
            float sw = J.F(st, "width", 16f), sd = J.F(st, "depth", 8f);

            var items = new List<SKUdon.PanelItem>();
            var locals = new List<KeyValuePair<Btn, Button>>();
            var parts = new SKUdon.PanelParts();

            // ---- 手前（持ち運べる）
            if (cfg.remote)
            {
                var front = new GameObject("Panel_Front（操作パネル・手前）");
                front.transform.SetParent(ctx.root.transform, false);
                Vector3 posW = ctx.ToWorld(new Vector3(-(sw / 2 + 2.6f), 0f, sd / 2 + 2.5f));
                var face = Quaternion.LookRotation(ctx.DirToWorld(new Vector3(0, 0, -1)), Vector3.up);
                front.transform.SetPositionAndRotation(posW, face);
                var canvas = BuildCanvas(ctx, front.transform, "Canvas", pages, false, udon, items, locals, parts);
                PlaceOnStand(ctx, front.transform, canvas, udon, parts);
            }
            // ---- 裏（背景の裏に固定・プレビュー付き）
            if (cfg.desks && udon)
            {
                var back = new GameObject("Panel_Back（操作パネル・ステージ裏）");
                back.transform.SetParent(ctx.root.transform, false);
                float z = BackZ(ctx, st);
                back.transform.SetPositionAndRotation(ctx.ToWorld(new Vector3(0, 0, z)), Quaternion.LookRotation(ctx.DirToWorld(new Vector3(0, 0, 1)), Vector3.up));
                var canvas = BuildCanvas(ctx, back.transform, "Canvas", pages, true, udon, items, locals, parts);
                PlaceFixed(ctx, back.transform, canvas, true);
                parts.backCenter = back.transform;
            }
            else if (cfg.desks && !udon)
                ctx.Log("（ステージ裏の操作パネルは UdonSharp 版のときだけ作ります）");
            if (items.Count == 0 && locals.Count == 0) return;

            // ---- 配線
            if (udon)
            {
                if (SKUdon.WirePanels(ctx, items, parts)) { Finish(ctx, true); return; }
                ctx.Log("⚠ UdonSharp の設定に失敗したので、Animator だけの操作パネルにしました（押した人の画面だけ切り替わります）");
            }
            foreach (var kv in locals) WireLocal(kv.Key, kv.Value);
            Finish(ctx, false);
        }

        static void Finish(SKContext ctx, bool udon)
        {
            // VRChat の UI を触れるようにする（SDK があるときだけ）
            var uiShape = SKUdon.FindType("VRC.SDK3.Components.VRCUiShape");
            int n = 0;
            foreach (var c in ctx.root.GetComponentsInChildren<Canvas>(true))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.WorldSpace || c.GetComponent<GraphicRaycaster>() == null) continue;
                if (uiShape != null && c.GetComponent(uiShape) == null) c.gameObject.AddComponent(uiShape);
                if (Camera.main != null) c.worldCamera = Camera.main;
                n++;
            }
            if (uiShape == null) ctx.Log("（VRChat SDK が無いので VRC Ui Shape は付けていません。VRChat で使うときは操作パネルの Canvas に VRC Ui Shape を追加してください）");
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem（Unity確認用）", typeof(EventSystem), typeof(StandaloneInputModule));
                es.tag = "EditorOnly";
                es.transform.SetParent(ctx.root.transform, false);
            }
            ctx.Log("操作パネルを " + n + " 枚置きました（手前：ステージ下手の前・持ち運べる ／ 裏：背景の裏・プレビュー付き）" + (udon ? "：UdonSharp 版（全員に同期）" : "：Animator だけの版（押した人の画面だけ）"));
        }

        /// <summary>裏のパネルの場所（three の z）：背景の裏から 0.8m、ステージの台より 0.6m 後ろ。後ろの階段があればその先</summary>
        static float BackZ(SKContext ctx, Dictionary<string, object> st)
        {
            float backZ = SKDeskBuilder.BackdropBackZ(ctx, out _);
            float hd = J.F(st, "depth", 8f) / 2f;
            float z = Mathf.Min(backZ - 0.8f, -hd - 0.6f);
            var steps = J.O(st, "steps");
            string back = J.S(steps, "back", "none");
            if (!string.IsNullOrEmpty(back) && back != "none")
                z = Mathf.Min(z, -hd - J.F(steps, "count", 4f) * J.F(steps, "tread", 0.42f) - 1.0f);
            return z;
        }

        // ------------------------------------------------------------------ キャンバス

        static RectTransform BuildCanvas(SKContext ctx, Transform parent, string name, List<Page> pages, bool preview, bool udon,
            List<SKUdon.PanelItem> items, List<KeyValuePair<Btn, Button>> locals, SKUdon.PanelParts parts)
        {
            int width = W + (preview ? PREV : 0);
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<Image>().color = BG;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, H);
            rt.pivot = new Vector2(0.5f, 0f);
            go.transform.localScale = Vector3.one * PX;

            // ---- 見出し（タイトル・状態・ランプ・タブ）
            Box(go.transform, 0, 0, width, HEAD, HeadBG);
            Label(go.transform, "STAGE PANEL ― すてーじ工房" + (udon ? "（全員に同期）" : "（押した人の画面だけ）"), PAD, 8, 640, 34, 24, TitleCol);
            var status = Label(go.transform, udon ? "BPM " + Mathf.RoundToInt(ctx.bpm) : "※ UdonSharp 版にすると全員に同期します", 820, 6, W - 820 - PAD - (preview ? 0 : 190) - 40, 84, 16, TextCol, TextAnchor.UpperLeft);
            if (udon) parts.status.Add(status);
            if (udon) parts.lamps.Add(Lamp(go.transform, W - PAD - 30, 8, 26));
            var pageGos = new List<GameObject>();
            var tabMarks = new List<GameObject>();
            var tabBtns = new List<Button>();
            for (int i = 0; i < pages.Count; i++)
            {
                var tb = MakeButton(go.transform, pages[i].tab, PAD + i * 182, 58, 176, 54, CTab, 20);
                tabBtns.Add(tb);
                var m = new GameObject("Selected", typeof(RectTransform), typeof(Image));
                m.transform.SetParent(tb.transform, false);
                var mr = (RectTransform)m.transform;
                mr.anchorMin = new Vector2(0, 0); mr.anchorMax = new Vector2(1, 0); mr.pivot = new Vector2(0.5f, 0);
                mr.sizeDelta = new Vector2(0, 7); mr.anchoredPosition = Vector2.zero;
                m.GetComponent<Image>().color = MarkCol; m.GetComponent<Image>().raycastTarget = false;
                m.SetActive(i == 0);
                tabMarks.Add(m);
            }
            // 持ち運べるパネル：元の場所に戻す
            if (!preview && udon)
            {
                var home = MakeButton(go.transform, "元の場所に戻す", W - PAD - 180, 66, 180, 46, CWarn, 18);
                items.Add(new SKUdon.PanelItem { button = home, kind = 0, ch = SKCh.ACT_HOME, val = 0 });
            }

            // ---- ページ
            foreach (var p in pages)
            {
                var pg = new GameObject("Page_" + SKBuilder.Sanitize(p.tab), typeof(RectTransform));
                pg.transform.SetParent(go.transform, false);
                var pr = (RectTransform)pg.transform;
                pr.anchorMin = new Vector2(0, 1); pr.anchorMax = new Vector2(0, 1); pr.pivot = new Vector2(0, 1);
                pr.anchoredPosition = new Vector2(0, -(HEAD + 8)); pr.sizeDelta = new Vector2(W, H - HEAD - 8);
                foreach (var col in p.cols)
                {
                    float y = 0;
                    if (col.bg.a > 0) Box(pg.transform, col.x - 8, -4, col.w + 16, H - HEAD - 8 - PAD + 4, col.bg);
                    foreach (var s in col.secs)
                    {
                        if (!string.IsNullOrEmpty(s.title)) { Label(pg.transform, s.title, col.x, y, col.w, TITLE - 2, 18, s.titleCol); y += TITLE; }
                        if (s.info >= 0)
                        {
                            float hh = s.lines * (s.fontSize + 7) + 4;
                            Box(pg.transform, col.x, y, col.w, hh, new Color(0, 0, 0, 0.35f));
                            var t = Label(pg.transform, "（UdonSharp 版で表示）", col.x + 8, y + 2, col.w - 16, hh - 4, s.fontSize, TextCol, TextAnchor.UpperLeft);
                            (s.info == 0 ? parts.infoA : s.info == 1 ? parts.infoB : parts.infoC).Add(t);
                            y += hh + 4 + SECGAP;
                            continue;
                        }
                        if (s.btns.Count == 0) continue;
                        int per = PerRow(s, col.w);
                        float bw = (col.w - (per - 1) * GAP) / per;
                        for (int i = 0; i < s.btns.Count; i++)
                        {
                            var b = s.btns[i];
                            var btn = MakeButton(pg.transform, b.label, col.x + (i % per) * (bw + GAP), y + (i / per) * (BH + GAP), bw, BH, b.stripes != null ? J.Col(b.stripes[0], Color.white) : b.color, 0);
                            if (b.stripes != null) Stripes(btn, b.stripes);
                            GameObject mark = b.mark && udon ? Mark(btn) : null;
                            if (udon && b.ch >= 0) items.Add(new SKUdon.PanelItem { button = btn, kind = b.kind, ch = b.ch, val = b.val, mark = mark });
                            if (b.kind == 0) locals.Add(new KeyValuePair<Btn, Button>(b, btn));
                            if (udon && b.pickCam >= 0)
                            {
                                var tx = btn.GetComponentInChildren<Text>();
                                if (tx != null) { parts.pickTexts.Add(tx); parts.pickCam.Add(b.pickCam); parts.pickSlot.Add(b.pickSlot); }
                            }
                        }
                        y += ((s.btns.Count + per - 1) / per) * (BH + GAP) + SECGAP;
                    }
                }
                pageGos.Add(pg);
            }
            for (int i = 0; i < pageGos.Count; i++) pageGos[i].SetActive(i == 0);
            // タブ：押した人の画面だけページを切り替える（UI イベントの GameObject.SetActive）
            for (int i = 0; i < tabBtns.Count; i++)
                for (int k = 0; k < pageGos.Count; k++)
                {
                    UnityEventTools.AddBoolPersistentListener(tabBtns[i].onClick, new UnityAction<bool>(pageGos[k].SetActive), k == i);
                    UnityEventTools.AddBoolPersistentListener(tabBtns[i].onClick, new UnityAction<bool>(tabMarks[k].SetActive), k == i);
                }

            // ---- 確認用のプレビュー（裏だけ）
            if (preview) BuildPreviews(ctx, go.transform);
            return rt;
        }

        static void BuildPreviews(SKContext ctx, Transform canvas)
        {
            float x = W + 10, w = PREV - 10 - PAD, y = HEAD + 10;
            Box(canvas, W, HEAD, PREV, H - HEAD, new Color(0, 0, 0, 0.45f));
            Label(canvas, "確認用のプレビュー（ここだけ）", x, 14, w, 30, 20, TitleCol);
            var vj = ctx.vj;
            if (vj != null && vj.output != null)
            {
                y = Preview(canvas, "VJ の出力（いま LED に映る VJ）", vj.output, x, y, w, 16f / 9f);
                float hw = (w - 10) / 2f;
                float y2 = Preview(canvas, "デッキA", vj.deckA, x, y, hw, 16f / 9f);
                Preview(canvas, "デッキB", vj.deckB, x + hw + 10, y, hw, 16f / 9f);
                y = y2;
            }
            float cw = (w - 10) / 2f;
            float yy = y;
            for (int k = 0; k < 2; k++)
            {
                var rtx = ctx.camRT[k];
                if (rtx == null) continue;
                float asp = rtx.height > 0 ? (float)rtx.width / rtx.height : 16f / 9f;
                yy = Mathf.Max(yy, Preview(canvas, "カメラ" + (k + 1), rtx, x + k * (cw + 10), y, cw, asp));
            }
        }

        /// <summary>プレビュー 1 枚（RawImage）。下の y を返す</summary>
        static float Preview(Transform parent, string label, Texture tex, float x, float y, float w, float asp)
        {
            if (tex == null) return y;
            float h = Mathf.Min(w / Mathf.Max(0.25f, asp), 300f);
            float iw = h * asp;
            Label(parent, label, x, y, w, 24, 16, TextCol);
            var go = new GameObject("Preview_" + SKBuilder.Sanitize(label), typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x + (w - iw) / 2f, -(y + 26)); r.sizeDelta = new Vector2(iw, h);
            var ri = go.GetComponent<RawImage>(); ri.texture = tex; ri.raycastTarget = false;
            return y + 26 + h + 12;
        }

        // ------------------------------------------------------------------ 置き方

        /// <summary>手前：台の上に書見台のように置く。UdonSharp 版は取っ手を持って運べる（パネルは取っ手に付いていく）</summary>
        static void PlaceOnStand(SKContext ctx, Transform root, RectTransform canvas, bool udon, SKUdon.PanelParts parts)
        {
            float pw = W * PX;
            const float topY = 1.15f;   // パネルの下の辺の高さ
            // 台（動かない）
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stand.name = "Stand（台）";
            stand.transform.SetParent(root, false);
            stand.transform.localPosition = new Vector3(0, topY / 2f - 0.05f, -0.05f);
            stand.transform.localScale = new Vector3(0.12f, topY - 0.1f, 0.12f);
            var foot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            foot.name = "Foot（台の足）";
            foot.transform.SetParent(root, false);
            foot.transform.localPosition = new Vector3(0, 0.02f, -0.05f);
            foot.transform.localScale = new Vector3(0.6f, 0.04f, 0.45f);
            // 取っ手（パネルの下の辺。持つところ）
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "Handle（持つところ）";
            handle.transform.SetParent(root, false);
            handle.transform.localPosition = new Vector3(0, topY - 0.02f, 0);
            handle.transform.localRotation = Quaternion.Euler(TILT, 0, 0);
            handle.transform.localScale = new Vector3(pw * 0.5f, 0.05f, 0.07f);
            // パネル：取っ手の上の辺から奥へ倒す
            canvas.SetParent(root, false);
            canvas.localRotation = Quaternion.Euler(TILT, 0, 0);
            canvas.localPosition = new Vector3(0, topY, 0) + canvas.localRotation * new Vector3(0, 0.01f, 0);
            if (!udon) return;
            var pickupT = SKUdon.FindType("VRC.SDK3.Components.VRCPickup");
            var syncT = SKUdon.FindType("VRC.SDK3.Components.VRCObjectSync");
            if (pickupT == null || syncT == null) { ctx.Log("（VRChat SDK が無いので、操作パネルは持ち運べません）"); return; }
            var rb = handle.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            var pk = handle.AddComponent(pickupT);
            var so = new SerializedObject(pk);
            var ah = so.FindProperty("AutoHold"); if (ah != null) ah.enumValueIndex = 1;   // Yes（デスクトップでも持ったまま）
            var ut = so.FindProperty("UseText"); if (ut != null) ut.stringValue = "操作パネル";
            var it = so.FindProperty("InteractionText"); if (it != null) it.stringValue = "操作パネルを持つ";
            so.ApplyModifiedProperties();
            var sync = handle.AddComponent(syncT);
            var sso = new SerializedObject(sync);
            var ct = sso.FindProperty("AllowCollisionOwnershipTransfer"); if (ct != null) { ct.boolValue = false; sso.ApplyModifiedProperties(); }
            parts.handle = handle.transform; parts.canvas = canvas; parts.sync = sync;
            // パネルは取っ手の子にしない（パネルの当たり判定＝UI の当たり判定で持ち上がらないように）。U# のコントローラーが毎フレーム取っ手に合わせる
        }

        /// <summary>裏：床に固定。体が通り抜けないように当たり判定</summary>
        static void PlaceFixed(SKContext ctx, Transform root, RectTransform canvas, bool preview)
        {
            const float topY = 1.0f;
            float pw = (W + (preview ? PREV : 0)) * PX;
            canvas.SetParent(root, false);
            canvas.localRotation = Quaternion.Euler(TILT, 0, 0);
            canvas.localPosition = new Vector3(0, topY, 0);
            var desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            desk.name = "Desk（台）";
            desk.transform.SetParent(root, false);
            desk.transform.localPosition = new Vector3(0, topY / 2f, 0.25f);
            desk.transform.localScale = new Vector3(pw, topY - 0.02f, 0.5f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "BackstageFloor（ステージ裏の床）";
            floor.transform.SetParent(root, false);
            floor.transform.localPosition = new Vector3(0, -0.025f, -0.6f);
            floor.transform.localScale = new Vector3(pw + 1.6f, 0.05f, 2.8f);
            var mat = SKAssets.NewMaterial("Standard", ctx.dir + "/Materials/Panel_Desk.mat");
            mat.color = new Color(0.035f, 0.038f, 0.06f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.25f);
            EditorUtility.SetDirty(mat);
            desk.GetComponent<Renderer>().sharedMaterial = mat;
            floor.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ------------------------------------------------------------------ UI の部品

        static void WireLocal(Btn s, Button b)
        {
            if (s.ps != null)
            {
                foreach (var ps in s.ps) UnityEventTools.AddVoidPersistentListener(b.onClick, new UnityAction(ps.Play));
                if (s.audio != null) foreach (var a in s.audio) UnityEventTools.AddVoidPersistentListener(b.onClick, new UnityAction(a.Play));
            }
            else if (s.anim != null && !string.IsNullOrEmpty(s.trigger))
                UnityEventTools.AddStringPersistentListener(b.onClick, new UnityAction<string>(s.anim.SetTrigger), s.trigger);
        }

        static void Box(Transform parent, float x, float y, float w, float h, Color c)
        {
            var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
            var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
        }

        static void Stripes(Button b, string[] cols)
        {
            for (int k = 1; k < 3 && k < cols.Length; k++)
            {
                var s = new GameObject("Stripe", typeof(RectTransform), typeof(Image));
                s.transform.SetParent(b.transform, false);
                var r = (RectTransform)s.transform;
                r.anchorMin = new Vector2(k / 3f, 0); r.anchorMax = new Vector2((k + 1) / 3f, 1); r.offsetMin = r.offsetMax = Vector2.zero;
                var img = s.GetComponent<Image>(); img.color = J.Col(cols[k], Color.white); img.raycastTarget = false;
            }
        }

        static GameObject Mark(Button b)
        {
            var m = new GameObject("Mark", typeof(RectTransform), typeof(Image));
            m.transform.SetParent(b.transform, false);
            var r = (RectTransform)m.transform;
            r.anchorMin = new Vector2(0.06f, 0); r.anchorMax = new Vector2(0.94f, 0); r.pivot = new Vector2(0.5f, 0);
            r.anchoredPosition = new Vector2(0, 3); r.sizeDelta = new Vector2(0, 6);
            var img = m.GetComponent<Image>(); img.color = MarkCol; img.raycastTarget = false;
            m.SetActive(false);
            return m;
        }

        /// <summary>拍のランプ（点滅でキャンバス全体を描き直さないよう、自分用の Canvas を持たせる）</summary>
        static GameObject Lamp(Transform parent, float x, float y, float size)
        {
            var holder = new GameObject("BeatLamp", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            var hr = (RectTransform)holder.transform;
            hr.anchorMin = hr.anchorMax = new Vector2(0, 1); hr.pivot = new Vector2(0, 1);
            hr.anchoredPosition = new Vector2(x, -y); hr.sizeDelta = new Vector2(size, size);
            holder.AddComponent<Canvas>();
            var off = new GameObject("Off", typeof(RectTransform), typeof(Image));
            off.transform.SetParent(holder.transform, false);
            Fill((RectTransform)off.transform);
            off.GetComponent<Image>().color = new Color(0.2f, 0.25f, 0.35f, 1f); off.GetComponent<Image>().raycastTarget = false;
            var on = new GameObject("On", typeof(RectTransform), typeof(Image));
            on.transform.SetParent(holder.transform, false);
            Fill((RectTransform)on.transform);
            on.GetComponent<Image>().color = MarkCol; on.GetComponent<Image>().raycastTarget = false;
            on.SetActive(false);
            return on;
        }

        static void Fill(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        static Text Label(Transform parent, string text, float x, float y, float w, float h, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            var t = go.GetComponent<Text>();
            t.text = text; t.font = font; t.fontSize = size; t.color = c; t.alignment = align; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.lineSpacing = 1.05f;
            return t;
        }

        static Button MakeButton(Transform parent, string label, float x, float y, float w, float h, Color bgc, int size)
        {
            var go = new GameObject("Btn_" + SKBuilder.Sanitize(string.IsNullOrEmpty(label) ? "color" : label), typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            go.GetComponent<Image>().color = bgc;
            var btn = go.GetComponent<Button>();
            var cb = btn.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(0.8f, 1f, 0.95f); cb.pressedColor = new Color(0.5f, 0.9f, 0.8f); cb.selectedColor = Color.white;
            btn.colors = cb;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };   // VRChat 推奨：キー操作で勝手に選ばれないように
            if (!string.IsNullOrEmpty(label))
            {
                var tg = new GameObject("Text", typeof(RectTransform), typeof(Text));
                tg.transform.SetParent(go.transform, false);
                var trt = (RectTransform)tg.transform;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(3, 2); trt.offsetMax = new Vector2(-3, -2);
                var t = tg.GetComponent<Text>();
                t.text = label; t.font = font; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white; t.raycastTarget = false;
                if (size > 0) t.fontSize = size;
                else { t.resizeTextForBestFit = true; t.resizeTextMinSize = 11; t.resizeTextMaxSize = 19; t.fontSize = 19; }
                t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.lineSpacing = 0.9f;
            }
            return btn;
        }

        static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
            if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (Exception) { } }
            return f;
        }
    }
}
