
/* =====================================================================
   ライト演出：パターン（純関数）・色・調光・キュー・特効トリガー
   -------------------------------------------------------------------
   VRC移植メモ：
   PATTERNS[name](cue, beat, i, n, xn, side) → { pan, tilt }（度・ワールド基準）
     pan  = 客席方向(+Z)から +X 側への振り角
     tilt = 床置きは真上、吊りは真下からの倒れ角（+Z=客席側に倒れる）
     beat = 経過拍（BPM×時間/60）、i/n = 同種灯体の通し番号/総数（左→右）、xn = -1..1 の横位置
   colorAt / dimAt も同じ引数で決まる。Udon では Update() で同じ式を回せば同じ動きになる。
   ===================================================================== */
const PATTERN_DEFS = [['still', '静止（扇に広げる）'], ['sweep', 'スイープ（左右に振る）'], ['wave', 'ウェーブ（波打つ）'], ['fan', 'ファン（開いて閉じる）'], ['circle', 'サークル（円を描く）'], ['cross', 'クロス（交差）'], ['updown', 'アップダウン'], ['random', 'ランダム'], ['audience', '客席あおり'], ['center', 'センター集中（演者を狙う）']];
const COLOR_MODES = [['single', '単色（色1）'], ['alt', '交互（色1・色2）'], ['three', '3色ローテ'], ['split', '左右で色分け'], ['rainbow', '虹色'], ['beat', '拍ごとに色替え'], ['chase', '追いかけ']];
const DIM_MODES = [['on', '点灯'], ['pulse', '拍で脈打つ'], ['wave', '明るさウェーブ'], ['chase', '1灯ずつ'], ['alt', '交互点滅'], ['strobe', 'ストロボ'], ['random', 'ランダム']];
const LASER_MODES = [['off', 'オフ'], ['fan', 'ファン'], ['sweep', 'スイープ'], ['cross', 'クロス'], ['cone', 'コーン（回転）']];
const PENLIGHT_MODES = [['cue', '照明の色'], ['white', '白'], ['rainbow', '虹色']];
/* リモコンの色ボタン（3色1組のパレット）。Unity側でも同じ並びで焼き込む */
const PALETTES = [
  { name: 'アイス', c: ['#bfe4ff', '#7f9cff', '#ffffff'] }, { name: 'パステル', c: ['#8fd3ff', '#ff8ad8', '#ffffff'] },
  { name: 'ネオン', c: ['#7fe8ff', '#ff6fd8', '#c79bff'] }, { name: 'ピンク', c: ['#ff7fd0', '#b07bff', '#ffffff'] },
  { name: 'サンセット', c: ['#ffb36b', '#ff5f8f', '#ffe08f'] }, { name: 'ミント', c: ['#7dffc4', '#3fd4ff', '#e8fff4'] },
  { name: 'レッド', c: ['#ff3b5c', '#ffffff', '#ff9fb0'] }, { name: 'ゴールド', c: ['#ffd27f', '#fff4d6', '#ffb347'] },
];
const SHORT = {
  pattern: { still: '静止', sweep: 'スイープ', wave: 'ウェーブ', fan: 'ファン', circle: 'サークル', cross: 'クロス', updown: '上下', random: 'ランダム', audience: '客席あおり', center: 'センター' },
  colorMode: { single: '単色', alt: '交互', three: '3色', split: '左右', rainbow: '虹', beat: '拍替え', chase: '追う' },
  dim: { on: '点灯', pulse: '脈打つ', wave: '波', chase: '1灯ずつ', alt: '交互', strobe: 'ストロボ', random: 'ランダム' },
  laser: { off: 'OFF', fan: 'ファン', sweep: 'スイープ', cross: 'クロス', cone: 'コーン' },
};
const smooth = x => x * x * (3 - 2 * x);

const PATTERNS = {
  still: (c, b, i, n, xn) => ({ pan: xn * c.spread, tilt: c.tilt }),
  sweep: (c, b, i, n, xn, s) => ({ pan: xn * c.spread * 0.3 + (c.sym ? s : 1) * c.amp * Math.sin(TAU * b * c.speed / 4), tilt: c.tilt }),
  wave: (c, b, i, n, xn, s) => { const ph = TAU * (b * c.speed / 4 - (xn + 1) * 0.5); return { pan: xn * c.spread * 0.5 + c.amp * 0.6 * Math.sin(ph) * (c.sym ? s : 1), tilt: c.tilt + c.amp * 0.5 * Math.sin(ph) }; },
  fan: (c, b, i, n, xn) => { const o = 0.5 + 0.5 * Math.sin(TAU * b * c.speed / 4); return { pan: xn * c.spread * o * 1.5, tilt: c.tilt + 10 * o }; },
  circle: (c, b, i, n, xn, s) => { const ph = TAU * (b * c.speed / 4) + (c.sym ? 0 : i * 0.6); return { pan: xn * c.spread * 0.4 + c.amp * Math.cos(ph) * (c.sym ? s : 1), tilt: c.tilt + c.amp * 0.6 * Math.sin(ph) }; },
  cross: (c, b, i, n, xn) => ({ pan: (i % 2 ? 1 : -1) * c.amp * Math.sin(TAU * b * c.speed / 4) - xn * c.spread * 0.6, tilt: c.tilt }),
  updown: (c, b, i, n, xn) => ({ pan: xn * c.spread, tilt: c.tilt + c.amp * (0.5 + 0.5 * Math.sin(TAU * (b * c.speed / 4 - i / Math.max(1, n) * 0.5))) }),
  random: (c, b, i, n) => { const k = b * c.speed * 0.5, f = Math.floor(k), u = smooth(k - f); const r = m => hash1(i * 13.1 + m * 7.7) * 2 - 1;
    return { pan: lerp(r(f), r(f + 1), u) * c.amp * 1.5, tilt: c.tilt + lerp(hash1(i * 3.3 + f * 5.1), hash1(i * 3.3 + (f + 1) * 5.1), u) * c.amp }; },
  audience: (c, b, i, n, xn) => ({ pan: xn * c.spread * 0.5 + c.amp * Math.sin(TAU * (b * c.speed / 8 + i * 0.13)), tilt: c.tilt + 6 * Math.sin(TAU * b * c.speed / 4 + i) }),
  center: (c, b, i, n, xn) => ({ pan: xn * 10, tilt: c.tilt }),
};
const LT = { t: 0, beat: 0, bar: -1, accent: 0, demoIdx: null, lastBeatInt: -1 };
const _cc = new THREE.Color();
const F = {
  t: 0, dt: 0, beat: 0, cue: null, colors: [new THREE.Color(), new THREE.Color(), new THREE.Color()], master: 1, haze: 0.7, accent: 0, strobeTrig: false,
  focus: V3(0, 2.5, 1), center: V3(0, 2.5, 1), performers: [], heads: [],
  colorAt(i, n, xn) {
    const c = F.cue, C = F.colors, b = F.beat;
    switch (c.colorMode) {
      case 'alt': return C[i % 2];
      case 'three': return C[i % 3];
      case 'split': return xn < 0 ? C[0] : C[1];
      case 'rainbow': return _cc.setHSL(fract(i / Math.max(1, n) * 0.8 + b * 0.06), 0.85, 0.6, THREE.SRGBColorSpace);
      case 'beat': return C[Math.floor(b) % 3];
      case 'chase': return Math.floor(b * 2) % Math.max(1, n) === i ? C[1] : C[0];
      default: return C[0];
    }
  },
  dimAt(i, n) {
    if (F.strobeTrig) return fract(F.t * 13) < 0.4 ? 1 : 0;
    const b = F.beat; n = Math.max(1, n);
    switch (F.cue.dim) {
      case 'pulse': return 0.3 + 0.7 * Math.exp(-fract(b) * 3.5);
      case 'wave': return 0.2 + 0.8 * (0.5 + 0.5 * Math.sin(TAU * (b / 4 - i / n)));
      case 'chase': return Math.floor(b * 2) % n === i ? 1 : 0.06;
      case 'alt': return Math.floor(b) % 2 === i % 2 ? 1 : 0.08;
      case 'strobe': return fract(b * 4) < 0.3 ? 1 : 0;
      case 'random': return hash1(i * 7.1 + Math.floor(b * 2) * 3.3) > 0.45 ? 1 : 0.05;
      default: return 1;
    }
  },
};
const perfPool = Array.from({ length: 16 }, () => V3()), headPool = Array.from({ length: 16 }, () => V3());

/* ---- ライブ状態（いま実際に出ている演出）。リモコンはここを直接書き換える ---- */
let onLiveChanged = () => {};
const EFF = {};   // live にスピード倍率などを掛けた、実際に使う値
/* シーン（メモリー）を呼び出して live に読み込む */
function recallScene(i, auto = false) {
  const L = S.lights; if (!L.cues.length) return;
  L.cue = ((i % L.cues.length) + L.cues.length) % L.cues.length;
  L.live = deepMerge(defaultCues()[0], L.cues[L.cue]); L.liveEdited = false;
  const fx = (L.live.fx || '').split(',');
  for (const k of fx) if (k && k in FX_TRIG) FX_TRIG[k] = LT.t;
  onLiveChanged(auto); scheduleSave();
}
const setCue = recallScene; // 旧名（互換）
/* live の一部を書き換える（リモコンのボタン）。手で触ったらデモは止める */
function setLive(patch, opt = {}) {
  const L = S.lights; Object.assign(L.live, patch); L.liveEdited = true;
  if (L.demo && !opt.keepDemo) { L.demo = false; toast('デモを止めて手動操作に切り替えました'); }
  onLiveChanged(false); scheduleSave();
}
const paletteIndexOf = (c) => PALETTES.findIndex(p => p.c[0] === c.c1 && p.c[1] === c.c2 && p.c[2] === c.c3);
function triggerFx(k) { FX_TRIG[k] = LT.t; }

/* 空中のキラキラ */
let glitter = null;
function buildGlitter() {
  if (glitter) { G.fx.remove(glitter); disposeTree(glitter); glitter = null; }
  const n = 700, pos = new Float32Array(n * 3), rr = new Float32Array(n); const R = mulberry32(99);
  const W = S.stage.width + 8, D = S.stage.depth + 10;
  for (let i = 0; i < n; i++) { pos[i * 3] = (R() - 0.5) * W; pos[i * 3 + 1] = S.stage.height + 0.3 + R() * 11; pos[i * 3 + 2] = (R() - 0.5) * D + 2; rr[i] = R(); }
  const g = new THREE.BufferGeometry(); g.setAttribute('position', new THREE.BufferAttribute(pos, 3)); g.setAttribute('aR', new THREE.BufferAttribute(rr, 1));
  glitter = new THREE.Points(g, new THREE.ShaderMaterial({
    uniforms: { uT: { value: 0 }, uC: { value: col('#ffffff') }, uTex: { value: texSpark() } },
    vertexShader: /* glsl */`attribute float aR; uniform float uT; varying float vA; void main(){ vec3 p = position; p.y += sin(uT*0.3 + aR*40.0)*0.4; p.x += sin(uT*0.2 + aR*17.0)*0.3;
      vA = pow(0.5+0.5*sin(uT*(1.5+aR*2.0) + aR*60.0), 8.0); vec4 mv = modelViewMatrix*vec4(p,1.0); gl_PointSize = (2.0 + aR*5.0)*vA*(60.0/ -mv.z) + 0.5; gl_Position = projectionMatrix*mv; }`,
    fragmentShader: /* glsl */`uniform vec3 uC; uniform sampler2D uTex; varying float vA; void main(){ float a = texture2D(uTex, gl_PointCoord).r; gl_FragColor = vec4(uC*2.5, a*vA); }`,
    transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
  }));
  glitter.frustumCulled = false; markFx(glitter); G.fx.add(glitter);
}

function updateLights(dt) {
  const L = S.lights;
  if (L.playing) LT.beat += dt * L.bpm / 60;
  LT.t += dt;
  if (L.demo && L.playing) {
    const idx = Math.floor(LT.beat / (4 * Math.max(1, L.demoBars)));
    if (LT.demoIdx === null) LT.demoIdx = idx;
    else if (idx !== LT.demoIdx) { LT.demoIdx = idx; recallScene(L.cue + 1, true); }
  } else LT.demoIdx = null;
  const bar = Math.floor(LT.beat / 4); if (bar !== LT.bar) { LT.bar = bar; LT.accent = 1; }
  LT.accent = Math.max(0, LT.accent - dt * 3);
  const live = L.live || L.cues[L.cue] || defaultCues()[0];
  Object.assign(EFF, live); EFF.speed = (live.speed ?? 1) * (L.speedMul ?? 1);
  const cue = EFF;
  F.t = LT.t; F.dt = Math.min(dt, 0.1); F.beat = LT.beat; F.cue = cue;
  F.colors[0].set(cue.c1); F.colors[1].set(cue.c2); F.colors[2].set(cue.c3);
  F.master = L.blackout ? 0 : L.master; F.haze = L.haze * VENUE_ENV.beamMul; F.accent = LT.accent; F.strobeTrig = LT.t - FX_TRIG.strobe < 1.6;
  // 演者（ピンスポ／カメラの狙い先）
  F.performers.length = 0;
  for (const o of REG.performerUnits) { if (F.performers.length >= perfPool.length) break; const v = perfPool[F.performers.length]; o.getWorldPosition(v); v.y += 1.3 * Math.abs(o.scale.y); F.performers.push(v); }
  // 演者の顔（頭の中心）：カメラのアップで「追う人」を決めたときに使う（踊っても顔を追う）
  F.heads.length = 0;
  for (const o of REG.performerUnits) {
    if (F.heads.length >= headPool.length) break;
    const v = headPool[F.heads.length], hn = o.userData.headNode || (o.userData.headNode = o.getObjectByName('PerfHead'));
    if (hn) hn.getWorldPosition(v); else { o.getWorldPosition(v); v.y += 1.45 * Math.abs(o.scale.y); }
    F.heads.push(v);
  }
  F.center.set(0, S.stage.height + 1.3, S.stage.ring.z);
  if (F.performers.length) { F.focus.set(0, 0, 0); for (const v of F.performers) F.focus.add(v); F.focus.multiplyScalar(1 / F.performers.length); } else F.focus.copy(F.center);
  // 灯体：種類ごとに左→右で番号を振る
  const kinds = {};
  for (const f of REG.fixtures) { (kinds[f.kind] ||= []).push(f); f._x = f.node.getWorldPosition(_v1).x; }
  for (const k in kinds) {
    const arr = kinds[k].sort((a, b) => a._x - b._x); const n = arr.length;
    const x0 = arr[0]._x, x1 = arr[n - 1]._x, mid = (x0 + x1) / 2, half = Math.max(0.01, (x1 - x0) / 2);
    arr.forEach((f, i) => f.update(F, i, n, n > 1 ? clamp((f._x - mid) / half, -1, 1) : 0));
  }
  // カラーウォッシュ
  washLights.forEach((w, k) => { w.color.copy(F.colorAt(k, 4, (k - 1.5) / 1.5)); w.intensity = cue.wash ? L.wash * 1.0 * F.dimAt(k, 4) * F.master : 0; });
  backLight.color.copy(F.colors[1]); backLight.intensity = (cue.wash ? 0.8 : 0.3) * L.wash * F.master;
  // アニメ
  for (const a of REG.anims) a(F);
  for (const a of VENUE_ANIMS) a(F);
  for (const a of BD_ANIMS) a(F);
  for (const m of BD_LEDS) { m.uniforms.uTime.value = F.t; m.uniforms.uBeat.value = F.beat; }
  for (const m of REG.monitors) { m.mat.uniforms.uTime.value = F.t; m.mat.uniforms.uBeat.value = F.beat; }
  if (S.stage.floor === 'led') updateLedFloor(F.beat, F.colors);
  if (glitter) { glitter.visible = !!L.glitter; glitter.material.uniforms.uT.value = F.t; glitter.material.uniforms.uC.value.set(L.glitterColor); }
}
