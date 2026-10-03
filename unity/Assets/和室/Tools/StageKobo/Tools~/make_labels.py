#!/usr/bin/env python3
# すてーじ工房：ステージ裏の卓の「文字のテクスチャ」（格子状の PNG）と対応表（SKLabels.cs）を作る
#   python3 make_labels.py <vjdefs.json> <フォント.ttc>
#   vjdefs.json … ブラウザ版で STAGEKOBO.exportUnity() の vjDefs を書き出したもの（映像の素・シーン・合成の名前）
# 文字の一覧は Editor/SKDeskBuilder.cs・SKRemoteBuilder.cs からも拾う。卓の文字を増やしたら作り直す。
import json, os, re, sys
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PKG = os.path.dirname(HERE)
CW, CH, COLS = 128, 64, 16
W = CW * COLS

defs = json.load(open(sys.argv[1], encoding='utf-8'))
defs = defs.get('vjDefs', defs)
FONT = sys.argv[2] if len(sys.argv) > 2 else '/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc'

desk = open(os.path.join(PKG, 'Editor', 'SKDeskBuilder.cs'), encoding='utf-8').read()
remote = open(os.path.join(PKG, 'Editor', 'SKRemoteBuilder.cs'), encoding='utf-8').read()

def uniq(xs):
    out = []
    for x in xs:
        if x and x not in out: out.append(x)
    return out

state_id = re.compile(r'^[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9]+$')
btn = []
btn += re.findall(r'Bt\("([^"]+)"', desk)
for call in re.findall(r'\bL\(((?:\s*"[^"]*"\s*,?)+)\)', desk):
    btn += [x for x in re.findall(r'"([^"]*)"', call) if not state_id.match(x)]
btn += re.findall(r'\{ "\w+", "([^"]+)" \}', remote)
for k in ('gensShort', 'fxNames', 'colorModeNames', 'mirrors'):
    btn += defs.get(k, [])
btn += [p['name'] for p in defs.get('presets', [])]
mix = defs.get('mix', {})
btn += [r['name'] for r in mix.get('recipes', [])]
for k in ('shapeNames', 'motionNames', 'opNames', 'spaceNames', 'postNames'):
    btn += mix.get(k, [])
btn = uniq([x for x in btn if not state_id.match(x)])

titles = re.findall(r'NewSec\(d, "([^"]+)"', desk)
for a, b in re.findall(r'NewSec\(d, \w+ == 0 \? "([^"]+)" : "([^"]+)"', desk):
    titles += [a, b]
titles = uniq(titles)
names = uniq(re.findall(r'new Desk\("([^"]+)"', desk))
mons = uniq(re.findall(r'NewMon\("([^"]+)"', desk))

def font(sz): return ImageFont.truetype(FONT, sz, index=0)

def text_w(t, sz):
    f = font(sz); b = f.getbbox(t); return b[2] - b[0]

SMALL = 'ァィゥェォャュョッーンぁぃぅぇぉゃゅょっ'
def kind(ch):
    o = ord(ch)
    if 0x3040 <= o <= 0x309f: return 'H'
    if 0x30a0 <= o <= 0x30ff: return 'T'
    if 0x4e00 <= o <= 0x9fff: return 'K'
    return 'O'

def fit_lines(t, maxw, maxh, sizes):
    """1行で入る大きさ → だめなら2行（真ん中あたりで分ける）"""
    for sz in sizes:
        if text_w(t, sz) <= maxw and sz * 1.15 <= maxh: return [t], sz
    best = None
    for i in range(1, len(t)):
        a, b = t[:i], t[i:]
        a = a.rstrip('・ '); b = b.lstrip('・ ')
        if not a or not b: continue
        p, q = kind(t[i - 1]), kind(t[i])
        score = abs(len(a) - len(b))
        if t[i - 1] in '・ ' or t[i] in '・ ': score -= 4                 # 「・」で分ける
        elif p != q and q != 'H': score -= 3                              # 文字の種類が変わる所（ひらがなの前は助詞が多いので避ける）
        elif p == q: score += 2                                           # 単語の途中
        if b[0] in SMALL: score += 6                                      # 行の頭に小さい字・のばす棒
        if best is None or score < best[0]: best = (score, [a, b])
    if best is None: return [t], sizes[-1]
    for sz in range(min(sizes[0], 26), 11, -1):
        if max(text_w(x, sz) for x in best[1]) <= maxw and sz * 2.2 <= maxh: return best[1], sz
    return best[1], 12

entries = []   # (key, span, lines, size)
for d in '0123456789': entries.append(('#' + d, 1, [d], 58))
for t in btn:
    lines, sz = fit_lines(t, CW - 12, CH - 6, list(range(30, 19, -1)))
    entries.append(('B:' + t, 1, lines, sz))
for t in titles:
    span = 1 if text_w(t, 28) <= CW - 10 else 2
    lines, sz = fit_lines(t, span * CW - 12, CH - 6, list(range(30 if span == 1 else 32, 19, -1)))
    entries.append(('T:' + t, span, lines, sz))
for t in names:
    lines, sz = fit_lines(t, 3 * CW - 16, CH - 4, list(range(48, 23, -1)))
    entries.append(('N:' + t, 3, lines, sz))
for t in mons:
    lines, sz = fit_lines(t, 2 * CW - 12, CH - 6, list(range(36, 19, -1)))
    entries.append(('M:' + t, 2, lines, sz))

# 詰める（数字 0〜9 は 1 行目の左から並べる。幅のあるものは行をまたがない）
cells = {}
cur = 0
for key, span, lines, sz in entries:
    if cur % COLS + span > COLS: cur = (cur // COLS + 1) * COLS
    cells[key] = cur
    cur += span
rows = (cur + COLS - 1) // COLS
H = max(CH, rows * CH)   # 2 のべき乗でなくてよい（取り込みで「縮めない」にする）
img = Image.new('L', (W, H), 0)
dr = ImageDraw.Draw(img)
for key, span, lines, sz in entries:
    c = cells[key]
    x0, y0 = (c % COLS) * CW, (c // COLS) * CH
    w = span * CW
    f = font(sz)
    if len(lines) == 1:
        dr.text((x0 + w / 2, y0 + CH / 2), lines[0], font=f, fill=255, anchor='mm')
    else:
        lh = sz * 1.08
        for i, ln in enumerate(lines):
            dr.text((x0 + w / 2, y0 + CH / 2 + (i - 0.5) * lh), ln, font=f, fill=255, anchor='mm')
rgba = Image.merge('RGBA', (Image.new('L', (W, H), 255),) * 3 + (img,))
out_png = os.path.join(PKG, 'Textures', 'SK_Labels.png')
rgba.save(out_png, optimize=True)

def cs(s): return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'
keys = [e[0] for e in entries]
code = '''// 自動生成（Tools~/make_labels.py）。手で書き換えないこと
// ステージ裏の卓の文字：Textures/SK_Labels.png（{W}×{H}・1升 {CW}×{CH}px）の中の場所
// キー：B: ボタン（1升）／T: 見出し（1〜2升）／N: 卓の名前（3升）／M: モニターの名前（2升）／#0〜#9 数字（横に並ぶ）
using System.Collections.Generic;
using UnityEngine;

namespace Washitsu.StageKobo.Editor
{{
    public static class SKLabels
    {{
        public const string AtlasFile = "SK_Labels.png";
        public const int W = {W}, H = {H}, CW = {CW}, CH = {CH}, COLS = {COLS};
        static readonly string[] Keys = {{ {keys} }};
        static readonly int[] Cells = {{ {cells} }};   // 升の番号 + 升の数 × 65536
        static Dictionary<string, int> map;

        static void Init()
        {{
            if (map != null) return;
            map = new Dictionary<string, int>();
            for (int i = 0; i < Keys.Length; i++) map[Keys[i]] = Cells[i];
        }}

        /// <summary>文字の場所（UV：x, y, 幅, 高さ）と升の数</summary>
        public static bool TryGet(string key, out Vector4 rect, out int span)
        {{
            Init();
            rect = Vector4.zero; span = 0;
            if (key == null || !map.TryGetValue(key, out int v)) return false;
            int c = v & 0xffff; span = v >> 16;
            rect = Rect(c, span);
            return true;
        }}

        public static int Span(string key, int def) {{ return TryGet(key, out _, out int s) ? s : def; }}

        static Vector4 Rect(int c, int span)
        {{
            int cx = c % COLS, cy = c / COLS;
            return new Vector4((float)(cx * CW) / W, 1f - (float)((cy + 1) * CH) / H, (float)(span * CW) / W, (float)CH / H);
        }}

        /// <summary>数字「0」の升（「1」〜「9」はその右に並ぶ）</summary>
        public static Vector4 DigitRect() {{ Init(); return Rect(map["#0"] & 0xffff, 1); }}
    }}
}}
'''.format(W=W, H=H, CW=CW, CH=CH, COLS=COLS, keys=', '.join(cs(k) for k in keys), cells=', '.join(str(cells[e[0]] + e[1] * 65536) for e in entries))
open(os.path.join(PKG, 'Editor', 'SKLabels.cs'), 'w', encoding='utf-8').write(code)
print('labels', len(entries), 'buttons', len(btn), 'titles', len(titles), 'cells', cur, 'atlas', W, 'x', H)
print('titles:', titles)
print('names:', names, 'mons:', mons)
