using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// メニュー「和室 → すてーじ工房 → 特効（パーティクル）の設定」
    /// ワールドに置いた好きなパーティクル（Particle System）を「ボタンの名前 → パーティクル」で登録すると、
    /// 組み立てるときに、正面のリモコンとステージ裏の照明卓にそのボタンが増える。
    /// 設定はシーン直下の「StageKobo_FX（特効の設定）」（EditorOnly：ワールドには入らない）に保存する。
    /// </summary>
    public class SKFxWindow : EditorWindow
    {
        static readonly string[] Modes = { "押すたびに 1 回出す", "ON / OFF（出しっぱなし・もう一度押すと止める）" };
        Vector2 scroll;
        string log = "";

        [MenuItem("和室/すてーじ工房/特効（パーティクル）の設定")]
        static void Open()
        {
            var w = GetWindow<SKFxWindow>("特効の設定");
            w.minSize = new Vector2(440, 520);
        }

        public static void OpenWindow() { Open(); }

        void OnSelectionChange() { Repaint(); }
        void OnHierarchyChange() { Repaint(); }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("追加の特効（好きなパーティクル）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "① ワールドの、出したい場所にパーティクル（Particle System）を置く（ステージ StageKobo_～ の外に）\n" +
                "② 下の「＋ ボタンを足す」で、ボタンの名前と、出すパーティクルを決める\n" +
                "③「操作パネルに反映」を押す（ステージを前回と同じ設定で組み立て直します）\n" +
                "→ 操作パネル（手前・裏）の「特効・カメラ」タブの「追加の特効」にボタンが増えます（UdonSharp 版は全員の画面で出ます）", MessageType.Info);

            var s = SKFx.Find();
            if (s == null)
            {
                EditorGUILayout.HelpBox("このシーンには、まだ特効の設定がありません。", MessageType.None);
                if (GUILayout.Button("特効の設定を作る", GUILayout.Height(28))) { s = SKFx.Create(); Selection.activeGameObject = s.gameObject; }
                EditorGUILayout.EndScrollView();
                return;
            }

            var so = new SerializedObject(s);
            so.Update();
            var entries = so.FindProperty("entries");
            EditorGUILayout.PropertyField(so.FindProperty("showBuiltin"), new GUIContent("最初からある特効（スパーク・紙吹雪・スモーク）のボタンも出す"));
            EditorGUILayout.Space(6);

            int remove = -1, up = -1;
            for (int i = 0; i < entries.arraySize; i++)
            {
                var e = entries.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("ボタン " + (i + 1), EditorStyles.boldLabel, GUILayout.Width(70));
                        GUILayout.FlexibleSpace();
                        using (new EditorGUI.DisabledScope(i == 0)) if (GUILayout.Button("▲", GUILayout.Width(28))) up = i;
                        using (new EditorGUI.DisabledScope(i == entries.arraySize - 1)) if (GUILayout.Button("▼", GUILayout.Width(28))) up = i + 1;
                        if (GUILayout.Button("消す", GUILayout.Width(44))) remove = i;
                    }
                    var name = e.FindPropertyRelative("name");
                    name.stringValue = EditorGUILayout.TextField(new GUIContent("ボタンの名前", "操作パネルのボタンに出る文字（短い方が読みやすい：8文字くらいまで）"), name.stringValue);
                    var mode = e.FindPropertyRelative("mode");
                    mode.intValue = EditorGUILayout.Popup(new GUIContent("出し方"), Mathf.Clamp(mode.intValue, 0, 1), Modes.Select(x => new GUIContent(x)).ToArray());
                    if (mode.intValue == 0)
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("seconds"), new GUIContent("止めるまでの秒", "0 = パーティクルの設定のまま（1回で終わるパーティクル向け）。ループするパーティクルは秒数を入れると、その時間で止まります"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("sound"), new GUIContent("中にある音（AudioSource）も鳴らす"));

                    // ---- パーティクル
                    var list = e.FindPropertyRelative("systems");
                    EditorGUILayout.LabelField("出すパーティクル（何個でも。いっしょに出ます）");
                    int del = -1;
                    for (int k = 0; k < list.arraySize; k++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var p = list.GetArrayElementAtIndex(k);
                            p.objectReferenceValue = EditorGUILayout.ObjectField(p.objectReferenceValue, typeof(ParticleSystem), true);
                            using (new EditorGUI.DisabledScope(p.objectReferenceValue == null))
                                if (GUILayout.Button(new GUIContent("試す", "シーンで再生してみる（そのパーティクルを選びます）"), GUILayout.Width(40))) Preview(p.objectReferenceValue as ParticleSystem);
                            if (GUILayout.Button("✕", GUILayout.Width(24))) del = k;
                        }
                    }
                    if (del >= 0) { list.GetArrayElementAtIndex(del).objectReferenceValue = null; list.DeleteArrayElementAtIndex(del); }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("＋ 欄を足す")) { list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = null; }
                        var sel = Selection.gameObjects.Select(g => g.GetComponent<ParticleSystem>()).Where(x => x != null).ToList();
                        using (new EditorGUI.DisabledScope(sel.Count == 0))
                            if (GUILayout.Button(new GUIContent("ヒエラルキーで選んでいるものを足す" + (sel.Count > 0 ? "（" + sel.Count + "）" : ""))))
                                foreach (var ps in sel)
                                {
                                    bool dup = false;
                                    for (int k = 0; k < list.arraySize; k++) if (list.GetArrayElementAtIndex(k).objectReferenceValue == ps) dup = true;
                                    if (dup) continue;
                                    list.arraySize++;
                                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = ps;
                                }
                    }
                    Warnings(list, mode.intValue, e.FindPropertyRelative("seconds").floatValue);
                }
            }
            if (remove >= 0) entries.DeleteArrayElementAtIndex(remove);
            if (up > 0) entries.MoveArrayElement(up, up - 1);
            using (new EditorGUI.DisabledScope(entries.arraySize >= StageKoboFxSettings.MaxEntries))
            {
                if (GUILayout.Button("＋ ボタンを足す" + (entries.arraySize >= StageKoboFxSettings.MaxEntries ? "（" + StageKoboFxSettings.MaxEntries + " 個まで）" : ""), GUILayout.Height(26)))
                {
                    entries.arraySize++;
                    var e = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                    e.FindPropertyRelative("name").stringValue = "特効" + entries.arraySize;
                    e.FindPropertyRelative("systems").arraySize = 0;
                    e.FindPropertyRelative("mode").intValue = 0;
                    e.FindPropertyRelative("seconds").floatValue = 0f;
                    e.FindPropertyRelative("sound").boolValue = true;
                    // ヒエラルキーで選んでいるパーティクルがあれば、最初から入れておく
                    var list = e.FindPropertyRelative("systems");
                    foreach (var ps in Selection.gameObjects.Select(g => g.GetComponent<ParticleSystem>()).Where(x => x != null))
                    {
                        list.arraySize++;
                        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = ps;
                    }
                }
            }
            so.ApplyModifiedProperties();

            // ---- 反映
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("ボタンを増やしたり名前を変えたりしたら、下のボタンでステージを組み立て直すと反映されます（インポーターで前回「組み立てる」を押したときと同じ設定）。\n" +
                                    "パーティクルの場所・見た目を変えただけなら、組み立て直さなくても大丈夫です。", MessageType.None);
            if (GUILayout.Button("操作パネルに反映（ステージを組み立て直す）", GUILayout.Height(34)))
            {
                string l = StageKoboImporterWindow.RebuildLast();
                if (l == null)
                {
                    log = "前回の組み立ての設定が見つかりません。インポーターで GLB と Unity用JSON を入れて「組み立てる」を押してください（2回目からはここから作り直せます）";
                    StageKoboImporterWindow.OpenWindow();
                }
                else log = l;
            }
            if (!string.IsNullOrEmpty(log))
                EditorGUILayout.HelpBox(log, log.StartsWith("エラー") || log.StartsWith("前回") ? MessageType.Warning : MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>よくあるつまずきを先に知らせる</summary>
        static void Warnings(SerializedProperty list, int mode, float secs)
        {
            int n = 0;
            for (int k = 0; k < list.arraySize; k++)
            {
                var ps = list.GetArrayElementAtIndex(k).objectReferenceValue as ParticleSystem;
                if (ps == null) continue;
                n++;
                if (SKFx.InsideStage(ps.transform))
                    EditorGUILayout.HelpBox("「" + ps.name + "」はステージ（StageKobo_～）の中にあります。組み立て直すと消えるので、ステージの外に出してください。", MessageType.Warning);
                else if (SKFx.EditorOnly(ps.transform))
                    EditorGUILayout.HelpBox("「" + ps.name + "」は EditorOnly のオブジェクトの中にあるので、ワールドに入りません。特効の設定のオブジェクトの外に出してください。", MessageType.Warning);
                if (!ps.gameObject.activeInHierarchy)
                    EditorGUILayout.HelpBox("「" + ps.name + "」は非表示（オフ）になっているので、押しても出ません。ヒエラルキーでチェックを入れてください（出るまでは見えないので、オンのままで大丈夫です）。", MessageType.Warning);
                var main = ps.main;
                if (main.playOnAwake)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.HelpBox("「" + ps.name + "」はワールドに入った瞬間に出ます（Play On Awake）。", MessageType.Warning);
                        if (GUILayout.Button("ボタンを押したときだけにする", GUILayout.Width(170), GUILayout.Height(38)))
                        {
                            foreach (var c in ps.GetComponentsInChildren<ParticleSystem>(true))
                            {
                                Undo.RecordObject(c, "Play On Awake を切る");
                                var m = c.main; m.playOnAwake = false;
                                EditorUtility.SetDirty(c);
                            }
                        }
                    }
                }
                if (mode == 0 && secs <= 0f && main.loop)
                    EditorGUILayout.HelpBox("「" + ps.name + "」はループするので、「1回出す」だと止まりません。「止めるまでの秒」を入れるか、出し方を「ON / OFF」にしてください。", MessageType.Warning);
            }
            if (n == 0) EditorGUILayout.HelpBox("パーティクルが入っていません（このボタンは作られません）。", MessageType.None);
        }

        static void Preview(ParticleSystem ps)
        {
            if (ps == null) return;
            Selection.activeGameObject = ps.gameObject;
            EditorGUIUtility.PingObject(ps.gameObject);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }
    }
}
