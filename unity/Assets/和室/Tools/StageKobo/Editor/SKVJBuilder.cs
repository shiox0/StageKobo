using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// 同期する値の並び。Runtime/StageKoboVJ.cs の I_～ / F_～ / FX_～ と同じ番号にすること
    /// （StageKoboVJ は UdonSharp があるときだけコンパイルされるので、エディタ側に同じ表を持つ）
    /// </summary>
    public static class SKVJIdx
    {
        public const int I_GEN = 0, I_CNT = 2, I_IMG = 4, I_PAL = 6, I_CMODE = 7, I_FX = 8, I_MIRROR = 9, I_KALEN = 10, I_XFAUTO = 11, I_IMGRATE = 12, I_AUTO = 13, I_AUTOBARS = 14,
            I_TEXTON = 15, I_TEXTANIM = 16, I_BACK = 17, I_MAP = 18, I_BLACK = 19, I_EDIT = 20, I_TAKE = 21, I_FADELEN = 22, NI = 23;
        public const int F_SPD = 0, F_ZOOM = 2, F_XF = 4, F_FFROM = 5, F_FTO = 6, F_FB0 = 7, F_FLEN = 8, F_HUE = 9, F_BRIGHT = 10, F_RATE = 11, F_AMT = 12, F_PUMP = 13, F_IMGAT = 14, NF = 16;
        public const int FX_ZOOM = 0, FX_FLASH = 1, FX_RGB = 2, FX_GLITCH = 3, FX_KALEIDO = 4, FX_SHAKE = 5, FX_ROTATE = 6, FX_FEEDBACK = 7, FX_PIXEL = 8, FX_INVERT = 9, FX_POSTER = 10, FX_EDGE = 11, FX_SCAN = 12;
        public const int GEN_IMAGE = 23, GEN_TEXT = 24, GEN_BLACK = 25, NGEN = 26;   // 26 合成は v0.8 で外した
        // ボタンの動作（StageKoboVJ の V_～）
        public const int V_PAD = 100, V_BLACK = 101, V_EDIT = 102, V_TAKE = 103, V_CUT = 104, V_FADELEN = 105, V_XFAUTO = 106, V_GEN = 107, V_SPEED = 108, V_COUNT = 109, V_ZOOM = 110,
            V_IMG = 111, V_IMGRATE = 112, V_PAL = 113, V_CMODE = 114, V_HUE = 115, V_FX = 116, V_MIRROR = 117, V_KALE = 118, V_RATE = 119, V_PUMP = 120, V_AMT = 121, V_BRIGHT = 122,
            V_TEXTON = 123, V_TEXTANIM = 124, V_AUTO = 125, V_AUTOBARS = 126, V_PRESET = 127, V_SLOT = 128, V_BACK = 129, V_MAP = 130;
    }

    /// <summary>LED の面1枚（VJ 映像を映せるもの）</summary>
    public class SKLed
    {
        public Renderer r;
        public Material m;
        public int dx, dy;
        public bool backdrop, camMonitor, vjAlways, sub;   // sub = サブのモニター（リモコンの「サブ」で切り替え）
    }

    /// <summary>組み立てた VJ（卓の組み立てで使う）</summary>
    public class SKVJInfo
    {
        public CustomRenderTexture deckA, deckB, output;
        public Material matA, matB, matFx;
        public List<Texture2D> images = new List<Texture2D>();
        public Texture2D text;
        public float textAsp = 4f;
        public int[] vi;
        public float[] vf;
        public List<string> presetNames = new List<string>(), slotNames = new List<string>();
        public List<int> presetI = new List<int>(), slotI = new List<int>();
        public List<float> presetF = new List<float>(), slotF = new List<float>();
        public string[] gensShort = new string[0], fxNames = new string[0];
        public Vector4[] vjPal, lightPal;
        public Component ctrl;   // StageKoboVJ（UdonSharp 版のときだけ）
    }

    /// <summary>
    /// ブラウザ版の VJ を Unity に組み立てる：
    ///  デッキA・デッキB・出力 の 3 枚の Custom Render Texture（StageKobo/VJDeck・VJFx）、画像バンク・文字のテクスチャ、
    ///  LED モニター・背景 LED への貼り付け（切り抜き・ミップの段）、UdonSharp 版の VJ コントローラー（StageKoboVJ）
    /// </summary>
    public static class SKVJBuilder
    {
        public const int W = 640, H = 360;

        public static void Build(SKContext ctx)
        {
            var defs = J.O(ctx.data, "vjDefs");
            var vj = J.O(ctx.state, "vj");
            if (defs == null || vj == null)
            {
                ctx.Log("⚠ この JSON には VJ の情報がありません（すてーじ工房 v0.7 以降で書き出し直すと、VJ と VJ 卓が作られます）");
                return;
            }
            var info = new SKVJInfo();
            ctx.vj = info;
            info.gensShort = J.L(defs, "gensShort").Select(x => x as string ?? "").ToArray();
            info.fxNames = J.L(defs, "fxNames").Select(x => x as string ?? "").ToArray();

            // ---- 画像バンク・文字
            var images = J.O(ctx.data, "images");
            int ii = 0;
            foreach (var idObj in J.L(defs, "images"))
            {
                string id = idObj as string;
                var tex = SKAssets.SaveDataUrlTexture(J.S(images, id), ctx.dir + "/Textures/VJ_Image" + (ii++).ToString("00"));
                if (tex != null) info.images.Add(tex);
            }
            info.text = SKAssets.SaveDataUrlTexture(J.S(images, "vjtext"), ctx.dir + "/Textures/VJ_Text");
            info.textAsp = J.F(defs, "textAsp", 4f);

            // ---- 色（リニア）
            var pals = J.L(ctx.data, "palettes");
            info.vjPal = new Vector4[pals.Count * 3];
            for (int i = 0; i < pals.Count; i++)
            {
                var cs = J.L(pals[i], "c");
                for (int k = 0; k < 3; k++) info.vjPal[i * 3 + k] = Lin(J.Col(k < cs.Count ? cs[k] as string : "#ffffff", Color.white));
            }
            // ---- いまの VJ の状態 → 同期する値
            var conv = new Conv(defs);
            conv.Snapshot(vj, out info.vi, out info.vf);
            // 組み込みシーン（ブラウザ版 vjApplyPreset と同じ：いまの状態に上書き）
            foreach (var p in J.L(defs, "presets"))
            {
                if (J.S(J.O(p, "a"), "gen") == "mix" || J.S(J.O(p, "b"), "gen") == "mix") continue;   // v0.7 の書き出しの「ゆめかわ合成」（合成は外した）
                var o = PresetState(vj, p);
                conv.Snapshot(o, out var pi, out var pf);
                info.presetNames.Add(J.S(p, "name"));
                info.presetI.AddRange(pi); info.presetF.AddRange(pf);
            }
            // ブラウザで登録したシーン
            int si = 0;
            foreach (var s in J.L(vj, "slots"))
            {
                si++;
                if (!(s is Dictionary<string, object> sd)) continue;
                var o = new Dictionary<string, object>(vj);
                foreach (var kv in sd) o[kv.Key] = kv.Value;
                conv.Snapshot(o, out var pi, out var pf);
                info.slotNames.Add("登録" + si);
                info.slotI.AddRange(pi); info.slotF.AddRange(pf);
            }
            // ---- Custom Render Texture とマテリアル
            info.matA = SKAssets.NewMaterial(SKAssets.ShaderVJDeck, ctx.dir + "/Materials/VJ_DeckA.mat");
            info.matB = SKAssets.NewMaterial(SKAssets.ShaderVJDeck, ctx.dir + "/Materials/VJ_DeckB.mat");
            info.matFx = SKAssets.NewMaterial(SKAssets.ShaderVJFx, ctx.dir + "/Materials/VJ_Output.mat");
            info.deckA = Crt(ctx, "VJ_DeckA", info.matA, false, false);
            info.deckB = Crt(ctx, "VJ_DeckB", info.matB, false, false);
            info.output = Crt(ctx, "VJ_Output", info.matFx, true, true);
            info.matFx.SetTexture("_A", info.deckA);
            info.matFx.SetTexture("_B", info.deckB);
            InitMaterials(ctx, info);
            EditorUtility.SetDirty(info.matA); EditorUtility.SetDirty(info.matB); EditorUtility.SetDirty(info.matFx);

            AttachLeds(ctx, info);
            ctx.Log("VJ：デッキA・B と出力（" + W + "×" + H + " の Custom Render Texture）、画像 " + info.images.Count + " 枚" + (info.text != null ? "・文字" : "") +
                    "／組み込みシーン " + info.presetNames.Count + "・登録シーン " + info.slotNames.Count);
        }

        /// <summary>
        /// UdonSharp 版：VJ のコントローラー（StageKoboVJ）を作る（照明の焼き込み・リモコンのあと。照明の「色」チャンネルの並びを使うため）。
        /// 作れなかったら、書き出したときの VJ のまま動く版にする
        /// </summary>
        public static void Finish(SKContext ctx)
        {
            var info = ctx.vj;
            if (info == null) return;
            info.lightPal = LightPal(ctx);
            if (ctx.udon)
            {
                var ctrl = SKUdon.EnsureController(ctx);
                if (ctrl != null) info.ctrl = SKUdon.AddVJ(ctx, info, ctrl);
                if (info.ctrl != null) { ctx.Log("VJ：UdonSharp 版のコントローラー（全員に同期）を作りました"); return; }
            }
            Fallback(ctx);
        }

        /// <summary>UdonSharp の VJ が無いとき：背景の LED は書き出したときの設定のまま（映す／映さない）、デッキはシェーダーの中で拍を数える</summary>
        static void Fallback(SKContext ctx)
        {
            bool backOn = J.B(J.O(ctx.state, "vj"), "backdrop");
            foreach (var l in ctx.leds)
                if (l.backdrop) { l.m.SetFloat("_VJBackdrop", 0f); l.m.SetFloat("_VJAlways", backOn ? 1f : 0f); EditorUtility.SetDirty(l.m); }
            if (ctx.udon) ctx.Log("⚠ VJ の UdonSharp を作れなかったので、VJ は書き出したときの設定のまま動きます");
        }

        /// <summary>照明の「色（パレット）」チャンネルのステートの並び × 3 色（リニア）。VJ の色「照明」で使う</summary>
        static Vector4[] LightPal(SKContext ctx)
        {
            var pals = J.L(ctx.data, "palettes");
            var palCh = ctx.channels[SKCh.PALETTE];
            var lp = new List<Vector4>();
            if (palCh != null)
                foreach (var n in palCh.names)
                {
                    string[] c = null;
                    if (n.StartsWith("Pal_") && int.TryParse(n.Substring(4), out int pi) && pi < pals.Count)
                    {
                        var cs = J.L(pals[pi], "c");
                        if (cs.Count >= 3) c = new[] { cs[0] as string, cs[1] as string, cs[2] as string };
                    }
                    if (c == null) c = new[] { ctx.live.c1, ctx.live.c2, ctx.live.c3 };   // Pal_Live（シーンの色）
                    foreach (var h in c) lp.Add(Lin(J.Col(h, Color.white)));
                }
            if (lp.Count == 0) foreach (var h in new[] { ctx.live.c1, ctx.live.c2, ctx.live.c3 }) lp.Add(Lin(J.Col(h, Color.white)));
            return lp.ToArray();
        }

        static Vector4 Lin(Color c) { var l = c.linear; return new Vector4(l.r, l.g, l.b, 1f); }

        static CustomRenderTexture Crt(SKContext ctx, string name, Material mat, bool doubleBuffered, bool mips)
        {
            var crt = new CustomRenderTexture(W, H, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = name,
                material = mat,
                updateMode = CustomRenderTextureUpdateMode.Realtime,
                initializationMode = CustomRenderTextureUpdateMode.OnLoad,
                initializationSource = CustomRenderTextureInitializationSource.TextureAndColor,
                initializationColor = Color.black,
                doubleBuffered = doubleBuffered,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = mips,
                autoGenerateMips = mips,
            };
            return SKAssets.Save(crt, ctx.dir + "/Textures/" + name + ".asset");
        }

        /// <summary>マテリアルの初期値（UdonSharp が無いときも、書き出したときの VJ のまま動くように）</summary>
        static void InitMaterials(SKContext ctx, SKVJInfo info)
        {
            int[] vi = info.vi; float[] vf = info.vf;
            float xf = vf[SKVJIdx.F_XF];
            int pal = vi[SKVJIdx.I_PAL];
            Vector4 c1, c2, c3;
            if (pal >= 0 && pal * 3 + 2 < info.vjPal.Length) { c1 = info.vjPal[pal * 3]; c2 = info.vjPal[pal * 3 + 1]; c3 = info.vjPal[pal * 3 + 2]; }
            else { c1 = Lin(J.Col(ctx.live.c1, Color.white)); c2 = Lin(J.Col(ctx.live.c2, Color.white)); c3 = Lin(J.Col(ctx.live.c3, Color.white)); }
            for (int k = 0; k < 2; k++)
            {
                var m = k == 0 ? info.matA : info.matB;
                m.SetFloat("_Driven", 0f);
                m.SetFloat("_BPM", ctx.bpm);
                m.SetFloat("_Active", (k == 0 ? xf < 0.999f : xf > 0.001f) ? 1f : 0f);
                m.SetFloat("_Gen", vi[SKVJIdx.I_GEN + k]);
                m.SetFloat("_Cnt", vi[SKVJIdx.I_CNT + k]);
                m.SetFloat("_Zoom", vf[SKVJIdx.F_ZOOM + k]);
                m.SetFloat("_Speed", vf[SKVJIdx.F_SPD + k]);
                m.SetFloat("_Asp", (float)W / H);
                m.SetVector("_C1", c1); m.SetVector("_C2", c2); m.SetVector("_C3", c3);
                if (info.images.Count > 0)
                {
                    var tex = info.images[((vi[SKVJIdx.I_IMG + k] % info.images.Count) + info.images.Count) % info.images.Count];
                    m.SetTexture("_Img", tex); m.SetFloat("_ImgOk", 1f); m.SetFloat("_ImgAsp", (float)tex.width / Mathf.Max(1, tex.height));
                }
                if (info.text != null) { m.SetTexture("_Txt", info.text); m.SetFloat("_TxtAsp", info.textAsp); }
            }
            var fx = info.matFx;
            int fxb = vi[SKVJIdx.I_FX];
            Func<int, bool> bit = b => ((fxb >> b) & 1) == 1;
            fx.SetFloat("_Xf", xf);
            fx.SetFloat("_Asp", (float)W / H); fx.SetFloat("_ResX", W); fx.SetFloat("_ResY", H);
            fx.SetFloat("_Mirror", vi[SKVJIdx.I_MIRROR]);
            fx.SetFloat("_Kale", bit(SKVJIdx.FX_KALEIDO) ? Mathf.Max(2, vi[SKVJIdx.I_KALEN]) : 0f);
            fx.SetFloat("_Fb", bit(SKVJIdx.FX_FEEDBACK) ? 1f : 0f);
            fx.SetFloat("_Edge", bit(SKVJIdx.FX_EDGE) ? 1f : 0f);
            fx.SetFloat("_ColMode", vi[SKVJIdx.I_CMODE]);
            fx.SetFloat("_Post", bit(SKVJIdx.FX_POSTER) ? 1f : 0f);
            fx.SetFloat("_Scan", bit(SKVJIdx.FX_SCAN) ? 1f : 0f);
            fx.SetFloat("_Bright", vf[SKVJIdx.F_BRIGHT]);
            fx.SetFloat("_TxtOn", vi[SKVJIdx.I_TEXTON] == 1 && info.text != null ? 1f : 0f);
            fx.SetFloat("_TxtAsp", info.textAsp);
            if (info.text != null) fx.SetTexture("_Txt", info.text);
            fx.SetVector("_C1", c1); fx.SetVector("_C2", c2); fx.SetVector("_C3", c3);
        }

        /// <summary>LED モニター・背景 LED に VJ 映像を貼る（切り抜き・ドット1個ぶんのミップの段は、ブラウザ版 vjApplyTargets と同じ計算）</summary>
        static void AttachLeds(SKContext ctx, SKVJInfo info)
        {
            var targets = ctx.leds.Where(l => l.backdrop || l.camMonitor || l.vjAlways).ToList();
            float texAsp = (float)W / H;
            // 「つなげて1枚」：VJ を映せる画面ぜんぶの外枠（正面から見た three の X・Y）
            float ux0 = 1e9f, ux1 = -1e9f, uy0 = 1e9f, uy1 = -1e9f;
            var box = new Dictionary<SKLed, Vector4>();
            foreach (var l in targets)
            {
                var b = l.r.bounds;
                float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
                for (int c = 0; c < 8; c++)
                {
                    var wp = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                    var t = ctx.Conv(ctx.model.InverseTransformPoint(wp));   // Conv は自分自身の逆
                    x0 = Mathf.Min(x0, t.x); x1 = Mathf.Max(x1, t.x); y0 = Mathf.Min(y0, t.y); y1 = Mathf.Max(y1, t.y);
                }
                box[l] = new Vector4(x0, x1, y0, y1);
                ux0 = Mathf.Min(ux0, x0); ux1 = Mathf.Max(ux1, x1); uy0 = Mathf.Min(uy0, y0); uy1 = Mathf.Max(uy1, y1);
            }
            bool udon = ctx.udon;
            bool backOn = J.B(J.O(ctx.state, "vj"), "backdrop");
            int nb = 0, nm = 0, na = 0;
            foreach (var l in ctx.leds)
            {
                var m = l.m;
                m.SetTexture("_VJTex", info.output);
                float pa = (float)l.dx / Mathf.Max(1, l.dy);
                var R = Cover(pa, texAsp);
                m.SetVector("_VJRect", R);
                m.SetFloat("_VJLod", Lod(R.z, l.dx));
                if (box.TryGetValue(l, out var bb) && ux1 > ux0 && uy1 > uy0)
                {
                    float uw = Mathf.Max(0.01f, ux1 - ux0), uh = Mathf.Max(0.01f, uy1 - uy0);
                    var crop = Cover(uw / uh, texAsp);
                    float ox = (bb.x - ux0) / uw, oy = (bb.z - uy0) / uh, sx = (bb.y - bb.x) / uw, sy = (bb.w - bb.z) / uh;
                    var RS = new Vector4(crop.x + ox * crop.z, crop.y + oy * crop.w, sx * crop.z, sy * crop.w);
                    m.SetVector("_VJRectSpan", RS);
                    m.SetFloat("_VJLodSpan", Lod(RS.z, l.dx));
                }
                else { m.SetVector("_VJRectSpan", R); m.SetFloat("_VJLodSpan", Lod(R.z, l.dx)); }
                if (l.backdrop)
                {
                    nb++;
                    // UdonSharp 版：VJ 卓の「背景LED」で切り替える。Animator だけの版：書き出したときのまま
                    if (udon) m.SetFloat("_VJBackdrop", 1f);
                    else if (backOn) m.SetFloat("_VJAlways", 1f);
                }
                if (l.camMonitor) { nm++; m.SetFloat("_Src", 0f); m.SetFloat("_VJ", SKBuilder.MonitorMode(ctx, l.sub) == "vj" ? 1f : 0f); }
                if (l.vjAlways) { na++; m.SetFloat("_VJAlways", 1f); }
                EditorUtility.SetDirty(m);
            }
            ctx.vjOutput = info.output;
            ctx.Log("VJ 映像を貼りました：背景の LED " + nb + " 枚" + (udon ? "（VJ卓の「背景LED」で切り替え）" : backOn ? "（いつも VJ）" : "（書き出したときに「背景LED：映さない」だったので VJ は映しません）") +
                    "・カメラのモニター " + nm + " 台（メイン・サブそれぞれ「VJ映像」で切り替え）・VJ のモニター " + na + " 台");
        }

        static Vector4 Cover(float panelAsp, float texAsp)
        {
            if (panelAsp > texAsp) { float s = texAsp / panelAsp; return new Vector4(0, (1 - s) / 2, 1, s); }
            float s2 = panelAsp / texAsp; return new Vector4((1 - s2) / 2, 0, s2, 1);
        }

        static float Lod(float rz, int dots) { return Mathf.Max(0f, Mathf.Log(Mathf.Max(1f, W * rz / Mathf.Max(1, dots)), 2f) - 0.5f); }

        /// <summary>ブラウザ版 vjApplyPreset と同じ：いまの状態に、組み込みシーンの値を上書きしたもの</summary>
        static Dictionary<string, object> PresetState(Dictionary<string, object> vj, object p)
        {
            var o = new Dictionary<string, object>(vj);
            foreach (var k in new[] { "a", "b" })
            {
                var d = new Dictionary<string, object>(J.O(vj, k) ?? new Dictionary<string, object>());
                d["speed"] = 1.0; d["count"] = 6.0; d["zoom"] = 1.0; d["img"] = k == "a" ? 0.0 : 1.0;
                var pd = J.O(p, k);
                if (pd != null) foreach (var kv in pd) d[kv.Key] = kv.Value;
                o[k] = d;
            }
            var fx = new Dictionary<string, object>();
            var pf = J.O(p, "fx");
            if (pf != null) foreach (var kv in pf) fx[kv.Key] = kv.Value;
            o["fx"] = fx;
            o["pal"] = J.Get(p, "pal") ?? -1.0;
            o["colorMode"] = J.S(p, "colorMode", "raw");
            o["mirror"] = J.Get(p, "mirror") ?? 0.0;
            o["kaleN"] = J.Get(p, "kaleN") ?? 6.0;
            o["hue"] = J.Get(p, "hue") ?? 0.0;
            if (J.Get(p, "imgRate") != null) o["imgRate"] = J.Get(p, "imgRate");
            o["xf"] = 0.0; o["xfAuto"] = 0.0;
            return o;
        }

        /// <summary>ブラウザの VJ の状態（名前）→ 同期する値（番号）</summary>
        class Conv
        {
            readonly List<string> gens, fx, cmodes;
            public Conv(Dictionary<string, object> defs)
            {
                Func<object, List<string>> ls = x => (x as List<object> ?? new List<object>()).Select(v => v is Dictionary<string, object> d ? J.S(d, "id") : v as string ?? "").ToList();
                gens = ls(J.Get(defs, "gens")); fx = ls(J.Get(defs, "fx")); cmodes = ls(J.Get(defs, "colorModes"));
            }
            static int Idx(List<string> l, string s, int def) { int i = l.IndexOf(s); return i >= 0 ? i : def; }

            public void Snapshot(Dictionary<string, object> v, out int[] vi, out float[] vf)
            {
                vi = new int[SKVJIdx.NI]; vf = new float[SKVJIdx.NF];
                for (int k = 0; k < 2; k++)
                {
                    var d = J.O(v, k == 0 ? "a" : "b") ?? new Dictionary<string, object>();
                    int g = Idx(gens, J.S(d, "gen", "tunnel"), 0);
                    vi[SKVJIdx.I_GEN + k] = g < SKVJIdx.NGEN ? g : 0;   // v0.7 の書き出しの「合成」はトンネルに
                    vi[SKVJIdx.I_CNT + k] = Mathf.Clamp(J.I(d, "count", 6), 1, 12);
                    vi[SKVJIdx.I_IMG + k] = Mathf.Max(0, J.I(d, "img", k));
                    vf[SKVJIdx.F_SPD + k] = J.F(d, "speed", 1f);
                    vf[SKVJIdx.F_ZOOM + k] = J.F(d, "zoom", 1f);
                    vf[SKVJIdx.F_IMGAT + k] = 0f;
                }
                vi[SKVJIdx.I_PAL] = Mathf.Clamp(J.I(v, "pal", -1), -1, 7);
                vi[SKVJIdx.I_CMODE] = Idx(cmodes, J.S(v, "colorMode", "raw"), 0);
                var f = J.O(v, "fx");
                int bits = 0;
                if (f != null) for (int b = 0; b < fx.Count; b++) if (J.B(f, fx[b])) bits |= 1 << b;
                vi[SKVJIdx.I_FX] = bits;
                vi[SKVJIdx.I_MIRROR] = Mathf.Clamp(J.I(v, "mirror", 0), 0, 3);
                vi[SKVJIdx.I_KALEN] = Mathf.Clamp(J.I(v, "kaleN", 6), 2, 12);
                vi[SKVJIdx.I_XFAUTO] = J.I(v, "xfAuto", 0);
                vi[SKVJIdx.I_IMGRATE] = J.I(v, "imgRate", 0);
                vi[SKVJIdx.I_AUTO] = J.B(v, "auto") ? 1 : 0;
                vi[SKVJIdx.I_AUTOBARS] = Mathf.Max(1, J.I(v, "autoBars", 4));
                vi[SKVJIdx.I_TEXTON] = J.B(v, "textOn") ? 1 : 0;
                string ta = J.S(v, "textAnim", "pulse");
                vi[SKVJIdx.I_TEXTANIM] = ta == "blink" ? 1 : ta == "stay" ? 2 : 0;
                vi[SKVJIdx.I_BACK] = J.B(v, "backdrop") ? 1 : 0;
                vi[SKVJIdx.I_MAP] = J.S(v, "map") == "span" ? 1 : 0;
                vi[SKVJIdx.I_BLACK] = 0;
                vi[SKVJIdx.I_EDIT] = 0;
                vi[SKVJIdx.I_TAKE] = 1;
                vi[SKVJIdx.I_FADELEN] = 2;
                vf[SKVJIdx.F_XF] = Mathf.Clamp01(Mathf.Round(J.F(v, "xf", 0f)));
                vf[SKVJIdx.F_HUE] = J.F(v, "hue", 0f);
                vf[SKVJIdx.F_BRIGHT] = J.F(v, "bright", 1f);
                vf[SKVJIdx.F_RATE] = J.F(v, "rate", 1f);
                vf[SKVJIdx.F_AMT] = J.F(v, "amt", 1f);
                vf[SKVJIdx.F_PUMP] = J.F(v, "pump", 0.5f);
            }
        }
    }
}
