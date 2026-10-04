import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { TransformControls } from 'three/addons/controls/TransformControls.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { Reflector } from 'three/addons/objects/Reflector.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

/* =====================================================================
   すてーじ工房 (Idol Stage Kit)
   ---------------------------------------------------------------------
   設計メモ（VRChat移植を見据えて）
   - すべての見た目は state(JSON) から再構築できる。state 以外に「正」は持たない。
   - 単位はメートル。Y上、+Z = 客席側。ステージ前縁 = z:+depth/2。
   - アセットは { type, pos, rot, scale, color, p(固有パラメータ), mirror, arr } の共通形式。
     mirror = X=0 を軸に左右対称コピー、arr = X方向に等間隔で並べる。
   - ライトの動きは「時間(拍) → 向き/色/明るさ」の純関数（PATTERNS / colorAt / dimAt）。
     Udon / Animator に同じ式を移植すれば同じ演出になる。
   - LEDモニター = カメラ → RenderTexture → ドットシェーダー（VRCでもそのまま同構成が可能）。
   ===================================================================== */

const APP_VERSION = '0.10.0';
const DEG = Math.PI / 180;
const TAU = Math.PI * 2;
const $ = (s, r = document) => r.querySelector(s);
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;
const fract = x => x - Math.floor(x);
const uid = (p = 'a') => p + '_' + Math.random().toString(36).slice(2, 8);
const col = (hex, s = 1) => new THREE.Color(hex).multiplyScalar(s);
const deepClone = o => JSON.parse(JSON.stringify(o));
function deepMerge(base, over) {
  if (over === undefined) return deepClone(base);
  if (base === null || typeof base !== 'object' || Array.isArray(base)) return deepClone(over);
  if (over === null || typeof over !== 'object' || Array.isArray(over)) return deepClone(over);
  const out = {};
  for (const k of Object.keys(base)) out[k] = deepMerge(base[k], over[k]);
  for (const k of Object.keys(over)) if (!(k in base)) out[k] = deepClone(over[k]);
  return out;
}
function mulberry32(a) { return function () { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
const hash1 = (n) => fract(Math.sin(n * 127.1 + 311.7) * 43758.5453);

/* ---------------- 既定 state ---------------- */
function defaultCues() {
  return [
    { name: 'オープニング', pattern: 'still', speed: 0.5, amp: 20, tilt: 8, spread: 18, sym: true, colorMode: 'single', c1: '#bfe4ff', c2: '#7f9cff', c3: '#ffffff', dim: 'on', beam: 1, laser: 'off', wash: true, penlight: 'white', fx: '' },
    { name: 'ゆったりウェーブ', pattern: 'wave', speed: 0.5, amp: 28, tilt: 25, spread: 20, sym: true, colorMode: 'alt', c1: '#7fe8ff', c2: '#ff8ad8', c3: '#ffffff', dim: 'wave', beam: 1, laser: 'off', wash: true, penlight: 'cue', fx: '' },
    { name: 'ファン開閉', pattern: 'fan', speed: 1, amp: 25, tilt: 22, spread: 40, sym: true, colorMode: 'three', c1: '#8fd3ff', c2: '#ff8fd0', c3: '#ffffff', dim: 'pulse', beam: 1, laser: 'fan', wash: true, penlight: 'cue', fx: '' },
    { name: 'サークル', pattern: 'circle', speed: 1, amp: 22, tilt: 30, spread: 20, sym: true, colorMode: 'rainbow', c1: '#ffffff', c2: '#ffffff', c3: '#ffffff', dim: 'on', beam: 1, laser: 'off', wash: true, penlight: 'rainbow', fx: '' },
    { name: 'クロス＋レーザー', pattern: 'cross', speed: 1, amp: 35, tilt: 28, spread: 20, sym: false, colorMode: 'alt', c1: '#b48cff', c2: '#7fe8ff', c3: '#ffffff', dim: 'alt', beam: 0.8, laser: 'sweep', wash: true, penlight: 'cue', fx: '' },
    { name: 'センター集中', pattern: 'center', speed: 1, amp: 10, tilt: 20, spread: 10, sym: true, colorMode: 'single', c1: '#ffffff', c2: '#ffd9f0', c3: '#ffffff', dim: 'on', beam: 1.4, laser: 'off', wash: false, penlight: 'white', fx: '' },
    { name: '客席あおり', pattern: 'audience', speed: 1, amp: 30, tilt: 70, spread: 30, sym: true, colorMode: 'beat', c1: '#ff8fd0', c2: '#8fd3ff', c3: '#fff38f', dim: 'pulse', beam: 1.1, laser: 'cross', wash: true, penlight: 'cue', fx: '' },
    { name: 'サビ（ストロボ）', pattern: 'random', speed: 2, amp: 40, tilt: 30, spread: 30, sym: true, colorMode: 'rainbow', c1: '#ffffff', c2: '#ffffff', c3: '#ffffff', dim: 'strobe', beam: 0.9, laser: 'cone', wash: true, penlight: 'rainbow', fx: 'spark,confetti' },
  ];
}

/* VJ リモコンの設定（演出の操作なので Undo には入れない。自動保存・JSON 保存には入る） */
function baseVJ() {
  return {
    a: { gen: 'tunnel', speed: 1, count: 6, zoom: 1, img: 0 },
    b: { gen: 'p_triangles', speed: 1, count: 6, zoom: 1, img: 1 },
    xf: 0, xfAuto: 0,
    pal: -1, colorMode: 'raw', hue: 0, bright: 1,
    fx: { zoom: true, flash: false, rgb: false, glitch: false, kaleido: false, shake: false, rotate: false, feedback: false, pixel: false, invert: false, posterize: false, edge: false, scan: false },
    rate: 1, amt: 1, pump: 0.5, mirror: 0, kaleN: 6, rotDir: 1,
    text: 'LIVE!', textOn: false, textAnim: 'pulse', textStyle: 'neon',
    auto: false, autoBars: 4, images: [], imgRate: 0,
    backdrop: false, map: 'same', black: false,
    slots: [null, null, null, null, null, null, null, null],
  };
}
function baseState() {
  return {
    format: 'stagekobo', version: 1, name: 'NEW STAGE',
    stage: {
      shape: 'rect', width: 16, depth: 8, height: 1.2,
      floor: 'gloss', floorColor: '#161a33', floorColor2: '#8fd3ff', tile: 1.0,
      reflect: true, reflectStrength: 0.55,
      bodyColor: '#12142a', front: 'panel', frontColor: '#1a1d3a', frontGlow: '#ff6fd8',
      edge: true, edgeColor: '#6fe6ff', edgeIntensity: 2.2, edgeBottom: true,
      steps: { on: true, pos: 'center', width: 8, count: 4, tread: 0.42, sideX: 5, color: '#20264f', led: true, led1: '#7fe8ff', led2: '#ff8ad8', side: 'none', sideZ: 0, back: 'none', backX: 4, sideWidth: 2.4 },
      upper: { on: false, width: 9, depth: 2.6, height: 1.0, stepWidth: 4, stepCount: 3 },
      runway: { on: false, length: 10, width: 2.4 },
      sub: { on: false, shape: 'round', size: 6 },
      ring: { on: true, radius: 2.0, z: 1.2, color: '#bfe9ff', intensity: 1.6, disc: true },
    },
    venue: { type: 'hall', color: '#0b0e22', floorColor: '#0a0b16', stars: true, seats: true, crowdDots: true },
    backdrop: { type: 'truss_led', width: 22, height: 10, z: 0, scale: 1, c1: '#8fd3ff', c2: '#ff8ad8', c3: '#ffffff', pattern: 'triangles', bright: 1.0, image: null },
    lights: {
      bpm: 128, playing: true, demo: false, demoBars: 4, cue: 1, master: 1, speedMul: 1, blackout: false, monitorMode: 'live', monitorMode2: 'live', liveEdited: false, haze: 0.7, fog: 0.012,
      bloom: 0.6, bloomRadius: 0.5, bloomThreshold: 0.92, exposure: 1.0, ambient: 0.35, wash: 0.8,
      glitter: true, glitterColor: '#cfe8ff', cues: defaultCues(),
    },
    cams: {
      // target：アップ（顔寄り）で追う演者（-1 = 全員のまん中。0〜 = 演者ダミーの番号。Unity では「登録した人」の顔を追う）
      cam1: { mode: 'auto', speed: 1, fov: 30, dist: 8, height: 1.7, target: -1 },
      cam2: { mode: 'closeup', speed: 1, fov: 22, dist: 3.5, height: 1.5, target: -1 },
      showRig: true, pip: true, res: 'mid',
    },
    vj: baseVJ(),
    assets: [],
  };
}

/* ---------------- 画像・バイナリ置き場（state には id だけ持つ） ---------------- */
const IMAGES = {};   // id -> dataURL
const BLOBS = {};    // id -> {name, dataURL}  (GLB)
const TEXCACHE = new Map(); // id -> THREE.Texture

/* ---------------- レンダラー / シーン ---------------- */
const viewEl = $('#view');
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(devicePixelRatio, 1.75));
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.0;
renderer.outputColorSpace = THREE.SRGBColorSpace;
viewEl.prepend(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color('#05060f');
scene.fog = new THREE.FogExp2('#05060f', 0.012);

const camera = new THREE.PerspectiveCamera(42, 1, 0.1, 1500);
camera.position.set(0, 5.5, 26);
const orbit = new OrbitControls(camera, renderer.domElement);
orbit.target.set(0, 3.2, 0);
orbit.enableDamping = true;
orbit.dampingFactor = 0.08;
orbit.maxPolarAngle = Math.PI * 0.5 - 0.01;
orbit.maxDistance = 220;

const composer = new EffectComposer(renderer);
const renderPass = new RenderPass(scene, camera);
const bloomPass = new UnrealBloomPass(new THREE.Vector2(512, 512), 0.85, 0.55, 0.82);
composer.addPass(renderPass);
composer.addPass(bloomPass);
composer.addPass(new OutputPass());

const pmrem = new THREE.PMREMGenerator(renderer);
scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;

const G = {
  venue: new THREE.Group(), backdrop: new THREE.Group(), stage: new THREE.Group(),
  assets: new THREE.Group(), fx: new THREE.Group(), helpers: new THREE.Group(), lights: new THREE.Group(),
};
G.venue.name = 'VENUE'; G.backdrop.name = 'BACKDROP'; G.stage.name = 'STAGE'; G.assets.name = 'ASSETS';
G.fx.name = 'FX'; G.helpers.name = 'HELPERS'; G.lights.name = 'LIGHTS';
for (const k in G) scene.add(G[k]);

// 基本ライト（会場によって強さが変わる）
const hemi = new THREE.HemisphereLight('#6f7fd0', '#0a0a18', 0.35);
const keyLight = new THREE.DirectionalLight('#dfe6ff', 0.35);
keyLight.position.set(0, 14, 22);
const sun = new THREE.DirectionalLight('#fff3dc', 0);
sun.position.set(-30, 50, 40);
G.lights.add(hemi, keyLight, sun);
// カラーウォッシュ（キューの色に追従する実ライト：数は固定で軽量）
const washLights = [];
for (let i = 0; i < 4; i++) {
  const s = new THREE.SpotLight('#ffffff', 0, 0, 34 * DEG, 0.7, 0);
  s.position.set((i - 1.5) * 6, 12, 14);
  s.target.position.set((i - 1.5) * 2.5, 1.2, 0);
  G.lights.add(s, s.target);
  washLights.push(s);
}
const backLight = new THREE.SpotLight('#8f7fff', 0, 0, 45 * DEG, 0.8, 0);
backLight.position.set(0, 12, -10); backLight.target.position.set(0, 1, 4);
G.lights.add(backLight, backLight.target);

function disposeTree(obj) {
  obj.traverse(o => {
    if (o.geometry && !o.geometry.userData.shared) o.geometry.dispose();
    if (o.userData.exportGeo) o.userData.exportGeo.dispose();
    if (o.material) {
      const ms = Array.isArray(o.material) ? o.material : [o.material];
      for (const m of ms) if (!m.userData.shared) m.dispose();
    }
  });
}
function clearGroup(g) {
  for (const c of [...g.children]) { g.remove(c); disposeTree(c); }
}
// 効果専用（GLB書き出し対象外・クリック対象外）にする
function markFx(o) { o.userData.fx = true; o.traverse(c => { c.userData.fx = true; c.raycast = () => {}; }); return o; }

const statusEl = $('#status');
let statusTimer = 0;
function toast(msg, ms = 2200) {
  statusEl.textContent = msg; statusEl.classList.add('show');
  clearTimeout(statusTimer); statusTimer = setTimeout(() => statusEl.classList.remove('show'), ms);
}
