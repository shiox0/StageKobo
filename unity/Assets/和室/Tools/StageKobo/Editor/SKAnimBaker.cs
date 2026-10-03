using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// 演出を Animator に焼き込む。リモコンの「チャンネル」ごとにレイヤーを分けるので、
    /// 動き × 色 × 明るさ × レーザー … を自由に組み合わせられる（ブラウザ版のリモコンと同じ考え方）。
    /// ステートの切り替えは同名のトリガー（例：Move_wave）。UI ボタンから Animator.SetTrigger で呼べる。
    /// Udon を使わずに動くので、VRChat ワールドにそのまま持っていける。
    /// </summary>
    public static class SKAnimBaker
    {
        const float FPS = 30f;
        public const string LightsController = "SK_Lights.controller";

        class Bind { public string path; public Type type; public string prop; public Bind(string p, Type t, string pr) { path = p; type = t; prop = pr; } }

        public const string TempoParam = "Tempo";          // 曲のテンポ（いまの BPM ÷ 焼き込んだ BPM）。UdonSharp 版が書き換える
        public const string MoveTempoParam = "MoveTempo";  // 動きだけの速さ（Tempo × ×½/×1/×2）

        public static void Bake(SKContext ctx)
        {
            var ctrl = NewController(ctx, LightsController);
            AddTempoParams(ctrl, true);
            var mh = ctx.fixtures.Where(f => f.kind == "mh").ToList();
            // LEDバー（bar）は beamR に光る部分を入れてある（色の付け方・パレット・明るさ・マスターがムービングライトと同じ式で効く）
            var dimFx = ctx.fixtures.Where(f => (f.kind == "mh" || f.kind == "par" || f.kind == "wash" || f.kind == "bar") && f.beamR != null).ToList();
            var colorFx = ctx.fixtures.Where(f => f.follow && ((f.kind == "mh" || f.kind == "par" || f.kind == "wash" || f.kind == "bar") && f.beamR != null || f.kind == "laser")).ToList();
            var strobeR = ctx.fixtures.Where(f => f.kind == "strobe").SelectMany(f => f.cellR).Where(r => r != null).ToList();   // ストロボ／ブラインダーの光る面
            var lasers = ctx.fixtures.Where(f => f.kind == "laser").ToList();
            double spb = 60.0 / ctx.bpm;
            bool first = true;
            int li, mode;

            // ---- 動き
            if (mh.Count > 0)
            {
                var sm = AddLayer(ctrl, "Move", ref first, out li);
                var ch = Chan(ctx, SKCh.MOVE, 0, li, 0.45f);
                int k = 0;
                foreach (var p in SKMath.Patterns)
                {
                    var clip = MoveClip(ctx, mh, p, spb, out mode);
                    var st = AddState(ctrl, sm, "Move_" + p, clip, 0.45f, k++, ch, mode, MoveTempoParam);
                    if (p == ctx.live.pattern) { sm.defaultState = st; ch.def = ch.names.Count - 1; }
                }
            }
            // ---- 色の付け方
            if (colorFx.Count > 0)
            {
                var sm = AddLayer(ctrl, "ColorMode", ref first, out li);
                var ch = Chan(ctx, SKCh.COLOR, 0, li, 0f);
                int k = 0;
                foreach (var m in SKMath.ColorModes)
                {
                    var st = AddState(ctrl, sm, "Col_" + m, ColorModeClip(ctx, colorFx, m, spb, out mode), 0f, k++, ch, mode, TempoParam);
                    if (m == ctx.live.colorMode) { sm.defaultState = st; ch.def = ch.names.Count - 1; }
                }
            }
            // ---- パレット（色ボタン）：照明のほか、ペンライト・客席のふち・奥のウォッシュも同じ色になる
            if (colorFx.Count > 0 || ctx.paletteExtra.Count > 0)
            {
                var sm2 = AddLayer(ctrl, "Palette", ref first, out li);
                var ch = Chan(ctx, SKCh.PALETTE, 0, li, 0.25f);
                var live = AddState(ctrl, sm2, "Pal_Live", PaletteClip(ctx, colorFx, "Pal_Live", ctx.live.c1, ctx.live.c2, ctx.live.c3), 0.25f, 0, ch, SKCh.MODE_CONST, null);
                sm2.defaultState = live;
                var pals = J.L(ctx.data, "palettes");
                for (int i = 0; i < pals.Count; i++)
                {
                    var cs = J.L(pals[i], "c");
                    if (cs.Count < 3) continue;
                    AddState(ctrl, sm2, "Pal_" + i, PaletteClip(ctx, colorFx, "Pal_" + i, cs[0] as string, cs[1] as string, cs[2] as string), 0.25f, i + 1, ch, SKCh.MODE_CONST, null);
                }
            }
            // ---- 明るさの変化
            if (dimFx.Count > 0 || strobeR.Count > 0)
            {
                var sm = AddLayer(ctrl, "Dim", ref first, out li);
                var ch = Chan(ctx, SKCh.DIM, 0, li, 0f);
                int k = 0;
                foreach (var m in SKMath.DimModes)
                {
                    var st = AddState(ctrl, sm, "Dim_" + m, DimClip(ctx, dimFx, strobeR, m, spb, out mode), 0f, k++, ch, mode, TempoParam);
                    if (m == ctx.live.dim) { sm.defaultState = st; ch.def = ch.names.Count - 1; }
                }
            }
            // ---- レーザー
            if (lasers.Count > 0)
            {
                var sm = AddLayer(ctrl, "Laser", ref first, out li);
                var ch = Chan(ctx, SKCh.LASER, 0, li, 0.2f);
                int k = 0;
                foreach (var m in SKMath.LaserModes)
                {
                    var st = AddState(ctrl, sm, "Laser_" + m, LaserClip(ctx, lasers, m, spb, out mode), 0.2f, k++, ch, mode, TempoParam);
                    if (m == ctx.live.laser) { sm.defaultState = st; ch.def = ch.names.Count - 1; }
                }
            }
            // ---- マスター（点灯・暗転・ストロボ）
            var masterR = new List<Renderer>();
            foreach (var f in ctx.fixtures) { if (f.beamR != null && f.kind != "pin") masterR.Add(f.beamR); if (f.lensR != null && f.kind != "pin") masterR.Add(f.lensR); masterR.AddRange(f.laserR); masterR.AddRange(f.cellR); }
            if (ctx.washBack != null) masterR.Add(ctx.washBack);
            masterR.AddRange(ctx.spotBeams);   // 演者を照らすスポットの見た目のビーム（ライト本体の明るさも下で一緒に）
            if (masterR.Count > 0 || ctx.spotLights.Count > 0)
            {
                var sm = AddLayer(ctrl, "Master", ref first, out li);
                var ch = Chan(ctx, SKCh.MASTER, 0, li, 0.15f);
                var on = AddState(ctrl, sm, "Master_On", ConstRendererClip(ctx, "Master_On", masterR, "_Master", 1f, strobeR, "_StrobeTrig", 0f), 0.15f, 0, ch, SKCh.MODE_CONST, null);
                AddState(ctrl, sm, "Master_Off", ConstRendererClip(ctx, "Master_Off", masterR, "_Master", 0f, strobeR, "_StrobeTrig", 0f), 0.15f, 1, ch, SKCh.MODE_CONST, null);
                var strobe = AddState(ctrl, sm, "Master_Strobe", StrobeClip(ctx, masterR, strobeR), 0f, 2, ch, SKCh.MODE_CONST, null);
                // スポットライト（本物のライト）：点灯 = 置いたときの明るさ、暗転 = 0、ストロボ = 点滅
                foreach (var s in sm.states)
                {
                    var c = s.state.motion as AnimationClip;
                    if (c == null) continue;
                    foreach (var L in ctx.spotLights)
                    {
                        if (L == null) continue;
                        AnimationCurve cv;
                        if (s.state.name == "Master_Off") cv = Const(0f, 1);
                        else if (s.state.name == "Master_Strobe") cv = StrobeCurve(L.intensity);
                        else cv = Const(L.intensity, 1);
                        c.SetCurve(P(ctx, L.transform), typeof(Light), "m_Intensity", cv);
                    }
                    EditorUtility.SetDirty(c);
                }
                var back = strobe.AddTransition(on);
                back.hasExitTime = true; back.exitTime = 1f; back.duration = 0f; back.hasFixedDuration = true;
                sm.defaultState = on;
            }
            // ---- モニター（ライブ映像 / VJ映像）：メイン（Monitor_～）とサブ（Monitor2_～）を別々に切り替える
            // VJ を組み立てたとき：カメラ映像（_Src 0）のまま、_VJ で VJ の映像に切り替える。無いとき：VJ パターン（_Src 1）
            string prop = ctx.vj != null ? "_VJ" : "_Src";
            for (int g = 0; g < 2; g++)
            {
                var rs = g == 0 ? ctx.camMonitors : ctx.camMonitors2;
                if (rs.Count == 0) continue;
                string pre = g == 0 ? "Monitor" : "Monitor2";
                var sm = AddLayer(ctrl, pre, ref first, out li);
                var ch = Chan(ctx, g == 0 ? SKCh.MONITOR : SKCh.MONITOR2, 0, li, 0f);
                var a = AddState(ctrl, sm, pre + "_Live", ConstRendererClip(ctx, pre + "_Live", rs, prop, 0f), 0f, 0, ch, SKCh.MODE_CONST, null);
                var b = AddState(ctrl, sm, pre + "_VJ", ConstRendererClip(ctx, pre + "_VJ", rs, prop, 1f), 0f, 1, ch, SKCh.MODE_CONST, null);
                bool vj = SKBuilder.MonitorMode(ctx, g == 1) == "vj";
                sm.defaultState = vj ? b : a; ch.def = vj ? 1 : 0;
            }
            // ---- カラーウォッシュ（ON/OFF）
            if (ctx.washR.Count > 0)
            {
                var sm = AddLayer(ctrl, "Wash", ref first, out li);
                var ch = Chan(ctx, SKCh.WASH, 0, li, 0.3f);
                var on = AddState(ctrl, sm, "Wash_On", WashClip(ctx, true), 0.3f, 0, ch, SKCh.MODE_CONST, null);
                var off = AddState(ctrl, sm, "Wash_Off", WashClip(ctx, false), 0.3f, 1, ch, SKCh.MODE_CONST, null);
                sm.defaultState = ctx.live.wash ? on : off; ch.def = ctx.live.wash ? 0 : 1;
            }
            // ---- 客席のペンライト（照明の色 / 白 / 虹）
            if (ctx.penCueR.Count > 0)
            {
                var sm = AddLayer(ctrl, "Penlight", ref first, out li);
                var ch = Chan(ctx, SKCh.PEN, 0, li, 0.3f);
                string[] keys = { "cue", "white", "rainbow" };
                for (int i = 0; i < keys.Length; i++)
                {
                    var st = AddState(ctrl, sm, "Pen_" + keys[i], ConstRendererClip(ctx, "Pen_" + keys[i], ctx.penCueR, "_Mode", SKBuilder.PenMode(keys[i])), 0f, i, ch, SKCh.MODE_CONST, null);
                    if (keys[i] == (ctx.live.penlight == "white" || ctx.live.penlight == "rainbow" ? ctx.live.penlight : "cue")) { sm.defaultState = st; ch.def = i; }
                }
            }
            // ---- 演者ダミーのダンス（常時・拍に合わせる）
            var perfs = ctx.cfg.performers ? ctx.units.Where(u => u.type == "performer").ToList() : new List<SKUnit>();
            if (perfs.Count > 0)
            {
                var clip = PerfClip(ctx, perfs, spb, out int danced);
                if (danced > 0)
                {
                    var sm = AddLayer(ctrl, "Performer", ref first, out li);
                    var ch = Chan(ctx, SKCh.PERF, 0, li, 0f);
                    sm.defaultState = AddState(ctrl, sm, "Perf_Dance", clip, 0f, 0, ch, SKCh.MODE_BEAT, TempoParam);
                    ctx.Log("演者ダミー " + danced + " 人のダンスを焼き込みました（8拍で1周）");
                }
                else ctx.Log("（演者ダミーの関節が GLB に見つからないのでダンスは焼き込みませんでした。すてーじ工房 v0.3 以降で書き出し直すと動きます）");
            }
            // ---- ミラーボールの回転（常時）
            if (ctx.mirrorBalls.Count > 0)
            {
                var sm = AddLayer(ctrl, "Ambient", ref first, out li);
                var ch = Chan(ctx, SKCh.AMBIENT, 0, li, 0f);
                var bu = ctx.units.FirstOrDefault(u => u.type == "mirror_ball");
                float speed = Mathf.Max(0.01f, bu != null ? J.F(bu.p, "speed", 0.15f) : 0.15f);
                sm.defaultState = AddState(ctrl, sm, "Ambient_Spin", SpinClip(ctx, ctx.mirrorBalls, 1f / (speed * 0.25f)), 0f, 0, ch, SKCh.MODE_TIME, null);
            }

            // ---- ネオンの回転・ふわふわ（常時。ブラウザ版：回転 0.6 rad/秒、ふわふわ sin(1.2 t + 置いた位置) × 0.15m）
            if (ctx.neonSpin.Count > 0) LoopLayer(ctrl, "NeonSpin", NeonSpinClip(ctx), ref first);
            if (ctx.neonFloat.Count > 0) LoopLayer(ctrl, "NeonFloat", NeonFloatClip(ctx), ref first);

            var anim = ctx.root.GetComponent<Animator>();
            if (anim == null) anim = ctx.root.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ctx.lightAnimator = anim;

            // ---- ステージカメラ
            if (ctx.cfg.cams) for (int i = 0; i < 2; i++) if (ctx.cams[i] != null) BakeCamera(ctx, i);
            ctx.Log("Animator に焼き込みました（BPM " + ctx.bpm + "）：動き " + SKMath.Patterns.Length + "・色 " + SKMath.ColorModes.Length + "・明るさ " + SKMath.DimModes.Length + (lasers.Count > 0 ? "・レーザー 5" : "") + "・カメラワーク " + SKMath.CamModes.Length
                + (ctx.washR.Count > 0 ? "・ウォッシュ" : "") + (ctx.penCueR.Count > 0 ? "・ペンライト" : ""));
        }

        /// <summary>テンポ用の Float パラメーター（初期値 1。0 だと止まってしまうので必ず 1 で作る）</summary>
        static void AddTempoParams(AnimatorController ctrl, bool move)
        {
            ctrl.AddParameter(new AnimatorControllerParameter { name = TempoParam, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            if (move) ctrl.AddParameter(new AnimatorControllerParameter { name = MoveTempoParam, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        }

        static SKChannel Chan(SKContext ctx, int ch, int anim, int layer, float fade)
        {
            var c = new SKChannel { ch = ch, anim = anim, layer = layer, fade = fade };
            ctx.channels[ch] = c;
            return c;
        }

        // ------------------------------------------------------------------ コントローラー

        static AnimatorController NewController(SKContext ctx, string file)
        {
            string path = ctx.dir + "/Animation/" + file;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        static AnimatorStateMachine AddLayer(AnimatorController ctrl, string name, ref bool first, out int index)
        {
            if (first)
            {
                first = false;
                var ls = ctrl.layers; ls[0].name = name; ls[0].defaultWeight = 1f; ctrl.layers = ls;
                index = 0;
                return ctrl.layers[0].stateMachine;
            }
            ctrl.AddLayer(name);
            var l2 = ctrl.layers; l2[l2.Length - 1].defaultWeight = 1f; ctrl.layers = l2;
            index = l2.Length - 1;
            return ctrl.layers[index].stateMachine;
        }

        /// <summary>
        /// ステートを足して、同名トリガーで AnyState から入れるようにする（UdonSharp なしのリモコン用）。
        /// ch があれば UdonSharp 版のコントローラーに渡す情報（名前・長さ・時間の合わせ方）も記録する。
        /// speedParam：拍に合わせて動くステートは、テンポのパラメーターで再生速度が変わる
        /// </summary>
        static AnimatorState AddState(AnimatorController ctrl, AnimatorStateMachine sm, string name, AnimationClip clip, float fade, int slot, SKChannel ch, int mode, string speedParam)
        {
            var st = sm.AddState(name, new Vector3(300 + (slot % 4) * 220, 60 + (slot / 4) * 70, 0));
            st.motion = clip;
            st.writeDefaultValues = false;
            if (speedParam != null && mode != SKCh.MODE_CONST && mode != SKCh.MODE_TIME)
            {
                st.speedParameterActive = true;
                st.speedParameter = speedParam;
            }
            if (!ctrl.parameters.Any(p => p.name == name)) ctrl.AddParameter(name, AnimatorControllerParameterType.Trigger);
            var tr = sm.AddAnyStateTransition(st);
            tr.AddCondition(AnimatorConditionMode.If, 0, name);
            tr.hasExitTime = false; tr.hasFixedDuration = true; tr.duration = fade; tr.canTransitionToSelf = false;
            if (ch != null)
            {
                ch.names.Add(name);
                ch.lens.Add(clip != null ? clip.length : 0f);
                ch.modes.Add(clip != null && clip.length > 0 ? mode : SKCh.MODE_CONST);
            }
            return st;
        }

        static AnimationClip SaveClip(SKContext ctx, AnimationClip clip, bool loop)
        {
            SKAssets.Save(clip, ctx.dir + "/Animation/" + clip.name + ".anim");
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            return clip;
        }

        static string P(SKContext ctx, Transform t) { return AnimationUtility.CalculateTransformPath(t, ctx.root.transform); }

        // ------------------------------------------------------------------ カーブ

        static AnimationCurve Linear(IList<float> t, IList<float> v, IList<bool> stepAfter = null)
        {
            int n = t.Count;
            var keys = new Keyframe[n];
            for (int i = 0; i < n; i++)
            {
                float inT = i > 0 ? (v[i] - v[i - 1]) / Mathf.Max(1e-5f, t[i] - t[i - 1]) : 0f;
                float outT = i < n - 1 ? (v[i + 1] - v[i]) / Mathf.Max(1e-5f, t[i + 1] - t[i]) : inT;
                if (i == 0) inT = outT;
                if (stepAfter != null && stepAfter[i]) outT = float.PositiveInfinity;
                if (stepAfter != null && i > 0 && stepAfter[i - 1]) inT = float.PositiveInfinity;
                keys[i] = new Keyframe(t[i], v[i], inT, outT);
            }
            return new AnimationCurve(keys);
        }

        static AnimationCurve Stepped(IList<float> t, IList<float> v)
        {
            var keys = new Keyframe[t.Count];
            for (int i = 0; i < t.Count; i++) keys[i] = new Keyframe(t[i], v[i], float.PositiveInfinity, float.PositiveInfinity);
            return new AnimationCurve(keys);
        }

        static AnimationCurve Const(float v, float len) { return Linear(new[] { 0f, len }, new[] { v, v }); }

        static void SetRot(AnimationClip clip, string path, IList<float> t, IList<Quaternion> q, IList<bool> step = null)
        {
            clip.SetCurve(path, typeof(Transform), "localRotation.x", Linear(t, q.Select(x => x.x).ToList(), step));
            clip.SetCurve(path, typeof(Transform), "localRotation.y", Linear(t, q.Select(x => x.y).ToList(), step));
            clip.SetCurve(path, typeof(Transform), "localRotation.z", Linear(t, q.Select(x => x.z).ToList(), step));
            clip.SetCurve(path, typeof(Transform), "localRotation.w", Linear(t, q.Select(x => x.w).ToList(), step));
        }

        static List<float> Times(int n, double dt) { var l = new List<float>(n + 1); for (int i = 0; i <= n; i++) l.Add((float)(i * dt)); return l; }

        // ------------------------------------------------------------------ 動き

        /// <summary>そのパターンを使っているシーンのパラメータ（振り幅など）。無ければ「いまの演出」の値</summary>
        static SKCue RepresentativeCue(SKContext ctx, string pattern)
        {
            var c = (ctx.scenes.FirstOrDefault(s => s.pattern == pattern) ?? ctx.live).Clone();
            c.pattern = pattern;
            return c;
        }

        static AnimationClip MoveClip(SKContext ctx, List<SKFixture> mh, string pattern, double spb, out int mode)
        {
            var clip = new AnimationClip { name = "Move_" + pattern, frameRate = FPS };
            var cue = RepresentativeCue(ctx, pattern);
            int wrap;
            double beats = SKMath.PatternPeriodBeats(cue, out wrap);
            mode = beats > 0 ? SKCh.MODE_MOVE : SKCh.MODE_CONST;
            foreach (var f in mh)
            {
                string pp = f.pan != null ? P(ctx, f.pan) : null, tp = P(ctx, f.tilt);
                if (beats <= 0)
                {
                    double pan, tilt;
                    SKBuilder.PoseAt(ctx, f, cue, 0, out pan, out tilt);
                    var tt = new[] { 0f, 1f };
                    if (pp != null) SetRot(clip, pp, tt, new[] { Quaternion.Euler(0, (float)pan, 0), Quaternion.Euler(0, (float)pan, 0) });
                    SetRot(clip, tp, tt, new[] { Quaternion.Euler((float)tilt, 0, 0), Quaternion.Euler((float)tilt, 0, 0) });
                    continue;
                }
                double T = beats * spb;
                int N = Mathf.Max(8, Mathf.CeilToInt((float)(T * FPS)));
                double dt = T / N;
                const int sub = 4;
                double h = dt / sub;
                double cp, ct;
                SKBuilder.PoseAt(ctx, f, cue, 0, out cp, out ct, wrap);
                var pans = new Quaternion[N + 1];
                var tilts = new Quaternion[N + 1];
                // ブラウザ版と同じ「首の追従の遅れ」を2周ぶん計算して、2周目を記録する（ループの継ぎ目がなめらかになる）
                for (int s = 0; s <= 2 * N * sub; s++)
                {
                    double time = s * h, tp2, tt2;
                    SKBuilder.PoseAt(ctx, f, cue, time / spb, out tp2, out tt2, wrap);
                    double k = 1 - Math.Exp(-h * 7);
                    if (tt2 > 1.72) cp += WrapDeg(tp2 - cp) * k;
                    ct += (tt2 - ct) * k;
                    if (s >= N * sub && (s - N * sub) % sub == 0)
                    {
                        int idx = (s - N * sub) / sub;
                        pans[idx] = Quaternion.Euler(0, (float)cp, 0);
                        tilts[idx] = Quaternion.Euler((float)ct, 0, 0);
                    }
                }
                var times = Times(N, dt);
                if (pp != null) SetRot(clip, pp, times, pans);
                SetRot(clip, tp, times, tilts);
            }
            clip.EnsureQuaternionContinuity();
            return SaveClip(ctx, clip, true);
        }

        static double WrapDeg(double a) { while (a > 180) a -= 360; while (a < -180) a += 360; return a; }

        // ------------------------------------------------------------------ 色・明るさ

        static List<Renderer> FixtureRenderers(SKFixture f)
        {
            var l = new List<Renderer>();
            if (f.beamR != null) l.Add(f.beamR);
            if (f.lensR != null && f.follow) l.Add(f.lensR);
            l.AddRange(f.laserR);
            return l;
        }

        static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }

        /// <summary>種類ごとに台数が違っても継ぎ目なくループする長さ（半拍単位の最小公倍数）</summary>
        static double CommonBeats(IEnumerable<SKFixture> fx, Func<int, double> periodOf)
        {
            int l = 1;
            foreach (var n in fx.Select(f => f.n).Distinct())
            {
                int halfBeats = Mathf.Max(1, Mathf.RoundToInt((float)(periodOf(n) * 2)));
                l = l / Gcd(l, halfBeats) * halfBeats;
                if (l > 256) { l = 256; break; }
            }
            return l / 2.0;
        }

        static AnimationClip ColorModeClip(SKContext ctx, List<SKFixture> fx, string mode, double spb, out int tmode)
        {
            var clip = new AnimationClip { name = "Col_" + mode, frameRate = FPS };
            double beats = mode == "rainbow" ? 50 : mode == "beat" ? 3 : mode == "chase" ? CommonBeats(fx, n => Math.Max(1, n) / 2.0) : 0;
            tmode = beats > 0 ? SKCh.MODE_BEAT : SKCh.MODE_CONST;
            foreach (var f in fx)
            {
                var rs = FixtureRenderers(f);
                List<float> t, slot, hue;
                if (beats <= 0)
                {
                    float s, h;
                    SKMath.ColorSlot(mode, 0, f.i, f.n, f.xn, out s, out h);
                    t = new List<float> { 0, 1 }; slot = new List<float> { s, s }; hue = new List<float> { h, h };
                }
                else if (mode == "rainbow")
                {
                    float s0, h0, s1, h1;
                    SKMath.ColorSlot(mode, 0, f.i, f.n, f.xn, out s0, out h0);
                    SKMath.ColorSlot(mode, beats, f.i, f.n, f.xn, out s1, out h1);
                    t = new List<float> { 0, (float)(beats * spb) }; slot = new List<float> { 3, 3 }; hue = new List<float> { h0, h1 };
                }
                else
                {
                    int steps = Mathf.RoundToInt((float)(beats * 2));
                    t = new List<float>(); slot = new List<float>(); hue = new List<float>();
                    for (int i = 0; i <= steps; i++)
                    {
                        double b = i * 0.5;
                        float s, h;
                        SKMath.ColorSlot(mode, b + 0.01, f.i, f.n, f.xn, out s, out h);
                        t.Add((float)(b * spb)); slot.Add(s); hue.Add(h);
                    }
                }
                foreach (var r in rs)
                {
                    string path = P(ctx, r.transform);
                    clip.SetCurve(path, r.GetType(), "material._Slot", Stepped(t, slot));
                    clip.SetCurve(path, r.GetType(), "material._Hue", Linear(t, hue));
                }
            }
            return SaveClip(ctx, clip, true);
        }

        static AnimationClip PaletteClip(SKContext ctx, List<SKFixture> fx, string name, string h1, string h2, string h3)
        {
            var clip = new AnimationClip { name = name, frameRate = FPS };
            var cs = new[] { J.Col(h1, Color.white), J.Col(h2, Color.white), J.Col(h3, Color.white) };
            foreach (var r in fx.SelectMany(FixtureRenderers).Concat(ctx.paletteExtra).Distinct())
            {
                string path = P(ctx, r.transform);
                for (int k = 0; k < 3; k++)
                {
                    string prop = "material._C" + (k + 1);
                    clip.SetCurve(path, r.GetType(), prop + ".r", Const(cs[k].r, 1));
                    clip.SetCurve(path, r.GetType(), prop + ".g", Const(cs[k].g, 1));
                    clip.SetCurve(path, r.GetType(), prop + ".b", Const(cs[k].b, 1));
                }
            }
            return SaveClip(ctx, clip, true);
        }

        static AnimationClip DimClip(SKContext ctx, List<SKFixture> fx, List<Renderer> strobeR, string mode, double spb, out int tmode)
        {
            var clip = new AnimationClip { name = "Dim_" + mode, frameRate = FPS };
            // ストロボ／ブラインダー：明るさ「ストロボ」のときだけ速く点滅（ブラウザ版の cue.dim === 'strobe'）
            foreach (var r in strobeR) clip.SetCurve(P(ctx, r.transform), r.GetType(), "material._DimStrobe", Const(mode == "strobe" ? 1f : 0f, 1));
            double beats = mode == "chase" ? CommonBeats(fx, n => Math.Max(1, n) / 2.0) : SKMath.DimPeriodBeats(mode, 1);
            if (fx.Count == 0) beats = 0;
            tmode = beats > 0 ? SKCh.MODE_BEAT : SKCh.MODE_CONST;
            bool stepped = SKMath.DimStepped(mode);
            foreach (var f in fx)
            {
                var t = new List<float>(); var v = new List<float>();
                if (beats <= 0) { t.Add(0); t.Add(1); v.Add(1); v.Add(1); }
                else if (stepped)
                {
                    // 段階的に変わるもの：変わる瞬間（拍の 1/20 刻み）だけキーを打つ
                    int steps = Mathf.RoundToInt((float)(beats * 20));
                    float last = -1;
                    for (int i = 0; i <= steps; i++)
                    {
                        double b = beats * i / steps;
                        float d = SKMath.Dim(mode, b + 1e-4, f.i, f.n);
                        if (i == 0 || i == steps || Math.Abs(d - last) > 1e-4f) { t.Add((float)(b * spb)); v.Add(d); last = d; }
                    }
                }
                else
                {
                    int N = Mathf.Max(8, Mathf.CeilToInt((float)(beats * spb * FPS)));
                    for (int i = 0; i <= N; i++) { double b = beats * i / N; t.Add((float)(b * spb)); v.Add(SKMath.Dim(mode, b, f.i, f.n)); }
                }
                var curve = stepped ? Stepped(t, v) : Linear(t, v);
                foreach (var r in new[] { f.beamR, f.lensR })
                    if (r != null) clip.SetCurve(P(ctx, r.transform), r.GetType(), "material._Intensity", curve);
            }
            return SaveClip(ctx, clip, true);
        }

        static AnimationClip ConstRendererClip(SKContext ctx, string name, IEnumerable<Renderer> rs, string prop, float v, IEnumerable<Renderer> rs2 = null, string prop2 = null, float v2 = 0f)
        {
            var clip = new AnimationClip { name = name, frameRate = FPS };
            foreach (var r in rs) clip.SetCurve(P(ctx, r.transform), r.GetType(), "material." + prop, Const(v, 1));
            if (rs2 != null) foreach (var r in rs2) clip.SetCurve(P(ctx, r.transform), r.GetType(), "material." + prop2, Const(v2, 1));
            return SaveClip(ctx, clip, true);
        }

        static AnimationClip StrobeClip(SKContext ctx, IEnumerable<Renderer> rs, IEnumerable<Renderer> strobeR)
        {
            var clip = new AnimationClip { name = "Master_Strobe", frameRate = 60 };
            var t = new List<float>(); var v = new List<float>();
            const float dur = 1.6f, per = 1f / 13f;
            for (float x = 0; x < dur; x += per) { t.Add(x); v.Add(1); t.Add(x + per * 0.4f); v.Add(0); }
            t.Add(dur); v.Add(1);
            var curve = Stepped(t, v);
            foreach (var r in rs) clip.SetCurve(P(ctx, r.transform), r.GetType(), "material._Master", curve);
            // ストロボ／ブラインダーは全開で光らせて、_Master の点滅でチカチカさせる（ブラウザ版の strobeTrig）
            foreach (var r in strobeR) clip.SetCurve(P(ctx, r.transform), r.GetType(), "material._StrobeTrig", Const(1f, dur));
            return SaveClip(ctx, clip, false);
        }

        // ------------------------------------------------------------------ レーザー

        static AnimationClip LaserClip(SKContext ctx, List<SKFixture> lasers, string mode, double spb, out int tmode)
        {
            var clip = new AnimationClip { name = "Laser_" + mode, frameRate = FPS };
            double beats = SKMath.LaserPeriodBeats(mode);
            tmode = beats > 0 ? SKCh.MODE_BEAT : SKCh.MODE_CONST;
            foreach (var f in lasers)
            {
                int count = f.laserT.Count;
                for (int j = 0; j < count; j++)
                {
                    string path = P(ctx, f.laserT[j]);
                    var r = f.laserR[j];
                    if (beats <= 0)
                    {
                        var q = Quaternion.FromToRotation(Vector3.up, ctx.Conv(SKMath.LaserDir("fan", 0, f.i, j, count, f.laserSpread, f.laserTilt)));
                        SetRot(clip, path, new[] { 0f, 1f }, new[] { q, q });
                        clip.SetCurve(P(ctx, r.transform), r.GetType(), "material._Intensity", Const(0, 1));
                        continue;
                    }
                    int N = Mathf.Max(8, Mathf.CeilToInt((float)(beats * spb * FPS)));
                    var t = Times(N, beats * spb / N);
                    var qs = new List<Quaternion>(N + 1);
                    for (int i = 0; i <= N; i++)
                        qs.Add(Quaternion.FromToRotation(Vector3.up, ctx.Conv(SKMath.LaserDir(mode, beats * i / N, f.i, j, count, f.laserSpread, f.laserTilt))));
                    SetRot(clip, path, t, qs);
                    clip.SetCurve(P(ctx, r.transform), r.GetType(), "material._Intensity", Const(1, 1));
                }
            }
            clip.EnsureQuaternionContinuity();
            return SaveClip(ctx, clip, true);
        }

        /// <summary>カラーウォッシュ ON/OFF。奥の1灯はブラウザ版と同じく OFF でもうっすら残る（0.3 / 0.8）</summary>
        static AnimationClip WashClip(SKContext ctx, bool on)
        {
            var clip = new AnimationClip { name = on ? "Wash_On" : "Wash_Off", frameRate = FPS };
            foreach (var r in ctx.washR)
                clip.SetCurve(P(ctx, r.transform), r.GetType(), "material._On", Const(on ? 1f : r == ctx.washBack ? 0.375f : 0f, 1));
            return SaveClip(ctx, clip, true);
        }

        // ------------------------------------------------------------------ 演者ダミー

        /// <summary>
        /// ブラウザ版の演者ダミー（50_assets.js の performer）のダンスを移植。8拍で1周するループにする。
        /// three の回転（Euler XYZ）を計算してから、GLB を取り込んだときの座標の向きに変換する。
        /// </summary>
        static AnimationClip PerfClip(SKContext ctx, List<SKUnit> perfs, double spb, out int danced)
        {
            var clip = new AnimationClip { name = "Perf_Dance", frameRate = FPS };
            const double beats = 8;
            double T = beats * spb;
            int N = Mathf.Max(16, Mathf.CeilToInt((float)(T * FPS)));
            var times = Times(N, T / N);
            // 時間（秒）で動くところ（手を振る・立ちの呼吸）も、ループの継ぎ目が合うように周波数を少しだけ丸める
            double w7 = 2 * Math.PI * Math.Max(1, Math.Round(T * 7 / (2 * Math.PI))) / T;
            double w15 = 2 * Math.PI * Math.Max(1, Math.Round(T * 1.5 / (2 * Math.PI))) / T;
            danced = 0;
            foreach (var u in perfs)
            {
                var body = SKBuilder.FindDeep(u.t, "PerfBody");
                if (body == null) continue;
                var skirt = SKBuilder.FindDeep(u.t, "PerfSkirt");
                var arm0 = SKBuilder.FindDeep(u.t, "PerfArm0");
                var arm1 = SKBuilder.FindDeep(u.t, "PerfArm1");
                var tails = new List<Transform>();
                foreach (var tn in new[] { "PerfTail0", "PerfTail1" }) { var t = SKBuilder.FindDeep(u.t, tn); if (t != null) tails.Add(t); }
                string d = J.S(u.p, "dance", "bounce");
                bool mic = J.B(u.p, "mic", true);
                var pos = J.L(u.asset, "pos");
                double ph = SKMath.Hash1(J.D(pos.Count > 0 ? pos[0] : null) * 3.1 + J.D(pos.Count > 2 ? pos[2] : null));

                var bodyP = new List<Vector3>(); var bodyQ = new List<Quaternion>(); var a0 = new List<Quaternion>(); var a1 = new List<Quaternion>();
                var skQ = new List<Quaternion>(); var tlQ = tails.Select(_ => new List<Quaternion>()).ToList();
                for (int i = 0; i <= N; i++)
                {
                    double tau = T * i / N, b = tau / spb + ph, PI = Math.PI;
                    double y = 0, ry = 0, rz = 0, a0x = mic ? -1.1 : 0, a0z, a1x = 0, a1z;
                    if (d == "bounce")
                    {
                        y = Math.Abs(Math.Sin(b * PI)) * 0.06; ry = Math.Sin(b * PI * 0.5) * 0.35;
                        a0z = mic ? -0.35 : -2.4 + Math.Sin(b * PI) * 0.4;
                        a1x = -0.4; a1z = 2.5 + Math.Sin(b * PI + 1) * 0.35;
                    }
                    else if (d == "sway")
                    {
                        rz = Math.Sin(b * PI * 0.5) * 0.08; ry = Math.Sin(b * PI * 0.25) * 0.2;
                        a0z = mic ? -0.3 : -0.5 + Math.Sin(b * PI * 0.5) * 0.3;
                        a1z = 0.5 + Math.Sin(b * PI * 0.5) * 0.3;
                    }
                    else if (d == "wave")
                    {
                        ry = Math.Sin(b * PI * 0.25) * 0.15;
                        a0z = mic ? -0.3 : -0.2;
                        a1z = 2.6 + Math.Sin(tau * w7) * 0.35;
                    }
                    else
                    {
                        y = Math.Sin(tau * w15) * 0.005;
                        a0z = mic ? -0.3 : -0.15; a1z = 0.15;
                    }
                    bodyP.Add(ctx.Conv(new Vector3(0, (float)y, 0)));
                    bodyQ.Add(ctx.ConvQ(EulerXYZ(0, ry, rz)));
                    a0.Add(ctx.ConvQ(EulerXYZ(a0x, 0, a0z)));
                    a1.Add(ctx.ConvQ(EulerXYZ(a1x, 0, a1z)));
                    skQ.Add(ctx.ConvQ(EulerXYZ(0, 0, -rz * 0.8 + Math.Sin(b * PI) * 0.03)));
                    for (int k = 0; k < tails.Count; k++)
                    {
                        float tx = ctx.Conv(tails[k].localPosition).x;   // three での左右（左なら +、右・まん中なら −）
                        double tz = Math.Sin(b * PI + 0.6) * 0.18 * (d == "idle" ? 0.2 : 1) * (tx < 0 ? 1 : -1);
                        tlQ[k].Add(ctx.ConvQ(EulerXYZ(0, 0, tz)));
                    }
                }
                string bp = P(ctx, body);
                clip.SetCurve(bp, typeof(Transform), "localPosition.x", Linear(times, bodyP.Select(v => v.x).ToList()));
                clip.SetCurve(bp, typeof(Transform), "localPosition.y", Linear(times, bodyP.Select(v => v.y).ToList()));
                clip.SetCurve(bp, typeof(Transform), "localPosition.z", Linear(times, bodyP.Select(v => v.z).ToList()));
                SetRot(clip, bp, times, bodyQ);
                if (arm0 != null) SetRot(clip, P(ctx, arm0), times, a0);
                if (arm1 != null) SetRot(clip, P(ctx, arm1), times, a1);
                if (skirt != null) SetRot(clip, P(ctx, skirt), times, skQ);
                for (int k = 0; k < tails.Count; k++) SetRot(clip, P(ctx, tails[k]), times, tlQ[k]);
                danced++;
            }
            clip.EnsureQuaternionContinuity();
            return SaveClip(ctx, clip, true);
        }

        /// <summary>three.js の Euler（XYZ 順・ラジアン）→ 四元数（three の座標のまま）</summary>
        static Quaternion EulerXYZ(double x, double y, double z)
        {
            var qx = new Quaternion((float)Math.Sin(x / 2), 0, 0, (float)Math.Cos(x / 2));
            var qy = new Quaternion(0, (float)Math.Sin(y / 2), 0, (float)Math.Cos(y / 2));
            var qz = new Quaternion(0, 0, (float)Math.Sin(z / 2), (float)Math.Cos(z / 2));
            return qx * qy * qz;
        }

        // ------------------------------------------------------------------ ネオンの回転・ふわふわ

        /// <summary>リモコンで切り替えない、ずっと流れるだけのレイヤー（トリガーなし）</summary>
        static void LoopLayer(AnimatorController ctrl, string name, AnimationClip clip, ref bool first)
        {
            var sm = AddLayer(ctrl, name, ref first, out _);
            var st = sm.AddState(name, new Vector3(300, 60, 0));
            st.motion = clip;
            st.writeDefaultValues = false;
            sm.defaultState = st;
        }

        static AnimationClip NeonSpinClip(SKContext ctx)
        {
            var clip = new AnimationClip { name = "Neon_Spin", frameRate = FPS };
            float period = (float)(2 * Math.PI / 0.6);
            const int N = 48;
            var t = Times(N, period / N);
            float sign = ctx.flipX ? -1f : 1f;   // three の Z 回転は、X反転で取り込むと向きが逆になる
            foreach (var n in ctx.neonSpin)
            {
                var qs = new List<Quaternion>();
                for (int i = 0; i <= N; i++) qs.Add(n.localRotation * Quaternion.Euler(0, 0, sign * 360f * i / N));
                SetRot(clip, P(ctx, n), t, qs);
            }
            clip.EnsureQuaternionContinuity();
            return SaveClip(ctx, clip, true);
        }

        static AnimationClip NeonFloatClip(SKContext ctx)
        {
            var clip = new AnimationClip { name = "Neon_Float", frameRate = FPS };
            float period = (float)(2 * Math.PI / 1.2);
            const int N = 48;
            var t = Times(N, period / N);
            foreach (var kv in ctx.neonFloat)
            {
                var n = kv.Key; var p0 = n.localPosition; string path = P(ctx, n);
                var ys = new List<float>();
                for (int i = 0; i <= N; i++) ys.Add(p0.y + Mathf.Sin(2f * Mathf.PI * i / N + kv.Value) * 0.15f);
                clip.SetCurve(path, typeof(Transform), "localPosition.x", Const(p0.x, period));
                clip.SetCurve(path, typeof(Transform), "localPosition.y", Linear(t, ys));
                clip.SetCurve(path, typeof(Transform), "localPosition.z", Const(p0.z, period));
            }
            return SaveClip(ctx, clip, true);
        }

        /// <summary>照明ぜんぶ「ストロボ」と同じ点滅（13 回/秒・1.6 秒）</summary>
        static AnimationCurve StrobeCurve(float on)
        {
            var t = new List<float>(); var v = new List<float>();
            const float dur = 1.6f, per = 1f / 13f;
            for (float x = 0; x < dur; x += per) { t.Add(x); v.Add(on); t.Add(x + per * 0.4f); v.Add(0); }
            t.Add(dur); v.Add(on);
            return Stepped(t, v);
        }

        // ------------------------------------------------------------------ ミラーボール

        static AnimationClip SpinClip(SKContext ctx, List<Transform> balls, float period)
        {
            var clip = new AnimationClip { name = "Ambient_Spin", frameRate = FPS };
            const int N = 48;
            var t = Times(N, period / N);
            foreach (var b in balls)
            {
                var qs = new List<Quaternion>();
                for (int i = 0; i <= N; i++) qs.Add(b.localRotation * Quaternion.Euler(0, 360f * i / N, 0));
                SetRot(clip, P(ctx, b), t, qs);
            }
            clip.EnsureQuaternionContinuity();
            return SaveClip(ctx, clip, true);
        }

        // ------------------------------------------------------------------ ステージカメラ

        static void BakeCamera(SKContext ctx, int k)
        {
            var cam = ctx.cams[k];
            var camsJ = J.O(ctx.state, "cams", k == 0 ? "cam1" : "cam2");
            float sp = J.F(camsJ, "speed", 1f), dist = J.F(camsJ, "dist", k == 0 ? 8f : 3.5f), height = J.F(camsJ, "height", 1.6f), fov = J.F(camsJ, "fov", 30f);
            float depth = J.F(J.O(ctx.state, "stage"), "depth", 8f);
            string cur = J.S(camsJ, "mode", "auto");
            // アップで追う人（ブラウザ版の演者ダミー）：その人の顔で焼き込む。UdonSharp 版では登録した人の顔を実行中に追う
            int target = J.I(camsJ, "target", -1);
            var heads = J.L(ctx.data, "performerHeads");
            Vector3? face = target >= 0 && target < heads.Count ? (Vector3?)J.V3(heads[target]) : null;
            var ctrl = NewController(ctx, "SK_Cam" + (k + 1) + ".controller");
            AddTempoParams(ctrl, false);
            bool first = true;
            var sm = AddLayer(ctrl, "Shot", ref first, out int li);
            var ch = Chan(ctx, k == 0 ? SKCh.CAM1 : SKCh.CAM2, k + 1, li, 0f);
            int slot = 0;
            foreach (var mode in SKMath.CamModes)
            {
                var clip = CamClip(ctx, k, mode, sp, dist, height, fov, depth, face);
                // オート（2小節ごとにショットを切り替え）は拍に合わせる、ほかは時間（秒）で動く
                int tm = mode == "auto" ? SKCh.MODE_BEAT : SKMath.ShotPeriod(mode, sp) > 0 ? SKCh.MODE_TIME : SKCh.MODE_CONST;
                var st = AddState(ctrl, sm, "Shot_" + mode, clip, 0f, slot++, ch, tm, TempoParam);
                if (mode == cur) { sm.defaultState = st; ch.def = ch.names.Count - 1; }
            }
            var anim = cam.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ctx.camAnimators[k] = anim;
        }

        static AnimationClip CamClip(SKContext ctx, int k, string mode, float sp, float dist, float height, float fov, float depth, Vector3? face)
        {
            const float CFPS = 15f;
            var clip = new AnimationClip { name = "Cam" + (k + 1) + "_" + mode, frameRate = CFPS };
            var t = new List<float>(); var pos = new List<Vector3>(); var rot = new List<Quaternion>(); var fovs = new List<float>(); var step = new List<bool>();
            Action<string, double, double, bool> sample = (m, time, loopT, cutAfter) =>
            {
                Vector3 p, l;
                bool faceUp = m == "closeup" && face.HasValue;
                if (faceUp) SKMath.FaceShot(time, face.Value, sp, SKMath.FaceDist(dist), loopT, out p, out l);
                else SKMath.ShotPose(m, time, ctx.focusThree, sp, dist, height, depth, loopT, out p, out l);
                Vector3 pw = ctx.ToWorld(p), lw = ctx.ToWorld(l);
                var root = ctx.root.transform;
                pos.Add(root.InverseTransformPoint(pw));
                var dir = lw - pw;
                rot.Add(Quaternion.Inverse(root.rotation) * Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.forward, Vector3.up));
                fovs.Add(faceUp ? SKMath.FaceFov(fov, dist) : m == "closeup" ? fov * 0.75f : fov);
                step.Add(cutAfter);
            };
            if (mode == "auto")
            {
                // ブラウザ版と同じ：2小節（8拍）ごとにショットを切り替える。7ショットで1周
                double seg = 8 * 60.0 / ctx.bpm;
                int per = Mathf.Max(2, Mathf.CeilToInt((float)(seg * CFPS)));
                for (int s = 0; s < 7; s++)
                {
                    string m = SKMath.AutoShots[(s * 5 + k * 3) % SKMath.AutoShots.Length];
                    for (int i = 0; i < per; i++) { double time = s * seg + seg * i / per; t.Add((float)time); sample(m, time, 0, i == per - 1); }
                }
                double end = 7 * seg; t.Add((float)end);
                sample(SKMath.AutoShots[(k * 3) % SKMath.AutoShots.Length], end, 0, false);
            }
            else
            {
                double T = SKMath.ShotPeriod(mode, sp);
                if (T <= 0) { t.Add(0); sample(mode, 0, 0, false); t.Add(1); sample(mode, 0, 0, false); }
                else
                {
                    int N = Mathf.Max(8, Mathf.CeilToInt((float)(T * CFPS)));
                    for (int i = 0; i <= N; i++) { double time = T * i / N; t.Add((float)time); sample(mode, time, T, false); }
                }
            }
            clip.SetCurve("", typeof(Transform), "localPosition.x", Linear(t, pos.Select(v => v.x).ToList(), step));
            clip.SetCurve("", typeof(Transform), "localPosition.y", Linear(t, pos.Select(v => v.y).ToList(), step));
            clip.SetCurve("", typeof(Transform), "localPosition.z", Linear(t, pos.Select(v => v.z).ToList(), step));
            // 回転は符号をそろえてから（四元数の q と -q は同じ向き）
            for (int i = 1; i < rot.Count; i++) if (Quaternion.Dot(rot[i - 1], rot[i]) < 0) { var q = rot[i]; rot[i] = new Quaternion(-q.x, -q.y, -q.z, -q.w); }
            SetRot(clip, "", t, rot, step);
            clip.SetCurve("", typeof(Camera), "field of view", Linear(t, fovs, step));
            return SaveClip(ctx, clip, true);
        }
    }
}
