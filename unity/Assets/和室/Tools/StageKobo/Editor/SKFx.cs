using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>追加の特効 1 個分（組み立てのときに StageKoboFxSettings から読む）</summary>
    public class SKFxCustom
    {
        public string name;
        public List<ParticleSystem> systems = new List<ParticleSystem>();
        public List<AudioSource> audio = new List<AudioSource>();
        public int mode;        // 0 = 1回、1 = ON/OFF
        public float secs;
        public int Group(int k) { return 3 + k; }
    }

    /// <summary>
    /// 追加の特効：シーンの「特効の設定」（StageKoboFxSettings）を読んで、リモコン・照明卓・UdonSharp 版のコントローラーへ渡す。
    /// 設定はステージの外（シーン直下の EditorOnly のオブジェクト）にあるので、ステージを作り直しても消えない。
    /// </summary>
    public static class SKFx
    {
        public const string ObjectName = "StageKobo_FX（特効の設定）";

        /// <summary>開いているシーンの設定（無ければ null）</summary>
        public static StageKoboFxSettings Find()
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(typeof(StageKoboFxSettings)))
            {
                var c = o as StageKoboFxSettings;
                if (c == null || EditorUtility.IsPersistent(c)) continue;
                if (!c.gameObject.scene.IsValid() || !c.gameObject.scene.isLoaded) continue;
                return c;
            }
            return null;
        }

        /// <summary>設定を作る（シーン直下・EditorOnly。ワールドには入らない）</summary>
        public static StageKoboFxSettings Create()
        {
            var go = new GameObject(ObjectName);
            go.tag = "EditorOnly";
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            var s = go.AddComponent<StageKoboFxSettings>();
            Undo.RegisterCreatedObjectUndo(go, "特効の設定を作る");
            return s;
        }

        /// <summary>ステージ（StageKobo_～）の中にあるか。作り直すと消えるので、追加の特効には使えない</summary>
        public static bool InsideStage(Transform t)
        {
            for (var p = t; p != null; p = p.parent) if (p.parent == null && p.name.StartsWith("StageKobo_") && p.name != ObjectName) return true;
            return false;
        }

        /// <summary>EditorOnly のオブジェクトの中にあるか（ワールドを作るときに消えるので使えない）</summary>
        public static bool EditorOnly(Transform t)
        {
            for (var p = t; p != null; p = p.parent) if (p.CompareTag("EditorOnly")) return true;
            return false;
        }

        /// <summary>組み立ての最初に呼ぶ（古いステージを消す前に）</summary>
        public static void Read(SKContext ctx)
        {
            var s = Find();
            ctx.fxBuiltin = s == null || s.showBuiltin;
            if (s == null) return;
            int skipped = 0, edOnly = 0;
            foreach (var e in s.entries.Take(StageKoboFxSettings.MaxEntries))
            {
                if (e == null) continue;
                var c = new SKFxCustom { name = string.IsNullOrWhiteSpace(e.name) ? "特効" + (ctx.fxCustom.Count + 1) : e.name.Trim(), mode = Mathf.Clamp(e.mode, 0, 1), secs = Mathf.Max(0f, e.seconds) };
                foreach (var ps in e.systems)
                {
                    if (ps == null) continue;
                    if (InsideStage(ps.transform)) { skipped++; continue; }
                    if (EditorOnly(ps.transform)) { edOnly++; continue; }
                    if (!c.systems.Contains(ps)) c.systems.Add(ps);
                    if (e.sound)
                        foreach (var a in ps.GetComponentsInChildren<AudioSource>(true))
                            if (!c.audio.Contains(a)) c.audio.Add(a);
                }
                if (c.systems.Count == 0) { ctx.Log("⚠ 追加の特効「" + c.name + "」はパーティクルが入っていないので、ボタンを作りませんでした"); continue; }
                ctx.fxCustom.Add(c);
            }
            if (s.entries.Count > StageKoboFxSettings.MaxEntries) ctx.Log("⚠ 追加の特効は " + StageKoboFxSettings.MaxEntries + " 個までです（それより後ろは作りませんでした）");
            if (skipped > 0) ctx.Log("⚠ 追加の特効のパーティクルのうち " + skipped + " 個はステージ（StageKobo_～）の中にあったので使いませんでした（作り直すと消えるため）。ステージの外に置いてください");
            if (edOnly > 0) ctx.Log("⚠ 追加の特効のパーティクルのうち " + edOnly + " 個は EditorOnly のオブジェクトの中にあったので使いませんでした（ワールドに入らないため）。特効の設定のオブジェクトの中には置かないでください");
            if (ctx.fxCustom.Count > 0)
                ctx.Log("追加の特効 " + ctx.fxCustom.Count + " 個：" + string.Join("・", ctx.fxCustom.Select(c => c.name + (c.mode == 1 ? "（ON/OFF）" : ""))) + (ctx.fxBuiltin ? "" : "（最初からある特効のボタンは出しません）"));
        }

        /// <summary>
        /// 卓のボタンの上に、追加の特効の名前を出す（文字のテクスチャには好きな名前が入っていないので、UI の文字で重ねる）。
        /// ボタンの GameObject は盤面の向き（ローカル Y = 盤面の上、Z = 奥）になっている
        /// </summary>
        public static void DeskLabel(GameObject button, string text, float capW, float capD, float capH)
        {
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (System.Exception) { }
            if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (System.Exception) { } }
            const float px = 0.0004f;   // 1px = 0.4mm
            var go = new GameObject("Label", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(button.transform, false);
            // 文字はボタンの天板の上（手前のランプの帯をよけて少し奥）。天板と同じ向きに寝かせる
            go.transform.localPosition = new Vector3(0, capH + 0.0007f, capD * 0.055f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * px;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;   // 文字をくっきり
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(capW * 0.9f / px, capD * 0.74f / px);
            var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)tgo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            var t = tgo.GetComponent<Text>();
            t.text = text; t.font = font; t.color = new Color(0.95f, 0.95f, 0.97f);
            t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 8; t.resizeTextMaxSize = 34; t.fontSize = 34;
            t.lineSpacing = 0.9f;
        }
    }
}
