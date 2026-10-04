using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// 曲の音に反応する（AudioLink）・YamaPlayer との連携。
    /// ・反応はシェーダーの中だけ（Shaders/SKAudioLink.cginc）。マテリアルの _ALAmount（強さ）を入れるだけで、
    ///   ワールドに AudioLink があれば反応し、無ければいつもの演出のまま。
    /// ・AudioLink・YamaPlayer のスクリプトは直接使わず、名前で探して SerializedObject で設定する
    ///   （どちらかが入っていないプロジェクトでもコンパイルエラーにならないように）。
    /// ・YamaPlayer：Controller の「Audio Link を使う」（_useAudioLink）と「AudioLink」（_audioLink）を設定する
    ///   （YamaPlayer のインスペクター「外部設定 → Audio Link」と同じ）。
    ///   AudioLink の autoSetMediaState は切る（再生・一時停止・停止の状態を YamaPlayer が AudioLink に伝えるので）。
    /// </summary>
    public static class SKAudio
    {
        public const string ALTypeName = "AudioLink.AudioLink";
        public const string YamaTypeName = "Yamadev.YamaStream.Controller";
        const string ALPrefabGuid = "8c1f201f848804f42aa401d0647f8902";     // AudioLink.prefab（YamaPlayer の「AudioLink を設定する」ボタンと同じもの）
        const string ALPrefabPath = "Packages/com.llealloo.audiolink/Runtime/AudioLink.prefab";
        const string YamaPrefabGuid = "0ca92d0fbf2bf3944bfeef01f4977da5";   // YamaPlayer.prefab（画面・操作パネル付き）

        public const string SpatialTypeName = "VRC.SDK3.Components.VRCSpatialAudioSource";

        public static Type ALType { get { return SKUdon.FindType(ALTypeName); } }
        public static Type YamaType { get { return SKUdon.FindType(YamaTypeName); } }
        public static bool ALInstalled { get { return ALType != null; } }
        public static bool YamaInstalled { get { return YamaType != null; } }

        /// <summary>開いているシーンにあるコンポーネント（非表示のものも）</summary>
        public static List<Component> InScene(Type t)
        {
            var list = new List<Component>();
            if (t == null) return list;
            foreach (var o in Resources.FindObjectsOfTypeAll(t))
            {
                var c = o as Component;
                if (c == null || EditorUtility.IsPersistent(c) || (c.hideFlags & (HideFlags.HideAndDontSave | HideFlags.NotEditable)) != 0) continue;
                if (!c.gameObject.scene.IsValid() || !c.gameObject.scene.isLoaded) continue;
                list.Add(c);
            }
            return list;
        }

        /// <summary>YamaPlayer が AudioLink につながっているか（インポーターの表示用）</summary>
        public static bool IsLinked(Component yamaController)
        {
            var so = new SerializedObject(yamaController);
            var use = so.FindProperty("_useAudioLink");
            var link = so.FindProperty("_audioLink");
            return use != null && link != null && use.boolValue && link.objectReferenceValue != null;
        }

        // ------------------------------------------------------------------ 組み立てのとき

        /// <summary>組み立ての最後に呼ぶ：マテリアルに強さを入れて、AudioLink を用意し、YamaPlayer をつなぐ。YamaPlayer の音をステージから会場ぜんたいへ</summary>
        public static void Setup(SKContext ctx)
        {
            var cfg = ctx.cfg;
            if (cfg.yamaSound && YamaInstalled)
            {
                try { SpreadSound(ctx.Log, ctx.root); }
                catch (Exception e)
                {
                    ctx.Log("⚠ YamaPlayer の音の設定でエラー：" + e.Message + "（インポーターの「YamaPlayer の音を会場に広げる」でやり直せます）");
                    Debug.LogException(e);
                }
            }
            float amount = ctx.audio ? Mathf.Clamp01(cfg.alAmount) : 0f;
            int n = ApplyMaterials(ctx, amount);
            if (!ctx.audio) return;
            ctx.Log("音に反応（AudioLink）：強さ " + Mathf.RoundToInt(amount * 100f) + "%（マテリアル " + n + " 個）" +
                (ctx.udon ? "。操作パネルの「照明」タブ「曲の音に反応」で ON/OFF できます" : ""));
            if (!ALInstalled)
            {
                ctx.Log("⚠ AudioLink のパッケージが入っていないので、まだ音には反応しません。VRChat Creator Companion（VCC）で AudioLink を追加して、" +
                        "インポーターの「AudioLink をつなぐ」を押してください（ステージは作り直さなくて大丈夫です）");
                return;
            }
            try { Link(ctx.Log, cfg.yama, ctx.root.scene); }
            catch (Exception e)
            {
                // ここで失敗してもステージは使える（音に反応しないだけ）ので、組み立ては止めない
                ctx.Log("⚠ AudioLink・YamaPlayer の設定でエラー：" + e.Message + "（インポーターの「AudioLink をつなぐ」でやり直せます）");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// AudioLink をシーンに用意して（無ければ置く）、YamaPlayer をつなぐ。インポーターのボタンからも呼ぶ（ステージを作り直さずに）。
        /// 戻り値：使う AudioLink（無ければ null）
        /// </summary>
        public static Component Link(Action<string> log, bool yama, Scene scene)
        {
            var alT = ALType;
            if (alT == null) { log("⚠ AudioLink のパッケージが入っていません"); return null; }
            var als = InScene(alT);
            Component al = als.FirstOrDefault(c => c.gameObject.activeInHierarchy) ?? als.FirstOrDefault();
            if (al == null) al = AddAudioLink(log, scene);
            else log("AudioLink：シーンの「" + al.gameObject.name + "」を使います" + (als.Count > 1 ? "（AudioLink が " + als.Count + " 個あります。ふつうは1個だけにします）" : ""));
            if (al == null) return null;
            if (!al.gameObject.activeInHierarchy) log("⚠ AudioLink「" + al.gameObject.name + "」が非表示になっています。表示（チェック）しないと音に反応しません");
            if (yama) LinkYama(log, al);
            if (al.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(al.gameObject.scene);
            return al;
        }

        static Component AddAudioLink(Action<string> log, Scene scene)
        {
            string path = AssetDatabase.GUIDToAssetPath(ALPrefabGuid);
            var prefab = !string.IsNullOrEmpty(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ALPrefabPath);
            if (prefab == null)
            {
                log("⚠ AudioLink のプレハブ（AudioLink.prefab）が見つかりませんでした。Packages → AudioLink → Runtime の AudioLink をシーンにドラッグしてください");
                return null;
            }
            var go = (scene.IsValid() ? PrefabUtility.InstantiatePrefab(prefab, scene) : PrefabUtility.InstantiatePrefab(prefab)) as GameObject;
            if (go == null) { log("⚠ AudioLink を置けませんでした"); return null; }
            Undo.RegisterCreatedObjectUndo(go, "AudioLink を置く");
            var al = go.GetComponentInChildren(ALType, true);
            log("AudioLink をシーンに置きました（「" + go.name + "」。ステージの外に置いているので、ステージを作り直しても消えません）");
            return al;
        }

        static void LinkYama(Action<string> log, Component al)
        {
            var yt = YamaType;
            if (yt == null) return;
            var ctrls = InScene(yt);
            if (ctrls.Count == 0)
            {
                log("YamaPlayer がシーンに無いので、まだつないでいません（YamaPlayer を置いたら、インポーターの「AudioLink をつなぐ」を押してください）");
                return;
            }
            int linked = 0;
            foreach (var c in ctrls)
            {
                var so = new SerializedObject(c);
                var use = so.FindProperty("_useAudioLink");
                var link = so.FindProperty("_audioLink");
                if (use == null || link == null)
                {
                    log("⚠ YamaPlayer の AudioLink の設定が見つかりませんでした（YamaPlayer のバージョンが違うかもしれません）。YamaPlayer の「設定 → 外部設定 → Audio Link」を手でオンにしてください");
                    continue;
                }
                use.boolValue = true;
                link.objectReferenceValue = al;
                so.ApplyModifiedProperties();
                so.Update();
                if (link.objectReferenceValue != al)
                {
                    log("⚠ YamaPlayer に AudioLink を入れられませんでした。YamaPlayer の「設定 → 外部設定 → Audio Link」で、AudioLink を手で選んでください");
                    continue;
                }
                EditorUtility.SetDirty(c);
                linked++;
            }
            if (linked == 0) return;
            // 再生・一時停止・停止の状態は YamaPlayer が AudioLink に伝える（AudioLink が自分で決めると上書きしてしまう）
            var aso = new SerializedObject(al);
            var auto = aso.FindProperty("autoSetMediaState");
            if (auto != null && auto.boolValue)
            {
                auto.boolValue = false;
                aso.ApplyModifiedProperties();
                EditorUtility.SetDirty(al);
            }
            log("YamaPlayer " + linked + " 台を AudioLink につなぎました（YamaPlayer の音でステージが動きます。止める・一時停止するといつもの演出に戻ります）" +
                (linked > 1 ? "\n　※ YamaPlayer が複数あるときは、最後に音量などを操作したプレイヤーの音に反応します" : ""));
        }

        /// <summary>YamaPlayer（画面・操作パネル付き）をシーンに置く。置いたものを選んだ状態にする</summary>
        public static GameObject AddYamaPlayer()
        {
            string path = AssetDatabase.GUIDToAssetPath(YamaPrefabGuid);
            var prefab = !string.IsNullOrEmpty(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            if (prefab == null) return null;
            var go = PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene()) as GameObject;   // ステージの中に入れない（作り直しで消えないように）
            if (go == null) return null;
            Undo.RegisterCreatedObjectUndo(go, "YamaPlayer を置く");
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            return go;
        }

        // ------------------------------------------------------------------ YamaPlayer の音を会場ぜんたいへ

        /// <summary>音を鳴らす場所と、届く距離</summary>
        public class SoundPlace
        {
            public Vector3 pos;
            public float near, far, radius;
            public string from;
        }

        /// <summary>シーンのステージ（StageKobo_～。いちばん新しく組み立てたもの）。無ければ null</summary>
        public static GameObject FindStageRoot()
        {
            GameObject best = null;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!go.name.StartsWith("StageKobo_") || go.name == SKFx.ObjectName) continue;
                if (FindDeep(go.transform, "STAGE") == null) continue;
                if (best == null || go.transform.GetSiblingIndex() > best.transform.GetSiblingIndex()) best = go;
            }
            return best;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        static Bounds? RendererBounds(Transform t)
        {
            Bounds? b = null;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; }
            }
            return b;
        }

        /// <summary>
        /// ステージを見て、音を鳴らす場所と届く距離を決める（ワールドの座標で見るので、軸の向きに関係なく使える）
        /// ・ステージのスピーカー（ラインアレイ・スピーカースタック）があれば、そのまん中。左右の間隔の半分を「音の広がり」（Volumetric Radius）に
        /// ・無ければステージのまん中の少し上。広がりはステージの幅の半分
        /// ・届く距離（Far）は、鳴らす場所から会場（組み立てたもの全部）のいちばん遠い角まで ×1.15（30〜250m）。Near（同じ大きさで聞こえる距離）は Far の 35%（6〜60m）
        /// </summary>
        public static SoundPlace Measure(GameObject root)
        {
            var p = new SoundPlace();
            var assets = FindDeep(root.transform, "ASSETS");
            var centers = new List<Vector3>();
            if (assets != null)
                foreach (Transform u in assets)
                {
                    if (!u.name.StartsWith("line_array_") && !u.name.StartsWith("speaker_stack_")) continue;
                    if (!u.gameObject.activeInHierarchy) continue;
                    foreach (Transform w in u) { var b = RendererBounds(w); if (b.HasValue) centers.Add(b.Value.center); }
                }
            var stage = FindDeep(root.transform, "STAGE");
            var sb = stage != null ? RendererBounds(stage) : null;
            if (centers.Count > 0)
            {
                foreach (var c in centers) p.pos += c;
                p.pos /= centers.Count;
                foreach (var c in centers) p.radius = Mathf.Max(p.radius, Vector2.Distance(new Vector2(c.x, c.z), new Vector2(p.pos.x, p.pos.z)));
                p.from = "ステージのスピーカー " + centers.Count + " 台のまん中";
            }
            else if (sb.HasValue)
            {
                p.pos = sb.Value.center; p.pos.y = sb.Value.max.y + 2f;
                p.radius = Mathf.Max(sb.Value.extents.x, sb.Value.extents.z) * 0.6f;
                p.from = "ステージのまん中（スピーカーのアセットが無いため）";
            }
            else
            {
                p.pos = root.transform.position + Vector3.up * 3f; p.radius = 4f;
                p.from = "ステージの場所";
            }
            var all = RendererBounds(root.transform);
            float far = 30f;
            if (all.HasValue)
            {
                var a = all.Value;
                foreach (var x in new[] { a.min.x, a.max.x })
                    foreach (var z in new[] { a.min.z, a.max.z })
                        far = Mathf.Max(far, Vector2.Distance(new Vector2(x, z), new Vector2(p.pos.x, p.pos.z)) * 1.15f);
            }
            p.far = Mathf.Clamp(far, 30f, 250f);
            p.near = Mathf.Clamp(p.far * 0.35f, 6f, 60f);
            p.radius = Mathf.Clamp(p.radius, 1f, p.near);
            return p;
        }

        /// <summary>
        /// YamaPlayer の音の出口（Controller の _audioSources ＝ 動画の音が出る AudioSource）を、ステージの上に動かして、会場ぜんたいに届く距離にする。
        /// 新しい AudioSource は作らない（YamaPlayer の音量・ミュート・AudioLink はそのまま効く）。インポーターのボタンからも呼ぶ（ステージを作り直さずに）
        /// </summary>
        public static bool SpreadSound(Action<string> log, GameObject stageRoot)
        {
            var yt = YamaType;
            if (yt == null) { log("⚠ YamaPlayer が入っていません"); return false; }
            if (stageRoot == null) { log("⚠ シーンにステージ（StageKobo_～）がありません。先にインポーターで組み立ててください"); return false; }
            var ctrls = InScene(yt);
            if (ctrls.Count == 0)
            {
                log("YamaPlayer がシーンに無いので、音の範囲はまだ設定していません（YamaPlayer を置いたら、インポーターの「YamaPlayer の音を会場に広げる」を押してください）");
                return false;
            }
            var place = Measure(stageRoot);
            var spT = SKUdon.FindType(SpatialTypeName);
            int n = 0;
            foreach (var c in ctrls)
            {
                var so = new SerializedObject(c);
                var arr = so.FindProperty("_audioSources");
                var srcs = new List<AudioSource>();
                if (arr != null && arr.isArray)
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var a = arr.GetArrayElementAtIndex(i).objectReferenceValue as AudioSource;
                        if (a != null && !srcs.Contains(a)) srcs.Add(a);
                    }
                if (srcs.Count == 0)
                {
                    log("⚠ YamaPlayer「" + c.gameObject.name + "」の音の出口（AudioSource）が見つかりませんでした（YamaPlayer のバージョンが違うかもしれません）");
                    continue;
                }
                foreach (var a in srcs)
                {
                    Undo.RecordObject(a.transform, "YamaPlayer の音をステージへ");
                    a.transform.position = place.pos;
                    Undo.RecordObject(a, "YamaPlayer の音の範囲");
                    a.minDistance = place.near;
                    a.maxDistance = place.far;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(a.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(a);
                    if (spT == null) continue;   // VRChat SDK が無い（Unity だけで試すとき）
                    var sp = a.GetComponent(spT);
                    if (sp == null) sp = Undo.AddComponent(a.gameObject, spT);
                    var sso = new SerializedObject(sp);
                    SetF(sso, "Far", place.far);
                    SetF(sso, "Near", place.near);
                    SetF(sso, "VolumetricRadius", place.radius);
                    var en = sso.FindProperty("EnableSpatialization");
                    if (en != null) en.boolValue = true;   // ステージの方から聞こえる（近くでは広がりの中なので、どこからでも同じ）
                    sso.ApplyModifiedProperties();
                }
                n++;
                if (c.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            }
            if (n == 0) return false;
            log("YamaPlayer の音：" + place.from + "から鳴らします（" + n + " 台）。まわり " + Mathf.RoundToInt(place.near) + "m は同じ大きさ、" +
                Mathf.RoundToInt(place.far) + "m 先まで届きます（会場の大きさから自動）" +
                "\n　※ 音の出口（YamaPlayer の中の Audio Source）をステージの上に動かしています。YamaPlayer を動かしたら、インポーターの「YamaPlayer の音を会場に広げる」を押し直してください");
            return true;
        }

        static void SetF(SerializedObject so, string name, float v)
        {
            var p = so.FindProperty(name);
            if (p != null) p.floatValue = v;
        }

        // ------------------------------------------------------------------ マテリアル

        /// <summary>ステージのマテリアル（_ALAmount を持つもの）に強さと、ステージの真ん中（灯体の遅れの基準）を入れる</summary>
        static int ApplyMaterials(SKContext ctx, float amount)
        {
            var mats = new HashSet<Material>();
            foreach (var r in ctx.root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null) mats.Add(m);
            if (ctx.vj != null)
                foreach (var m in new[] { ctx.vj.matA, ctx.vj.matB, ctx.vj.matFx })
                    if (m != null) mats.Add(m);
            // ピンスポット（演者を追うスポット）は音で明るさを変えない（本物のライブでも一定）
            var still = new HashSet<Material>();
            foreach (var f in ctx.fixtures)
                if (f.kind == "pin")
                    foreach (var r in new[] { f.beamR, f.lensR })
                        if (r != null) foreach (var m in r.sharedMaterials) if (m != null) still.Add(m);
            foreach (var r in ctx.spotBeams) if (r != null && r.sharedMaterial != null) still.Add(r.sharedMaterial);   // 演者を照らすスポットも同じ
            Vector3 c = ctx.ToWorld(ctx.centerThree);
            int n = 0;
            foreach (var m in mats)
            {
                if (!m.HasProperty("_ALAmount")) continue;
                m.SetFloat("_ALAmount", still.Contains(m) ? 0f : amount);
                if (m.HasProperty("_ALCenter")) m.SetVector("_ALCenter", new Vector4(c.x, c.y, c.z, 0f));
                EditorUtility.SetDirty(m);
                n++;
            }
            return n;
        }
    }
}
