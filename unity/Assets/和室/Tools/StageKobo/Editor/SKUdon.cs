using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// UdonSharp 版のリモコンを組み立てる。
    /// ・UdonSharp が入っていないプロジェクトでもエラーにならないよう、実行時のスクリプト（Runtime/StageKobo*.cs）は
    ///   シンボル STAGEKOBO_UDON があるときだけ有効になる。インポーターのボタンでシンボルを追加する。
    /// ・UdonSharp のエディタ機能はバージョンで名前が変わることがあるので、リフレクションで探して呼ぶ。
    /// </summary>
    public static class SKUdon
    {
        public const string Define = "STAGEKOBO_UDON";

        public class Item
        {
            public Button button;
            public int ch, val;
            public GameObject mark;
        }

        /// <summary>操作パネル（UI）のボタン1個。kind 0 = 照明（StageKoboController）、1 = VJ（StageKoboVJ）</summary>
        public class PanelItem
        {
            public Button button;
            public int kind, ch, val;
            public GameObject mark;
        }

        /// <summary>操作パネルの文字・ランプ・持ち運び（パネルの数だけ）</summary>
        public class PanelParts
        {
            public List<Text> status = new List<Text>(), infoA = new List<Text>(), infoB = new List<Text>(), infoC = new List<Text>();
            public List<GameObject> lamps = new List<GameObject>();
            public Transform handle, canvas, backCenter;
            public Component sync;
        }

        /// <summary>ステージ裏の卓のボタン1個</summary>
        public class DeskButton
        {
            public GameObject go;
            public int desk, slot, kind, act, val;
            public bool lamp;
            public Material mat;
            public string text;
        }

        /// <summary>UdonSharp がプロジェクトに入っているか</summary>
        public static bool Installed { get { return FindType("UdonSharp.UdonSharpBehaviour") != null; } }

        /// <summary>UdonSharp 版のスクリプトが有効（コンパイル済み）か</summary>
        public static bool Compiled
        {
            get
            {
#if STAGEKOBO_UDON
                return true;
#else
                return false;
#endif
            }
        }

        public static bool Ready { get { return Installed && Compiled; } }

        static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS };

        public static bool DefineSet()
        {
            return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone).Split(';').Contains(Define);
        }

        /// <summary>STAGEKOBO_UDON を PC・Quest(Android)・iOS に追加する（このあと Unity が再コンパイルする）</summary>
        public static void EnableDefine()
        {
            foreach (var t in Targets)
            {
                string s = PlayerSettings.GetScriptingDefineSymbols(t) ?? "";
                var list = s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (list.Contains(Define)) continue;
                list.Add(Define);
                PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", list));
            }
        }

        public static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName); } catch (Exception) { }
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>UdonSharp の「プログラムアセット」（.asset）が無ければ作る。組み立ての最初に呼ぶ</summary>
        public static bool PrepareProgramAssets(SKContext ctx)
        {
#if STAGEKOBO_UDON
            try
            {
                bool made = EnsureProgramAsset(ctx, typeof(StageKoboController)) | EnsureProgramAsset(ctx, typeof(StageKoboButton)) | EnsureProgramAsset(ctx, typeof(StageKoboVJ));
                if (HasProgramAsset(typeof(StageKoboController)) && HasProgramAsset(typeof(StageKoboButton)) && HasProgramAsset(typeof(StageKoboVJ)))
                {
                    if (made) TryCompile(ctx);
                    return true;
                }
                ctx.Log("⚠ UdonSharp のプログラムアセットを作れませんでした");
            }
            catch (Exception e)
            {
                ctx.Log("⚠ UdonSharp の準備でエラー：" + e.Message);
                Debug.LogException(e);
            }
#endif
            return false;
        }

        /// <summary>
        /// UdonSharp 版のコントローラー（StageKoboController）を1個だけ作る（リモコン・ステージ裏の卓・VJ で共有）。
        /// 作れなかったら null
        /// </summary>
        public static Component EnsureController(SKContext ctx)
        {
#if STAGEKOBO_UDON
            if (ctx.controller != null) return ctx.controller;
            if (!ctx.udon) return null;
            GameObject cgo = null;
            try
            {
                cgo = new GameObject("StageKoboController");
                cgo.transform.SetParent(ctx.root.transform, false);
                var ctrl = AddUSharp(cgo, typeof(StageKoboController)) as StageKoboController;
                if (ctrl == null) throw new Exception("StageKoboController を追加できませんでした");

                // ---- チャンネル（Animator のレイヤー）の情報を平らな配列にして渡す
                var names = new List<string>(); var lens = new List<float>(); var modes = new List<int>();
                int n = SKCh.N;
                var chStart = new int[n]; var chCount = new int[n]; var chAnim = new int[n]; var chLayer = new int[n]; var chFade = new float[n]; var chDefault = new int[n];
                for (int c = 0; c < n; c++)
                {
                    var ch = ctx.channels[c];
                    chStart[c] = names.Count;
                    if (ch == null || ch.names.Count == 0) continue;
                    chCount[c] = ch.names.Count; chAnim[c] = ch.anim; chLayer[c] = ch.layer; chFade[c] = ch.fade; chDefault[c] = Mathf.Clamp(ch.def, 0, ch.names.Count - 1);
                    names.AddRange(ch.names); lens.AddRange(ch.lens); modes.AddRange(ch.modes);
                }
                ctrl.anims = new[] { ctx.lightAnimator, ctx.camAnimators[0], ctx.camAnimators[1] };
                ctrl.bakedBpm = ctx.bpm;
                ctrl.startBpm = ctx.bpm;
                ctrl.bpm = ctx.bpm;
                ctrl.stNames = names.ToArray(); ctrl.stLens = lens.ToArray(); ctrl.stModes = modes.ToArray();
                ctrl.chStart = chStart; ctrl.chCount = chCount; ctrl.chAnim = chAnim; ctrl.chLayer = chLayer; ctrl.chFade = chFade; ctrl.chDefault = chDefault;
                var fxs = new List<ParticleSystem>(); var fxg = new List<int>();
                var groups = new[] { ctx.sparks, ctx.confetti, ctx.smoke };
                for (int g = 0; g < groups.Length; g++) foreach (var ps in groups[g]) { fxs.Add(ps); fxg.Add(g); }
                // 追加の特効（グループ 3 + k）
                var fxa = new List<AudioSource>(); var fxag = new List<int>();
                for (int k = 0; k < ctx.fxCustom.Count; k++)
                {
                    var c = ctx.fxCustom[k];
                    foreach (var ps in c.systems) { fxs.Add(ps); fxg.Add(3 + k); }
                    foreach (var a in c.audio) { fxa.Add(a); fxag.Add(3 + k); }
                }
                ctrl.fxSystems = fxs.ToArray(); ctrl.fxGroups = fxg.ToArray();
                ctrl.fxModes = ctx.fxCustom.Select(c => c.mode).ToArray();
                ctrl.fxSecs = ctx.fxCustom.Select(c => c.secs).ToArray();
                ctrl.fxAudio = fxa.ToArray(); ctrl.fxAudioGroups = fxag.ToArray();
                ctrl.fxOn = new bool[ctx.fxCustom.Count];
                ctrl.marks = new GameObject[0]; ctrl.markCh = new int[0]; ctrl.markVal = new int[0];
                // カメラのアップで顔を追う（登録した人）：ブラウザ版の faceDist / faceFov と同じ値
                ctrl.stageCams = new[] { ctx.cams[0], ctx.cams[1] };
                ctrl.upFwd = ctx.DirToWorld(new Vector3(0, 0, 1)).normalized;
                ctrl.upSide = ctx.DirToWorld(new Vector3(1, 0, 0)).normalized;
                ctrl.upDist = new float[2]; ctrl.upFov = new float[2]; ctrl.upSpeed = new float[2];
                var poses = J.L(ctx.data, "stageCameras");
                for (int k = 0; k < 2; k++)
                {
                    var cj = J.O(ctx.state, "cams", k == 0 ? "cam1" : "cam2");
                    float dist = J.F(cj, "dist", k == 0 ? 8f : 3.5f), fov = J.F(cj, "fov", 30f);
                    ctrl.upDist[k] = k < poses.Count ? J.F(poses[k], "faceDist", SKMath.FaceDist(dist)) : SKMath.FaceDist(dist);
                    ctrl.upFov[k] = k < poses.Count ? J.F(poses[k], "faceFov", SKMath.FaceFov(fov, dist)) : SKMath.FaceFov(fov, dist);
                    ctrl.upSpeed[k] = J.F(cj, "speed", 1f);
                }
                ctrl.hasAudio = ctx.audio;
                ctrl.spots = ctx.spotLights.ToArray();
                ctrl.spotAims = ctx.spotAims.ToArray();
                ctrl.spotBeams = ctx.spotBeams.Select(r => r != null ? r.gameObject : null).ToArray();
                Finish(ctrl);
                ctx.controller = ctrl;
                return ctrl;
            }
            catch (Exception e)
            {
                ctx.Log("⚠ UdonSharp のコントローラーを作れませんでした：" + e.Message);
                Debug.LogException(e);
                if (cgo != null) UnityEngine.Object.DestroyImmediate(cgo);
                return null;
            }
#else
            return null;
#endif
        }

        /// <summary>リモコンのボタンを UdonSharp のコントローラーに配線する。失敗したら false</summary>
        public static bool Wire(SKContext ctx, GameObject remote, List<Item> items, Text status, GameObject lamp)
        {
#if STAGEKOBO_UDON
            var ctrl = EnsureController(ctx) as StageKoboController;
            if (ctrl == null) return false;
            try
            {
                var marked = items.Where(i => i.mark != null).ToList();
                ctrl.marks = marked.Select(i => i.mark).ToArray();
                ctrl.markCh = marked.Select(i => i.ch).ToArray();
                ctrl.markVal = marked.Select(i => i.val).ToArray();
                ctrl.statusText = status;
                ctrl.beatLamp = lamp;
                Finish(ctrl);

                // ---- ボタン：OnClick → UdonBehaviour.SendCustomEvent("Press")
                int wired = 0;
                foreach (var it in items)
                {
                    if (it.button == null || it.ch < 0) continue;
                    var sb = AddUSharp(it.button.gameObject, typeof(StageKoboButton)) as StageKoboButton;
                    if (sb == null) throw new Exception("StageKoboButton を追加できませんでした");
                    sb.controller = ctrl; sb.ch = it.ch; sb.val = it.val;
                    Finish(sb);
                    var ub = Backing(sb);
                    if (ub == null) throw new Exception("UdonBehaviour が見つかりません");
                    var del = (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), ub, "SendCustomEvent");
                    UnityEventTools.AddStringPersistentListener(it.button.onClick, del, "Press");
                    wired++;
                }
                ctx.Log("UdonSharp：リモコンのボタン " + wired + " 個を配線しました");
                return true;
            }
            catch (Exception e)
            {
                ctx.Log("⚠ UdonSharp の配線でエラー：" + e.Message);
                Debug.LogException(e);
                try { Cleanup(items); } catch (Exception e2) { Debug.LogException(e2); }
                ctrl.marks = new GameObject[0]; ctrl.markCh = new int[0]; ctrl.markVal = new int[0]; ctrl.statusText = null; ctrl.beatLamp = null;
                Finish(ctrl);
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// 操作パネル（手前・裏の 2 枚）のボタンを配線する：OnClick → UdonBehaviour.SendCustomEvent("Press")（StageKoboButton）。
        /// 印は照明のボタンは StageKoboController、VJ のボタンは StageKoboVJ が ON/OFF する。失敗したら false（呼び出し側が Animator だけの版に戻す）
        /// </summary>
        public static bool WirePanels(SKContext ctx, List<PanelItem> items, PanelParts parts)
        {
#if STAGEKOBO_UDON
            var ctrl = EnsureController(ctx) as StageKoboController;
            if (ctrl == null) return false;
            var vj = ctx.vj != null ? ctx.vj.ctrl as StageKoboVJ : null;
            var added = new List<Component>();
            try
            {
                var m0 = items.Where(i => i.mark != null && i.kind == 0).ToList();
                ctrl.marks = m0.Select(i => i.mark).ToArray();
                ctrl.markCh = m0.Select(i => i.ch).ToArray();
                ctrl.markVal = m0.Select(i => i.val).ToArray();
                ctrl.statusText = null; ctrl.beatLamp = null;
                ctrl.statusTexts = parts.status.ToArray();
                ctrl.beatLamps = parts.lamps.ToArray();
                ctrl.deskMat = null; ctrl.deskCh = new int[0]; ctrl.deskVal = new int[0];
                ctrl.panelHandle = parts.handle; ctrl.panelCanvas = parts.canvas;
                if (parts.handle != null && parts.canvas != null)
                {
                    ctrl.panelOffPos = parts.handle.InverseTransformPoint(parts.canvas.position);
                    ctrl.panelOffRot = Quaternion.Inverse(parts.handle.rotation) * parts.canvas.rotation;
                }
                ctrl.panelSync = parts.sync as VRC.SDK3.Components.VRCObjectSync;
                if (vj != null) ctrl.vj = vj;
                Finish(ctrl);
                if (vj != null)
                {
                    var m1 = items.Where(i => i.mark != null && i.kind == 1).ToList();
                    vj.uiMarks = m1.Select(i => i.mark).ToArray();
                    vj.uiAct = m1.Select(i => i.ch).ToArray();
                    vj.uiVal = m1.Select(i => i.val).ToArray();
                    vj.infoA = parts.infoA.ToArray(); vj.infoB = parts.infoB.ToArray(); vj.infoC = parts.infoC.ToArray();
                    // 卓（物理スイッチ）はもう作らない
                    vj.deskMats = new Material[0]; vj.btnDesk = new int[0]; vj.btnSlot = new int[0]; vj.btnKind = new int[0]; vj.btnAct = new int[0]; vj.btnVal = new int[0];
                    vj.deskCenter = parts.backCenter;   // 裏のパネルの近くにいる人は、確認用のプレビューのためにデッキ A・B を両方描く
                    var defs = J.O(ctx.data, "vjDefs");
                    vj.genNames = J.L(defs, "gensFull").Select(x => x as string ?? "").ToArray();
                    vj.cmodeNames = J.L(defs, "colorModeNames").Select(x => x as string ?? "").ToArray();
                    vj.mirrorNames = J.L(defs, "mirrors").Select(x => x as string ?? "").ToArray();
                    vj.fxNames = ctx.vj.fxNames ?? new string[0];
                    Finish(vj);
                }
                int wired = 0;
                foreach (var it in items)
                {
                    if (it.button == null || it.ch < 0) continue;
                    var sb = AddUSharp(it.button.gameObject, typeof(StageKoboButton)) as StageKoboButton;
                    if (sb == null) throw new Exception("StageKoboButton を追加できませんでした");
                    added.Add(sb);
                    sb.controller = ctrl; sb.vj = it.kind == 1 ? vj : null; sb.ch = it.ch; sb.val = it.val;
                    sb.deskMat = null;
                    Finish(sb);
                    var ub = Backing(sb);
                    if (ub == null) throw new Exception("UdonBehaviour が見つかりません");
                    added.Add(ub);
                    var del = (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), ub, "SendCustomEvent");
                    UnityEventTools.AddStringPersistentListener(it.button.onClick, del, "Press");
                    wired++;
                }
                ctx.Log("UdonSharp：操作パネルのボタン " + wired + " 個を配線しました");
                return true;
            }
            catch (Exception e)
            {
                ctx.Log("⚠ 操作パネルの配線でエラー：" + e.Message);
                Debug.LogException(e);
                foreach (var it in items) if (it.button != null) for (int k = it.button.onClick.GetPersistentEventCount() - 1; k >= 0; k--) UnityEventTools.RemovePersistentListener(it.button.onClick, k);
                foreach (var c in added) if (c != null) UnityEngine.Object.DestroyImmediate(c);
                ctrl.marks = new GameObject[0]; ctrl.markCh = new int[0]; ctrl.markVal = new int[0];
                ctrl.statusTexts = new Text[0]; ctrl.beatLamps = new GameObject[0]; ctrl.panelHandle = null; ctrl.panelCanvas = null; ctrl.panelSync = null;
                Finish(ctrl);
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>VJ のコントローラー（StageKoboVJ）を作って、組み立てた VJ をつなぐ。失敗したら null</summary>
        public static Component AddVJ(SKContext ctx, SKVJInfo info, Component controller)
        {
#if STAGEKOBO_UDON
            GameObject go = null;
            try
            {
                go = new GameObject("StageKoboVJ");
                go.transform.SetParent(ctx.root.transform, false);
                var vj = AddUSharp(go, typeof(StageKoboVJ)) as StageKoboVJ;
                if (vj == null) throw new Exception("StageKoboVJ を追加できませんでした");
                var ctrl = controller as StageKoboController;
                vj.main = ctrl;
                vj.deckA = info.matA; vj.deckB = info.matB; vj.fx = info.matFx;
                vj.deckATex = info.deckA; vj.deckBTex = info.deckB;
                vj.images = info.images.ToArray();
                vj.textTex = info.text; vj.textAsp = info.textAsp;
                vj.resX = SKVJBuilder.W; vj.resY = SKVJBuilder.H;
                vj.vjPal = info.vjPal; vj.lightPal = info.lightPal; vj.lightPalCh = SKCh.PALETTE;
                vj.presetI = info.presetI.ToArray(); vj.presetF = info.presetF.ToArray();
                vj.slotI = info.slotI.ToArray(); vj.slotF = info.slotF.ToArray();
                vj.vi = (int[])info.vi.Clone(); vj.vf = (float[])info.vf.Clone();
                vj.deskMats = new Material[0];
                vj.btnDesk = new int[0]; vj.btnSlot = new int[0]; vj.btnKind = new int[0]; vj.btnAct = new int[0]; vj.btnVal = new int[0];
                Finish(vj);
                if (ctrl != null) { ctrl.vj = vj; Finish(ctrl); }
                return vj;
            }
            catch (Exception e)
            {
                ctx.Log("⚠ VJ の UdonSharp でエラー：" + e.Message);
                Debug.LogException(e);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
                return null;
            }
#else
            return null;
#endif
        }

        /// <summary>ステージ裏の卓のボタンを配線する（押す：Interact → StageKoboButton.Press、ランプ：コントローラーが _B0〜_B3 に入れる）</summary>
        public static bool WireDesks(SKContext ctx, Component controller, Component vjComp, Material[] deskMats, List<DeskButton> btns, Transform deskCenter)
        {
#if STAGEKOBO_UDON
            var ctrl = controller as StageKoboController;
            var vj = vjComp as StageKoboVJ;
            if (ctrl == null) return false;
            var added = new List<Component>();
            try
            {
                var lamps = btns.Where(b => b.lamp).ToList();
                if (vj != null)
                {
                    // VJ があるとき：ランプはぜんぶ VJ のコントローラーがまとめて入れる（照明のボタンは StageKoboController に聞く）
                    vj.deskMats = deskMats;
                    vj.btnDesk = lamps.Select(b => b.desk).ToArray();
                    vj.btnSlot = lamps.Select(b => b.slot).ToArray();
                    vj.btnKind = lamps.Select(b => b.kind).ToArray();
                    vj.btnAct = lamps.Select(b => b.act).ToArray();
                    vj.btnVal = lamps.Select(b => b.val).ToArray();
                    vj.deskCenter = deskCenter;
                    Finish(vj);
                    ctrl.deskMat = null; ctrl.deskCh = new int[0]; ctrl.deskVal = new int[0];
                }
                else
                {
                    // 照明卓だけ：コントローラーがランプを入れる（番号 = ボタンの番号）
                    var light = btns.Where(b => b.desk == 0).ToList();
                    int n = light.Count == 0 ? 0 : light.Max(b => b.slot) + 1;
                    var ch = Enumerable.Repeat(-1, n).ToArray(); var val = new int[n];
                    foreach (var b in light) if (b.lamp) { ch[b.slot] = b.act; val[b.slot] = b.val; }
                    ctrl.deskMat = deskMats.Length > 0 ? deskMats[0] : null; ctrl.deskCh = ch; ctrl.deskVal = val;
                }
                Finish(ctrl);
                var ut = FindType("VRC.Udon.UdonBehaviour");
                var fText = ut != null ? ut.GetField("interactText") : null;
                var fProx = ut != null ? ut.GetField("proximity") : null;
                foreach (var b in btns)
                {
                    var sb = AddUSharp(b.go, typeof(StageKoboButton)) as StageKoboButton;
                    if (sb == null) throw new Exception("StageKoboButton を追加できませんでした");
                    added.Add(sb);
                    sb.controller = ctrl;
                    sb.vj = b.kind == 1 ? vj : null;
                    sb.ch = b.act; sb.val = b.val;
                    sb.deskMat = b.mat; sb.slot = b.slot;
                    Finish(sb);
                    var ub = Backing(sb);
                    if (ub != null)
                    {
                        added.Add(ub);
                        if (fText != null) fText.SetValue(ub, b.text);
                        if (fProx != null) fProx.SetValue(ub, 2.5f);
                        EditorUtility.SetDirty(ub);
                    }
                }
                ctx.Log("UdonSharp：ステージ裏の卓のボタン " + btns.Count + " 個を配線しました");
                return true;
            }
            catch (Exception e)
            {
                ctx.Log("⚠ 卓の配線でエラー：" + e.Message);
                Debug.LogException(e);
                foreach (var c in added) if (c != null) UnityEngine.Object.DestroyImmediate(c);
                if (vj != null) { vj.deskMats = new Material[0]; vj.btnDesk = new int[0]; vj.btnSlot = new int[0]; vj.btnKind = new int[0]; vj.btnAct = new int[0]; vj.btnVal = new int[0]; Finish(vj); }
                ctrl.deskMat = null; Finish(ctrl);
                return false;
            }
#else
            return false;
#endif
        }

#if STAGEKOBO_UDON
        static bool HasProgramAsset(Type t) { return FindProgramAsset(t) != null; }

        static UnityEngine.Object FindProgramAsset(Type t)
        {
            var pat = FindType("UdonSharp.UdonSharpProgramAsset");
            var field = pat != null ? pat.GetField("sourceCsScript") : null;
            if (field == null) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
            {
                var a = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), pat);
                var ms = a != null ? field.GetValue(a) as MonoScript : null;
                if (ms != null && ms.GetClass() == t) return a;
            }
            return null;
        }

        /// <summary>スクリプトの隣に UdonSharpProgramAsset を作る。作ったら true</summary>
        static bool EnsureProgramAsset(SKContext ctx, Type t)
        {
            if (HasProgramAsset(t)) return false;
            var pat = FindType("UdonSharp.UdonSharpProgramAsset");
            var field = pat != null ? pat.GetField("sourceCsScript") : null;
            if (field == null) return false;
            MonoScript script = null;
            foreach (var guid in AssetDatabase.FindAssets(t.Name + " t:MonoScript"))
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (ms != null && ms.GetClass() == t) { script = ms; break; }
            }
            if (script == null) { ctx.Log("⚠ " + t.Name + ".cs が見つかりません（Runtime フォルダが取り込まれているか確認してください）"); return false; }
            var asset = ScriptableObject.CreateInstance(pat);
            field.SetValue(asset, script);
            string path = Path.ChangeExtension(AssetDatabase.GetAssetPath(script), ".asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();   // UdonSharp が新しいプログラムアセットを見つけられるように（UdonSharp 自身もこうしている）
            ctx.Log("UdonSharp のプログラムアセットを作りました：" + path);
            return true;
        }

        /// <summary>作ったプログラムアセットをコンパイルする（終わるまで待つ版を優先）</summary>
        static void TryCompile(SKContext ctx)
        {
            try
            {
                var comp = FindType("UdonSharp.Compiler.UdonSharpCompilerV1");
                var sync = comp != null ? comp.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == "CompileSync") : null;
                if (sync != null) { sync.Invoke(null, Defaults(sync)); return; }
                var pat = FindType("UdonSharp.UdonSharpProgramAsset");
                var m = pat != null ? pat.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == "CompileAllCsPrograms") : null;
                if (m != null)
                {
                    var args = m.GetParameters().Select(p => p.Name.IndexOf("force", StringComparison.OrdinalIgnoreCase) >= 0 ? (object)true : p.HasDefaultValue ? p.DefaultValue : (p.ParameterType == typeof(bool) ? (object)true : null)).ToArray();
                    m.Invoke(null, args);
                    return;
                }
            }
            catch (Exception e) { Debug.LogWarning("[すてーじ工房] UdonSharp のコンパイルを呼べませんでした：" + e.Message); }
            ctx.Log("（UdonSharp のコンパイルは、Unity の再生ボタン・アップロードのときに自動で行われます）");
        }

        static object[] Defaults(MethodInfo m)
        {
            return m.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        }

        /// <summary>UdonSharp のコンポーネントを付ける（バージョンによって方法が違うので順に試す）</summary>
        static Component AddUSharp(GameObject go, Type t)
        {
            var undo = FindType("UdonSharpEditor.UdonSharpUndo");
            if (undo != null)
            {
                var m = undo.GetMethod("AddComponent", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject), typeof(Type) }, null);
                if (m != null) { var c = m.Invoke(null, new object[] { go, t }) as Component; if (c != null) return c; }
                var g = undo.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == "AddComponent" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1);
                if (g != null) { var c = g.MakeGenericMethod(t).Invoke(null, new object[] { go }) as Component; if (c != null) return c; }
            }
            // 古い書き方（拡張メソッド AddUdonSharpComponent）
            var ext = FindType("UdonSharpEditor.UdonSharpComponentExtensions");
            var add = ext != null ? ext.GetMethod("AddUdonSharpComponent", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject), typeof(Type) }, null) : null;
            if (add != null) { var c = add.Invoke(null, new object[] { go, t }) as Component; if (c != null) return c; }
            // 最後の手段：普通に付けてから、UdonSharp の初期設定を呼ぶ
            var comp = go.AddComponent(t);
            var util = FindType("UdonSharpEditor.UdonSharpEditorUtility");
            var setup = util != null ? util.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).FirstOrDefault(x => x.Name == "RunBehaviourSetup") : null;
            if (setup != null) setup.Invoke(null, Args(setup, comp));
            return comp;
        }

        /// <summary>UdonSharp の裏にある本物の UdonBehaviour</summary>
        static Component Backing(Component usb)
        {
            var util = FindType("UdonSharpEditor.UdonSharpEditorUtility");
            var m = util != null ? util.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == "GetBackingUdonBehaviour" && x.GetParameters().Length == 1) : null;
            if (m != null)
            {
                try { var r = m.Invoke(null, new object[] { usb }) as Component; if (r != null) return r; } catch (Exception) { }
            }
            var ut = FindType("VRC.Udon.UdonBehaviour");
            return ut != null ? usb.GetComponent(ut) : null;
        }

        /// <summary>
        /// 値を保存する。UdonBehaviour 側への書き込みは、UdonSharp が再生・アップロードのときに自動で行う
        /// （作った直後はまだコンパイル前のことがあり、ここで書き込もうとすると失敗するため呼ばない）
        /// </summary>
        static void Finish(Component usb)
        {
            EditorUtility.SetDirty(usb);
            var ub = Backing(usb);
            if (ub != null) EditorUtility.SetDirty(ub);
        }

        /// <summary>配線の途中で失敗したとき：付けた UdonSharp の部品とボタンの配線を外して、元に戻す</summary>
        static void Cleanup(List<Item> items)
        {
            var ut = FindType("VRC.Udon.UdonBehaviour");
            foreach (var it in items)
            {
                if (it.button == null) continue;
                for (int k = it.button.onClick.GetPersistentEventCount() - 1; k >= 0; k--) UnityEventTools.RemovePersistentListener(it.button.onClick, k);
                foreach (var c in it.button.GetComponents<Component>())
                    if (c != null && (c is StageKoboButton || (ut != null && ut.IsInstanceOfType(c)))) UnityEngine.Object.DestroyImmediate(c);
            }
        }

        static object[] Args(MethodInfo m, object first)
        {
            var ps = m.GetParameters();
            var a = new object[ps.Length];
            a[0] = first;
            for (int i = 1; i < ps.Length; i++)
                a[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            return a;
        }
#endif
    }
}
