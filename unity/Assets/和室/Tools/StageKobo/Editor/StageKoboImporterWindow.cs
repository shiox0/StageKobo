using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>メニュー「和室 → すてーじ工房 → インポーター」</summary>
    public class StageKoboImporterWindow : EditorWindow
    {
        GameObject glb;
        UnityEngine.Object glbAsset;   // 欄に入れたもの（GLB が取り込まれていないと GameObject ではなく「ただのファイル」になる）
        string glbPath;                // 読み込み直し（glTFast を入れた後など）で中身が入れ替わっても欄を空にしないため
        TextAsset json;
        string outFolder = "Assets/和室/StageKobo_Generated";
        SKAxisMode axis = SKAxisMode.Auto;
        bool led = true, cams = true, beams = true, anim = true, fx = true, remote = true, colliders = true, glitter = true, pinLight = true;
        bool wash = true, crowd = true, performers = true, udon = true, probe = true, vj = true, desks = true;
        bool audioLink = true, yama = true, spots = true, yamaSound = true, video = true, stageLight = true;
        float stageLightPower = 1f;
        float alAmount = 0.7f, spotPower = 1f;
        // シーンの AudioLink・YamaPlayer（表示用。毎回探すと重いので1秒ごと）
        int sceneAL, sceneYama, sceneYamaLinked;
        double nextScan;
        string linkLog = "", soundLog = "";
        string log = "";
        Vector2 scroll;

        [MenuItem("和室/すてーじ工房/インポーター")]
        static void Open()
        {
            var w = GetWindow<StageKoboImporterWindow>("すてーじ工房");
            w.minSize = new Vector2(440, 600);
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("すてーじ工房 インポーター", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "① ブラウザの「保存・書き出し」タブで GLB と Unity用JSON を書き出す\n" +
                "② 2つのファイルを Unity の Assets にドラッグ（GLB は glTFast か UniGLTF でモデルとして取り込まれます）\n" +
                "③ 下の欄にそれぞれドラッグして「組み立てる」\n" +
                "※ 同じステージ名で作り直すと、前のものは置き換わります（Ctrl+Z で戻せます）", MessageType.Info);

            GltfGUI();
            GlbField();
            json = (TextAsset)EditorGUILayout.ObjectField("Unity用JSON", json, typeof(TextAsset), false);
            if (json != null && !json.name.Contains("unity")) EditorGUILayout.HelpBox("ファイル名に .unity.json が付いた方（Unity用JSON）を指定してください。プロジェクト保存用の .stagekobo.json では組み立てられません。", MessageType.Warning);
            outFolder = EditorGUILayout.TextField("生成物の保存先", outFolder);
            axis = (SKAxisMode)EditorGUILayout.EnumPopup(new GUIContent("軸の向き", "Auto で自動判定。左右や前後が逆になったときだけ手動で切り替えます"), axis);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("作るもの", EditorStyles.boldLabel);
            led = EditorGUILayout.ToggleLeft("LEDモニター・LEDパネル（ドット表示シェーダー）", led);
            cams = EditorGUILayout.ToggleLeft("ステージカメラ 2台 ＋ RenderTexture（モニターにライブ映像）", cams);
            beams = EditorGUILayout.ToggleLeft("照明のビーム・レーザー", beams);
            pinLight = EditorGUILayout.ToggleLeft("    ピンスポットを実際のライトにする（演者を照らす）", pinLight);
            spots = EditorGUILayout.ToggleLeft(new GUIContent("演者を照らすスポットライト（本物のライト 2 灯・客席の上から）",
                "ステージの照明はほとんどが見た目だけなので、ステージに立ったアバターは照らされません。本物の Spot Light で照らします。\n" +
                "ふだんはステージに固定。UdonSharp 版は操作パネルの「自分→スポット1／2」で、押した人を追います"), spots);
            using (new EditorGUI.DisabledScope(!spots))
                spotPower = EditorGUILayout.Slider(new GUIContent("    スポットの明るさ", "1 = ふつう。ステージの LED に負けて暗く見えるときは上げる（あとから変えるときは組み立て直し。明るさは「照明ぜんぶ」の点灯・暗転で Animator が決めるため）"), spotPower, 0.3f, 2.5f);
            stageLight = EditorGUILayout.ToggleLeft(new GUIContent("ステージを照らすライト（本物のライト 2 灯・照明の色）",
                "ステージの手前の上から、左右 2 灯で演者を照明の色に染めます。色は「色（パレット）」、明るさは「照明ぜんぶ」の点灯・暗転・ストロボに合わせて変わります（重いので Quest では外してもOK）"), stageLight);
            using (new EditorGUI.DisabledScope(!stageLight))
                stageLightPower = EditorGUILayout.Slider(new GUIContent("    ステージのライトの明るさ", "1 = ふつう（あとから変えるときは組み立て直し）"), stageLightPower, 0.2f, 2.5f);
            wash = EditorGUILayout.ToggleLeft("カラーウォッシュ（舞台の床を照明の色で照らす・軽い）", wash);
            crowd = EditorGUILayout.ToggleLeft("客席の人・ペンライトを拍に合わせて動かす（シェーダー）", crowd);
            performers = EditorGUILayout.ToggleLeft("演者ダミーを踊らせる", performers);
            anim = EditorGUILayout.ToggleLeft("演出を Animator に焼き込む（動き・色・明るさ・レーザー・カメラワーク）", anim);
            remote = EditorGUILayout.ToggleLeft(new GUIContent("    操作パネル（手前・持ち運べる。レーザーで押す）", "横長のパネル。タブで 照明／VJ／VJ の効果／特効・カメラ／モニター を切り替えます。UdonSharp 版は取っ手を持って運べて、「元の場所に戻す」もあります"), remote);
            UdonGUI();
            vj = EditorGUILayout.ToggleLeft(new GUIContent("VJ（ブラウザ版の VJ リモコンの映像を LED モニター・背景 LED に映す）", "すてーじ工房 v0.7 以降で書き出した JSON のとき。デッキA/B・エフェクトをシェーダーで描きます"), vj);
            using (new EditorGUI.DisabledScope(!anim || !SKUdon.Ready || !udon))
                desks = EditorGUILayout.ToggleLeft(new GUIContent("    ステージ裏にも同じ操作パネル（確認用のプレビュー付き）", "UdonSharp 版のときだけ。背景の裏に置きます（v0.10 までの物理スイッチの卓の代わり）"), desks);
            fx = EditorGUILayout.ToggleLeft("特効パーティクル（スパーク・紙吹雪・スモーク）", fx);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(18);
                if (GUILayout.Button(new GUIContent("＋ 好きなパーティクルを特効ボタンにする…", "ワールドに置いたパーティクルを、操作パネルのボタンで出せるようにします（特効の設定ウィンドウ）"))) SKFxWindow.OpenWindow();
            }
            glitter = EditorGUILayout.ToggleLeft("    空中のキラキラ", glitter);
            colliders = EditorGUILayout.ToggleLeft("床・階段にコライダー（歩けるようにする）", colliders);
            probe = EditorGUILayout.ToggleLeft("床の映り込み（Reflection Probe）", probe);
            AudioGUI();

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(glb == null || json == null))
            {
                if (GUILayout.Button("組み立てる", GUILayout.Height(38))) Run();
            }
            if (!string.IsNullOrEmpty(log))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("結果", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(log, log.StartsWith("エラー") ? MessageType.Error : MessageType.None);
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "演出は Animator のステート（例：Move_wave / Col_rainbow / Pal_3 / Dim_pulse / Laser_fan / Master_Off / Shot_closeup）。\n" +
                "UdonSharp 版：操作パネルの操作が全員に同期し、BPM（±・TAP）と動きの速さ（×½/×2）も変えられます。\n" +
                "Animator だけの版：同じ名前のトリガーを SetTrigger すると切り替わります（押した人の画面だけ・テンポは書き出したときの BPM）。", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>GLB の欄。GLB がモデルとして取り込まれていないときは、理由と直し方を出す</summary>
        void GlbField()
        {
            if (glbAsset == null && glb != null) glbAsset = glb;
            if (glbAsset == null && !string.IsNullOrEmpty(glbPath)) glbAsset = AssetDatabase.LoadMainAssetAtPath(glbPath);
            EditorGUI.BeginChangeCheck();
            glbAsset = EditorGUILayout.ObjectField("GLB（取り込んだモデル）", glbAsset, typeof(UnityEngine.Object), false);
            if (EditorGUI.EndChangeCheck()) glbPath = glbAsset != null ? AssetDatabase.GetAssetPath(glbAsset) : null;
            // 読み込み直しでモデルになっていたら、そのモデルに差し替える
            if (glbAsset != null && !(glbAsset is GameObject) && !string.IsNullOrEmpty(glbPath))
            {
                var main = AssetDatabase.LoadMainAssetAtPath(glbPath) as GameObject;
                if (main != null) glbAsset = main;
            }
            glb = glbAsset as GameObject;
            if (glbAsset == null || glb != null) return;
            string path = AssetDatabase.GetAssetPath(glbAsset);
            bool isGltf = path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase);
            if (!isGltf)
            {
                EditorGUILayout.HelpBox("ここにはブラウザで書き出した GLB（.glb）を入れてください。", MessageType.Warning);
                return;
            }
            if (!SKGltf.Installed)
            {
                EditorGUILayout.HelpBox("この GLB はまだ「モデル」として読み込まれていません（Unity は最初から GLB を読めません）。\n上の「glTFast を入れる」を押してください。入ると GLB が自動でモデルになります。", MessageType.Warning);
                return;
            }
            EditorGUILayout.HelpBox("glTFast は入っていますが、この GLB はまだモデルになっていません。下のボタンで読み込み直してください。", MessageType.Warning);
            if (GUILayout.Button("この GLB を読み込み直す"))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                glbAsset = AssetDatabase.LoadMainAssetAtPath(path);
                glb = glbAsset as GameObject;
            }
        }

        /// <summary>GLB を読むパッケージ（glTFast / UniGLTF）が無いときに、入れるボタンを出す</summary>
        void GltfGUI()
        {
            if (SKGltf.Installed) return;
            if (SKGltf.Busy)
            {
                EditorGUILayout.HelpBox("glTFast を入れています…（1〜2分。終わると Unity がスクリプトを読み込み直します）", MessageType.Info);
                Repaint();
                return;
            }
            EditorGUILayout.HelpBox("GLB をモデルとして読み込むパッケージ（glTFast）が入っていません。\nUnity は最初から GLB を読めないので、下のボタンで入れてください（Unity 公式のパッケージです）。" +
                (string.IsNullOrEmpty(SKGltf.Error) ? "" : "\n\n前回のエラー：" + SKGltf.Error), MessageType.Warning);
            if (GUILayout.Button("glTFast を入れる（GLB を読めるようにする）", GUILayout.Height(28))) SKGltf.Install();
        }

        /// <summary>曲の音に反応（AudioLink）・YamaPlayer との連携</summary>
        void AudioGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("曲の音に合わせる（AudioLink・YamaPlayer）", EditorStyles.boldLabel);
            bool alOk = SKAudio.ALInstalled, yamaOk = SKAudio.YamaInstalled;
            // 数えるのは Layout のときだけ（Layout と Repaint でボタンの数が変わると GUI がエラーになるので）
            if (Event.current.type == EventType.Layout && EditorApplication.timeSinceStartup >= nextScan)
            {
                nextScan = EditorApplication.timeSinceStartup + 1.0;
                sceneAL = alOk ? SKAudio.InScene(SKAudio.ALType).Count : 0;
                var ys = yamaOk ? SKAudio.InScene(SKAudio.YamaType) : new System.Collections.Generic.List<Component>();
                sceneYama = ys.Count;
                sceneYamaLinked = 0;
                foreach (var y in ys) if (SKAudio.IsLinked(y)) sceneYamaLinked++;
            }
            audioLink = EditorGUILayout.ToggleLeft(new GUIContent("AudioLink（曲の音）に反応する（照明・LED・床の色・客席・ペンライト・VJ）",
                "低音（キック）でビームやレンズが跳ね、客席が弾み、LED の模様とVJ の「ドン」が音に合います。曲が止まっているときは、いつもの BPM の演出のままです"), audioLink);
            using (new EditorGUI.DisabledScope(!audioLink))
            {
                alAmount = EditorGUILayout.Slider(new GUIContent("    反応の強さ", "小さいと控えめ、1 で音だけで明るさが決まります（あとから変えるときは組み立て直し）"), alAmount, 0.1f, 1f);
                using (new EditorGUI.DisabledScope(!yamaOk))
                {
                    bool v = EditorGUILayout.ToggleLeft(new GUIContent("    YamaPlayer の音で動かす（YamaPlayer → AudioLink を自動でつなぐ）" + (yamaOk ? "" : "　※ YamaPlayer が入っていません"),
                        "YamaPlayer の「設定 → 外部設定 → Audio Link」をオンにして、シーンの AudioLink を選びます。再生・一時停止・停止もステージに伝わります"), yama && yamaOk);
                    if (yamaOk) yama = v;
                }
                if (audioLink)
                {
                    if (!alOk)
                        EditorGUILayout.HelpBox("AudioLink がプロジェクトに入っていません。VRChat Creator Companion（VCC）の「Manage Project」で AudioLink を追加すると、音に反応するようになります。\n" +
                            "（このまま組み立てても大丈夫です。AudioLink を入れたら下の「AudioLink をつなぐ」を押すだけで、作り直しはいりません）", MessageType.Warning);
                    else
                    {
                        string s = sceneAL > 0 ? "シーンの AudioLink：" + sceneAL + " 個" : "シーンに AudioLink はまだありません（組み立てるときに置きます）";
                        if (yamaOk) s += "\nシーンの YamaPlayer：" + sceneYama + " 台" + (sceneYama > 0 ? "（AudioLink につながっている：" + sceneYamaLinked + " 台）" : "");
                        s += "\n曲が止まっている・一時停止中は、いつもの BPM の演出に戻ります。" + (udon && SKUdon.Ready ? "操作パネルの「曲の音に反応」で ON/OFF もできます。" : "");
                        EditorGUILayout.HelpBox(s, MessageType.None);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (yamaOk && yama && sceneYama == 0 && GUILayout.Button(new GUIContent("YamaPlayer を置く", "画面・操作パネル付きの YamaPlayer をシーンに置きます（ステージの外。置いたら好きな場所に動かしてください）")))
                            {
                                if (SKAudio.AddYamaPlayer() == null) linkLog = "⚠ YamaPlayer のプレハブが見つかりませんでした。メニュー GameObject → YamaPlayer から置いてください";
                                else linkLog = "YamaPlayer を置きました（ヒエラルキーで選ばれています。好きな場所に動かしてください）。続けて「AudioLink をつなぐ」を押すと、ステージを作り直さずにつなげます";
                                nextScan = 0;
                            }
                            if (GUILayout.Button(new GUIContent(yamaOk && yama ? "AudioLink をつなぐ（YamaPlayer も）" : "AudioLink を用意する", "シーンに AudioLink が無ければ置いて、YamaPlayer をつなぎます。ステージは作り直しません")))
                            {
                                var sb = new System.Text.StringBuilder();
                                SKAudio.Link(x => sb.AppendLine(x), yamaOk && yama, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                                linkLog = sb.ToString().TrimEnd();
                                Debug.Log("[すてーじ工房]\n" + linkLog);
                                nextScan = 0;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(linkLog)) EditorGUILayout.HelpBox(linkLog, MessageType.None);
                }
            }
            // YamaPlayer の音の範囲（AudioLink を使わなくても使える）
            using (new EditorGUI.DisabledScope(!yamaOk))
            {
                bool v = EditorGUILayout.ToggleLeft(new GUIContent("YamaPlayer の音をステージから会場ぜんたいに鳴らす" + (yamaOk ? "" : "　※ YamaPlayer が入っていません"),
                    "YamaPlayer の音の出口をステージ（スピーカーのアセットがあればそのまん中）に動かして、会場のどこでも聞こえる距離にします（会場の大きさから自動。YamaPlayer の音量・ミュートはそのまま使えます）"), yamaSound && yamaOk);
                if (yamaOk) yamaSound = v;
            }
            using (new EditorGUI.DisabledScope(!yamaOk))
            {
                bool v = EditorGUILayout.ToggleLeft(new GUIContent("YamaPlayer の映像をモニター・背景の LED に映せるようにする（操作パネルで「映像」を選ぶ）",
                    "YamaPlayer の画面（スクリーン）に、ステージの LED のマテリアルを足します。映すかどうかは操作パネルの「モニター・カメラ」タブで選びます"), video && yamaOk);
                if (yamaOk) video = v;
            }
            if (yamaOk && (yamaSound || video) && sceneYama > 0)
            {
                if (GUILayout.Button(new GUIContent("YamaPlayer をステージにつなぐ（音の範囲・映像。ステージは作り直さない）", "あとから YamaPlayer を置いた・動かしたときに押します")))
                {
                    var sb = new System.Text.StringBuilder();
                    var root = SKAudio.FindStageRoot();
                    SKAudio.SpreadSound(x => sb.AppendLine(x), root);
                    if (video) SKAudio.LinkVideo(x => sb.AppendLine(x), SKAudio.VideoMaterials(root));
                    soundLog = sb.ToString().TrimEnd();
                    Debug.Log("[すてーじ工房]\n" + soundLog);
                }
                if (!string.IsNullOrEmpty(soundLog)) EditorGUILayout.HelpBox(soundLog, MessageType.None);
            }
        }

        /// <summary>UdonSharp 版の状態表示と「有効にする」ボタン</summary>
        void UdonGUI()
        {
            using (new EditorGUI.DisabledScope((!remote && !desks) || !anim))
            {
                if (SKUdon.Ready)
                {
                    udon = EditorGUILayout.ToggleLeft("    UdonSharp 版にする（全員に同期・テンポ変更・TAP）", udon);
                }
                else if (SKUdon.Installed)
                {
                    if (EditorApplication.isCompiling)
                        EditorGUILayout.HelpBox("UdonSharp 版を準備中です（スクリプトの再コンパイル待ち）。終わるとここにチェックが出ます。", MessageType.Info);
                    else if (SKUdon.DefineSet())
                        EditorGUILayout.HelpBox("STAGEKOBO_UDON は設定済みですが、UdonSharp 版のスクリプトが有効になっていません。\nConsole に赤いエラーが出ていないか確認してください（Runtime フォルダが取り込まれているかも確認）。", MessageType.Warning);
                    else
                    {
                        EditorGUILayout.HelpBox("UdonSharp が見つかりました。下のボタンで UdonSharp 版（全員に同期・テンポ変更）を使えるようにします。\n（スクリプトの再コンパイルが1回走ります）", MessageType.Info);
                        if (GUILayout.Button("UdonSharp 版を有効にする")) SKUdon.EnableDefine();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("UdonSharp が入っていないので、操作パネルは Animator だけの版（押した人の画面だけ切り替わる）になります。\nVRChat Creator Companion で作ったワールドのプロジェクトなら UdonSharp は最初から入っています。", MessageType.None);
                }
            }
        }

        SKBuildSettings Settings()
        {
            return new SKBuildSettings
            {
                glb = glb, json = json, outFolder = outFolder, axis = axis,
                led = led, cams = cams, beams = beams, anim = anim, fx = fx, remote = remote, colliders = colliders, glitter = glitter, pinLight = pinLight,
                wash = wash, crowd = crowd, performers = performers, udon = udon, probe = probe, vj = vj, desks = desks,
                audioLink = audioLink, alAmount = alAmount, yama = yama, yamaSound = yamaSound, video = video, stageLight = stageLight, stageLightPower = stageLightPower, spots = spots, spotPower = spotPower,
            };
        }

        void Run()
        {
            var cfg = Settings();
            SaveLast(cfg);
            log = BuildWith(cfg);
            nextScan = 0;
        }

        static string BuildWith(SKBuildSettings cfg)
        {
            try
            {
                string l = SKBuilder.Build(cfg);
                Debug.Log("[すてーじ工房]\n" + l);
                return l;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return "エラー：" + e.Message;
            }
        }

        // ------------------------------------------------------------------ 前回の組み立ての設定（ウィンドウを開き直しても欄が空にならない・特効の設定から作り直せる）

        [Serializable]
        class Saved
        {
            public string glbPath, jsonPath, outFolder;
            public int axis;
            public bool led = true, cams = true, beams = true, anim = true, fx = true, remote = true, colliders = true, glitter = true, pinLight = true;
            public bool wash = true, crowd = true, performers = true, udon = true, probe = true, vj = true, desks = true;
            public bool audioLink = true, yama = true, spots = true, yamaSound = true, video = true, stageLight = true;
            public float stageLightPower = 1f;
            public float alAmount = 0.7f, spotPower = 1f;
        }

        static string PrefKey { get { return "Washitsu.StageKobo.Importer.Last." + Application.dataPath.GetHashCode().ToString("x"); } }

        static void SaveLast(SKBuildSettings c)
        {
            var s = new Saved
            {
                glbPath = c.glb != null ? AssetDatabase.GetAssetPath(c.glb) : null, jsonPath = c.json != null ? AssetDatabase.GetAssetPath(c.json) : null, outFolder = c.outFolder, axis = (int)c.axis,
                led = c.led, cams = c.cams, beams = c.beams, anim = c.anim, fx = c.fx, remote = c.remote, colliders = c.colliders, glitter = c.glitter, pinLight = c.pinLight,
                wash = c.wash, crowd = c.crowd, performers = c.performers, udon = c.udon, probe = c.probe, vj = c.vj, desks = c.desks,
                audioLink = c.audioLink, yama = c.yama, yamaSound = c.yamaSound, video = c.video, stageLight = c.stageLight, stageLightPower = c.stageLightPower, spots = c.spots, alAmount = c.alAmount, spotPower = c.spotPower,
            };
            EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(s));
        }

        /// <summary>前回「組み立てる」を押したときの設定（無ければ null）</summary>
        static SKBuildSettings LoadLast()
        {
            string js = EditorPrefs.GetString(PrefKey, "");
            if (string.IsNullOrEmpty(js)) return null;
            Saved s;
            try { s = JsonUtility.FromJson<Saved>(js); } catch (Exception) { return null; }
            if (s == null) return null;
            return new SKBuildSettings
            {
                glb = !string.IsNullOrEmpty(s.glbPath) ? AssetDatabase.LoadMainAssetAtPath(s.glbPath) as GameObject : null,
                json = !string.IsNullOrEmpty(s.jsonPath) ? AssetDatabase.LoadAssetAtPath<TextAsset>(s.jsonPath) : null,
                outFolder = string.IsNullOrEmpty(s.outFolder) ? "Assets/和室/StageKobo_Generated" : s.outFolder, axis = (SKAxisMode)s.axis,
                led = s.led, cams = s.cams, beams = s.beams, anim = s.anim, fx = s.fx, remote = s.remote, colliders = s.colliders, glitter = s.glitter, pinLight = s.pinLight,
                wash = s.wash, crowd = s.crowd, performers = s.performers, udon = s.udon, probe = s.probe, vj = s.vj, desks = s.desks,
                audioLink = s.audioLink, yama = s.yama, yamaSound = s.yamaSound, video = s.video, stageLight = s.stageLight, stageLightPower = s.stageLightPower, spots = s.spots, alAmount = s.alAmount, spotPower = s.spotPower,
            };
        }

        void OnEnable()
        {
            if (glbAsset != null || json != null) return;   // 開いているあいだに入れたものはそのまま
            var c = LoadLast();
            if (c == null) return;
            glb = c.glb; glbAsset = c.glb; glbPath = c.glb != null ? AssetDatabase.GetAssetPath(c.glb) : null; json = c.json; outFolder = c.outFolder; axis = c.axis;
            led = c.led; cams = c.cams; beams = c.beams; anim = c.anim; fx = c.fx; remote = c.remote; colliders = c.colliders; glitter = c.glitter; pinLight = c.pinLight;
            wash = c.wash; crowd = c.crowd; performers = c.performers; udon = c.udon; probe = c.probe; vj = c.vj; desks = c.desks;
            audioLink = c.audioLink; yama = c.yama; yamaSound = c.yamaSound; video = c.video; stageLight = c.stageLight; stageLightPower = c.stageLightPower; spots = c.spots; alAmount = c.alAmount; spotPower = c.spotPower;
        }

        /// <summary>前回と同じ設定で組み立て直す（特効の設定ウィンドウの「反映」から）。null = 前回の設定が無い</summary>
        public static string RebuildLast()
        {
            var c = LoadLast();
            if (c == null || c.glb == null || c.json == null) return null;
            string l = BuildWith(c);
            foreach (var w in Resources.FindObjectsOfTypeAll<StageKoboImporterWindow>()) { w.log = l; w.Repaint(); }
            return l;
        }

        public static void OpenWindow() { Open(); }
    }

    /// <summary>
    /// GLB を読むパッケージの確認と、glTFast のインストール。
    /// 6.13.1 に固定しているのは、6.14 以降が Burst 1.8.24 を求め、VRChat SDK の入ったプロジェクトのパッケージを上げてしまうため。
    /// </summary>
    public static class SKGltf
    {
        public const string Package = "com.unity.cloud.gltfast@6.13.1";
        static bool? installed;
        static AddRequest req;
        public static string Error = "";

        /// <summary>glTFast か UniGLTF の GLB 取り込み（ScriptedImporter）があるか</summary>
        public static bool Installed
        {
            get
            {
                if (installed == null)
                {
                    installed = false;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        string n = asm.GetName().Name;
                        if (n.StartsWith("glTFast", StringComparison.OrdinalIgnoreCase) || n.StartsWith("UniGLTF", StringComparison.OrdinalIgnoreCase)) { installed = true; break; }
                    }
                }
                return installed.Value;
            }
        }
        public static bool Busy => req != null && !req.IsCompleted;

        public static void Install()
        {
            if (Busy) return;
            Error = "";
            req = Client.Add(Package);
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (req == null || !req.IsCompleted) return;
            EditorApplication.update -= Poll;
            if (req.Status == StatusCode.Success)
            {
                Debug.Log("[すてーじ工房] glTFast を入れました：" + req.Result.version + "\nGLB は自動でモデルとして読み込まれます（されないときはインポーターの「この GLB を読み込み直す」）。");
                installed = null;
            }
            else
            {
                Error = req.Error != null ? req.Error.message : "不明なエラー";
                Debug.LogError("[すてーじ工房] glTFast を入れられませんでした：" + Error + "\nWindow → Package Manager → ＋ → Add package by name… に com.unity.cloud.gltfast と入れても入れられます。");
            }
            req = null;
        }
    }
}
