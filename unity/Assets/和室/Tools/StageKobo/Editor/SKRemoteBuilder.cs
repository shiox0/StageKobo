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
    /// ワールドに置く「リモコン」（World Space Canvas）。2通りの作り方がある：
    ///  ・UdonSharp 版（おすすめ）：押した内容が全員に同期する。テンポ（BPM・TAP）と動きの速さ（×½/×2）も変えられる
    ///  ・Animator だけの版：ボタンが Animator.SetTrigger / ParticleSystem.Play を直接呼ぶ。押した人の画面だけ切り替わる
    /// </summary>
    public static class SKRemoteBuilder
    {
        const float PX = 0.0012f;   // 1px = 1.2mm
        const int W = 1000, PAD = 22, CELL_W = 110, CELL_H = 46, GAP = 8, COLS = 8;
        static Font font;

        /// <summary>ステートの名前 → ボタンの文字（ステージ裏の照明卓でも使う。卓の文字は Textures/SK_Labels.png に焼いてあるので、増やしたら生成し直す）</summary>
        public static readonly Dictionary<string, string> JP = new Dictionary<string, string>
        {
            { "Move_still", "静止" }, { "Move_sweep", "スイープ" }, { "Move_wave", "ウェーブ" }, { "Move_fan", "ファン" }, { "Move_circle", "サークル" },
            { "Move_cross", "クロス" }, { "Move_updown", "上下" }, { "Move_random", "ランダム" }, { "Move_audience", "客席あおり" }, { "Move_center", "センター" },
            { "Col_single", "単色" }, { "Col_alt", "交互" }, { "Col_three", "3色" }, { "Col_split", "左右" }, { "Col_rainbow", "虹" }, { "Col_beat", "拍替え" }, { "Col_chase", "追う" },
            { "Dim_on", "点灯" }, { "Dim_pulse", "脈打つ" }, { "Dim_wave", "波" }, { "Dim_chase", "1灯ずつ" }, { "Dim_alt", "交互" }, { "Dim_strobe", "ストロボ" }, { "Dim_random", "ランダム" },
            { "Laser_off", "OFF" }, { "Laser_fan", "ファン" }, { "Laser_sweep", "スイープ" }, { "Laser_cross", "クロス" }, { "Laser_cone", "コーン" },
            { "Shot_auto", "オート" }, { "Shot_orbit", "周回" }, { "Shot_dolly", "寄り引き" }, { "Shot_crane", "クレーン" }, { "Shot_truck", "横移動" },
            { "Shot_closeup", "アップ" }, { "Shot_audience", "客席" }, { "Shot_top", "真上" }, { "Shot_fixed", "固定" },
            { "Master_On", "点灯" }, { "Master_Off", "暗転" }, { "Master_Strobe", "ストロボ" }, { "Monitor_Live", "ライブ映像" }, { "Monitor_VJ", "VJ映像" }, { "Monitor2_Live", "ライブ映像" }, { "Monitor2_VJ", "VJ映像" },
            { "Wash_On", "ON" }, { "Wash_Off", "OFF" }, { "Pen_cue", "照明の色" }, { "Pen_white", "白" }, { "Pen_rainbow", "虹" },
        };

        static readonly Color BtnColor = new Color(0.13f, 0.15f, 0.33f, 1f), FxColor = new Color(0.45f, 0.2f, 0.5f, 1f), WarnColor = new Color(0.5f, 0.12f, 0.2f, 1f),
            TempoColor = new Color(0.1f, 0.3f, 0.32f, 1f), TitleColor = new Color(0.5f, 0.96f, 0.84f), SectionColor = new Color(1f, 0.56f, 0.82f), MarkColor = new Color(0.5f, 1f, 0.88f);

        /// <summary>ボタン1個分の設計図</summary>
        class Spec
        {
            public string label;
            public Color color = BtnColor;
            public int ch = -1, val;           // UdonSharp 版：チャンネル（または動作）と値
            public bool mark;                  // UdonSharp 版：選ばれていたら印を出す
            public string trigger;             // Animator だけの版：SetTrigger する名前
            public Animator anim;
            public List<ParticleSystem> ps;    // Animator だけの版：特効
            public List<AudioSource> audio;    // Animator だけの版：追加の特効の音
            public string[] stripes;           // 色ボタンの色
        }

        public static void Build(SKContext ctx)
        {
            if (ctx.lightAnimator == null) return;
            font = LoadFont();
            var go = new GameObject("Remote（リモコン）", typeof(RectTransform));
            go.transform.SetParent(ctx.root.transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            go.AddComponent<GraphicRaycaster>();
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.14f, 0.92f);

            bool udon = ctx.udon;
            float y = PAD;
            Label(go.transform, "STAGE REMOTE ― すてーじ工房" + (udon ? "（全員に同期）" : ""), PAD, y, 30, TitleColor); y += 44;
            Text status = null;
            GameObject lamp = null;
            if (udon)
            {
                int lines = ctx.spotLights.Count > 0 ? 3 : 2;
                status = Label(go.transform, "BPM " + Mathf.RoundToInt(ctx.bpm), PAD, y, 20, new Color(0.85f, 0.9f, 1f), lines); y += 32 + 26 * (lines - 1);
                lamp = Lamp(go.transform, W - PAD - 30, PAD + 6, 28);
            }
            else
            {
                Label(go.transform, "※ このリモコンは押した人の画面だけ切り替わります（UdonSharp 版にすると全員に同期します）", PAD, y, 16, new Color(0.7f, 0.74f, 0.9f)); y += 30;
            }

            var rows = new List<KeyValuePair<string, List<Spec>>>();
            var a = ctx.lightAnimator;
            if (udon)
            {
                var t = new List<Spec>
                {
                    Act("−5", SKCh.ACT_BPM, -5, TempoColor), Act("−1", SKCh.ACT_BPM, -1, TempoColor), Act("TAP", SKCh.ACT_BPM, 0, new Color(0.15f, 0.42f, 0.4f, 1f)),
                    Act("+1", SKCh.ACT_BPM, 1, TempoColor), Act("+5", SKCh.ACT_BPM, 5, TempoColor),
                };
                if (ctx.channels[SKCh.MOVE] != null)
                {
                    t.Add(Act("動き×½", SKCh.ACT_SPEED, 0, BtnColor, true));
                    t.Add(Act("動き×1", SKCh.ACT_SPEED, 1, BtnColor, true));
                    t.Add(Act("動き×2", SKCh.ACT_SPEED, 2, BtnColor, true));
                }
                rows.Add(Row("テンポ（TAP は拍に合わせて2回以上）・動きの速さ", t));
                if (ctx.audio)
                    rows.Add(Row("曲の音に反応（AudioLink：YamaPlayer などの曲で光る・弾む。止まっているときはいつもの演出）", new List<Spec>
                    {
                        Act("ON", SKCh.ACT_AUDIO, 1, TempoColor, true), Act("OFF", SKCh.ACT_AUDIO, 0, BtnColor, true),
                    }));
            }
            rows.Add(Row("動き", ChannelSpecs(ctx, SKCh.MOVE, SKMath.Patterns.Select(x => "Move_" + x), a)));
            rows.Add(Row("色の付け方", ChannelSpecs(ctx, SKCh.COLOR, SKMath.ColorModes.Select(x => "Col_" + x), a)));
            rows.Add(Row("色（パレット）", PaletteSpecs(ctx, a)));
            rows.Add(Row("明るさ", ChannelSpecs(ctx, SKCh.DIM, SKMath.DimModes.Select(x => "Dim_" + x), a)));
            rows.Add(Row("レーザー", ChannelSpecs(ctx, SKCh.LASER, SKMath.LaserModes.Select(x => "Laser_" + x), a)));
            var master = ChannelSpecs(ctx, SKCh.MASTER, new[] { "Master_On", "Master_Off", "Master_Strobe" }, a);
            foreach (var s in master)
            {
                if (s.trigger == "Master_Off") s.color = WarnColor;
                if (s.trigger == "Master_Strobe")
                {
                    s.color = FxColor;
                    if (udon) { s.ch = SKCh.ACT_STROBE; s.val = 0; s.mark = false; }   // ストロボは「一瞬のイベント」として全員に送る
                }
            }
            rows.Add(Row("照明ぜんぶ", master));
            rows.Add(Row("カラーウォッシュ（舞台の床を色で照らす）", ChannelSpecs(ctx, SKCh.WASH, new[] { "Wash_On", "Wash_Off" }, a)));
            rows.Add(Row("客席のペンライト", ChannelSpecs(ctx, SKCh.PEN, new[] { "Pen_cue", "Pen_white", "Pen_rainbow" }, a)));
            // 特効（パーティクル）
            var fx = new List<Spec>();
            var groups = new[] { ctx.sparks, ctx.confetti, ctx.smoke };
            string[] fxNames = { "スパーク", "紙吹雪", "スモーク" };
            for (int g = 0; g < 3; g++)
                if (groups[g].Count > 0) fx.Add(new Spec { label = fxNames[g], color = FxColor, ch = SKCh.ACT_FX, val = g, ps = groups[g] });
            rows.Add(Row("特効", fx));
            // 追加の特効（特効の設定ウィンドウで作ったもの）
            var fx2 = new List<Spec>();
            for (int k = 0; k < ctx.fxCustom.Count; k++)
            {
                var c = ctx.fxCustom[k];
                fx2.Add(new Spec { label = c.name, color = FxColor, ch = SKCh.ACT_FX, val = 3 + k, ps = c.systems, audio = c.audio, mark = udon && c.mode == 1 });
            }
            if (fx2.Count > 0) rows.Add(Row(udon && ctx.fxCustom.Any(c => c.mode == 1) ? "追加の特効（ON/OFF のものは、もう一度押すと止まる）" : "追加の特効", fx2));
            for (int k = 0; k < 2; k++)
                if (ctx.camAnimators[k] != null)
                    rows.Add(Row("カメラ" + (k + 1) + "（モニターの映像）", ChannelSpecs(ctx, k == 0 ? SKCh.CAM1 : SKCh.CAM2, SKMath.CamModes.Select(x => "Shot_" + x), ctx.camAnimators[k])));
            if (udon && (ctx.cams[0] != null || ctx.cams[1] != null))
                rows.Add(Row("カメラのアップで顔を追う人（押した人を登録・もう一度押すと外れる）", new List<Spec>
                {
                    Act("自分→カメラ1", SKCh.ACT_UP, 0, TempoColor, true), Act("自分→カメラ2", SKCh.ACT_UP, 1, TempoColor, true), Act("登録を外す", SKCh.ACT_UP, 3, WarnColor),
                }));
            if (udon && ctx.spotLights.Count > 0)
                rows.Add(Row("スポットライト（演者を照らす本物のライト。押した人を追う・もう一度押すと固定に戻る）", new List<Spec>
                {
                    Act("自分→スポット1", SKCh.ACT_SPOT, 0, TempoColor, true), Act("自分→スポット2", SKCh.ACT_SPOT, 1, TempoColor, true), Act("固定に戻す", SKCh.ACT_SPOT, 3, WarnColor),
                    Act("ON", SKCh.ACT_SPOTON, 1, BtnColor, true), Act("OFF", SKCh.ACT_SPOTON, 0, BtnColor, true),
                }));
            rows.Add(Row("モニター（メイン）", ChannelSpecs(ctx, SKCh.MONITOR, new[] { "Monitor_Live", "Monitor_VJ" }, a)));
            rows.Add(Row("モニター（サブ：左右ミラーなど）", ChannelSpecs(ctx, SKCh.MONITOR2, new[] { "Monitor2_Live", "Monitor2_VJ" }, a)));

            // ---- ボタンを並べる
            var made = new List<KeyValuePair<Spec, Button>>();
            foreach (var row in rows)
            {
                if (row.Value.Count == 0) continue;
                Label(go.transform, row.Key, PAD, y, 22, SectionColor); y += 32;
                for (int i = 0; i < row.Value.Count; i++)
                {
                    var s = row.Value[i];
                    var b = MakeButton(go.transform, s.label, PAD + (i % COLS) * (CELL_W + GAP), y + (i / COLS) * (CELL_H + GAP), CELL_W, CELL_H, s.color);
                    if (s.stripes != null) Stripes(b, s.stripes);
                    made.Add(new KeyValuePair<Spec, Button>(s, b));
                }
                y += ((row.Value.Count + COLS - 1) / COLS) * (CELL_H + GAP) + 10;
            }
            y += PAD - 10;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(W, y);
            rt.pivot = new Vector2(0.5f, 0f);
            go.transform.localScale = Vector3.one * PX;
            // 置き場所：客席から見てステージ下手（左）手前。パネルの正面が操作する人の方を向く
            var st = J.O(ctx.state, "stage");
            float sw = J.F(st, "width", 16f), sd = J.F(st, "depth", 8f);
            Vector3 posW = ctx.ToWorld(new Vector3(-(sw / 2 + 2.6f), 0.95f, sd / 2 + 2.5f));
            go.transform.position = posW;
            go.transform.rotation = Quaternion.LookRotation(ctx.DirToWorld(new Vector3(0, 0, -1)), Vector3.up) * Quaternion.Euler(15, 0, 0);
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stand.name = "RemoteStand";
            stand.transform.SetParent(ctx.root.transform, true);
            stand.transform.position = posW - Vector3.up * 0.475f;
            stand.transform.localScale = new Vector3(0.08f, 0.95f, 0.08f);

            // ---- ボタンの配線
            if (udon)
            {
                if (!SKUdon.Wire(ctx, go, made.Select(m => new SKUdon.Item { button = m.Value, ch = m.Key.ch, val = m.Key.val, mark = m.Key.mark ? Mark(m.Value) : null }).ToList(), status, lamp))
                {
                    ctx.Log("⚠ UdonSharp の設定に失敗したので、Animator だけのリモコンにしました（押した人の画面だけ切り替わります）");
                    foreach (var m in made) WireLocal(m.Key, m.Value);
                }
            }
            else foreach (var m in made) WireLocal(m.Key, m.Value);

            // VRChat：ワールドの UI を触れるようにする（SDK があるときだけ）
            var uiShape = SKUdon.FindType("VRC.SDK3.Components.VRCUiShape");
            if (uiShape != null) go.AddComponent(uiShape);
            else ctx.Log("（VRChat SDK が無いので VRC Ui Shape は付けていません。VRChat で使うときは Canvas に VRC Ui Shape を追加してください）");
            // Unity の再生ボタンで試すための EventSystem（EditorOnly タグなのでワールドには入らない）
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem（Unity確認用）", typeof(EventSystem), typeof(StandaloneInputModule));
                es.tag = "EditorOnly";
                es.transform.SetParent(ctx.root.transform, false);
            }
            if (Camera.main != null) canvas.worldCamera = Camera.main;
            ctx.Log("ワールド用リモコンを置きました（ステージ下手の手前）" + (ctx.udon ? "：UdonSharp 版（全員に同期・テンポ変更あり）" : "：Animator だけの版（押した人の画面だけ）"));
        }

        static KeyValuePair<string, List<Spec>> Row(string title, List<Spec> specs) { return new KeyValuePair<string, List<Spec>>(title, specs); }

        static Spec Act(string label, int ch, int val, Color c, bool mark = false) { return new Spec { label = label, ch = ch, val = val, color = c, mark = mark }; }

        /// <summary>チャンネル（Animator のレイヤー）にあるステートだけボタンにする</summary>
        static List<Spec> ChannelSpecs(SKContext ctx, int chIndex, IEnumerable<string> triggers, Animator anim)
        {
            var list = new List<Spec>();
            var ch = ctx.channels[chIndex];
            if (ch == null || anim == null) return list;
            foreach (var t in triggers)
            {
                int idx = ch.IndexOf(t);
                if (idx < 0) continue;
                list.Add(new Spec { label = JP.TryGetValue(t, out var jp) ? jp : t, ch = chIndex, val = idx, mark = true, trigger = t, anim = anim });
            }
            return list;
        }

        static List<Spec> PaletteSpecs(SKContext ctx, Animator anim)
        {
            var list = new List<Spec>();
            var ch = ctx.channels[SKCh.PALETTE];
            if (ch == null) return list;
            var items = new List<KeyValuePair<string, string[]>> { new KeyValuePair<string, string[]>("Pal_Live", new[] { ctx.live.c1, ctx.live.c2, ctx.live.c3 }) };
            var pals = J.L(ctx.data, "palettes");
            for (int i = 0; i < pals.Count; i++)
            {
                var cs = J.L(pals[i], "c");
                if (cs.Count >= 3) items.Add(new KeyValuePair<string, string[]>("Pal_" + i, new[] { cs[0] as string, cs[1] as string, cs[2] as string }));
            }
            foreach (var it in items)
            {
                int idx = ch.IndexOf(it.Key);
                if (idx < 0) continue;
                bool live = it.Key == "Pal_Live";
                list.Add(new Spec { label = live ? "シーン色" : "", color = J.Col(it.Value[0], Color.white), ch = SKCh.PALETTE, val = idx, mark = true, trigger = it.Key, anim = anim, stripes = live ? null : it.Value });
            }
            return list;
        }

        /// <summary>Animator だけの版：ボタンから SetTrigger / ParticleSystem.Play を直接呼ぶ</summary>
        static void WireLocal(Spec s, Button b)
        {
            if (s.ps != null)
            {
                foreach (var ps in s.ps) UnityEventTools.AddVoidPersistentListener(b.onClick, new UnityAction(ps.Play));
                if (s.audio != null) foreach (var a in s.audio) UnityEventTools.AddVoidPersistentListener(b.onClick, new UnityAction(a.Play));
            }
            else if (s.anim != null && !string.IsNullOrEmpty(s.trigger))
                UnityEventTools.AddStringPersistentListener(b.onClick, new UnityAction<string>(s.anim.SetTrigger), s.trigger);
        }

        static void Stripes(Button b, string[] cols)
        {
            for (int k = 1; k < 3; k++)
            {
                var s = new GameObject("Stripe", typeof(RectTransform), typeof(Image));
                s.transform.SetParent(b.transform, false);
                var r = (RectTransform)s.transform;
                r.anchorMin = new Vector2(k / 3f, 0); r.anchorMax = new Vector2((k + 1) / 3f, 1); r.offsetMin = r.offsetMax = Vector2.zero;
                var img = s.GetComponent<Image>(); img.color = J.Col(cols[k], Color.white); img.raycastTarget = false;
            }
        }

        /// <summary>選ばれているボタンの下に出す印（UdonSharp 版のコントローラーが ON/OFF する）</summary>
        static GameObject Mark(Button b)
        {
            var m = new GameObject("Mark", typeof(RectTransform), typeof(Image));
            m.transform.SetParent(b.transform, false);
            var r = (RectTransform)m.transform;
            r.anchorMin = new Vector2(0.08f, 0); r.anchorMax = new Vector2(0.92f, 0); r.pivot = new Vector2(0.5f, 0);
            r.anchoredPosition = new Vector2(0, 3); r.sizeDelta = new Vector2(0, 6);
            var img = m.GetComponent<Image>(); img.color = MarkColor; img.raycastTarget = false;
            m.SetActive(false);
            return m;
        }

        /// <summary>拍に合わせて光るランプ。点滅でキャンバス全体を描き直さないよう、自分用の Canvas を持たせる</summary>
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
            on.GetComponent<Image>().color = MarkColor; on.GetComponent<Image>().raycastTarget = false;
            on.SetActive(false);
            return on;
        }

        static void Fill(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        static Text Label(Transform parent, string text, float x, float y, int size, Color c, int lines = 1)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(W - 2 * PAD - 40, (size + 6) * lines + 4);
            var t = go.GetComponent<Text>();
            t.text = text; t.font = font; t.fontSize = size; t.color = c; t.alignment = TextAnchor.MiddleLeft; t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(Transform parent, string label, float x, float y, float w, float h, Color bgc)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
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
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
                var t = tg.GetComponent<Text>();
                t.text = label; t.font = font; t.fontSize = label.Length > 7 ? 15 : label.Length > 5 ? 17 : 20; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white; t.raycastTarget = false;
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
