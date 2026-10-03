using System;
using UnityEngine;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>ブラウザ版の「シーン（演出の組み合わせ）」1つ分</summary>
    public class SKCue
    {
        public string name = "", pattern = "still", colorMode = "single", dim = "on", laser = "off", penlight = "cue", fx = "";
        public float speed = 1f, amp = 20f, tilt = 20f, spread = 20f, beam = 1f;
        public bool sym = true, wash = true;
        public string c1 = "#ffffff", c2 = "#ffffff", c3 = "#ffffff";

        public static SKCue From(object o)
        {
            var c = new SKCue();
            if (o == null) return c;
            c.name = J.S(o, "name", c.name);
            c.pattern = J.S(o, "pattern", c.pattern);
            c.colorMode = J.S(o, "colorMode", c.colorMode);
            c.dim = J.S(o, "dim", c.dim);
            c.laser = J.S(o, "laser", c.laser);
            c.penlight = J.S(o, "penlight", c.penlight);
            c.fx = J.S(o, "fx", c.fx);
            c.speed = J.F(o, "speed", c.speed);
            c.amp = J.F(o, "amp", c.amp);
            c.tilt = J.F(o, "tilt", c.tilt);
            c.spread = J.F(o, "spread", c.spread);
            c.beam = J.F(o, "beam", c.beam);
            c.sym = J.B(o, "sym", c.sym);
            c.wash = J.B(o, "wash", c.wash);
            c.c1 = J.S(o, "c1", c.c1);
            c.c2 = J.S(o, "c2", c.c2);
            c.c3 = J.S(o, "c3", c.c3);
            return c;
        }

        public SKCue Clone() { return (SKCue)MemberwiseClone(); }
    }

    /// <summary>
    /// ブラウザ版（60_lights.js / 70_cams.js）の演出の式をそのまま移植したもの。
    /// 角度は度、座標は three.js 基準（Y上・+Z=客席）。Unity 座標への変換は呼び出し側で行う。
    /// </summary>
    public static class SKMath
    {
        public const double TAU = Math.PI * 2;
        public static readonly string[] Patterns = { "still", "sweep", "wave", "fan", "circle", "cross", "updown", "random", "audience", "center" };
        public static readonly string[] ColorModes = { "single", "alt", "three", "split", "rainbow", "beat", "chase" };
        public static readonly string[] DimModes = { "on", "pulse", "wave", "chase", "alt", "strobe", "random" };
        public static readonly string[] LaserModes = { "off", "fan", "sweep", "cross", "cone" };
        public static readonly string[] CamModes = { "auto", "orbit", "dolly", "crane", "truck", "closeup", "audience", "top", "fixed" };
        public static readonly string[] AutoShots = { "orbit", "closeup", "crane", "truck", "dolly", "audience", "top" };

        public static double Fract(double x) { return x - Math.Floor(x); }
        public static double Hash1(double n) { return Fract(Math.Sin(n * 127.1 + 311.7) * 43758.5453); }
        static double Smooth(double x) { return x * x * (3 - 2 * x); }
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double Wrap(double f, int m) { return m > 0 ? ((f % m) + m) % m : f; }

        /// <summary>動きパターン → pan / tilt（度）。randomWrap はループさせるための乱数の周期</summary>
        public static void Pattern(SKCue c, double b, int i, int n, double xn, int randomWrap, out double pan, out double tilt)
        {
            double side = xn < 0 ? -1 : 1;
            double sy = c.sym ? side : 1;
            switch (c.pattern)
            {
                case "sweep":
                    pan = xn * c.spread * 0.3 + sy * c.amp * Math.Sin(TAU * b * c.speed / 4); tilt = c.tilt; break;
                case "wave":
                {
                    double ph = TAU * (b * c.speed / 4 - (xn + 1) * 0.5);
                    pan = xn * c.spread * 0.5 + c.amp * 0.6 * Math.Sin(ph) * sy; tilt = c.tilt + c.amp * 0.5 * Math.Sin(ph); break;
                }
                case "fan":
                {
                    double o = 0.5 + 0.5 * Math.Sin(TAU * b * c.speed / 4);
                    pan = xn * c.spread * o * 1.5; tilt = c.tilt + 10 * o; break;
                }
                case "circle":
                {
                    double ph = TAU * (b * c.speed / 4) + (c.sym ? 0 : i * 0.6);
                    pan = xn * c.spread * 0.4 + c.amp * Math.Cos(ph) * sy; tilt = c.tilt + c.amp * 0.6 * Math.Sin(ph); break;
                }
                case "cross":
                    pan = (i % 2 == 1 ? 1 : -1) * c.amp * Math.Sin(TAU * b * c.speed / 4) - xn * c.spread * 0.6; tilt = c.tilt; break;
                case "updown":
                    pan = xn * c.spread; tilt = c.tilt + c.amp * (0.5 + 0.5 * Math.Sin(TAU * (b * c.speed / 4 - (double)i / Math.Max(1, n) * 0.5))); break;
                case "random":
                {
                    double k = b * c.speed * 0.5, f = Math.Floor(k), u = Smooth(k - f);
                    double f0 = Wrap(f, randomWrap), f1 = Wrap(f + 1, randomWrap);
                    pan = Lerp(Hash1(i * 13.1 + f0 * 7.7) * 2 - 1, Hash1(i * 13.1 + f1 * 7.7) * 2 - 1, u) * c.amp * 1.5;
                    tilt = c.tilt + Lerp(Hash1(i * 3.3 + f0 * 5.1), Hash1(i * 3.3 + f1 * 5.1), u) * c.amp; break;
                }
                case "audience":
                    pan = xn * c.spread * 0.5 + c.amp * Math.Sin(TAU * (b * c.speed / 8 + i * 0.13)); tilt = c.tilt + 6 * Math.Sin(TAU * b * c.speed / 4 + i); break;
                case "center":
                    pan = xn * 10; tilt = c.tilt; break;
                default:
                    pan = xn * c.spread; tilt = c.tilt; break;
            }
        }

        /// <summary>パターン1周期の拍数（0 = 動かない）</summary>
        public static double PatternPeriodBeats(SKCue c, out int randomWrap)
        {
            randomWrap = 0;
            double s = Math.Max(0.05, c.speed);
            switch (c.pattern)
            {
                case "still": case "center": return 0;
                case "audience": return 8 / s;
                case "random": randomWrap = 16; return 32 / s;
                default: return 4 / s;
            }
        }

        /// <summary>three.js の sphDir：ワールド方向（three 座標）。床置きは真上、吊りは真下が基準</summary>
        public static Vector3 SphDir(double panDeg, double tiltDeg, bool hang)
        {
            double p = panDeg * Math.PI / 180, t = tiltDeg * Math.PI / 180;
            return new Vector3((float)(Math.Sin(t) * Math.Sin(p)), (float)((hang ? -1 : 1) * Math.Cos(t)), (float)(Math.Sin(t) * Math.Cos(p)));
        }

        /// <summary>色の付け方 → 色スロット（0,1,2 = 色1〜3、3 = 虹）と虹の色相（frac はシェーダー側）</summary>
        public static void ColorSlot(string mode, double b, int i, int n, double xn, out float slot, out float hue)
        {
            hue = 0f;
            n = Math.Max(1, n);
            switch (mode)
            {
                case "alt": slot = i % 2; break;
                case "three": slot = i % 3; break;
                case "split": slot = xn < 0 ? 0 : 1; break;
                case "rainbow": slot = 3; hue = (float)((double)i / n * 0.8 + b * 0.06); break;
                case "beat": slot = (float)(((long)Math.Floor(b)) % 3); break;
                case "chase": slot = ((long)Math.Floor(b * 2)) % n == i ? 1 : 0; break;
                default: slot = 0; break;
            }
        }

        public static double ColorPeriodBeats(string mode, int n)
        {
            switch (mode)
            {
                case "rainbow": return 1 / 0.06;
                case "beat": return 3;
                case "chase": return Math.Max(1, n) / 2.0;
                default: return 0;
            }
        }

        /// <summary>明るさの変化（0〜1）</summary>
        public static float Dim(string mode, double b, int i, int n)
        {
            n = Math.Max(1, n);
            switch (mode)
            {
                case "pulse": return (float)(0.3 + 0.7 * Math.Exp(-Fract(b) * 3.5));
                case "wave": return (float)(0.2 + 0.8 * (0.5 + 0.5 * Math.Sin(TAU * (b / 4 - (double)i / n))));
                case "chase": return ((long)Math.Floor(b * 2)) % n == i ? 1f : 0.06f;
                case "alt": return ((long)Math.Floor(b)) % 2 == i % 2 ? 1f : 0.08f;
                case "strobe": return Fract(b * 4) < 0.3 ? 1f : 0f;
                case "random": return Hash1(i * 7.1 + Wrap(Math.Floor(b * 2), 16) * 3.3) > 0.45 ? 1f : 0.05f;
                default: return 1f;
            }
        }

        public static double DimPeriodBeats(string mode, int n)
        {
            switch (mode)
            {
                case "pulse": return 1;
                case "wave": return 4;
                case "chase": return Math.Max(1, n) / 2.0;
                case "alt": return 2;
                case "strobe": return 0.25;
                case "random": return 8;
                default: return 0;
            }
        }

        public static bool DimStepped(string mode) { return mode == "chase" || mode == "alt" || mode == "strobe" || mode == "random"; }

        /// <summary>レーザー1本の向き（three のユニット内ローカル）。unitIndex はレーザー機材の通し番号</summary>
        public static Vector3 LaserDir(string mode, double b, int unitIndex, int j, int count, float spreadDeg, float tiltDeg)
        {
            double k = count > 1 ? (double)j / (count - 1) - 0.5 : 0;
            double sp = spreadDeg * Math.PI / 180;
            double yaw = 0, pitch = tiltDeg * Math.PI / 180;
            switch (mode)
            {
                case "fan": yaw = k * sp * (0.55 + 0.45 * Math.Sin(b * Math.PI / 4)); pitch += 0.12 * Math.Sin(b * Math.PI / 2); break;
                case "sweep": yaw = k * sp * 0.15 + Math.Sin(b * Math.PI / 4) * sp * 0.45; break;
                case "cross": yaw = k * sp * 0.4 + (unitIndex % 2 == 1 ? 1 : -1) * Math.Sin(b * Math.PI / 4) * sp * 0.3; pitch += unitIndex % 2 == 1 ? 0.1 : -0.05; break;
                case "cone": { double a = (double)j / count * TAU + b * Math.PI / 2; yaw = Math.Cos(a) * sp * 0.2; pitch += Math.Sin(a) * sp * 0.2; break; }
            }
            return new Vector3((float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(Math.Cos(yaw) * Math.Cos(pitch)));
        }

        public static double LaserPeriodBeats(string mode) { return mode == "cone" ? 4 : mode == "off" ? 0 : 8; }

        /// <summary>ステージカメラの1ショットの周期（秒）。0 = 動かない</summary>
        public static double ShotPeriod(string mode, float sp)
        {
            sp = Mathf.Max(0.05f, sp);
            switch (mode)
            {
                case "orbit": return TAU / (0.15 * sp);
                case "dolly": return TAU / (0.25 * sp);
                case "crane": return TAU / (0.1 * sp);
                case "truck": return TAU / (0.2 * sp);
                case "closeup": return TAU / (0.1 * sp);
                case "audience": return TAU / (0.1 * sp);
                case "top": return TAU / (0.1 * sp);
                default: return 0;
            }
        }

        /// <summary>
        /// カメラワーク（70_cams.js の shotPose）。loopT &gt; 0 のときは細かい揺れの周波数を周期に合わせて丸め、ループの継ぎ目を無くす
        /// </summary>
        public static void ShotPose(string mode, double t, Vector3 f, float sp, float d, float h, float depth, double loopT, out Vector3 pos, out Vector3 look)
        {
            Func<double, double> q = w => loopT > 0 ? Math.Max(1, Math.Round(w * loopT / TAU)) * TAU / loopT : w;
            look = f;
            switch (mode)
            {
                case "orbit":
                {
                    double a = Math.Sin(t * q(0.15 * sp)) * 1.1;
                    pos = new Vector3(f.x + (float)Math.Sin(a) * d, f.y + h * 0.4f + 0.3f, f.z + (float)Math.Cos(a) * d); break;
                }
                case "dolly":
                {
                    double k = 0.5 + 0.5 * Math.Sin(t * q(0.25 * sp));
                    pos = new Vector3(f.x, f.y + 0.25f, f.z + (float)Lerp(d * 0.4, d * 1.4, k)); break;
                }
                case "crane":
                {
                    double k = 0.5 + 0.5 * Math.Sin(t * q(0.2 * sp));
                    pos = new Vector3(f.x + (float)Math.Sin(t * q(0.1 * sp)) * 2, f.y + (float)Lerp(-0.6, 6, k), f.z + d * 1.1f);
                    look = new Vector3(f.x, f.y + (float)Lerp(0.3, -0.5, k), f.z); break;
                }
                case "truck":
                {
                    float s = (float)Math.Sin(t * q(0.2 * sp));
                    pos = new Vector3(f.x + s * d * 0.8f, f.y + 0.2f, f.z + d * 0.8f); look = new Vector3(f.x + s, f.y, f.z); break;
                }
                case "closeup":
                    pos = new Vector3(f.x + (float)(Math.Sin(t * q(0.3 * sp)) * 0.6 + Math.Sin(t * q(0.7)) * 0.05), f.y + 0.15f + (float)Math.Sin(t * q(1.1)) * 0.03f, f.z + d * 0.45f);
                    look = new Vector3(f.x, f.y + 0.1f, f.z); break;
                case "audience":
                    pos = new Vector3((float)Math.Sin(t * q(0.1 * sp)) * 3, 2.2f + (float)Math.Sin(t * q(0.9)) * 0.05f, depth / 2 + 12); break;
                case "top":
                {
                    double a = t * q(0.1 * sp);
                    pos = new Vector3(f.x + (float)Math.Cos(a) * 1.5f, f.y + d * 1.5f + 4, f.z + (float)Math.Sin(a) * 1.5f); break;
                }
                default:
                    pos = new Vector3(f.x, f.y + 0.4f, f.z + d); break;
            }
        }

        /// <summary>
        /// アップ（顔）：追う人の顔の正面（客席側 +Z）から、胸から上が入る大きさで撮る（70_cams.js の faceShot と同じ式）。
        /// hd = 顔のまん中（three の座標）、d = 顔からの距離（faceDist）
        /// </summary>
        public static void FaceShot(double t, Vector3 hd, float sp, float d, double loopT, out Vector3 pos, out Vector3 look)
        {
            Func<double, double> q = w => loopT > 0 ? Math.Max(1, Math.Round(w * loopT / TAU)) * TAU / loopT : w;
            pos = new Vector3(hd.x + (float)(Math.Sin(t * q(0.3 * sp)) * d * 0.18 + Math.Sin(t * q(0.7)) * 0.02), hd.y - 0.03f + (float)Math.Sin(t * q(1.1)) * 0.015f, hd.z + d);
            look = new Vector3(hd.x, hd.y - 0.08f, hd.z);
        }

        /// <summary>顔のアップの距離と画角（ブラウザ版 faceDist / faceFov と同じ式）</summary>
        public static float FaceDist(float dist) { return Mathf.Clamp(dist * 0.35f, 1.2f, 6f); }
        public static float FaceFov(float fov, float dist) { return 2f * Mathf.Atan(0.375f * (fov / 30f) / FaceDist(dist)) * Mathf.Rad2Deg; }

        /// <summary>three.js の HSL（sRGB）→ Color</summary>
        public static Color Hsl(float h, float s, float l)
        {
            h = h - Mathf.Floor(h);
            float q2 = l < 0.5f ? l * (1 + s) : l + s - l * s, p2 = 2 * l - q2;
            return new Color(Hue(p2, q2, h + 1f / 3), Hue(p2, q2, h), Hue(p2, q2, h - 1f / 3));
        }

        static float Hue(float p, float q, float t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1f / 6) return p + (q - p) * 6 * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3) return p + (q - p) * 6 * (2f / 3 - t);
            return p;
        }
    }
}
