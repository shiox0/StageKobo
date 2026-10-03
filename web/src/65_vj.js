/* =====================================================================
   VJ エンジン：BPM に合わせて「ドゥンドゥン」動く映像を作る
   - デッキ A / B：それぞれ「映像の素」（幾何学の数式・画像・文字）を1つ流す
   - クロスフェーダーで A↔B を混ぜて、エフェクト（万華鏡・ミラー・RGBずれ・グリッチ・ズーム・フラッシュ…）と色を掛ける
   - 出力先：ステージの LED モニター／背景の LED パネル／全画面の VJ 出力モード／別ウィンドウ
   - 拍は照明と同じ LT.beat（BPM・TAP はリモコンと共通）
   ===================================================================== */
// 映像の素（id はシェーダーの uGen と同じ並び）
const VJ_GENS = [
  ['tunnel', '多角形トンネル', 'トンネル'], ['rings', '同心円パルス', '同心円'], ['rays', 'サンバースト', '放射'], ['synth', 'シンセグリッド', 'シンセ'],
  ['polys', '回転ポリゴン', 'ポリゴン'], ['lissa', 'リサージュ曲線', 'リサージュ'], ['spiral', 'スパイラル', 'うず'], ['plasma', 'プラズマ', 'プラズマ'],
  ['checker', 'パタパタ市松', '市松'], ['halftone', 'ドットの波', 'ドット波'], ['hex', 'ハニカム', 'ハニカム'], ['stripes', 'ストライプ', 'ストライプ'],
  ['warp', 'ワープ（星が飛ぶ）', 'ワープ'], ['hearts', 'ハートシャワー', 'ハート'], ['eq', 'イコライザー', 'EQ'], ['crystal', 'クリスタル（セル）', 'セル'],
  ['p_triangles', '三角コンフェッティ', '三角'], ['p_stripes', 'パステル虹', 'パステル'], ['p_galaxy', '銀河・星雲', '銀河'], ['p_tunnel', '三角トンネル', '三角筒'],
  ['p_blobs', 'ゆめかわ流体', 'ゆめかわ'], ['p_dots', 'ポップドット', 'ポップ'], ['p_sparkle', 'キラキラ星', 'キラキラ'],
  ['image', '画像（画像バンクから）', '画像'], ['text', '文字', '文字'], ['black', '黒（何も出さない）', '黒'],
];
const VJ_GEN_ID = Object.fromEntries(VJ_GENS.map((g, i) => [g[0], i]));

// エフェクト（トグル）
const VJ_FX = [
  ['zoom', 'ズーム', '拍でドンと寄る'], ['flash', 'フラッシュ', '拍で白く光る'], ['rgb', 'RGBずれ', '色がずれてにじむ'], ['glitch', 'グリッチ', '映像が横にずれて乱れる'],
  ['kaleido', '万華鏡', '放射状に折り返す'], ['shake', '揺れ', '拍で画面が揺れる'], ['rotate', '回転', 'ゆっくり回る'], ['feedback', '残像', '前のコマが尾を引く（トンネル状）'],
  ['pixel', 'モザイク', '拍でモザイク'], ['invert', '反転', '拍ごとに白黒反転'], ['posterize', 'ポスター', '色数を減らす'], ['edge', 'ネオン輪郭', '輪郭だけを光らせる'],
  ['scan', '走査線', 'ブラウン管・VHS 風'],
];
const VJ_COLOR_MODES = [['raw', 'そのまま'], ['grad', 'グラデ'], ['duo', '2色'], ['mono', '白黒']];
const VJ_MIRRORS = ['なし', '左右', '上下', '4分割'];
// 組み込みの VJ シーン
const VJ_PRESETS = [
  { name: 'ネオントンネル', a: { gen: 'tunnel', count: 6 }, b: { gen: 'rings' }, pal: 2, colorMode: 'raw', fx: { zoom: true, rgb: true } },
  { name: '万華鏡ポップ', a: { gen: 'p_triangles' }, b: { gen: 'plasma' }, pal: 1, colorMode: 'raw', fx: { kaleido: true, zoom: true, flash: true }, kaleN: 6 },
  { name: 'シンセウェーブ', a: { gen: 'synth' }, b: { gen: 'warp' }, pal: 4, colorMode: 'raw', fx: { scan: true, rgb: true, zoom: true } },
  { name: 'モノクロ・ストロボ', a: { gen: 'checker', count: 4 }, b: { gen: 'polys', count: 4 }, pal: 0, colorMode: 'mono', fx: { flash: true, invert: true, zoom: true } },
  { name: 'ゆめかわ', a: { gen: 'p_blobs' }, b: { gen: 'hearts' }, pal: 1, colorMode: 'raw', fx: { feedback: true, zoom: true }, mirror: 1 },
  { name: 'ハニカム・グリッチ', a: { gen: 'hex', count: 5 }, b: { gen: 'crystal' }, pal: 5, colorMode: 'raw', fx: { glitch: true, rgb: true, zoom: true } },
  { name: 'ワープ・トリップ', a: { gen: 'warp' }, b: { gen: 'spiral', count: 3 }, pal: 3, colorMode: 'raw', fx: { feedback: true, rotate: true, zoom: true } },
  { name: '画像ドゥンドゥン', a: { gen: 'image' }, b: { gen: 'image', img: 1 }, pal: -1, colorMode: 'raw', fx: { zoom: true, rgb: true, glitch: true, flash: true }, imgRate: 1 },
];

/* ---------- シェーダー ---------- */
const VJ_GEN_FS = /* glsl */`
uniform int uGen; uniform float uT, uBt, uPulse, uCnt, uZoom, uAsp;
uniform vec3 uC1, uC2, uC3;
uniform sampler2D uImg; uniform float uImgAsp, uImgOk, uImgK;
uniform sampler2D uTxt; uniform float uTxtAsp;
varying vec2 vUv;
${PATTERN_GLSL}
#define PI 3.14159265
#define TAU 6.2831853
float sdPoly(vec2 p, float n, float r){ float a = atan(p.x, p.y) + PI; float b = TAU / n; return cos(floor(0.5 + a / b) * b - a) * length(p) - r; }
float sdSeg(vec2 p, vec2 a, vec2 b){ vec2 pa = p - a, ba = b - a; float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0); return length(pa - ba * h); }
float dot2(vec2 v){ return dot(v, v); }
float sdHeart(vec2 p){ p.x = abs(p.x); if (p.y + p.x > 1.0) return sqrt(dot2(p - vec2(0.25, 0.75))) - 0.35355; return sqrt(min(dot2(p - vec2(0.0, 1.0)), dot2(p - 0.5 * max(p.x + p.y, 0.0)))) * sign(p.x - p.y); }
vec3 pal3(float k, vec3 a, vec3 b, vec3 c){ float m = mod(k, 3.0); return m < 1.0 ? a : (m < 2.0 ? b : c); }
const vec2 HS = vec2(1.0, 1.7320508);
vec4 hexCell(vec2 p){ vec4 hc = floor(vec4(p, p - vec2(0.5, 1.0)) / HS.xyxy) + 0.5; vec4 h = vec4(p - hc.xy * HS, p - (hc.zw + 0.5) * HS); return dot(h.xy, h.xy) < dot(h.zw, h.zw) ? vec4(h.xy, hc.xy) : vec4(h.zw, hc.zw + 0.5); }
float hexD(vec2 p){ p = abs(p); return max(dot(p, HS * 0.5), p.x); }
vec3 gen(vec2 uv){
  vec2 p = (uv - 0.5) * vec2(uAsp, 1.0) / uZoom;
  float bt = uBt, tt = uT, pu = uPulse, n = uCnt; vec3 c1 = uC1, c2 = uC2, c3 = uC3, col = vec3(0.0);
  float r = length(p), a = atan(p.y, p.x);
  if (uGen == 0) {            // 多角形トンネル：奥から手前へ流れる多角形の輪
    float ns = max(3.0, floor(n)); vec2 q = rot2(bt * 0.12) * p;
    float aa = atan(q.x, q.y) + PI, b = TAU / ns; float d = cos(floor(0.5 + aa / b) * b - aa) * length(q);
    float z = 0.5 / max(d, 0.002) + bt * 0.5; float k = fract(z);
    float band = smoothstep(0.0, 0.05, k) * smoothstep(0.34, 0.26, k);
    col = mix(c1, c2, step(1.0, mod(floor(z), 2.0))) * band * (0.5 + 0.8 * pu) + c3 * exp(-abs(k - 0.62) * 40.0) * 0.5;
    col *= smoothstep(0.0, 0.1, d) * (0.6 + 0.4 * smoothstep(0.0, 0.5, d));
  } else if (uGen == 1) {     // 同心円パルス：拍ごとに外へ広がる輪
    float k = r * max(1.0, n) * 1.6 - bt; float f = fract(k);
    col = pal3(floor(k), c1, c2, c3) * smoothstep(0.0, 0.08, f) * smoothstep(0.42, 0.3, f) * (0.5 + 0.8 * pu * exp(-r * 1.5));
    col += c3 * exp(-r * 9.0) * (0.4 + pu);
  } else if (uGen == 2) {     // サンバースト：回る放射線
    float m = max(3.0, floor(n)) * 2.0; float s = step(0.5, fract((a + bt * 0.2) / TAU * m));
    col = mix(c1, c2, s) * (0.3 + 0.7 * smoothstep(0.95, 0.0, r)) * (0.6 + 0.6 * pu);
    col += c3 * smoothstep(0.12 + 0.06 * pu, 0.0, r);
  } else if (uGen == 3) {     // シンセグリッド：夕日と、流れる遠近の格子
    col = mix(c2 * 0.12, c1 * 0.22, smoothstep(-0.1, 0.5, p.y));
    vec2 sp = p - vec2(0.0, 0.1); float sun = smoothstep(0.24, 0.235, length(sp));
    float cut = step(0.0, sp.y) + step(0.0, sin(sp.y * 90.0 - bt * 3.0));
    col = mix(col, mix(c3, c2, smoothstep(0.25, -0.1, sp.y)) * 1.2, sun * clamp(cut, 0.0, 1.0));
    if (p.y < -0.02) {
      float z = 0.12 / (-p.y); vec2 g = vec2(p.x * z * 3.0, z - bt * 0.5);
      vec2 gd = abs(fract(g) - 0.5) / fwidth(g); float line = 1.0 - min(min(gd.x, gd.y), 1.0);
      col = mix(c2 * 0.04, c1 * 1.3, line * smoothstep(7.0, 0.8, z)) * (0.7 + 0.5 * pu);
    }
  } else if (uGen == 4) {     // 回転ポリゴン：入れ子の多角形が交互に回る
    float ns = max(3.0, floor(n));
    for (int i = 0; i < 7; i++) { float fi = float(i);
      float rr = (0.05 + fi * 0.065) * (1.0 + 0.18 * pu);
      vec2 q = rot2(bt * 0.25 * (mod(fi, 2.0) < 1.0 ? 1.0 : -1.0) + fi * 0.4) * p;
      float d = abs(sdPoly(q, ns, rr));
      col += pal3(fi, c1, c2, c3) * (smoothstep(0.01, 0.0, d) * 1.2 + exp(-d * 45.0) * 0.25); }
  } else if (uGen == 5) {     // リサージュ曲線
    float A = clamp(floor(n), 1.0, 6.0), B = A + 1.0, best = 1e9; vec2 pv = vec2(0.0);
    if (abs(p.x) < 0.6 && abs(p.y) < 0.56) for (int i = 0; i <= 96; i++) { float s = float(i) / 96.0 * TAU;   // 曲線の外側は計算しない（軽くする）
      vec2 q = vec2(0.44 * sin(A * s + bt * 0.35), 0.4 * sin(B * s));
      if (i > 0) best = min(best, sdSeg(p, pv, q)); pv = q; }
    col = mix(c1, c2, 0.5 + 0.5 * sin(bt * 0.5)) * (smoothstep(0.006, 0.0, best) * 1.3 + exp(-best * 30.0) * (0.3 + 0.5 * pu));
  } else if (uGen == 6) {     // スパイラル
    float arms = max(1.0, floor(n)); float k = a / TAU * arms + log(r + 1e-3) * 1.6 - bt * 0.5; float f = fract(k);
    col = mix(c1, c2, step(1.0, mod(floor(k), 2.0))) * smoothstep(0.0, 0.06, f) * smoothstep(0.55, 0.47, f) * smoothstep(0.0, 0.08, r);
    col += c3 * exp(-r * 10.0) * (0.3 + pu);
  } else if (uGen == 7) {     // プラズマ：重なり合う波
    vec2 q = p * (2.0 + n * 0.4); float s = tt * 0.6 + bt * 0.25;
    float v = sin(q.x * 2.0 + s) + sin(q.y * 2.6 - s * 0.8) + sin((q.x + q.y) * 1.4 + s * 0.6) + sin(length(q) * 3.0 - s * 1.2);
    v = v * 0.25 + 0.5; col = v < 0.5 ? mix(c1, c2, v * 2.0) : mix(c2, c3, v * 2.0 - 1.0); col *= 0.65 + 0.5 * pu;
  } else if (uGen == 8) {     // パタパタ市松：拍ごとに 45° ずつ回る
    float m = max(2.0, floor(n)); float ang = 0.785398 * (floor(bt) + smoothstep(0.0, 0.3, fract(bt)));
    vec2 q = rot2(ang) * p * m; vec2 id = floor(q), f = fract(q);
    float flip = mod(id.x + id.y + floor(bt), 2.0); float e = smoothstep(0.0, 0.05, min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y)));
    col = mix(c1, c2, flip) * (0.5 + 0.5 * e) * (0.65 + 0.55 * pu);
  } else if (uGen == 9) {     // ドットの波（ハーフトーン）
    float m = 6.0 + n * 2.0; vec2 g = p * m; vec2 id = floor(g) + 0.5, f = fract(g) - 0.5;
    float w = 0.5 + 0.5 * sin(length(id) / m * 12.0 - bt * PI);
    float rr = 0.06 + 0.4 * w * (0.75 + 0.25 * pu);
    col = mix(c1, c2, w) * smoothstep(rr, rr - 0.06, length(f));
  } else if (uGen == 10) {    // ハニカム：拍ごとにランダムな六角が光る
    vec4 h = hexCell(p * (3.0 + n)); float e = smoothstep(0.5, 0.43, hexD(h.xy));
    float rr = h21(h.zw + floor(bt) * 7.13); float on = step(0.55, rr);
    col = pal3(floor(rr * 3.0), c1, c2, c3) * e * (on * (0.55 + 0.7 * pu) + 0.06);
    col += c3 * smoothstep(0.47, 0.5, hexD(h.xy)) * 0.25;
  } else if (uGen == 11) {    // ストライプ：斜めに流れる帯
    float m = max(1.0, floor(n)); vec2 q = rot2(0.6) * p; float k = q.x * m * 2.0 + bt; float f = fract(k);
    col = pal3(floor(k), c1, c2, c3) * smoothstep(0.0, 0.04, f) * smoothstep(0.62, 0.56, f) * (0.55 + 0.6 * pu);
  } else if (uGen == 12) {    // ワープ：中心から星が飛んでくる
    for (int L = 0; L < 3; L++) { float fl = float(L);
      float rays = 70.0 + fl * 45.0; float id = floor(a / TAU * rays); float h = h11(id * (fl + 1.37) + fl * 9.1);
      float z = fract(h * 7.0 + bt * (0.15 + 0.2 * h) * max(1.0, n * 0.25));
      float pos = z * z * 0.9, len = 0.015 + 0.2 * z * z * (0.4 + pu);
      float d = abs(r - pos - len * 0.5) - len * 0.5; float aw = abs(fract(a / TAU * rays) - 0.5);
      col += mix(c3, h < 0.5 ? c1 : c2, 0.6) * smoothstep(0.008, 0.0, d) * smoothstep(0.3, 0.0, aw) * z * 1.6; }
    col += c1 * exp(-r * 12.0) * 0.4;
  } else if (uGen == 13) {    // ハートシャワー：ハートが降ってくる
    float m = 3.0 + n * 0.5; vec2 q = p * m + vec2(0.0, bt * 0.5); vec2 id = floor(q), f = fract(q) - 0.5;
    float h = h21(id), sz = 0.22 + 0.16 * h; vec2 o = vec2(h21(id + 3.1) - 0.5, h21(id + 7.7) - 0.5) * 0.3;
    vec2 l = rot2(sin(tt * 2.0 + h * 9.0) * 0.4) * (f - o) / (sz * (1.0 + 0.12 * pu));
    float d = sdHeart(l * 1.2 + vec2(0.0, 0.55));
    float show = step(0.35, h);
    col = pal3(floor(h * 9.0), c1, c2, c3) * (smoothstep(0.03, 0.0, d) + exp(-max(d, 0.0) * 12.0) * 0.25) * show;
    col += mix(c1, c2, uv.y) * 0.06;
  } else if (uGen == 14) {    // イコライザー
    col = vjPattern(5, 0.5 + (uv - 0.5) / uZoom, tt, bt, c1, c2, c3, uAsp);
  } else if (uGen == 15) {    // クリスタル：ボロノイのセルが拍で光る
    vec2 q = p * (3.0 + n * 0.6); vec2 ip = floor(q), fp = fract(q); float d1 = 9.0, d2 = 9.0; vec2 cid = vec2(0.0);
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) { vec2 g = vec2(float(x), float(y));
      vec2 o = vec2(h21(ip + g), h21(ip + g + 5.3)); o = 0.5 + 0.4 * sin(tt * 0.6 + TAU * o);
      float d = length(g + o - fp); if (d < d1) { d2 = d1; d1 = d; cid = ip + g; } else if (d < d2) d2 = d; }
    float rr = h21(cid + floor(bt) * 3.7); float edge = smoothstep(0.06, 0.0, d2 - d1);
    col = pal3(floor(rr * 3.0), c1, c2, c3) * (step(0.6, rr) * (0.45 + 0.7 * pu) + 0.08) + c3 * edge * 0.7;
  } else if (uGen >= 16 && uGen <= 22) {   // これまでの VJ パターン
    int k = uGen == 16 ? 0 : uGen == 17 ? 1 : uGen == 18 ? 2 : uGen == 19 ? 3 : uGen == 20 ? 4 : uGen == 21 ? 6 : 7;
    col = vjPattern(k, 0.5 + (uv - 0.5) / uZoom, tt, bt, c1, c2, c3, uAsp) * (0.8 + 0.35 * pu);
  } else if (uGen == 23) {    // 画像：画面いっぱいに（はみ出す分は切る）、ゆっくり寄る＋拍で少し跳ねる
    if (uImgOk < 0.5) col = mix(c1, c2, uv.y) * 0.08;
    else { vec2 q = uv - 0.5; if (uAsp > uImgAsp) q.y *= uImgAsp / uAsp; else q.x *= uAsp / uImgAsp;
      q /= uZoom * (1.0 + 0.1 * uImgK + 0.05 * pu); col = texture2D(uImg, q + 0.5).rgb; }
  } else if (uGen == 24) {    // 文字
    vec2 q = (uv - 0.5) * vec2(uAsp, 1.0); float hh = min(0.5, 0.92 * uAsp / uTxtAsp) * uZoom * (1.0 + 0.1 * pu);
    vec2 tuv = vec2(q.x / (hh * uTxtAsp), q.y / hh) + 0.5;
    float inb = step(0.0, tuv.x) * step(tuv.x, 1.0) * step(0.0, tuv.y) * step(tuv.y, 1.0);
    vec4 tx = texture2D(uTxt, clamp(tuv, 0.0, 1.0)) * inb;
    col = mix(c1, c2, tuv.y) * tx.a * (0.75 + 0.6 * pu) + mix(c2, c1, uv.y) * 0.05;
  }
  return col;
}
void main(){ gl_FragColor = vec4(max(gen(vUv), vec3(0.0)), 1.0); }`;

const VJ_FX_FS = /* glsl */`
uniform sampler2D uA, uB, uPrev, uTxt;
uniform float uXf, uAsp, uT;
uniform vec2 uShakeV, uRes;
uniform float uZoomP, uRot, uMirror, uKale, uKaleRot, uPix, uGlitch, uGlitchSeed, uRgb, uFb, uEdge, uColMode, uHue, uPost, uInvert, uScan, uFlash, uStrobe, uBright, uBlack;
uniform float uTxtOn, uTxtAsp, uTxtK;
uniform vec3 uC1, uC2, uC3;
varying vec2 vUv;
#define TAU 6.2831853
const vec3 LUM = vec3(0.299, 0.587, 0.114);
float h11(float n){ return fract(sin(n * 127.1) * 43758.5453); }
mat2 rot2(float a){ float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }
vec3 src(vec2 uv){ uv = 1.0 - abs(1.0 - mod(uv, 2.0)); return mix(texture2D(uA, uv).rgb, texture2D(uB, uv).rgb, uXf); }
vec3 hueShift(vec3 c, float a){ const vec3 k = vec3(0.57735); float ca = cos(a); return c * ca + cross(k, c) * sin(a) + k * dot(k, c) * (1.0 - ca); }
void main(){
  vec2 c = (vUv - 0.5) * vec2(uAsp, 1.0);
  c += uShakeV;
  c /= 1.0 + uZoomP;
  c = rot2(uRot) * c;
  if (uMirror > 2.5) c = abs(c); else if (uMirror > 1.5) c.y = abs(c.y); else if (uMirror > 0.5) c.x = abs(c.x);
  if (uKale > 1.5) { float a = atan(c.y, c.x) + uKaleRot, r = length(c), s = TAU / uKale; a = mod(a, s); a = abs(a - s * 0.5); c = vec2(cos(a), sin(a)) * r; }
  if (uPix > 0.001) { float px = mix(0.006, 0.07, uPix); c = (floor(c / px) + 0.5) * px; }
  vec2 uv = c / vec2(uAsp, 1.0) + 0.5;
  if (uGlitch > 0.001) {
    float row = floor(uv.y * 16.0 + h11(uGlitchSeed) * 5.0); float r = h11(row * 1.37 + uGlitchSeed);
    if (r < uGlitch * 0.6) uv.x += (h11(row * 3.1 + uGlitchSeed * 1.7) - 0.5) * 0.35 * uGlitch;
    if (h11(row * 7.7 + uGlitchSeed) < uGlitch * 0.25) uv.y += 0.04 * uGlitch;
  }
  vec2 off = (vUv - 0.5) * uRgb * 0.05 + vec2(uGlitch * 0.015, 0.0);
  vec3 col = vec3(src(uv + off).r, src(uv).g, src(uv - off).b);
  if (uEdge > 0.5) {
    vec2 e = 1.5 / uRes;
    float l = dot(src(uv - vec2(e.x, 0.0)), LUM), rr = dot(src(uv + vec2(e.x, 0.0)), LUM), u = dot(src(uv + vec2(0.0, e.y)), LUM), d = dot(src(uv - vec2(0.0, e.y)), LUM);
    float g = clamp(length(vec2(rr - l, u - d)) * 4.0, 0.0, 1.5);
    col = col * 0.12 + mix(uC1, uC3, clamp(g - 0.5, 0.0, 1.0)) * g * 1.3;
  }
  float lum = dot(col, LUM);
  if (uColMode > 2.5) col = vec3(lum);
  else if (uColMode > 1.5) col = mix(uC1 * 0.1, uC2 * 1.1, smoothstep(0.03, 0.85, lum));
  else if (uColMode > 0.5) { float t = clamp(lum * 1.1, 0.0, 1.0); col = t < 0.33 ? mix(vec3(0.0), uC1, t / 0.33) : (t < 0.66 ? mix(uC1, uC2, (t - 0.33) / 0.33) : mix(uC2, uC3, (t - 0.66) / 0.34)); }
  if (abs(uHue) > 0.0005) col = max(hueShift(col, uHue), 0.0);
  if (uPost > 0.5) col = floor(col * 4.0 + 0.5) / 4.0;
  col *= uBright;
  if (uFb > 0.001) { vec2 fc = rot2(0.012) * (vUv - 0.5) * 0.965 + 0.5; col = max(col, texture2D(uPrev, fc).rgb * 0.9 * uFb); }
  if (uInvert > 0.001) col = mix(col, max(vec3(1.0) - col, 0.0), uInvert);
  if (uScan > 0.5) {
    col *= 0.8 + 0.2 * sin(vUv.y * uRes.y * 1.7);
    col += (h11(floor(vUv.y * uRes.y * 0.5) + floor(uT * 24.0)) - 0.5) * 0.05;
    vec2 v = vUv - 0.5; col *= 1.0 - dot(v, v) * 0.9;
  }
  if (uTxtOn > 0.001) {
    vec2 q = (vUv - 0.5) * vec2(uAsp, 1.0); float hh = min(0.3, 0.9 * uAsp / uTxtAsp) * (1.0 + 0.12 * uTxtK);
    vec2 tuv = vec2(q.x / (hh * uTxtAsp), q.y / hh) + 0.5;
    float inb = step(0.0, tuv.x) * step(tuv.x, 1.0) * step(0.0, tuv.y) * step(tuv.y, 1.0);
    vec4 tx = texture2D(uTxt, clamp(tuv, 0.0, 1.0)) * inb;
    col = mix(col, mix(vec3(1.2), uC3 * 1.3, 0.35) * (0.6 + 0.6 * uTxtK), tx.a * uTxtOn);
  }
  col = mix(col, vec3(1.25), clamp(uFlash, 0.0, 1.0));
  col = mix(col, vec3(1.4), uStrobe);
  col *= 1.0 - uBlack;
  gl_FragColor = vec4(max(col, vec3(0.0)), 1.0);
}`;

/* ---------- 1フレーム分の値（全部のエンジンで共通 → メインとウィンドウで同じ映像） ---------- */
const VJP = {
  t: 0, beat: 0, pulse: 0, deck: [{}, {}], xf: 0, colors: [new THREE.Color(), new THREE.Color(), new THREE.Color()],
  fx: {}, txtTex: null, txtAsp: 4, txtK: 0, txtOn: 0,
};
const VJ_TRIG = { flash: -99, invert: -99, glitch: -99, zoom: -99 };
const VJ_HOLD = { strobe: false };
const VJRT = { fade: null, black: 0, autoIdx: null, needed: false };

/* ---------- エンジン（レンダラーごとに1つ：メイン・別ウィンドウ） ---------- */
class VJEngine {
  constructor(renderer) {
    this.r = renderer; this.w = 0; this.h = 0; this.cur = 0;
    this.cam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1); this.scene = new THREE.Scene();
    this.quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2)); this.quad.frustumCulled = false; this.scene.add(this.quad);
    const deckU = () => ({ uGen: { value: 0 }, uT: { value: 0 }, uBt: { value: 0 }, uPulse: { value: 0 }, uCnt: { value: 6 }, uZoom: { value: 1 }, uAsp: { value: 16 / 9 },
      uC1: { value: new THREE.Color() }, uC2: { value: new THREE.Color() }, uC3: { value: new THREE.Color() },
      uImg: { value: BLACK_TEX }, uImgAsp: { value: 1.6 }, uImgOk: { value: 0 }, uImgK: { value: 0 }, uTxt: { value: BLACK_TEX }, uTxtAsp: { value: 4 } });
    this.deckMat = [0, 1].map(() => new THREE.ShaderMaterial({ uniforms: deckU(), vertexShader: SIMPLE_VS, fragmentShader: VJ_GEN_FS, depthTest: false, depthWrite: false }));
    const fu = { uA: { value: null }, uB: { value: null }, uPrev: { value: null }, uTxt: { value: BLACK_TEX }, uXf: { value: 0 }, uAsp: { value: 16 / 9 }, uT: { value: 0 },
      uShakeV: { value: new THREE.Vector2() }, uRes: { value: new THREE.Vector2(640, 360) } };
    for (const k of ['uZoomP', 'uRot', 'uMirror', 'uKale', 'uKaleRot', 'uPix', 'uGlitch', 'uGlitchSeed', 'uRgb', 'uFb', 'uEdge', 'uColMode', 'uHue', 'uPost', 'uInvert', 'uScan', 'uFlash', 'uStrobe', 'uBright', 'uBlack', 'uTxtOn', 'uTxtAsp', 'uTxtK']) fu[k] = { value: 0 };
    fu.uC1 = { value: new THREE.Color() }; fu.uC2 = { value: new THREE.Color() }; fu.uC3 = { value: new THREE.Color() };
    this.fxMat = new THREE.ShaderMaterial({ uniforms: fu, vertexShader: SIMPLE_VS, fragmentShader: VJ_FX_FS, depthTest: false, depthWrite: false });
    this.blitMat = new THREE.MeshBasicMaterial({ toneMapped: false, depthTest: false, depthWrite: false });
    this.rt = [];
  }
  ensure(w, h) {
    w = Math.max(16, Math.round(w)); h = Math.max(16, Math.round(h));
    if (w === this.w && h === this.h) return;
    this.w = w; this.h = h; for (const t of this.rt) t.dispose();
    // 小数の色（HalfFloat）で持つと残像が汚く残らない。使えない GPU では普通の 8bit にする
    if (this.type === undefined) { try { const e = this.r.extensions; this.type = (e.has('EXT_color_buffer_float') || e.has('EXT_color_buffer_half_float')) ? THREE.HalfFloatType : THREE.UnsignedByteType; } catch (err) { this.type = THREE.UnsignedByteType; } }
    const mk = (mip) => new THREE.WebGLRenderTarget(w, h, { type: this.type, depthBuffer: false, generateMipmaps: mip, minFilter: mip ? THREE.LinearMipmapLinearFilter : THREE.LinearFilter, magFilter: THREE.LinearFilter, wrapS: THREE.ClampToEdgeWrapping, wrapT: THREE.ClampToEdgeWrapping });
    // 0,1 = デッキ A/B、2,3 = 出力（残像のために交互に使う）
    this.rt = [mk(false), mk(false), mk(true), mk(true)];
    this.cur = 2; this.r.setRenderTarget(this.rt[2]); this.r.clear(); this.r.setRenderTarget(this.rt[3]); this.r.clear(); this.r.setRenderTarget(null);
  }
  get texture() { return this.rt[this.cur] ? this.rt[this.cur].texture : BLACK_TEX; }
  get aspect() { return this.w / Math.max(1, this.h); }
  pass(mat, target) { this.quad.material = mat; this.r.setRenderTarget(target); this.r.render(this.scene, this.cam); }
  render() {
    const P = VJP, asp = this.aspect;
    for (let k = 0; k < 2; k++) {
      const D = P.deck[k]; if (!D.active) continue;
      const u = this.deckMat[k].uniforms;
      u.uGen.value = D.gen; u.uT.value = D.t; u.uBt.value = D.bt; u.uPulse.value = P.pulse; u.uCnt.value = D.count; u.uZoom.value = D.zoom; u.uAsp.value = asp;
      u.uC1.value.copy(P.colors[0]); u.uC2.value.copy(P.colors[1]); u.uC3.value.copy(P.colors[2]);
      u.uImg.value = D.img || BLACK_TEX; u.uImgAsp.value = D.imgAsp; u.uImgOk.value = D.img ? 1 : 0; u.uImgK.value = D.imgK;
      u.uTxt.value = P.txtTex || BLACK_TEX; u.uTxtAsp.value = P.txtAsp;
      this.pass(this.deckMat[k], this.rt[k]);
    }
    const prev = this.cur, next = this.cur === 2 ? 3 : 2;
    const u = this.fxMat.uniforms, X = P.fx;
    u.uA.value = this.rt[0].texture; u.uB.value = this.rt[1].texture; u.uPrev.value = this.rt[prev].texture; u.uXf.value = P.xf; u.uAsp.value = asp; u.uT.value = P.t;
    u.uRes.value.set(this.w, this.h); u.uShakeV.value.set(X.shakeX, X.shakeY);
    for (const k of ['zoomP', 'rot', 'mirror', 'kale', 'kaleRot', 'pix', 'glitch', 'glitchSeed', 'rgb', 'fb', 'edge', 'colMode', 'hue', 'post', 'invert', 'scan', 'flash', 'strobe', 'bright', 'black']) u['u' + k[0].toUpperCase() + k.slice(1)].value = X[k];
    u.uC1.value.copy(P.colors[0]); u.uC2.value.copy(P.colors[1]); u.uC3.value.copy(P.colors[2]);
    u.uTxt.value = P.txtTex || BLACK_TEX; u.uTxtOn.value = P.txtOn; u.uTxtAsp.value = P.txtAsp; u.uTxtK.value = P.txtK;
    this.pass(this.fxMat, this.rt[next]);
    this.cur = next;
    this.r.setRenderTarget(null);
  }
  /* 出力を画面（キャンバス）全体、または一部（x,y,w,h：CSS ピクセル・下から）に描く */
  blit(x, y, w, h, tex) {
    // tex を渡すとそのテクスチャ（デッキの映像）を描く。マテリアルを分けて、毎フレームの差し替えで作り直しが起きないようにする
    const mat = tex ? (this.blitMat2 || (this.blitMat2 = new THREE.MeshBasicMaterial({ toneMapped: false, depthTest: false, depthWrite: false }))) : this.blitMat;
    const t = tex || this.texture;
    if (mat.map !== t) { const first = !mat.map; mat.map = t; if (first) mat.needsUpdate = true; }
    const r = this.r;
    if (w !== undefined) { r.setScissorTest(true); r.setScissor(x, y, w, h); r.setViewport(x, y, w, h); }
    this.quad.material = mat; r.setRenderTarget(null); r.render(this.scene, this.cam);
    if (w !== undefined) { r.setScissorTest(false); const s = r.getSize(new THREE.Vector2()); r.setViewport(0, 0, s.x, s.y); }
  }
}

/* ---------- 毎フレーム：拍・エフェクトの値を計算して、必要なら VJ 映像を作る ---------- */
let VJ_MAIN = null;
const VJOUT = { full: false, win: null, winEng: null, winRenderer: null, winCanvas: null };
function vjColors(out) {
  const V = S.vj;
  if (V.pal >= 0 && PALETTES[V.pal]) for (let i = 0; i < 3; i++) out[i].set(PALETTES[V.pal].c[i]);
  else for (let i = 0; i < 3; i++) out[i].copy(F.colors[i]);
}
/* 画像バンク：VJ 専用のテクスチャ（imageTexture と違って、読み込み完了でステージを組み立て直さない） */
const VJ_IMG_CACHE = new Map();
function vjImageTex(id) {
  if (!id || !IMAGES[id]) return null;
  let t = VJ_IMG_CACHE.get(id);
  if (!t) { t = new THREE.TextureLoader().load(IMAGES[id]); t.colorSpace = THREE.SRGBColorSpace; t.userData.shared = true; VJ_IMG_CACHE.set(id, t); }
  return t;
}
function vjImages() { return (S.vj.images || []).filter(id => IMAGES[id]); }
/* いま映す画像の番号：選んだ画像から、「拍で次の画像へ」の拍数ごとに1つずつ進む */
function vjImgIndex(D) {
  const n = vjImages().length; if (!n) return 0;
  const ir = S.vj.imgRate || 0; const step = ir > 0 ? Math.floor(Math.max(0, F.beat - (D.imgAt || 0)) / ir) : 0;
  return (((D.img || 0) + step) % n + n) % n;
}
function vjImageAt(idx) {
  const ims = vjImages(); if (!ims.length) return null;
  return vjImageTex(ims[((idx % ims.length) + ims.length) % ims.length]);
}
/* 文字：VJ 専用のテクスチャ（打つたびに増えないよう、変わったら前のものを捨てる） */
const VJ_TXT = { key: '', tex: null };
function vjTextTex(text, style) {
  const key = text + '|' + style;
  if (VJ_TXT.key === key && VJ_TXT.tex) return VJ_TXT.tex;
  if (VJ_TXT.tex) VJ_TXT.tex.dispose();
  const font = `bold 150px "Hiragino Maru Gothic ProN","Arial Rounded MT Bold","Yu Gothic UI",sans-serif`;
  const m = document.createElement('canvas').getContext('2d'); m.font = font;
  const cv = document.createElement('canvas'); cv.width = Math.min(4096, Math.max(256, Math.ceil(m.measureText(text || ' ').width) + 140)); cv.height = 256;
  const g = cv.getContext('2d'); g.font = font; g.textAlign = 'center'; g.textBaseline = 'middle';
  if (style === 'neon') { g.shadowColor = '#ffffff'; g.shadowBlur = 30; g.lineWidth = 8; g.strokeStyle = 'rgba(255,255,255,.75)'; g.strokeText(text, cv.width / 2, 132); g.shadowBlur = 10; }
  g.fillStyle = '#ffffff'; g.fillText(text, cv.width / 2, 132);
  const t = new THREE.CanvasTexture(cv); t.colorSpace = THREE.SRGBColorSpace; t.userData.aspect = cv.width / cv.height;
  VJ_TXT.key = key; VJ_TXT.tex = t;
  return t;
}
function vjTrigger(k) { VJ_TRIG[k] = LT.t; }
function vjNeeded() {
  const V = S.vj; if (!V) return false;
  if (VJOUT.full || (VJOUT.win && !VJOUT.win.closed)) return true;
  if (UIPREF.vjOpen && !UIPREF.vjMin) return true;
  return vjTargetMats().length > 0;
}
function vjFrame(dt) {
  const V = S.vj; if (!V) return;
  const P = VJP, beat = F.beat, t = F.t, bpm = Math.max(40, S.lights.bpm || 120), spb = 60 / bpm;
  // オートVJ：決めた小節ごとに、隠れている方のデッキを入れ替えてフェード
  if (V.auto) {
    const idx = Math.floor(beat / (4 * Math.max(1, V.autoBars)));
    if (VJRT.autoIdx === null) VJRT.autoIdx = idx;
    else if (idx !== VJRT.autoIdx) { VJRT.autoIdx = idx; vjAutoStep(); }
  } else VJRT.autoIdx = null;
  // クロスフェーダー（フェード中・拍で交互）
  let xf = V.xf;
  if (VJRT.fade) {
    if (!S.lights.playing) VJRT.fade.b0 = Math.min(VJRT.fade.b0, beat) - dt * bpm / 60;   // 一時停止中も時間で進める（止まったままにしない）
    const k = clamp((beat - VJRT.fade.b0) / VJRT.fade.len, 0, 1); xf = lerp(VJRT.fade.from, VJRT.fade.to, smooth(k)); V.xf = xf;
    if (k >= 1) { VJRT.fade = null; vjUIUpdate(); }
  }
  if (V.xfAuto > 0) xf = Math.floor(beat / V.xfAuto) % 2;
  P.xf = xf;
  P.t = t; P.beat = beat;
  P.pulse = Math.exp(-fract(beat) * 4.5);
  vjColors(P.colors);
  const rate = Math.max(0.25, V.rate || 1), bR = beat / rate, pR = Math.exp(-fract(bR) * 4.5), iR = Math.floor(bR);
  const amt = V.amt ?? 1;
  const env = (k, len) => { const a = (t - VJ_TRIG[k]) / (len * spb); return a < 0 || a > 1 ? 0 : Math.pow(1 - a, 2); };
  // デッキ
  const decks = [V.a, V.b];
  for (let k = 0; k < 2; k++) {
    const D = decks[k], O = P.deck[k];
    O.active = k === 0 ? xf < 0.999 : xf > 0.001;
    O.gen = VJ_GEN_ID[D.gen] ?? 0; O.count = D.count ?? 6; O.zoom = D.zoom ?? 1;
    const sp = D.speed ?? 1; O.bt = beat * sp; O.t = t * sp;
    O.img = null; O.imgAsp = 1.6; O.imgK = 0;
    if (D.gen === 'image') {
      const ir = V.imgRate || 0;
      const tex = vjImageAt(vjImgIndex(D));
      if (tex && tex.image && tex.image.width) { O.img = tex; O.imgAsp = tex.image.width / tex.image.height; }
      O.imgK = ir > 0 ? fract((beat - (D.imgAt || 0)) / ir) : fract(beat / 16);
    }
  }
  // 文字
  const wantTxt = V.text && (V.textOn || V.a.gen === 'text' || V.b.gen === 'text');
  if (wantTxt) { const tx = vjTextTex(V.text, V.textStyle === 'neon' ? 'neon' : 'plain'); P.txtTex = tx; P.txtAsp = tx.userData.aspect || 4; } else P.txtTex = null;
  P.txtOn = V.textOn && V.text ? (V.textAnim === 'blink' ? (Math.floor(beat * 2) % 2 ? 1 : 0) : 1) : 0;
  P.txtK = V.textAnim === 'stay' ? 0.3 : P.pulse;
  // エフェクト
  const X = P.fx, fx = V.fx || {};
  const pump = V.pump ?? 0.5;
  const oneZoom = env('zoom', 1), oneFlash = env('flash', 1), oneInv = env('invert', 1) > 0 ? 1 : 0, oneGl = env('glitch', 1.5);
  X.zoomP = pump * 0.05 * P.pulse + (fx.zoom ? amt * 0.16 * pR : 0) + oneZoom * 0.35;
  X.flash = (fx.flash ? amt * Math.pow(pR, 3) * 0.7 : 0) + oneFlash * 0.9;
  const invBase = fx.invert ? (iR % 2) : 0; X.invert = oneInv ? 1 - invBase : invBase;   // ボタンの反転は、いまの状態をひっくり返す
  const glOn = fx.glitch && hash1(iR * 13.7) > 0.35;
  X.glitch = Math.min(1, (glOn ? amt * (0.25 + 0.75 * pR) : 0) + oneGl);
  X.glitchSeed = Math.floor(t * 18) * 1.37 + iR;
  X.rgb = (fx.rgb ? amt * (0.25 + 0.75 * pR) : 0) + oneGl * 0.5;
  const sh = (fx.shake ? amt * pR : 0) + oneZoom * 0.5;
  X.shakeX = (hash1(iR * 3.1 + Math.floor(t * 30)) - 0.5) * 0.05 * sh; X.shakeY = (hash1(iR * 5.7 + Math.floor(t * 30) + 2) - 0.5) * 0.05 * sh;
  X.rot = fx.rotate ? beat * 0.09 * (V.rotDir || 1) : 0;
  X.mirror = V.mirror || 0;
  X.kale = fx.kaleido ? Math.max(2, V.kaleN || 6) : 0; X.kaleRot = beat * 0.12;
  X.pix = fx.pixel ? amt * (0.15 + 0.85 * pR) : 0;
  X.fb = fx.feedback ? 1 : 0;
  X.edge = fx.edge ? 1 : 0;
  X.colMode = Math.max(0, VJ_COLOR_MODES.findIndex(m => m[0] === V.colorMode));
  X.hue = (V.hue || 0) * beat / 16 * TAU;
  X.post = fx.posterize ? 1 : 0;
  X.scan = fx.scan ? 1 : 0;
  X.strobe = VJ_HOLD.strobe ? (fract(t * 12) < 0.4 ? 1 : 0) : 0;
  X.bright = (V.bright ?? 1) * (1 - pump * 0.45 * (1 - P.pulse));
  VJRT.black = lerp(VJRT.black, V.black ? 1 : 0, 1 - Math.exp(-dt * 14));
  X.black = VJRT.black;
  // 描く
  VJRT.needed = vjNeeded();
  if (!VJRT.needed) { if (VJ_HELD.size) vjApplyTargets(false); return; }
  if (!VJ_MAIN) VJ_MAIN = new VJEngine(renderer);
  if (VJOUT.full) { const s = renderer.getDrawingBufferSize(new THREE.Vector2()); const k = Math.min(1, 1920 / s.x); VJ_MAIN.ensure(s.x * k, s.y * k); }
  else { const res = QUALITY.level === 'low' ? 480 : 640; VJ_MAIN.ensure(res, res * 9 / 16); }
  VJ_MAIN.render();
  vjApplyTargets(true);
  vjRenderWindow();
}
/* オートVJ：隠れている方のデッキに次の映像を入れて、2拍でフェード */
function vjAutoStep() {
  const V = S.vj; const shown = VJRT.fade ? (VJRT.fade.to > 0.5 ? 'b' : 'a') : (V.xf < 0.5 ? 'a' : 'b'); const hidden = shown === 'a' ? 'b' : 'a';
  const pool = VJ_GENS.map(g => g[0]).filter(k => k !== 'black' && k !== 'text' && (k !== 'image' || vjImages().length));
  let g; do { g = pool[Math.floor(Math.random() * pool.length)]; } while (pool.length > 1 && g === V[hidden].gen);
  V[hidden] = { ...V[hidden], gen: g, count: 3 + Math.floor(Math.random() * 6), speed: [0.5, 1, 1, 2][Math.floor(Math.random() * 4)] };
  const opts = ['kaleido', 'rgb', 'glitch', 'flash', 'feedback', 'pixel', 'rotate', 'shake'];
  for (const k of opts) V.fx[k] = false;
  for (let i = 0; i < 2; i++) if (Math.random() < 0.6) V.fx[opts[Math.floor(Math.random() * opts.length)]] = true;
  V.fx.zoom = true;
  vjFade(hidden === 'b' ? 1 : 0, 2);
}
/* クロスフェード。beats=0 はカット。quant=true なら次の拍の頭から（押すのが少し遅れても拍に合う） */
function vjFade(to, beats = 2, quant = true) {
  const b = F.beat, fb = fract(b);
  const b0 = !quant ? b : fb < 0.15 ? Math.floor(b) : Math.ceil(b);
  VJRT.fade = { from: S.vj.xf, to, b0, len: Math.max(0.02, beats) }; S.vj.xfAuto = 0; scheduleSave(); vjUIUpdate();
}

/* ---------- 出力先：LED モニター・背景 LED に VJ 映像を貼る ---------- */
// VJ 映像を映しているマテリアル → 元の設定（やめたときに戻す・GLB 書き出しで元の設定を使う）
const VJ_HELD = new Map();
/* モニターの区分：メイン／サブ。リモコンの「ライブ映像 / VJ映像」をそれぞれ切り替える（メインだけカメラ・サブは VJ、など）。
   自動 = 左右ミラーで複製したモニターはサブ、それ以外はメイン */
function monGroup(inst) { const g = inst && inst.p && inst.p.group; return g === 'main' || g === 'sub' ? g : (inst && inst.mirror ? 'sub' : 'main'); }
function monModeKey(g) { return g === 'sub' ? 'monitorMode2' : 'monitorMode'; }
function monitorShowsVJ(inst) {
  const src = inst && inst.p && inst.p.src;
  return src === 'vj' || ((src === 'cam1' || src === 'cam2') && S.lights[monModeKey(monGroup(inst))] === 'vj');
}
/* いずれかのカメラ映像のモニターが VJ になっているか（VJ リモコンの上の「モニター」ボタン） */
function monAnyVJ() { return S.lights.monitorMode === 'vj' || S.lights.monitorMode2 === 'vj'; }
function setMonitorMode(g, mode) {
  if (g === 'all') { S.lights.monitorMode = mode; S.lights.monitorMode2 = mode; } else S.lights[monModeKey(g)] = mode;
  updateRemote(); scheduleSave(); vjUIUpdate();
}
function vjTargetMats() {
  const out = [];
  if (S.vj && S.vj.backdrop) for (const m of BD_LEDS) out.push(m);
  for (const m of REG.monitors) if (monitorShowsVJ(m.inst)) out.push(m.mat);
  return out;
}
function vjRelease(mat) {
  const p = VJ_HELD.get(mat); if (!p) return;
  const u = mat.uniforms; u.uSrc.value = p.src; u.uTex.value = p.tex; u.uTexRect.value.copy(p.rect); u.uTexLod.value = p.lod;   // 切り抜き（画像・カメラ）も元に戻す
  VJ_HELD.delete(mat);
}
// 「つなげて1枚」用：マテリアル → 面（メッシュ）。組み立て直したときだけ探し直す
let VJ_MESH = { sig: '', map: new Map() };
function vjMeshFor(mats) {
  const sig = mats.map(m => m.id).join(',');
  if (sig !== VJ_MESH.sig) {
    const want = new Set(mats), map = new Map();
    for (const root of [G.backdrop, G.assets]) root.traverse(o => { if (o.isMesh && want.has(o.material)) map.set(o.material, o); });
    VJ_MESH = { sig, map };
  }
  return VJ_MESH.map;
}
function coverRect(panelAsp, texAsp, out) {
  if (panelAsp > texAsp) { const s = texAsp / panelAsp; out.set(0, (1 - s) / 2, 1, s); } else { const s = panelAsp / texAsp; out.set((1 - s) / 2, 0, s, 1); }
  return out;
}
const _vjBox = new THREE.Box3(), _vjCrop = new THREE.Vector4();
function vjApplyTargets(active) {
  const targets = active ? vjTargetMats() : [];
  const set = new Set(targets);
  for (const m of [...VJ_HELD.keys()]) if (!set.has(m)) vjRelease(m);
  if (!targets.length || !VJ_MAIN) return;
  const tex = VJ_MAIN.texture, texAsp = VJ_MAIN.aspect;
  for (const m of targets) {
    const u = m.uniforms;
    if (!VJ_HELD.has(m)) VJ_HELD.set(m, { src: u.uSrc.value, tex: u.uTex.value, rect: u.uTexRect.value.clone(), lod: u.uTexLod.value });
    u.uSrc.value = 0; u.uTex.value = tex;
  }
  // 「つなげて1枚」：全部の画面の外枠（正面から見た X・Y）を VJ 映像1枚に対応させる
  let U = null, meshes = null;
  if (S.vj.map === 'span' && targets.length > 1) {
    meshes = vjMeshFor(targets); U = { x0: 1e9, x1: -1e9, y0: 1e9, y1: -1e9 };
    for (const m of targets) {
      const me = meshes.get(m); if (!me) continue;
      _vjBox.setFromObject(me); m.userData.vjB = [_vjBox.min.x, _vjBox.max.x, _vjBox.min.y, _vjBox.max.y];
      U.x0 = Math.min(U.x0, _vjBox.min.x); U.x1 = Math.max(U.x1, _vjBox.max.x); U.y0 = Math.min(U.y0, _vjBox.min.y); U.y1 = Math.max(U.y1, _vjBox.max.y);
    }
    if (U.x1 <= U.x0) U = null;
  }
  for (const m of targets) {
    const u = m.uniforms, R = u.uTexRect.value, b = U && meshes.get(m) ? m.userData.vjB : null;
    if (b) {
      const uw = Math.max(0.01, U.x1 - U.x0), uh = Math.max(0.01, U.y1 - U.y0);
      coverRect(uw / uh, texAsp, _vjCrop);
      const ox = (b[0] - U.x0) / uw, oy = (b[2] - U.y0) / uh, sx = (b[1] - b[0]) / uw, sy = (b[3] - b[2]) / uh;
      R.set(_vjCrop.x + ox * _vjCrop.z, _vjCrop.y + oy * _vjCrop.w, sx * _vjCrop.z, sy * _vjCrop.w);
    } else coverRect(u.uAspect.value, texAsp, R);
    // ドット1個に入る VJ 映像のピクセル数 → ミップマップの段（細かい模様がチラつかない）
    u.uTexLod.value = Math.max(0, Math.log2(Math.max(1, VJ_MAIN.w * R.z / u.uDots.value.x)) - 0.5);
  }
}

/* ---------- 全画面の VJ 出力モード ---------- */
function vjEnterFull() {
  if (VJOUT.full) return;
  VJOUT.full = true; document.body.classList.add('vjFull'); orbit.enabled = false; tcontrols.detach();
  UIPREF.vjOpen = true; saveUIPref(); buildVJRemote();
  try { document.documentElement.requestFullscreen?.().catch(() => {}); } catch (e) {}
  setTimeout(onResize, 50);
  toast('VJ 出力モード（Esc で戻る・Tab でリモコンを隠す）', 3000);
}
function vjExitFull() {
  if (!VJOUT.full) return;
  VJOUT.full = false; document.body.classList.remove('vjFull', 'vjHideUI'); orbit.enabled = VIEW.mode === 'free'; reattachGizmo();
  try { if (document.fullscreenElement) document.exitFullscreen?.().catch(() => {}); } catch (e) {}
  setTimeout(() => { onResize(); placeVJRemote(); }, 50);
  buildVJRemote();
}
document.addEventListener('fullscreenchange', () => { if (!document.fullscreenElement && VJOUT.full) vjExitFull(); });
// ページを閉じる・読み直すときは別窓も閉じる（止まった映像が残らないように）
window.addEventListener('pagehide', () => { try { if (VJOUT.win && !VJOUT.win.closed) VJOUT.win.close(); } catch (e) {} });
function vjRenderFull() { VJ_MAIN.blit(); }

/* ---------- 別ウィンドウ出力（プロジェクター・OBS 用） ---------- */
function vjOpenWindow() {
  if (VJOUT.win && !VJOUT.win.closed) { VJOUT.win.focus(); return; }
  const w = window.open('', 'stagekobo_vj', 'width=960,height=540');
  if (!w) { toast('ポップアップがブロックされました。ブラウザでこのページのポップアップを許可してください', 5000); return; }
  // 前の別窓が残っていたら中身を作り直す（ページを読み直したときなど）
  w.document.open(); w.document.write('<!DOCTYPE html><html lang="ja"><head><meta charset="UTF-8"><title>すてーじ工房 VJ 出力</title></head><body></body></html>'); w.document.close();
  w.document.body.style.cssText = 'margin:0;background:#000;overflow:hidden';
  // 別窓でキーを押しても、パッド（Z X C V N M）などが効くようにメインへ渡す
  for (const type of ['keydown', 'keyup']) w.addEventListener(type, (e) => { if (e.key === 'Escape' || e.key === 'Tab') return; window.dispatchEvent(new KeyboardEvent(type, { key: e.key, repeat: e.repeat, shiftKey: e.shiftKey })); if (e.key === ' ') e.preventDefault(); });
  const cv = w.document.createElement('canvas'); cv.style.cssText = 'display:block;width:100vw;height:100vh;cursor:none'; w.document.body.appendChild(cv);
  const tip = w.document.createElement('div'); tip.textContent = 'ダブルクリックで全画面'; tip.style.cssText = 'position:fixed;left:12px;top:10px;color:#9fe;font:12px sans-serif;opacity:.8;transition:opacity 1s'; w.document.body.appendChild(tip);
  setTimeout(() => { tip.style.opacity = 0; }, 2500);
  cv.addEventListener('dblclick', () => { try { w.document.documentElement.requestFullscreen(); } catch (e) {} });
  const r = new THREE.WebGLRenderer({ canvas: cv, antialias: false });
  r.setPixelRatio(Math.min(2, w.devicePixelRatio || 1));
  VJOUT.win = w; VJOUT.winCanvas = cv; VJOUT.winRenderer = r; VJOUT.winEng = new VJEngine(r);
  w.addEventListener('beforeunload', () => vjCloseWindow(true));
  vjUIUpdate();
}
function vjCloseWindow(fromWin = false) {
  if (VJOUT.winRenderer) { try { VJOUT.winRenderer.dispose(); } catch (e) {} }
  if (!fromWin && VJOUT.win && !VJOUT.win.closed) VJOUT.win.close();
  VJOUT.win = null; VJOUT.winRenderer = null; VJOUT.winEng = null; VJOUT.winCanvas = null;
  vjUIUpdate();
}
function vjRenderWindow() {
  const w = VJOUT.win; if (!w) return;
  if (w.closed) { vjCloseWindow(true); return; }
  const r = VJOUT.winRenderer, cw = w.innerWidth, ch = w.innerHeight; if (!cw || !ch) return;
  const sz = r.getSize(new THREE.Vector2()); if (sz.x !== cw || sz.y !== ch) r.setSize(cw, ch, false);
  const ds = r.getDrawingBufferSize(new THREE.Vector2()); const k = Math.min(1, 1920 / ds.x);
  VJOUT.winEng.ensure(ds.x * k, ds.y * k);
  VJOUT.winEng.render(); VJOUT.winEng.blit();
}
