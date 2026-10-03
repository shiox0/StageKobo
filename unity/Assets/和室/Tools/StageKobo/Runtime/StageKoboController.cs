#if STAGEKOBO_UDON
// すてーじ工房：リモコンの UdonSharp 版（全員に同期）
// ※ このファイルは UdonSharp が入っているプロジェクトだけで有効になります（STAGEKOBO_UDON）。
//    インポーターの「UdonSharp 版を有効にする」ボタンで自動的に設定されます。
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Washitsu.StageKobo
{
    /// <summary>
    /// 演出そのものは Animator に焼き込んだアニメーション。このコントローラーは
    ///  ・チャンネルごとに「いまどのステートか」（動き・色・明るさ…）
    ///  ・テンポ（BPM）と動きの速さ（×½ / ×1 / ×2）
    ///  ・拍の位置（サーバー時刻を基準にして、全員の画面で同じ拍にそろえる）
    /// を同期して Animator を切り替える。あとから入った人にも同じ状態が届く。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class StageKoboController : UdonSharpBehaviour
    {
        // ---- チャンネル番号（インポーター側の SKCh と同じ番号）
        public const int CH_MOVE = 0;
        public const int CH_COLOR = 1;
        public const int CH_PALETTE = 2;
        public const int CH_DIM = 3;
        public const int CH_LASER = 4;
        public const int CH_MASTER = 5;
        public const int CH_MONITOR = 6;
        public const int CH_WASH = 7;
        public const int CH_PEN = 8;
        public const int CH_CAM1 = 9;
        public const int CH_CAM2 = 10;
        public const int CH_PERF = 11;
        public const int CH_AMBIENT = 12;
        public const int CH_MONITOR2 = 13;  // サブのモニター（CH_MONITOR はメイン）
        public const int NCH = 14;
        // ---- ボタンの「動作」番号
        public const int ACT_BPM = 20;      // val = 増減（0 = TAP）
        public const int ACT_SPEED = 21;    // val = 0:×½ 1:×1 2:×2
        public const int ACT_STROBE = 22;
        public const int ACT_FX = 23;       // val = 0:スパーク 1:紙吹雪 2:スモーク 3〜:追加の特効（特効の設定ウィンドウで作ったもの）
        public const int ACT_UP = 24;       // カメラのアップで顔を追う人に「押した人」を登録（val 0 = カメラ1、1 = カメラ2、3 = 外す）
        public const int ACT_AUDIO = 25;    // 曲の音に反応（AudioLink）：val 1 = ON、0 = OFF
        public const int ACT_SPOT = 26;     // 演者を照らすスポットに「押した人」を登録（val 0 = スポット1、1 = スポット2、3 = 固定に戻す）
        public const int ACT_SPOTON = 27;   // スポットライト：val 1 = ON、0 = OFF
        // ---- ステートの時間の合わせ方
        public const int MODE_CONST = 0;    // 止まっている（色の固定など）
        public const int MODE_BEAT = 1;     // 拍に合わせる（Tempo）
        public const int MODE_MOVE = 2;     // 拍 × 動きの速さ（MoveTempo）
        public const int MODE_TIME = 3;     // 時間（秒）に合わせる（カメラワーク・ミラーボール）

        [Header("インポーターが設定します（手で変えなくてOK）")]
        public Animator[] anims;            // 0 = 照明, 1 = カメラ1, 2 = カメラ2
        public float bakedBpm = 128f;       // 焼き込んだときの BPM
        public float startBpm = 128f;
        public string[] stNames;            // ステート名（全チャンネルを並べたもの）
        public float[] stLens;              // クリップの長さ（秒・焼き込んだテンポで）
        public int[] stModes;
        public int[] chStart;
        public int[] chCount;
        public int[] chAnim;
        public int[] chLayer;
        public float[] chFade;
        public int[] chDefault;
        public ParticleSystem[] fxSystems;
        public int[] fxGroups;              // 0〜2 = 最初からある特効、3 + k = 追加の特効 k 番
        // 追加の特効（k 番 = グループ 3 + k）
        public int[] fxModes;               // 0 = 押すたびに 1 回、1 = ON/OFF（出しっぱなし）
        public float[] fxSecs;              // 「1回」のとき何秒で止めるか（0 = パーティクルのまま）
        public AudioSource[] fxAudio;       // 一緒に鳴らす音
        public int[] fxAudioGroups;
        public GameObject[] marks;          // 選ばれているボタンの印
        public int[] markCh;
        public int[] markVal;
        public Text statusText;
        public GameObject beatLamp;
        // ステージ裏の照明卓（物理スイッチ）のランプ
        public Material deskMat;
        public int[] deskCh;
        public int[] deskVal;
        public StageKoboVJ vj;              // VJ 卓（モニターのボタンのランプを VJ 卓でも出す）
        // カメラのアップ：登録した人の顔を追う（引きのショットは焼き込んだカメラワークのまま）
        public Camera[] stageCams;          // 0 = カメラ1, 1 = カメラ2
        public Vector3 upFwd = Vector3.forward;   // 客席の方向（ワールド）。カメラは顔のこちら側に置く
        public Vector3 upSide = Vector3.right;    // 横の揺れの向き
        public float[] upDist;              // 顔からの距離（m）
        public float[] upFov;               // 顔のアップの画角（縦・度）
        public float[] upSpeed;             // 揺れの速さ（カメラの「動きの速さ」）
        public bool hasAudio;               // 音に反応（AudioLink）するステージか（状態の表示用）
        // 演者を照らすスポットライト（本物のライト）：ふだんは固定の場所、登録した人がいればその人の胸を追う
        public Light[] spots;
        public Vector3[] spotAims;          // 固定のときに照らす場所（ワールド）
        public GameObject[] spotBeams;      // 見た目のビーム（OFF のとき消す）
        public float spotMaxDist = 25f;     // 登録した人が固定の場所からこれより遠いときは追わない（ステージの外）
        public float spotRadius = 1.4f;     // 照らす丸の半径（m）

        // ---- 同期する値
        [UdonSynced] public int[] sel;
        [UdonSynced] public float bpm = 128f;
        [UdonSynced] public float speedMul = 1f;
        [UdonSynced] public int anchorMs;       // このサーバー時刻に…
        [UdonSynced] public double anchorBeat;  // …この拍だった（長時間でも精度が落ちないよう double）
        [UdonSynced] public int epochMs;        // 時間で動く演出の基準
        [UdonSynced] public double mvBase;      // 動きの拍（速さを反映）の基準
        [UdonSynced] public double mvBeat0;
        [UdonSynced] public int up1 = -1;       // カメラ1のアップで顔を追う人（プレイヤーID。-1 = いない）
        [UdonSynced] public int up2 = -1;       // カメラ2
        [UdonSynced] public bool audioOn = true;  // 曲の音に反応する（AudioLink。シェーダーには _Udon_SKALOff で渡す）
        [UdonSynced] public int sp1 = -1;       // スポット1が追う人（プレイヤーID。-1 = 固定）
        [UdonSynced] public int sp2 = -1;       // スポット2
        [UdonSynced] public bool spotOn = true; // スポットライト ON/OFF
        [UdonSynced] public bool[] fxOn;        // 追加の特効の ON/OFF（ON/OFF のものだけ使う）

        // ---- 自分の画面だけの値
        int[] applied;
        float[] lastChange;
        float appliedBpm = -1f;
        float appliedSpeed = -1f;
        double localBeat;
        bool setupDone;
        bool synced;
        bool lampOn;
        float nextResync;
        float strobeUntil;
        float[] taps;
        int tapCount;
        float lastTap = -10f;
        int idBeat;
        int idSync;
        int idBpm;
        int idALOff;
        int[] closeIdx;     // カメラごとの「アップ」「オート」のステート番号
        int[] autoIdx;
        bool[] following;
        Vector3[] smPos;
        Vector3[] smLook;
        bool spotInit;
        bool[] fxApplied;
        float[] fxStopAt;

        void Start()
        {
            Setup();
            if (Networking.IsOwner(gameObject))
            {
                ResetSynced();
                RequestSerialization();
                synced = true;
            }
            else if (!synced)
            {
                ResetSynced();   // 同期データが届くまでの仮の値（届いたら上書きされる）
            }
            localBeat = ServerBeat();
            ApplyAll(true);
        }

        void Setup()
        {
            if (setupDone) return;
            setupDone = true;
            applied = new int[NCH];
            lastChange = new float[NCH];
            taps = new float[4];
            for (int c = 0; c < NCH; c++) { applied[c] = Def(c); lastChange[c] = -100f; }
            idBeat = VRCShader.PropertyToID("_Udon_SKBeat");
            idSync = VRCShader.PropertyToID("_Udon_SKSync");
            idBpm = VRCShader.PropertyToID("_Udon_SKBpm");
            idALOff = VRCShader.PropertyToID("_Udon_SKALOff");
            VRCShader.SetGlobalFloat(idSync, 1f);
            closeIdx = new int[2]; autoIdx = new int[2];
            following = new bool[2]; smPos = new Vector3[2]; smLook = new Vector3[2];
            for (int k = 0; k < 2; k++)
            {
                closeIdx[k] = -1; autoIdx[k] = -1;
                int c = k == 0 ? CH_CAM1 : CH_CAM2;
                if (chCount == null || c >= chCount.Length) continue;
                for (int i = 0; i < chCount[c]; i++)
                {
                    string n = stNames[chStart[c] + i];
                    if (n == "Shot_closeup") closeIdx[k] = i;
                    else if (n == "Shot_auto") autoIdx[k] = i;
                }
            }
        }

        void ResetSynced()
        {
            sel = new int[NCH];
            for (int c = 0; c < NCH; c++) sel[c] = Def(c);
            int now = Networking.GetServerTimeInMilliseconds();
            bpm = startBpm;
            speedMul = 1f;
            anchorMs = now;
            epochMs = now;
            anchorBeat = 0;
            mvBase = 0;
            mvBeat0 = 0;
            audioOn = true;
            sp1 = -1;
            sp2 = -1;
            spotOn = true;
            fxOn = new bool[FxCount()];
        }

        int FxCount() { return fxModes != null ? fxModes.Length : 0; }

        public override void OnDeserialization()
        {
            Setup();
            if (sel == null || sel.Length != NCH) return;
            bool first = !synced;
            synced = true;
            double d = ServerBeat() - localBeat;
            bool jump = d > 0.2 || d < -0.2;
            // テンポ・速さが変わった：届くまでの間は古い速さで動いていたので、拍の位置に合わせ直す
            bool tempoChanged = bpm != appliedBpm || speedMul != appliedSpeed;
            if (jump || first || tempoChanged) localBeat += d;   // 合わせ直す前に、拍もサーバー時刻の値にそろえる
            ApplyAll(first);
            if (!first && (jump || tempoChanged)) RealignBeat();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            UpdateUI();
        }

        /// <summary>登録した人が抜けたら外す（持ち主が直して同期する）</summary>
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !Networking.IsOwner(gameObject)) return;
            int id = player.playerId;
            if (up1 != id && up2 != id && sp1 != id && sp2 != id) return;
            if (up1 == id) up1 = -1;
            if (up2 == id) up2 = -1;
            if (sp1 == id) sp1 = -1;
            if (sp2 == id) sp2 = -1;
            RequestSerialization();
            UpdateUI();
        }

        // ------------------------------------------------------------------ カメラのアップ：登録した人の顔を追う

        /// <summary>そのカメラがいま「アップ」か（オートの中のアップも。ブラウザ版と同じ 2小節ごとの並び）</summary>
        bool IsCloseup(int k)
        {
            int c = k == 0 ? CH_CAM1 : CH_CAM2;
            if (chCount == null || chCount[c] <= 0) return false;
            int v = Clamp(sel[c], c);
            if (v == closeIdx[k]) return true;
            if (v == autoIdx[k])
            {
                int seg = Mathf.FloorToInt((float)(localBeat / 8.0));
                return (((seg * 5 + k * 3) % 7) + 7) % 7 == 1;   // AutoShots[1] = closeup
            }
            return false;
        }

        /// <summary>アバターの頭が動いたあと（IK のあと）に、焼き込んだカメラワークを上書きする</summary>
        public override void PostLateUpdate()
        {
            if (setupDone && sel != null) UpdateSpots();
            if (!setupDone || stageCams == null || sel == null || sel.Length != NCH || upDist == null || upFov == null || upSpeed == null) return;
            for (int k = 0; k < 2 && k < stageCams.Length; k++)
            {
                Camera cam = stageCams[k];
                if (cam == null) continue;
                int pid = k == 0 ? up1 : up2;
                bool on = false;
                Vector3 head = Vector3.zero;
                if (pid >= 0 && IsCloseup(k))
                {
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
                    if (Utilities.IsValid(p))
                    {
                        head = p.GetBonePosition(HumanBodyBones.Head);
                        if (head.sqrMagnitude < 0.0001f) head = p.GetPosition() + Vector3.up * 1.45f;   // 頭のボーンが無いアバター
                        else head += Vector3.up * 0.08f;                                                  // 頭のボーン（首の上）→ 顔のまん中
                        on = true;
                    }
                }
                if (!on) { following[k] = false; continue; }
                float t = Time.time, sp = upSpeed[k], d = upDist[k];
                Vector3 pos = head + upSide * (Mathf.Sin(t * 0.3f * sp) * d * 0.18f + Mathf.Sin(t * 0.7f) * 0.02f)
                            + Vector3.up * (-0.03f + Mathf.Sin(t * 1.1f) * 0.015f) + upFwd * d;
                Vector3 look = head - Vector3.up * 0.08f;
                if (!following[k]) { smPos[k] = pos; smLook[k] = look; following[k] = true; }
                else
                {
                    float a = 1f - Mathf.Exp(-Time.deltaTime * 8f);   // 少しなめらかに（手持ちカメラのように）
                    smPos[k] = Vector3.Lerp(smPos[k], pos, a);
                    smLook[k] = Vector3.Lerp(smLook[k], look, a);
                }
                Vector3 dir = smLook[k] - smPos[k];
                if (dir.sqrMagnitude < 1e-6f) continue;
                cam.transform.position = smPos[k];
                cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                cam.fieldOfView = upFov[k];
            }
        }

        /// <summary>演者を照らすスポット：登録した人の胸（いなければ固定の場所）へ、なめらかに向ける</summary>
        void UpdateSpots()
        {
            if (spots == null || spotAims == null) return;
            float a = 1f - Mathf.Exp(-Time.deltaTime * 5f);
            for (int k = 0; k < spots.Length && k < spotAims.Length; k++)
            {
                Light L = spots[k];
                if (L == null) continue;
                bool on = spotOn;
                if (L.enabled != on) L.enabled = on;
                if (spotBeams != null && k < spotBeams.Length && spotBeams[k] != null && spotBeams[k].activeSelf != on) spotBeams[k].SetActive(on);
                if (!on) continue;
                Vector3 aim = spotAims[k];
                int pid = k == 0 ? sp1 : (k == 1 ? sp2 : -1);
                if (pid >= 0)
                {
                    VRCPlayerApi p = VRCPlayerApi.GetPlayerById(pid);
                    if (Utilities.IsValid(p))
                    {
                        Vector3 c = p.GetBonePosition(HumanBodyBones.Chest);
                        if (c.sqrMagnitude < 0.0001f) c = p.GetPosition() + Vector3.up * 1.1f;   // 胸のボーンが無いアバター
                        if (Vector3.Distance(c, spotAims[k]) < spotMaxDist) aim = c;
                    }
                }
                Transform tr = L.transform;
                Vector3 dir = aim - tr.position;
                if (dir.sqrMagnitude < 0.01f) continue;
                Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
                tr.rotation = spotInit ? Quaternion.Slerp(tr.rotation, want, a) : want;
                L.spotAngle = Mathf.Clamp(2f * Mathf.Atan(spotRadius / Mathf.Max(0.5f, dir.magnitude)) * Mathf.Rad2Deg, 4f, 60f);
            }
            spotInit = true;
        }

        void Update()
        {
            if (!setupDone || sel == null) return;
            float dt = Time.deltaTime;
            localBeat += dt * bpm / 60.0;
            // サーバー時刻から求めた拍に、なめらかに寄せる（大きくずれたら合わせ直す）
            double err = ServerBeat() - localBeat;
            if (err > 0.25 || err < -0.25) localBeat += err;
            else localBeat += err * Mathf.Min(1f, dt * 2f);
            // シェーダーには 4096 拍で折り返して渡す（float の精度が落ちないように。模様の周期は 4096 を割り切るので継ぎ目は出ない）
            VRCShader.SetGlobalFloat(idBeat, (float)(localBeat % 4096.0));

            if (beatLamp != null)
            {
                bool on = localBeat % 1.0 < 0.2;
                if (on != lampOn) { lampOn = on; beatLamp.SetActive(on); }
            }
            if (Time.time >= nextResync)
            {
                nextResync = Time.time + 4f;
                Resync();
            }
            CheckFxStop();
        }

        // ------------------------------------------------------------------ ボタン

        /// <summary>リモコンのボタン（StageKoboButton）から呼ばれる</summary>
        public void Press(int ch, int val)
        {
            Setup();
            if (sel == null || sel.Length != NCH) ResetSynced();
            if (ch == ACT_STROBE)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Strobe));
                return;
            }
            if (ch == ACT_FX)
            {
                if (val == 0) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxSpark));
                else if (val == 1) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxConfetti));
                else if (val == 2) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(FxSmoke));
                else
                {
                    int k = val - 3;
                    if (k < 0 || k >= FxCount()) return;
                    if (fxModes[k] == 1)
                    {
                        // ON/OFF：同期する（あとから入った人にも出しっぱなしが見える）
                        if (fxOn == null || fxOn.Length != FxCount()) fxOn = new bool[FxCount()];
                        if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
                        synced = true;
                        fxOn[k] = !fxOn[k];
                        ApplyFxToggles();
                        RequestSerialization();
                        UpdateUI();
                    }
                    else SendCustomNetworkEvent(NetworkEventTarget.All, "FxC" + k.ToString());   // 1回：全員の画面で出す
                }
                return;
            }
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            synced = true;
            if (ch >= 0 && ch < NCH)
            {
                if (chCount[ch] <= 0) return;
                sel[ch] = Mathf.Clamp(val, 0, chCount[ch] - 1);
                ApplyChannel(ch, false);
            }
            else if (ch == ACT_BPM)
            {
                if (val == 0) Tap();
                else SetBpm(bpm + val);
            }
            else if (ch == ACT_SPEED)
            {
                SetSpeed(val == 0 ? 0.5f : val == 2 ? 2f : 1f);
            }
            else if (ch == ACT_AUDIO)
            {
                audioOn = val != 0;
                VRCShader.SetGlobalFloat(idALOff, audioOn ? 0f : 1f);
            }
            else if (ch == ACT_SPOT)
            {
                // 押した人を登録（もう一度押すと固定に戻る）。「固定に戻す」はどちらのスポットも
                int me2 = Networking.LocalPlayer.playerId;
                if (val == 0) sp1 = sp1 == me2 ? -1 : me2;
                else if (val == 1) sp2 = sp2 == me2 ? -1 : me2;
                else if (val == 2) { sp1 = me2; sp2 = me2; }
                else { sp1 = -1; sp2 = -1; }
            }
            else if (ch == ACT_SPOTON)
            {
                spotOn = val != 0;
            }
            else if (ch == ACT_UP)
            {
                // 押した人を登録（もう一度押すと外れる）。「外す」はどちらのカメラも外す
                int me = Networking.LocalPlayer.playerId;
                if (val == 0) up1 = up1 == me ? -1 : me;
                else if (val == 1) up2 = up2 == me ? -1 : me;
                else if (val == 2) { up1 = me; up2 = me; }
                else { up1 = -1; up2 = -1; }
            }
            RequestSerialization();
            UpdateUI();
        }

        void SetBpm(float nb)
        {
            nb = Mathf.Clamp(Mathf.Round(nb), 40f, 240f);
            if (nb == bpm) return;
            anchorBeat = ServerBeat();
            anchorMs = Networking.GetServerTimeInMilliseconds();
            bpm = nb;
            SetTempo();
        }

        void SetSpeed(float s)
        {
            if (s == speedMul) return;
            double b = ServerBeat();
            mvBase = mvBase + (b - mvBeat0) * speedMul;
            mvBeat0 = b;
            speedMul = s;
            SetTempo();
        }

        /// <summary>TAP：2回以上たたくと、その間隔を BPM にして、たたいた瞬間を拍の頭にそろえる</summary>
        void Tap()
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastTap > 2f) tapCount = 0;
            lastTap = now;
            for (int k = 3; k > 0; k--) taps[k] = taps[k - 1];
            taps[0] = now;
            if (tapCount < 4) tapCount++;
            if (tapCount < 2) return;
            float avg = (taps[0] - taps[tapCount - 1]) / (tapCount - 1);
            if (avg < 0.25f || avg > 1.5f) return;
            float nb = Mathf.Clamp(Mathf.Round(60f / avg), 40f, 240f);
            double b = ServerBeat();
            double fr = b % 1.0;
            b = b - fr + (fr >= 0.5 ? 1.0 : 0.0);   // いちばん近い拍の頭
            double mb = mvBase + (b - mvBeat0) * speedMul;
            anchorBeat = b;
            anchorMs = Networking.GetServerTimeInMilliseconds();
            bpm = nb;
            mvBase = mb;
            mvBeat0 = b;
            localBeat = b;
            SetTempo();
            RealignBeat();
        }

        // ------------------------------------------------------------------ 全員に届くイベント

        public void Strobe()
        {
            if (chCount == null || chCount[CH_MASTER] < 3) return;
            Animator a = anims[chAnim[CH_MASTER]];
            if (a == null) return;
            a.Play(stNames[chStart[CH_MASTER] + 2], chLayer[CH_MASTER], 0f);
            // ストロボのクリップは 1.6 秒。終わる直前（消えている瞬間）に、選ばれている状態（点灯 / 暗転）へすぐ戻す。
            // こうしないと Animator が自動で「点灯」に戻るので、暗転中のストロボの最後に一瞬光ってしまう
            strobeUntil = Time.time + 1.5f;
            SendCustomEventDelayedSeconds(nameof(_StrobeEnd), 1.58f);
        }

        public void _StrobeEnd()
        {
            if (Time.time < strobeUntil) return;   // あとから押されたストロボの途中
            ApplyChannel(CH_MASTER, true);
        }

        public void FxSpark() { PlayFx(0); }
        public void FxConfetti() { PlayFx(1); }
        public void FxSmoke() { PlayFx(2); }

        void PlayFx(int g)
        {
            if (fxSystems == null) return;
            for (int k = 0; k < fxSystems.Length; k++)
                if (fxGroups[k] == g && fxSystems[k] != null) fxSystems[k].Play();
        }

        // ---- 追加の特効（1回）：ネットワークイベントは引数を渡せないので、番号ごとの入口を用意してある（最大 16 個）
        public void FxC0() { FireCustom(0); }
        public void FxC1() { FireCustom(1); }
        public void FxC2() { FireCustom(2); }
        public void FxC3() { FireCustom(3); }
        public void FxC4() { FireCustom(4); }
        public void FxC5() { FireCustom(5); }
        public void FxC6() { FireCustom(6); }
        public void FxC7() { FireCustom(7); }
        public void FxC8() { FireCustom(8); }
        public void FxC9() { FireCustom(9); }
        public void FxC10() { FireCustom(10); }
        public void FxC11() { FireCustom(11); }
        public void FxC12() { FireCustom(12); }
        public void FxC13() { FireCustom(13); }
        public void FxC14() { FireCustom(14); }
        public void FxC15() { FireCustom(15); }

        /// <summary>1回出す：いま出ていても頭から出し直す（出ている粒は消さない）。秒数があればその時間で止める</summary>
        void FireCustom(int k)
        {
            if (k < 0 || k >= FxCount() || fxSystems == null) return;
            int g = 3 + k;
            for (int i = 0; i < fxSystems.Length; i++)
            {
                ParticleSystem ps = fxSystems[i];
                if (fxGroups[i] != g || ps == null) continue;
                if (ps.isPlaying) ps.Stop();
                ps.Play();
            }
            if (fxAudio != null)
                for (int i = 0; i < fxAudio.Length; i++)
                    if (fxAudioGroups[i] == g && fxAudio[i] != null) { fxAudio[i].Stop(); fxAudio[i].Play(); }
            if (fxStopAt == null || fxStopAt.Length != FxCount()) fxStopAt = new float[FxCount()];
            fxStopAt[k] = fxSecs != null && k < fxSecs.Length && fxSecs[k] > 0f ? Time.time + fxSecs[k] : 0f;
        }

        void StopCustom(int k)
        {
            int g = 3 + k;
            if (fxSystems != null)
                for (int i = 0; i < fxSystems.Length; i++)
                    if (fxGroups[i] == g && fxSystems[i] != null) fxSystems[i].Stop();
            if (fxAudio != null)
                for (int i = 0; i < fxAudio.Length; i++)
                    if (fxAudioGroups[i] == g && fxAudio[i] != null) fxAudio[i].Stop();
        }

        /// <summary>ON/OFF の特効を、同期した状態に合わせる</summary>
        void ApplyFxToggles()
        {
            int n = FxCount();
            if (n == 0 || fxOn == null || fxOn.Length != n) return;
            if (fxApplied == null || fxApplied.Length != n) fxApplied = new bool[n];
            for (int k = 0; k < n; k++)
            {
                if (fxModes[k] != 1 || fxOn[k] == fxApplied[k]) continue;
                fxApplied[k] = fxOn[k];
                if (fxOn[k])
                {
                    int g = 3 + k;
                    if (fxSystems != null)
                        for (int i = 0; i < fxSystems.Length; i++)
                            if (fxGroups[i] == g && fxSystems[i] != null) fxSystems[i].Play();
                    if (fxAudio != null)
                        for (int i = 0; i < fxAudio.Length; i++)
                            if (fxAudioGroups[i] == g && fxAudio[i] != null) fxAudio[i].Play();
                }
                else StopCustom(k);
            }
        }

        /// <summary>「1回」で秒数を決めたものを止める（Update から）</summary>
        void CheckFxStop()
        {
            if (fxStopAt == null) return;
            for (int k = 0; k < fxStopAt.Length; k++)
                if (fxStopAt[k] > 0f && Time.time >= fxStopAt[k]) { fxStopAt[k] = 0f; StopCustom(k); }
        }

        // ------------------------------------------------------------------ Animator

        int Def(int c) { return chCount != null && c < chCount.Length && chCount[c] > 0 ? chDefault[c] : 0; }

        int Clamp(int v, int c)
        {
            if (v < 0) return 0;
            if (v >= chCount[c]) return chCount[c] - 1;
            return v;
        }

        double ServerBeat()
        {
            int el = Networking.GetServerTimeInMilliseconds() - anchorMs;   // int のまま引き算（サーバー時刻が一周しても大丈夫）
            return anchorBeat + el * 0.001 * bpm / 60.0;
        }

        float ServerSec()
        {
            int el = Networking.GetServerTimeInMilliseconds() - epochMs;
            return el * 0.001f;
        }

        double MoveBeat() { return mvBase + (localBeat - mvBeat0) * speedMul; }

        float Tempo() { return bakedBpm > 0f ? bpm / bakedBpm : 1f; }

        /// <summary>いまの拍（時刻）で、そのステートのクリップが何秒目にいるべきか</summary>
        float ClipTime(int i)
        {
            float len = stLens[i];
            if (len <= 0f) return 0f;
            int m = stModes[i];
            double spb = 60.0 / Mathf.Max(1f, bakedBpm);
            double t = 0;
            if (m == MODE_BEAT) t = localBeat * spb;
            else if (m == MODE_MOVE) t = MoveBeat() * spb;
            else if (m == MODE_TIME) t = ServerSec();
            double r = t % len;
            if (r < 0) r += len;
            return (float)r;
        }

        /// <summary>いま流れているステートの実際の長さ（クロスフェードの長さの計算用）</summary>
        float SrcDuration(int c)
        {
            int si = chStart[c] + Clamp(applied[c], c);
            float len = stLens[si];
            if (len <= 0f) return 1f;
            int m = stModes[si];
            if (m == MODE_BEAT) len /= Mathf.Max(0.05f, Tempo());
            else if (m == MODE_MOVE) len /= Mathf.Max(0.05f, Tempo() * speedMul);
            return Mathf.Max(0.05f, len);
        }

        void ApplyChannel(int c, bool instant)
        {
            if (chCount[c] <= 0) return;
            Animator a = anims[chAnim[c]];
            if (a == null) return;
            int v = Clamp(sel[c], c);
            int i = chStart[c] + v;
            float len = stLens[i];
            float nt = len > 0f ? ClipTime(i) / len : 0f;
            float fade = instant ? 0f : chFade[c];
            if (fade <= 0f) a.Play(stNames[i], chLayer[c], nt);
            else a.CrossFade(stNames[i], Mathf.Min(1f, fade / SrcDuration(c)), chLayer[c], nt);
            applied[c] = v;
            lastChange[c] = Time.time;
            if (c == CH_MASTER) strobeUntil = 0f;
        }

        void ApplyAll(bool force)
        {
            if (chCount == null || sel == null || sel.Length != NCH) return;
            if (bpm != appliedBpm || speedMul != appliedSpeed) SetTempo();
            VRCShader.SetGlobalFloat(idALOff, audioOn ? 0f : 1f);
            ApplyFxToggles();
            for (int c = 0; c < NCH; c++)
            {
                if (chCount[c] <= 0) continue;
                if (force || Clamp(sel[c], c) != applied[c]) ApplyChannel(c, force);
            }
            UpdateUI();
        }

        void SetTempo()
        {
            float t = Tempo();
            if (anims != null)
                for (int k = 0; k < anims.Length; k++)
                {
                    Animator a = anims[k];
                    if (a == null) continue;
                    a.SetFloat("Tempo", t);
                    if (k == 0) a.SetFloat("MoveTempo", t * speedMul);
                }
            appliedBpm = bpm;
            appliedSpeed = speedMul;
            VRCShader.SetGlobalFloat(idBpm, bpm);   // 卓の BPM 表示
        }

        /// <summary>同期した拍（VJ コントローラーが使う）</summary>
        public double GetBeat() { return localBeat; }

        /// <summary>そのボタンが「選ばれている」か（卓のランプ）</summary>
        public bool IsOn(int c, int v)
        {
            if (sel == null || sel.Length != NCH || chCount == null) return false;
            if (c >= 0 && c < NCH) return chCount[c] > 0 && Clamp(sel[c], c) == v;
            if (c == ACT_SPEED) return (v == 0 && speedMul < 0.75f) || (v == 1 && speedMul >= 0.75f && speedMul <= 1.5f) || (v == 2 && speedMul > 1.5f);
            if (c == ACT_AUDIO) return (v != 0) == audioOn;
            if (c == ACT_SPOTON) return (v != 0) == spotOn;
            if (c == ACT_FX)
            {
                int k = v - 3;
                return k >= 0 && k < FxCount() && fxModes[k] == 1 && fxOn != null && k < fxOn.Length && fxOn[k];
            }
            if (c == ACT_SPOT)
            {
                VRCPlayerApi me2 = Networking.LocalPlayer;
                if (!Utilities.IsValid(me2)) return false;
                return (v == 0 && sp1 == me2.playerId) || (v == 1 && sp2 == me2.playerId);
            }
            if (c == ACT_UP)
            {
                VRCPlayerApi me = Networking.LocalPlayer;
                if (!Utilities.IsValid(me)) return false;
                return (v == 0 && up1 == me.playerId) || (v == 1 && up2 == me.playerId);
            }
            return false;
        }

        /// <summary>照明卓のランプをまとめて更新（24 個ずつを1つの数にして _B0〜_B3 に入れる）</summary>
        void RefreshDesk()
        {
            if (deskMat != null && deskCh != null)
            {
                float[] w = new float[16];
                for (int i = 0; i < deskCh.Length; i++)
                    if (IsOn(deskCh[i], deskVal[i])) w[i / 24] += Mathf.Pow(2f, i % 24);
                deskMat.SetVector("_B0", new Vector4(w[0], w[1], w[2], w[3]));
                deskMat.SetVector("_B1", new Vector4(w[4], w[5], w[6], w[7]));
                deskMat.SetVector("_B2", new Vector4(w[8], w[9], w[10], w[11]));
                deskMat.SetVector("_B3", new Vector4(w[12], w[13], w[14], w[15]));
            }
            if (vj != null) vj.RefreshDesks();
        }

        /// <summary>拍の位置が飛んだとき（TAP・テンポや速さの変更が届いたとき）：拍で動くチャンネルを新しい拍の位置に合わせ直す</summary>
        void RealignBeat()
        {
            for (int c = 0; c < NCH; c++)
            {
                if (chCount[c] <= 0 || c == CH_MASTER) continue;
                int i = chStart[c] + Clamp(sel[c], c);
                int m = stModes[i];
                if (m != MODE_BEAT && m != MODE_MOVE) continue;
                Animator a = anims[chAnim[c]];
                if (a == null || stLens[i] <= 0f) continue;
                a.Play(stNames[i], chLayer[c], ClipTime(i) / stLens[i]);
                lastChange[c] = Time.time;
            }
        }

        /// <summary>ときどき（4秒ごと）位置を合わせ直す。処理落ちなどで少しずつずれるのを防ぐ</summary>
        void Resync()
        {
            if (chCount == null) return;
            for (int c = 0; c < NCH; c++)
            {
                if (chCount[c] <= 0 || c == CH_MASTER) continue;
                if (Time.time - lastChange[c] < 1.5f) continue;
                int i = chStart[c] + Clamp(sel[c], c);
                if (stModes[i] == MODE_CONST || stLens[i] <= 0f) continue;
                Animator a = anims[chAnim[c]];
                if (a == null) continue;
                a.Play(stNames[i], chLayer[c], ClipTime(i) / stLens[i]);
            }
        }

        // ------------------------------------------------------------------ 表示

        string PlayerName(int id)
        {
            if (id < 0) return "-";
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(id);
            return Utilities.IsValid(p) ? p.displayName : "-";
        }

        string SpotName(int id) { return id < 0 ? "固定" : PlayerName(id); }

        string SpeedLabel()
        {
            if (speedMul < 0.75f) return "½";
            if (speedMul > 1.5f) return "2";
            return "1";
        }

        void UpdateUI()
        {
            RefreshDesk();
            if (marks != null && sel != null && sel.Length == NCH)
                for (int k = 0; k < marks.Length; k++)
                {
                    if (marks[k] == null) continue;
                    bool on = IsOn(markCh[k], markVal[k]);
                    if (marks[k].activeSelf != on) marks[k].SetActive(on);
                }
            if (statusText != null)
            {
                VRCPlayerApi o = Networking.GetOwner(gameObject);
                string who = Utilities.IsValid(o) ? o.displayName : "-";
                statusText.text = "BPM " + Mathf.RoundToInt(bpm).ToString() + "   動きの速さ ×" + SpeedLabel() + "   さいごに操作した人：" + who
                    + "\nアップで追う人：カメラ1 " + PlayerName(up1) + " ／ カメラ2 " + PlayerName(up2)
                    + (hasAudio ? "   音に反応 " + (audioOn ? "ON" : "OFF") : "")
                    + (spots != null && spots.Length > 0 ? "\nスポット：1 " + SpotName(sp1) + " ／ 2 " + SpotName(sp2) + (spotOn ? "" : "（OFF）") : "");
            }
        }
    }
}
#endif
