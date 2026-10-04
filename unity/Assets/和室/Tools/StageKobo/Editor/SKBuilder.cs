using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Washitsu.StageKobo.Editor
{
    public enum SKAxisMode { Auto, FlipX, FlipZ }

    public class SKBuildSettings
    {
        public GameObject glb;
        public TextAsset json;
        public string outFolder = "Assets/和室/StageKobo_Generated";
        public SKAxisMode axis = SKAxisMode.Auto;
        public bool led = true, cams = true, beams = true, anim = true, fx = true, remote = true, colliders = true, glitter = true, pinLight = true;
        public bool wash = true, crowd = true, performers = true, probe = true;
        /// <summary>ブラウザ版の VJ（デッキA/B・エフェクト）を LED モニター・背景 LED に映す</summary>
        public bool vj = true;
        /// <summary>ステージ裏の操作パネル（手前と同じ・プレビュー付き。UdonSharp 版のときだけ）</summary>
        public bool desks = true;
        /// <summary>UdonSharp 版のリモコン（全員に同期・テンポ変更）。UdonSharp が無いときは無視される</summary>
        public bool udon = true;
        /// <summary>曲の音（AudioLink）に反応する：照明・LED・ウォッシュ・客席・VJ</summary>
        public bool audioLink = true;
        /// <summary>音に反応する強さ（0〜1）</summary>
        public float alAmount = 0.7f;
        /// <summary>シーンの YamaPlayer の音を AudioLink に送るように設定する</summary>
        public bool yama = true;
        /// <summary>シーンの YamaPlayer の音を、ステージ（スピーカー）から会場ぜんたいに届くようにする</summary>
        public bool yamaSound = true;
        /// <summary>演者を照らすスポットライト（本物の Spot Light 2 灯。UdonSharp 版は登録した人を追う）</summary>
        public bool spots = true;
        /// <summary>スポットライトの明るさ（1 = ふつう）</summary>
        public float spotPower = 1f;
    }

    /// <summary>
    /// リモコンの「チャンネル」番号。Runtime/StageKoboController.cs の CH_～ と同じ番号にすること。
    /// </summary>
    public static class SKCh
    {
        public const int MOVE = 0, COLOR = 1, PALETTE = 2, DIM = 3, LASER = 4, MASTER = 5, MONITOR = 6, WASH = 7, PEN = 8, CAM1 = 9, CAM2 = 10, PERF = 11, AMBIENT = 12, MONITOR2 = 13, N = 14;
        // MONITOR = メインのモニター、MONITOR2 = サブのモニター（ライブ映像 / VJ映像を別々に切り替える）
        // ACT_UP：カメラのアップで顔を追う人に「押した人」を登録（val 0 = カメラ1、1 = カメラ2、3 = 外す）
        // ACT_AUDIO：音に反応（AudioLink）の ON / OFF（val 1 = ON、0 = OFF）
        // ACT_SPOT：演者を照らすスポットに「押した人」を登録（val 0 = スポット1、1 = スポット2、3 = 固定に戻す）／ACT_SPOTON：スポット ON / OFF
        public const int ACT_BPM = 20, ACT_SPEED = 21, ACT_STROBE = 22, ACT_FX = 23, ACT_UP = 24, ACT_AUDIO = 25, ACT_SPOT = 26, ACT_SPOTON = 27, ACT_HOME = 28;
        public const int MODE_CONST = 0, MODE_BEAT = 1, MODE_MOVE = 2, MODE_TIME = 3;
    }

    /// <summary>Animator のレイヤー1つ分（UdonSharp 版のコントローラーに渡す情報）</summary>
    public class SKChannel
    {
        public int ch, anim, layer, def;
        public float fade;
        public List<string> names = new List<string>();
        public List<float> lens = new List<float>();
        public List<int> modes = new List<int>();
        public int IndexOf(string name) { return names.IndexOf(name); }
    }

    /// <summary>GLB の中の「アセット1個分」（ミラー・配列で増えたものも1個ずつ）</summary>
    public class SKUnit
    {
        public string assetId, type, name;
        public Dictionary<string, object> asset, p;
        public Transform t;
        public bool mirrored;
        public int idx;
        public Vector3 threePos;
    }

    /// <summary>動く灯体（ムービング・パー・ピンスポ・レーザー）</summary>
    public class SKFixture
    {
        public string kind;      // mh / par / pin / laser / wash（カラーウォッシュの光だまり）
        public SKUnit u;
        public Transform pan, tilt;
        public Renderer beamR, lensR;
        public bool hang, follow = true;
        public int i, n;
        public double xn;
        public float power = 1f, half = 2.2f, len = 20f;
        public List<Transform> laserT = new List<Transform>();
        public List<Renderer> laserR = new List<Renderer>();
        public List<Renderer> cellR = new List<Renderer>();          // ストロボの光る面（StrobeCell）
        public float laserSpread = 50f, laserTilt = 8f;
    }

    public class SKContext
    {
        public SKBuildSettings cfg;
        public Dictionary<string, object> data, state;
        public string dir, stageName;
        public GameObject root;
        public Transform model;
        public bool flipX = true;
        public StringBuilder log = new StringBuilder();
        public List<SKUnit> units = new List<SKUnit>();
        public Dictionary<Transform, SKUnit> unitByT = new Dictionary<Transform, SKUnit>();
        public List<SKFixture> fixtures = new List<SKFixture>();
        public RenderTexture[] camRT = new RenderTexture[2];
        public Camera[] cams = new Camera[2];
        public float[] camAspect = { 16f / 9f, 16f / 9f };           // ステージカメラの画角の縦横比（RenderTexture の縦横比）
        public Animator lightAnimator;
        public Animator[] camAnimators = new Animator[2];
        public List<Renderer> camMonitors = new List<Renderer>();     // カメラ映像のモニター：メイン
        public List<Renderer> camMonitors2 = new List<Renderer>();    // カメラ映像のモニター：サブ
        public List<ParticleSystem> sparks = new List<ParticleSystem>(), confetti = new List<ParticleSystem>(), smoke = new List<ParticleSystem>();
        public List<Transform> mirrorBalls = new List<Transform>();
        public List<Renderer> paletteExtra = new List<Renderer>();   // パレットの色に追従するもの（ペンライト・客席のふち・奥のウォッシュ）
        public List<Renderer> penCueR = new List<Renderer>();        // 「照明の色に合わせる」ペンライト（リモコンで白・虹に切り替え）
        public List<Renderer> washR = new List<Renderer>();          // カラーウォッシュ（ON/OFF）
        public Renderer washBack;
        public SKChannel[] channels = new SKChannel[SKCh.N];
        public bool udon;                                            // UdonSharp 版で組み立てる
        public float bpm = 128f, haze = 0.7f;
        public SKCue live;
        public List<SKCue> scenes = new List<SKCue>();
        public Vector3 focusThree, centerThree;
        public List<Vector3> performersThree = new List<Vector3>();
        public Mesh coneMesh, laserMesh;
        public List<SKLed> leds = new List<SKLed>();                 // LED の面（VJ 映像を貼る候補）
        public SKVJInfo vj;                                          // VJ（JSON に VJ の情報があるとき）
        public Texture vjOutput;
        public Component controller;                                 // UdonSharp 版のコントローラー（StageKoboController）
        public bool audio;                                           // 音に反応（AudioLink）する（リモコン・卓に ON/OFF を出す）
        public List<Light> spotLights = new List<Light>();           // 演者を照らすスポットライト（SKSpots）
        public List<Vector3> spotAims = new List<Vector3>();         // 固定のときに照らす場所（ワールド）
        public List<Renderer> spotBeams = new List<Renderer>();      // スポットの見た目のビーム
        public List<Transform> neonSpin = new List<Transform>();     // ネオンの「回転」（Animator で回す）
        public List<KeyValuePair<Transform, float>> neonFloat = new List<KeyValuePair<Transform, float>>();   // ネオンの「ふわふわ」（位相）
        public List<SKFxCustom> fxCustom = new List<SKFxCustom>();   // 追加の特効（特効の設定ウィンドウ）
        public bool fxBuiltin = true;                                // 最初からある特効（スパーク・紙吹雪・スモーク）のボタンを出す

        public Vector3 Conv(Vector3 v) { return flipX ? new Vector3(-v.x, v.y, v.z) : new Vector3(v.x, v.y, -v.z); }
        public Vector3 ToWorld(Vector3 three) { return model.TransformPoint(Conv(three)); }
        public Vector3 DirToWorld(Vector3 three) { return model.TransformDirection(Conv(three)); }
        /// <summary>three の回転（四元数）→ GLB を取り込んだあとのローカル回転（鏡写しなので軸の向きが変わる）</summary>
        public Quaternion ConvQ(Quaternion q) { return flipX ? new Quaternion(q.x, -q.y, -q.z, q.w) : new Quaternion(-q.x, -q.y, q.z, q.w); }
        public void Log(string s) { log.AppendLine(s); }
    }

    /// <summary>
    /// すてーじ工房のインポーター本体。GLB（取り込み済みのモデル）＋ Unity用JSON からステージを組み立てる。
    /// 1. GLB を置く → 2. 軸の向きを判定 → 3. LED・発光・ビームのマテリアル → 4. ステージカメラ
    /// → 5. 特効パーティクル → 6. 演出を Animator に焼き込み → 7. ワールド用リモコン
    /// </summary>
    public static class SKBuilder
    {
        public static string Build(SKBuildSettings cfg)
        {
            if (cfg.glb == null || cfg.json == null) throw new Exception("GLB と Unity用JSON の両方を指定してください");
            var data = SKJson.Parse(cfg.json.text) as Dictionary<string, object>;
            if (data == null || J.S(data, "format") != "stagekobo-unity") throw new Exception("すてーじ工房の「Unity用JSON」ではありません（*.unity.json を指定してください）");
            if (J.I(data, "version", 1) < 2) throw new Exception("JSON が古い形式です。すてーじ工房 v0.2 以降で GLB と JSON を書き出し直してください");

            var ctx = new SKContext { cfg = cfg, data = data, state = J.O(data, "state") };
            ctx.stageName = Sanitize(J.S(data, "name", "Stage"));
            ctx.dir = cfg.outFolder.Replace('\\', '/').TrimEnd('/') + "/" + ctx.stageName;
            foreach (var sub in new[] { "", "/Materials", "/Textures", "/Meshes", "/Animation" }) SKAssets.EnsureFolder(ctx.dir + sub);

            SKFx.Read(ctx);   // 追加の特効（古いステージを消す前に読む）

            string rootName = "StageKobo_" + ctx.stageName;
            var old = GameObject.Find(rootName);
            if (old != null)
            {
                if (!EditorUtility.DisplayDialog("すてーじ工房", "シーンに「" + rootName + "」が既にあります。作り直しますか？\n（古い方は削除されます。Ctrl+Z で戻せます）", "作り直す", "やめる"))
                    return "中止しました。";
                Undo.DestroyObjectImmediate(old);
            }

            try
            {
                EditorUtility.DisplayProgressBar("すてーじ工房", "GLB を配置しています…", 0.05f);
                ctx.root = new GameObject(rootName);
                Undo.RegisterCreatedObjectUndo(ctx.root, "すてーじ工房 インポート");
                var inst = PrefabUtility.InstantiatePrefab(cfg.glb) as GameObject;
                if (inst == null) inst = (GameObject)UnityEngine.Object.Instantiate(cfg.glb);
                if (PrefabUtility.IsPartOfPrefabInstance(inst)) PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                inst.transform.SetParent(ctx.root.transform, false);
                ctx.model = inst.transform;
                ctx.udon = cfg.udon && cfg.anim && (cfg.remote || cfg.desks) && SKUdon.Ready;
                ctx.audio = cfg.audioLink && cfg.alAmount > 0.001f;
                if (ctx.udon && !SKUdon.PrepareProgramAssets(ctx)) ctx.udon = false;

                ReadState(ctx);
                CollectUnits(ctx);
                DetectAxis(ctx);
                ctx.coneMesh = SKAssets.Save(SKAssets.BuildCone("SK_BeamCone", 28, 0.012f, 1f), ctx.dir + "/Meshes/SK_BeamCone.asset");
                ctx.laserMesh = SKAssets.Save(SKAssets.BuildCone("SK_LaserLine", 6, 1f, 1f), ctx.dir + "/Meshes/SK_LaserLine.asset");

                EditorUtility.DisplayProgressBar("すてーじ工房", "ステージカメラ…", 0.15f);
                if (cfg.cams) BuildCameras(ctx);
                EditorUtility.DisplayProgressBar("すてーじ工房", "マテリアル（LED・発光）…", 0.3f);
                FixMaterials(ctx);
                if (cfg.vj) { EditorUtility.DisplayProgressBar("すてーじ工房", "VJ…", 0.38f); SKVJBuilder.Build(ctx); }
                EditorUtility.DisplayProgressBar("すてーじ工房", "照明（ビーム・レーザー）…", 0.45f);
                if (cfg.beams) BuildFixtures(ctx);
                SKStageLights.Build(ctx);   // LEDバー・ストロボ・ネオン・文字の動き（ブラウザ版と同じ光り方。音にも反応）
                SKSpots.Build(ctx);         // 演者を照らすスポットライト（本物のライト）
                if (cfg.wash) BuildWash(ctx);
                if (cfg.colliders) AddColliders(ctx);
                if (cfg.probe) AddReflectionProbe(ctx);
                EditorUtility.DisplayProgressBar("すてーじ工房", "特効パーティクル…", 0.55f);
                if (cfg.fx) BuildFX(ctx);
                EditorUtility.DisplayProgressBar("すてーじ工房", "演出を Animator に焼き込んでいます…", 0.65f);
                if (cfg.anim) SKAnimBaker.Bake(ctx);
                EditorUtility.DisplayProgressBar("すてーじ工房", "VJ…", 0.9f);
                SKVJBuilder.Finish(ctx);
                // 操作パネル（v0.11〜：手前＝持ち運べる・裏＝プレビュー付き。以前のリモコン・ステージ裏の卓の代わり）
                if ((cfg.remote || cfg.desks) && cfg.anim) { EditorUtility.DisplayProgressBar("すてーじ工房", "操作パネル…", 0.95f); SKPanelBuilder.Build(ctx); }
                EditorUtility.DisplayProgressBar("すてーじ工房", "AudioLink・YamaPlayer…", 0.98f);
                SKAudio.Setup(ctx);

                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(ctx.root.scene);
                Selection.activeGameObject = ctx.root;
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            ctx.Log("完了：" + rootName + "（生成物は " + ctx.dir + "）");
            return ctx.log.ToString();
        }

        // ------------------------------------------------------------------ 読み込み

        static void ReadState(SKContext ctx)
        {
            var L = J.O(ctx.state, "lights");
            ctx.bpm = Mathf.Max(30f, J.F(L, "bpm", 128f));
            ctx.haze = J.F(L, "haze", 0.7f) * (J.S(J.O(ctx.state, "venue"), "type") == "outdoor_day" ? 0.45f : 1f);
            foreach (var c in J.L(L, "cues")) ctx.scenes.Add(SKCue.From(c));
            var live = J.O(L, "live");
            if (live != null) ctx.live = SKCue.From(live);
            else ctx.live = ctx.scenes.Count > 0 ? ctx.scenes[Mathf.Clamp(J.I(L, "cue"), 0, ctx.scenes.Count - 1)].Clone() : new SKCue();
            var st = J.O(ctx.state, "stage");
            ctx.centerThree = J.V3(J.Get(ctx.data, "center"));
            if (ctx.centerThree == Vector3.zero) ctx.centerThree = new Vector3(0, J.F(st, "height", 1.2f) + 1.3f, J.F(J.O(st, "ring"), "z", 1.2f));
            ctx.focusThree = J.V3(J.Get(ctx.data, "focus"));
            if (ctx.focusThree == Vector3.zero) ctx.focusThree = ctx.centerThree;
            foreach (var p in J.L(ctx.data, "performers")) ctx.performersThree.Add(J.V3(p));
            ctx.Log("BPM " + ctx.bpm + "／シーン " + ctx.scenes.Count + " 個／いまの演出：" + ctx.live.pattern + " / " + ctx.live.colorMode + " / " + ctx.live.dim);
        }

        static void CollectUnits(SKContext ctx)
        {
            var assetsT = FindDeep(ctx.model, "ASSETS");
            if (assetsT == null) { ctx.Log("⚠ GLB の中に ASSETS が見つかりません。アセットの演出は作れません"); return; }
            var byId = new Dictionary<string, Dictionary<string, object>>();
            foreach (var a in J.L(ctx.state, "assets")) { var d = a as Dictionary<string, object>; if (d != null) byId[J.S(d, "id")] = d; }
            int miss = 0;
            foreach (var uo in J.L(ctx.data, "units"))
            {
                string rootN = J.S(uo, "root"), node = J.S(uo, "node");
                int wi = J.I(uo, "wrapIndex");
                var r = FindChild(assetsT, rootN);
                if (r == null) { miss++; continue; }   // 非表示（書き出し対象外）など
                Transform t = null;
                if (wi < r.childCount) { var w = r.GetChild(wi); if (w.childCount > 0) t = w.GetChild(0); }
                if (t == null || !NameMatches(t.name, node)) { var alt = FindDeep(r, node); if (alt != null) t = alt; }
                if (t == null) { miss++; continue; }
                var u = new SKUnit
                {
                    assetId = J.S(uo, "assetId"), type = J.S(uo, "type"), name = J.S(uo, "name"), t = t,
                    mirrored = J.B(uo, "mirrored"), idx = J.I(uo, "index"), threePos = J.V3(J.Get(uo, "position")),
                };
                byId.TryGetValue(u.assetId, out u.asset);
                u.p = J.O(u.asset, "p") ?? new Dictionary<string, object>();
                ctx.units.Add(u);
                ctx.unitByT[t] = u;
            }
            ctx.Log("アセット " + ctx.units.Count + " 個を GLB の中で見つけました" + (miss > 0 ? "（" + miss + " 個は GLB に入っていません：非表示・観客除外など）" : ""));
        }

        static void DetectAxis(SKContext ctx)
        {
            if (ctx.cfg.axis != SKAxisMode.Auto) { ctx.flipX = ctx.cfg.axis == SKAxisMode.FlipX; ctx.Log("軸の向き：" + (ctx.flipX ? "X反転" : "Z反転") + "（手動指定）"); return; }
            foreach (var u in ctx.units)
            {
                var p = u.threePos;
                if (Mathf.Abs(p.x) < 0.3f || Mathf.Abs(p.z) < 0.3f) continue;
                Vector3 l = ctx.model.InverseTransformPoint(u.t.position);
                float dx = (l - new Vector3(-p.x, p.y, p.z)).sqrMagnitude, dz = (l - new Vector3(p.x, p.y, -p.z)).sqrMagnitude;
                ctx.flipX = dx <= dz;
                ctx.Log("軸の向き：" + (ctx.flipX ? "X反転（glTFast など）" : "Z反転（UniGLTF など）") + " と判定しました");
                return;
            }
            ctx.flipX = true;
            ctx.Log("⚠ 軸の向きを判定できなかったので X反転 とみなしました（ずれる場合はウィンドウで手動指定）");
        }

        // ------------------------------------------------------------------ ステージカメラ

        static void BuildCameras(SKContext ctx)
        {
            var camsJ = J.O(ctx.state, "cams");
            string res = J.S(camsJ, "res", "mid");
            int w = res == "low" ? 320 : res == "high" ? 768 : 512;
            var poses = J.L(ctx.data, "stageCameras");
            var aspLog = new List<string>();
            for (int k = 0; k < 2; k++)
            {
                var cj = J.O(camsJ, k == 0 ? "cam1" : "cam2");
                // 画角の縦横比：ブラウザ版が書き出した値（v0.7.1〜）。無ければ、そのカメラを映すモニターのいちばん横長のもの。
                // 縦の画角（fov）はそのままで横の範囲だけが変わる。形のちがうモニターは、まん中を切り抜いて映す（映像は伸び縮みしない）
                float asp = k < poses.Count ? J.F(poses[k], "aspect", 0f) : 0f;
                if (asp <= 0f) asp = CamAspectFromMonitors(ctx, k == 0 ? "cam1" : "cam2");
                asp = Mathf.Clamp(asp, 0.25f, 4f);
                int area = w * Mathf.RoundToInt(w * 9f / 16f);   // ピクセル数は 16:9 のときと同じくらい
                float fw = Mathf.Sqrt(area * asp), fh = Mathf.Sqrt(area / asp), sc = Mathf.Min(1f, 2048f / Mathf.Max(fw, fh));
                int rw = Mathf.Max(16, Mathf.RoundToInt(fw * sc)), rh = Mathf.Max(16, Mathf.RoundToInt(fh * sc));
                ctx.camAspect[k] = (float)rw / rh;
                aspLog.Add("カメラ" + (k + 1) + " " + rw + "×" + rh);
                var rt = new RenderTexture(rw, rh, 24, RenderTextureFormat.ARGBHalf) { name = "SK_Cam" + (k + 1), useMipMap = false, antiAliasing = 1 };
                ctx.camRT[k] = SKAssets.Save(rt, ctx.dir + "/Textures/SK_Cam" + (k + 1) + ".renderTexture");
                var go = new GameObject("StageCam" + (k + 1));
                go.transform.SetParent(ctx.root.transform, false);
                var cam = go.AddComponent<Camera>();
                cam.targetTexture = ctx.camRT[k];
                cam.fieldOfView = J.F(cj, "fov", 30f);
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 600f; cam.depth = -10;
                cam.allowHDR = true; cam.allowMSAA = false;
                cam.cullingMask = StageCamMask();
                Vector3 pos = new Vector3(0, 3, 10), look = new Vector3(0, 2, 0);
                if (k < poses.Count) { pos = J.V3(J.Get(poses[k], "position")); look = J.V3(J.Get(poses[k], "lookAt")); }
                go.transform.position = ctx.ToWorld(pos);
                go.transform.rotation = Quaternion.LookRotation(ctx.ToWorld(look) - go.transform.position, Vector3.up);
                ctx.cams[k] = cam;
            }
            ctx.Log("ステージカメラ 2 台と RenderTexture を作りました（" + string.Join("・", aspLog) + "。画角の縦横比は映すモニターに合わせる）");
            if (LayerMask.NameToLayer("PlayerLocal") < 0)
                ctx.Log("⚠ VRChat のレイヤー設定がまだです。VRChat SDK の「Setup Layers for VRChat」を押してから組み立て直すと、ステージカメラに自分のアバター（頭なし）やメニューが映りません");
        }

        /// <summary>そのカメラを映している LED モニターのうち、いちばん横長のものの縦横比（無ければ 16:9）</summary>
        static float CamAspectFromMonitors(SKContext ctx, string key)
        {
            float a = 0f;
            foreach (var u in ctx.units)
                if (u.type == "led_monitor" && J.S(u.p, "src") == key) a = Mathf.Max(a, MonitorAspect(u, 16f / 9f));
            return a > 0f ? a : 16f / 9f;
        }

        /// <summary>モニターの区分（ブラウザ版 monGroup と同じ）：自動 = 左右ミラーで複製したものはサブ</summary>
        public static bool IsSubMonitor(SKUnit u)
        {
            string g = J.S(u.p, "group", "auto");
            return g == "sub" || (g != "main" && J.B(u.asset, "mirror"));
        }

        /// <summary>書き出したときの「ライブ映像 / VJ映像」（v0.7 までの JSON はメイン・サブ共通）</summary>
        public static string MonitorMode(SKContext ctx, bool sub)
        {
            var L = J.O(ctx.state, "lights");
            string main = J.S(L, "monitorMode", "live");
            return sub ? J.S(L, "monitorMode2", main) : main;
        }

        static float MonitorAspect(SKUnit u, float def)
        {
            float mw = J.F(u.p, "w", 0f), mh = J.F(u.p, "h", 0f);
            return mw > 0f && mh > 0f ? mw / mh : def;
        }

        /// <summary>縦横比 panelAsp の画面に、縦横比 texAsp の映像を伸び縮みさせずに貼るための切り抜き（まん中。x, y, 幅, 高さ）</summary>
        public static Vector4 Cover(float panelAsp, float texAsp)
        {
            if (panelAsp > texAsp) { float s = texAsp / panelAsp; return new Vector4(0, (1 - s) / 2, 1, s); }
            float s2 = panelAsp / texAsp; return new Vector4((1 - s2) / 2, 0, s2, 1);
        }

        /// <summary>
        /// ステージカメラに映すレイヤー（VRChat のレイヤー設定のとき）。映さないもの：
        ///   5 UI          … ほかの人の VRChat カメラ（レンズ）・スキャンの光・パーソナルミラー
        ///  10 PlayerLocal … 自分の一人称用のアバター（頭が消えている）
        ///  12 UiMenu      … ネームプレート・ほかの人のカメラの名札
        ///  19 InternalUI  … 自分の VRChat カメラ（レンズ・カメラの UI）・メニュー・HUD
        ///  20 HardwareObjects … コントローラーなど
        /// 自分のアバターは MirrorReflection（18）で頭のある姿が映る（鏡と同じ）。レイヤー設定が無いプロジェクトでは全部映す
        /// </summary>
        public static int StageCamMask()
        {
            int mask = ~0;
            if (LayerMask.NameToLayer("PlayerLocal") != 10) return mask;   // VRChat のレイヤー設定がまだ
            foreach (int l in new[] { 5, 10, 12, 19, 20 }) mask &= ~(1 << l);
            return mask;
        }

        // ------------------------------------------------------------------ マテリアル

        static readonly Regex ReGlow = new Regex(@"^SKGlow_([0-9a-fA-F]{6})_x([0-9.]+)(?:_o([0-9.]+))?(_add)?");
        static readonly Regex ReGlowVC = new Regex(@"^SKGlowVC_x([0-9.]+)");

        static void FixMaterials(SKContext ctx)
        {
            var cache = new Dictionary<string, Material>();
            var texCache = new Dictionary<string, Texture2D>();
            int led = 0, glow = 0, pen = 0, crowd = 0;
            foreach (var r in ctx.model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int k = 0; k < mats.Length; k++)
                {
                    var m = mats[k];
                    if (m == null) continue;
                    string n = m.name.Replace(" (Instance)", "");
                    Material nm = null;
                    if (n.StartsWith("SKPenlight_")) { nm = Penlight(ctx, r, n, cache); if (nm != null) pen++; }
                    else if (n.StartsWith("SKCrowd_") && ctx.cfg.crowd) { nm = Crowd(ctx, r, n, cache); if (nm != null) crowd++; }
                    else if (n.StartsWith("SKGlowVC_")) { nm = GlowVC(ctx, n, cache); glow++; }
                    else if (n.StartsWith("SKGlow_")) { nm = Glow(ctx, n, cache); glow++; }
                    else if (n.StartsWith("LED_") && ctx.cfg.led) { nm = Led(ctx, r, n, texCache, led); led++; }
                    if (nm != null) { mats[k] = nm; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
                if (r is MeshRenderer && (r.sharedMaterials.Any(x => x != null && x.shader != null && x.shader.name.StartsWith("StageKobo/"))))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            ctx.Log("発光マテリアル " + glow + " 面・LED " + led + " 面を差し替えました" + (pen > 0 ? "／ペンライト " + pen + " 組（拍で揺れる）" : "") + (crowd > 0 ? "／客席の人 " + crowd + " 組（拍で弾む）" : ""));
        }

        static Material Glow(SKContext ctx, string n, Dictionary<string, Material> cache)
        {
            var m = ReGlow.Match(n);
            if (!m.Success) return null;
            string key = m.Value;
            if (cache.TryGetValue(key, out var hit)) return hit;
            float x = ParseF(m.Groups[2].Value, 2f), o = m.Groups[3].Success ? ParseF(m.Groups[3].Value, 1f) : 1f;
            bool add = m.Groups[4].Success, transparent = m.Groups[3].Success;
            var mat = SKAssets.NewMaterial(SKAssets.ShaderGlow, ctx.dir + "/Materials/Glow_" + m.Groups[1].Value + "_" + x.ToString("0.00", CultureInfo.InvariantCulture) + (transparent ? "_o" + o.ToString("0.00", CultureInfo.InvariantCulture) : "") + (add ? "_add" : "") + ".mat");
            mat.SetColor("_Color", J.Col(m.Groups[1].Value, Color.white));
            mat.SetFloat("_Emission", x);
            mat.SetFloat("_Opacity", o);
            SetBlend(mat, add ? 2 : transparent ? 1 : 0);
            cache[key] = mat;
            return mat;
        }

        static Material GlowVC(SKContext ctx, string n, Dictionary<string, Material> cache)
        {
            var m = ReGlowVC.Match(n);
            if (!m.Success) return null;
            return GlowVCX(ctx, ParseF(m.Groups[1].Value, 1f), cache);
        }

        static Material GlowVCX(SKContext ctx, float x, Dictionary<string, Material> cache)
        {
            string key = "vc_" + x.ToString("0.00", CultureInfo.InvariantCulture);
            if (cache.TryGetValue(key, out var hit)) return hit;
            var mat = SKAssets.NewMaterial(SKAssets.ShaderGlow, ctx.dir + "/Materials/GlowVC_" + x.ToString("0.00", CultureInfo.InvariantCulture) + ".mat");
            mat.SetColor("_Color", Color.white);
            mat.SetFloat("_Emission", x);
            mat.SetFloat("_UseVertexColor", 1f);
            mat.EnableKeyword("_VERTEXCOLOR_ON");
            SetBlend(mat, 0);
            cache[key] = mat;
            return mat;
        }

        static readonly Regex RePen = new Regex(@"^SKPenlight_x([0-9.]+)_l([0-9.]+)");
        static readonly Regex ReCrowd = new Regex(@"^SKCrowd_([0-9a-fA-F]{6})");

        /// <summary>客席のペンライト：拍に合わせて1本ずつ揺れるシェーダー（StageKobo/Penlight）</summary>
        static Material Penlight(SKContext ctx, Renderer r, string n, Dictionary<string, Material> cache)
        {
            var m = RePen.Match(n);
            if (!m.Success) return null;
            if (!ctx.cfg.crowd) return GlowVCX(ctx, ParseF(m.Groups[1].Value, 1.25f), cache);   // 動かさない設定：書き出した色のまま光らせる
            var u = OwnerUnit(ctx, r.transform);
            string id = u != null ? Sanitize(u.assetId) : "x";
            string mode = u != null ? J.S(u.p, "penlight", "cue") : "cue";
            string key = "pen_" + id;
            if (!cache.TryGetValue(key, out var mat))
            {
                mat = SKAssets.NewMaterial(SKAssets.ShaderPenlight, ctx.dir + "/Materials/Penlight_" + id + ".mat");
                mat.SetFloat("_Emission", ParseF(m.Groups[1].Value, 1.25f));
                mat.SetFloat("_Len", ParseF(m.Groups[2].Value, 0.28f));
                mat.SetFloat("_Sway", u != null ? J.F(u.p, "sway", 1f) : 1f);
                mat.SetFloat("_Jump", u != null ? J.F(u.p, "jump", 0.5f) : 0.5f);
                mat.SetFloat("_BPM", ctx.bpm);
                mat.SetFloat("_AxisFlipX", ctx.flipX ? 1f : 0f);
                SetLiveColors(ctx, mat);
                // cue = リモコンの「ペンライト」で切り替え（照明の色・白・虹）、white / rainbow は固定、pastel / custom は書き出した色
                mat.SetFloat("_Mode", mode == "cue" ? PenMode(ctx.live.penlight) : mode == "white" ? 2f : mode == "rainbow" ? 3f : 0f);
                cache[key] = mat;
            }
            if (mode == "cue") { ctx.penCueR.Add(r); ctx.paletteExtra.Add(r); }
            r.shadowCastingMode = ShadowCastingMode.Off;
            return mat;
        }

        public static float PenMode(string livePenlight) { return livePenlight == "white" ? 2f : livePenlight == "rainbow" ? 3f : 1f; }

        /// <summary>客席の人のシルエット：拍で弾む＋ふちがステージの色で光る（StageKobo/Crowd）</summary>
        static Material Crowd(SKContext ctx, Renderer r, string n, Dictionary<string, Material> cache)
        {
            var m = ReCrowd.Match(n);
            if (!m.Success) return null;
            var u = OwnerUnit(ctx, r.transform);
            string id = u != null ? Sanitize(u.assetId) : "x";
            string key = "crowd_" + id;
            if (!cache.TryGetValue(key, out var mat))
            {
                mat = SKAssets.NewMaterial(SKAssets.ShaderCrowd, ctx.dir + "/Materials/Crowd_" + id + ".mat");
                mat.SetColor("_Color", J.Col(m.Groups[1].Value, new Color(0.05f, 0.055f, 0.09f)));
                mat.SetFloat("_Jump", u != null ? J.F(u.p, "jump", 0.5f) : 0.5f);
                mat.SetFloat("_BPM", ctx.bpm);
                mat.SetFloat("_Rim", J.S(J.O(ctx.state, "venue"), "type") == "outdoor_day" ? 0.12f : 0.35f);
                SetLiveColors(ctx, mat);
                cache[key] = mat;
            }
            ctx.paletteExtra.Add(r);
            return mat;
        }

        /// <summary>0 = 不透明、1 = 半透明、2 = 加算</summary>
        public static void SetBlend(Material mat, int mode)
        {
            if (mode == 2) { mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.One); mat.SetFloat("_ZWrite", 0); mat.renderQueue = 3000; }
            else if (mode == 1) { mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); mat.SetFloat("_ZWrite", 0); mat.renderQueue = 3000; }
            else { mat.SetFloat("_SrcBlend", (float)BlendMode.One); mat.SetFloat("_DstBlend", (float)BlendMode.Zero); mat.SetFloat("_ZWrite", 1); mat.renderQueue = 2000; }
        }

        static int Tok(string n, string key, int def)
        {
            var m = Regex.Match(n, "_" + key + "(-?[0-9]+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : def;
        }

        static Material Led(SKContext ctx, Renderer r, string n, Dictionary<string, Texture2D> texCache, int index)
        {
            var cols = Regex.Matches(n, "_c([0-9a-fA-F]{6})");
            var mat = SKAssets.NewMaterial(SKAssets.ShaderLED, ctx.dir + "/Materials/LED_" + index.ToString("00") + "_" + Sanitize(r.name) + ".mat");
            int dx = Mathf.Max(4, Tok(n, "dx", 96)), dy = Mathf.Max(4, Tok(n, "dy", 54));
            mat.SetFloat("_Pattern", Tok(n, "pat", 0));
            mat.SetFloat("_DotsX", dx);
            mat.SetFloat("_DotsY", dy);
            mat.SetFloat("_Aspect", (float)dx / dy);
            mat.SetFloat("_Gap", Tok(n, "gap", 35) / 100f);
            mat.SetFloat("_Round", Tok(n, "rnd", 1));
            mat.SetFloat("_Bright", Tok(n, "br", 100) / 100f);
            mat.SetFloat("_Led", Tok(n, "led", 1));
            mat.SetFloat("_Flip", Tok(n, "fl", 0));
            mat.SetFloat("_BPM", ctx.bpm);
            if (cols.Count > 0) mat.SetColor("_C1", J.Col(cols[0].Groups[1].Value, Color.white));
            if (cols.Count > 1) mat.SetColor("_C2", J.Col(cols[1].Groups[1].Value, Color.white));
            if (cols.Count > 2) mat.SetColor("_C3", J.Col(cols[2].Groups[1].Value, Color.white));
            mat.SetFloat("_Src", 1);   // 既定は VJ パターン
            var info = new SKLed { r = r, m = mat, dx = dx, dy = dy, backdrop = IsUnder(r.transform, "BACKDROP") };
            ctx.leds.Add(info);

            // LEDモニター（アセット）なら、映すもの（カメラ・画像・文字・VJリモコンの映像）を state から決める
            var u = OwnerUnit(ctx, r.transform);
            if (u != null && u.type == "led_monitor")
            {
                string src = J.S(u.p, "src", "pattern");
                if (src == "vj") info.vjAlways = true;   // VJ を組み立てたら VJ 映像。組み立てないときは VJ パターン
                var images = J.O(ctx.data, "images");
                float ma = MonitorAspect(u, (float)dx / dy);
                if ((src == "cam1" || src == "cam2") && ctx.cfg.cams && ctx.camRT[0] != null)
                {
                    int ck = src == "cam1" ? 0 : 1;
                    mat.SetTexture("_MainTex", ctx.camRT[ck]);
                    mat.SetVector("_MainRect", Cover(ma, ctx.camAspect[ck]));   // カメラの絵のまん中を、モニターの縦横比で切り抜く
                    info.sub = IsSubMonitor(u);
                    mat.SetFloat("_Src", MonitorMode(ctx, info.sub) == "vj" ? 1 : 0);
                    (info.sub ? ctx.camMonitors2 : ctx.camMonitors).Add(r);
                    info.camMonitor = true;
                }
                else if (src == "image" || src == "text")
                {
                    string id = src == "image" ? J.S(u.p, "image") : "text_" + u.assetId;
                    if (!texCache.TryGetValue(id, out var tex))
                    {
                        tex = SKAssets.SaveDataUrlTexture(J.S(images, id), ctx.dir + "/Textures/" + Sanitize(id));
                        texCache[id] = tex;
                    }
                    if (tex != null)
                    {
                        mat.SetTexture("_MainTex", tex); mat.SetFloat("_Src", 0);
                        // 画像はまん中を切り抜く（文字はモニターの縦横比で作ってあるのでそのまま）。ドット1個ぶんの平均色を取る
                        var R = src == "image" ? Cover(ma, (float)tex.width / Mathf.Max(1, tex.height)) : new Vector4(0, 0, 1, 1);
                        mat.SetVector("_MainRect", R);
                        mat.SetFloat("_MainLod", Mathf.Max(0f, Mathf.Log(Mathf.Max(1f, tex.width * R.z / dx), 2f) - 0.5f));
                    }
                    else ctx.Log("⚠ " + u.name + " の" + (src == "image" ? "画像" : "文字") + "が JSON に無かったので VJ パターンにしました");
                }
                else if (src == "off") mat.SetFloat("_Src", 2);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ 灯体

        static void BuildFixtures(SKContext ctx)
        {
            var beamCache = new Dictionary<string, Material>();
            Material lensMat = null;
            foreach (var u in ctx.units)
            {
                SKFixture f = null;
                switch (u.type)
                {
                    case "moving_head":
                    {
                        string bt = J.S(u.p, "beam", "beam");
                        f = new SKFixture { kind = "mh", u = u, pan = FindDeep(u.t, "Pan"), tilt = FindDeep(u.t, "Tilt"), hang = J.S(u.p, "mount") == "hang",
                            follow = J.B(u.p, "follow", true), power = J.F(u.p, "power", 1f), half = bt == "wash" ? 13f : bt == "spot" ? 6f : 2.2f, len = 22f };
                        break;
                    }
                    case "par_light":
                        f = new SKFixture { kind = "par", u = u, tilt = FindDeep(u.t, "Head"), follow = J.B(u.p, "follow", true), power = J.F(u.p, "power", 0.8f), half = J.F(u.p, "angle", 12f), len = 16f };
                        break;
                    case "pin_spot":
                        f = new SKFixture { kind = "pin", u = u, pan = FindDeep(u.t, "Pan"), tilt = FindDeep(u.t, "Tilt"), follow = false, power = J.F(u.p, "power", 1f), half = J.F(u.p, "angle", 5f), len = 30f };
                        break;
                    case "laser":
                        f = new SKFixture { kind = "laser", u = u, follow = J.B(u.p, "follow", true), power = J.F(u.p, "power", 1f), laserSpread = J.F(u.p, "spread", 50f), laserTilt = J.F(u.p, "tilt", 8f) };
                        break;
                    case "mirror_ball":
                    {
                        var ball = FindDeep(u.t, "Ball");
                        if (ball != null) ctx.mirrorBalls.Add(ball);
                        break;
                    }
                }
                if (f == null) continue;
                if (f.kind != "laser" && f.tilt == null) { ctx.Log("⚠ " + u.name + " の首（Tilt/Head）が GLB に見つかりません"); continue; }
                ctx.fixtures.Add(f);
            }
            // 種類ごとに左→右で番号（ブラウザ版と同じ）
            foreach (var g in ctx.fixtures.GroupBy(x => x.kind))
            {
                var arr = g.OrderBy(x => x.u.threePos.x).ToList();
                float x0 = arr[0].u.threePos.x, x1 = arr[arr.Count - 1].u.threePos.x, mid = (x0 + x1) / 2, half = Mathf.Max(0.01f, (x1 - x0) / 2);
                for (int i = 0; i < arr.Count; i++) { arr[i].i = i; arr[i].n = arr.Count; arr[i].xn = arr.Count > 1 ? Mathf.Clamp((arr[i].u.threePos.x - mid) / half, -1, 1) : 0; }
            }
            int beams = 0, lasers = 0;
            foreach (var f in ctx.fixtures)
            {
                if (f.kind == "laser") { BuildLaser(ctx, f, beamCache); lasers++; continue; }
                // 首の向き（ピンスポは演者を狙って固定、ほかは「いまの演出」の 0 拍目）
                if (f.kind == "pin") AimPin(ctx, f);
                else if (f.kind == "mh") { double pan, tilt; PoseAt(ctx, f, ctx.live, 0, out pan, out tilt); ApplyPanTilt(f, pan, tilt); }
                var lens = FindDeep(f.tilt, "Lens");
                f.lensR = lens != null ? lens.GetComponent<Renderer>() : null;
                if (f.lensR != null)
                {
                    if (f.kind == "pin")
                    {
                        var lm = SKAssets.NewMaterial(SKAssets.ShaderGlow, ctx.dir + "/Materials/Lens_Pin_" + Sanitize(f.u.assetId) + "_" + f.u.idx + ".mat");
                        lm.SetColor("_Color", J.Col(J.S(f.u.p, "fixed", "#fff4e6"), Color.white)); lm.SetFloat("_Emission", 0.5f + 3f * f.power); SetBlend(lm, 0);
                        f.lensR.sharedMaterial = lm;
                    }
                    else
                    {
                        if (lensMat == null)
                        {
                            lensMat = SKAssets.NewMaterial(SKAssets.ShaderGlow, ctx.dir + "/Materials/Lens_Palette.mat");
                            lensMat.SetFloat("_UsePalette", 1f); lensMat.EnableKeyword("_PALETTE_ON"); SetLiveColors(ctx, lensMat); SetBlend(lensMat, 0);
                        }
                        f.lensR.sharedMaterial = f.follow ? lensMat : FixedLens(ctx, f);
                    }
                    f.lensR.shadowCastingMode = ShadowCastingMode.Off;
                }
                var go = new GameObject("Beam");
                go.transform.SetParent(f.tilt, false);
                go.transform.localPosition = new Vector3(0, f.kind == "pin" ? 0.25f : 0.17f, 0);
                go.AddComponent<MeshFilter>().sharedMesh = ctx.coneMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = BeamMaterial(ctx, f, beamCache);
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                float s = Mathf.Max(0.001f, Mathf.Abs(f.tilt.lossyScale.y));
                float widthMul = f.kind == "mh" ? ctx.live.beam : 1f;
                float r = Mathf.Tan(f.half * widthMul * Mathf.Deg2Rad) * f.len;
                go.transform.localScale = new Vector3(r / s, f.len / s, r / s);
                f.beamR = mr;
                beams++;
                if (f.kind == "pin" && ctx.cfg.pinLight)
                {
                    var lg = new GameObject("PinLight");
                    lg.transform.SetParent(go.transform.parent, false);
                    lg.transform.localPosition = go.transform.localPosition;
                    lg.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                    var light = lg.AddComponent<Light>();
                    light.type = LightType.Spot; light.spotAngle = f.half * 3.2f; light.range = f.len + 5; light.intensity = 2.2f * f.power;
                    light.color = J.Col(J.S(f.u.p, "fixed", "#fff4e6"), Color.white); light.shadows = LightShadows.None;
                }
            }
            ctx.Log("ビーム " + beams + " 本・レーザー " + lasers + " 台を作りました");
        }

        public static void SetLiveColors(SKContext ctx, Material m)
        {
            m.SetColor("_C1", J.Col(ctx.live.c1, Color.white));
            m.SetColor("_C2", J.Col(ctx.live.c2, Color.white));
            m.SetColor("_C3", J.Col(ctx.live.c3, Color.white));
        }

        // ------------------------------------------------------------------ カラーウォッシュ

        /// <summary>
        /// ブラウザ版のカラーウォッシュ（客席側の上から舞台を照らす色付きスポット4灯＋奥から1灯）。
        /// VRChat で本物のライトを5灯置くと重いので、舞台の床と同じ形のメッシュに「加算」の光だまりを描く。
        /// 手前の4灯は照明と同じく「色の付け方」「明るさ」「暗転」に従う。リモコンの「ウォッシュ」で ON/OFF。
        /// </summary>
        static void BuildWash(SKContext ctx)
        {
            var stage = FindDeep(ctx.model, "STAGE");
            var floors = stage == null ? new List<MeshFilter>() : stage.GetComponentsInChildren<MeshFilter>(true).Where(mf => mf.sharedMesh != null && mf.name.StartsWith("StageFloor")).ToList();
            if (floors.Count == 0) { ctx.Log("（舞台の床 StageFloor が GLB に見つからないので、カラーウォッシュは作りませんでした）"); return; }
            var root = ctx.root.transform;
            var ci = new List<CombineInstance>();
            foreach (var mf in floors)
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    ci.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = root.worldToLocalMatrix * mf.transform.localToWorldMatrix });
            var mesh = new Mesh { name = "SK_WashFloor" };
            if (floors.Sum(f => f.sharedMesh.vertexCount) > 60000) mesh.indexFormat = IndexFormat.UInt32;
            try { mesh.CombineMeshes(ci.ToArray(), true, true); }
            catch (Exception e) { ctx.Log("⚠ 舞台の床のメッシュを読めなかったので、カラーウォッシュは作りませんでした（" + e.Message + "）"); return; }
            mesh.RecalculateBounds();
            SKAssets.Save(mesh, ctx.dir + "/Meshes/SK_WashFloor.asset");

            var L = J.O(ctx.state, "lights");
            float wash = J.F(L, "wash", 0.8f);
            var holder = new GameObject("ColorWash");
            holder.transform.SetParent(root, false);
            for (int k = 0; k < 5; k++)
            {
                bool back = k == 4;
                Vector3 lp3 = back ? new Vector3(0, 12, -10) : new Vector3((k - 1.5f) * 6f, 12, 14);
                Vector3 tg3 = back ? new Vector3(0, 1, 4) : new Vector3((k - 1.5f) * 2.5f, 1.2f, 0);
                Vector3 lp = root.InverseTransformPoint(ctx.ToWorld(lp3)), tg = root.InverseTransformPoint(ctx.ToWorld(tg3));
                float ang = back ? 45f : 34f, pen = back ? 0.8f : 0.7f;
                var mat = SKAssets.NewMaterial(SKAssets.ShaderWash, ctx.dir + "/Materials/Wash_" + (back ? "Back" : k.ToString()) + ".mat");
                mat.SetVector("_LightPos", new Vector4(lp.x, lp.y, lp.z, 0));
                var dir = (tg - lp).normalized;
                mat.SetVector("_LightDir", new Vector4(dir.x, dir.y, dir.z, 0));
                mat.SetFloat("_CosOuter", Mathf.Cos(ang * Mathf.Deg2Rad));
                mat.SetFloat("_CosInner", Mathf.Cos(ang * (1f - pen) * Mathf.Deg2Rad));
                mat.SetFloat("_Gain", (back ? 0.1f : 0.09f) * wash);
                SetLiveColors(ctx, mat);
                float slot = 1f, hue = 0f;
                if (!back) SKMath.ColorSlot(ctx.live.colorMode, 0, k, 4, (k - 1.5) / 1.5, out slot, out hue);
                mat.SetFloat("_Slot", slot);
                mat.SetFloat("_Hue", hue);
                mat.SetFloat("_On", ctx.live.wash ? 1f : back ? 0.375f : 0f);
                var go = new GameObject(back ? "Wash_Back" : "Wash_" + k);
                go.transform.SetParent(holder.transform, false);
                go.transform.localPosition = new Vector3(0, 0.004f, 0);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                ctx.washR.Add(mr);
                if (back) { ctx.washBack = mr; ctx.paletteExtra.Add(mr); }
                else ctx.fixtures.Add(new SKFixture { kind = "wash", i = k, n = 4, xn = (k - 1.5) / 1.5, follow = true, beamR = mr });
            }
            ctx.Log("カラーウォッシュ（床の光だまり）を作りました：手前4灯＋奥1灯");
        }

        static Material FixedLens(SKContext ctx, SKFixture f)
        {
            var lm = SKAssets.NewMaterial(SKAssets.ShaderGlow, ctx.dir + "/Materials/Lens_Fixed_" + Sanitize(f.u.assetId) + "_" + f.u.idx + ".mat");
            lm.SetColor("_Color", J.Col(J.S(f.u.p, "fixed", "#ffffff"), Color.white)); lm.SetFloat("_Emission", 2.5f); SetBlend(lm, 0);
            lm.SetFloat("_ALMode", 1f);   // 音に反応：ビームと同じく低音で跳ねる
            return lm;
        }

        static Material BeamMaterial(SKContext ctx, SKFixture f, Dictionary<string, Material> cache)
        {
            float gain = f.kind == "mh" ? (f.half >= 13 ? 0.38f : f.half >= 6 ? 0.75f : 1f) * f.power : f.kind == "par" ? 0.45f * f.power : 0.6f * f.power;
            bool palette = f.kind != "pin" && f.follow;
            string col = palette ? "pal" : J.S(f.u.p, "fixed", "#ffffff").TrimStart('#');
            string key = f.kind + "_" + gain.ToString("0.00", CultureInfo.InvariantCulture) + "_" + f.len.ToString("0", CultureInfo.InvariantCulture) + "_" + col;
            if (cache.TryGetValue(key, out var hit)) return hit;
            var m = SKAssets.NewMaterial(SKAssets.ShaderBeam, ctx.dir + "/Materials/Beam_" + key + ".mat");
            m.SetFloat("_Gain", gain);
            m.SetFloat("_Len", f.len);
            m.SetFloat("_Haze", ctx.haze);
            m.SetFloat("_EndFade", 1f);
            if (palette) SetLiveColors(ctx, m);
            else { var c = J.Col(J.S(f.u.p, "fixed", "#ffffff"), Color.white); m.SetColor("_C1", c); m.SetColor("_C2", c); m.SetColor("_C3", c); }
            cache[key] = m;
            return m;
        }

        static void BuildLaser(SKContext ctx, SKFixture f, Dictionary<string, Material> cache)
        {
            var ap = FindDeep(f.u.t, "Aperture");
            var holder = new GameObject("LaserBeams").transform;
            holder.SetParent(f.u.t, false);
            holder.localPosition = ap != null ? ap.localPosition : ctx.Conv(new Vector3(0, 0.13f, 0.2f));
            string key = "laser_" + f.power.ToString("0.00", CultureInfo.InvariantCulture) + "_" + (f.follow ? "pal" : J.S(f.u.p, "fixed", "#7dff9a").TrimStart('#'));
            if (!cache.TryGetValue(key, out var mat))
            {
                mat = SKAssets.NewMaterial(SKAssets.ShaderBeam, ctx.dir + "/Materials/Beam_" + key + ".mat");
                mat.SetFloat("_Gain", 5f * f.power); mat.SetFloat("_Len", 18f); mat.SetFloat("_Haze", Mathf.Max(0.35f, ctx.haze)); mat.SetFloat("_EndFade", 1f);
                if (f.follow) SetLiveColors(ctx, mat);
                else { var c = J.Col(J.S(f.u.p, "fixed", "#7dff9a"), Color.green); mat.SetColor("_C1", c); mat.SetColor("_C2", c); mat.SetColor("_C3", c); }
                cache[key] = mat;
            }
            int count = Mathf.Clamp(J.I(f.u.p, "count", 8), 1, 32);
            float s = Mathf.Max(0.001f, Mathf.Abs(holder.lossyScale.y));
            string mode = ctx.live.laser == "off" ? "fan" : ctx.live.laser;
            for (int j = 0; j < count; j++)
            {
                var go = new GameObject("Laser_" + j);
                go.transform.SetParent(holder, false);
                go.AddComponent<MeshFilter>().sharedMesh = ctx.laserMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                go.transform.localScale = new Vector3(0.02f / s, 45f / s, 0.02f / s);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, ctx.Conv(SKMath.LaserDir(mode, 0, f.i, j, count, f.laserSpread, f.laserTilt)));
                f.laserT.Add(go.transform); f.laserR.Add(mr);
            }
        }

        /// <summary>three のワールド方向 → この灯体の pan / tilt（度）。ミラー・吊り・回転はここで全部吸収する</summary>
        public static void AimLocal(SKContext ctx, SKFixture f, Vector3 dirThree, out double pan, out double tilt)
        {
            Vector3 dw = ctx.DirToWorld(dirThree);
            Transform parent = f.pan != null ? f.pan.parent : f.tilt.parent;
            Vector3 l = parent.worldToLocalMatrix.MultiplyVector(dw).normalized;
            pan = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
            tilt = Mathf.Atan2(new Vector2(l.x, l.z).magnitude, l.y) * Mathf.Rad2Deg;
        }

        /// <summary>演出の式から、拍 b での向き（目標値）を求める</summary>
        public static void PoseAt(SKContext ctx, SKFixture f, SKCue cue, double b, out double pan, out double tilt, int randomWrap = 0)
        {
            Vector3 dir;
            if (cue.pattern == "center")
            {
                Vector3 from = Three(ctx, f.tilt.position);
                Vector3 target = ctx.performersThree.Count > 0 ? ctx.performersThree[f.i % ctx.performersThree.Count] : ctx.focusThree;
                dir = (target - from).normalized;
            }
            else
            {
                double p, t;
                SKMath.Pattern(cue, b, f.i, f.n, f.xn, randomWrap, out p, out t);
                dir = SKMath.SphDir(p, t, f.hang);
            }
            AimLocal(ctx, f, dir, out pan, out tilt);
        }

        public static void ApplyPanTilt(SKFixture f, double pan, double tilt)
        {
            if (f.pan != null) f.pan.localRotation = Quaternion.Euler(0, (float)pan, 0);
            f.tilt.localRotation = Quaternion.Euler((float)tilt, 0, 0);
        }

        static void AimPin(SKContext ctx, SKFixture f)
        {
            Vector3 target = J.S(f.u.p, "target", "performer") == "performer" && ctx.performersThree.Count > 0
                ? ctx.performersThree[f.i % ctx.performersThree.Count] : ctx.centerThree;
            Vector3 from = Three(ctx, f.tilt.position);
            double pan, tilt;
            AimLocal(ctx, f, (target - from).normalized, out pan, out tilt);
            ApplyPanTilt(f, pan, tilt);
            f.len = Mathf.Max(2f, (target - from).magnitude - 0.5f);
        }

        /// <summary>Unity ワールド座標 → three 座標</summary>
        public static Vector3 Three(SKContext ctx, Vector3 world)
        {
            return ctx.Conv(ctx.model.InverseTransformPoint(world));   // Conv は自分自身の逆変換
        }

        // ------------------------------------------------------------------ 床の映り込み

        /// <summary>
        /// ブラウザ版の床の映り込み（Reflector）の代わりに、ステージを囲む Reflection Probe を置く。
        /// 床（つやのあるマテリアル）にステージ・LED・トラスが映る。読み込み時に1回だけ撮る（Realtime / OnAwake）ので軽い
        /// </summary>
        static void AddReflectionProbe(SKContext ctx)
        {
            var st = J.O(ctx.state, "stage");
            float W = J.F(st, "width", 16f), D = J.F(st, "depth", 8f), H = J.F(st, "height", 1.2f);
            var go = new GameObject("FloorReflectionProbe");
            go.transform.SetParent(ctx.root.transform, false);
            go.transform.position = ctx.ToWorld(new Vector3(0, H + 2.5f, 0));
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            p.resolution = 256;
            p.hdr = true;
            p.boxProjection = true;
            p.importance = 2;
            p.size = new Vector3(W + 10f, 16f, D + 14f);
            p.center = new Vector3(0, 4f, 0);
            ctx.Log("床の映り込み用に Reflection Probe を置きました（読み込み時に1回撮影。Quest で重いときは Baked にするか削除）");
        }

        // ------------------------------------------------------------------ コライダー

        static void AddColliders(SKContext ctx)
        {
            int n = 0;
            var stage = FindDeep(ctx.model, "STAGE");
            var venue = FindDeep(ctx.model, "VENUE");
            var targets = new List<MeshFilter>();
            if (stage != null)
                targets.AddRange(stage.GetComponentsInChildren<MeshFilter>(true).Where(mf => !Regex.IsMatch(mf.name, "Edge|CenterRing|StairsLED|StageFloor|LED")));
            if (venue != null)
                targets.AddRange(venue.GetComponentsInChildren<MeshFilter>(true).Where(mf => mf.name.StartsWith("Venue_Ground") || mf.name.StartsWith("Venue_Seats")));
            foreach (var u in ctx.units)
                if (u.type == "riser" || u.type == "stairs")
                    targets.AddRange(u.t.GetComponentsInChildren<MeshFilter>(true).Where(mf => !mf.name.Contains("LED")));
            foreach (var mf in targets)
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                n++;
            }
            ctx.Log("床・階段などにコライダーを " + n + " 個付けました（歩けるようにするため）");
        }

        // ------------------------------------------------------------------ 特効

        static Material particleAdd, particleSoft, particleFlat;

        static void BuildFX(SKContext ctx)
        {
            particleAdd = SKAssets.NewMaterial(SKAssets.ShaderParticle, ctx.dir + "/Materials/Particle_Spark.mat");
            particleAdd.SetFloat("_Shape", 1); particleAdd.SetFloat("_Boost", 3f); SetBlend(particleAdd, 2);
            particleSoft = SKAssets.NewMaterial(SKAssets.ShaderParticle, ctx.dir + "/Materials/Particle_Smoke.mat");
            particleSoft.SetFloat("_Shape", 0); SetBlend(particleSoft, 1);
            particleFlat = SKAssets.NewMaterial(SKAssets.ShaderParticle, ctx.dir + "/Materials/Particle_Confetti.mat");
            particleFlat.SetFloat("_Shape", 2); SetBlend(particleFlat, 1);
            int n = 0;
            foreach (var u in ctx.units)
            {
                bool always = J.S(u.p, "mode", "trigger") == "always";
                if (!ctx.fxBuiltin && (u.type == "spark" || u.type == "confetti" || u.type == "smoke")) continue;   // 特効の設定で「最初からある特効」を出さない
                if (u.type == "spark") { ctx.sparks.Add(Spark(ctx, u, always)); n++; }
                else if (u.type == "confetti") { ctx.confetti.Add(Confetti(ctx, u, always)); n++; }
                else if (u.type == "smoke") { ctx.smoke.Add(Smoke(ctx, u, always)); n++; }
            }
            if (ctx.cfg.glitter && J.B(J.O(ctx.state, "lights"), "glitter", true)) { Glitter(ctx); n++; }
            ctx.Log("特効パーティクルを " + n + " 個作りました");
        }

        static ParticleSystem NewPS(string name, Transform parent, Vector3 localPos, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
            return ps;
        }

        static Gradient Fade(Color a, Color b, float peakAlpha)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(peakAlpha, 0.1f), new GradientAlphaKey(peakAlpha, 0.6f), new GradientAlphaKey(0, 1) });
            return g;
        }

        static ParticleSystem Spark(SKContext ctx, SKUnit u, bool always)
        {
            var ps = NewPS("SparkFX", u.t, new Vector3(0, 0.2f, 0), particleAdd);
            float h = J.F(u.p, "height", 4f), v0 = Mathf.Sqrt(2 * 9.8f * h);
            var c = J.Col(J.S(J.O(u.asset), "color2", "#ffd98a"), new Color(1f, 0.85f, 0.55f));
            var main = ps.main;
            main.duration = 3.2f; main.loop = always; main.playOnAwake = always;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(v0 * 0.85f, v0 * 1.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.gravityModifier = 0.9f; main.maxParticles = 1500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(c * 3.2f, Color.white * 4f);
            var em = ps.emission; em.rateOverTime = J.F(u.p, "rate", 260f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 4f; sh.radius = 0.05f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(Fade(Color.white, Color.white, 1f));
            return ps;
        }

        static ParticleSystem Confetti(SKContext ctx, SKUnit u, bool always)
        {
            Vector3 dir = ctx.Conv(new Vector3(0, Mathf.Cos(0.55f), Mathf.Sin(0.55f)));
            var ps = NewPS("ConfettiFX", u.t, ctx.Conv(new Vector3(0, 0.85f, 0.45f)), particleFlat);
            ps.transform.localRotation = Quaternion.LookRotation(dir);
            int count = Mathf.Clamp(J.I(u.p, "count", 500), 10, 3000);
            float power = J.F(u.p, "power", 14f);
            var main = ps.main;
            main.duration = 1f; main.loop = always; main.playOnAwake = always;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(power * 0.5f, power * 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.1f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); main.startRotationY = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2); main.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = 0.25f; main.maxParticles = count * (always ? 2 : 1);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var g = new Gradient();
            bool pastel = J.B(u.p, "pastel", true);
            g.SetKeys(new[] {
                    new GradientColorKey(pastel ? new Color(1f, 0.7f, 0.85f) : new Color(1f, 0.2f, 0.4f), 0f), new GradientColorKey(pastel ? new Color(0.7f, 0.9f, 1f) : new Color(0.2f, 0.6f, 1f), 0.33f),
                    new GradientColorKey(pastel ? new Color(1f, 0.97f, 0.7f) : new Color(1f, 0.9f, 0.2f), 0.66f), new GradientColorKey(pastel ? new Color(0.75f, 1f, 0.85f) : new Color(0.3f, 1f, 0.5f), 1f) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            var mg = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
            main.startColor = mg;
            var em = ps.emission;
            if (always) em.rateOverTime = count / 8f;
            else { em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) }); }
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 25f; sh.radius = 0.1f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.y = new ParticleSystem.MinMaxCurve(-6f, 6f); rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = 1.2f; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = true;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.4f;
            var pr = ps.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Mesh;
            pr.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            pr.alignment = ParticleSystemRenderSpace.Local;
            return ps;
        }

        static ParticleSystem Smoke(SKContext ctx, SKUnit u, bool always)
        {
            bool low = J.S(u.p, "kind", "co2") == "lowfog";
            float amount = J.F(u.p, "amount", 1f);
            var ps = NewPS(low ? "LowFogFX" : "CO2FX", u.t, new Vector3(0, low ? 0.12f : 0.3f, 0), particleSoft);
            var main = ps.main;
            main.duration = low ? 6f : 1.6f; main.loop = always; main.playOnAwake = always;
            main.startLifetime = low ? new ParticleSystem.MinMaxCurve(5f, 8f) : new ParticleSystem.MinMaxCurve(1.3f, 1.8f);
            main.startSpeed = low ? new ParticleSystem.MinMaxCurve(0.3f, 1.2f) : new ParticleSystem.MinMaxCurve(9f, 12f);
            main.startSize = low ? new ParticleSystem.MinMaxCurve(2.2f, 3.7f) : new ParticleSystem.MinMaxCurve(0.5f, 0.7f);
            main.gravityModifier = 0f; main.maxParticles = low ? 300 : 500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.93f, 0.95f, 1f, 1f);
            var em = ps.emission; em.rateOverTime = (low ? 30f : 160f) * amount;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone;
            if (low) { sh.angle = 70f; sh.radius = 0.2f; sh.rotation = ctx.flipX ? new Vector3(0, 0, 0) : new Vector3(0, 180, 0); }
            else { sh.angle = 3f; sh.radius = 0.05f; sh.rotation = new Vector3(-90, 0, 0); }
            var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(Fade(Color.white, Color.white, low ? 0.12f : 0.3f));
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, low ? 0.7f : 0.25f), new Keyframe(1, 1f)));
            var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.drag = low ? 0.35f : 1.4f; lv.multiplyDragByParticleSize = false;
            return ps;
        }

        static void Glitter(SKContext ctx)
        {
            var st = J.O(ctx.state, "stage");
            float W = J.F(st, "width", 16f) + 8f, D = J.F(st, "depth", 8f) + 10f, H = J.F(st, "height", 1.2f);
            var ps = NewPS("Glitter", ctx.root.transform, ctx.root.transform.InverseTransformPoint(ctx.ToWorld(new Vector3(0, H + 0.3f + 5.5f, 2f))), particleAdd);
            var c = J.Col(J.S(J.O(ctx.state, "lights"), "glitterColor", "#cfe8ff"), Color.white);
            var main = ps.main;
            main.duration = 5f; main.loop = true; main.playOnAwake = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.maxParticles = 700;
            main.startColor = c * 2.5f;
            var em = ps.emission; em.rateOverTime = 140f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(W, 11f, D);
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.2f, 1), new Keyframe(0.35f, 0.1f), new Keyframe(0.6f, 1), new Keyframe(1, 0)));
        }

        // ------------------------------------------------------------------ 小道具

        public static SKUnit OwnerUnit(SKContext ctx, Transform t)
        {
            while (t != null && t != ctx.model) { if (ctx.unitByT.TryGetValue(t, out var u)) return u; t = t.parent; }
            return null;
        }

        /// <summary>インポーターが付けがちな「__1」「 (1)」「.001」などの後ろの付け足しを許して名前を比べる</summary>
        public static bool NameMatches(string n, string key)
        {
            if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(key)) return false;
            if (n == key) return true;
            if (!n.StartsWith(key)) return false;
            string rest = n.Substring(key.Length);
            return Regex.IsMatch(rest, @"^(__\d+|_\d+_?|\s*\(\d+\)|\.\d+|-\d+)$");
        }

        public static Transform FindChild(Transform t, string key)
        {
            if (t == null) return null;
            for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).name == key) return t.GetChild(i);
            for (int i = 0; i < t.childCount; i++) if (NameMatches(t.GetChild(i).name, key)) return t.GetChild(i);
            return null;
        }

        static bool IsUnder(Transform t, string name)
        {
            for (var p = t; p != null; p = p.parent) if (NameMatches(p.name, name)) return true;
            return false;
        }

        public static Transform FindDeep(Transform t, string key)
        {
            if (t == null) return null;
            var q = new Queue<Transform>();
            q.Enqueue(t);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (c != t && NameMatches(c.name, key)) return c;
                for (int i = 0; i < c.childCount; i++) q.Enqueue(c.GetChild(i));
            }
            return null;
        }

        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Stage";
            s = Regex.Replace(s, @"[\\/:*?""<>|\s]+", "_");
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        static float ParseF(string s, float def)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;
        }

        public static string PathFrom(Transform t, Transform root)
        {
            return AnimationUtility.CalculateTransformPath(t, root);
        }
    }
}
