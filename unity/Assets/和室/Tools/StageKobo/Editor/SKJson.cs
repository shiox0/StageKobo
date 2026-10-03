using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Washitsu.StageKobo.Editor
{
    /// <summary>
    /// 小さな JSON パーサー。外部パッケージに頼らないために自前で持つ。
    /// object は Dictionary&lt;string, object&gt; / List&lt;object&gt; / double / string / bool / null のどれか。
    /// </summary>
    public static class SKJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new FormatException("JSON が空です");
            int i = 0;
            var v = ParseValue(json, ref i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON が途中で終わっています");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string k = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("':' がありません（位置 " + i + "）");
                i++;
                d[k] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("オブジェクトが閉じていません");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("',' か '}' がありません（位置 " + i + "）");
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("配列が閉じていません");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException("',' か ']' がありません（位置 " + i + "）");
            }
        }

        static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("文字列が必要です（位置 " + i + "）");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("文字列が閉じていません");
        }

        static object ParseNumber(string s, ref int i)
        {
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == st) throw new FormatException("読めない文字があります（位置 " + i + "）");
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>JSON の値を安全に読むための小道具</summary>
    public static class J
    {
        public static object Get(object o, params string[] path)
        {
            foreach (var k in path)
            {
                var d = o as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(k, out o)) return null;
            }
            return o;
        }

        public static Dictionary<string, object> O(object o, params string[] path)
        {
            return Get(o, path) as Dictionary<string, object>;
        }

        public static List<object> L(object o, params string[] path)
        {
            return Get(o, path) as List<object> ?? new List<object>();
        }

        public static string S(object o, string key, string def = "")
        {
            var v = Get(o, key);
            if (v is string str) return str;
            if (v is double d) return d.ToString(CultureInfo.InvariantCulture);
            if (v is bool b) return b ? "true" : "false";
            return def;
        }

        public static float F(object o, string key, float def = 0f)
        {
            return Num(Get(o, key), def);
        }

        public static float Num(object v, float def = 0f)
        {
            if (v is double d) return (float)d;
            if (v is bool b) return b ? 1f : 0f;
            if (v is string s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
            return def;
        }

        /// <summary>倍精度のまま読む（位置から位相を作るときなど、float だと結果が変わってしまう計算用）</summary>
        public static double D(object v, double def = 0)
        {
            if (v is double d) return d;
            if (v is bool b) return b ? 1 : 0;
            if (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
            return def;
        }

        public static int I(object o, string key, int def = 0)
        {
            var v = Get(o, key);
            return v == null ? def : Mathf.RoundToInt(Num(v, def));
        }

        public static bool B(object o, string key, bool def = false)
        {
            var v = Get(o, key);
            if (v is bool b) return b;
            if (v is double d) return d != 0;
            return def;
        }

        public static Vector3 V3(object list)
        {
            var l = list as List<object>;
            if (l == null || l.Count < 3) return Vector3.zero;
            return new Vector3(Num(l[0]), Num(l[1]), Num(l[2]));
        }

        public static Color Col(string hex, Color def)
        {
            if (string.IsNullOrEmpty(hex)) return def;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : def;
        }
    }
}
