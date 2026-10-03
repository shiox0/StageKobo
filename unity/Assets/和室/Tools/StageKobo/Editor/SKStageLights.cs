using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// ステージに置く照明アセットのうち、GLB では「書き出した瞬間の色」で止まってしまうものを動かす（シェーダー StageKobo/StageLight）。
    /// ・LEDバー（led_bar）：GLB の LEDSegments（色を焼き込んだメッシュ）を、分割番号つきの箱のメッシュに差し替える。
    ///   光り方（キュー・流れる・レベルメーター・常時点灯）はブラウザ版と同じ式。色・明るさ・マスターは Animator（ムービングライトと同じチャンネル）
    ///   音に反応（AudioLink）：レベルメーターは本物の音（配列で並べたものは、中から外へ 低い音 → 高い音）
    /// ・ストロボ／ブラインダー（strobe）：StrobeCell に小節の頭のフラッシュ・ストロボ（明るさ／照明ぜんぶ）。音があると強いキックで光る
    /// ・ネオン装飾（neon）：音に大きく反応。「動き」の 拍で脈打つ・点滅・虹色（Glow の _Anim）、回転・ふわふわ（Animator）
    /// ・文字・ロゴ（text_panel）：加算で光る文字にして、動き（脈打つ・点滅・虹色）と音への反応
    /// </summary>
    public static class SKStageLights
    {
        public const string Shader = "StageKobo/StageLight";

        public static void Build(SKContext ctx)
        {
            var bars = new List<SKFixture>();
            var strobes = new List<SKFixture>();
            var meshCache = new Dictionary<string, Mesh>();
            int neon = 0, text = 0, skipped = 0;
            foreach (var u in ctx.units)
            {
                if (u.type == "led_bar")
                {
                    var t = SKBuilder.FindDeep(u.t, "LEDSegments");
                    var r = t != null ? t.GetComponent<MeshRenderer>() : null;
                    var mf = t != null ? t.GetComponent<MeshFilter>() : null;
                    if (r == null || mf == null) { skipped++; continue; }
                    int seg = Mathf.Clamp(Mathf.RoundToInt(J.F(u.p, "seg", 14f)), 1, 40);
                    float len = Mathf.Max(0.05f, J.F(u.p, "length", 2.5f));
                    string key = seg + "_" + len.ToString("0.000", CultureInfo.InvariantCulture);
                    if (!meshCache.TryGetValue(key, out var mesh))
                    {
                        mesh = SKAssets.Save(BarMesh(seg, len), ctx.dir + "/Meshes/SK_LEDBar_" + seg + "_" + len.ToString("0.00", CultureInfo.InvariantCulture) + ".asset");
                        meshCache[key] = mesh;
                    }
                    mf.sharedMesh = mesh;
                    Quiet(r);
                    string mode = J.S(u.p, "mode", "follow");
                    bars.Add(new SKFixture { kind = "bar", u = u, beamR = r, follow = mode != "static", power = J.F(u.p, "power", 1f) });
                }
                else if (u.type == "strobe")
                {
                    var cells = u.t.GetComponentsInChildren<Renderer>(true).Where(x => SKBuilder.NameMatches(x.name, "StrobeCell")).ToList();
                    if (cells.Count == 0) { skipped++; continue; }
                    foreach (var c in cells) Quiet(c);
                    strobes.Add(new SKFixture { kind = "strobe", u = u, follow = false, power = J.F(u.p, "power", 1f), cellR = cells });
                }
                else if (u.type == "neon")
                {
                    if (Neon(ctx, u)) neon++;
                }
                else if (u.type == "text_panel")
                {
                    if (TextPanel(ctx, u)) text++;
                }
            }
            Number(bars);
            Number(strobes);
            // レベルメーターが拾う音の高さ：同じアセットを配列で並べたものは、並びの順（配列の 0 番 = 置いた位置から）に 低い音 → 高い音
            // ミラーの左右は同じ並びになる（中から外へ広がるイコライザー）。1台ずつ置いたものは左から右へ
            foreach (var f in bars)
            {
                var same = bars.Where(b => b.u.assetId == f.u.assetId && b.u.mirrored == f.u.mirrored).ToList();
                float freq;
                if (same.Count > 1)
                {
                    int lo = same.Min(b => b.u.idx), hi = same.Max(b => b.u.idx);
                    freq = hi > lo ? (float)(f.u.idx - lo) / (hi - lo) : 0f;
                }
                else freq = f.n > 1 ? (float)(f.xn * 0.5 + 0.5) : -1f;
                f.beamR.sharedMaterials = new[] { BarMaterial(ctx, f, freq) };
            }
            var strobeMats = new Dictionary<string, Material>();
            foreach (var f in strobes)
            {
                bool accent = J.B(f.u.p, "accent", true);
                string key = (accent ? "a" : "n") + "_" + f.power.ToString("0.00", CultureInfo.InvariantCulture);
                if (!strobeMats.TryGetValue(key, out var m))
                {
                    m = SKAssets.NewMaterial(Shader, ctx.dir + "/Materials/Strobe_" + key + ".mat");
                    m.SetFloat("_Kind", 1f);
                    m.SetColor("_Color", J.Col("#fff4e8", Color.white));
                    m.SetFloat("_Power", f.power);
                    m.SetFloat("_Accent", accent ? 1f : 0f);
                    m.SetFloat("_BPM", ctx.bpm);
                    EditorUtility.SetDirty(m);
                    strobeMats[key] = m;
                }
                foreach (var c in f.cellR) c.sharedMaterials = new[] { m };
            }
            ctx.fixtures.AddRange(bars);
            ctx.fixtures.AddRange(strobes);
            if (bars.Count + strobes.Count + neon + text > 0)
                ctx.Log("ステージの照明：LEDバー " + bars.Count + " 本・ストロボ " + strobes.Count + " 台・ネオン " + neon + " 個・文字 " + text + " 枚（ブラウザ版と同じ光り方で動き、音にも反応します）" +
                    (ctx.neonSpin.Count + ctx.neonFloat.Count > 0 ? "。ネオンの回転・ふわふわ " + (ctx.neonSpin.Count + ctx.neonFloat.Count) + " 個" + (ctx.cfg.anim ? "" : "（Animator に焼き込むときだけ動きます）") : ""));
            if (skipped > 0) ctx.Log("⚠ LEDバー・ストロボのうち " + skipped + " 台は GLB の中に光る部分（LEDSegments / StrobeCell）が見つからず、動かせませんでした");
        }

        /// <summary>種類ごとに左→右で番号（ブラウザ版の灯体と同じ。色の付け方・明るさの式に使う）</summary>
        static void Number(List<SKFixture> arr)
        {
            if (arr.Count == 0) return;
            var s = arr.OrderBy(x => x.u.threePos.x).ToList();
            float x0 = s[0].u.threePos.x, x1 = s[s.Count - 1].u.threePos.x, mid = (x0 + x1) / 2, half = Mathf.Max(0.01f, (x1 - x0) / 2);
            for (int i = 0; i < s.Count; i++) { s[i].i = i; s[i].n = s.Count; s[i].xn = s.Count > 1 ? Mathf.Clamp((s[i].u.threePos.x - mid) / half, -1, 1) : 0; }
        }

        static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static Material BarMaterial(SKContext ctx, SKFixture f, float freq)
        {
            var u = f.u;
            string mode = J.S(u.p, "mode", "follow");
            var m = SKAssets.NewMaterial(Shader, ctx.dir + "/Materials/LEDBar_" + SKBuilder.Sanitize(u.assetId) + "_" + (u.mirrored ? "R" : "L") + u.idx + ".mat");
            m.SetFloat("_Kind", 0f);
            m.SetFloat("_Mode", mode == "chase" ? 1f : mode == "vu" ? 2f : mode == "static" ? 3f : 0f);
            m.SetColor("_Color", J.Col(J.S(u.asset, "color", "#ffffff"), Color.white));
            m.SetFloat("_Power", f.power);
            m.SetFloat("_Seg", Mathf.Clamp(Mathf.RoundToInt(J.F(u.p, "seg", 14f)), 1, 40));
            m.SetFloat("_Idx", f.i);
            m.SetFloat("_Freq", freq);
            m.SetFloat("_BPM", ctx.bpm);
            SKBuilder.SetLiveColors(ctx, m);
            SKMath.ColorSlot(ctx.live.colorMode, 0, f.i, f.n, f.xn, out float slot, out float hue);
            m.SetFloat("_Slot", slot);
            m.SetFloat("_Hue", hue);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>LEDバーの形（ブラウザ版と同じ：分割1個 = 0.07 × 長さ/分割×0.88 × 0.07 の箱）。uv.x = 下から何番目か</summary>
        static Mesh BarMesh(int n, float len)
        {
            float sl = len / n, hw = 0.035f, hh = sl * 0.88f * 0.5f;
            var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int j = 0; j < n; j++)
            {
                var c = new Vector3(0, (j + 0.5f) * sl, 0);
                foreach (var d in dirs)
                {
                    // 面の向き d と、面の中の2方向 a（画面の上）・b（画面の右。外から d の逆向きに見たとき）
                    Vector3 a = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                    Vector3 b = Vector3.Cross(d, a);
                    var size = new Vector3(hw, hh, hw);
                    Vector3 S(Vector3 x) => Vector3.Scale(x, size);
                    int k = v.Count;
                    v.Add(c + S(d - a - b)); v.Add(c + S(d - a + b)); v.Add(c + S(d + a + b)); v.Add(c + S(d + a - b));
                    for (int q = 0; q < 4; q++) { nr.Add(d); uv.Add(new Vector2(j, 0)); }
                    tri.Add(k); tri.Add(k + 2); tri.Add(k + 1); tri.Add(k); tri.Add(k + 3); tri.Add(k + 2);   // Unity は外から見て時計回りが表
                }
            }
            var m = new Mesh { name = "SK_LEDBar_" + n };
            m.SetVertices(v); m.SetNormals(nr); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// ネオン装飾：そのネオンだけのマテリアルにして、音に大きく反応させる（_ALMode 2）。「動き」の 脈打つ・点滅・虹色 は _Anim、
        /// 回転・ふわふわは Animator（SKAnimBaker の NeonSpin / NeonFloat レイヤー）で動かす
        /// </summary>
        static bool Neon(SKContext ctx, SKUnit u)
        {
            string anim = J.S(u.p, "anim", "none");
            float a = anim == "pulse" ? 1f : anim == "blink" ? 2f : anim == "rainbow" ? 3f : 0f;
            var done = new Dictionary<Material, Material>();
            foreach (var r in u.t.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int k = 0; k < mats.Length; k++)
                {
                    var src = mats[k];
                    if (src == null || src.shader == null || src.shader.name != SKAssets.ShaderGlow || src.IsKeywordEnabled("_PALETTE_ON")) continue;
                    if (!done.TryGetValue(src, out var nm))
                    {
                        nm = new Material(src) { name = src.name + "_" + anim };
                        nm = SKAssets.Save(nm, ctx.dir + "/Materials/" + src.name + "_Neon_" + SKBuilder.Sanitize(u.assetId) + "_" + (u.mirrored ? "R" : "L") + u.idx + ".mat");
                        nm.SetFloat("_Anim", a);
                        nm.SetFloat("_AnimX", J.V3(J.Get(u.asset, "pos")).x);   // ブラウザ版と同じく、アセットを置いた位置（配列・ミラーでも同じ色）
                        nm.SetFloat("_BPM", ctx.bpm);
                        nm.SetFloat("_ALMode", 2f);   // 音に大きく反応
                        EditorUtility.SetDirty(nm);
                        done[src] = nm;
                    }
                    mats[k] = nm;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            // 回転・ふわふわ：ネオンの形（Neon_<形>）を Animator で動かす
            if (anim == "spin" || anim == "float")
            {
                var n = SKBuilder.FindDeep(u.t, "Neon_" + J.S(u.p, "shape", "triangle"));
                if (n == null) ctx.Log("⚠ " + u.name + " のネオンの形が GLB に見つからないので、" + (anim == "spin" ? "回転" : "ふわふわ") + "は付けませんでした");
                else if (anim == "spin") ctx.neonSpin.Add(n);
                else ctx.neonFloat.Add(new KeyValuePair<Transform, float>(n, J.V3(J.Get(u.asset, "pos")).x));
            }
            return done.Count > 0;
        }

        /// <summary>
        /// 文字・ロゴ（text_panel）：GLB では半透明のテクスチャ（加算が無くなる・動かない）→ ブラウザ版と同じ「加算で光る文字」にして、
        /// 動き（脈打つ・点滅・虹色）と音への反応を付ける（Glow の _MAINTEX_ON・_Anim 4〜6・_ALMode 2）
        /// </summary>
        static bool TextPanel(SKContext ctx, SKUnit u)
        {
            var t = SKBuilder.FindDeep(u.t, "TextPanel");
            var r = t != null ? t.GetComponent<Renderer>() : null;
            if (r == null || r.sharedMaterial == null) return false;
            var tex = FindTexture(r.sharedMaterial);
            if (tex == null) { ctx.Log("⚠ " + u.name + " の文字のテクスチャが見つからないので、そのままにしました"); return false; }
            string anim = J.S(u.p, "anim", "pulse");
            var m = SKAssets.NewMaterial(SKAssets.ShaderGlow, ctx.dir + "/Materials/Text_" + SKBuilder.Sanitize(u.assetId) + "_" + (u.mirrored ? "R" : "L") + u.idx + ".mat");
            m.SetColor("_Color", J.Col(J.S(u.asset, "color", "#ff8fd0"), Color.white));
            m.SetFloat("_Emission", J.F(u.p, "glow", 2f));
            m.SetFloat("_UseMainTex", 1f); m.EnableKeyword("_MAINTEX_ON");
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Anim", anim == "pulse" ? 4f : anim == "blink" ? 5f : anim == "rainbow" ? 6f : 0f);
            m.SetFloat("_BPM", ctx.bpm);
            m.SetFloat("_ALMode", 2f);
            m.SetFloat("_Cull", 0f);
            SKBuilder.SetBlend(m, 2);   // 加算（ブラウザ版と同じ）
            EditorUtility.SetDirty(m);
            r.sharedMaterials = new[] { m };
            Quiet(r);
            return true;
        }

        /// <summary>GLB を取り込んだマテリアルの色テクスチャ（glTFast：baseColorTexture、UniGLTF：_MainTex）</summary>
        static Texture FindTexture(Material m)
        {
            Texture best = null;
            foreach (var name in m.GetTexturePropertyNames())
            {
                var tx = m.GetTexture(name);
                if (tx == null) continue;
                string n = name.ToLowerInvariant();
                if (n.Contains("basecolor") || n.Contains("maintex") || n.Contains("albedo")) return tx;
                if (best == null) best = tx;
            }
            return best;
        }
    }
}
