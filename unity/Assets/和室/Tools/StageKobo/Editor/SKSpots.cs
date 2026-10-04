using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// 演者を照らすスポットライト（フォロースポット）。ステージの照明はほとんどが「見た目だけ」（加算のビーム）なので、
    /// ステージに立ったアバターは照らされず暗く見える → 本物の Spot Light を客席の上（ステージの手前・高いところ）に 2 灯置く。
    /// ・ふだんはステージに固定（演者ダミーの位置、いなければステージのまん中）を照らす
    /// ・UdonSharp 版：リモコン・照明卓の「自分→スポット1／2」で登録した人を追う（カメラのアップと同じしくみ）。「固定に戻す」で固定へ
    /// ・照明の「暗転・ストロボ」で一緒に消える／点滅する。音（AudioLink）では明るさを変えない
    /// </summary>
    public static class SKSpots
    {
        public const int N = 2;
        public static readonly Color Warm = new Color(1f, 0.96f, 0.9f);

        public static void Build(SKContext ctx)
        {
            if (!ctx.cfg.spots) return;
            var st = J.O(ctx.state, "stage");
            float W = J.F(st, "width", 16f), D = J.F(st, "depth", 8f), H = J.F(st, "height", 1.2f);
            float power = Mathf.Clamp(ctx.cfg.spotPower, 0.1f, 3f);
            // 固定のときに照らす場所（胸の高さ）：演者ダミー（左から）。いなければステージのまん中の左右
            var perf = ctx.performersThree.OrderBy(p => p.x).ToList();
            var aims = new List<Vector3>();
            for (int k = 0; k < N; k++)
            {
                Vector3 a;
                if (perf.Count == 0) a = ctx.centerThree + new Vector3(k == 0 ? -1f : 1f, 0, 0);
                else if (perf.Count == 1) a = perf[0];
                else a = perf[Mathf.RoundToInt((float)k * (perf.Count - 1) / (N - 1))];
                aims.Add(a);
            }
            var holder = new GameObject("FollowSpots（演者を照らすスポット）");
            holder.transform.SetParent(ctx.root.transform, false);
            var beamMat = SKAssets.NewMaterial(SKAssets.ShaderBeam, ctx.dir + "/Materials/Beam_FollowSpot.mat");
            beamMat.SetFloat("_Gain", 0.22f * power);
            beamMat.SetFloat("_Haze", ctx.haze);
            beamMat.SetFloat("_EndFade", 1f);
            foreach (var c in new[] { "_C1", "_C2", "_C3" }) beamMat.SetColor(c, Warm);
            for (int k = 0; k < N; k++)
            {
                // 客席側（+Z）の上、ステージの左右寄りから
                var fromT = new Vector3((k == 0 ? -1f : 1f) * W * 0.2f, H + 8f, D / 2f + 14f);
                Vector3 from = ctx.ToWorld(fromT), aim = ctx.ToWorld(aims[k]);
                float dist = Mathf.Max(2f, Vector3.Distance(from, aim));
                var go = new GameObject("FollowSpot" + (k + 1));
                go.transform.SetParent(holder.transform, true);
                go.transform.position = from;
                go.transform.rotation = Quaternion.LookRotation(aim - from, Vector3.up);
                var L = go.AddComponent<Light>();
                L.type = LightType.Spot;
                L.color = Warm;
                L.intensity = 5f * power;      // Unity（Built-in）の減衰は距離に強いので、届く範囲を距離の 3 倍にして明るさを保つ
                L.range = dist * 3f;
                L.spotAngle = SpotAngle(dist, 1.4f);
                L.shadows = LightShadows.None;
                L.renderMode = LightRenderMode.ForcePixel;   // 頂点ライトにされると、アバターがきれいに照らされない
                L.bounceIntensity = 0f;
                L.lightmapBakeType = LightmapBakeType.Realtime;
                // 見た目のビーム（客席の上から伸びる光の筋）
                var b = new GameObject("Beam");
                b.transform.SetParent(go.transform, false);
                b.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // コーンの +Y をライトの向き（+Z）に
                float len = Mathf.Max(1f, dist - 0.3f), r = Mathf.Tan(L.spotAngle * 0.5f * Mathf.Deg2Rad) * len;
                b.transform.localScale = new Vector3(r, len, r);
                b.AddComponent<MeshFilter>().sharedMesh = ctx.coneMesh;
                var mr = b.AddComponent<MeshRenderer>();
                mr.sharedMaterial = beamMat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                beamMat.SetFloat("_Len", len);
                ctx.spotLights.Add(L);
                ctx.spotAims.Add(aim);
                ctx.spotBeams.Add(mr);
            }
            EditorUtility.SetDirty(beamMat);
            ctx.Log("演者を照らすスポットライト " + N + " 灯（本物のライト）を客席の上に置きました" +
                (ctx.udon ? "。操作パネルの「特効・カメラ」タブの「自分→スポット1／2」で、押した人を追います" : "（ステージに固定）"));
        }

        /// <summary>距離 d のところで半径 r の丸を照らす角度（度）</summary>
        public static float SpotAngle(float d, float r)
        {
            return Mathf.Clamp(2f * Mathf.Atan(r / Mathf.Max(0.5f, d)) * Mathf.Rad2Deg, 4f, 60f);
        }
    }
}
