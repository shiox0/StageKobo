#if STAGEKOBO_UDON
// すてーじ工房：VJ リモコンの UdonSharp 版（全員に同期）
// ブラウザ版 65_vj.js の vjFrame / 85_vjui.js の操作を移植。映像そのものはシェーダー（Custom Render Texture）が描く：
//   デッキA・デッキB（StageKobo/VJDeck）→ 出力（StageKobo/VJFx）→ LED モニター・背景 LED・ステージ裏の確認モニター
// このコントローラーは「いまの設定」を全員に同期して、毎フレームの値（拍・ドン・パッドの光り方など）をマテリアルに入れる。
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Washitsu.StageKobo
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class StageKoboVJ : UdonSharpBehaviour
    {
        // ---- 同期する値の場所（int）
        public const int I_GEN = 0;       // +デッキ（0 = A, 1 = B）
        public const int I_CNT = 2;       // +デッキ
        public const int I_IMG = 4;       // +デッキ
        public const int I_PAL = 6;       // -1 = 照明の色、0〜7 = パレット
        public const int I_CMODE = 7;     // 色の付け方 0 そのまま / 1 グラデ / 2 2色 / 3 白黒
        public const int I_FX = 8;        // エフェクトのビット（FX_～）
        public const int I_MIRROR = 9;
        public const int I_KALEN = 10;
        public const int I_XFAUTO = 11;   // 拍で交互（0 / 1 / 2 / 4 拍）
        public const int I_IMGRATE = 12;  // 拍で次の画像へ（0 / 1 / 2 / 4 / 8 拍）
        public const int I_AUTO = 13;
        public const int I_AUTOBARS = 14;
        public const int I_TEXTON = 15;
        public const int I_TEXTANIM = 16; // 0 ドン / 1 点滅 / 2 固定
        public const int I_BACK = 17;     // 背景の LED に映す
        public const int I_MAP = 18;      // 0 1枚ずつ / 1 つなげて1枚
        public const int I_BLACK = 19;
        public const int I_EDIT = 20;     // 編集するデッキ
        public const int I_TAKE = 21;     // 選んだら切り替え
        public const int I_FADELEN = 22;  // 切り替えの長さ（拍。0 = カット）
        public const int NI = 23;
        // ---- 同期する値の場所（float）
        public const int F_SPD = 0;       // +デッキ
        public const int F_ZOOM = 2;      // +デッキ
        public const int F_XF = 4;        // クロスフェーダー（フェード中でないとき）
        public const int F_FFROM = 5, F_FTO = 6, F_FB0 = 7, F_FLEN = 8;   // フェード（から・へ・始まりの拍・長さ。長さ 0 = フェードなし）
        public const int F_HUE = 9, F_BRIGHT = 10, F_RATE = 11, F_AMT = 12, F_PUMP = 13;
        public const int F_IMGAT = 14;    // +デッキ：画像を選んだ拍
        public const int NF = 16;
        // ---- エフェクトのビット（ブラウザ版 VJ_FX の並び）
        public const int FX_ZOOM = 0, FX_FLASH = 1, FX_RGB = 2, FX_GLITCH = 3, FX_KALEIDO = 4, FX_SHAKE = 5, FX_ROTATE = 6, FX_FEEDBACK = 7, FX_PIXEL = 8, FX_INVERT = 9, FX_POSTER = 10, FX_EDGE = 11, FX_SCAN = 12;
        public const int GEN_IMAGE = 23, GEN_TEXT = 24, GEN_BLACK = 25, NGEN = 26;
        // ---- ボタンの動作
        public const int V_PAD = 100;      // val 0 フラッシュ / 1 反転 / 2 グリッチ / 3 ズーム / 4 ストロボ（2拍）
        public const int V_BLACK = 101, V_EDIT = 102, V_TAKE = 103;
        public const int V_CUT = 104;      // val 0 A へ / 1 B へ / 2 もう一方へ / 3 編集中のデッキへ
        public const int V_FADELEN = 105, V_XFAUTO = 106, V_GEN = 107, V_SPEED = 108, V_COUNT = 109, V_ZOOM = 110, V_IMG = 111, V_IMGRATE = 112;
        public const int V_PAL = 113, V_CMODE = 114, V_HUE = 115, V_FX = 116, V_MIRROR = 117, V_KALE = 118, V_RATE = 119, V_PUMP = 120, V_AMT = 121, V_BRIGHT = 122;
        public const int V_TEXTON = 123, V_TEXTANIM = 124, V_AUTO = 125, V_AUTOBARS = 126, V_PRESET = 127, V_SLOT = 128, V_BACK = 129, V_MAP = 130;
        // デッキを決めた操作（操作パネルの左 = A、右 = B）：動作番号 + 1000 = デッキA、+ 2000 = デッキB（映像の素・速さ・数・ズーム・画像）
        public const int DECK_A = 1000, DECK_B = 2000;

        [Header("インポーターが設定します（手で変えなくてOK）")]
        public StageKoboController main;
        public Material deckA, deckB, fx;          // Custom Render Texture のマテリアル
        public Texture deckATex, deckBTex;
        public Texture2D[] images;                 // 画像バンク
        public Texture textTex;
        public float textAsp = 4f;
        public float resX = 640f, resY = 360f;
        public Vector4[] vjPal;                    // 8 パレット × 3 色（リニア）
        public Vector4[] lightPal;                 // 照明のパレットのステート × 3 色（リニア）
        public int lightPalCh = 2;                 // 照明の「色（パレット）」チャンネル
        public int[] presetI; public float[] presetF;   // 組み込みシーン（NI / NF ずつ）
        public int[] slotI; public float[] slotF;       // ブラウザで登録したシーン
        public Transform deskCenter;               // ステージ裏の卓（近くにいるときだけ編集中のデッキを描く）
        // 卓のボタン（ランプ）
        public Material[] deskMats;
        public int[] btnDesk, btnSlot, btnKind, btnAct, btnVal;
        // 操作パネル（UI）：選ばれているボタンの印と、いまの状態の文字（A・B・まん中。パネルの数だけ）
        public GameObject[] uiMarks;
        public int[] uiAct, uiVal;
        public Text[] infoA, infoB, infoC;
        public string[] genNames, fxNames, cmodeNames, mirrorNames;
        float nextInfo;

        [UdonSynced] public int[] vi;
        [UdonSynced] public float[] vf;

        bool ready;
        int autoIdx = int.MinValue;
        int lastBeat = -1;
        int palKey = -999;
        int[] imgShown;
        bool[] activeShown;
        float tFlash = -99f, tInvert = -99f, tGlitch = -99f, tZoom = -99f, strobeUntil = -99f;
        float blackS;
        int idBack, idMap;
        Vector4 c1, c2, c3;

        void Start()
        {
            Setup();
            if (Networking.IsOwner(gameObject)) RequestSerialization();
            ApplyState();
        }

        void Setup()
        {
            if (ready) return;
            ready = true;
            if (vi == null || vi.Length != NI) { vi = new int[NI]; vi[I_PAL] = -1; vi[I_CNT] = 6; vi[I_CNT + 1] = 6; vi[I_KALEN] = 6; vi[I_TAKE] = 1; vi[I_FADELEN] = 2; vi[I_AUTOBARS] = 4; vi[I_FX] = 1; }
            if (vf == null || vf.Length != NF) { vf = new float[NF]; vf[F_SPD] = 1f; vf[F_SPD + 1] = 1f; vf[F_ZOOM] = 1f; vf[F_ZOOM + 1] = 1f; vf[F_BRIGHT] = 1f; vf[F_RATE] = 1f; vf[F_AMT] = 1f; vf[F_PUMP] = 0.5f; }
            imgShown = new int[2]; imgShown[0] = -1; imgShown[1] = -1;
            activeShown = new bool[2];
            idBack = VRCShader.PropertyToID("_Udon_SKVJBack");
            idMap = VRCShader.PropertyToID("_Udon_SKVJMap");
            if (deckA != null) deckA.SetFloat("_Driven", 1f);
            if (deckB != null) deckB.SetFloat("_Driven", 1f);
            if (fx != null) { fx.SetFloat("_ResX", resX); fx.SetFloat("_ResY", resY); fx.SetFloat("_Asp", resX / resY); if (textTex != null) fx.SetTexture("_Txt", textTex); fx.SetFloat("_TxtAsp", textAsp); }
            for (int k = 0; k < 2; k++)
            {
                Material m = k == 0 ? deckA : deckB;
                if (m == null) continue;
                m.SetFloat("_Asp", resX / resY);
                if (textTex != null) m.SetTexture("_Txt", textTex);
                m.SetFloat("_TxtAsp", textAsp);
            }
        }

        public override void OnDeserialization()
        {
            Setup();
            if (vi == null || vi.Length != NI || vf == null || vf.Length != NF) return;
            ApplyState();
        }

        // ------------------------------------------------------------------ 拍・時間

        double Beat() { return main != null ? main.GetBeat() : Time.time * 2.0; }
        float Spb() { return 60f / Mathf.Max(40f, main != null ? main.bpm : 120f); }
        bool IsOwner() { return Networking.IsOwner(gameObject); }

        void TakeOwner()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        void Sync()
        {
            RequestSerialization();
            ApplyState();
        }

        /// <summary>いま見えている（フェード先の）デッキ</summary>
        int ShownDeck()
        {
            if (vf[F_FLEN] > 0f) return vf[F_FTO] > 0.5f ? 1 : 0;
            return vf[F_XF] < 0.5f ? 0 : 1;
        }

        float XfNow(double beat)
        {
            float xf = vf[F_XF];
            if (vf[F_FLEN] > 0f)
            {
                float k = Mathf.Clamp01((float)(beat - vf[F_FB0]) / vf[F_FLEN]);
                k = k * k * (3f - 2f * k);
                xf = Mathf.Lerp(vf[F_FFROM], vf[F_FTO], k);
            }
            if (vi[I_XFAUTO] > 0) xf = (Mathf.FloorToInt((float)(beat / vi[I_XFAUTO])) % 2 == 0) ? 0f : 1f;
            return xf;
        }

        /// <summary>クロスフェード。beats = 0 はカット。次の拍の頭から（押すのが少し遅れても拍に合う）</summary>
        void Fade(float to, float beats)
        {
            double b = Beat();
            double fb = b % 1.0;
            double b0 = fb < 0.15 ? b - fb : b - fb + 1.0;
            vf[F_FFROM] = XfNow(b);
            vf[F_FTO] = to;
            vf[F_FB0] = (float)b0;
            vf[F_FLEN] = Mathf.Max(0.02f, beats);
            vi[I_XFAUTO] = 0;
        }

        // ------------------------------------------------------------------ 毎フレーム

        void Update()
        {
            if (!ready || vi == null || vi.Length != NI) return;
            double beatD = Beat();
            float t = Time.time;
            float spb = Spb();

            // フェードの終わり（持ち主だけが確定させて同期する。ほかの人は同じ式で計算しているので見た目は同じ）
            if (vf[F_FLEN] > 0f && beatD - vf[F_FB0] >= vf[F_FLEN] && IsOwner())
            {
                vf[F_XF] = vf[F_FTO]; vf[F_FLEN] = 0f;
                Sync();
            }
            // オートVJ（持ち主だけ）
            if (vi[I_AUTO] == 1 && IsOwner())
            {
                int idx = Mathf.FloorToInt((float)(beatD / (4.0 * Mathf.Max(1, vi[I_AUTOBARS]))));
                if (autoIdx == int.MinValue) autoIdx = idx;
                else if (idx != autoIdx) { autoIdx = idx; AutoStep(); }
            }
            else autoIdx = int.MinValue;

            float xf = XfNow(beatD);
            float beat = (float)beatD;
            float pulse = Mathf.Exp(-Frac(beat) * 4.5f);
            UpdateColors();

            // ---- デッキ（映っていない方は描かない。ステージ裏の卓の近くにいる人は、確認用モニターのために両方描く）
            bool near = false;
            if (deskCenter != null)
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (Utilities.IsValid(lp) && Vector3.Distance(lp.GetPosition(), deskCenter.position) < 9f) near = true;
            }
            for (int k = 0; k < 2; k++)
            {
                Material m = k == 0 ? deckA : deckB;
                if (m == null) continue;
                bool active = (k == 0 ? xf < 0.999f : xf > 0.001f) || near;
                if (active != activeShown[k]) { activeShown[k] = active; m.SetFloat("_Active", active ? 1f : 0f); }
                if (!active) continue;
                float sp = vf[F_SPD + k];
                m.SetFloat("_Bt", (float)(beatD * sp));
                m.SetFloat("_T", t * sp);
                m.SetFloat("_Pulse", pulse);
                if (vi[I_GEN + k] == GEN_IMAGE) UpdateImage(k, m, beatD);
            }

            // ---- エフェクト（ブラウザ版 vjFrame と同じ式）
            if (fx == null) return;
            int fxb = vi[I_FX];
            float rate = Mathf.Max(0.25f, vf[F_RATE]);
            float bR = beat / rate;
            float pR = Mathf.Exp(-Frac(bR) * 4.5f);
            int iR = Mathf.FloorToInt(bR);
            float amt = vf[F_AMT], pump = vf[F_PUMP];
            float oneZoom = Env(t, tZoom, 1f, spb), oneFlash = Env(t, tFlash, 1f, spb), oneGl = Env(t, tGlitch, 1.5f, spb);
            float oneInv = Env(t, tInvert, 1f, spb) > 0f ? 1f : 0f;
            fx.SetFloat("_Xf", xf);
            fx.SetFloat("_T", t);
            fx.SetFloat("_ZoomP", pump * 0.05f * pulse + (Bit(fxb, FX_ZOOM) ? amt * 0.16f * pR : 0f) + oneZoom * 0.35f);
            fx.SetFloat("_Flash", (Bit(fxb, FX_FLASH) ? amt * pR * pR * pR * 0.7f : 0f) + oneFlash * 0.9f);
            float invBase = Bit(fxb, FX_INVERT) ? (float)(((iR % 2) + 2) % 2) : 0f;
            fx.SetFloat("_Invert", oneInv > 0f ? 1f - invBase : invBase);
            bool glOn = Bit(fxb, FX_GLITCH) && Hash1(iR * 13.7f) > 0.35f;
            float glitch = Mathf.Min(1f, (glOn ? amt * (0.25f + 0.75f * pR) : 0f) + oneGl);
            fx.SetFloat("_Glitch", glitch);
            fx.SetFloat("_GlitchSeed", Mathf.Floor(t * 18f) * 1.37f + iR);
            fx.SetFloat("_Rgb", (Bit(fxb, FX_RGB) ? amt * (0.25f + 0.75f * pR) : 0f) + oneGl * 0.5f);
            float sh = (Bit(fxb, FX_SHAKE) ? amt * pR : 0f) + oneZoom * 0.5f;
            float ft = Mathf.Floor(t * 30f);
            fx.SetFloat("_ShakeX", (Hash1(iR * 3.1f + ft) - 0.5f) * 0.05f * sh);
            fx.SetFloat("_ShakeY", (Hash1(iR * 5.7f + ft + 2f) - 0.5f) * 0.05f * sh);
            fx.SetFloat("_Rot", Bit(fxb, FX_ROTATE) ? beat * 0.09f : 0f);
            fx.SetFloat("_Kale", Bit(fxb, FX_KALEIDO) ? Mathf.Max(2, vi[I_KALEN]) : 0f);
            fx.SetFloat("_KaleRot", beat * 0.12f);
            fx.SetFloat("_Pix", Bit(fxb, FX_PIXEL) ? amt * (0.15f + 0.85f * pR) : 0f);
            fx.SetFloat("_Hue", vf[F_HUE] * beat / 16f * 6.2831853f);
            fx.SetFloat("_Strobe", t < strobeUntil ? (Frac(t * 12f) < 0.4f ? 1f : 0f) : 0f);
            fx.SetFloat("_Bright", vf[F_BRIGHT] * (1f - pump * 0.45f * (1f - pulse)));
            blackS = Mathf.Lerp(blackS, vi[I_BLACK] == 1 ? 1f : 0f, 1f - Mathf.Exp(-Time.deltaTime * 14f));
            fx.SetFloat("_Black", blackS);
            float txtOn = vi[I_TEXTON] == 1 && textTex != null ? (vi[I_TEXTANIM] == 1 ? (Mathf.FloorToInt(beat * 2f) % 2 != 0 ? 1f : 0f) : 1f) : 0f;
            fx.SetFloat("_TxtOn", txtOn);
            fx.SetFloat("_TxtK", vi[I_TEXTANIM] == 2 ? 0.3f : pulse);

            // 拍が変わったら：画像が拍で切り替わるときのランプ
            int bi = Mathf.FloorToInt(beat);
            if (bi != lastBeat)
            {
                lastBeat = bi;
                if (vi[I_IMGRATE] > 0 && (vi[I_GEN] == GEN_IMAGE || vi[I_GEN + 1] == GEN_IMAGE)) RefreshDesks();
            }
            // 操作パネルの文字（クロスフェーダーの位置などが動くので、ときどき書き直す）
            if (infoC != null && infoC.Length > 0 && Time.time >= nextInfo)
            {
                nextInfo = Time.time + 0.25f;
                RefreshInfo();
            }
        }

        float Frac(float x) { return x - Mathf.Floor(x); }
        bool Bit(int bits, int b) { return ((bits >> b) & 1) == 1; }
        float Hash1(float n) { float x = Mathf.Sin(n * 127.1f) * 43758.5453f; return x - Mathf.Floor(x); }
        float Env(float t, float t0, float len, float spb) { float a = (t - t0) / (len * spb); if (a < 0f || a > 1f) return 0f; return (1f - a) * (1f - a); }

        /// <summary>いま映す画像の番号：選んだ画像から、「拍で次の画像へ」の拍数ごとに1つずつ進む</summary>
        public int ImgIndex(int k)
        {
            int n = images != null ? images.Length : 0;
            if (n == 0) return 0;
            int ir = vi[I_IMGRATE];
            int step = 0;
            if (ir > 0) { double b = Beat() - vf[F_IMGAT + k]; if (b < 0) b = 0; step = Mathf.FloorToInt((float)(b / ir)); }
            return (((vi[I_IMG + k] + step) % n) + n) % n;
        }

        void UpdateImage(int k, Material m, double beatD)
        {
            int n = images != null ? images.Length : 0;
            int idx = n > 0 ? ImgIndex(k) : -1;
            if (idx != imgShown[k])
            {
                imgShown[k] = idx;
                Texture2D tex = idx >= 0 ? images[idx] : null;
                m.SetFloat("_ImgOk", tex != null ? 1f : 0f);
                if (tex != null) { m.SetTexture("_Img", tex); m.SetFloat("_ImgAsp", (float)tex.width / Mathf.Max(1, tex.height)); }
            }
            int ir = vi[I_IMGRATE];
            float x = ir > 0 ? (float)((beatD - vf[F_IMGAT + k]) / ir) : (float)(beatD / 16.0);
            m.SetFloat("_ImgK", Frac(x));
        }

        /// <summary>VJ の色（パレット or 照明の色）。照明のパレットが変わったときもここで追いかける</summary>
        void UpdateColors()
        {
            int pal = vi[I_PAL];
            int lp = -1;
            if (pal < 0 && main != null && main.sel != null && lightPalCh < main.sel.Length) lp = main.sel[lightPalCh];
            int key = pal >= 0 ? pal : -10 - lp;
            if (key == palKey) return;
            palKey = key;
            if (pal >= 0 && vjPal != null && pal * 3 + 2 < vjPal.Length) { c1 = vjPal[pal * 3]; c2 = vjPal[pal * 3 + 1]; c3 = vjPal[pal * 3 + 2]; }
            else if (lightPal != null && lightPal.Length >= 3)
            {
                int i = Mathf.Clamp(lp, 0, lightPal.Length / 3 - 1);
                c1 = lightPal[i * 3]; c2 = lightPal[i * 3 + 1]; c3 = lightPal[i * 3 + 2];
            }
            for (int k = 0; k < 3; k++)
            {
                Material m = k == 0 ? deckA : k == 1 ? deckB : fx;
                if (m == null) continue;
                m.SetVector("_C1", c1); m.SetVector("_C2", c2); m.SetVector("_C3", c3);
            }
        }

        // ------------------------------------------------------------------ 設定をマテリアルへ

        void ApplyState()
        {
            if (vi == null || vi.Length != NI) return;
            for (int k = 0; k < 2; k++)
            {
                Material m = k == 0 ? deckA : deckB;
                if (m == null) continue;
                m.SetFloat("_Gen", vi[I_GEN + k]);
                m.SetFloat("_Cnt", vi[I_CNT + k]);
                m.SetFloat("_Zoom", vf[F_ZOOM + k]);
                imgShown[k] = -1;
                if (vi[I_GEN + k] != GEN_IMAGE) m.SetFloat("_ImgOk", 0f);
            }
            if (fx != null)
            {
                int fxb = vi[I_FX];
                fx.SetFloat("_Mirror", vi[I_MIRROR]);
                fx.SetFloat("_Fb", Bit(fxb, FX_FEEDBACK) ? 1f : 0f);
                fx.SetFloat("_Edge", Bit(fxb, FX_EDGE) ? 1f : 0f);
                fx.SetFloat("_ColMode", vi[I_CMODE]);
                fx.SetFloat("_Post", Bit(fxb, FX_POSTER) ? 1f : 0f);
                fx.SetFloat("_Scan", Bit(fxb, FX_SCAN) ? 1f : 0f);
            }
            VRCShader.SetGlobalFloat(idBack, vi[I_BACK] == 1 ? 1f : 0f);
            VRCShader.SetGlobalFloat(idMap, vi[I_MAP] == 1 ? 1f : 0f);
            palKey = -999;
            UpdateColors();
            RefreshDesks();
        }

        // ------------------------------------------------------------------ ボタン

        /// <summary>卓のボタン（StageKoboButton）から呼ばれる</summary>
        public void Press(int act, int val)
        {
            Setup();
            // パッドは「一瞬のイベント」として全員に送る（状態は同期しない）
            if (act == V_PAD)
            {
                if (val == 0) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PadFlash));
                else if (val == 1) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PadInvert));
                else if (val == 2) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PadGlitch));
                else if (val == 3) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PadZoom));
                else SendCustomNetworkEvent(NetworkEventTarget.All, nameof(PadStrobe));
                return;
            }
            TakeOwner();
            int ed = vi[I_EDIT];
            int deck = -1;   // 操作パネルの「デッキを決めた操作」
            if (act >= DECK_A)
            {
                deck = act >= DECK_B ? 1 : 0;
                act = act % 1000;
                ed = deck;
            }
            if (deck >= 0 && (act == V_GEN || act == V_IMG))
            {
                PickSourceOn(deck, act == V_IMG ? GEN_IMAGE : val, act == V_IMG ? val : -1);
                Sync();
                return;
            }
            if (act == V_BLACK) vi[I_BLACK] = vi[I_BLACK] == 1 ? 0 : 1;
            else if (act == V_EDIT) vi[I_EDIT] = val == 1 ? 1 : 0;
            else if (act == V_TAKE) vi[I_TAKE] = vi[I_TAKE] == 1 ? 0 : 1;
            else if (act == V_CUT)
            {
                int to = val == 0 ? 0 : val == 1 ? 1 : val == 2 ? 1 - ShownDeck() : ed;
                Fade(to, vi[I_FADELEN]);
            }
            else if (act == V_FADELEN) vi[I_FADELEN] = val;
            else if (act == V_XFAUTO)
            {
                vf[F_FLEN] = 0f;
                if (val == 0) vf[F_XF] = Mathf.Round(XfNow(Beat()));
                vi[I_XFAUTO] = val;
            }
            else if (act == V_GEN) PickSource(val, -1);
            else if (act == V_IMG) PickSource(GEN_IMAGE, val);
            else if (act == V_SPEED) vf[F_SPD + ed] = val == 0 ? 0.25f : val == 1 ? 0.5f : val == 3 ? 2f : val == 4 ? 4f : 1f;
            else if (act == V_COUNT) vi[I_CNT + ed] = Mathf.Clamp(vi[I_CNT + ed] + val, 1, 12);
            else if (act == V_ZOOM) vf[F_ZOOM + ed] = Mathf.Clamp(val > 0 ? vf[F_ZOOM + ed] * 1.25f : vf[F_ZOOM + ed] / 1.25f, 0.4f, 3f);
            else if (act == V_IMGRATE) vi[I_IMGRATE] = val;
            else if (act == V_PAL) vi[I_PAL] = val;
            else if (act == V_CMODE) vi[I_CMODE] = val;
            else if (act == V_HUE) vf[F_HUE] = val == 1 ? 0.5f : val == 2 ? 2f : 0f;
            else if (act == V_FX) vi[I_FX] = vi[I_FX] ^ (1 << val);
            else if (act == V_MIRROR) vi[I_MIRROR] = val;
            else if (act == V_KALE) { vi[I_KALEN] = val; vi[I_FX] = vi[I_FX] | (1 << FX_KALEIDO); }
            else if (act == V_RATE) vf[F_RATE] = val == 0 ? 0.5f : val == 2 ? 2f : val == 3 ? 4f : 1f;
            else if (act == V_PUMP) vf[F_PUMP] = val == 0 ? 0f : val == 1 ? 0.3f : val == 3 ? 1f : 0.6f;
            else if (act == V_AMT) vf[F_AMT] = val == 0 ? 0.5f : val == 2 ? 1.6f : 1f;
            else if (act == V_BRIGHT) vf[F_BRIGHT] = val == 0 ? 0.5f : val == 1 ? 0.75f : val == 3 ? 1.3f : 1f;
            else if (act == V_TEXTON) vi[I_TEXTON] = vi[I_TEXTON] == 1 ? 0 : 1;
            else if (act == V_TEXTANIM) vi[I_TEXTANIM] = val;
            else if (act == V_AUTO) vi[I_AUTO] = vi[I_AUTO] == 1 ? 0 : 1;
            else if (act == V_AUTOBARS) vi[I_AUTOBARS] = val;
            else if (act == V_PRESET) ApplyPreset(val);
            else if (act == V_SLOT) ApplySlot(val);
            else if (act == V_BACK) vi[I_BACK] = val;
            else if (act == V_MAP) vi[I_MAP] = val;
            Sync();
        }

        /// <summary>
        /// 操作パネル：決めたデッキに映像の素を入れる。映っているデッキならすぐ変わる。
        /// 映っていないデッキ（裏で準備）は、「選んだら切り替え」が ON なら拍に合わせて切り替える（OFF ならそのまま待つ）
        /// </summary>
        void PickSourceOn(int deck, int gen, int img)
        {
            vi[I_GEN + deck] = gen;
            if (img >= 0) vi[I_IMG + deck] = img;
            if (gen == GEN_IMAGE) { double b = Beat(); vf[F_IMGAT + deck] = (float)(b - b % 1.0 + (deck == ShownDeck() ? 0.0 : 1.0)); }
            vi[I_EDIT] = deck;
            if (vi[I_TAKE] == 1 && deck != ShownDeck()) Fade(deck, vi[I_FADELEN]);
        }

        /// <summary>映像の素を選ぶ。「選んだら切り替え」なら見えていない方のデッキに入れて、拍に合わせて切り替える</summary>
        void PickSource(int gen, int img)
        {
            if (vi[I_TAKE] == 1)
            {
                int shown = ShownDeck(), hid = 1 - shown;
                CopyDeck(shown, hid);
                vi[I_GEN + hid] = gen;
                if (img >= 0) vi[I_IMG + hid] = img;
                if (gen == GEN_IMAGE) { double b = Beat(); vf[F_IMGAT + hid] = (float)(b - b % 1.0 + 1.0); }
                vi[I_EDIT] = hid;
                Fade(hid, vi[I_FADELEN]);
            }
            else
            {
                int ed = vi[I_EDIT];
                vi[I_GEN + ed] = gen;
                if (img >= 0) vi[I_IMG + ed] = img;
                if (gen == GEN_IMAGE) { double b = Beat(); vf[F_IMGAT + ed] = (float)(b - b % 1.0); }
            }
        }

        void CopyDeck(int from, int to)
        {
            if (from == to) return;
            vi[I_GEN + to] = vi[I_GEN + from]; vi[I_CNT + to] = vi[I_CNT + from]; vi[I_IMG + to] = vi[I_IMG + from];
            vf[F_SPD + to] = vf[F_SPD + from]; vf[F_ZOOM + to] = vf[F_ZOOM + from]; vf[F_IMGAT + to] = vf[F_IMGAT + from];
        }

        /// <summary>組み込みシーン：デッキ・エフェクト・色・ミラー・万華鏡・色相・画像の切り替えを入れ替えて、A をすぐ映す</summary>
        void ApplyPreset(int p)
        {
            if (presetI == null || (p + 1) * NI > presetI.Length) return;
            int o = p * NI, of = p * NF;
            for (int k = 0; k < 2; k++)
            {
                vi[I_GEN + k] = presetI[o + I_GEN + k]; vi[I_CNT + k] = presetI[o + I_CNT + k]; vi[I_IMG + k] = presetI[o + I_IMG + k];
                vf[F_SPD + k] = presetF[of + F_SPD + k]; vf[F_ZOOM + k] = presetF[of + F_ZOOM + k];
            }
            vi[I_FX] = presetI[o + I_FX]; vi[I_PAL] = presetI[o + I_PAL]; vi[I_CMODE] = presetI[o + I_CMODE];
            vi[I_MIRROR] = presetI[o + I_MIRROR]; vi[I_KALEN] = presetI[o + I_KALEN]; vi[I_IMGRATE] = presetI[o + I_IMGRATE];
            vf[F_HUE] = presetF[of + F_HUE];
            vf[F_FLEN] = 0f; vf[F_XF] = 0f; vi[I_XFAUTO] = 0; vi[I_EDIT] = 0;
        }

        /// <summary>ブラウザで登録したシーン</summary>
        void ApplySlot(int s)
        {
            if (slotI == null || (s + 1) * NI > slotI.Length) return;
            int o = s * NI, of = s * NF;
            for (int k = 0; k < 2; k++)
            {
                vi[I_GEN + k] = slotI[o + I_GEN + k]; vi[I_CNT + k] = slotI[o + I_CNT + k]; vi[I_IMG + k] = slotI[o + I_IMG + k];
                vf[F_SPD + k] = slotF[of + F_SPD + k]; vf[F_ZOOM + k] = slotF[of + F_ZOOM + k];
            }
            vi[I_FX] = slotI[o + I_FX]; vi[I_PAL] = slotI[o + I_PAL]; vi[I_CMODE] = slotI[o + I_CMODE];
            vi[I_MIRROR] = slotI[o + I_MIRROR]; vi[I_KALEN] = slotI[o + I_KALEN]; vi[I_IMGRATE] = slotI[o + I_IMGRATE];
            vi[I_TEXTON] = slotI[o + I_TEXTON]; vi[I_TEXTANIM] = slotI[o + I_TEXTANIM];
            vf[F_HUE] = slotF[of + F_HUE]; vf[F_BRIGHT] = slotF[of + F_BRIGHT]; vf[F_RATE] = slotF[of + F_RATE]; vf[F_AMT] = slotF[of + F_AMT]; vf[F_PUMP] = slotF[of + F_PUMP];
            vf[F_FLEN] = 0f; vf[F_XF] = slotF[of + F_XF]; vi[I_XFAUTO] = 0;
        }

        /// <summary>オートVJ：見えていない方のデッキに次の映像を入れて、2拍でフェード（持ち主だけが決めて同期する）</summary>
        void AutoStep()
        {
            int hid = 1 - ShownDeck();
            int n = images != null ? images.Length : 0;
            int g = vi[I_GEN + hid];
            for (int tries = 0; tries < 8; tries++)
            {
                g = Random.Range(0, NGEN);
                if (g == GEN_BLACK || g == GEN_TEXT || (g == GEN_IMAGE && n == 0)) continue;
                if (g != vi[I_GEN + hid]) break;
            }
            if (g == GEN_BLACK || g == GEN_TEXT || (g == GEN_IMAGE && n == 0)) g = 0;
            vi[I_GEN + hid] = g;
            vi[I_CNT + hid] = 3 + Random.Range(0, 6);
            int r = Random.Range(0, 4);
            vf[F_SPD + hid] = r == 0 ? 0.5f : r == 3 ? 2f : 1f;
            int fxb = vi[I_FX];
            int[] opts = new int[] { FX_KALEIDO, FX_RGB, FX_GLITCH, FX_FLASH, FX_FEEDBACK, FX_PIXEL, FX_ROTATE, FX_SHAKE };
            for (int i = 0; i < opts.Length; i++) fxb &= ~(1 << opts[i]);
            for (int i = 0; i < 2; i++) if (Random.value < 0.6f) fxb |= 1 << opts[Random.Range(0, opts.Length)];
            fxb |= 1 << FX_ZOOM;
            vi[I_FX] = fxb;
            Fade(hid, 2f);
            Sync();
        }

        // ------------------------------------------------------------------ パッド（全員に届くイベント）

        public void PadFlash() { tFlash = Time.time; }
        public void PadInvert() { tInvert = Time.time; }
        public void PadGlitch() { tGlitch = Time.time; }
        public void PadZoom() { tZoom = Time.time; }
        public void PadStrobe() { strobeUntil = Time.time + 2f * Spb(); }

        // ------------------------------------------------------------------ 卓のランプ

        /// <summary>そのボタンが「選ばれている」か</summary>
        public bool IsOn(int act, int val)
        {
            int ed = vi[I_EDIT];
            if (act >= DECK_A)
            {
                int dk = act >= DECK_B ? 1 : 0;
                act = act % 1000;
                if (act == V_IMG) return vi[I_GEN + dk] == GEN_IMAGE && ImgIndex(dk) == val;
                ed = dk;
            }
            if (act == V_BLACK) return vi[I_BLACK] == 1;
            if (act == V_EDIT) return ed == val;
            if (act == V_TAKE) return vi[I_TAKE] == 1;
            if (act == V_CUT)
            {
                if (val == 0) return vi[I_XFAUTO] == 0 && vf[F_FLEN] <= 0f && vf[F_XF] <= 0.001f;
                if (val == 1) return vi[I_XFAUTO] == 0 && vf[F_FLEN] <= 0f && vf[F_XF] >= 0.999f;
                if (val == 3) return ShownDeck() == ed;
                return false;
            }
            if (act == V_FADELEN) return vi[I_FADELEN] == val;
            if (act == V_XFAUTO) return vi[I_XFAUTO] == val;
            if (act == V_GEN) return vi[I_GEN + ed] == val;
            if (act == V_IMG) { int sd = ShownDeck(); return vi[I_GEN + sd] == GEN_IMAGE && ImgIndex(sd) == val; }
            if (act == V_SPEED) { float s = vf[F_SPD + ed]; return Mathf.Abs(s - (val == 0 ? 0.25f : val == 1 ? 0.5f : val == 3 ? 2f : val == 4 ? 4f : 1f)) < 0.01f; }
            if (act == V_IMGRATE) return vi[I_IMGRATE] == val;
            if (act == V_PAL) return vi[I_PAL] == val;
            if (act == V_CMODE) return vi[I_CMODE] == val;
            if (act == V_HUE) { float h = vf[F_HUE]; return val == 0 ? h == 0f : val == 1 ? Mathf.Abs(h - 0.5f) < 0.01f : Mathf.Abs(h - 2f) < 0.01f; }
            if (act == V_FX) return Bit(vi[I_FX], val);
            if (act == V_MIRROR) return vi[I_MIRROR] == val;
            if (act == V_KALE) return Bit(vi[I_FX], FX_KALEIDO) && vi[I_KALEN] == val;
            if (act == V_RATE) { float r = vf[F_RATE]; return Mathf.Abs(r - (val == 0 ? 0.5f : val == 2 ? 2f : val == 3 ? 4f : 1f)) < 0.01f; }
            if (act == V_PUMP) { float p = vf[F_PUMP]; return val == 2 ? Mathf.Abs(p - 0.6f) < 0.05f || Mathf.Abs(p - 0.5f) < 0.05f : Mathf.Abs(p - (val == 0 ? 0f : val == 1 ? 0.3f : 1f)) < 0.05f; }
            if (act == V_AMT) { float a = vf[F_AMT]; return Mathf.Abs(a - (val == 0 ? 0.5f : val == 2 ? 1.6f : 1f)) < 0.05f; }
            if (act == V_BRIGHT) { float b = vf[F_BRIGHT]; return Mathf.Abs(b - (val == 0 ? 0.5f : val == 1 ? 0.75f : val == 3 ? 1.3f : 1f)) < 0.05f; }
            if (act == V_TEXTON) return vi[I_TEXTON] == 1;
            if (act == V_TEXTANIM) return vi[I_TEXTANIM] == val;
            if (act == V_AUTO) return vi[I_AUTO] == 1;
            if (act == V_AUTOBARS) return vi[I_AUTOBARS] == val;
            if (act == V_BACK) return vi[I_BACK] == val;
            if (act == V_MAP) return vi[I_MAP] == val;
            return false;
        }

        // ------------------------------------------------------------------ 操作パネル（UI）

        /// <summary>操作パネルの印（選ばれているボタン）と状態の文字</summary>
        public void RefreshUI()
        {
            if (vi == null || vi.Length != NI || vf == null || vf.Length != NF) return;
            if (uiMarks != null && uiAct != null && uiVal != null)
                for (int i = 0; i < uiMarks.Length; i++)
                {
                    if (uiMarks[i] == null) continue;
                    bool on = IsOn(uiAct[i], uiVal[i]);
                    if (uiMarks[i].activeSelf != on) uiMarks[i].SetActive(on);
                }
            RefreshInfo();
        }

        string NameOf(string[] names, int i, string fallback)
        {
            if (names == null || i < 0 || i >= names.Length || names[i] == null || names[i] == "") return fallback;
            return names[i];
        }

        string SpeedName(float s)
        {
            if (s < 0.3f) return "¼";
            if (s < 0.75f) return "½";
            if (s > 3f) return "4";
            if (s > 1.5f) return "2";
            return "1";
        }

        string DeckText(int k)
        {
            int shown = ShownDeck();
            int g = vi[I_GEN + k];
            string src = g == GEN_IMAGE ? "画像 " + (ImgIndex(k) + 1).ToString() : NameOf(genNames, g, "映像" + g.ToString());
            return (k == 0 ? "デッキA" : "デッキB") + (shown == k ? "　● いま映っている" : "　○ 裏で準備（待機）")
                + "\n" + src
                + "\n速さ ×" + SpeedName(vf[F_SPD + k]) + "　数 " + vi[I_CNT + k].ToString() + "　ズーム " + Mathf.RoundToInt(vf[F_ZOOM + k] * 100f).ToString() + "%";
        }

        string CenterText()
        {
            float xf = Mathf.Clamp01(XfNow(Beat()));
            int n = Mathf.RoundToInt(xf * 10f);
            string bar = "";
            for (int i = 0; i < 10; i++) bar += i < n ? "■" : "□";
            string mix = "A " + bar + " B";
            if (vf[F_FLEN] > 0f) mix += "（" + (vf[F_FTO] >= 0.5f ? "B" : "A") + " へ切り替え中）";
            else if (vi[I_XFAUTO] > 0) mix += "（" + vi[I_XFAUTO].ToString() + "拍で交互）";
            int fxb = vi[I_FX];
            string fxs = "";
            for (int b = 0; b <= FX_SCAN; b++)
            {
                if (!Bit(fxb, b)) continue;
                string nm = NameOf(fxNames, b, "FX" + b.ToString());
                if (b == FX_KALEIDO) nm += vi[I_KALEN].ToString();
                fxs += (fxs == "" ? "" : "・") + nm;
            }
            if (fxs == "") fxs = "なし";
            int p = vi[I_PAL];
            string pal = p < 0 ? "照明の色" : "パレット" + (p + 1).ToString();
            string hue = vf[F_HUE] > 1f ? "・色が速く回る" : vf[F_HUE] > 0f ? "・色が回る" : "";
            return "出力　" + mix
                + "\nエフェクト：" + fxs + "　ミラー：" + NameOf(mirrorNames, vi[I_MIRROR], "なし")
                + "\n色：" + pal + "・" + NameOf(cmodeNames, vi[I_CMODE], "") + hue
                + "\nドン " + (vf[F_RATE] < 0.75f ? "½" : vf[F_RATE] > 3f ? "4" : vf[F_RATE] > 1.5f ? "2" : "1") + "拍　ドゥン " + (vf[F_PUMP] <= 0.01f ? "なし" : vf[F_PUMP] < 0.4f ? "弱" : vf[F_PUMP] > 0.8f ? "強" : "中")
                + "　明るさ " + Mathf.RoundToInt(vf[F_BRIGHT] * 100f).ToString() + "%"
                + (vi[I_BLACK] == 1 ? "　■暗転中" : "") + (vi[I_AUTO] == 1 ? "　オートVJ " + vi[I_AUTOBARS].ToString() + "小節" : "");
        }

        void RefreshInfo()
        {
            if (vi == null || vi.Length != NI || vf == null || vf.Length != NF) return;
            if (infoA != null && infoA.Length > 0) { string t = DeckText(0); for (int i = 0; i < infoA.Length; i++) if (infoA[i] != null) infoA[i].text = t; }
            if (infoB != null && infoB.Length > 0) { string t = DeckText(1); for (int i = 0; i < infoB.Length; i++) if (infoB[i] != null) infoB[i].text = t; }
            if (infoC != null && infoC.Length > 0) { string t = CenterText(); for (int i = 0; i < infoC.Length; i++) if (infoC[i] != null) infoC[i].text = t; }
        }

        /// <summary>卓のランプをまとめて更新（24 個ずつを1つの数にして、マテリアルの _B0〜_B3 に入れる）</summary>
        public void RefreshDesks()
        {
            RefreshUI();
            if (deskMats == null || btnAct == null || vi == null || vi.Length != NI || vf == null || vf.Length != NF) return;
            int nd = deskMats.Length;
            float[] w = new float[nd * 16];
            int n = btnAct.Length;
            for (int i = 0; i < n; i++)
            {
                bool on;
                if (btnKind[i] == 0) on = main != null && main.IsOn(btnAct[i], btnVal[i]);
                else on = IsOn(btnAct[i], btnVal[i]);
                if (!on) continue;
                int s = btnSlot[i];
                int wi = btnDesk[i] * 16 + s / 24;
                w[wi] += Mathf.Pow(2f, s % 24);
            }
            // 数字の表示（編集中のデッキの「数」「ズーム」）
            int ed = vi[I_EDIT];
            Vector4 n0 = new Vector4(vi[I_CNT + ed], Mathf.Round(vf[F_ZOOM + ed] * 100f), 0f, 0f);
            for (int d = 0; d < nd; d++)
            {
                Material m = deskMats[d];
                if (m == null) continue;
                m.SetVector("_N0", n0);
                int b = d * 16;
                m.SetVector("_B0", new Vector4(w[b], w[b + 1], w[b + 2], w[b + 3]));
                m.SetVector("_B1", new Vector4(w[b + 4], w[b + 5], w[b + 6], w[b + 7]));
                m.SetVector("_B2", new Vector4(w[b + 8], w[b + 9], w[b + 10], w[b + 11]));
                m.SetVector("_B3", new Vector4(w[b + 12], w[b + 13], w[b + 14], w[b + 15]));
            }
        }
    }
}
#endif
