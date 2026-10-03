using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>アセット（フォルダ・メッシュ・マテリアル・テクスチャ）を作る小道具</summary>
    public static class SKAssets
    {
        public const string ShaderBeam = "StageKobo/Beam";
        public const string ShaderGlow = "StageKobo/Glow";
        public const string ShaderLED = "StageKobo/LEDDot";
        public const string ShaderParticle = "StageKobo/Particle";
        public const string ShaderWash = "StageKobo/Wash";
        public const string ShaderPenlight = "StageKobo/Penlight";
        public const string ShaderCrowd = "StageKobo/Crowd";
        public const string ShaderVJDeck = "StageKobo/VJDeck";
        public const string ShaderVJFx = "StageKobo/VJFx";
        public const string ShaderPanel = "StageKobo/Panel";
        public const string ShaderScreen = "StageKobo/Screen";

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = slash > 0 ? path.Substring(0, slash) : "Assets";
            string name = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        /// <summary>同じパスに既にあれば作り直して保存する</summary>
        public static T Save<T>(T obj, string path) where T : UnityEngine.Object
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        public static Shader FindShader(string name)
        {
            var sh = Shader.Find(name);
            if (sh == null) throw new Exception("シェーダー「" + name + "」が見つかりません。パッケージの Shaders フォルダが取り込まれているか確認してください");
            return sh;
        }

        public static Material NewMaterial(string shader, string path)
        {
            var m = new Material(FindShader(shader)) { name = Path.GetFileNameWithoutExtension(path) };
            return Save(m, path);
        }

        public static string FullPath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        }

        /// <summary>data:image/...;base64,... をファイルに書き出してテクスチャとして読み込む</summary>
        public static Texture2D SaveDataUrlTexture(string dataUrl, string assetPathNoExt)
        {
            if (string.IsNullOrEmpty(dataUrl)) return null;
            int comma = dataUrl.IndexOf(',');
            if (comma < 0) return null;
            string head = dataUrl.Substring(0, comma);
            string ext = head.Contains("jpeg") || head.Contains("jpg") ? ".jpg" : head.Contains("webp") ? ".webp" : ".png";
            if (ext == ".webp") { Debug.LogWarning("[すてーじ工房] WebP 画像は Unity で読めないため飛ばしました"); return null; }
            byte[] bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
            string path = assetPathNoExt + ext;
            File.WriteAllBytes(FullPath(path), bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.sRGBTexture = true;
                imp.npotScale = TextureImporterNPOTScale.None;   // 2 のべき乗に伸ばさない（縦横比をそのまま使うため。width / height で切り抜きを計算する）
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>ビーム用の円錐（長さ1・先端半径1・+Y方向。uv.y = 根元0→先端1）</summary>
        public static Mesh BuildCone(string name, int seg, float r0, float r1)
        {
            var m = new Mesh { name = name };
            var v = new Vector3[(seg + 1) * 2];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            float slope = r1 - r0;
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2, c = Mathf.Cos(a), s = Mathf.Sin(a);
                Vector3 nn = new Vector3(c, -slope, s).normalized;
                v[i * 2] = new Vector3(c * r0, 0, s * r0); uv[i * 2] = new Vector2((float)i / seg, 0); n[i * 2] = nn;
                v[i * 2 + 1] = new Vector3(c * r1, 1, s * r1); uv[i * 2 + 1] = new Vector2((float)i / seg, 1); n[i * 2 + 1] = nn;
            }
            var tri = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3, k = i * 6;
                tri[k] = a; tri[k + 1] = b; tri[k + 2] = c; tri[k + 3] = c; tri[k + 4] = b; tri[k + 5] = d;
            }
            m.vertices = v; m.normals = n; m.uv = uv; m.triangles = tri;
            m.RecalculateBounds();
            return m;
        }
    }
}
